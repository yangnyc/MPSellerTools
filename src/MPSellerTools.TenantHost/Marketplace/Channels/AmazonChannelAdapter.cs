using System.Globalization;
using System.Text.Json;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;

namespace MPSellerTools.TenantHost.Marketplace.Channels;

/// <summary>
/// Request bodies for Amazon's Listings Items API (2021-08-01) and the
/// JSON_LISTINGS_FEED, built with no network involved. Attribute names beyond
/// the handful written here depend on the product type; they come from the
/// listing's own attributes and are passed through under the name given.
/// </summary>
public static class AmazonPayloads
{
    /// <summary>
    /// The body of putListingsItem. With a known ASIN it is an offer on that
    /// catalog item (LISTING_OFFER_ONLY); without one it describes a new item.
    /// </summary>
    public static object PutListing(ListingSnapshot s)
    {
        var market = s.MarketplaceCode;
        var attributes = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["condition_type"] = Values(market, s.Condition switch
            {
                ItemCondition.Used => "used_good",
                ItemCondition.Refurbished => "refurbished_refurbished",
                _ => "new_new",
            }),
            ["purchasable_offer"] = PurchasableOffer(s),
        };
        if (s.FulfillmentMode == FulfillmentMode.Merchant)
        {
            attributes["fulfillment_availability"] = FulfillmentAvailability(s.Quantity);
        }

        if (s.ExistingCatalogItemId is { } asin)
        {
            attributes["merchant_suggested_asin"] = Values(market, asin);
            return new { productType = s.ExternalCategoryId ?? "PRODUCT", requirements = "LISTING_OFFER_ONLY", attributes };
        }

        if (s.Title is not null)
        {
            attributes["item_name"] = Values(market, s.Title);
        }
        if (s.Brand is not null)
        {
            attributes["brand"] = Values(market, s.Brand);
        }
        if (s.Description is not null)
        {
            attributes["product_description"] = Values(market, s.Description);
        }

        var identifier = new[] { ProductIdentifierType.Upc, ProductIdentifierType.Ean, ProductIdentifierType.Gtin, ProductIdentifierType.Isbn }
            .Where(s.Identifiers.ContainsKey)
            .Select(type => new { type = type.ToString().ToLowerInvariant(), value = s.Identifiers[type], marketplace_id = market })
            .FirstOrDefault();
        if (identifier is not null)
        {
            attributes["externally_assigned_product_identifier"] = new[] { identifier };
        }
        if (s.Identifiers.TryGetValue(ProductIdentifierType.Mpn, out var mpn))
        {
            attributes["part_number"] = Values(market, mpn);
        }

        for (var i = 0; i < s.ImageUrls.Count && i < 9; i++)
        {
            attributes[i == 0 ? "main_product_image_locator" : $"other_product_image_locator_{i}"] =
                new[] { new { media_location = s.ImageUrls[i], marketplace_id = market } };
        }

        foreach (var (name, value) in s.Options.Concat(s.Attributes))
        {
            attributes[name.ToLowerInvariant().Replace(' ', '_')] = Values(market, value);
        }

        if (s.Group is { } group)
        {
            // The child points at its parent; the parent itself holds no stock and is not a listing here.
            attributes["parentage_level"] = new[] { new { value = "child", marketplace_id = market } };
            attributes["child_parent_sku_relationship"] =
                new[] { new { child_relationship_type = "variation", parent_sku = group.GroupKey, marketplace_id = market } };
            attributes["variation_theme"] =
                new[] { new { name = string.Join("/", group.VariationAttributes).ToUpperInvariant(), marketplace_id = market } };
        }

