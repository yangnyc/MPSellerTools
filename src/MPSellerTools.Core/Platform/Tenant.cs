namespace MPSellerTools.Core.Platform;

/// <summary>
/// A row in the platform database describing one company's application instance.
/// The platform database never stores the company's business data, Identity
/// users, or passwords (brief §4) — only this metadata.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; }

    /// <summary>Unique, URL-safe, normalized identifier (brief §10 step 1).</summary>
    public required string Slug { get; set; }

    public required string Name { get; set; }

    /// <summary>Email address invited as the initial TenantAdmin during provisioning.</summary>
    public required string InitialAdminEmail { get; set; }

    public TenantStatus Status { get; set; } = TenantStatus.Provisioning;

    /// <summary>Assigned by the provisioning pipeline; null until a port is reserved.</summary>
    public int? Port { get; set; }

    /// <summary>Server-generated database name for this tenant's own database.</summary>
    public string? DatabaseName { get; set; }

    /// <summary>Local dev/base URL once Active (e.g. https://localhost:7203).</summary>
    public string? Url { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Set when Status transitions to Failed; safe to display (no secrets).</summary>
    public string? FailureReason { get; set; }

    /// <summary>Optimistic concurrency token.</summary>
    public byte[]? RowVersion { get; set; }
}
