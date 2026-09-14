namespace MPSellerTools.Core.Business;

public class Product
{
    public Guid Id { get; set; }

    /// <summary>Unique within this tenant's database (brief §9).</summary>
    public required string Sku { get; set; }

    public required string Name { get; set; }

    public decimal Price { get; set; }

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
