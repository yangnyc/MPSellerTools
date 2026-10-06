using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;

namespace MPSellerTools.TenantHost.Marketplace.Channels;

/// <summary>
/// Request bodies for Walmart Marketplace, built with no network involved.
/// The item feed's layout follows Walmart's item spec, which changes by
/// version and by product type: the version is taken from the account's
/// settings, never assumed, and the layout written here has to be checked
/// against the spec file for that version before live use (see
/// docs/marketplace-integrations.md).
/// </summary>
public static class WalmartPayloads
{
    /// <summary>An MP_ITEM feed: full setup of new seller-fulfilled items.</summary>
    public static object ItemFeed(IReadOnlyList<ListingSnapshot> items, string specVersion) => new
    {
        MPItemFeedHeader = new { businessUnit = "WALMART_US", locale = "en", version = specVersion },
        MPItem = items.Select(s => new
        {
            Orderable = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sku"] = s.SellerSku,
                ["productIdentifiers"] = Identifier(s),
                ["productName"] = s.Title,
                ["brand"] = s.Brand,
                ["price"] = s.Price,
                ["ShippingWeight"] = s.WeightValue,
            },
            Visible = new Dictionary<string, object>
            {
                [s.ExternalCategoryId ?? ""] = VisibleAttributes(s),
            },
        }),
    };

    /// <summary>An MP_ITEM_MATCH feed: an offer on an item already in Walmart's catalog, found by its identifier.</summary>
    public static object MatchFeed(IReadOnlyList<ListingSnapshot> items, string specVersion) => new
    {
        MPItemFeedHeader = new { businessUnit = "WALMART_US", locale = "en", version = specVersion },
        MPItem = items.Select(s => new
        {
            Item = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sku"] = s.SellerSku,
                ["productIdentifiers"] = Identifier(s),
                ["price"] = s.Price,
                ["ShippingWeight"] = s.WeightValue,
            },
        }),
    };

    public static object Price(ListingSnapshot s) => new
    {
        sku = s.SellerSku,
        pricing = new[] { new { currentPriceType = "BASE", currentPrice = new { currency = s.Currency, amount = s.Price } } },
    };

    public static object Inventory(ListingSnapshot s, int quantity) => new
    {
        sku = s.SellerSku,
        quantity = new { unit = "EACH", amount = quantity },
    };

    /// <summary>True when the listing is to be set up against an existing Walmart catalog item.</summary>
    public static bool IsMatch(ListingSnapshot s) =>
        s.ExistingCatalogItemId is not null || s.Attributes.GetValueOrDefault("walmart.setup") == "match";

    private static object? Identifier(ListingSnapshot s)
    {
        foreach (var type in new[] { ProductIdentifierType.Gtin, ProductIdentifierType.Upc, ProductIdentifierType.Ean, ProductIdentifierType.Isbn })
        {
            if (s.Identifiers.TryGetValue(type, out var value))
            {
                return new { productIdType = type.ToString().ToUpperInvariant(), productId = value };
            }
        }
        return null;
    }

    private static SortedDictionary<string, object?> VisibleAttributes(ListingSnapshot s)
    {
        var visible = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["shortDescription"] = s.Description,
            ["mainImageUrl"] = s.ImageUrls.FirstOrDefault(),
            ["productSecondaryImageURL"] = s.ImageUrls.Count > 1 ? s.ImageUrls.Skip(1).ToList() : null,
        };
        foreach (var (name, value) in s.Options.Concat(s.Attributes).Where(a => !a.Key.StartsWith("walmart.", StringComparison.Ordinal)))
        {
            visible[name] = value;
        }
        if (s.Group is { } group)
        {
            visible["variantGroupId"] = group.GroupKey;
            visible["variantAttributeNames"] = group.VariationAttributes;
        }
        return visible;
    }
}

