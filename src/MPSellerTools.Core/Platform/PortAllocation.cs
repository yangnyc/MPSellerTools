namespace MPSellerTools.Core.Platform;

/// <summary>
/// The local dev port pool for provisioned TenantHost instances. Starts at
/// 7201 so the first two companies provisioned through this pipeline land on
/// exactly the ports brief §6 documents for the Company A / Company B demo
/// seed data — later companies (e.g. a third company created through the
/// PlatformAdmin UI, brief §13 check #3) get 7203, 7204, and so on.
/// </summary>
public static class PortAllocation
{
    public const int MinPort = 7201;
    public const int MaxPort = 7299;
}
