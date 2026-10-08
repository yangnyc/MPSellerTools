using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;

namespace MPSellerTools.TenantHost.Marketplace.Channels;

/// <summary>
/// Where a Magento store's REST API is and how it is called. Unlike the
/// marketplaces, a Magento store is the company's own site at an address of
/// its own, kept in the account's "baseUrl" setting, and every call carries
/// the access token of an integration created in the store's admin.
/// </summary>
public static class MagentoApi
{
    public const string Name = "Magento";

    /// <summary>The account's non-secret setting, or null.</summary>
    public static string? Setting(ChannelAccount account, string name)
    {
        if (string.IsNullOrWhiteSpace(account.SettingsJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(account.SettingsJson);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// The store's address without a trailing slash. It has to be a public
    /// https address: the server calls whatever is entered here, so it must
    /// not be pointed at this machine or the network behind it, and the
    /// access token must not travel unencrypted. The address of the API
    /// itself (".../rest/V1/") is taken for the store's.
    /// </summary>
    public static string Root(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ChannelException(SyncErrorClass.DataCorrection, "The Magento account needs its store address, starting with https://.");
        }
        if (uri.IsLoopback || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IsPrivate(address)))
        {
            throw new ChannelException(SyncErrorClass.DataCorrection, "The Magento store address must be a public one.");
        }

        var root = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        var api = root.IndexOf("/rest", uri.GetLeftPart(UriPartial.Authority).Length, StringComparison.OrdinalIgnoreCase);
        // Only a whole "/rest" segment, not a folder that merely starts with it.
        return api >= 0 && (api + 5 == root.Length || root[api + 5] == '/') ? root[..api] : root;
    }

    public static async Task<ChannelResponse> SendAsync(
        ChannelHttp http, ChannelSecrets secrets, ChannelAccount account, HttpMethod method, string url, object? body,
        CancellationToken cancellationToken, params int[] alsoAccept)
    {
        var request = ChannelHttp.JsonRequest(method, url, body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ChannelSecrets.Required(secrets.Unprotect(account), "accessToken"));
        return await http.SendAsync(ChannelHttp.MagentoClient, Name, request, cancellationToken, alsoAccept);
    }

    public static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>A number Magento may send either as a number or as text ("12.5000").</summary>
    public static decimal? Number(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] is 10 or 127 or 0
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254);
    }
}

