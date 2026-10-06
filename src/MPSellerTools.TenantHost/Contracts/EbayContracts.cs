using MPSellerTools.Core.Business;

namespace MPSellerTools.TenantHost.Contracts;

/// <summary>
/// The state of the company's eBay link. The Cert ID and the seller's tokens
/// are never part of it. <see cref="CallbackPath"/> is the path on this
/// workspace that eBay must send the seller back to.
/// </summary>
public record EbayStatusResponse(
    bool Configured,
    EbayEnvironment Environment,
    string? ClientId,
    string? RuName,
    bool Connected,
    DateTime? ConnectedAtUtc,
    DateTime? AccessExpiresAtUtc,
    DateTime? LastOrderSyncAtUtc,
    DateTime? LastProductSyncAtUtc,
    string? LastSyncError,
    int ImportedOrders,
    string CallbackPath);

/// <summary><see cref="ClientSecret"/> may be left empty to keep the one already saved.</summary>
public record SaveEbaySettingsRequest(EbayEnvironment Environment, string ClientId, string? ClientSecret, string RuName);

public record EbayConnectResponse(string AuthorizeUrl);

/// <summary>The address eBay sent the seller back to, or just the code from it.</summary>
public record CompleteEbayConnectRequest(string CodeOrUrl);
