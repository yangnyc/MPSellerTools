namespace MPSellerTools.Core.Business;

/// <summary>Tenant-level audit record (brief §8 `/audit` in the company workspace).</summary>
public class AuditEntry
{
    public Guid Id { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public Guid ActorUserId { get; set; }

    public required string ActorEmail { get; set; }

    /// <summary>Short machine-readable action name, e.g. "ProductCreated", "UserBlocked".</summary>
    public required string Action { get; set; }

    public string? Details { get; set; }
}
