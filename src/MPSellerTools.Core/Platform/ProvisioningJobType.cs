namespace MPSellerTools.Core.Platform;

public enum ProvisioningJobType
{
    CreateTenant,
    Suspend,
    Resume,

    /// <summary>Stops an Active tenant's process and starts it again.</summary>
    Restart,

    /// <summary>Stops the tenant's process, drops its database, and removes it from the registry.</summary>
    Delete,
}
