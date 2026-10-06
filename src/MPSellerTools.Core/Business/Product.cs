namespace MPSellerTools.Core.Business;

public class Product
{
    public Guid Id { get; set; }

    /// <summary>Unique within this tenant's database (brief §9).</summary>
    public required string Sku { get; set; }

    public required string Name { get; set; }

    public string? Brand { get; set; }

    /// <summary>Base description, shared by every channel that does not override it.</summary>
    public string? Description { get; set; }

    /// <summary>Internal category, mapped per marketplace by <see cref="Marketplace.CategoryMapping"/>.</summary>
    public string? Category { get; set; }

    /// <summary>
    /// Price of the product's default variant. Kept in step with
    /// <see cref="Marketplace.ProductVariant.Price"/> on every save.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// On-hand stock of the default variant in the merchant warehouse. Kept
    /// in step with its <see cref="Marketplace.InventoryBalance"/> on every save.
    /// </summary>
    public int StockQuantity { get; set; }

    /// <summary>
    /// Archived products are hidden from catalog/creation but retained so past
    /// OrderItem snapshots stay valid (brief §9 — never hard-delete a referenced product).
    /// </summary>
    public bool IsArchived { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[]? RowVersion { get; set; }
}
