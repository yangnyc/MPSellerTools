using System.ComponentModel.DataAnnotations;

namespace MPSellerTools.Core.Marketplace;

public enum InventoryLocationKind
{
    MerchantWarehouse,
    AmazonFba,
    WalmartWfs,
}

/// <summary>
/// A pool of stock with one owner. The merchant's own warehouse is owned
/// here; an FBA or WFS pool is owned by the channel, and is only ever read.
/// </summary>
public class InventoryLocation
{
    /// <summary>The merchant warehouse every tenant database starts with.</summary>
    public static readonly Guid DefaultId = new("0a000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; }

    [MaxLength(32)]
    public required string Code { get; set; }

    [MaxLength(100)]
    public required string Name { get; set; }

    public InventoryLocationKind Kind { get; set; }

    /// <summary>True when the channel is the source of truth and quantities here are a copy of its report.</summary>
    public bool IsExternallyOwned { get; set; }

    public Guid? ChannelAccountId { get; set; }
}

/// <summary>Stock of one variant at one location.</summary>
public class InventoryBalance
{
    public Guid Id { get; set; }

    public Guid LocationId { get; set; }

    public Guid VariantId { get; set; }

    public int OnHand { get; set; }

    public int Reserved { get; set; }

    public int SafetyStock { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>What can still be promised to a buyer.</summary>
    public int AvailableToSell => Availability.ToSell(OnHand, Reserved, SafetyStock);
}

public static class Availability
{
    public static int ToSell(int onHand, int reserved, int safetyStock) => Math.Max(0, onHand - reserved - safetyStock);

    /// <summary>What one channel is told, given the listing's own cap.</summary>
    public static int ForChannel(int availableToSell, int? quantityCap) =>
        quantityCap is { } cap ? Math.Min(availableToSell, Math.Max(0, cap)) : availableToSell;
}

public enum ReservationStatus
{
    Active,

    /// <summary>The goods left: on hand and reserved both went down.</summary>
    Shipped,

    /// <summary>Cancelled or expired: the units are free again.</summary>
    Released,
}

/// <summary>
/// Units held for one order line. <see cref="IdempotencyKey"/> is unique, so
/// the same order or event arriving twice holds the units once.
/// </summary>
public class InventoryReservation
{
    public Guid Id { get; set; }

    public Guid LocationId { get; set; }

    public Guid VariantId { get; set; }

    public int Quantity { get; set; }

    public ReservationStatus Status { get; set; }

    [MaxLength(128)]
    public required string IdempotencyKey { get; set; }

    public Guid? OrderId { get; set; }

    /// <summary>When an unpaid hold lapses on its own; null for a hold that only shipping or cancelling ends.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ClosedAtUtc { get; set; }
}

public enum InventoryMovementType
{
    Adjustment,
    Reserve,
    Release,
    Ship,
    ReturnReceipt,
}

/// <summary>One change to a balance, kept for the audit trail.</summary>
public class InventoryMovement
{
    public Guid Id { get; set; }

    public Guid LocationId { get; set; }

    public Guid VariantId { get; set; }

    public InventoryMovementType Type { get; set; }

    public int OnHandDelta { get; set; }

    public int ReservedDelta { get; set; }

    [MaxLength(200)]
    public string? Reference { get; set; }

    /// <summary>Unique when set: a receipt or event recorded once however often it is delivered.</summary>
    [MaxLength(128)]
    public string? IdempotencyKey { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}

public enum OrderLineIssueReason
{
    /// <summary>The channel's SKU matches nothing here.</summary>
    UnknownSku,

    /// <summary>The channel's SKU matches more than one variant.</summary>
    AmbiguousSku,

    /// <summary>The channel sold more than could be reserved: an oversell to sort out by hand.</summary>
    InventoryShortfall,
}

/// <summary>
/// An imported order line that could not be mapped or reserved. It waits
/// here for someone to resolve it; nothing is guessed.
/// </summary>
public class OrderLineIssue
{
    public Guid Id { get; set; }

    public Guid? ChannelAccountId { get; set; }

    [MaxLength(64)]
    public required string ExternalOrderId { get; set; }

    [MaxLength(64)]
    public required string ExternalLineId { get; set; }

    [MaxLength(64)]
    public string? SellerSku { get; set; }

    public int Quantity { get; set; }

    public OrderLineIssueReason Reason { get; set; }

    public Guid? OrderId { get; set; }

    [MaxLength(500)]
    public string? Details { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }
}
