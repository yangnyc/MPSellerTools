namespace MPSellerTools.TenantHost.Contracts;

public record AuditEntryResponse(Guid Id, DateTime OccurredAtUtc, string ActorEmail, string Action, string? Details);
