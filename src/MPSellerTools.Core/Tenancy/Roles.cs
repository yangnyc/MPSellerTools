namespace MPSellerTools.Core.Tenancy;

/// <summary>
/// Role names used across both the platform database (PlatformAdmin) and every
/// tenant database (TenantAdmin, Employee). See brief §5 for the permission matrix.
/// </summary>
public static class Roles
{
    public const string PlatformAdmin = "PlatformAdmin";
    public const string TenantAdmin = "TenantAdmin";
    public const string Employee = "Employee";

    /// <summary>
    /// The platform console acting inside a tenant through that tenant's
    /// platform access key. Never stored against a user; it may manage the
    /// tenant's users and nothing else.
    /// </summary>
    public const string PlatformOperator = "PlatformOperator";
}
