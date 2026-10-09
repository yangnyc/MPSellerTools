using MPSellerTools.Core.Business;

namespace MPSellerTools.TenantHost.Contracts;

/// <summary><see cref="Sales"/> and <see cref="Workspace"/> are the company-wide picture and are only there for a TenantAdmin.</summary>
public record TenantDashboardResponse(
    int ProductCount,
    int OpenOrderCount,
    int OpenTaskCount,
    int TotalOrderCount,
    int TotalTaskCount,
    SalesDashboard? Sales = null,
    WorkspaceOverview? Workspace = null);

/// <summary>One sales channel account and how its listings stand. <see cref="LiveWrites"/> is whether anything is really sent to it.</summary>
public record ChannelOverview(
    Guid Id, string Name, SalesChannel Channel, bool IsEnabled, bool LiveWrites, bool OrderImport, bool StockSync, int Listings, int Live, int Drafts, int Rejected);

/// <summary>Products that are not ready to sell as they are: with no price, nothing in stock, no category or no picture.</summary>
public record CatalogGaps(int NoPrice, int NoStock, int NoCategory, int NoPictures);

/// <summary>
/// The company's sales channels, its background jobs and the state of its catalog.
/// <see cref="JobsNeedingALook"/> counts the jobs of the last <see cref="JobDays"/> days that failed or held items back.
/// </summary>
public record WorkspaceOverview(
    IReadOnlyList<ChannelOverview> Channels,
    int JobsRunning,
    int JobsWaiting,
    int JobsNeedingALook,
    int JobDays,
    string? LastJobSummary,
    CatalogGaps Catalog,
    int ProductsAddedThisWeek);

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