        return new { productType = s.ExternalCategoryId, requirements = "LISTING", attributes };
    }

    /// <summary>The variation parent: product facts only, no offer, no quantity.</summary>
    public static object PutParent(ListingSnapshot s) => new
    {
        productType = s.ExternalCategoryId,
        requirements = "LISTING_PRODUCT_ONLY",
        attributes = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["item_name"] = Values(s.MarketplaceCode, s.Title ?? s.Group!.GroupKey),
            ["brand"] = Values(s.MarketplaceCode, s.Brand ?? ""),
            ["parentage_level"] = new[] { new { value = "parent", marketplace_id = s.MarketplaceCode } },
            ["child_parent_sku_relationship"] = new[] { new { child_relationship_type = "variation", marketplace_id = s.MarketplaceCode } },
            ["variation_theme"] =
                new[] { new { name = string.Join("/", s.Group!.VariationAttributes).ToUpperInvariant(), marketplace_id = s.MarketplaceCode } },
        },
    };

    public static object PatchPrice(ListingSnapshot s) => Patch(s, "purchasable_offer", PurchasableOffer(s));

    public static object PatchQuantity(ListingSnapshot s, int quantity) => Patch(s, "fulfillment_availability", FulfillmentAvailability(quantity));

    /// <summary>
    /// A JSON_LISTINGS_FEED (schema version 2.0) carrying one full update per
    /// listing, for sending many at once through the Feeds API.
    /// </summary>
    public static object ListingsFeed(string sellerId, IReadOnlyList<ListingSnapshot> snapshots) => new
    {
        header = new { sellerId, version = "2.0", issueLocale = "en_US" },
        messages = snapshots.Select((s, index) =>
        {
            var put = JsonSerializer.SerializeToElement(PutListing(s));
            return new
            {
                messageId = index + 1,
                sku = s.SellerSku,
                operationType = "UPDATE",
                productType = put.GetProperty("productType").GetString(),
                requirements = put.GetProperty("requirements").GetString(),
                attributes = put.GetProperty("attributes"),
            };
        }),
    };

    private static object Patch(ListingSnapshot s, string attribute, object value) => new
    {
        productType = s.ExternalCategoryId ?? "PRODUCT",
        patches = new[] { new { op = "replace", path = $"/attributes/{attribute}", value } },
    };

    private static object PurchasableOffer(ListingSnapshot s) => new[]
    {
        new
        {
            marketplace_id = s.MarketplaceCode,
            currency = s.Currency,
            our_price = new[] { new { schedule = new[] { new { value_with_tax = s.Price } } } },
        },
    };

    private static object FulfillmentAvailability(int quantity) =>
        new[] { new { fulfillment_channel_code = "DEFAULT", quantity } };

    private static object Values(string marketplaceId, string value) => new[] { new { value, marketplace_id = marketplaceId } };
}

/// <summary>
/// Amazon through the Selling Partner API. A submission is answered with
/// ACCEPTED long before the listing is buyable, so every write here ends as
/// "accepted" and the listing is only called live once getListingsItem
/// reports it BUYABLE.
/// </summary>
public class AmazonChannelAdapter(ChannelHttp http, ChannelSecrets secrets, ChannelTokenCache tokens) : ChannelAdapterBase
{
    private const string Name = "Amazon";
    private const string TokenUrl = "https://api.amazon.com/auth/o2/token";

    public override SalesChannel Channel => SalesChannel.Amazon;

    public override IReadOnlyList<ValidationIssue> Validate(ListingWork work, ChannelContext context)
    {
        var s = work.Snapshot;
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(context.Account.SellerId))
        {
            issues.Add(Issue("account.sellerId", "required", "The Amazon account needs its selling partner id."));
        }

        if (s.ExistingCatalogItemId is null)
        {
            if (string.IsNullOrWhiteSpace(s.ExternalCategoryId))
            {
                issues.Add(Issue("category", "required", "Map the product's category to an Amazon product type."));
            }

            // A new catalog item needs a real product identifier, or an exemption Amazon granted the seller.
            var hasIdentifier = s.Identifiers.Keys.Any(t => t != ProductIdentifierType.Mpn);
            if (!hasIdentifier && !s.Attributes.ContainsKey("supplier_declared_has_product_identifier_exemption"))
            {
                issues.Add(Issue("identifiers", "required",
                    "Amazon needs a UPC, EAN or GTIN to create a catalog item, or the ASIN of an existing one, or a recorded identifier exemption."));
            }
        }

