namespace MPSellerTools.Core.Platform;

/// <summary>Runtime state of a tenant instance (brief §10).</summary>
public enum TenantStatus
{
    Provisioning,
    Active,
    Suspended,
    Failed,
}
