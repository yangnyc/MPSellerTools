namespace MPSellerTools.Core.Business;

public class Order
{
    public Guid Id { get; set; }

    /// <summary>Unique within this tenant's database.</summary>
    public required string OrderNumber { get; set; }

    /// <summary>
    /// The eBay order id, for an order imported from eBay; null for one
    /// created here. Unique, so importing again updates rather than duplicates.
    /// </summary>
    public string? EbayOrderId { get; set; }

    /// <summary>The channel account an imported order came through; null for one created here.</summary>
    public Guid? ChannelAccountId { get; set; }

    /// <summary>The channel's order id. Unique within the account, so importing again never duplicates.</summary>
    public string? ExternalOrderId { get; set; }

    public string? Currency { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.New;

    /// <summary>Employee/TenantAdmin responsible for this order; null until assigned.</summary>
    public Guid? AssignedUserId { get; set; }

    public List<OrderItem> Items { get; set; } = [];

    /// <summary>
    /// Server-computed sum of Items' (UnitPrice * Quantity). Never trust a
    /// client-supplied total (brief §9) — this is recalculated on every write.
    /// </summary>
    public decimal Total { get; set; }

    /// <summary>Who carried the parcel, as entered when the order was shipped from here; null when not recorded.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public string? ShippingCarrier { get; set; }

    /// <summary>The carrier's tracking number, as entered when the order was shipped; null when not recorded.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? TrackingNumber { get; set; }

    /// <summary>When the order was marked shipped from the Shipping pages; null for one completed another way.</summary>
    public DateTime? ShippedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[]? RowVersion { get; set; }
}
