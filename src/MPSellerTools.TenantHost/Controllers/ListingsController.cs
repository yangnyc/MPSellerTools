using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;

namespace MPSellerTools.TenantHost.Controllers;

/// <summary>
/// The company's products as posted on e-commerce sites. Read-only, like the
/// catalog is for employees: every row is what a site itself reported, either
/// through the eBay import or as the observed state of a channel listing, and
/// is never edited here.
/// </summary>
[ApiController]
[Route("api/listings")]
[Authorize(Policy = Roles.Employee)]
public class ListingsController(TenantDbContext db) : ControllerBase
{
    /// <summary>With <paramref name="channel"/>, only that marketplace's postings.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] SalesChannel? channel, CancellationToken cancellationToken)
    {
        var listings = await (
            from listing in db.Listings
            join product in db.Products on listing.ProductId equals product.Id
            where channel == null || listing.Channel == channel
            orderby product.Name, listing.ExternalId
            select new ListingResponse(
                listing.Id, product.Id, product.Sku, product.Name, listing.Channel, listing.ExternalId, listing.Marketplace,
                listing.Url, listing.Status, listing.Price, listing.Currency, listing.AvailableQuantity, listing.SoldQuantity,
                listing.LastSyncedAtUtc)
        ).ToListAsync(cancellationToken);

        // The same eBay item can be known both ways; the import's row, which also carries what was sold, wins.
        var known = listings.Select(l => (l.Channel, l.ExternalId)).ToHashSet();
        listings.AddRange((await ObservedAsync(channel, cancellationToken)).Where(l => !known.Contains((l.Channel, l.ExternalId))));

        var connection = await db.EbayConnections.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var ebayConnected = connection?.RefreshTokenProtected is not null;
        var connected = channel switch
        {
            null => ebayConnected || await db.ChannelAccounts.AnyAsync(a => a.Channel != SalesChannel.Website, cancellationToken),
            SalesChannel.Ebay => ebayConnected,
            _ => await db.ChannelAccounts.AnyAsync(a => a.Channel == channel, cancellationToken),
        };
        var lastRead = listings.Count > 0 ? listings.Max(l => l.LastSyncedAtUtc) : (DateTime?)null;
        return Ok(new ListingsResponse(
            listings.OrderBy(l => l.ProductName).ThenBy(l => l.ExternalId).ToList(),
            connected,
            channel is null or SalesChannel.Ebay ? connection?.LastProductSyncAtUtc ?? lastRead : lastRead));
    }

    /// <summary>
    /// Channel listings the marketplace itself has reported as on sale or
    /// ended. One that was only sent, is still being processed or was turned
    /// down is not a posting and is left out.
    /// </summary>
    private async Task<List<ListingResponse>> ObservedAsync(SalesChannel? channel, CancellationToken cancellationToken)
    {
        var rows = await (
            from listing in db.ChannelListings.AsNoTracking()
            join market in db.ChannelMarkets.AsNoTracking() on listing.ChannelMarketId equals market.Id
            join account in db.ChannelAccounts.AsNoTracking() on market.ChannelAccountId equals account.Id
            join variant in db.ProductVariants.AsNoTracking() on listing.VariantId equals variant.Id
            join product in db.Products.AsNoTracking() on variant.ProductId equals product.Id
            where account.Channel != SalesChannel.Website && (channel == null || account.Channel == channel)
                && (listing.ObservedStatus == ListingObservedStatus.Live || listing.ObservedStatus == ListingObservedStatus.Inactive)
            select new { listing, market, account.Channel, account.Environment, product.Id, product.Name }).Take(1000).ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.listing.Id).ToList();
        var references = (await db.ExternalReferences.AsNoTracking()
            .Where(r => r.OwnerType == ExternalOwnerType.ChannelListing && ids.Contains(r.OwnerId)
                && (r.ResourceType == ExternalResourceType.Listing || r.ResourceType == ExternalResourceType.CatalogItem))
            .ToListAsync(cancellationToken))
            // The public listing's number where there is one (eBay), otherwise the catalog item's (an ASIN, a Walmart item id).
            .GroupBy(r => r.OwnerId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.ResourceType == ExternalResourceType.Listing ? 0 : 1).First());

        return rows.Select(r =>
        {
            var reference = references.GetValueOrDefault(r.listing.Id);
            // A sandbox or test-only number leads nowhere on the public site.
            var linkable = reference is { IsTestOnly: false } && r.Environment == ChannelEnvironment.Production;
            return new ListingResponse(
                r.listing.Id, r.Id, r.listing.SellerSku, r.Name, r.Channel, reference?.Value ?? r.listing.SellerSku, r.market.MarketplaceCode,
                linkable ? PublicUrl(r.Channel, r.market.MarketplaceCode, reference!.Value) : null,
                r.listing.ObservedStatus == ListingObservedStatus.Inactive ? ListingStatus.Ended
                    : r.listing.ObservedQuantity == 0 ? ListingStatus.OutOfStock
                    : ListingStatus.Live,
                r.listing.ObservedPrice, r.market.Currency, r.listing.ObservedQuantity, SoldQuantity: null,
                r.listing.ObservedAtUtc ?? r.listing.UpdatedAtUtc);
        }).ToList();
    }

    /// <summary>The posting's page on the site's United States storefront; null for a site whose address is not known here.</summary>
    internal static string? PublicUrl(SalesChannel channel, string marketplace, string id) => (channel, marketplace) switch
    {
        (SalesChannel.Ebay, "EBAY_US") => $"https://www.ebay.com/itm/{Uri.EscapeDataString(id)}",
        (SalesChannel.Amazon, "ATVPDKIKX0DER") => $"https://www.amazon.com/dp/{Uri.EscapeDataString(id)}",
        (SalesChannel.Walmart, "WALMART_US") => $"https://www.walmart.com/ip/{Uri.EscapeDataString(id)}",
        _ => null,
    };
}
