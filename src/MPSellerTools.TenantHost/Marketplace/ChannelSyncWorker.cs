using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Infrastructure.Tenants;

namespace MPSellerTools.TenantHost.Marketplace;

/// <summary>
/// Runs the <see cref="SyncEngine"/> on a timer inside this tenant's own
/// host. The queue lives in the tenant's database, so the worker needs no
/// broker and stays inside the one-process-one-database boundary. Editing
/// requests never wait for it: they only write outbox rows.
/// </summary>
public class ChannelSyncWorker(IServiceScopeFactory scopes, IOptions<MarketplaceOptions> options, ILogger<ChannelSyncWorker> logger)
    : BackgroundService
{
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.AutoBackfill && !settings.WorkerEnabled)
        {
            return;
        }

        // This host does not migrate its own database (the provisioning worker does). Started
        // against a schema from before the multichannel catalog, it stands by rather than fail every pass.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, settings.WorkerIntervalSeconds)));
        var warned = false;
        while (!await SchemaReadyAsync(stoppingToken))
        {
            if (!warned)
            {
                logger.LogWarning("The tenant database has pending migrations; channel sync is standing by until they are applied.");
                warned = true;
            }
            if (!await WaitAsync(timer, stoppingToken))
            {
                return;
            }
        }

        if (settings.AutoBackfill)
        {
            await RunAsync(async services =>
            {
                var report = await services.GetRequiredService<CatalogBackfill>().RunAsync(dryRun: false, stoppingToken);
                if (report.VariantsCreated > 0 || report.Conflicts.Count > 0)
                {
                    logger.LogInformation(
                        "Catalog backfill: {Variants} default variant(s) created, {Lines} order line(s) linked, {Conflicts} conflict(s)",
                        report.VariantsCreated, report.OrderLinesLinked, report.Conflicts.Count);
                }
            });
        }

        if (!settings.WorkerEnabled)
        {
            return;
        }

        do
        {
            await RunAsync(async services =>
            {
                var engine = services.GetRequiredService<SyncEngine>();
                await engine.ScheduleRecurringAsync(stoppingToken);
                await engine.RunOnceAsync(_workerId, stoppingToken);
            });
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private async Task<bool> SchemaReadyAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<TenantDbContext>().Database;
            return !(await database.GetPendingMigrationsAsync(stoppingToken)).Any();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private async Task RunAsync(Func<IServiceProvider, Task> work)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await work(scope.ServiceProvider);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // One bad pass must not end the worker; the next tick starts clean.
            logger.LogError(ex, "Channel sync pass failed");
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

public record BackfillConflict(Guid ProductId, string Sku, string Reason);

/// <summary>
/// What a backfill run did, or with <c>DryRun</c> what it would do.
/// <see cref="Remaining"/> is how many products are still without a variant
/// afterwards: zero, or the number of conflicts.
/// </summary>
public record BackfillReport(
    bool DryRun, int ProductsExamined, int VariantsCreated, int OrderLinesLinked, int Remaining, IReadOnlyList<BackfillConflict> Conflicts);

/// <summary>
/// Gives every product from before variants existed its default variant and
/// warehouse balance, and points old order lines at that variant.
///
/// It can be run any number of times: it only ever looks at products that
/// still have no default variant, in batches, each batch its own
/// transaction, so stopping part-way loses nothing and the next run carries
/// on. It publishes nothing and invents no external ids. A product edited
/// while this runs gets its variant from the save itself (see
/// <see cref="CatalogSaveChangesInterceptor"/>); the unique index on the
/// default variant makes whichever comes second a no-op.
/// </summary>
public class CatalogBackfill(TenantDbContext db)
{
    private const int BatchSize = 200;

    public async Task<BackfillReport> RunAsync(bool dryRun, CancellationToken cancellationToken)
    {
        var conflicts = new List<BackfillConflict>();
        int examined = 0, created = 0;
        var after = Guid.Empty;
        while (true)
        {
            db.ChangeTracker.Clear();
            // Keyset paging on the id is the checkpoint: a conflict is passed over, not retried forever.
            var batch = await db.Products
                .Where(p => p.Id.CompareTo(after) > 0 && !db.ProductVariants.Any(v => v.ProductId == p.Id && v.IsDefault))
                .OrderBy(p => p.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            after = batch[^1].Id;
            examined += batch.Count;
            var skus = batch.Select(p => p.Sku).ToList();
            var taken = await db.ProductVariants.AsNoTracking().Where(v => skus.Contains(v.Sku)).Select(v => v.Sku).ToListAsync(cancellationToken);
            var now = DateTime.UtcNow;
            foreach (var product in batch)
            {
                if (taken.Contains(product.Sku))
                {
                    conflicts.Add(new BackfillConflict(product.Id, product.Sku, "Another product's variant already uses this SKU."));
                    continue;
                }

                created++;
                if (!dryRun)
                {
                    var variant = CatalogSaveChangesInterceptor.NewDefaultVariant(product, now);
                    db.ProductVariants.Add(variant);
                    db.InventoryBalances.Add(CatalogSaveChangesInterceptor.NewBalance(variant.Id, product.StockQuantity, now));
                }
            }

            if (!dryRun)
            {
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    // Something else gave one of these products its variant in the meantime. Start over:
                    // products already done are no longer selected, so nothing is repeated.
                    (examined, created, after) = (0, 0, Guid.Empty);
                    conflicts.Clear();
                    continue;
                }
            }
        }

        var linkable = await db.OrderItems.CountAsync(
            i => i.VariantId == null && db.ProductVariants.Any(v => v.ProductId == i.ProductId && v.IsDefault), cancellationToken);
        if (!dryRun && linkable > 0)
        {
            linkable = await db.Database.ExecuteSqlAsync($"""
                UPDATE i SET VariantId = v.Id
                FROM OrderItems i JOIN ProductVariants v ON v.ProductId = i.ProductId AND v.IsDefault = 1
                WHERE i.VariantId IS NULL
                """, cancellationToken);
        }

        var remaining = await db.Products.CountAsync(p => !db.ProductVariants.Any(v => v.ProductId == p.Id && v.IsDefault), cancellationToken);
        return new BackfillReport(dryRun, examined, created, linkable, dryRun ? remaining - created : remaining, conflicts);
    }
}

/// <summary>Numbers that say whether the channels are keeping up.</summary>
public record SyncHealth(
    int UndispatchedEvents,
    int PendingJobs,
    int RunningJobs,
    int AwaitingRemoteJobs,
    int FailedJobs,
    int NeedsCorrectionJobs,
    int ExpiredLeases,
    int RetriedJobs,
    DateTime? OldestPendingAtUtc,
    DateTime? LastSuccessAtUtc,
    IReadOnlyList<AccountHealth> Accounts);

/// <summary><see cref="OrdersStale"/>: orders have not been read recently enough for stock sent to this channel to be trusted.</summary>
public record AccountHealth(
    Guid Id, SalesChannel Channel, string Name, bool LiveWrites, DateTime? LastOrderImportAtUtc, bool OrdersStale, string? LastError, int OpenOrderIssues);

public class SyncHealthReader(TenantDbContext db, IOptions<MarketplaceOptions> options)
{
    public async Task<SyncHealth> ReadAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var staleBefore = now.AddMinutes(-options.Value.OrderStaleMinutes);
        var counts = await db.SyncJobs.GroupBy(j => j.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var issues = await db.OrderLineIssues.Where(i => i.ResolvedAtUtc == null && i.ChannelAccountId != null)
            .GroupBy(i => i.ChannelAccountId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var accounts = await db.ChannelAccounts.AsNoTracking().OrderBy(a => a.Channel).ThenBy(a => a.Name).ToListAsync(cancellationToken);

        return new SyncHealth(
            await db.OutboxEvents.CountAsync(e => e.DispatchedAtUtc == null, cancellationToken),
            counts.GetValueOrDefault(SyncJobStatus.Pending),
            counts.GetValueOrDefault(SyncJobStatus.Running),
            counts.GetValueOrDefault(SyncJobStatus.AwaitingRemote),
            counts.GetValueOrDefault(SyncJobStatus.Failed),
            counts.GetValueOrDefault(SyncJobStatus.NeedsCorrection),
            await db.SyncJobs.CountAsync(j => j.Status == SyncJobStatus.Running && j.LeaseExpiresAtUtc < now, cancellationToken),
            await db.SyncJobs.CountAsync(j => j.Attempts > 1 && (j.Status == SyncJobStatus.Pending || j.Status == SyncJobStatus.Running), cancellationToken),
            await db.SyncJobs.Where(j => j.Status == SyncJobStatus.Pending).MinAsync(j => (DateTime?)j.CreatedAtUtc, cancellationToken),
            await db.SyncJobs.Where(j => j.Status == SyncJobStatus.Succeeded).MaxAsync(j => j.CompletedAtUtc, cancellationToken),
            accounts.Select(a => new AccountHealth(
                a.Id, a.Channel, a.Name, a.LiveWritesEnabled && options.Value.LiveWritesEnabled, a.LastOrderImportAtUtc,
                a.Channel != SalesChannel.Website && a.InventorySyncEnabled && (a.LastOrderImportAtUtc is null || a.LastOrderImportAtUtc < staleBefore),
                a.LastError, issues.GetValueOrDefault(a.Id))).ToList());
    }
}
