using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace;
using MPSellerTools.TenantHost.Marketplace.Channels;

namespace MPSellerTools.TenantHost.Services;

/// <summary>One thing asked of Amazon's catalog: an ASIN or a barcode, and the SKU its product is to have here, if one was given.</summary>
public record AmazonImportEntry(string Type, string Value, string? Sku);

/// <summary>What a bulk import was given, kept with its job.</summary>
public record AmazonImportParameters(IReadOnlyList<AmazonImportEntry> Items, string SkuPrefix, bool UpdateExisting);

public enum AmazonImportOutcome
{
    Created,
    Updated,

    /// <summary>A product with the SKU is already here and was not to be changed.</summary>
    AlreadyHere,
}

public record AmazonImportResult(AmazonImportOutcome Outcome, Guid ProductId, string Sku);

/// <summary>
/// Makes products out of items in Amazon's catalog. The catalog is read
/// through the company's own Amazon account (the Catalog Items API), which
/// knows every item on Amazon, not only the ones the company sells; nothing
/// is changed on Amazon. A product gets the item's name, brand, description
/// and bullet points, category, pictures, barcodes, part number, weight and
/// size. Amazon's catalog has no selling price or stock: the price is the
/// list price where Amazon has one, and stock starts at nothing unless given.
/// </summary>
public partial class AmazonImport(TenantDbContext db, ListingService listings)
{
    /// <summary>What an imported product's SKU starts with when none was chosen for it.</summary>
    public const string DefaultSkuPrefix = "AMZ-";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"(?:/dp/|/gp/product/|/gp/aw/d/|/product/|[?&]asin=)([A-Za-z0-9]{10})(?:[/?&#]|$)")]
    private static partial Regex AsinInAddress();

    [GeneratedRegex("^[A-Za-z0-9]{10}$")]
    private static partial Regex Asin();

    public static string Serialize(AmazonImportParameters parameters) => JsonSerializer.Serialize(parameters, Json);

    public static AmazonImportParameters? Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<AmazonImportParameters>(json, Json);

    /// <summary>
    /// Reads what was typed or pasted as an ASIN, the address of an item's
    /// page on Amazon, or a barcode (UPC, EAN or GTIN). Null when it is none of those.
    /// </summary>
    public static (string Type, string Value)? ParseIdentifier(string? input)
    {
        var text = input?.Trim() ?? "";
        if (AsinInAddress().Match(text) is { Success: true } address)
        {
            return ("ASIN", address.Groups[1].Value.ToUpperInvariant());
        }
        if (Asin().IsMatch(text))
        {
            return ("ASIN", text.ToUpperInvariant());
        }
        return text.All(char.IsAsciiDigit)
            ? text.Length switch { 12 => ("UPC", text), 13 => ("EAN", text), 14 => ("GTIN", text), _ => null }
            : null;
    }

    /// <summary>The company's Amazon account and its marketplace, or why the catalog cannot be read.</summary>
    public async Task<(ChannelContext? Context, string? Problem)> ContextAsync(CancellationToken cancellationToken)
    {
        var account = await db.ChannelAccounts.AsNoTracking().OrderBy(a => a.CreatedAtUtc).FirstOrDefaultAsync(a => a.Channel == SalesChannel.Amazon, cancellationToken);
        return account is null ? (null, "Add Amazon as a sales channel first: its catalog is read through your own Amazon account.") : await ContextAsync(account, cancellationToken);
    }

    public async Task<(ChannelContext? Context, string? Problem)> ContextAsync(ChannelAccount account, CancellationToken cancellationToken)
    {
        if (!account.IsEnabled)
        {
            return (null, "The Amazon account is switched off, so nothing is read from Amazon.");
        }

        var market = await db.ChannelMarkets.AsNoTracking().OrderBy(m => m.MarketplaceCode).FirstOrDefaultAsync(m => m.ChannelAccountId == account.Id, cancellationToken);
        return market is null ? (null, "The Amazon account has no marketplace to read the catalog of.") : (ListingService.ContextFor(account, market), null);
    }

    /// <summary>
    /// Asks Amazon for these, all of one type and no more than a lookup takes, and pairs each with the item
    /// that answers it; null for one Amazon does not know.
    /// </summary>
    public async Task<IReadOnlyList<(AmazonImportEntry Entry, AmazonCatalogItem? Item)>> LookupAsync(
        ChannelContext context, IReadOnlyList<AmazonImportEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var amazon = (AmazonChannelAdapter)listings.AdapterFor(context.Account);
        var type = entries[0].Type;
        var items = await amazon.GetCatalogItemsAsync(context, entries.Select(e => e.Value).Distinct().ToList(), type, cancellationToken);
        return entries
            .Select(entry => (entry, type == "ASIN"
                ? items.FirstOrDefault(i => string.Equals(i.Asin, entry.Value, StringComparison.OrdinalIgnoreCase))
                // A barcode with its leading zeros dropped or added is still the same barcode.
                : items.FirstOrDefault(i => i.Barcodes.Any(b => b.TrimStart('0') == entry.Value.TrimStart('0')))))
            .ToList();
    }

    /// <summary>The items Amazon puts first for words of a name.</summary>
    public Task<IReadOnlyList<AmazonCatalogItem>> FindAsync(ChannelContext context, string keywords, CancellationToken cancellationToken) =>
        ((AmazonChannelAdapter)listings.AdapterFor(context.Account)).FindCatalogItemsAsync(context, keywords, cancellationToken);

    /// <summary>The SKU an item's product gets when none was chosen: the prefix and its ASIN.</summary>
    public static string SkuFor(AmazonCatalogItem item, string? prefix) => $"{prefix ?? DefaultSkuPrefix}{item.Asin}";

    /// <summary>The item's description and bullet points as one text, as a product keeps them.</summary>
    public static string? DescriptionOf(AmazonCatalogItem item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            parts.Add(item.Description.Trim());
        }
        if (item.BulletPoints.Count > 0)
        {
            parts.Add(string.Join("\n", item.BulletPoints.Select(point => $"- {point}")));
        }
        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    /// <summary>
    /// Makes the item a product under <paramref name="sku"/>, or brings the product already there up to date
    /// when <paramref name="updateExisting"/> says so; an archived product is never touched. A product
    /// brought up to date keeps its own price and stock, and its own pictures when it has any. Nothing is
    /// saved here: the caller saves, so a batch goes in together.
    /// </summary>
    public async Task<AmazonImportResult> ApplyAsync(
        AmazonCatalogItem item, string sku, decimal? price, int stock, bool updateExisting, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var product = db.Products.Local.FirstOrDefault(p => string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase))
            ?? await db.Products.FirstOrDefaultAsync(p => p.Sku == sku, cancellationToken);
        if (product is null)
        {
            // The SKU of another product's variant is taken too.
            if (await db.ProductVariants.AnyAsync(v => v.Sku == sku, cancellationToken))
            {
                throw new InvalidOperationException($"The SKU {sku} is already in use by a variant of another product.");
            }

            product = new Product
            {
                Id = Guid.NewGuid(),
                Sku = sku,
                Name = Truncate(string.IsNullOrWhiteSpace(item.Title) ? sku : item.Title.Trim(), 200),
                Brand = Blank(item.Brand, 200),
                Description = DescriptionOf(item),
                Category = Blank(item.Category, 100),
                Price = Math.Max(price ?? item.ListPrice ?? 0m, 0m),
                StockQuantity = Math.Max(stock, 0),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            db.Products.Add(product);
            // Made here rather than left to the save, so it can carry the weight and size.
            var created = CatalogSaveChangesInterceptor.NewDefaultVariant(product, now);
            Measure(created, item, overwrite: true);
            db.ProductVariants.Add(created);
            db.InventoryBalances.Add(CatalogSaveChangesInterceptor.NewBalance(created.Id, product.StockQuantity, now));
            AddIdentifiers(item, product.Id, created.Id, []);
            AddPictures(item, product.Id, now);
            return new AmazonImportResult(AmazonImportOutcome.Created, product.Id, product.Sku);
        }

        if (!updateExisting || product.IsArchived || db.Entry(product).State == EntityState.Added)
        {
            return new AmazonImportResult(AmazonImportOutcome.AlreadyHere, product.Id, product.Sku);
        }

        product.Name = string.IsNullOrWhiteSpace(item.Title) ? product.Name : Truncate(item.Title.Trim(), 200);
        product.Brand = Blank(item.Brand, 200) ?? product.Brand;
        product.Description = DescriptionOf(item) ?? product.Description;
        product.Category = Blank(item.Category, 100) ?? product.Category;
        if (price is { } given && given >= 0)
        {
            product.Price = given;
        }
        product.UpdatedAtUtc = now;

        var variant = await db.ProductVariants.FirstOrDefaultAsync(v => v.ProductId == product.Id && v.IsDefault, cancellationToken);
        if (variant is not null)
        {
            Measure(variant, item, overwrite: false);
            var held = await db.ProductIdentifiers.Where(i => i.ProductId == product.Id || i.VariantId == variant.Id).Select(i => i.Type).ToListAsync(cancellationToken);
            AddIdentifiers(item, product.Id, variant.Id, held);
        }
        if (!await db.ProductMedia.AnyAsync(m => m.ProductId == product.Id, cancellationToken))
        {
            AddPictures(item, product.Id, now);
        }
        return new AmazonImportResult(AmazonImportOutcome.Updated, product.Id, product.Sku);
    }

    private static void Measure(ProductVariant variant, AmazonCatalogItem item, bool overwrite)
    {
        if (item.WeightValue is not null && (overwrite || variant.WeightValue is null))
        {
            (variant.WeightValue, variant.WeightUnit) = (item.WeightValue, item.WeightUnit);
        }
        if (item.Length is not null && (overwrite || (variant.Length ?? variant.Width ?? variant.Height) is null))
        {
            (variant.Length, variant.Width, variant.Height, variant.DimensionUnit) = (item.Length, item.Width, item.Height, item.DimensionUnit);
        }
    }

    /// <summary>The barcodes go on the variant that is sold, the part number on the product; a type already held is left.</summary>
    private void AddIdentifiers(AmazonCatalogItem item, Guid productId, Guid variantId, IReadOnlyCollection<ProductIdentifierType> held)
    {
        foreach (var (type, value) in item.Identifiers.Where(i => !held.Contains(i.Key)))
        {
            db.ProductIdentifiers.Add(new ProductIdentifier
            {
                Id = Guid.NewGuid(),
                ProductId = type == ProductIdentifierType.Mpn ? productId : null,
                VariantId = type == ProductIdentifierType.Mpn ? null : variantId,
                Type = type,
                Value = value,
            });
        }
    }

    private void AddPictures(AmazonCatalogItem item, Guid productId, DateTime now)
    {
        for (var i = 0; i < item.ImageUrls.Count && i < 12; i++)
        {
            var asset = new MediaAsset { Id = Guid.NewGuid(), Url = item.ImageUrls[i], CreatedAtUtc = now };
            db.MediaAssets.Add(asset);
            db.ProductMedia.Add(new ProductMedia
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                MediaAssetId = asset.Id,
                Purpose = i == 0 ? MediaPurpose.Main : MediaPurpose.Gallery,
                Position = i,
            });
        }
    }

    private static string? Blank(string? text, int length) => string.IsNullOrWhiteSpace(text) ? null : Truncate(text.Trim(), length);

    private static string Truncate(string text, int length) => text.Length > length ? text[..length] : text;
}
