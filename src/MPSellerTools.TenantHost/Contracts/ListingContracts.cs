using MPSellerTools.Core.Business;

namespace MPSellerTools.TenantHost.Contracts;

public record ListingResponse(
    Guid Id,
    Guid ProductId,
    string ProductSku,
    string ProductName,
    SalesChannel Channel,
    string ExternalId,
    string? Marketplace,
    string? Url,
    ListingStatus Status,
    decimal? Price,
    string? Currency,
    int? AvailableQuantity,
    int? SoldQuantity,
    DateTime LastSyncedAtUtc);

/// <summary>
/// The company's postings, with what the page needs to explain an empty list:
/// whether a site is connected at all, and when it was last read.
/// </summary>
public record ListingsResponse(IReadOnlyList<ListingResponse> Listings, bool Connected, DateTime? LastSyncedAtUtc);
