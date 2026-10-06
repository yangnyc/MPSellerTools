namespace MPSellerTools.TenantHost.Contracts;

/// <summary><see cref="Sales"/> is the company-wide picture and is only there for a TenantAdmin.</summary>
public record TenantDashboardResponse(
    int ProductCount,
    int OpenOrderCount,
    int OpenTaskCount,
    int TotalOrderCount,
    int TotalTaskCount,
    SalesDashboard? Sales = null);

public record ChannelSales(string Channel, int Orders, decimal Revenue);

public record DailySales(DateOnly Date, int Orders, decimal Revenue);

public record LowStockItem(Guid VariantId, string Sku, string ProductName, int AvailableToSell, int OnHand);

public record ListingStateCounts(int Draft, int Live, int Processing, int Rejected, int OffSale);

/// <summary>
/// Orders and revenue cover the last <see cref="Days"/> days and leave
/// cancelled orders out. <see cref="StaleChannels"/> names the channels whose
/// orders have not been read recently enough for stock to be sent to them.
/// </summary>
public record SalesDashboard(
    int Days,
    decimal Revenue,
    int Orders,
    IReadOnlyList<ChannelSales> Channels,
    IReadOnlyList<DailySales> Daily,
    int LowStockThreshold,
    int LowStockCount,
    IReadOnlyList<LowStockItem> LowStock,
    ListingStateCounts Listings,
    int FailedSyncJobs,
    int OpenOrderIssues,
    IReadOnlyList<string> StaleChannels);
