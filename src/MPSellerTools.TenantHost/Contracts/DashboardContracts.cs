namespace MPSellerTools.TenantHost.Contracts;

public record TenantDashboardResponse(
    int ProductCount,
    int OpenOrderCount,
    int OpenTaskCount,
    int TotalOrderCount,
    int TotalTaskCount);
