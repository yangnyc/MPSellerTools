namespace MPSellerTools.Core.Business;

public class WorkItem
{
    public Guid Id { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public WorkItemStatus Status { get; set; } = WorkItemStatus.Open;

    public required Guid AssignedUserId { get; set; }

    public DateTime? DueAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[]? RowVersion { get; set; }
}