/// <summary>
/// Walmart Marketplace. Items are set up through feeds, which Walmart works
/// through later and answers for each SKU on its own; a feed being taken, or
/// even a SKU being ingested, is not the item being published, so "live" is
/// read from the item itself.
/// </summary>
public class WalmartChannelAdapter(ChannelHttp http, ChannelSecrets secrets, ChannelTokenCache tokens) : ChannelAdapterBase
{
    private const string Name = "Walmart";

    public override SalesChannel Channel => SalesChannel.Walmart;

    /// <summary>Item feeds are heavily rate limited, so content goes many SKUs to a feed.</summary>
    public override int BatchSize(SyncOperation operation) => operation == SyncOperation.Content ? 50 : 1;

    public override IReadOnlyList<ValidationIssue> Validate(ListingWork work, ChannelContext context)
    {
        var s = work.Snapshot;
        var issues = new List<ValidationIssue>();
        if (!s.Identifiers.Keys.Any(t => t != ProductIdentifierType.Mpn))
        {
            issues.Add(Issue("identifiers", "required", "Walmart needs a GTIN, UPC, EAN or ISBN for every item."));
        }
        if (string.IsNullOrWhiteSpace(context.Setting("itemSpecVersion")))
        {
            issues.Add(Issue("account.settings.itemSpecVersion", "required",
                "Set the Walmart account's \"itemSpecVersion\" to the item spec version currently published by Walmart."));
        }
        if (!WalmartPayloads.IsMatch(s))
        {
            if (string.IsNullOrWhiteSpace(s.ExternalCategoryId))
            {
                issues.Add(Issue("category", "required", "Map the product's category to a Walmart product type."));
            }
            if (string.IsNullOrWhiteSpace(s.Brand))
            {
                issues.Add(Issue("brand", "required", "Walmart needs a brand to set up a new item."));
            }
            if (s.ImageUrls.Count == 0)
            {
                issues.Add(Issue("images", "required", "Walmart needs a main image to set up a new item."));
            }
        }
        if (s.FulfillmentMode == FulfillmentMode.ChannelFulfilled && s.Quantity != 0)
        {
            issues.Add(Issue("quantity", "ownership", "WFS stock is Walmart's to report; no quantity is sent for it."));
        }
        return issues;
    }

    public override IReadOnlyList<ChannelRequest> Preview(SyncOperation operation, ListingWork work, ChannelContext context)
    {
        var s = work.Snapshot;
        var root = BaseUrl(context);
        var version = context.Setting("itemSpecVersion") ?? "";
        return operation switch
        {
            SyncOperation.Content when WalmartPayloads.IsMatch(s) =>
                [new("POST", $"{root}/v3/feeds?feedType=MP_ITEM_MATCH", WalmartPayloads.MatchFeed([s], version))],
            SyncOperation.Content => [new("POST", $"{root}/v3/feeds?feedType=MP_ITEM", WalmartPayloads.ItemFeed([s], version))],
            SyncOperation.Price => [new("PUT", $"{root}/v3/price", WalmartPayloads.Price(s))],
            SyncOperation.Inventory => [new("PUT", $"{root}/v3/inventory?sku={Uri.EscapeDataString(s.SellerSku)}", WalmartPayloads.Inventory(s, s.Quantity))],
            SyncOperation.Deactivate => [new("DELETE", $"{root}/v3/items/{Uri.EscapeDataString(s.SellerSku)}", null)],
            _ => [],
        };
    }

