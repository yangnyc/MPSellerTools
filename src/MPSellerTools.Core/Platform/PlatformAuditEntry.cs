namespace MPSellerTools.Core.Platform;

/// <summary>Platform-level audit record (brief §8 `/audit` in the platform console).</summary>
public class PlatformAuditEntry
{
    public Guid Id { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public required Guid ActorUserId { get; set; }

    public required string ActorEmail { get; set; }

    /// <summary>Short machine-readable action name, e.g. "TenantCreated", "TenantSuspended".</summary>
    public required string Action { get; set; }

    public Guid? TenantId { get; set; }

    /// <summary>Human-readable, non-sensitive detail text (no secrets, no connection strings).</summary>
    public string? Details { get; set; }
}
