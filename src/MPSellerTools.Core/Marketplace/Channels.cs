using System.ComponentModel.DataAnnotations;
using MPSellerTools.Core.Business;

namespace MPSellerTools.Core.Marketplace;

public enum ChannelEnvironment
{
    Sandbox,
    Production,
}

/// <summary>What to do when the price seen on the channel is not the one managed here.</summary>
public enum PriceConflictPolicy
{
    /// <summary>Send the local price again.</summary>
    RestoreLocal,

    /// <summary>Take the channel's price as the listing's price here.</summary>
    ImportRemote,

    /// <summary>Change nothing on either side; mark the listing as in conflict.</summary>
    ReportConflict,
}

/// <summary>
/// One seller account on a marketplace, or the company's own website. Holds
/// no secret in the clear: an eBay account uses the keys of the company's
/// <see cref="EbayConnection"/>; the others keep theirs encrypted in
/// <see cref="CredentialsProtected"/>.
/// </summary>
public class ChannelAccount
{
    public Guid Id { get; set; }

    public SalesChannel Channel { get; set; }

    [MaxLength(100)]
    public required string Name { get; set; }

    public ChannelEnvironment Environment { get; set; }

    /// <summary>The channel's own id for the seller (Amazon's selling partner id); not a secret.</summary>
    [MaxLength(100)]
    public string? SellerId { get; set; }

    /// <summary>Channel credentials as encrypted JSON. Never returned by the API or logged.</summary>
    public string? CredentialsProtected { get; set; }

