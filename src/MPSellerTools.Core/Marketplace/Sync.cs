using System.ComponentModel.DataAnnotations;

namespace MPSellerTools.Core.Marketplace;

/// <summary>
/// A fact about a catalog, listing or stock change, written in the same
/// transaction as the change itself. The sync worker turns these into
/// <see cref="SyncJob"/>s; no channel is ever called while the change is saved.
/// </summary>
public class OutboxEvent
{
    public const string ProductContentChanged = "ProductContentChanged";
    public const string VariantContentChanged = "VariantContentChanged";
    public const string VariantPriceChanged = "VariantPriceChanged";
    public const string InventoryChanged = "InventoryChanged";
    public const string ListingContentChanged = "ListingContentChanged";
    public const string ListingPriceChanged = "ListingPriceChanged";
    public const string ListingInventoryChanged = "ListingInventoryChanged";

    /// <summary>Database-assigned and increasing, so events are handled in the order they were written.</summary>
    public long Id { get; set; }

    [MaxLength(64)]
    public required string Type { get; set; }

    /// <summary>The product, variant or listing the event is about, according to <see cref="Type"/>.</summary>
    public Guid SubjectId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? DispatchedAtUtc { get; set; }
}

public enum SyncOperation
{
    /// <summary>Create or update the listing's content and make it match the desired state.</summary>
    Content,
    Price,
    Inventory,
    Deactivate,
    OrderImport,

    /// <summary>Read the channel's state and compare it with what is wanted.</summary>
    Reconcile,
}

public enum SyncJobStatus
{
    Pending,
    Running,

    /// <summary>The channel accepted the submission; its final answer is still to be fetched.</summary>
    AwaitingRemote,
    Succeeded,

    /// <summary>Gave up: a permanent refusal, an authorization problem, or too many attempts.</summary>
    Failed,

    /// <summary>The data has to be fixed before another attempt makes sense.</summary>
    NeedsCorrection,

    /// <summary>Built and validated but sent nowhere. Not a successful channel operation.</summary>
    DryRunCompleted,
    Cancelled,
}

public enum SyncErrorClass
{
    None,

    /// <summary>Worth another attempt: timeouts, throttling, the channel's own failures.</summary>
    Transient,
    Authorization,
    DataCorrection,
    Permanent,
}

/// <summary>
/// One operation against a channel. It carries no payload: when it runs it
/// sends the listing's current desired state, so jobs queued for older
/// states collapse into the newest one.
/// </summary>
public class SyncJob
{
    public Guid Id { get; set; }

    public Guid ChannelAccountId { get; set; }

    /// <summary>Null for account-wide work such as an order import.</summary>
    public Guid? ChannelListingId { get; set; }

    public SyncOperation Operation { get; set; }

    public SyncJobStatus Status { get; set; }

    /// <summary>The desired version this job was last asked to bring the channel to.</summary>
    public long TargetVersion { get; set; }

    /// <summary>Higher runs first. Inventory outranks price, which outranks content.</summary>
    public int Priority { get; set; }

    public int Attempts { get; set; }

    public int MaxAttempts { get; set; }

    public DateTime NextAttemptAtUtc { get; set; }

    [MaxLength(64)]
    public string? LeaseOwner { get; set; }

    /// <summary>A running job whose lease has passed is taken to belong to a worker that died.</summary>
    public DateTime? LeaseExpiresAtUtc { get; set; }

    /// <summary>The channel's id for an accepted submission or feed, used to fetch its outcome.</summary>
    [MaxLength(128)]
    public string? ExternalSubmissionId { get; set; }

    public SyncErrorClass ErrorClass { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    public bool DryRun { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public static int PriorityOf(SyncOperation operation) => operation switch
    {
        SyncOperation.Inventory => 100,
        SyncOperation.Deactivate => 80,
        SyncOperation.OrderImport => 60,
        SyncOperation.Price => 50,
        SyncOperation.Content => 10,
        _ => 1,
    };
}

/// <summary>One try at a job and how it ended. Holds no credentials and no buyer data.</summary>
public class SyncAttempt
{
    public Guid Id { get; set; }

    public Guid SyncJobId { get; set; }

    public int Number { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime FinishedAtUtc { get; set; }

    public SyncJobStatus Outcome { get; set; }

    public SyncErrorClass ErrorClass { get; set; }

    public int? HttpStatus { get; set; }

    /// <summary>The channel's request or submission id for this attempt, when it gave one.</summary>
    [MaxLength(128)]
    public string? ExternalRequestId { get; set; }

    [MaxLength(2000)]
    public string? Detail { get; set; }
}

/// <summary>
/// Something a channel told us, remembered by its key so that hearing it
/// again changes nothing.
/// </summary>
public class InboxEvent
{
    public Guid Id { get; set; }

    public Guid ChannelAccountId { get; set; }

    [MaxLength(200)]
    public required string EventKey { get; set; }

    [MaxLength(64)]
    public required string Type { get; set; }

    public DateTime ReceivedAtUtc { get; set; }
}

public static class RetryPolicy
{
    /// <summary>
    /// When to try again: the channel's Retry-After when it gave one,
    /// otherwise 30s doubling per attempt up to an hour, with up to 25% added
    /// at random so failed jobs do not all return at once.
    /// </summary>
    public static TimeSpan Delay(int attempt, TimeSpan? retryAfter, Random random)
    {
        if (retryAfter is { } asked && asked > TimeSpan.Zero)
        {
            return asked;
        }

        var seconds = Math.Min(3600d, 30d * Math.Pow(2, Math.Max(0, attempt - 1)));
        return TimeSpan.FromSeconds(seconds * (1 + random.NextDouble() * 0.25));
    }
}
