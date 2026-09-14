namespace MPSellerTools.Core.Platform;

/// <summary>
/// A durable unit of work processed by MPSellerTools.Provisioning.Worker. The
/// worker claims a job by writing its own identity + a lease expiry (brief
/// §10 step 3) rather than relying on row locks held across process restarts.
/// </summary>
public class ProvisioningJob
{
    public Guid Id { get; set; }

    public required Guid TenantId { get; set; }

    public required ProvisioningJobType JobType { get; set; }

    public ProvisioningJobStatus Status { get; set; } = ProvisioningJobStatus.Pending;

    public int Attempts { get; set; }

    /// <summary>Opaque identity of the worker process currently holding the lease, if any.</summary>
    public string? LeaseOwner { get; set; }

    public DateTime? LeaseExpiresAtUtc { get; set; }

    /// <summary>Safe-to-display error description; never a raw exception/stack trace.</summary>
    public string? LastError { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[]? RowVersion { get; set; }
}
