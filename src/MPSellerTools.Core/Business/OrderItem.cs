namespace MPSellerTools.Core.Business;

public class OrderItem
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public required Guid ProductId { get; set; }

    public int Quantity { get; set; }

    /// <summary>Snapshot of Product.Price at the time this item was added — not a live reference.</summary>
    public decimal UnitPrice { get; set; }
}
