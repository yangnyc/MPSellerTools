using System.ComponentModel.DataAnnotations;

namespace MPSellerTools.Core.Business;

/// <summary>The e-commerce site a product is posted on.</summary>
public enum SalesChannel
{
    Ebay,
    Amazon,
    Walmart,

    /// <summary>The company's own website: published locally, with no outside API to call.</summary>
    Website,
}

public enum ListingStatus
{
    /// <summary>Buyers can see and buy it.</summary>
    Live,

    /// <summary>Still posted, but with nothing left to sell.</summary>
    OutOfStock,

    /// <summary>No longer on sale; kept on the site's side as a finished listing.</summary>
    Ended,
}

/// <summary>
/// One posting of a product on an e-commerce site, as that site last reported
/// it. Rows are written only by an import from the site; nothing here is
/// edited by hand, and a posting the site no longer reports is removed.
/// </summary>
public class Listing
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public SalesChannel Channel { get; set; }

    /// <summary>The site's own number for the posting (eBay's item number). Unique per channel.</summary>
    [MaxLength(64)]
    public required string ExternalId { get; set; }

    /// <summary>Which of the channel's sites it is on, as the channel names it (e.g. EBAY_US).</summary>
    [MaxLength(32)]
    public string? Marketplace { get; set; }

    /// <summary>The public page of the posting.</summary>
    [MaxLength(500)]
    public string? Url { get; set; }

    public ListingStatus Status { get; set; }

    /// <summary>The price asked on the site, which can differ from the catalog price.</summary>
    public decimal? Price { get; set; }

    [MaxLength(3)]
    public string? Currency { get; set; }

    public int? AvailableQuantity { get; set; }

    public int? SoldQuantity { get; set; }

    public DateTime FirstSeenAtUtc { get; set; }

    public DateTime LastSyncedAtUtc { get; set; }
}