    public override async Task<IReadOnlyList<OperationOutcome>> ExecuteAsync(
        SyncOperation operation, ChannelContext context, IReadOnlyList<ListingWork> items, CancellationToken cancellationToken)
    {
        if (operation != SyncOperation.Content)
        {
            return await base.ExecuteAsync(operation, context, items, cancellationToken);
        }

        var outcomes = new OperationOutcome[items.Count];
        var toSend = new List<int>();
        for (var i = 0; i < items.Count; i++)
        {
            try
            {
                // A feed cannot be recalled or sent twice safely. If the item is already known to Walmart
                // (an earlier feed whose answer was lost, say), its state is read instead of setting it up again.
                var known = items[i].References.ContainsKey(ExternalResourceType.CatalogItem)
                    ? null
                    : await ReadItemAsync(context, items[i].Snapshot.SellerSku, cancellationToken);
                if (known is { } existing)
                {
                    outcomes[i] = existing;
                }
                else
                {
                    toSend.Add(i);
                }
            }
            catch (ChannelException ex)
            {
                outcomes[i] = OperationOutcome.From(ex);
            }
        }

        var version = context.Setting("itemSpecVersion") ?? "";
        foreach (var group in toSend.GroupBy(i => WalmartPayloads.IsMatch(items[i].Snapshot)))
        {
            var snapshots = group.Select(i => items[i].Snapshot).ToList();
            try
            {
                var (feedType, feed) = group.Key
                    ? ("MP_ITEM_MATCH", WalmartPayloads.MatchFeed(snapshots, version))
                    : ("MP_ITEM", WalmartPayloads.ItemFeed(snapshots, version));
                var response = await SendFeedAsync(context, feedType, feed, cancellationToken);
                var feedId = Text(response.Body, "feedId")
                    ?? throw new ChannelException(SyncErrorClass.Transient, "Walmart took a feed but did not say which.", ambiguous: true);
                foreach (var i in group)
                {
                    outcomes[i] = OperationOutcome.Accepted(feedId) with { RequestId = response.RequestId };
                }
            }
            catch (ChannelException ex)
            {
                foreach (var i in group)
                {
                    outcomes[i] = OperationOutcome.From(ex);
                }
            }
        }

        return outcomes;
    }

    protected override async Task<OperationOutcome> ExecuteOneAsync(
        SyncOperation operation, ChannelContext context, ListingWork work, CancellationToken cancellationToken)
    {
        var s = work.Snapshot;
        var root = BaseUrl(context);
        switch (operation)
        {
            case SyncOperation.Price:
                await SendAsync(context, HttpMethod.Put, $"{root}/v3/price", WalmartPayloads.Price(s), cancellationToken);
                return OperationOutcome.Confirmed(new RemoteState(ListingObservedStatus.Live, s.Price, null));

            case SyncOperation.Inventory when s.FulfillmentMode == FulfillmentMode.ChannelFulfilled:
                return OperationOutcome.Failed(SyncErrorClass.Permanent, "This listing is fulfilled by Walmart; its quantity is not managed from here.");

            case SyncOperation.Inventory:
                await SendAsync(
                    context, HttpMethod.Put, $"{root}/v3/inventory?sku={Uri.EscapeDataString(s.SellerSku)}", WalmartPayloads.Inventory(s, s.Quantity), cancellationToken);
                return OperationOutcome.Confirmed(new RemoteState(ListingObservedStatus.Live, null, s.Quantity));

            case SyncOperation.Deactivate:
                await SendAsync(context, HttpMethod.Delete, $"{root}/v3/items/{Uri.EscapeDataString(s.SellerSku)}", null, cancellationToken, 404);
                return OperationOutcome.Confirmed(new RemoteState(ListingObservedStatus.Inactive, null, 0));

            default:
                return OperationOutcome.Failed(SyncErrorClass.Permanent, $"Walmart has no {operation} operation here.");
        }
    }

