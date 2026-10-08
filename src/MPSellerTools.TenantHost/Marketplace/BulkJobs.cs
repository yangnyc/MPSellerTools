using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace.Channels;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Marketplace;

/// <summary>An item a bulk job could not do, and why.</summary>
public record BulkJobError(string Item, string Message);

/// <summary>
/// Carries out bulk jobs: one at a time, oldest first, a batch of items at a
/// go, writing its progress after each batch so the page can show it and a
/// request to stop is noticed. A job works out what is left to do from the
/// listings as they are, so one that was interrupted simply carries on.
/// </summary>
public class BulkJobRunner(
    TenantDbContext db, ListingService listings, MagentoSync magento, EbaySync ebay, ILogger<BulkJobRunner> logger)
{
    /// <summary>How many items are read, done and written together.</summary>
    private const int BatchSize = 100;

    /// <summary>How many of a job's failures are kept with it; the rest are only counted.</summary>
    private const int MaxErrorsKept = 100;

    /// <summary>A running job that has not reported for this long was interrupted, by a restart say.</summary>
    private static readonly TimeSpan Abandoned = TimeSpan.FromMinutes(2);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static List<BulkJobError> ParseErrors(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<BulkJobError>>(json, Json) ?? [];

    /// <summary>Runs the next job waiting, if there is one. Returns whether it ran one.</summary>
    public async Task<bool> RunNextAsync(CancellationToken cancellationToken)
    {
        var quietSince = DateTime.UtcNow - Abandoned;
        var job = await db.BulkJobs
            .Where(j => j.Status == BulkJobStatus.Queued
                || (j.Status == BulkJobStatus.Running && (j.HeartbeatAtUtc == null || j.HeartbeatAtUtc < quietSince)))
            .OrderBy(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        job.Status = BulkJobStatus.Running;
        job.StartedAtUtc ??= now;
        job.HeartbeatAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var account = await db.ChannelAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == job.ChannelAccountId, cancellationToken);
            if (account is null)
            {
                await FinishAsync(job, BulkJobStatus.Failed, null, "The sales channel account no longer exists.", cancellationToken);
            }
            else if (job.Type == BulkJobType.ReadStore)
            {
                await ReadStoreAsync(job, account, cancellationToken);
            }
            else
            {
                await WorkThroughListingsAsync(job, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping. The job stays as running and is carried on by whichever host starts next.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bulk job {JobId} ({Type}) failed", job.Id, job.Type);
            db.ChangeTracker.Clear();
            var failed = await db.BulkJobs.FirstAsync(j => j.Id == job.Id, CancellationToken.None);
            // A channel's own refusal is safe to show; anything else is said plainly without its internals.
            var reason = ex is ChannelException or EbayApiException ? ex.Message : "Something went wrong while this job was running. It can be run again.";
            await FinishAsync(failed, BulkJobStatus.Failed, null, reason, CancellationToken.None);
        }
        return true;
    }

    /// <summary>The listings of the account a job of this type works on, in the order it takes them.</summary>
    private IQueryable<Guid> Scope(BulkJob job)
    {
        var ofAccount =
            from listing in db.ChannelListings.AsNoTracking()
            join market in db.ChannelMarkets.AsNoTracking() on listing.ChannelMarketId equals market.Id
            where market.ChannelAccountId == job.ChannelAccountId
            select listing;
        ofAccount = job.Type switch
        {
            BulkJobType.PublishDrafts => ofAccount.Where(l => l.DesiredState == ListingDesiredState.Draft),
            BulkJobType.TakeOffSale => ofAccount.Where(l => l.DesiredState == ListingDesiredState.Active),
            BulkJobType.SendAgain => ofAccount.Where(l => l.DesiredState == ListingDesiredState.Active
                && (l.ObservedStatus == ListingObservedStatus.Unknown || l.ObservedStatus == ListingObservedStatus.NotListed
                    || l.ObservedStatus == ListingObservedStatus.Rejected)),
            _ => ofAccount,
        };
        return ofAccount.OrderBy(l => l.SellerSku).Select(l => l.Id);
    }

    private async Task WorkThroughListingsAsync(BulkJob job, CancellationToken cancellationToken)
    {
        // Taken once: publishing a draft takes it out of "the drafts", so the list would shift under a job that asked again.
        var ids = await Scope(job).ToListAsync(cancellationToken);
        // Carried on after an interruption, what was already done is no longer in scope; the counts go on from where they were.
        job.Total = job.Processed + ids.Count;
        var errors = ParseErrors(job.ErrorsJson);

        foreach (var batch in ids.Chunk(BatchSize))
        {
            if (await db.BulkJobs.AsNoTracking().Where(j => j.Id == job.Id).Select(j => j.CancelRequested).FirstAsync(cancellationToken))
            {
                await FinishAsync(job, BulkJobStatus.Cancelled, $"Stopped after {job.Processed} of {job.Total}.", null, cancellationToken);
                return;
            }

            var bundles = (await listings.LoadManyAsync(batch, cancellationToken)).ToDictionary(b => b.Listing.Id);
            var tracked = await db.ChannelListings.Where(l => EF.Parameter(batch).Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);
            var now = DateTime.UtcNow;
            foreach (var id in batch)
            {
                job.Processed++;
                // Removed since the job began: nothing to do, and nothing wrong.
                if (!bundles.TryGetValue(id, out var bundle) || !tracked.TryGetValue(id, out var listing))
                {
                    job.Succeeded++;
                    continue;
                }

                var problem = Apply(job.Type, listing, bundle, now);
                if (problem is null)
                {
                    job.Succeeded++;
                }
                else
                {
                    job.Failed++;
                    if (errors.Count < MaxErrorsKept)
                    {
                        errors.Add(new BulkJobError(listing.SellerSku, problem.Length > 500 ? problem[..500] : problem));
                    }
                }
            }

            job.ErrorsJson = errors.Count == 0 ? null : JsonSerializer.Serialize(errors, Json);
            job.HeartbeatAtUtc = DateTime.UtcNow;
            // The listings, the events that send them on, and the job's progress, together.
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            db.Attach(job);
        }

        var what = job.Type switch
        {
            BulkJobType.PublishDrafts => "queued for publishing",
            BulkJobType.TakeOffSale => "queued to come off sale",
            BulkJobType.SendAgain => "queued to be sent again",
            _ => "ready to publish",
        };
        var summary = job.Total == 0
            ? "There was nothing to do."
            : job.Failed == 0 ? $"{job.Succeeded} {what}." : $"{job.Succeeded} {what}; {job.Failed} held back.";
        await FinishAsync(job, job.Failed == 0 ? BulkJobStatus.Succeeded : BulkJobStatus.CompletedWithErrors, summary, null, cancellationToken);
    }

    /// <summary>Does the job's work for one listing. Returns why it could not, or null when it did.</summary>
    private string? Apply(BulkJobType type, ChannelListing listing, ListingBundle bundle, DateTime now)
    {
        switch (type)
        {
            case BulkJobType.PublishDrafts:
            case BulkJobType.CheckListings:
                // Checked as it would be sent: on sale.
                var issues = listings.Validate(bundle with { Work = bundle.Work with { DesiredState = ListingDesiredState.Active } });
                listing.IssuesJson = issues.Count == 0 ? null : JsonSerializer.Serialize(issues, Json);
                if (issues.Count > 0)
                {
                    return string.Join(" ", issues.Select(i => i.Message));
                }
                if (type == BulkJobType.PublishDrafts)
                {
                    SetWanted(listing, ListingDesiredState.Active, now);
                }
                return null;

            case BulkJobType.TakeOffSale:
                SetWanted(listing, ListingDesiredState.Inactive, now);
                return null;

            case BulkJobType.SendAgain:
                db.OutboxEvents.Add(new OutboxEvent { Type = OutboxEvent.ListingContentChanged, SubjectId = listing.Id, CreatedAtUtc = now });
                return null;

            default:
                return $"Nothing is done to a listing by a {type} job.";
        }
    }

    /// <summary>The same change the listing's own Publish and Take off sale buttons make: the sync queue carries it out.</summary>
    private void SetWanted(ChannelListing listing, ListingDesiredState state, DateTime now)
    {
        listing.DesiredState = state;
        listing.UpdatedAtUtc = now;
        db.OutboxEvents.Add(new OutboxEvent { Type = OutboxEvent.ListingContentChanged, SubjectId = listing.Id, CreatedAtUtc = now });
    }

    private async Task ReadStoreAsync(BulkJob job, ChannelAccount account, CancellationToken cancellationToken)
    {
        string summary;
        switch (account.Channel)
        {
            case SalesChannel.Magento:
                var store = await magento.ImportListingsAsync(account, cancellationToken);
                summary = $"{store.Listings} product(s) read from the store, {store.Created} new to the catalog.";
                break;

            case SalesChannel.Ebay:
                var connection = await db.EbayConnections.FirstOrDefaultAsync(cancellationToken)
                    ?? throw new EbayApiException("Connect the eBay account first.");
                var read = await ebay.ImportProductsAsync(connection, cancellationToken);
                summary = $"{read.Listings} listing(s) read from eBay, {read.Created} product(s) new to the catalog, {read.Updated} updated."
                    + (read.Warning is null ? "" : $" {read.Warning}");
                break;

            default:
                await FinishAsync(job, BulkJobStatus.Failed, null, $"{account.Channel} reports its listings through the sync queue; there is nothing to read on request.", cancellationToken);
                return;
        }

        // The reads above save their own work; the job is looked up again in case its row moved on meanwhile.
        db.ChangeTracker.Clear();
        var finished = await db.BulkJobs.FirstAsync(j => j.Id == job.Id, cancellationToken);
        finished.Processed = finished.Succeeded = finished.Total = 1;
        await FinishAsync(finished, BulkJobStatus.Succeeded, summary, null, cancellationToken);
    }

    private async Task FinishAsync(BulkJob job, BulkJobStatus status, string? summary, string? error, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        job.Status = status;
        job.Summary = summary is { Length: > 500 } ? summary[..500] : summary;
        job.LastError = error is { Length: > 1000 } ? error[..1000] : error;
        job.FinishedAtUtc = now;
        job.HeartbeatAtUtc = now;
        // In the audit trail under whoever asked for it, as the work was theirs.
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = now,
            ActorUserId = job.CreatedByUserId,
            ActorEmail = job.CreatedByEmail,
            Action = "BulkJobFinished",
            Details = $"type={job.Type}; status={status}; succeeded={job.Succeeded}; failed={job.Failed}; total={job.Total}",
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Runs waiting bulk jobs inside this tenant's own host, one after another.
/// The jobs are rows in the tenant's database, so nothing is lost on a
/// restart: a job that was running is taken up again.
/// </summary>
public class BulkJobWorker(IServiceScopeFactory scopes, IOptions<MarketplaceOptions> options, ILogger<BulkJobWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var ran = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var database = scope.ServiceProvider.GetRequiredService<TenantDbContext>().Database;
                // Started against a schema from before bulk jobs existed, it stands by rather than fail every pass.
                if (!(await database.GetPendingMigrationsAsync(stoppingToken)).Any())
                {
                    ran = await scope.ServiceProvider.GetRequiredService<BulkJobRunner>().RunNextAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // One bad pass must not end the worker; the next starts clean.
                logger.LogError(ex, "Bulk job pass failed");
            }

            // Straight on to the next job when there was one; otherwise a short rest.
            if (!ran)
            {
                try
                {
                    await Task.Delay(Idle, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
