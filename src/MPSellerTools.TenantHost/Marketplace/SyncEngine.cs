using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace.Channels;

namespace MPSellerTools.TenantHost.Marketplace;

/// <summary>A job just taken from the queue, and the status it was taken from.</summary>
public class ClaimedJob
{
    public Guid Id { get; set; }

    public int PreviousStatus { get; set; }
}

/// <summary>
/// Moves the channels towards what the catalog wants, one job at a time:
/// turns outbox events into jobs, claims a job, carries it out through the
/// channel's adapter, and records what the channel said.
///
/// Delivery is at-least-once: anything here can run twice (a worker can die
/// after the channel acted and before the result was saved), so every step
/// is safe to repeat, and a result is only recorded as the confirmed state
/// when it is for a newer version than the one already confirmed.
/// </summary>
public class SyncEngine(
    TenantDbContext db,
    ListingService listings,
    OrderIngestionService orderIngestion,
    InventoryService inventory,
    IOptions<MarketplaceOptions> options,
    ILogger<SyncEngine> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly MarketplaceOptions _options = options.Value;

    /// <summary>One full pass: dispatch, recover, then run jobs until none is due. Returns how many ran.</summary>
    public async Task<int> RunOnceAsync(string workerId, CancellationToken cancellationToken, int maxJobs = 200)
    {
        await DispatchOutboxAsync(cancellationToken);
        await RecoverExpiredLeasesAsync(cancellationToken);
        var ran = 0;
        while (ran < maxJobs && await RunNextAsync(workerId, cancellationToken))
        {
            ran++;
            // A job can itself raise events (a reconcile restoring a price), so they are picked up as they appear.
            await DispatchOutboxAsync(cancellationToken);
        }
        return ran;
    }

    /// <summary>
    /// Turns undispatched outbox events into jobs. Each event raises the
    /// desired version of the listings it touches; a job already waiting for
    /// that listing and operation has its target raised instead of a second
    /// job being queued, so a burst of changes becomes one send of the latest state.
    /// </summary>
    public async Task<int> DispatchOutboxAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var events = await db.OutboxEvents.Where(e => e.DispatchedAtUtc == null).OrderBy(e => e.Id).Take(500).ToListAsync(cancellationToken);
        if (events.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var accounts = await db.ChannelAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, cancellationToken);
        var markets = await db.ChannelMarkets.AsNoTracking().ToDictionaryAsync(m => m.Id, cancellationToken);
        foreach (var outboxEvent in events)
        {
            var id = outboxEvent.SubjectId;
            var (affected, stream) = outboxEvent.Type switch
            {
                OutboxEvent.ProductContentChanged => (
                    await (from l in db.ChannelListings
                           join v in db.ProductVariants on l.VariantId equals v.Id
                           where v.ProductId == id
                           select l).ToListAsync(cancellationToken),
                    SyncOperation.Content),
                OutboxEvent.VariantContentChanged => (await db.ChannelListings.Where(l => l.VariantId == id).ToListAsync(cancellationToken), SyncOperation.Content),
                OutboxEvent.VariantPriceChanged => (
                    await db.ChannelListings.Where(l => l.VariantId == id && l.PriceOverride == null).ToListAsync(cancellationToken), SyncOperation.Price),
                OutboxEvent.InventoryChanged => (
                    await db.ChannelListings.Where(l => l.VariantId == id && l.FulfillmentMode == FulfillmentMode.Merchant).ToListAsync(cancellationToken),
                    SyncOperation.Inventory),
                OutboxEvent.ListingContentChanged => (await db.ChannelListings.Where(l => l.Id == id).ToListAsync(cancellationToken), SyncOperation.Content),
                OutboxEvent.ListingPriceChanged => (await db.ChannelListings.Where(l => l.Id == id).ToListAsync(cancellationToken), SyncOperation.Price),
                OutboxEvent.ListingInventoryChanged => (await db.ChannelListings.Where(l => l.Id == id).ToListAsync(cancellationToken), SyncOperation.Inventory),
                _ => (new List<ChannelListing>(), SyncOperation.Reconcile),
            };

            foreach (var listing in affected)
            {
                var account = accounts[markets[listing.ChannelMarketId].ChannelAccountId];
                var version = stream switch
                {
                    SyncOperation.Content => ++listing.ContentVersion,
                    SyncOperation.Price => ++listing.PriceVersion,
                    _ => ++listing.InventoryVersion,
                };
                if (JobFor(listing, account, stream) is { } operation)
                {
                    await QueueAsync(account.Id, listing.Id, operation, version, dryRun: false, now, cancellationToken);
                }
            }

            outboxEvent.DispatchedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return events.Count;
    }

    /// <summary>
    /// Which job, if any, a change to one of a listing's streams calls for.
    /// A draft sends nothing. Price and stock travel inside the first content
    /// submission, so they get jobs of their own only once the listing exists
    /// on the channel. Stock is sent only when the account has that turned on
    /// and orders are being accounted for.
    /// </summary>
    private SyncOperation? JobFor(ChannelListing listing, ChannelAccount account, SyncOperation stream)
    {
        if (!account.IsEnabled || listing.DesiredState == ListingDesiredState.Draft)
        {
            return null;
        }

        var existsRemotely = listing.ConfirmedContentVersion > 0;
        if (listing.DesiredState == ListingDesiredState.Inactive)
        {
            return stream == SyncOperation.Content && (existsRemotely || listing.ObservedStatus is not (ListingObservedStatus.Unknown or ListingObservedStatus.NotListed))
                ? SyncOperation.Deactivate
                : null;
        }

        return stream switch
        {
            SyncOperation.Content => SyncOperation.Content,
            SyncOperation.Price => existsRemotely ? SyncOperation.Price : null,
            SyncOperation.Inventory => existsRemotely && InventorySyncAllowed(account) ? SyncOperation.Inventory : null,
            _ => null,
        };
    }

    public bool InventorySyncAllowed(ChannelAccount account) =>
        account.Channel == SalesChannel.Website || (account.InventorySyncEnabled && _options.InventoryAccountingEnabled);

    /// <summary>Adds a waiting job, or raises the target of the one already waiting for the same listing and operation.</summary>
    public async Task<SyncJob> QueueAsync(
        Guid accountId, Guid? listingId, SyncOperation operation, long targetVersion, bool dryRun, DateTime now, CancellationToken cancellationToken)
    {
        var waiting = db.SyncJobs.Local.FirstOrDefault(Matches)
            ?? await db.SyncJobs.FirstOrDefaultAsync(
                j => j.Status == SyncJobStatus.Pending && j.ChannelAccountId == accountId && j.ChannelListingId == listingId
                    && j.Operation == operation && j.DryRun == dryRun,
                cancellationToken);
        if (waiting is not null)
        {
            waiting.TargetVersion = Math.Max(waiting.TargetVersion, targetVersion);
            waiting.UpdatedAtUtc = now;
            return waiting;
        }

        var job = new SyncJob
        {
            Id = Guid.NewGuid(),
            ChannelAccountId = accountId,
            ChannelListingId = listingId,
            Operation = operation,
            Status = SyncJobStatus.Pending,
            TargetVersion = targetVersion,
            Priority = SyncJob.PriorityOf(operation),
            MaxAttempts = _options.MaxAttempts,
            NextAttemptAtUtc = now,
            DryRun = dryRun,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.SyncJobs.Add(job);
        return job;

        bool Matches(SyncJob j) =>
            j.Status == SyncJobStatus.Pending && j.ChannelAccountId == accountId && j.ChannelListingId == listingId
            && j.Operation == operation && j.DryRun == dryRun;
    }

    /// <summary>
    /// Puts jobs whose worker stopped renewing its lease back in the queue.
    /// What that worker got done is unknown, which is why every operation
    /// looks at the channel before creating anything.
    /// </summary>
    public async Task<int> RecoverExpiredLeasesAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var now = DateTime.UtcNow;
        var expired = await db.SyncJobs
            .Where(j => j.Status == SyncJobStatus.Running && j.LeaseExpiresAtUtc < now)
            .ToListAsync(cancellationToken);
        foreach (var job in expired)
        {
            db.SyncAttempts.Add(new SyncAttempt
            {
                Id = Guid.NewGuid(),
                SyncJobId = job.Id,
                Number = job.Attempts,
                StartedAtUtc = job.UpdatedAtUtc,
                FinishedAtUtc = now,
                Outcome = SyncJobStatus.Failed,
                ErrorClass = SyncErrorClass.Transient,
                Detail = $"The worker \"{job.LeaseOwner}\" stopped before finishing; the outcome on the channel is unknown.",
            });
            await RequeueOrSupersedeAsync(job, job.ExternalSubmissionId is null ? SyncJobStatus.Pending : SyncJobStatus.AwaitingRemote, now, cancellationToken);
            job.LeaseOwner = null;
            job.LeaseExpiresAtUtc = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    /// <summary>Claims the most urgent due job (and, where the channel takes batches, more like it) and runs it. False when nothing is due.</summary>
    public async Task<bool> RunNextAsync(string workerId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var now = DateTime.UtcNow;
        var lease = now.AddSeconds(_options.LeaseSeconds);

        // One statement finds and takes the job, so two workers cannot take the same one. A listing
        // with a job already running is passed over: its operations go one at a time, in order.
        var claimed = await db.Database.SqlQuery<ClaimedJob>($"""
            WITH due AS (
                SELECT TOP (1) j.* FROM SyncJobs j WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE j.Status IN (0, 2) AND j.NextAttemptAtUtc <= {now}
                  AND (j.ChannelListingId IS NULL OR NOT EXISTS (
                        SELECT 1 FROM SyncJobs r WHERE r.ChannelListingId = j.ChannelListingId AND r.Status = 1))
                ORDER BY j.Priority DESC, j.NextAttemptAtUtc, j.CreatedAtUtc)
            UPDATE due SET Status = 1, LeaseOwner = {workerId}, LeaseExpiresAtUtc = {lease}, UpdatedAtUtc = {now}
            OUTPUT inserted.Id, deleted.Status AS PreviousStatus
            """).ToListAsync(cancellationToken);
        if (claimed.Count == 0)
        {
            return false;
        }

        var first = await db.SyncJobs.FirstAsync(j => j.Id == claimed[0].Id, cancellationToken);
        var account = await db.ChannelAccounts.FirstAsync(a => a.Id == first.ChannelAccountId, cancellationToken);
        var polling = claimed[0].PreviousStatus == (int)SyncJobStatus.AwaitingRemote;
        var jobs = new List<SyncJob> { first };

        var batchSize = polling || first.DryRun || first.ChannelListingId is null ? 1 : listings.AdapterFor(account).BatchSize(first.Operation);
        if (batchSize > 1)
        {
            var more = batchSize - 1;
            var operation = (int)first.Operation;
            var extra = await db.Database.SqlQuery<ClaimedJob>($"""
                WITH due AS (
                    SELECT TOP ({more}) j.* FROM SyncJobs j WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE j.Status = 0 AND j.NextAttemptAtUtc <= {now} AND j.DryRun = 0
                      AND j.ChannelAccountId = {first.ChannelAccountId} AND j.Operation = {operation} AND j.ChannelListingId IS NOT NULL
                      AND NOT EXISTS (SELECT 1 FROM SyncJobs r WHERE r.ChannelListingId = j.ChannelListingId AND r.Status = 1)
                    ORDER BY j.NextAttemptAtUtc, j.CreatedAtUtc)
                UPDATE due SET Status = 1, LeaseOwner = {workerId}, LeaseExpiresAtUtc = {lease}, UpdatedAtUtc = {now}
                OUTPUT inserted.Id, deleted.Status AS PreviousStatus
                """).ToListAsync(cancellationToken);
            var ids = extra.Select(e => e.Id).ToList();
            jobs.AddRange(await db.SyncJobs.Where(j => ids.Contains(j.Id)).ToListAsync(cancellationToken));
        }

        try
        {
            if (first.Operation == SyncOperation.OrderImport)
            {
                await ImportOrdersAsync(first, account, cancellationToken);
            }
            else
            {
                await ExecuteAsync(jobs, account, polling, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A fault of ours, not the channel's. The lease runs out and the job is taken up again.
            logger.LogError(ex, "Sync job {JobId} ({Operation}) failed unexpectedly", first.Id, first.Operation);
            db.ChangeTracker.Clear();
        }

        return true;
    }

    private async Task ExecuteAsync(List<SyncJob> jobs, ChannelAccount account, bool polling, CancellationToken cancellationToken)
    {
        var adapter = listings.AdapterFor(account);
        var started = DateTime.UtcNow;
        var ready = new List<(SyncJob Job, ListingBundle Bundle)>();
        foreach (var job in jobs)
        {
            var bundle = await listings.LoadAsync(job.ChannelListingId!.Value, cancellationToken);
            if (bundle is null || !account.IsEnabled)
            {
                await FinishAsync(job, null, SyncJobStatus.Cancelled, started, new OperationOutcome(OutcomeKind.Failed, Message: "The listing or its account is gone or disabled."), cancellationToken);
                continue;
            }

            // Checked here as well as when queued: the data may have changed since.
            var problems = job.Operation is SyncOperation.Content && !polling ? listings.Validate(bundle) : [];
            if (problems.Count > 0)
            {
                await FinishAsync(
                    job, bundle, SyncJobStatus.NeedsCorrection, started,
                    OperationOutcome.Failed(SyncErrorClass.DataCorrection, $"{problems.Count} problem(s) to fix before this can be submitted.", problems),
                    cancellationToken);
                continue;
            }

            ready.Add((job, bundle));
        }

        if (ready.Count == 0)
        {
            return;
        }

        var live = account.Channel == SalesChannel.Website || (_options.LiveWritesEnabled && account.LiveWritesEnabled);
        var reads = jobs[0].Operation == SyncOperation.Reconcile;
        if (jobs[0].DryRun || (!live && !reads))
        {
            foreach (var (job, bundle) in ready)
            {
                // The requests are built, to prove the data makes a well-formed submission, and then dropped.
                var requests = adapter.Preview(job.Operation, bundle.Work, bundle.Context);
                var reason = job.DryRun ? "Dry run requested" : "Live writes are switched off";
                await FinishAsync(
                    job, bundle, SyncJobStatus.DryRunCompleted, started,
                    new OperationOutcome(OutcomeKind.Failed, Message: $"{reason}: {requests.Count} request(s) built, nothing sent."), cancellationToken);
            }
            return;
        }

        IReadOnlyList<OperationOutcome> outcomes;
        try
        {
            if (polling || reads)
            {
                var (job, bundle) = ready[0];
                outcomes = [await adapter.GetStatusAsync(bundle.Context, bundle.Work, job.ExternalSubmissionId, cancellationToken)];
            }
            else
            {
                foreach (var (job, _) in ready)
                {
                    job.Attempts++;
                }
                outcomes = await adapter.ExecuteAsync(jobs[0].Operation, ready[0].Bundle.Context, ready.Select(r => r.Bundle.Work).ToList(), cancellationToken);
            }
        }
        catch (ChannelException ex)
        {
            outcomes = ready.Select(_ => OperationOutcome.From(ex)).ToList();
        }

        for (var i = 0; i < ready.Count; i++)
        {
            var (job, bundle) = ready[i];
            await ApplyAsync(job, bundle, account, outcomes[i], polling, started, cancellationToken);
        }
    }

    private async Task ApplyAsync(
        SyncJob job, ListingBundle bundle, ChannelAccount account, OperationOutcome outcome, bool polling, DateTime started, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var work = bundle.Work;
        if (outcome.References is { Count: > 0 } references)
        {
            await SaveReferencesAsync(bundle, references, now, cancellationToken);
        }

        switch (outcome.Kind)
        {
            case OutcomeKind.Confirmed when job.Operation == SyncOperation.Reconcile:
                await ReconcileAsync(bundle, account, outcome.State, now, cancellationToken);
                await FinishAsync(job, bundle, SyncJobStatus.Succeeded, started, outcome, cancellationToken);
                break;

            case OutcomeKind.Confirmed when job.Operation == SyncOperation.Content && outcome.State?.Status == ListingObservedStatus.Rejected:
                await FinishAsync(
                    job, bundle, SyncJobStatus.NeedsCorrection, started,
                    outcome with { ErrorClass = SyncErrorClass.DataCorrection, Message = "The channel turned the listing down." }, cancellationToken, channelRejected: true);
                break;

            case OutcomeKind.Confirmed when polling && !Reached(job.Operation, work, outcome.State):
                // The channel has an answer, but not yet the one this job is waiting for.
                await KeepWaitingAsync(job, bundle, outcome, started, now, cancellationToken);
                break;

            case OutcomeKind.Confirmed:
                await ConfirmAsync(bundle.Listing.Id, job.Operation, work, outcome.State, now, cancellationToken);
                if (outcome.GroupListingId is { } groupListingId)
                {
                    await GroupPublishedAsync(bundle, account, groupListingId, now, cancellationToken);
                }
                Audit(job.Operation, bundle, now);
                await FinishAsync(job, bundle, SyncJobStatus.Succeeded, started, outcome, cancellationToken);
                break;

            case OutcomeKind.Accepted:
                await KeepWaitingAsync(job, bundle, outcome, started, now, cancellationToken);
                break;

            default:
                await FailAsync(job, bundle, account, outcome, started, now, cancellationToken);
                break;
        }
    }

    /// <summary>Whether what the channel reports is what the operation set out to achieve.</summary>
    private static bool Reached(SyncOperation operation, ListingWork work, RemoteState? state) => operation switch
    {
        // Wanted on sale, only "live" will do: a listing the channel shows but will not sell has not got there.
        SyncOperation.Content => work.DesiredState == ListingDesiredState.Active
            ? state?.Status == ListingObservedStatus.Live
            : state?.Status is ListingObservedStatus.Inactive or ListingObservedStatus.NotListed or ListingObservedStatus.Live,
        SyncOperation.Price => state?.Price == work.Snapshot.Price,
        SyncOperation.Inventory => state?.Quantity == work.Snapshot.Quantity,
        SyncOperation.Deactivate => state?.Status is ListingObservedStatus.Inactive or ListingObservedStatus.NotListed || state?.Quantity == 0,
        _ => true,
    };

    /// <summary>
    /// Records a confirmed operation. The version check sits in the UPDATE's
    /// WHERE, so an answer for an older version arriving late (a slow worker,
    /// a retried poll) cannot overwrite what a newer version already confirmed.
    /// </summary>
    public async Task<bool> ConfirmAsync(
        Guid listingId, SyncOperation operation, ListingWork work, RemoteState? state, DateTime now, CancellationToken cancellationToken)
    {
        var issues = state?.Issues is { Count: > 0 } list ? JsonSerializer.Serialize(list, Json) : null;
        var price = state?.Price;
        var quantity = state?.Quantity;
        int updated;
        switch (operation)
        {
            case SyncOperation.Content:
                // The content submission carried the price and quantity of that moment along with it.
                var status = state?.Status ?? ListingObservedStatus.Processing;
                updated = await db.ChannelListings
                    .Where(l => l.Id == listingId && l.ConfirmedContentVersion < work.ContentVersion)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(l => l.ConfirmedContentVersion, work.ContentVersion)
                        .SetProperty(l => l.ConfirmedPriceVersion, l => l.ConfirmedPriceVersion < work.PriceVersion ? work.PriceVersion : l.ConfirmedPriceVersion)
                        .SetProperty(l => l.ConfirmedInventoryVersion, l => l.ConfirmedInventoryVersion < work.InventoryVersion ? work.InventoryVersion : l.ConfirmedInventoryVersion)
                        .SetProperty(l => l.ObservedStatus, status)
                        .SetProperty(l => l.ObservedPrice, l => price != null ? price : l.ObservedPrice)
                        .SetProperty(l => l.ObservedQuantity, l => quantity != null ? quantity : l.ObservedQuantity)
                        .SetProperty(l => l.IssuesJson, issues)
                        .SetProperty(l => l.ObservedAtUtc, now), cancellationToken);
                break;

            case SyncOperation.Price:
                updated = await db.ChannelListings
                    .Where(l => l.Id == listingId && l.ConfirmedPriceVersion < work.PriceVersion)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(l => l.ConfirmedPriceVersion, work.PriceVersion)
                        .SetProperty(l => l.ObservedPrice, l => price != null ? price : l.ObservedPrice)
                        .SetProperty(l => l.HasPriceConflict, false)
                        .SetProperty(l => l.ObservedAtUtc, now), cancellationToken);
                break;

            case SyncOperation.Inventory:
                updated = await db.ChannelListings
                    .Where(l => l.Id == listingId && l.ConfirmedInventoryVersion < work.InventoryVersion)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(l => l.ConfirmedInventoryVersion, work.InventoryVersion)
                        .SetProperty(l => l.ObservedQuantity, l => quantity != null ? quantity : l.ObservedQuantity)
                        .SetProperty(l => l.ObservedAtUtc, now), cancellationToken);
                break;

            default:
                var observed = state?.Status ?? ListingObservedStatus.Inactive;
                updated = await db.ChannelListings
                    .Where(l => l.Id == listingId && l.ConfirmedContentVersion <= work.ContentVersion)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(l => l.ConfirmedContentVersion, work.ContentVersion)
                        .SetProperty(l => l.ObservedStatus, observed)
                        .SetProperty(l => l.ObservedQuantity, l => quantity != null ? quantity : l.ObservedQuantity)
                        .SetProperty(l => l.ObservedAtUtc, now), cancellationToken);
                break;
        }

        return updated > 0;
    }

    private async Task KeepWaitingAsync(SyncJob job, ListingBundle bundle, OperationOutcome outcome, DateTime started, DateTime now, CancellationToken cancellationToken)
    {
        job.ExternalSubmissionId = outcome.SubmissionId ?? job.ExternalSubmissionId;
        if (now - job.CreatedAtUtc > TimeSpan.FromHours(_options.MaxAwaitHours))
        {
            await FinishAsync(
                job, bundle, SyncJobStatus.Failed, started,
                outcome with { ErrorClass = SyncErrorClass.Transient, Message = "The channel gave no final answer in time; the next reconciliation will read the listing's state." },
                cancellationToken);
            return;
        }

        if (job.Operation == SyncOperation.Content)
        {
            // Accepted is not for sale. The listing shows as processing until the channel says otherwise.
            await db.ChannelListings
                .Where(l => l.Id == bundle.Listing.Id && l.ObservedStatus != ListingObservedStatus.Live)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.ObservedStatus, ListingObservedStatus.Processing).SetProperty(l => l.ObservedAtUtc, now), cancellationToken);
        }

        job.NextAttemptAtUtc = now.AddSeconds(_options.PollSeconds);
        await FinishAsync(job, bundle, SyncJobStatus.AwaitingRemote, started, outcome, cancellationToken);
    }

    private async Task FailAsync(
        SyncJob job, ListingBundle bundle, ChannelAccount account, OperationOutcome outcome, DateTime started, DateTime now, CancellationToken cancellationToken)
    {
        switch (outcome.ErrorClass)
        {
            case SyncErrorClass.Transient when job.Attempts < job.MaxAttempts:
                // Back to the queue, to be sent again: whatever was submitted before is void.
                job.ExternalSubmissionId = null;
                job.NextAttemptAtUtc = now + RetryPolicy.Delay(job.Attempts, outcome.RetryAfter, Random.Shared);
                job.ErrorClass = outcome.ErrorClass;
                job.LastError = Truncate(outcome.Message);
                AddAttempt(job, SyncJobStatus.Failed, started, outcome);
                await RequeueOrSupersedeAsync(job, SyncJobStatus.Pending, now, cancellationToken);
                job.LeaseOwner = null;
                job.LeaseExpiresAtUtc = null;
                await db.SaveChangesAsync(cancellationToken);
                break;

            case SyncErrorClass.Authorization:
                // Retrying cannot help until someone fixes the account, so it is said once, on the account.
                account.LastError = Truncate(outcome.Message, 1000);
                account.UpdatedAtUtc = now;
                await FinishAsync(job, bundle, SyncJobStatus.Failed, started, outcome, cancellationToken);
                break;

            case SyncErrorClass.DataCorrection:
                await FinishAsync(
                    job, bundle, SyncJobStatus.NeedsCorrection, started, outcome, cancellationToken, channelRejected: job.Operation == SyncOperation.Content);
                break;

            default:
                await FinishAsync(job, bundle, SyncJobStatus.Failed, started, outcome, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Returns a job to the queue. If a newer job for the same listing and
    /// operation is already waiting, that one will send the current state
    /// anyway, so this one steps aside.
    /// </summary>
    private async Task RequeueOrSupersedeAsync(SyncJob job, SyncJobStatus status, DateTime now, CancellationToken cancellationToken)
    {
        var superseded = status == SyncJobStatus.Pending && await db.SyncJobs.AnyAsync(
            j => j.Id != job.Id && j.Status == SyncJobStatus.Pending && j.ChannelAccountId == job.ChannelAccountId
                && j.ChannelListingId == job.ChannelListingId && j.Operation == job.Operation,
            cancellationToken);
        job.Status = superseded ? SyncJobStatus.Cancelled : status;
        job.CompletedAtUtc = superseded ? now : null;
        job.UpdatedAtUtc = now;
    }

    private async Task FinishAsync(
        SyncJob job, ListingBundle? bundle, SyncJobStatus status, DateTime started, OperationOutcome outcome, CancellationToken cancellationToken,
        bool channelRejected = false)
    {
        var now = DateTime.UtcNow;
        job.Status = status;
        job.ErrorClass = status is SyncJobStatus.Succeeded or SyncJobStatus.AwaitingRemote or SyncJobStatus.DryRunCompleted ? SyncErrorClass.None : outcome.ErrorClass;
        job.LastError = status is SyncJobStatus.Succeeded or SyncJobStatus.AwaitingRemote ? null : Truncate(outcome.Message);
        job.LeaseOwner = null;
        job.LeaseExpiresAtUtc = null;
        job.UpdatedAtUtc = now;
        job.CompletedAtUtc = status is SyncJobStatus.AwaitingRemote ? null : now;
        AddAttempt(job, status, started, outcome);

        if (bundle is not null && status == SyncJobStatus.NeedsCorrection)
        {
            var issues = outcome.State?.Issues is { Count: > 0 } list
                ? list
                : [new ValidationIssue(bundle.Context.Account.Channel, "listing", "rejected", outcome.Message ?? "The channel turned the submission down.")];
            var json = JsonSerializer.Serialize(issues, Json);
            // A problem found here, before sending, leaves the observed status alone: the channel was never asked.
            await db.ChannelListings.Where(l => l.Id == bundle.Listing.Id).ExecuteUpdateAsync(s => s
                .SetProperty(l => l.IssuesJson, json)
                .SetProperty(l => l.ObservedStatus, l => channelRejected ? ListingObservedStatus.Rejected : l.ObservedStatus), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private void AddAttempt(SyncJob job, SyncJobStatus outcomeStatus, DateTime started, OperationOutcome outcome) =>
        db.SyncAttempts.Add(new SyncAttempt
        {
            Id = Guid.NewGuid(),
            SyncJobId = job.Id,
            Number = job.Attempts,
            StartedAtUtc = started,
            FinishedAtUtc = DateTime.UtcNow,
            Outcome = outcomeStatus,
            ErrorClass = outcome.Kind == OutcomeKind.Failed ? outcome.ErrorClass : SyncErrorClass.None,
            HttpStatus = outcome.HttpStatus,
            ExternalRequestId = Truncate(outcome.SubmissionId ?? outcome.RequestId, 128),
            Detail = Truncate(outcome.Message),
        });

    private async Task SaveReferencesAsync(
        ListingBundle bundle, IReadOnlyDictionary<ExternalResourceType, string> references, DateTime now, CancellationToken cancellationToken)
    {
        var existing = await db.ExternalReferences
            .Where(r => r.OwnerType == ExternalOwnerType.ChannelListing && r.OwnerId == bundle.Listing.Id)
            .ToListAsync(cancellationToken);
        foreach (var (type, value) in references)
        {
            var reference = existing.FirstOrDefault(r => r.ResourceType == type);
            if (reference is null)
            {
                db.ExternalReferences.Add(new ExternalReference
                {
                    Id = Guid.NewGuid(),
                    ChannelAccountId = bundle.Context.Account.Id,
                    ChannelMarketId = bundle.Context.Market.Id,
                    OwnerType = ExternalOwnerType.ChannelListing,
                    OwnerId = bundle.Listing.Id,
                    ResourceType = type,
                    Value = value,
                    CreatedAtUtc = now,
                });
            }
            else if (reference.Value != value)
            {
                reference.Value = value;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A group went live as one listing: note its id on the group and have the other members' state read.</summary>
    private async Task GroupPublishedAsync(ListingBundle bundle, ChannelAccount account, string listingId, DateTime now, CancellationToken cancellationToken)
    {
        var membership = await db.ListingGroupMembers.FirstOrDefaultAsync(m => m.ChannelListingId == bundle.Listing.Id, cancellationToken);
        if (membership is null)
        {
            return;
        }

        var reference = await db.ExternalReferences.FirstOrDefaultAsync(
            r => r.OwnerType == ExternalOwnerType.ListingGroup && r.OwnerId == membership.ListingGroupId && r.ResourceType == ExternalResourceType.Listing,
            cancellationToken);
        if (reference is null)
        {
            db.ExternalReferences.Add(new ExternalReference
            {
                Id = Guid.NewGuid(),
                ChannelAccountId = account.Id,
                ChannelMarketId = bundle.Context.Market.Id,
                OwnerType = ExternalOwnerType.ListingGroup,
                OwnerId = membership.ListingGroupId,
                ResourceType = ExternalResourceType.Listing,
                Value = listingId,
                CreatedAtUtc = now,
            });
        }
        else
        {
            reference.Value = listingId;
        }

        var siblings = await db.ListingGroupMembers
            .Where(m => m.ListingGroupId == membership.ListingGroupId && m.ChannelListingId != bundle.Listing.Id)
            .Select(m => m.ChannelListingId)
            .ToListAsync(cancellationToken);
        foreach (var sibling in siblings)
        {
            await QueueAsync(account.Id, sibling, SyncOperation.Reconcile, 0, dryRun: false, now, cancellationToken);
        }
    }

    /// <summary>
    /// Compares what the channel reports with what is wanted. A price that
    /// differs while nothing is on its way there was changed on the channel's
    /// side, and the account's policy says what happens next. Each policy
    /// acts once per difference, so the two sides cannot keep overwriting each other.
    /// </summary>
    private async Task ReconcileAsync(ListingBundle bundle, ChannelAccount account, RemoteState? state, DateTime now, CancellationToken cancellationToken)
    {
        if (state is null)
        {
            return;
        }

        var listing = await db.ChannelListings.FirstAsync(l => l.Id == bundle.Listing.Id, cancellationToken);
        listing.ObservedStatus = state.Status;
        listing.ObservedPrice = state.Price ?? listing.ObservedPrice;
        listing.ObservedQuantity = state.Quantity ?? listing.ObservedQuantity;
        listing.ObservedAtUtc = now;
        if (state.Issues is { Count: > 0 } issues)
        {
            listing.IssuesJson = JsonSerializer.Serialize(issues, Json);
        }

        var wanted = bundle.Work.Snapshot.Price;
        var settled = listing.ConfirmedPriceVersion >= listing.PriceVersion;
        if (state.Price is { } remote && remote != wanted && settled && listing.DesiredState == ListingDesiredState.Active)
        {
            switch (account.PriceConflictPolicy)
            {
                case PriceConflictPolicy.RestoreLocal:
                    db.OutboxEvents.Add(new OutboxEvent { Type = OutboxEvent.ListingPriceChanged, SubjectId = listing.Id, CreatedAtUtc = now });
                    AddAudit("ChannelPriceRestored", $"listing={listing.SellerSku}; channel={account.Channel}; remote={remote}; local={wanted}", now);
                    break;

                case PriceConflictPolicy.ImportRemote:
                    // The channel's price becomes this listing's price here. Nothing is sent, as the channel already has it.
                    listing.PriceOverride = remote;
                    listing.PriceVersion++;
                    listing.ConfirmedPriceVersion = listing.PriceVersion;
                    AddAudit("ChannelPriceImported", $"listing={listing.SellerSku}; channel={account.Channel}; price={remote}", now);
                    break;

                default:
                    listing.HasPriceConflict = true;
                    break;
            }
        }
        else if (state.Price == wanted)
        {
            listing.HasPriceConflict = false;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Queues the work that recurs: order imports, reconciliation of listings
    /// not read for a while, and the release of holds that ran out.
    /// </summary>
    public async Task ScheduleRecurringAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var now = DateTime.UtcNow;
        var importDue = now.AddMinutes(-_options.OrderImportMinutes);
        var accounts = await db.ChannelAccounts.AsNoTracking()
            .Where(a => a.IsEnabled && a.OrderImportEnabled && a.Channel != SalesChannel.Website
                && (a.LastOrderImportAtUtc == null || a.LastOrderImportAtUtc < importDue))
            .ToListAsync(cancellationToken);
        foreach (var account in accounts)
        {
            var busy = await db.SyncJobs.AnyAsync(
                j => j.ChannelAccountId == account.Id && j.Operation == SyncOperation.OrderImport
                    && (j.Status == SyncJobStatus.Pending || j.Status == SyncJobStatus.Running),
                cancellationToken);
            if (!busy)
            {
                await QueueAsync(account.Id, null, SyncOperation.OrderImport, 0, dryRun: false, now, cancellationToken);
            }
        }

        var reconcileDue = now.AddMinutes(-_options.ReconcileMinutes);
        var stale = await (
            from listing in db.ChannelListings.AsNoTracking()
            join market in db.ChannelMarkets.AsNoTracking() on listing.ChannelMarketId equals market.Id
            join account in db.ChannelAccounts.AsNoTracking() on market.ChannelAccountId equals account.Id
            where account.IsEnabled && account.LiveWritesEnabled && account.Channel != SalesChannel.Website
                && listing.DesiredState == ListingDesiredState.Active && listing.ConfirmedContentVersion > 0
                && (listing.ObservedAtUtc == null || listing.ObservedAtUtc < reconcileDue)
            orderby listing.ObservedAtUtc
            select new { listing.Id, AccountId = account.Id }).Take(50).ToListAsync(cancellationToken);
        if (_options.LiveWritesEnabled)
        {
            foreach (var item in stale)
            {
                await QueueAsync(item.AccountId, item.Id, SyncOperation.Reconcile, 0, dryRun: false, now, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await inventory.ExpireAsync(cancellationToken);
    }

    private async Task ImportOrdersAsync(SyncJob job, ChannelAccount account, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var adapter = listings.AdapterFor(account);
        var market = await db.ChannelMarkets.AsNoTracking().FirstOrDefaultAsync(m => m.ChannelAccountId == account.Id, cancellationToken);
        if (market is null || job.DryRun)
        {
            await FinishAsync(job, null, market is null ? SyncJobStatus.Failed : SyncJobStatus.DryRunCompleted, started,
                new OperationOutcome(OutcomeKind.Failed, SyncErrorClass.Permanent, market is null ? "The account has no marketplace." : "Dry run: no orders were read."),
                cancellationToken);
            return;
        }

        job.Attempts++;
        // Reach back a little further than the last import, so a change the channel recorded late is not missed.
        var since = account.LastOrderImportAtUtc is { } last ? last.AddHours(-1) : started.AddDays(-30);
        IReadOnlyList<ChannelOrder> orders;
        try
        {
            orders = await adapter.GetOrdersAsync(ListingService.ContextFor(account, market), since, cancellationToken);
        }
        catch (ChannelException ex)
        {
            // The job and account were read before the adapter ran; they are still tracked.
            await FailOrderImportAsync(job, account, OperationOutcome.From(ex), started, cancellationToken);
            return;
        }

        int created = 0, updated = 0, issues = 0;
        foreach (var order in orders)
        {
            var result = await orderIngestion.IngestAsync(account, account.Channel, order, UnknownSkuPolicy.RouteForResolution, cancellationToken);
            created += result.Outcome == IngestOutcome.Created ? 1 : 0;
            updated += result.Outcome == IngestOutcome.Updated ? 1 : 0;
            issues += result.Issues;
        }

        // Ingestion clears nothing it does not own, but the entities are fetched again to be safe after its transactions.
        var storedAccount = await db.ChannelAccounts.FirstAsync(a => a.Id == account.Id, cancellationToken);
        var storedJob = await db.SyncJobs.FirstAsync(j => j.Id == job.Id, cancellationToken);
        storedAccount.LastOrderImportAtUtc = started;
        storedAccount.LastError = null;
        AddAudit("ChannelOrdersImported", $"channel={account.Channel}; account={account.Name}; created={created}; updated={updated}; issues={issues}", started);
        await FinishAsync(
            storedJob, null, SyncJobStatus.Succeeded, started,
            new OperationOutcome(OutcomeKind.Confirmed, Message: $"created={created}; updated={updated}; issues={issues}"), cancellationToken);
    }

    private async Task FailOrderImportAsync(SyncJob job, ChannelAccount account, OperationOutcome outcome, DateTime started, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (outcome.ErrorClass == SyncErrorClass.Transient && job.Attempts < job.MaxAttempts)
        {
            job.NextAttemptAtUtc = now + RetryPolicy.Delay(job.Attempts, outcome.RetryAfter, Random.Shared);
            job.ErrorClass = outcome.ErrorClass;
            job.LastError = Truncate(outcome.Message);
            job.LeaseOwner = null;
            job.LeaseExpiresAtUtc = null;
            AddAttempt(job, SyncJobStatus.Failed, started, outcome);
            await RequeueOrSupersedeAsync(job, SyncJobStatus.Pending, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        account.LastError = Truncate(outcome.Message, 1000);
        await FinishAsync(job, null, SyncJobStatus.Failed, started, outcome, cancellationToken);
    }

    private void Audit(SyncOperation operation, ListingBundle bundle, DateTime now)
    {
        var s = bundle.Work.Snapshot;
        var (action, detail) = operation switch
        {
            SyncOperation.Price => ("ChannelPriceUpdated", $"price={s.Price} {s.Currency}; version={bundle.Work.PriceVersion}"),
            SyncOperation.Inventory => ("ChannelInventoryUpdated", $"quantity={s.Quantity}; version={bundle.Work.InventoryVersion}"),
            SyncOperation.Deactivate => ("ChannelListingDeactivated", $"version={bundle.Work.ContentVersion}"),
            _ => ("ChannelListingSubmitted", $"version={bundle.Work.ContentVersion}"),
        };
        AddAudit(action, $"channel={s.Channel}; market={s.MarketplaceCode}; sku={s.SellerSku}; {detail}", now);
    }

    private void AddAudit(string action, string details, DateTime now) =>
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = now,
            ActorUserId = Guid.Empty,
            ActorEmail = "system",
            Action = action,
            Details = details,
        });

    private static string? Truncate(string? text, int length = 2000) => text is { Length: > 0 } && text.Length > length ? text[..length] : text;
}