    public override async Task<OperationOutcome> GetStatusAsync(
        ChannelContext context, ListingWork work, string? submissionId, CancellationToken cancellationToken)
    {
        try
        {
            var sku = work.Snapshot.SellerSku;
            if (submissionId is not null)
            {
                var feed = await SendAsync(
                    context, HttpMethod.Get, $"{BaseUrl(context)}/v3/feeds/{Uri.EscapeDataString(submissionId)}?includeDetails=true&limit=1000", null, cancellationToken);
                var item = feed.Body.TryGetProperty("itemDetails", out var details)
                    && details.TryGetProperty("itemIngestionStatus", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().FirstOrDefault(x => Text(x, "sku") == sku)
                    : default;
                switch (Text(item, "ingestionStatus"))
                {
                    case "DATA_ERROR":
                        return OperationOutcome.Failed(SyncErrorClass.DataCorrection, $"Walmart found errors in {sku}.", IngestionIssues(item));
                    case "SYSTEM_ERROR" or "TIMEOUT_ERROR":
                        // Walmart's own failure for this one SKU: it alone is worth sending again.
                        return OperationOutcome.Failed(SyncErrorClass.Transient, $"Walmart could not process {sku} and asks for it again.");
                    case "SUCCESS":
                        break;
                    default:
                        return Text(feed.Body, "feedStatus") == "ERROR"
                            ? OperationOutcome.Failed(SyncErrorClass.Transient, "Walmart could not process the feed.")
                            : OperationOutcome.Accepted(submissionId);
                }
            }

            // Ingested is not published: only the item's own published status says buyers can see it.
            return await ReadItemAsync(context, sku, cancellationToken)
                ?? (submissionId is null
                    ? OperationOutcome.Confirmed(new RemoteState(ListingObservedStatus.NotListed, null, null))
                    : OperationOutcome.Accepted(submissionId));
        }
        catch (ChannelException ex)
        {
            return OperationOutcome.From(ex);
        }
    }

    public override async Task<IReadOnlyList<ChannelOrder>> GetOrdersAsync(ChannelContext context, DateTime sinceUtc, CancellationToken cancellationToken)
    {
        var orders = new List<ChannelOrder>();
        var url = $"{BaseUrl(context)}/v3/orders?createdStartDate={Uri.EscapeDataString(sinceUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))}&limit=200";
        for (var page = 0; page < 50; page++)
        {
            var response = await SendAsync(context, HttpMethod.Get, url, null, cancellationToken, 404);
            if (response.Status == 404 || !response.Body.TryGetProperty("list", out var list))
            {
                break;
            }

            if (list.TryGetProperty("elements", out var elements) && elements.TryGetProperty("order", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                orders.AddRange(items.EnumerateArray().Select(ToOrder).OfType<ChannelOrder>());
            }

            var next = list.TryGetProperty("meta", out var meta) ? Text(meta, "nextCursor") : null;
            if (string.IsNullOrEmpty(next))
            {
                break;
            }
            url = $"{BaseUrl(context)}/v3/orders{next}";
        }
        return orders;
    }

    private static ChannelOrder? ToOrder(JsonElement order)
    {
        var id = Text(order, "purchaseOrderId");
        if (id is null)
        {
            return null;
        }

        var lines = new List<ChannelOrderLine>();
        var statuses = new List<string>();
        string? currency = null;
        if (order.TryGetProperty("orderLines", out var orderLines) && orderLines.TryGetProperty("orderLine", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in list.EnumerateArray())
            {
                var item = line.TryGetProperty("item", out var i) ? i : default;
                var quantity = line.TryGetProperty("orderLineQuantity", out var q) && int.TryParse(Text(q, "amount"), out var parsed) ? parsed : 0;
                var unitPrice = 0m;
                if (line.TryGetProperty("charges", out var charges) && charges.TryGetProperty("charge", out var chargeList) && chargeList.ValueKind == JsonValueKind.Array)
                {
                    var product = chargeList.EnumerateArray().FirstOrDefault(c => Text(c, "chargeType") == "PRODUCT");
                    if (product.ValueKind == JsonValueKind.Object && product.TryGetProperty("chargeAmount", out var amount))
                    {
                        currency ??= Text(amount, "currency");
                        unitPrice = amount.TryGetProperty("amount", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : 0m;
                    }
                }

                if (line.TryGetProperty("orderLineStatuses", out var lineStatuses) && lineStatuses.TryGetProperty("orderLineStatus", out var statusList)
                    && statusList.ValueKind == JsonValueKind.Array)
                {
                    statuses.AddRange(statusList.EnumerateArray().Select(x => Text(x, "status")).OfType<string>());
                }

                if (quantity > 0 && Text(line, "lineNumber") is { } lineNumber)
                {
                    lines.Add(new ChannelOrderLine(lineNumber, Text(item, "sku"), Text(item, "productName"), quantity, unitPrice));
                }
            }
        }

        var status = statuses.Count > 0 && statuses.All(x => x == "Cancelled") ? OrderStatus.Cancelled
            : statuses.Count > 0 && statuses.All(x => x is "Shipped" or "Delivered" or "Cancelled") ? OrderStatus.Completed
            : statuses.Any(x => x is "Acknowledged" or "Shipped") ? OrderStatus.InProgress
            : OrderStatus.New;
        DateTime? created = order.TryGetProperty("orderDate", out var date) && date.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeMilliseconds(date.GetInt64()).UtcDateTime
            : null;
        return new ChannelOrder(id, status, created, currency, Text(order, "shipNode") is "WFSFulfilled", lines);
    }

    /// <summary>The item as Walmart has it, or null when Walmart does not know the SKU.</summary>
    private async Task<OperationOutcome?> ReadItemAsync(ChannelContext context, string sku, CancellationToken cancellationToken)
    {
        var response = await SendAsync(context, HttpMethod.Get, $"{BaseUrl(context)}/v3/items/{Uri.EscapeDataString(sku)}", null, cancellationToken, 404);
        var item = response.Status != 404 && response.Body.TryGetProperty("ItemResponse", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0
            ? list[0]
            : default;
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var status = Text(item, "publishedStatus") switch
        {
            "PUBLISHED" => ListingObservedStatus.Live,
            "UNPUBLISHED" => Text(item, "lifecycleStatus") is "RETIRED" or "ARCHIVED" ? ListingObservedStatus.Inactive : ListingObservedStatus.Rejected,
            "SYSTEM_PROBLEM" => ListingObservedStatus.Rejected,
            _ => ListingObservedStatus.Processing,
        };
        decimal? price = item.TryGetProperty("price", out var p) && p.TryGetProperty("amount", out var amount) && amount.ValueKind == JsonValueKind.Number
            ? amount.GetDecimal()
            : null;
        var references = Text(item, "wpid") is { } wpid ? new Dictionary<ExternalResourceType, string> { [ExternalResourceType.CatalogItem] = wpid } : null;
        if (status == ListingObservedStatus.Processing)
        {
            return OperationOutcome.Accepted(null, references);
        }

        var reasons = item.TryGetProperty("unpublishedReasons", out var r) && r.TryGetProperty("reason", out var reasonList) && reasonList.ValueKind == JsonValueKind.Array
            ? reasonList.EnumerateArray().Select(x => new ValidationIssue(SalesChannel.Walmart, "listing", "unpublished", x.GetString() ?? "")).ToList()
            : null;
        return OperationOutcome.Confirmed(new RemoteState(status, price, null, reasons), references);
    }

    private static List<ValidationIssue> IngestionIssues(JsonElement item) =>
        item.TryGetProperty("ingestionErrors", out var errors) && errors.TryGetProperty("ingestionError", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(e => new ValidationIssue(
                SalesChannel.Walmart,
                Text(e, "field") is { Length: > 0 } field ? $"attributes.{field}" : "listing",
                Text(e, "code") ?? "DATA_ERROR",
                Text(e, "description") ?? "Walmart reported a data error.")).ToList()
            : [new ValidationIssue(SalesChannel.Walmart, "listing", "DATA_ERROR", "Walmart reported a data error without details.")];

    private static string BaseUrl(ChannelContext context) =>
        context.Account.Environment == ChannelEnvironment.Production ? "https://marketplace.walmartapis.com" : "https://sandbox.walmartapis.com";

    private async Task<ChannelResponse> SendFeedAsync(ChannelContext context, string feedType, object feed, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl(context)}/v3/feeds?feedType={feedType}");
        var file = new StringContent(ChannelHttp.Serialize(feed), Encoding.UTF8, "application/json");
        request.Content = new MultipartFormDataContent { { file, "file", "feed.json" } };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await SendAsync(context, request, cancellationToken);
    }

    private Task<ChannelResponse> SendAsync(
        ChannelContext context, HttpMethod method, string url, object? body, CancellationToken cancellationToken, params int[] alsoAccept) =>
        SendAsync(context, ChannelHttp.JsonRequest(method, url, body), cancellationToken, alsoAccept);

    private async Task<ChannelResponse> SendAsync(ChannelContext context, HttpRequestMessage request, CancellationToken cancellationToken, params int[] alsoAccept)
    {
        AddServiceHeaders(request);
        request.Headers.TryAddWithoutValidation("WM_SEC.ACCESS_TOKEN", await TokenAsync(context, cancellationToken));
        try
        {
            return await http.SendAsync(ChannelHttp.WalmartClient, Name, request, cancellationToken, alsoAccept);
        }
        catch (ChannelException ex) when (ex.HttpStatus == 401)
        {
            tokens.Invalidate(context.Account.Id);
            throw;
        }
    }

    private static void AddServiceHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("WM_SVC.NAME", "Walmart Marketplace");
        request.Headers.TryAddWithoutValidation("WM_QOS.CORRELATION_ID", Guid.NewGuid().ToString());
    }

    /// <summary>Client-credentials grant; Walmart's tokens last fifteen minutes.</summary>
    private Task<string> TokenAsync(ChannelContext context, CancellationToken cancellationToken) =>
        tokens.GetAsync(context.Account.Id, async () =>
        {
            var credentials = secrets.Unprotect(context.Account);
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{ChannelSecrets.Required(credentials, "clientId")}:{ChannelSecrets.Required(credentials, "clientSecret")}"));
            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl(context)}/v3/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            AddServiceHeaders(request);

            ChannelResponse response;
            try
            {
                response = await http.SendAsync(ChannelHttp.WalmartClient, Name, request, cancellationToken);
            }
            catch (ChannelException ex) when (ex.HttpStatus is 400)
            {
                throw new ChannelException(SyncErrorClass.Authorization, ex.Message, ex.HttpStatus);
            }

            var token = Text(response.Body, "access_token")
                ?? throw new ChannelException(SyncErrorClass.Authorization, "Walmart did not return an access token.");
            var lifetime = response.Body.TryGetProperty("expires_in", out var seconds) && seconds.ValueKind == JsonValueKind.Number ? seconds.GetInt32() : 900;
            return (token, TimeSpan.FromSeconds(lifetime));
        }, cancellationToken);

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>
/// The company's own website. Publishing is local: there is nothing to call,
/// so an operation is confirmed as soon as it is carried out, and the
/// storefront projection simply reads the listings this marks as live.
/// </summary>
public class WebsiteChannelAdapter : ChannelAdapterBase
{
    public override SalesChannel Channel => SalesChannel.Website;

    public override IReadOnlyList<ValidationIssue> Validate(ListingWork work, ChannelContext context) => [];

    public override IReadOnlyList<ChannelRequest> Preview(SyncOperation operation, ListingWork work, ChannelContext context) =>
        [new("LOCAL", $"storefront/{work.Snapshot.SellerSku}", new { operation = operation.ToString(), work.Snapshot.Title, work.Snapshot.Price, work.Snapshot.Quantity })];

    protected override Task<OperationOutcome> ExecuteOneAsync(
        SyncOperation operation, ChannelContext context, ListingWork work, CancellationToken cancellationToken) =>
        Task.FromResult(OperationOutcome.Confirmed(StateOf(work, operation == SyncOperation.Deactivate)));

    public override Task<OperationOutcome> GetStatusAsync(
        ChannelContext context, ListingWork work, string? submissionId, CancellationToken cancellationToken) =>
        Task.FromResult(OperationOutcome.Confirmed(StateOf(work, false)));

    private static RemoteState StateOf(ListingWork work, bool deactivated) => new(
        deactivated || work.DesiredState != ListingDesiredState.Active ? ListingObservedStatus.Inactive : ListingObservedStatus.Live,
        work.Snapshot.Price,
        work.Snapshot.Quantity);
}
