namespace MPSellerTools.Provisioning.Worker;

public class ProvisioningOptions
{
    public const string SectionName = "Provisioning";

    /// <summary>
    /// Directory containing the published (brief §10: "prebuilt, approved
    /// executable") MPSellerTools.TenantHost output — produced once by
    /// scripts/Build.ps1, never compiled ad hoc per tenant.
    /// </summary>
    public required string TenantHostPublishDirectory { get; set; }

    /// <summary>
    /// Path to the dotnet executable used to launch the published TenantHost
    /// DLL. Defaults to "dotnet" (resolved via PATH). On an ARM64 Windows
    /// host this MUST resolve to an x64 dotnet install — SQL Server LocalDB's
    /// native components are x64-only, so an ARM64-hosted TenantHost process
    /// cannot open a LocalDB connection at all (encountered and documented
    /// during Increment 2/3; see README's environment notes).
    /// </summary>
    public string DotnetExecutablePath { get; set; } = "dotnet";

    public int PollIntervalSeconds { get; set; } = 5;

    public int LeaseDurationSeconds { get; set; } = 120;

    public int ReadinessTimeoutSeconds { get; set; } = 30;
}
