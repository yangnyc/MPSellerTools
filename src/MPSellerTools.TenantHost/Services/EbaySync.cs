using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace;
using MPSellerTools.TenantHost.Marketplace.Channels;

namespace MPSellerTools.TenantHost.Services;

public record EbayOrderSyncResult(int Created, int Updated, int ProductsCreated);

/// <summary>
/// <see cref="Listings"/> is how many of the seller's items eBay reported as
/// posted. <see cref="Warning"/> says why the listings made on the eBay site
/// could not be read, when they could not; the rest was still imported.
/// </summary>
public record EbayProductSyncResult(int Created, int Updated, int Listings, string? Warning = null);

/// <summary>
/// Copies the connected seller's eBay orders and inventory into this
/// company's own orders and products. eBay is only read; nothing here
/// changes a listing or an order on eBay.
/// </summary>
public class EbaySync(
    TenantDbContext db, EbayClient ebay, IDataProtectionProvider dataProtection, AuditLogger audit, OrderIngestionService orders)
{
    /// <summary>How far back the first order import reaches; eBay keeps orders for about this long.</summary>
    private static readonly TimeSpan FirstImportWindow = TimeSpan.FromDays(90);

    /// <summary>Re-read on every import, so a change eBay recorded late is not missed.</summary>
    private static readonly TimeSpan Overlap = TimeSpan.FromHours(1);

    private readonly IDataProtector _protector = dataProtection.CreateProtector("MPSellerTools.Ebay");

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);

    /// <summary>
    /// Imports orders changed since the last import. A new eBay order becomes
    /// an order here; one imported before only has its status brought up to
    /// date, so edits made here to its items or assignee are kept.
    /// </summary>
    public async Task<EbayOrderSyncResult> ImportOrdersAsync(EbayConnection connection, CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTime.UtcNow;
        var sinceUtc = connection.LastOrderSyncAtUtc is { } last ? last - Overlap : startedAtUtc - FirstImportWindow;

        var accessToken = await AccessTokenAsync(connection, cancellationToken);
        var ebayOrders = await ebay.GetOrdersAsync(connection.Environment, accessToken, sinceUtc, cancellationToken);

        // Orders go through the same door as every other channel's. When the
        // company has an eBay channel account they are filed under it.
        var account = await db.ChannelAccounts.FirstOrDefaultAsync(a => a.Channel == SalesChannel.Ebay, cancellationToken);
        int created = 0, updated = 0, productsCreated = 0;
        foreach (var ebayOrder in ebayOrders)
        {
            var order = EbayChannelAdapter.ToChannelOrder(ebayOrder);
            // A listing made without a SKU still needs one here, so its eBay item number stands in.
            var lines = (ebayOrder.LineItems ?? []).Where(l => l.Quantity > 0)
                .Zip(order.Lines, (source, line) => line with { SellerSku = SkuOf(source) })
                .ToList();
            var result = await orders.IngestAsync(
                account, SalesChannel.Ebay, order with { Lines = lines }, UnknownSkuPolicy.CreatePlaceholderProduct, cancellationToken);
            created += result.Outcome == IngestOutcome.Created ? 1 : 0;
            updated += result.Outcome == IngestOutcome.Updated ? 1 : 0;
            productsCreated += result.ProductsCreated;
        }

        connection.LastOrderSyncAtUtc = startedAtUtc;
        connection.LastSyncError = null;
        audit.Log("EbayOrdersImported", $"created={created}; updated={updated}; productsCreated={productsCreated}");
        await db.SaveChangesAsync(cancellationToken);
        return new EbayOrderSyncResult(created, updated, productsCreated);
    }

    /// <summary>
    /// Imports the seller's eBay inventory items as products, matched by SKU.
    /// A product already here gets eBay's name, quantity and price; an
    /// archived one is left alone. Each item's published offers are recorded
    /// as its listings, and so is everything else the seller has on sale:
    /// a listing made on the eBay site has no inventory item, so it is filed
    /// under the product with its SKU, or under a new one made for it.
    /// </summary>
    public async Task<EbayProductSyncResult> ImportProductsAsync(EbayConnection connection, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var accessToken = await AccessTokenAsync(connection, cancellationToken);
        var items = await ebay.GetInventoryItemsAsync(connection.Environment, accessToken, cancellationToken);
        // Longer SKUs than a product can hold cannot be matched reliably, so they are skipped.
        items = items.Where(i => i.Sku.Length <= 64).DistinctBy(i => i.Sku).ToList();

        var skus = items.Select(i => i.Sku).ToList();
        var products = await db.Products.Where(p => EF.Parameter(skus).Contains(p.Sku)).ToDictionaryAsync(p => p.Sku, cancellationToken);

        var listings = await db.Listings
            .Where(l => l.Channel == SalesChannel.Ebay)
            .ToDictionaryAsync(l => l.ExternalId, cancellationToken);
        var seenListingIds = new HashSet<string>();
        // Products whose offers eBay would not show this time: their listings are left as they were.
        var unreadProductIds = new HashSet<Guid>();

        int created = 0, updated = 0;
        foreach (var item in items)
        {
            var offers = await ebay.GetOffersAsync(connection.Environment, accessToken, item.Sku, cancellationToken);
            var price = offers?.FirstOrDefault()?.Price;
            var name = Truncate(string.IsNullOrWhiteSpace(item.Title) ? item.Sku : item.Title.Trim(), 200);
            var quantity = Math.Max(item.Quantity ?? 0, 0);

            if (!products.TryGetValue(item.Sku, out var product))
            {
                product = new Product
                {
                    Id = Guid.NewGuid(),
                    Sku = item.Sku,
                    Name = name,
                    Price = price ?? 0m,
                    StockQuantity = quantity,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                db.Products.Add(product);
                products[item.Sku] = product;
                created++;
            }
            else if (!product.IsArchived
                && (product.Name != name || product.StockQuantity != quantity || (price is { } p && product.Price != p)))
            {
                product.Name = name;
                product.StockQuantity = quantity;
                product.Price = price ?? product.Price;
                product.UpdatedAtUtc = now;
                updated++;
            }

            if (offers is null)
            {
                unreadProductIds.Add(product.Id);
                continue;
            }

            // Only a published offer has a listing; an unpublished one is a draft nobody can buy.
            foreach (var offer in offers.Where(o => o.ListingId is { Length: <= 64 }))
            {
                if (!seenListingIds.Add(offer.ListingId!))
                {
                    continue;
                }

                if (!listings.TryGetValue(offer.ListingId!, out var listing))
                {
                    listing = new Listing { Id = Guid.NewGuid(), Channel = SalesChannel.Ebay, ExternalId = offer.ListingId!, FirstSeenAtUtc = now };
                    db.Listings.Add(listing);
                }

                listing.ProductId = product.Id;
                listing.Marketplace = offer.MarketplaceId is { Length: > 0 and <= 32 } marketplace ? marketplace : null;
                listing.Url = EbayClient.ListingUrl(connection.Environment, offer.ListingId!);
                listing.Status = ListingStatusOf(offer);
                listing.Price = offer.Price;
                listing.Currency = offer.Currency is { Length: 3 } currency ? currency : null;
                listing.AvailableQuantity = offer.AvailableQuantity;
                listing.SoldQuantity = offer.SoldQuantity;
                listing.LastSyncedAtUtc = now;
            }
        }

        // Everything else on sale: the listings made on the eBay site, which have no inventory item.
        List<EbayActiveListing>? onSale = null;
        string? warning = null;
        try
        {
            onSale = await ebay.GetActiveListingsAsync(connection.Environment, await TradingTokenAsync(connection, cancellationToken), cancellationToken);
        }
        catch (EbayApiException ex)
        {
            warning = $"Listings made on the eBay site could not be read. {ex.Message}";
        }

        var unfiled = (onSale ?? []).Where(l => l.ItemId.Length <= 64 && !seenListingIds.Contains(l.ItemId)).DistinctBy(l => l.ItemId).ToList();
        var unfiledSkus = unfiled.Select(SkuOf).Where(sku => !products.ContainsKey(sku)).Distinct().ToList();
        foreach (var known in await db.Products.Where(p => EF.Parameter(unfiledSkus).Contains(p.Sku)).ToListAsync(cancellationToken))
        {
            products[known.Sku] = known;
        }

        foreach (var item in unfiled)
        {
            seenListingIds.Add(item.ItemId);
            var sku = SkuOf(item);
            // A product already here is the company's own record and is left as it is.
            if (!products.TryGetValue(sku, out var product))
            {
                product = new Product
                {
                    Id = Guid.NewGuid(),
                    Sku = sku,
                    Name = Truncate(item.Title ?? sku, 200),
                    Price = item.Price ?? 0m,
                    StockQuantity = Math.Max(item.AvailableQuantity ?? 0, 0),
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                db.Products.Add(product);
                products[sku] = product;
                created++;
            }

            if (!listings.TryGetValue(item.ItemId, out var listing))
            {
                listing = new Listing { Id = Guid.NewGuid(), Channel = SalesChannel.Ebay, ExternalId = item.ItemId, FirstSeenAtUtc = now };
                db.Listings.Add(listing);
            }

            listing.ProductId = product.Id;
            listing.Marketplace = null;
            listing.Url = item.Url is { Length: <= 500 } url ? url : EbayClient.ListingUrl(connection.Environment, item.ItemId);
            listing.Status = item.AvailableQuantity == 0 ? ListingStatus.OutOfStock : ListingStatus.Live;
            listing.Price = item.Price;
            listing.Currency = item.Currency is { Length: 3 } currency ? currency : null;
            listing.AvailableQuantity = item.AvailableQuantity;
            listing.SoldQuantity = item.Quantity is { } posted && item.AvailableQuantity is { } left && posted >= left ? posted - left : null;
            listing.LastSyncedAtUtc = now;
        }

        // A listing eBay no longer reports is gone from the site, so it goes from here too. Not when
        // the listings on sale could not be read: one of those may simply not have been seen this time.
        if (onSale is not null)
        {
            db.Listings.RemoveRange(listings.Values.Where(l => !seenListingIds.Contains(l.ExternalId) && !unreadProductIds.Contains(l.ProductId)));
        }

        connection.LastProductSyncAtUtc = now;
        connection.LastSyncError = warning is { Length: > 1000 } ? warning[..1000] : warning;
        audit.Log("EbayProductsImported", $"created={created}; updated={updated}; listings={seenListingIds.Count}");
        await db.SaveChangesAsync(cancellationToken);
        return new EbayProductSyncResult(created, updated, seenListingIds.Count, warning);
    }

    /// <summary>A token with every scope the seller consented to, as the Trading API wants more than the read-only two.</summary>
    private async Task<string> TradingTokenAsync(EbayConnection connection, CancellationToken cancellationToken)
    {
        var tokens = await ebay.RefreshWithGrantedScopesAsync(
            connection, Unprotect(connection.ClientSecretProtected), Unprotect(connection.RefreshTokenProtected!), cancellationToken);
        return tokens.AccessToken;
    }

    private async Task<string> AccessTokenAsync(EbayConnection connection, CancellationToken cancellationToken)
    {
        if (connection.RefreshTokenProtected is null)
        {
            throw new EbayApiException("Connect the eBay account first.");
        }

        var tokens = await ebay.RefreshAsync(
            connection, Unprotect(connection.ClientSecretProtected), Unprotect(connection.RefreshTokenProtected), cancellationToken);
        return tokens.AccessToken;
    }

    // A listing made without a SKU still needs one here, so its eBay item number stands in.
    private static string SkuOf(EbayLineItem line) =>
        Truncate(string.IsNullOrWhiteSpace(line.Sku) ? $"EBAY-{line.LegacyItemId}" : line.Sku.Trim(), 64);

    // The same stand-in as for an order line without a SKU, so a listing and its orders meet on one product.
    private static string SkuOf(EbayActiveListing listing) =>
        Truncate(string.IsNullOrWhiteSpace(listing.Sku) ? $"EBAY-{listing.ItemId}" : listing.Sku.Trim(), 64);

    private static ListingStatus ListingStatusOf(EbayOffer offer) => offer.ListingStatus switch
    {
        null or "" or "ACTIVE" => ListingStatus.Live,
        "OUT_OF_STOCK" => ListingStatus.OutOfStock,
        _ => ListingStatus.Ended,
    };

    private static string Truncate(string text, int length) => text.Length > length ? text[..length] : text;
}