    /// <summary>Non-secret channel settings as JSON (eBay policy ids and location key, Walmart spec version...).</summary>
    public string? SettingsJson { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Whether this account may change anything on the channel. Off by
    /// default, and the host-wide switch has to be on as well; while either
    /// is off every operation is carried out as a dry run.
    /// </summary>
    public bool LiveWritesEnabled { get; set; }

    /// <summary>Whether quantities are sent to the channel when stock changes.</summary>
    public bool InventorySyncEnabled { get; set; }

    public bool OrderImportEnabled { get; set; }

    public PriceConflictPolicy PriceConflictPolicy { get; set; } = PriceConflictPolicy.ReportConflict;

    public DateTime? LastOrderImportAtUtc { get; set; }

    /// <summary>Why the channel last refused this account (bad credentials, usually); null once it works.</summary>
    [MaxLength(1000)]
    public string? LastError { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[]? RowVersion { get; set; }
}

/// <summary>One marketplace of an account: a site with its own language and currency.</summary>
public class ChannelMarket
{
    public Guid Id { get; set; }

    public Guid ChannelAccountId { get; set; }

    /// <summary>The channel's code for the site: ATVPDKIKX0DER, EBAY_US, WALMART_US, or "default" for the website.</summary>
    [MaxLength(32)]
    public required string MarketplaceCode { get; set; }

    [MaxLength(10)]
    public string Language { get; set; } = "en-US";

    [MaxLength(3)]
    public string Currency { get; set; } = "USD";
}

/// <summary>
/// Where an internal category goes on one marketplace, and what that place
/// requires. The requirements are a snapshot; where they came from and which
/// version they are is kept so a submission can say what it was checked against.
/// </summary>
public class CategoryMapping
{
    public Guid Id { get; set; }

    public Guid ChannelMarketId { get; set; }

    [MaxLength(100)]
    public required string InternalCategory { get; set; }

    /// <summary>eBay category id, Amazon product type, or Walmart product type.</summary>
    [MaxLength(100)]
    public required string ExternalCategoryId { get; set; }

    /// <summary>JSON: {"required":[...],"enums":{attr:[...]},"conditional":[{"when":attr,"equals":value,"require":[...]}]}.</summary>
    public string? RequirementsJson { get; set; }

    [MaxLength(500)]
    public string? RequirementsSource { get; set; }

    [MaxLength(100)]
    public string? RequirementsVersion { get; set; }

    public DateTime? RequirementsRetrievedAtUtc { get; set; }
}

public enum ListingDesiredState
{
    /// <summary>Being prepared; nothing is sent.</summary>
    Draft,

    /// <summary>Should be on sale.</summary>
    Active,

    /// <summary>Should not be on sale; the record stays so the remote listing can still be found and ended.</summary>
    Inactive,
}

/// <summary>What the channel itself last said about the listing. Never set from what was merely sent.</summary>
public enum ListingObservedStatus
{
    Unknown,
    NotListed,

    /// <summary>The channel took the submission and has not finished with it. Not yet for sale.</summary>
    Processing,

    /// <summary>The channel confirmed buyers can purchase it.</summary>
    Live,
    Inactive,

    /// <summary>The channel turned it down; see the listing's issues.</summary>
    Rejected,
}

public enum FulfillmentMode
{
    /// <summary>Shipped from the merchant's own stock; this system owns the quantity.</summary>
    Merchant,

    /// <summary>Shipped by the channel (Amazon FBA, Walmart WFS); the channel owns the quantity and it is never sent from here.</summary>
    ChannelFulfilled,
}

/// <summary>
/// How one variant is offered on one marketplace. Three things are kept
/// apart: what is wanted (desired state, overrides, versions), what the
/// channel reported (observed fields), and the operations in between
/// (<see cref="SyncJob"/>). Each of content, price and inventory has its own
/// version counter: "desired" moves on every change, "confirmed" only when
/// the channel has confirmed that version or a newer one.
/// </summary>
public class ChannelListing
{
    public Guid Id { get; set; }

    public Guid ChannelMarketId { get; set; }

    public Guid VariantId { get; set; }

    /// <summary>The SKU the channel knows the offer by. Unique within the marketplace; usually the internal SKU.</summary>
    [MaxLength(64)]
    public required string SellerSku { get; set; }

    /// <summary>Overrides the category mapping for this one listing.</summary>
    [MaxLength(100)]
    public string? ExternalCategoryId { get; set; }

    /// <summary>
    /// Per-field content overrides as JSON: a field that is absent is
    /// inherited from the product, {"value":"..."} replaces it, and
    /// {"cleared":true} deliberately sends it empty.
    /// </summary>
    public string? ContentOverridesJson { get; set; }

    /// <summary>
    /// The product's pictures chosen for this marketplace, as a JSON array of
    /// <see cref="ProductMedia"/> ids in the order they are sent. Null sends
    /// all of the product's pictures in the product's own order.
    /// </summary>
    public string? ImageSelectionJson { get; set; }

    /// <summary>Category-specific attributes as a JSON object of name to value.</summary>
    public string? AttributesJson { get; set; }

    /// <summary>The price on this channel; null to use the variant's.</summary>
    public decimal? PriceOverride { get; set; }

    public FulfillmentMode FulfillmentMode { get; set; }

    /// <summary>The most this channel may be told is available; null for no cap of its own.</summary>
    public int? QuantityCap { get; set; }

    public ListingDesiredState DesiredState { get; set; }

    public long ContentVersion { get; set; }

    public long PriceVersion { get; set; }

    public long InventoryVersion { get; set; }

    public long ConfirmedContentVersion { get; set; }

    public long ConfirmedPriceVersion { get; set; }

    public long ConfirmedInventoryVersion { get; set; }

    public ListingObservedStatus ObservedStatus { get; set; }

    public decimal? ObservedPrice { get; set; }

    public int? ObservedQuantity { get; set; }

    /// <summary>The channel's or the local validation's open issues as a JSON array.</summary>
    public string? IssuesJson { get; set; }

    public DateTime? ObservedAtUtc { get; set; }

    /// <summary>Set under <see cref="PriceConflictPolicy.ReportConflict"/> when the channel's price is not the local one.</summary>
    public bool HasPriceConflict { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[]? RowVersion { get; set; }
}

/// <summary>
/// A variation family on one marketplace (an eBay inventory item group, an
/// Amazon parent). Groups are per marketplace on purpose: channels allow
/// different variation themes, so they need not match each other or the
/// product's own variants. The group itself holds no stock.
/// </summary>
public class ListingGroup
{
    public Guid Id { get; set; }

    public Guid ChannelMarketId { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>Seller-chosen key: eBay's inventoryItemGroupKey, Amazon's parent SKU.</summary>
    [MaxLength(64)]
    public required string GroupKey { get; set; }

    /// <summary>JSON array of the option names the members differ by, e.g. ["Color","Size"].</summary>
    public string? VariationAttributesJson { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

public class ListingGroupMember
{
    public Guid ListingGroupId { get; set; }

    public Guid ChannelListingId { get; set; }
}

public enum ExternalResourceType
{
    /// <summary>The channel's catalog item (ASIN, Walmart item id).</summary>
    CatalogItem,

    /// <summary>eBay offer id.</summary>
    Offer,

    /// <summary>The public listing (eBay item number). Several SKUs of one group share it.</summary>
    Listing,
    ItemGroup,
}

public enum ExternalOwnerType
{
    ChannelListing,
    ListingGroup,
}

/// <summary>
/// An id a channel gave to something of ours. One row per owner and kind;
/// the value is deliberately not unique, since one eBay listing id belongs
/// to every variant sold through it.
/// </summary>
public class ExternalReference
{
    public Guid Id { get; set; }

    public Guid ChannelAccountId { get; set; }

    public Guid? ChannelMarketId { get; set; }

    public ExternalOwnerType OwnerType { get; set; }

    public Guid OwnerId { get; set; }

    public ExternalResourceType ResourceType { get; set; }

    [MaxLength(128)]
    public required string Value { get; set; }

    /// <summary>True for ids made up by fixtures and tests; never true for anything a channel issued.</summary>
    public bool IsTestOnly { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
