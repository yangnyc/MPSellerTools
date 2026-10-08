using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace.Channels;

namespace MPSellerTools.TenantHost.Services;

/// <summary>What the store said about itself: its address, its store views and its base currency.</summary>
public record MagentoTestResult(string? StoreAddress, IReadOnlyList<string> StoreViews, string? Currency);

/// <summary>
/// A category of the store. <see cref="Path"/> is its name with its parents', below the store's root
/// category ("Medicine Cabinet / Pain &amp; Fever"); the root itself (level 1) is just its own name.
/// </summary>
public record MagentoStoreCategory(long Id, long ParentId, string Name, string Path, int Level, bool IsActive, int ProductCount);

/// <summary>
/// <see cref="Matched"/> products of the store have the SKU prefix; <see cref="Removed"/> were deleted from
/// it this time (or would be, in a dry run), <see cref="Kept"/> were left because this workspace still lists
/// them, and <see cref="Remaining"/> are still to go.
/// </summary>
public record MagentoRemovalResult(bool DryRun, int Matched, int Removed, int Kept, int Remaining);

/// <summary><see cref="Listings"/> is how many products the store reported; <see cref="Created"/> how many of them were new to the catalog here.</summary>
public record MagentoImportResult(int Created, int Listings);

/// <summary>
/// Reads the connected Magento store: whether it can be reached with the
/// saved token, and the products in its catalog, which are recorded as the
/// company's listings there. The store is only read.
/// </summary>
public class MagentoSync(TenantDbContext db, ChannelHttp http, ChannelSecrets secrets, AuditLogger audit)
{
    private const int PageSize = 100;
    private const int MaxPages = 50;

    /// <summary>Asks the store for its own configuration, which only a working address and token can answer.</summary>
    public async Task<MagentoTestResult> TestAsync(ChannelAccount account, CancellationToken cancellationToken)
    {
        var root = MagentoApi.Root(MagentoApi.Setting(account, "baseUrl"));
        var response = await MagentoApi.SendAsync(http, secrets, account, HttpMethod.Get, $"{root}/rest/V1/store/storeConfigs", null, cancellationToken);
        var stores = response.Body.ValueKind == JsonValueKind.Array ? response.Body.EnumerateArray().ToList() : [];
        return new MagentoTestResult(
            stores.Select(s => MagentoApi.Text(s, "base_url")).FirstOrDefault(url => url is not null),
            stores.Select(s => MagentoApi.Text(s, "code")).OfType<string>().ToList(),
            stores.Select(s => MagentoApi.Text(s, "base_currency_code")).FirstOrDefault(code => code is not null));
    }