/// <summary>The request bodies of Magento's catalog API, built from a listing with no network involved.</summary>
public static class MagentoPayloads
{
    /// <summary>The attribute set new products are filed under; 4 is "Default" on a fresh Magento.</summary>
    public static int AttributeSetId(ChannelContext context) =>
        int.TryParse(context.Setting("attributeSetId"), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 4;

    /// <summary>A simple product. It is enabled only when it is meant to be on sale; a draft is saved disabled.</summary>
    public static object Product(ListingSnapshot s, ChannelContext context, bool enabled)
    {
        var custom = new List<object>();
        if (!string.IsNullOrWhiteSpace(s.Description))
        {
            custom.Add(new { attribute_code = "description", value = s.Description });
        }
        // The listing's attributes go by Magento's own attribute codes.
        foreach (var (code, value) in s.Attributes.OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            custom.Add(new { attribute_code = code, value });
        }

        return new
        {
            product = new
            {
                sku = s.SellerSku,
                name = s.Title,
                price = s.Price,
                status = enabled ? 1 : 2,
                // Catalog and search.
                visibility = 4,
                type_id = "simple",
                attribute_set_id = AttributeSetId(context),
                weight = Weight(s, context),
                extension_attributes = new
                {
                    stock_item = Stock(s.Quantity),
                    category_links = int.TryParse(s.ExternalCategoryId, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                        ? new[] { new { category_id = s.ExternalCategoryId, position = 0 } }
                        : null,
                },
                custom_attributes = custom.Count > 0 ? custom : null,
            },
        };
    }

    public static object Price(ListingSnapshot s) => new { product = new { price = s.Price } };

    public static object Status(bool enabled) => new { product = new { status = enabled ? 1 : 2 } };

    public static object StockItem(int quantity) => new { stockItem = Stock(quantity) };

    private static object Stock(int quantity) => new { qty = quantity, is_in_stock = quantity > 0 };

    /// <summary>The weight in the store's own unit (the account's "weightUnit": lbs, the default, or kgs).</summary>
    private static decimal? Weight(ListingSnapshot s, ChannelContext context)
    {
        if (s.WeightValue is not { } value)
        {
            return null;
        }

        var kilograms = s.WeightUnit switch
        {
            "kg" => value,
            "g" => value / 1000m,
            "oz" => value * 0.028349523125m,
            _ => value * 0.45359237m,
        };
        return Math.Round(context.Setting("weightUnit") == "kgs" ? kilograms : kilograms / 0.45359237m, 4);
    }
}

/// <summary>
/// A Magento Open Source store through its REST API. A listing here is a
/// product in the store's catalog, kept under the same SKU: saving it creates
/// the product or updates the one already there, so nothing is sent twice.
/// Pictures are not sent; Magento's API takes them as file contents, not addresses.
/// </summary>
public class MagentoChannelAdapter(ChannelHttp http, ChannelSecrets secrets) : ChannelAdapterBase
{
    private const int PageSize = 100;
    private const int MaxPages = 50;

    public override SalesChannel Channel => SalesChannel.Magento;

    public override IReadOnlyList<ValidationIssue> Validate(ListingWork work, ChannelContext context)
    {
        var s = work.Snapshot;
        var issues = new List<ValidationIssue>();
        try
        {
            Root(context);
        }
        catch (ChannelException ex)
        {
            issues.Add(Issue("account.settings.baseUrl", "required", ex.Message));
        }
        if (s.SellerSku.Length > 64)
        {
            issues.Add(Issue("sellerSku", "length", "Magento accepts SKUs of up to 64 characters."));
        }
        if (s.Title is { Length: > 255 })
        {
            issues.Add(Issue("title", "length", "Magento accepts product names of up to 255 characters."));
        }
        if (!string.IsNullOrWhiteSpace(s.ExternalCategoryId) && !int.TryParse(s.ExternalCategoryId, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            issues.Add(Issue("category", "format", "A Magento category is its number, as shown in the store's admin."));
        }
        return issues;
    }

    public override IReadOnlyList<ChannelRequest> Preview(SyncOperation operation, ListingWork work, ChannelContext context)
    {
        var s = work.Snapshot;
        var root = Root(context);
        return operation switch
        {
            SyncOperation.Content => [new("POST", $"{root}/rest/all/V1/products", MagentoPayloads.Product(s, context, work.DesiredState == ListingDesiredState.Active))],
            SyncOperation.Price => [new("PUT", ProductUrl(root, s), MagentoPayloads.Price(s))],
            SyncOperation.Inventory => [new("PUT", StockUrl(root, s), MagentoPayloads.StockItem(s.Quantity))],
            SyncOperation.Deactivate => [new("PUT", ProductUrl(root, s), MagentoPayloads.Status(false))],
            _ => [],
        };
    }

    protected override async Task<OperationOutcome> ExecuteOneAsync(
        SyncOperation operation, ChannelContext context, ListingWork work, CancellationToken cancellationToken)
    {
        var s = work.Snapshot;
        var root = Root(context);
        switch (operation)
        {
            case SyncOperation.Content:
                // Saving by SKU creates the product or updates the one already there.
                var saved = await SendAsync(
                    context, HttpMethod.Post, $"{root}/rest/all/V1/products",
                    MagentoPayloads.Product(s, context, work.DesiredState == ListingDesiredState.Active), cancellationToken);
                return Confirmed(saved.Body, work.DesiredState);

            case SyncOperation.Price:
                await SendAsync(context, HttpMethod.Put, ProductUrl(root, s), MagentoPayloads.Price(s), cancellationToken);
                return OperationOutcome.Confirmed(new RemoteState(ListingObservedStatus.Live, s.Price, null));

            case SyncOperation.Inventory:
                await SendAsync(context, HttpMethod.Put, StockUrl(root, s), MagentoPayloads.StockItem(s.Quantity), cancellationToken);
                return OperationOutcome.Confirmed(new RemoteState(ListingObservedStatus.Live, null, s.Quantity));

            case SyncOperation.Deactivate:
                // 404: the product is already gone from the store, which is what was wanted.
                await SendAsync(context, HttpMethod.Put, ProductUrl(root, s), MagentoPayloads.Status(false), cancellationToken, 404);
                return OperationOutcome.Confirmed(new RemoteState(ListingObservedStatus.Inactive, null, null));

            default:
                return OperationOutcome.Failed(SyncErrorClass.Permanent, $"Magento has no {operation} operation here.");
        }
    }

    public override async Task<OperationOutcome> GetStatusAsync(
        ChannelContext context, ListingWork work, string? submissionId, CancellationToken cancellationToken)
    {
        try
        {
            var root = Root(context);
            var response = await SendAsync(context, HttpMethod.Get, ProductUrl(root, work.Snapshot), null, cancellationToken, 404);
            return response.Status == 404
                ? OperationOutcome.Confirmed(new RemoteState(ListingObservedStatus.NotListed, null, null))
                : Confirmed(response.Body, work.DesiredState);
        }
        catch (ChannelException ex)
        {
            return OperationOutcome.From(ex);
        }
    }

    public override async Task<IReadOnlyList<ChannelOrder>> GetOrdersAsync(ChannelContext context, DateTime sinceUtc, CancellationToken cancellationToken)
    {
        var root = Root(context);
        var since = Uri.EscapeDataString(sinceUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        var orders = new List<ChannelOrder>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var url = $"{root}/rest/V1/orders?searchCriteria[filter_groups][0][filters][0][field]=updated_at"
                + $"&searchCriteria[filter_groups][0][filters][0][value]={since}"
                + "&searchCriteria[filter_groups][0][filters][0][condition_type]=gteq"
                + $"&searchCriteria[pageSize]={PageSize}&searchCriteria[currentPage]={page}";
            var response = await SendAsync(context, HttpMethod.Get, url, null, cancellationToken);
            var items = response.Body.TryGetProperty("items", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [];
            orders.AddRange(items.Select(ToChannelOrder).OfType<ChannelOrder>());

            // Magento answers a page past the end with the last page again, so the total is what ends the loop.
            var total = (int)(MagentoApi.Number(response.Body, "total_count") ?? 0);
            if (items.Count < PageSize || page * PageSize >= total)
            {
                break;
            }
        }
        return orders;
    }

    private static ChannelOrder? ToChannelOrder(JsonElement order)
    {
        var number = MagentoApi.Text(order, "increment_id");
        if (number is null)
        {
            return null;
        }

        var lines = order.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray()
                // A configurable product is two rows: the parent carries the price, and its child row is left out.
                .Where(i => !i.TryGetProperty("parent_item_id", out var parent) || parent.ValueKind == JsonValueKind.Null)
                .Select(i => new ChannelOrderLine(
                    ((long)(MagentoApi.Number(i, "item_id") ?? 0)).ToString(CultureInfo.InvariantCulture),
                    MagentoApi.Text(i, "sku")?.Trim() is { Length: > 0 } sku ? sku : null,
                    MagentoApi.Text(i, "name"),
                    (int)(MagentoApi.Number(i, "qty_ordered") ?? 0),
                    MagentoApi.Number(i, "price") ?? 0m))
                .Where(l => l.Quantity > 0)
                .ToList()
            : [];

        return new ChannelOrder(
            number,
            MagentoApi.Text(order, "state") switch
            {
                "complete" => OrderStatus.Completed,
                "canceled" or "closed" => OrderStatus.Cancelled,
                "processing" => OrderStatus.InProgress,
                _ => OrderStatus.New,
            },
            // Magento keeps its times in UTC.
            DateTime.TryParseExact(
                MagentoApi.Text(order, "created_at"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var created) ? created : null,
            MagentoApi.Text(order, "order_currency_code"),
            FulfilledByChannel: false,
            lines);
    }

    /// <summary>What Magento's own answer for a product says about it: its number, whether it is enabled, its price and stock.</summary>
    private static OperationOutcome Confirmed(JsonElement product, ListingDesiredState desired)
    {
        var enabled = MagentoApi.Number(product, "status") == 1;
        var stock = product.TryGetProperty("extension_attributes", out var extension) && extension.TryGetProperty("stock_item", out var item) ? item : default;
        var state = new RemoteState(
            // A draft is saved disabled on purpose; that is not the same as being taken off sale.
            enabled ? ListingObservedStatus.Live : desired == ListingDesiredState.Draft ? ListingObservedStatus.NotListed : ListingObservedStatus.Inactive,
            MagentoApi.Number(product, "price"),
            MagentoApi.Number(stock, "qty") is { } quantity ? (int)quantity : null);
        var references = MagentoApi.Number(product, "id") is { } id
            ? new Dictionary<ExternalResourceType, string> { [ExternalResourceType.CatalogItem] = ((long)id).ToString(CultureInfo.InvariantCulture) }
            : null;
        return OperationOutcome.Confirmed(state, references);
    }

    private static string Root(ChannelContext context) => MagentoApi.Root(context.Setting("baseUrl"));

    private static string ProductUrl(string root, ListingSnapshot s) => $"{root}/rest/all/V1/products/{Uri.EscapeDataString(s.SellerSku)}";

    // The number at the end is the stock item's own id, which Magento finds by the SKU and ignores here.
    private static string StockUrl(string root, ListingSnapshot s) => $"{root}/rest/V1/products/{Uri.EscapeDataString(s.SellerSku)}/stockItems/1";

    private Task<ChannelResponse> SendAsync(
        ChannelContext context, HttpMethod method, string url, object? body, CancellationToken cancellationToken, params int[] alsoAccept) =>
        MagentoApi.SendAsync(http, secrets, context.Account, method, url, body, cancellationToken, alsoAccept);
}
