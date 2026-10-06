namespace MPSellerTools.Core.Business;

public class OrderItem
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public required Guid ProductId { get; set; }

    /// <summary>The variant sold. Null only on lines written before variants existed and not yet backfilled.</summary>
    public Guid? VariantId { get; set; }

    /// <summary>The channel's id for the line, on an imported order.</summary>
    public string? ExternalLineId { get; set; }

    /// <summary>The SKU as the channel sent it, on an imported order.</summary>
    public string? SellerSku { get; set; }

    public int Quantity { get; set; }

    /// <summary>Snapshot of Product.Price at the time this item was added — not a live reference.</summary>
    public decimal UnitPrice { get; set; }
}
