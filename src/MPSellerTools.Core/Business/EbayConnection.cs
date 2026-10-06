using System.ComponentModel.DataAnnotations;

namespace MPSellerTools.Core.Business;

public enum EbayEnvironment
{
    /// <summary>eBay's test marketplace: test users and listings, no real money.</summary>
    Sandbox,
    Production,
}

/// <summary>
/// Single-row table: this company's link to its eBay seller account. The
/// application keys come from the company's eBay developer account; the
/// seller grants access through eBay's own consent page, which gives a
/// refresh token. Both secrets are stored encrypted with this instance's
/// data protection keys and are never returned by the API.
/// </summary>
public class EbayConnection
{
    public Guid Id { get; set; }

    public EbayEnvironment Environment { get; set; }

    /// <summary>The keyset's App ID.</summary>
    [MaxLength(200)]
    public required string ClientId { get; set; }

    /// <summary>The keyset's Cert ID, encrypted.</summary>
    [MaxLength(2048)]
    public required string ClientSecretProtected { get; set; }

    /// <summary>The keyset's redirect URL name, which eBay takes in place of a redirect URL.</summary>
    [MaxLength(200)]
    public required string RuName { get; set; }

    /// <summary>The seller's refresh token, encrypted. Null until the seller has granted access.</summary>
    [MaxLength(4096)]
    public string? RefreshTokenProtected { get; set; }

    public DateTime? RefreshTokenExpiresAtUtc { get; set; }

    public DateTime? ConnectedAtUtc { get; set; }

    /// <summary>The value sent with a consent request still in progress; eBay must send it back.</summary>
    [MaxLength(100)]
    public string? PendingState { get; set; }

    public DateTime? LastOrderSyncAtUtc { get; set; }

    public DateTime? LastProductSyncAtUtc { get; set; }

    /// <summary>Why the last sync failed; null after one that worked.</summary>
    [MaxLength(1000)]
    public string? LastSyncError { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
