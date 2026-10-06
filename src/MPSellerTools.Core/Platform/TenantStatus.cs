namespace MPSellerTools.Core.Platform;

/// <summary>Runtime state of a tenant instance (brief §10).</summary>
public enum TenantStatus
{
    Provisioning,
    Active,
    Suspended,
    Failed,

    /// <summary>Deletion was requested; the worker is removing the instance, its database and its files.</summary>
    Deleting,
}
