namespace MPSellerTools.Core.Business;

public class Order
{
    public Guid Id { get; set; }

    /// <summary>Unique within this tenant's database.</summary>
    public required string OrderNumber { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.New;

    /// <summary>Employee/TenantAdmin responsible for this order; null until assigned.</summary>
    public Guid? AssignedUserId { get; set; }

    public List<OrderItem> Items { get; set; } = [];

    /// <summary>
    /// Server-computed sum of Items' (UnitPrice * Quantity). Never trust a
    /// client-supplied total (brief §9) — this is recalculated on every write.
    /// </summary>
    public decimal Total { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[]? RowVersion { get; set; }
}
