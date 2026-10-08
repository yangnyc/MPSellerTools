using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using MPSellerTools.Core.Business;

namespace MPSellerTools.TenantHost.Services;

/// <summary>
/// eBay refused a request or could not be reached; the message is safe to
/// show. <see cref="StatusCode"/> is eBay's answer, when it gave one.
/// </summary>
public class EbayApiException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
}

public record EbayTokens(string AccessToken, string? RefreshToken, int? RefreshTokenExpiresInSeconds);

public record EbayOrder(
    string OrderId,
    string? OrderFulfillmentStatus,
    EbayCancelStatus? CancelStatus,
    DateTime? CreationDate,
    List<EbayLineItem>? LineItems);

public record EbayCancelStatus(string? CancelState);

/// <summary><see cref="LineItemCost"/> is the price of all the units of the line together.</summary>
public record EbayLineItem(string? LegacyItemId, string? Sku, string? Title, int Quantity, EbayAmount? LineItemCost, string? LineItemId = null);

public record EbayAmount(string? Value, string? Currency);

public record EbayInventoryItem(string Sku, string? Title, int? Quantity);

/// <summary>
/// An inventory item offered on one eBay marketplace. <see cref="ListingId"/>
/// is set once the offer is published, which is what makes it a listing
/// buyers can see; <see cref="ListingStatus"/> is eBay's own word for its state.
/// </summary>
public record EbayOffer(
    string? MarketplaceId,
    decimal? Price,
    string? Currency,
    int? AvailableQuantity,
    string? ListingId,
    string? ListingStatus,
    int? SoldQuantity);

/// <summary>
/// A listing on sale on the seller's account, however it was made.
/// <see cref="Quantity"/> is what the listing was posted with and
/// <see cref="AvailableQuantity"/> what is left of it.
/// </summary>
public record EbayActiveListing(
    string ItemId,
    string? Sku,
    string? Title,
    decimal? Price,
    string? Currency,
    int? Quantity,
    int? AvailableQuantity,
    string? Url);

