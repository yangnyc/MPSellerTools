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

    /// <summary>Email address of the company's first TenantAdmin, whose account is made during provisioning.</summary>
    public required string InitialAdminEmail { get; set; }

    /// <summary>
    /// The hash of the password chosen for the first administrator when the company was asked for, kept
    /// only until provisioning has made the account and then cleared. Null when none was chosen: the
    /// administrator then keeps the password they have in another company. Never the password itself.
    /// </summary>
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? InitialAdminPasswordHash { get; set; }

    public TenantStatus Status { get; set; } = TenantStatus.Provisioning;

    /// <summary>Assigned by the provisioning pipeline; null until a port is reserved.</summary>
    public int? Port { get; set; }

    /// <summary>Server-generated database name for this tenant's own database.</summary>
    public string? DatabaseName { get; set; }

    /// <summary>Local dev/base URL once Active (e.g. https://localhost:7203).</summary>
    public string? Url { get; set; }

    /// <summary>Distinct from TenantId — identifies this particular running instance (brief §4).</summary>
    public Guid? ApplicationInstanceId { get; set; }

    /// <summary>
    /// The OS process currently serving this tenant, if any. Combined with
    /// <see cref="ProcessStartTimeUtc"/> so the worker can verify it is still
    /// its own process before treating it as running or stopping it — a bare
    /// PID is not sufficient because the OS may reuse it (brief §10).
    /// </summary>
    public int? ProcessId { get; set; }

    public DateTime? ProcessStartTimeUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Set when Status transitions to Failed; safe to display (no secrets).</summary>
    public string? FailureReason { get; set; }

    /// <summary>Optimistic concurrency token.</summary>
    public byte[]? RowVersion { get; set; }
}
