using System.ComponentModel.DataAnnotations;

namespace MPSellerTools.Core.Business;

/// <summary>What a bulk job does. Each works on everything of one kind in one sales channel account.</summary>
public enum BulkJobType
{
    /// <summary>Publishes every draft that passes its check.</summary>
    PublishDrafts,

    /// <summary>Takes everything that is wanted on sale off sale.</summary>
    TakeOffSale,

    /// <summary>Sends again every listing that is wanted on sale but has not reached the marketplace.</summary>
    SendAgain,

    /// <summary>Checks every listing again and records what stops each being published.</summary>
    CheckListings,

    /// <summary>Reads what the marketplace or store itself has on sale (eBay, Magento).</summary>
    ReadStore,

    /// <summary>
    /// Sends every listing that is wanted on sale again, whether or not it arrived before: for a change the
    /// listings do not show by themselves, such as which of the store's categories a product category goes to.
    /// </summary>
    SendEverythingAgain,
}

public enum BulkJobStatus
{
    Queued,
    Running,
    Succeeded,

    /// <summary>Finished, with some items that could not be done.</summary>
    CompletedWithErrors,

    /// <summary>Could not be carried out at all.</summary>
    Failed,
    Cancelled,
}

/// <summary>
/// A large piece of work asked for once and carried out in the background,
/// item by item: publishing thousands of listings, say. It lives in the
/// tenant's own database, so it survives a restart and is picked up again;
/// what it has done so far is kept in its counts.
/// </summary>
public class BulkJob
{
    public Guid Id { get; set; }

    public BulkJobType Type { get; set; }

    public BulkJobStatus Status { get; set; }

    /// <summary>The sales channel account it works on.</summary>
    public Guid ChannelAccountId { get; set; }

    /// <summary>How many items it set out to do; 0 for work that is not counted in items (reading a store).</summary>
    public int Total { get; set; }

    public int Processed { get; set; }

    public int Succeeded { get; set; }

    public int Failed { get; set; }

    /// <summary>Asked to stop; a running job stops after the items it is on.</summary>
    public bool CancelRequested { get; set; }

    /// <summary>What it came to, in a sentence, once finished.</summary>
    [MaxLength(500)]
    public string? Summary { get; set; }

    /// <summary>Why it could not be carried out at all.</summary>
    [MaxLength(1000)]
    public string? LastError { get; set; }

    /// <summary>The first items that could not be done and why, as JSON: [{"item","message"}].</summary>
    public string? ErrorsJson { get; set; }

    public Guid CreatedByUserId { get; set; }

    [MaxLength(256)]
    public required string CreatedByEmail { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? FinishedAtUtc { get; set; }

    /// <summary>When the worker last reported on it; a running job gone quiet is taken to have been interrupted.</summary>
    public DateTime? HeartbeatAtUtc { get; set; }
}