/// <summary>
/// The calls this application makes to eBay: OAuth (consent, tokens), the
/// Fulfillment API (orders), the Inventory API (items and their offers,
/// which carry the price and the listing) and, for the listings the
/// Inventory API does not know about, the Trading API's GetMyeBaySelling.
/// Everything is read-only on eBay's side.
/// </summary>
public class EbayClient(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "ebay";

    /// <summary>Read-only access to the seller's orders and inventory, and nothing else.</summary>
    public const string Scopes =
        "https://api.ebay.com/oauth/api_scope/sell.fulfillment.readonly https://api.ebay.com/oauth/api_scope/sell.inventory.readonly";

    /// <summary>
    /// What publishing and updating listings needs on top of reading. Asked
    /// for only once an eBay channel account has been allowed to write.
    /// </summary>
    public const string WriteScopes =
        "https://api.ebay.com/oauth/api_scope/sell.fulfillment https://api.ebay.com/oauth/api_scope/sell.inventory https://api.ebay.com/oauth/api_scope/sell.account.readonly";

    /// <summary>
    /// eBay's basic scope, asked for at consent alongside the others: the
    /// Trading API, which lists what the seller has on sale, is called with it.
    /// </summary>
    public const string TradingScope = "https://api.ebay.com/oauth/api_scope";

    private const int PageSize = 100;
    private const int MaxPages = 50;

    private const string TradingNamespace = "urn:ebay:apis:eBLBaseComponents";
    private const string TradingVersion = "1349";
    private const int TradingPageSize = 200;
    private static readonly XNamespace Trading = TradingNamespace;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string ApiHost(EbayEnvironment environment) =>
        environment == EbayEnvironment.Production ? "https://api.ebay.com" : "https://api.sandbox.ebay.com";

    private static string AuthHost(EbayEnvironment environment) =>
        environment == EbayEnvironment.Production ? "https://auth.ebay.com" : "https://auth.sandbox.ebay.com";

    /// <summary>eBay's consent page for this keyset; the seller signs in there and is sent back with a code.</summary>
    public static string AuthorizeUrl(EbayConnection connection, string state, bool forWriting = false) =>
        $"{AuthHost(connection.Environment)}/oauth2/authorize" +
        $"?client_id={Uri.EscapeDataString(connection.ClientId)}" +
        $"&redirect_uri={Uri.EscapeDataString(connection.RuName)}" +
        "&response_type=code" +
        $"&scope={Uri.EscapeDataString(forWriting ? $"{TradingScope} {Scopes} {WriteScopes}" : $"{TradingScope} {Scopes}")}" +
        $"&state={Uri.EscapeDataString(state)}";

    /// <summary>Exchanges the code from the consent page for the seller's tokens.</summary>
    public Task<EbayTokens> ExchangeCodeAsync(EbayConnection connection, string clientSecret, string code, CancellationToken cancellationToken) =>
        RequestTokensAsync(connection, clientSecret, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = connection.RuName,
        }, cancellationToken);

    /// <summary>Gets a short-lived access token from the seller's refresh token.</summary>
    public Task<EbayTokens> RefreshAsync(EbayConnection connection, string clientSecret, string refreshToken, CancellationToken cancellationToken) =>
        RequestTokensAsync(connection, clientSecret, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = Scopes,
        }, cancellationToken);

    /// <summary>
    /// Gets an access token carrying every scope the seller consented to
    /// (eBay grants them all when none is named), for the calls that write.
    /// </summary>
    public Task<EbayTokens> RefreshWithGrantedScopesAsync(EbayConnection connection, string clientSecret, string refreshToken, CancellationToken cancellationToken) =>
        RequestTokensAsync(connection, clientSecret, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, cancellationToken);

    public static string ApiBase(EbayEnvironment environment) => ApiHost(environment);

    /// <summary>Orders created or changed since <paramref name="sinceUtc"/>.</summary>
    public async Task<List<EbayOrder>> GetOrdersAsync(
        EbayEnvironment environment, string accessToken, DateTime sinceUtc, CancellationToken cancellationToken)
    {
        var filter = Uri.EscapeDataString($"lastmodifieddate:[{sinceUtc:yyyy-MM-ddTHH:mm:ss.fffZ}..]");
        var orders = new List<EbayOrder>();
        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"{ApiHost(environment)}/sell/fulfillment/v1/order?filter={filter}&limit={PageSize}&offset={page * PageSize}";
            var result = await GetAsync<OrdersPage>(url, accessToken, cancellationToken);
            orders.AddRange(result.Orders ?? []);
            if (orders.Count >= result.Total || result.Orders is not { Count: > 0 })
            {
                break;
            }
        }
        return orders;
    }

    /// <summary>
    /// The seller's inventory items. eBay only keeps these for listings made
    /// through its Inventory API; listings made on the eBay site are not included.
    /// </summary>
    public async Task<List<EbayInventoryItem>> GetInventoryItemsAsync(
        EbayEnvironment environment, string accessToken, CancellationToken cancellationToken)
    {
        var items = new List<EbayInventoryItem>();
        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"{ApiHost(environment)}/sell/inventory/v1/inventory_item?limit={PageSize}&offset={page * PageSize}";
            var result = await GetAsync<InventoryPage>(url, accessToken, cancellationToken);
            items.AddRange((result.InventoryItems ?? [])
                .Where(i => !string.IsNullOrWhiteSpace(i.Sku))
                .Select(i => new EbayInventoryItem(i.Sku!, i.Product?.Title, i.Availability?.ShipToLocationAvailability?.Quantity)));
            if (items.Count >= result.Total || result.InventoryItems is not { Count: > 0 })
            {
                break;
            }
        }
        return items;
    }

    /// <summary>
    /// The item's offers: empty when it has none (eBay answers 404 for that),
    /// null when they could not be read, so the caller can tell "none" from "unknown".
    /// </summary>
    public async Task<List<EbayOffer>?> GetOffersAsync(
        EbayEnvironment environment, string accessToken, string sku, CancellationToken cancellationToken)
    {
        var url = $"{ApiHost(environment)}/sell/inventory/v1/offer?sku={Uri.EscapeDataString(sku)}";
        try
        {
            var result = await GetAsync<OffersPage>(url, accessToken, cancellationToken);
            return (result.Offers ?? [])
                .Select(o => new EbayOffer(
                    o.MarketplaceId,
                    ParseAmount(o.PricingSummary?.Price),
                    o.PricingSummary?.Price?.Currency,
                    o.AvailableQuantity,
                    string.IsNullOrWhiteSpace(o.Listing?.ListingId) ? null : o.Listing.ListingId.Trim(),
                    o.Listing?.ListingStatus,
                    o.Listing?.SoldQuantity))
                .ToList();
        }
        catch (EbayApiException ex)
        {
            return ex.StatusCode == StatusCodes.Status404NotFound ? [] : null;
        }
    }

    /// <summary>
    /// Every listing the seller has on sale, including the ones made on the
    /// eBay site or with another tool, which the Inventory API leaves out.
    /// Read with the Trading API's GetMyeBaySelling, a page at a time.
    /// </summary>
    public async Task<List<EbayActiveListing>> GetActiveListingsAsync(
        EbayEnvironment environment, string accessToken, CancellationToken cancellationToken)
    {
        var listings = new List<EbayActiveListing>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiHost(environment)}/ws/api.dll")
            {
                Content = new StringContent(
                    $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <GetMyeBaySellingRequest xmlns="{TradingNamespace}">
                      <ActiveList>
                        <Include>true</Include>
                        <Pagination><EntriesPerPage>{TradingPageSize}</EntriesPerPage><PageNumber>{page}</PageNumber></Pagination>
                      </ActiveList>
                    </GetMyeBaySellingRequest>
                    """,
                    Encoding.UTF8, "text/xml"),
            };
            request.Headers.Add("X-EBAY-API-CALL-NAME", "GetMyeBaySelling");
            request.Headers.Add("X-EBAY-API-SITEID", "0");
            request.Headers.Add("X-EBAY-API-COMPATIBILITY-LEVEL", TradingVersion);
            // The Trading API takes an OAuth access token in this header instead of Authorization.
            request.Headers.Add("X-EBAY-API-IAF-TOKEN", accessToken);

            var response = await SendTradingAsync(request, cancellationToken);
            var active = response.Element(Trading + "ActiveList");
            var items = active?.Element(Trading + "ItemArray")?.Elements(Trading + "Item").ToList() ?? [];
            foreach (var item in items)
            {
                var itemId = item.Element(Trading + "ItemID")?.Value.Trim();
                if (string.IsNullOrEmpty(itemId))
                {
                    continue;
                }

                // A fixed-price listing carries its price as the current price; an auction's is its latest bid.
                var price = item.Element(Trading + "SellingStatus")?.Element(Trading + "CurrentPrice")
                    ?? item.Element(Trading + "BuyItNowPrice");
                listings.Add(new EbayActiveListing(
                    itemId,
                    NullIfBlank(item.Element(Trading + "SKU")?.Value),
                    NullIfBlank(item.Element(Trading + "Title")?.Value),
                    ParseAmount(new EbayAmount(price?.Value, null)),
                    NullIfBlank(price?.Attribute("currencyID")?.Value),
                    ParseCount(item.Element(Trading + "Quantity")?.Value),
                    ParseCount(item.Element(Trading + "QuantityAvailable")?.Value),
                    NullIfBlank(item.Element(Trading + "ListingDetails")?.Element(Trading + "ViewItemURL")?.Value)));
            }

            var pages = ParseCount(active?.Element(Trading + "PaginationResult")?.Element(Trading + "TotalNumberOfPages")?.Value) ?? 1;
            if (page >= pages || items.Count == 0)
            {
                break;
            }
        }
        return listings;
    }

    /// <summary>The public page of a listing.</summary>
    public static string ListingUrl(EbayEnvironment environment, string listingId) =>
        $"{(environment == EbayEnvironment.Production ? "https://www.ebay.com" : "https://www.sandbox.ebay.com")}/itm/{Uri.EscapeDataString(listingId)}";

    public static decimal? ParseAmount(EbayAmount? amount) =>
        decimal.TryParse(amount?.Value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private async Task<EbayTokens> RequestTokensAsync(
        EbayConnection connection, string clientSecret, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiHost(connection.Environment)}/identity/v1/oauth2/token")
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connection.ClientId}:{clientSecret}")));

        var token = await SendAsync<TokenResponse>(request, cancellationToken);
        if (string.IsNullOrEmpty(token.AccessToken))
        {
            throw new EbayApiException("eBay did not return an access token.");
        }
        return new EbayTokens(token.AccessToken, token.RefreshToken, token.RefreshTokenExpiresIn);
    }

    private Task<T> GetAsync<T>(string url, string accessToken, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return SendAsync<T>(request, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        string body;
        try
        {
            response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new EbayApiException("eBay could not be reached.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new EbayApiException(
                    $"eBay refused the request ({(int)response.StatusCode}): {ErrorMessage(body)}", (int)response.StatusCode);
            }

            try
            {
                return JsonSerializer.Deserialize<T>(body, Json) ?? throw new JsonException();
            }
            catch (JsonException)
            {
                throw new EbayApiException("eBay returned an answer that could not be read.");
            }
        }
    }

    /// <summary>Sends a Trading API call and returns its answer's root element. The Trading API answers 200 even when it refuses.</summary>
    private async Task<XElement> SendTradingAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        string body;
        try
        {
            response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new EbayApiException("eBay could not be reached.");
        }

        using (response)
        {
            XElement? root = null;
            try
            {
                root = XDocument.Parse(body).Root;
            }
            catch (XmlException)
            {
            }

            var refused = root?.Element(Trading + "Ack")?.Value == "Failure";
            if (!response.IsSuccessStatusCode || refused)
            {
                var error = root?.Element(Trading + "Errors");
                var reason = NullIfBlank(error?.Element(Trading + "LongMessage")?.Value)
                    ?? NullIfBlank(error?.Element(Trading + "ShortMessage")?.Value) ?? "no reason given";
                throw new EbayApiException(
                    $"eBay refused the request ({(int)response.StatusCode}): {Truncate(reason)}", (int)response.StatusCode);
            }

            return root ?? throw new EbayApiException("eBay returned an answer that could not be read.");
        }
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static int? ParseCount(string? text) =>
        int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    // The token endpoint answers {"error", "error_description"}; the APIs answer {"errors": [{"message"}]}.
    private static string ErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error_description", out var description) && description.GetString() is { Length: > 0 } text)
                {
                    return Truncate(text);
                }

                if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
                    && errors[0].TryGetProperty("message", out var message) && message.GetString() is { Length: > 0 } first)
                {
                    return Truncate(first);
                }
            }
        }
        catch (JsonException)
        {
        }
        return "no reason given";
    }

    private static string Truncate(string text) => text.Length > 300 ? text[..300] : text;

    private record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("refresh_token_expires_in")] int? RefreshTokenExpiresIn);

    private record OrdersPage(List<EbayOrder>? Orders, int Total);

    private record InventoryPage(List<InventoryItemDto>? InventoryItems, int Total);

    private record InventoryItemDto(string? Sku, InventoryProduct? Product, InventoryAvailability? Availability);

    private record InventoryProduct(string? Title);

    private record InventoryAvailability(ShipToLocationAvailability? ShipToLocationAvailability);

    private record ShipToLocationAvailability(int? Quantity);

    private record OffersPage(List<OfferDto>? Offers);

    private record OfferDto(string? MarketplaceId, int? AvailableQuantity, OfferPricing? PricingSummary, OfferListing? Listing);

    private record OfferPricing(EbayAmount? Price);

    private record OfferListing(string? ListingId, string? ListingStatus, int? SoldQuantity);
}