        if (s.FulfillmentMode == FulfillmentMode.ChannelFulfilled && s.Quantity != 0)
        {
            issues.Add(Issue("quantity", "ownership", "FBA stock is Amazon's to report; no quantity is sent for it."));
        }
        return issues;
    }

    public override IReadOnlyList<ChannelRequest> Preview(SyncOperation operation, ListingWork work, ChannelContext context)
    {
        var s = work.Snapshot;
        var url = ItemUrl(context, s.SellerSku);
        return operation switch
        {
            SyncOperation.Content when s.Group is not null && s.ExistingCatalogItemId is null =>
            [
                new("PUT", ItemUrl(context, s.Group.GroupKey), AmazonPayloads.PutParent(s)),
                new("PUT", url, AmazonPayloads.PutListing(s)),
            ],
            SyncOperation.Content => [new("PUT", url, AmazonPayloads.PutListing(s))],
            SyncOperation.Price => [new("PATCH", url, AmazonPayloads.PatchPrice(s))],
            SyncOperation.Inventory => [new("PATCH", url, AmazonPayloads.PatchQuantity(s, s.Quantity))],
            SyncOperation.Deactivate => [new("PATCH", url, AmazonPayloads.PatchQuantity(s, 0))],
            _ => [],
        };
    }

    protected override async Task<OperationOutcome> ExecuteOneAsync(
        SyncOperation operation, ChannelContext context, ListingWork work, CancellationToken cancellationToken)
    {
        var s = work.Snapshot;
        if (operation is SyncOperation.Inventory or SyncOperation.Deactivate && s.FulfillmentMode == FulfillmentMode.ChannelFulfilled)
        {
            return OperationOutcome.Failed(SyncErrorClass.Permanent, "This listing is fulfilled by Amazon; its quantity is not managed from here.");
        }

        ChannelResponse response = default!;
        // PUT and PATCH address the listing by seller SKU, so sending one again after a lost answer replaces rather than duplicates.
        foreach (var request in Preview(operation, work, context))
        {
            response = await SendAsync(context, new HttpMethod(request.Method), request.Url, request.Body, cancellationToken);
            var status = Text(response.Body, "status");
            if (status != "ACCEPTED")
            {
                return new OperationOutcome(
                    OutcomeKind.Failed, SyncErrorClass.DataCorrection,
                    $"Amazon did not accept the submission for {s.SellerSku} ({status ?? "no status"}).",
                    SubmissionId: Text(response.Body, "submissionId"), RequestId: response.RequestId,
                    State: new RemoteState(ListingObservedStatus.Rejected, null, null, Issues(response.Body)));
            }
        }

        // ACCEPTED is Amazon taking the data, not the listing being for sale.
        return OperationOutcome.Accepted(Text(response.Body, "submissionId")) with { RequestId = response.RequestId };
    }

    public override async Task<OperationOutcome> GetStatusAsync(
        ChannelContext context, ListingWork work, string? submissionId, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{ItemUrl(context, work.Snapshot.SellerSku)}&includedData=summaries,issues,offers,fulfillmentAvailability";
            var response = await SendAsync(context, HttpMethod.Get, url, null, cancellationToken, 404);
            if (response.Status == 404)
            {
                return submissionId is null
                    ? OperationOutcome.Confirmed(new RemoteState(ListingObservedStatus.NotListed, null, null))
                    : OperationOutcome.Accepted(submissionId);
            }

            var body = response.Body;
            var issues = Issues(body);
            var summary = body.TryGetProperty("summaries", out var summaries) && summaries.ValueKind == JsonValueKind.Array
                ? summaries.EnumerateArray().FirstOrDefault(x => Text(x, "marketplaceId") == context.Market.MarketplaceCode)
                : default;
            if (summary.ValueKind != JsonValueKind.Object)
            {
                // Known to Amazon but with nothing to show yet: still being processed, unless it has already been turned down.
                return issues.Count > 0 && HasError(body)
                    ? OperationOutcome.Failed(SyncErrorClass.DataCorrection, "Amazon reported errors on the listing.", issues)
                    : OperationOutcome.Accepted(submissionId);
            }

            var statuses = summary.TryGetProperty("status", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Select(x => x.GetString()).ToList()
                : [];
            var status = statuses.Contains("BUYABLE") ? ListingObservedStatus.Live
                : HasError(body) ? ListingObservedStatus.Rejected
                : ListingObservedStatus.Inactive;

            decimal? price = null;
            if (body.TryGetProperty("offers", out var offers) && offers.ValueKind == JsonValueKind.Array)
            {
                var offer = offers.EnumerateArray().FirstOrDefault(o => Text(o, "offerType") is "B2C" or null);
                if (offer.ValueKind == JsonValueKind.Object && offer.TryGetProperty("price", out var amount) && amount.TryGetProperty("amount", out var value))
                {
                    price = value.ValueKind == JsonValueKind.Number
                        ? value.GetDecimal()
                        : decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
                }
            }

            int? quantity = null;
            if (body.TryGetProperty("fulfillmentAvailability", out var availability) && availability.ValueKind == JsonValueKind.Array)
            {
                var own = availability.EnumerateArray().FirstOrDefault(a => Text(a, "fulfillmentChannelCode") == "DEFAULT");
                if (own.ValueKind == JsonValueKind.Object && own.TryGetProperty("quantity", out var q) && q.ValueKind == JsonValueKind.Number)
                {
                    quantity = q.GetInt32();
                }
            }

            var references = Text(summary, "asin") is { } asin
                ? new Dictionary<ExternalResourceType, string> { [ExternalResourceType.CatalogItem] = asin }
                : null;
            return OperationOutcome.Confirmed(new RemoteState(status, price, quantity, issues), references);
        }
        catch (ChannelException ex)
        {
            return OperationOutcome.From(ex);
        }
    }

    /// <summary>Orders through searchOrders of the Orders API v2026-01-01 (v0 is deprecated and goes away in March 2027).</summary>
    public override async Task<IReadOnlyList<ChannelOrder>> GetOrdersAsync(ChannelContext context, DateTime sinceUtc, CancellationToken cancellationToken)
    {
        var orders = new List<ChannelOrder>();
        string? page = null;
        for (var i = 0; i < 50; i++)
        {
            var url = $"{BaseUrl(context)}/orders/2026-01-01/orders?marketplaceIds={Uri.EscapeDataString(context.Market.MarketplaceCode)}"
                + $"&lastUpdatedAfter={Uri.EscapeDataString(sinceUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))}"
                + "&includedData=FULFILLMENT&maxResultsPerPage=100"
                + (page is null ? "" : $"&paginationToken={Uri.EscapeDataString(page)}");
            var response = await SendAsync(context, HttpMethod.Get, url, null, cancellationToken);
            if (response.Body.TryGetProperty("orders", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                orders.AddRange(list.EnumerateArray().Select(ToOrder).OfType<ChannelOrder>());
            }

            page = response.Body.TryGetProperty("pagination", out var pagination) ? Text(pagination, "nextToken") : null;
            if (page is null)
            {
                break;
            }
        }
        return orders;
    }

    /// <summary>The product type's requirements from the Product Type Definitions API (2020-09-01).</summary>
    public override async Task<CategoryRequirements?> FetchRequirementsAsync(ChannelContext context, string externalCategoryId, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl(context)}/definitions/2020-09-01/productTypes/{Uri.EscapeDataString(externalCategoryId)}"
            + $"?marketplaceIds={Uri.EscapeDataString(context.Market.MarketplaceCode)}&requirements=LISTING&requirementsEnforced=ENFORCED";
        var response = await SendAsync(context, HttpMethod.Get, url, null, cancellationToken);

        // The rules themselves live in the JSON Schema the definition links to. That schema is not
        // interpreted here, so no required attributes are derived from it: what is recorded is which
        // definition and version applied, and Amazon's own validation remains the judge of the content.
        var schema = response.Body.TryGetProperty("schema", out var s) && s.TryGetProperty("link", out var link) ? Text(link, "resource") : null;
        var version = response.Body.TryGetProperty("productTypeVersion", out var v) ? Text(v, "version") : null;
        return new CategoryRequirements { Source = schema ?? url, Version = version };
    }

    /// <summary>
    /// Finds existing catalog items through searchCatalogItems of the Catalog
    /// Items API (2022-04-01), by keywords or, for a barcode, by identifier.
    /// A read: it changes nothing on Amazon.
    /// </summary>
    public async Task<IReadOnlyList<CatalogSearchResult>> SearchCatalogAsync(ChannelContext context, string query, CancellationToken cancellationToken)
    {
        var term = query.Trim();
        var barcode = term.All(char.IsAsciiDigit) ? term.Length switch { 12 => "UPC", 13 => "EAN", 14 => "GTIN", _ => null } : null;
        var url = $"{BaseUrl(context)}/catalog/2022-04-01/items?marketplaceIds={Uri.EscapeDataString(context.Market.MarketplaceCode)}"
            + (barcode is null
                ? $"&keywords={Uri.EscapeDataString(term)}"
                : $"&identifiers={Uri.EscapeDataString(term)}&identifiersType={barcode}")
            + "&includedData=summaries&pageSize=10";
        var response = await SendAsync(context, HttpMethod.Get, url, null, cancellationToken);
        if (!response.Body.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<CatalogSearchResult>();
        foreach (var item in items.EnumerateArray())
        {
            if (Text(item, "asin") is not { } asin)
            {
                continue;
            }

            var summary = item.TryGetProperty("summaries", out var summaries) && summaries.ValueKind == JsonValueKind.Array
                ? summaries.EnumerateArray().FirstOrDefault(x => Text(x, "marketplaceId") == context.Market.MarketplaceCode)
                : default;
            results.Add(new CatalogSearchResult(asin, Text(summary, "itemName"), Text(summary, "brand")));
        }
        return results;
    }

    private static ChannelOrder? ToOrder(JsonElement order)
    {
        var id = Text(order, "orderId");
        if (id is null)
        {
            return null;
        }

        var fulfillment = order.TryGetProperty("fulfillment", out var f) ? f : default;
        var status = Text(fulfillment, "fulfillmentStatus") switch
        {
            "SHIPPED" => OrderStatus.Completed,
            "CANCELLED" or "UNFULFILLABLE" => OrderStatus.Cancelled,
            "PARTIALLY_SHIPPED" => OrderStatus.InProgress,
            _ => OrderStatus.New,
        };

        var lines = new List<ChannelOrderLine>();
        string? currency = null;
        if (order.TryGetProperty("orderItems", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var product = item.TryGetProperty("product", out var p) ? p : default;
                var quantity = item.TryGetProperty("quantityOrdered", out var q) && q.ValueKind == JsonValueKind.Number ? q.GetInt32() : 0;
                var unitPrice = 0m;
                if (product.ValueKind == JsonValueKind.Object && product.TryGetProperty("price", out var price) && price.TryGetProperty("unitPrice", out var unit))
                {
                    currency ??= Text(unit, "currencyCode");
                    decimal.TryParse(Text(unit, "amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out unitPrice);
                }

                if (quantity > 0 && Text(item, "orderItemId") is { } lineId)
                {
                    lines.Add(new ChannelOrderLine(lineId, Text(product, "sellerSku"), Text(product, "title"), quantity, unitPrice));
                }
            }
        }

        var created = DateTime.TryParse(Text(order, "createdTime"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var at) ? at : (DateTime?)null;
        return new ChannelOrder(id, status, created, currency, Text(fulfillment, "fulfilledBy") == "AMAZON", lines);
    }

    private static List<ValidationIssue> Issues(JsonElement body) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array
            ? issues.EnumerateArray().Select(i => new ValidationIssue(
                SalesChannel.Amazon,
                i.TryGetProperty("attributeNames", out var names) && names.ValueKind == JsonValueKind.Array && names.GetArrayLength() > 0
                    ? $"attributes.{names[0].GetString()}"
                    : "listing",
                $"{Text(i, "severity") ?? "ERROR"}:{Text(i, "code") ?? "unknown"}",
                Text(i, "message") ?? "Amazon reported an issue.")).ToList()
            : [];

    private static bool HasError(JsonElement body) =>
        body.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array
        && issues.EnumerateArray().Any(i => Text(i, "severity") == "ERROR");

    private static string BaseUrl(ChannelContext context) =>
        context.Setting("endpoint") ?? (context.Account.Environment == ChannelEnvironment.Production
            ? "https://sellingpartnerapi-na.amazon.com"
            : "https://sandbox.sellingpartnerapi-na.amazon.com");

    private static string ItemUrl(ChannelContext context, string sku) =>
        $"{BaseUrl(context)}/listings/2021-08-01/items/{Uri.EscapeDataString(context.Account.SellerId ?? "")}/{Uri.EscapeDataString(sku)}"
        + $"?marketplaceIds={Uri.EscapeDataString(context.Market.MarketplaceCode)}&issueLocale=en_US";

    private async Task<ChannelResponse> SendAsync(
        ChannelContext context, HttpMethod method, string url, object? body, CancellationToken cancellationToken, params int[] alsoAccept)
    {
        var request = ChannelHttp.JsonRequest(method, url, body);
        request.Headers.TryAddWithoutValidation("x-amz-access-token", await TokenAsync(context, cancellationToken));
        try
        {
            return await http.SendAsync(ChannelHttp.AmazonClient, Name, request, cancellationToken, alsoAccept);
        }
        catch (ChannelException ex) when (ex.HttpStatus is 401 or 403)
        {
            tokens.Invalidate(context.Account.Id);
            throw;
        }
    }

    /// <summary>Login with Amazon: the seller's refresh token is exchanged for an access token that lasts an hour.</summary>
    private Task<string> TokenAsync(ChannelContext context, CancellationToken cancellationToken) =>
        tokens.GetAsync(context.Account.Id, async () =>
        {
            var credentials = secrets.Unprotect(context.Account);
            var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = ChannelSecrets.Required(credentials, "refreshToken"),
                    ["client_id"] = ChannelSecrets.Required(credentials, "clientId"),
                    ["client_secret"] = ChannelSecrets.Required(credentials, "clientSecret"),
                }),
            };
            ChannelResponse response;
            try
            {
                response = await http.SendAsync(ChannelHttp.AmazonClient, Name, request, cancellationToken);
            }
            catch (ChannelException ex) when (ex.HttpStatus is 400)
            {
                // Login with Amazon answers a bad or revoked grant with 400.
                throw new ChannelException(SyncErrorClass.Authorization, ex.Message, ex.HttpStatus);
            }

            var token = Text(response.Body, "access_token")
                ?? throw new ChannelException(SyncErrorClass.Authorization, "Amazon did not return an access token.");
            var lifetime = response.Body.TryGetProperty("expires_in", out var seconds) && seconds.ValueKind == JsonValueKind.Number ? seconds.GetInt32() : 3600;
            return (token, TimeSpan.FromSeconds(lifetime));
        }, cancellationToken);

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
