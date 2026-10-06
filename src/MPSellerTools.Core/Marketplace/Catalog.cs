using System.ComponentModel.DataAnnotations;

namespace MPSellerTools.Core.Marketplace;

public enum ItemCondition
{
    New,
    Used,
    Refurbished,
}

/// <summary>
/// One sellable configuration of a <see cref="Business.Product"/>: the unit
/// that has a SKU, a price and stock. Every product has at least one (its
/// default variant, which carries the product's own SKU), so a product
/// without colour/size choices is sold through that one.
/// </summary>
public class ProductVariant
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>Internal SKU, unique in this database. Stable: never regenerated when names change.</summary>
    [MaxLength(64)]
    public required string Sku { get; set; }

    /// <summary>What sets this variant apart ("Blue / Large"); null for a default variant.</summary>
    [MaxLength(200)]
    public string? Name { get; set; }

    /// <summary>The variant's option values as a JSON object, e.g. {"Color":"Blue","Size":"L"}.</summary>
    public string? OptionsJson { get; set; }

    public ItemCondition Condition { get; set; } = ItemCondition.New;

    public decimal Price { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    public decimal? WeightValue { get; set; }

    /// <summary>Unit of <see cref="WeightValue"/>: "lb", "oz", "kg" or "g".</summary>
    [MaxLength(8)]
    public string? WeightUnit { get; set; }

    public decimal? Length { get; set; }

    public decimal? Width { get; set; }

    public decimal? Height { get; set; }

    /// <summary>Unit of the three dimensions: "in" or "cm".</summary>
    [MaxLength(8)]
    public string? DimensionUnit { get; set; }

    /// <summary>
    /// The variant that mirrors the product's own SKU, price and stock, kept
    /// in step with those legacy columns on every save. One per product.
    /// </summary>
    public bool IsDefault { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[]? RowVersion { get; set; }
}

public enum ProductIdentifierType
{
    Gtin,
    Upc,
    Ean,
    Isbn,
    Mpn,
}

/// <summary>
/// A real-world identifier of a product or of one variant. Kept as text so
/// leading zeros survive. Never generated here: it is what the seller holds.
/// </summary>
public class ProductIdentifier
{
    public Guid Id { get; set; }

    /// <summary>Set when the identifier belongs to the whole product (an MPN, usually).</summary>
    public Guid? ProductId { get; set; }

    /// <summary>Set when it belongs to one variant (a GTIN/UPC/EAN, usually).</summary>
    public Guid? VariantId { get; set; }

    public ProductIdentifierType Type { get; set; }

    [MaxLength(64)]
    public required string Value { get; set; }
}

/// <summary>An image kept in outside storage; only its address and description live here.</summary>
public class MediaAsset
{
    public Guid Id { get; set; }

    /// <summary>Public address. Marketplaces fetch it, so it has to stay reachable while they do.</summary>
    [MaxLength(1000)]
    public required string Url { get; set; }

    [MaxLength(500)]
    public string? AltText { get; set; }

    [MaxLength(100)]
    public string? ContentType { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

public enum MediaPurpose
{
    Main,
    Gallery,
    Swatch,
}

/// <summary>Puts an image on a product, or on one of its variants, at a position.</summary>
public class ProductMedia
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Guid? VariantId { get; set; }

    public Guid MediaAssetId { get; set; }

    public MediaPurpose Purpose { get; set; }

    public int Position { get; set; }
}