    /// <summary>
    /// Deletes from the store products whose SKU starts with <paramref name="skuPrefix"/>, up to
    /// <paramref name="limit"/> at a time, so a large clear-out is made in several calls. A product this
    /// workspace still has a Magento listing for is never deleted here: that one is taken off sale from its
    /// listing instead. With <paramref name="dryRun"/> nothing is deleted and the counts say what would be.
    /// </summary>
    public async Task<MagentoRemovalResult> RemoveStoreProductsAsync(
        ChannelAccount account, string skuPrefix, int limit, bool dryRun, CancellationToken cancellationToken)
    {
        var root = MagentoApi.Root(MagentoApi.Setting(account, "baseUrl"));
        // In a LIKE the store reads % and _ as wildcards; a prefix is meant letter for letter.
        var pattern = Uri.EscapeDataString(skuPrefix.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%");
        var managed = (await (
            from listing in db.ChannelListings.AsNoTracking()
            join market in db.ChannelMarkets.AsNoTracking() on listing.ChannelMarketId equals market.Id
            where market.ChannelAccountId == account.Id && listing.SellerSku.StartsWith(skuPrefix)
            select listing.SellerSku).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var (matched, kept, removable) = (0, 0, new List<string>());
        for (var page = 1; page <= MaxPages && removable.Count < limit; page++)
        {
            var response = await MagentoApi.SendAsync(
                http, secrets, account, HttpMethod.Get,
                $"{root}/rest/all/V1/products?searchCriteria[filter_groups][0][filters][0][field]=sku"
                + $"&searchCriteria[filter_groups][0][filters][0][value]={pattern}"
                + "&searchCriteria[filter_groups][0][filters][0][condition_type]=like"
                + $"&searchCriteria[pageSize]={PageSize}&searchCriteria[currentPage]={page}&fields=items[sku],total_count",
                null, cancellationToken);
            var items = response.Body.TryGetProperty("items", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [];
            matched = (int)(MagentoApi.Number(response.Body, "total_count") ?? 0);
            foreach (var sku in items.Select(i => MagentoApi.Text(i, "sku")).OfType<string>())
            {
                // The store's LIKE does not tell upper from lower case; the prefix here is meant as written.
                if (!sku.StartsWith(skuPrefix, StringComparison.Ordinal))
                {
                    continue;
                }
                if (managed.Contains(sku))
                {
                    kept++;
                }
                else if (removable.Count < limit)
                {
                    removable.Add(sku);
                }
            }
            if (items.Count < PageSize || page * PageSize >= matched)
            {
                break;
            }
        }

        if (!dryRun)
        {
            foreach (var sku in removable)
            {
                // 404: already gone, which is what was wanted.
                await MagentoApi.SendAsync(
                    http, secrets, account, HttpMethod.Delete, $"{root}/rest/all/V1/products/{Uri.EscapeDataString(sku)}", null, cancellationToken, 404);
            }
            audit.Log("MagentoStoreProductsRemoved", $"prefix={skuPrefix}; removed={removable.Count}");
            await db.SaveChangesAsync(cancellationToken);
        }
        return new MagentoRemovalResult(dryRun, matched, removable.Count, kept, Math.Max(0, matched - kept - removable.Count));
    }

    /// <summary>The store's whole category tree, parents before their children. The catalog's own root (level 0) is left out.</summary>
    public async Task<List<MagentoStoreCategory>> GetCategoriesAsync(ChannelAccount account, CancellationToken cancellationToken)
    {
        var root = MagentoApi.Root(MagentoApi.Setting(account, "baseUrl"));
        var response = await MagentoApi.SendAsync(http, secrets, account, HttpMethod.Get, $"{root}/rest/all/V1/categories", null, cancellationToken);
        var categories = new List<MagentoStoreCategory>();
        Collect(response.Body, "", categories);
        return categories;
    }

    private static void Collect(JsonElement node, string parentPath, List<MagentoStoreCategory> into)
    {
        if (node.ValueKind != JsonValueKind.Object || MagentoApi.Number(node, "id") is not { } id)
        {
            return;
        }

        var level = (int)(MagentoApi.Number(node, "level") ?? 0);
        var name = MagentoApi.Text(node, "name")?.Trim() ?? "";
        // The store's root category (level 1) is where paths start, so it is not part of its children's.
        var path = level <= 1 ? name : parentPath.Length == 0 ? name : $"{parentPath} / {name}";
        if (level >= 1)
        {
            into.Add(new MagentoStoreCategory(
                (long)id, (long)(MagentoApi.Number(node, "parent_id") ?? 0), name, path, level,
                node.TryGetProperty("is_active", out var active) && active.ValueKind == JsonValueKind.True,
                (int)(MagentoApi.Number(node, "product_count") ?? 0)));
        }
        if (node.TryGetProperty("children_data", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                Collect(child, level <= 1 ? "" : path, into);
            }
        }
    }

    /// <summary>Creates a category in the store under <paramref name="parentId"/> and returns the number the store gave it.</summary>
    public async Task<long> CreateCategoryAsync(
        ChannelAccount account, string name, long parentId, bool isActive, bool includeInMenu, CancellationToken cancellationToken)
    {
        var root = MagentoApi.Root(MagentoApi.Setting(account, "baseUrl"));
        var response = await MagentoApi.SendAsync(
            http, secrets, account, HttpMethod.Post, $"{root}/rest/all/V1/categories",
            new { category = new { parent_id = parentId, name, is_active = isActive, include_in_menu = includeInMenu } }, cancellationToken);
        return MagentoApi.Number(response.Body, "id") is { } id
            ? (long)id
            : throw new ChannelException(SyncErrorClass.Transient, "Magento created a category but did not say which.", ambiguous: true);
    }

    /// <summary>
    /// Records every product in the store's catalog as a listing, under the
    /// product with its SKU, or under a new one made for it; a product
    /// already here is the company's own record and is left as it is. A
    /// listing the store no longer reports is removed.
    /// </summary>
    public async Task<MagentoImportResult> ImportListingsAsync(ChannelAccount account, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var root = MagentoApi.Root(MagentoApi.Setting(account, "baseUrl"));

        var remote = new List<JsonElement>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var response = await MagentoApi.SendAsync(
                http, secrets, account, HttpMethod.Get,
                $"{root}/rest/all/V1/products?searchCriteria[pageSize]={PageSize}&searchCriteria[currentPage]={page}", null, cancellationToken);
            var items = response.Body.TryGetProperty("items", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [];
            remote.AddRange(items);

            // Magento answers a page past the end with the last page again, so the total is what ends the loop.
            var total = (int)(MagentoApi.Number(response.Body, "total_count") ?? 0);
            if (items.Count < PageSize || page * PageSize >= total)
            {
                break;
            }
        }

        var stock = await StockAsync(account, root, cancellationToken);
        var currency = await db.ChannelMarkets.Where(m => m.ChannelAccountId == account.Id).Select(m => m.Currency).FirstOrDefaultAsync(cancellationToken);

        // A product needs a number to be linked to and a SKU a product here can hold.
        var found = remote
            .Select(p => (Product: p, Id: MagentoApi.Number(p, "id"), Sku: MagentoApi.Text(p, "sku")?.Trim()))
            .Where(p => p.Id is not null && p.Sku is { Length: > 0 and <= 64 })
            .DistinctBy(p => p.Id)
            .ToList();

        var skus = found.Select(p => p.Sku!).Distinct().ToList();
        var products = (await db.Products.Where(p => skus.Contains(p.Sku)).ToListAsync(cancellationToken)).ToDictionary(p => p.Sku);
        var listings = await db.Listings.Where(l => l.Channel == SalesChannel.Magento).ToDictionaryAsync(l => l.ExternalId, cancellationToken);
        var seen = new HashSet<string>();

        var created = 0;
        foreach (var (remoteProduct, remoteId, remoteSku) in found)
        {
            var id = (long)remoteId!.Value;
            var externalId = id.ToString(CultureInfo.InvariantCulture);
            var price = MagentoApi.Number(remoteProduct, "price");
            (decimal Quantity, bool InStock)? held = stock is not null && stock.TryGetValue(id, out var item) ? item : null;

            if (!products.TryGetValue(remoteSku!, out var product))
            {
                var name = MagentoApi.Text(remoteProduct, "name")?.Trim();
                product = new Product
                {
                    Id = Guid.NewGuid(),
                    Sku = remoteSku!,
                    Name = Truncate(string.IsNullOrEmpty(name) ? remoteSku! : name, 200),
                    Price = price ?? 0m,
                    StockQuantity = (int)Math.Max(held?.Quantity ?? 0m, 0m),
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                db.Products.Add(product);
                products[remoteSku!] = product;
                created++;
            }

            seen.Add(externalId);
            if (!listings.TryGetValue(externalId, out var listing))
            {
                listing = new Listing { Id = Guid.NewGuid(), Channel = SalesChannel.Magento, ExternalId = externalId, FirstSeenAtUtc = now };
                db.Listings.Add(listing);
            }

            listing.ProductId = product.Id;
            listing.Marketplace = null;
            // An address every Magento storefront answers, whatever the product's own URL key is.
            listing.Url = $"{root}/catalog/product/view/id/{externalId}";
            listing.Status = MagentoApi.Number(remoteProduct, "status") != 1 ? ListingStatus.Ended
                : held is { } known && (!known.InStock || known.Quantity <= 0) ? ListingStatus.OutOfStock
                : ListingStatus.Live;
            listing.Price = price;
            listing.Currency = currency is { Length: 3 } ? currency : null;
            listing.AvailableQuantity = held is { } stocked ? (int)Math.Max(stocked.Quantity, 0m) : null;
            listing.SoldQuantity = null;
            listing.LastSyncedAtUtc = now;
        }

        db.Listings.RemoveRange(listings.Values.Where(l => !seen.Contains(l.ExternalId)));

        audit.Log("MagentoListingsImported", $"created={created}; listings={seen.Count}");
        await db.SaveChangesAsync(cancellationToken);
        return new MagentoImportResult(created, seen.Count);
    }

    /// <summary>
    /// Every product's stock by its number, from the one call that returns
    /// them all (the low-stock report, asked with a ceiling no quantity
    /// reaches). Null when the store would not answer it, as one using
    /// several stock sources may not; the listings are then recorded without a quantity.
    /// </summary>
    private async Task<Dictionary<long, (decimal Quantity, bool InStock)>?> StockAsync(ChannelAccount account, string root, CancellationToken cancellationToken)
    {
        var stock = new Dictionary<long, (decimal, bool)>();
        try
        {
            for (var page = 1; page <= MaxPages; page++)
            {
                var response = await MagentoApi.SendAsync(
                    http, secrets, account, HttpMethod.Get,
                    $"{root}/rest/V1/stockItems/lowStock/?scopeId=0&qty=99999999&pageSize=500&currentPage={page}", null, cancellationToken);
                var items = response.Body.TryGetProperty("items", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [];
                foreach (var item in items)
                {
                    if (MagentoApi.Number(item, "product_id") is { } productId)
                    {
                        stock[(long)productId] = (
                            MagentoApi.Number(item, "qty") ?? 0m,
                            item.TryGetProperty("is_in_stock", out var inStock) && inStock.ValueKind == JsonValueKind.True);
                    }
                }

                var total = (int)(MagentoApi.Number(response.Body, "total_count") ?? 0);
                if (items.Count < 500 || page * 500 >= total)
                {
                    break;
                }
            }
        }
        catch (ChannelException)
        {
            return null;
        }
        return stock;
    }

    private static string Truncate(string text, int length) => text.Length > length ? text[..length] : text;
}
