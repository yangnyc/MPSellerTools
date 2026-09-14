using MPSellerTools.Core.Business;

namespace MPSellerTools.TenantHost.Contracts;

public record WorkItemResponse(
    Guid Id,
    string Title,
    string? Description,
    WorkItemStatus Status,
    Guid AssignedUserId,
    DateTime? DueAtUtc,
    string RowVersion,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public record CreateWorkItemRequest(string Title, string? Description, Guid AssignedUserId, DateTime? DueAtUtc);

public record UpdateWorkItemRequest(string Title, string? Description, Guid AssignedUserId, DateTime? DueAtUtc, string RowVersion);

public record ChangeWorkItemStatusRequest(WorkItemStatus Status, string RowVersion);
