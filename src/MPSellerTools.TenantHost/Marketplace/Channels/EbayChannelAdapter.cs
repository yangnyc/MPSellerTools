using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Marketplace.Channels;

/// <summary>The request bodies of eBay's Inventory API, built from a listing with no network involved.</summary>
public static class EbayPayloads
{
    public static object InventoryItem(ListingSnapshot s)
    {
        var aspects = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (name, value) in s.Attributes.Concat(s.Options))
        {
            aspects[name] = [value];
        }
        if (!string.IsNullOrWhiteSpace(s.Brand))
        {
            aspects["Brand"] = [s.Brand];
        }

        return new
        {
            availability = new { shipToLocationAvailability = new { quantity = s.Quantity } },
            condition = s.Condition switch
            {
                ItemCondition.Used => "USED_GOOD",
                ItemCondition.Refurbished => "SELLER_REFURBISHED",
                _ => "NEW",
            },
            product = new
            {
                title = s.Title,
                description = s.Description,
                brand = s.Brand,
                mpn = s.Identifiers.GetValueOrDefault(ProductIdentifierType.Mpn),
                upc = One(s.Identifiers.GetValueOrDefault(ProductIdentifierType.Upc) ?? s.Identifiers.GetValueOrDefault(ProductIdentifierType.Gtin)),
                ean = One(s.Identifiers.GetValueOrDefault(ProductIdentifierType.Ean)),
                isbn = One(s.Identifiers.GetValueOrDefault(ProductIdentifierType.Isbn)),
                aspects,
                imageUrls = s.ImageUrls.Count > 0 ? s.ImageUrls : null,
            },
            packageWeightAndSize = s.WeightValue is null && s.Length is null ? null : new
            {
                weight = s.WeightValue is { } weight ? new { value = weight, unit = WeightUnit(s.WeightUnit) } : null,
                dimensions = s.Length is { } length && s.Width is { } width && s.Height is { } height
                    ? new { length, width, height, unit = s.DimensionUnit == "cm" ? "CENTIMETER" : "INCH" }
                    : null,
            },
        };
    }

    public static object Offer(ListingSnapshot s, ChannelContext context) => new
    {
        sku = s.SellerSku,
        marketplaceId = s.MarketplaceCode,
        format = "FIXED_PRICE",
        availableQuantity = s.Quantity,
        categoryId = s.ExternalCategoryId,
        listingDescription = s.Description,
        merchantLocationKey = context.Setting("merchantLocationKey"),
        pricingSummary = new { price = Amount(s.Price, s.Currency) },
        listingPolicies = new
        {
            fulfillmentPolicyId = context.Setting("fulfillmentPolicyId"),
            paymentPolicyId = context.Setting("paymentPolicyId"),
            returnPolicyId = context.Setting("returnPolicyId"),
        },
    };

    public static object ItemGroup(ListingSnapshot s) => new
    {
        title = s.Title,
        description = s.Description,
        imageUrls = s.ImageUrls.Count > 0 ? s.ImageUrls : null,
        variantSKUs = s.Group!.MemberSkus,
        variesBy = new
        {
            specifications = s.Group.VariationAttributes
                .Select(name => new { name, values = s.Group.VariationValues.GetValueOrDefault(name) ?? [] }),
        },
    };

    /// <summary>One bulkUpdatePriceQuantity request; a null price or quantity leaves that side alone.</summary>
    public static object PriceQuantity(IEnumerable<(ListingSnapshot Snapshot, string OfferId)> items, bool price, bool quantity) => new
    {
        requests = items.Select(i => new
        {
            sku = i.Snapshot.SellerSku,
            shipToLocationAvailability = quantity ? new { quantity = i.Snapshot.Quantity } : null,
            offers = new[]
            {
                new
                {
                    offerId = i.OfferId,
                    availableQuantity = quantity ? i.Snapshot.Quantity : (int?)null,
                    price = price ? Amount(i.Snapshot.Price, i.Snapshot.Currency) : null,
                },
            },
        }),
    };

    private static object Amount(decimal value, string currency) =>
        new { value = value.ToString("0.00", CultureInfo.InvariantCulture), currency };

    private static string[]? One(string? value) => string.IsNullOrWhiteSpace(value) ? null : [value];

    private static string WeightUnit(string? unit) => unit switch
    {
        "oz" => "OUNCE",
        "kg" => "KILOGRAM",
        "g" => "GRAM",
        _ => "POUND",
    };
}

/// <summary>
/// eBay through the Inventory API: inventory item, then offer, then publish.
/// It manages only what that API created; a listing made on the eBay site or
/// through the older Trading API is left to the read-only import.
/// </summary>
public class EbayChannelAdapter(TenantDbContext db, ChannelHttp http, EbayClient ebay, EbaySync secrets, ChannelTokenCache tokens)
    : ChannelAdapterBase
{
    private const string Name = "eBay";

    public override SalesChannel Channel => SalesChannel.Ebay;

    /// <summary>eBay's bulkUpdatePriceQuantity takes up to 25 offers at a time.</summary>
    public override int BatchSize(SyncOperation operation) =>
        operation is SyncOperation.Price or SyncOperation.Inventory ? 25 : 1;

    public override IReadOnlyList<ValidationIssue> Validate(ListingWork work, ChannelContext context)
    {
        var s = work.Snapshot;
        var issues = new List<ValidationIssue>();
        if (s.SellerSku.Length > 50)
        {
            issues.Add(Issue("sellerSku", "length", "eBay accepts SKUs of up to 50 characters."));
        }
        if (s.Title is { Length: > 80 })
        {
            issues.Add(Issue("title", "length", "eBay accepts titles of up to 80 characters."));
        }
        if (string.IsNullOrWhiteSpace(s.Description))
        {
            issues.Add(Issue("description", "required", "eBay needs a listing description."));
        }
        if (string.IsNullOrWhiteSpace(s.ExternalCategoryId))
        {
            issues.Add(Issue("category", "required", "Map the product's category to an eBay category id."));
        }
        if (s.ImageUrls.Count == 0)
        {
            issues.Add(Issue("images", "required", "eBay needs at least one image."));
        }
        foreach (var setting in new[] { "merchantLocationKey", "fulfillmentPolicyId", "paymentPolicyId", "returnPolicyId" })
        {
            if (string.IsNullOrWhiteSpace(context.Setting(setting)))
            {
                issues.Add(Issue($"account.settings.{setting}", "required", $"The eBay account needs \"{setting}\" in its settings."));
            }
        }
        return issues;
    }

    public override IReadOnlyList<ChannelRequest> Preview(SyncOperation operation, ListingWork work, ChannelContext context)
    {
        var s = work.Snapshot;
        var root = $"{EbayClient.ApiBase(Environment(context))}/sell/inventory/v1";
        var offerId = work.References.GetValueOrDefault(ExternalResourceType.Offer) ?? "{offerId}";
        return operation switch
        {
            SyncOperation.Content => work.DesiredState != ListingDesiredState.Active
                ?
                [
                    new("PUT", $"{root}/inventory_item/{Uri.EscapeDataString(s.SellerSku)}", EbayPayloads.InventoryItem(s)),
                    new("POST", $"{root}/offer", EbayPayloads.Offer(s, context)),
                ]
                : s.Group is null
                ?
                [
                    new("PUT", $"{root}/inventory_item/{Uri.EscapeDataString(s.SellerSku)}", EbayPayloads.InventoryItem(s)),
                    new("POST", $"{root}/offer", EbayPayloads.Offer(s, context)),
                    new("POST", $"{root}/offer/{offerId}/publish", null),
                ]
                :
                [
                    new("PUT", $"{root}/inventory_item/{Uri.EscapeDataString(s.SellerSku)}", EbayPayloads.InventoryItem(s)),
                    new("POST", $"{root}/offer", EbayPayloads.Offer(s, context)),
                    new("PUT", $"{root}/inventory_item_group/{Uri.EscapeDataString(s.Group.GroupKey)}", EbayPayloads.ItemGroup(s)),
                    new("POST", $"{root}/offer/publish_by_inventory_item_group",
                        new { inventoryItemGroupKey = s.Group.GroupKey, marketplaceId = s.MarketplaceCode }),
                ],
            SyncOperation.Price => [new("POST", $"{root}/bulk_update_price_quantity", EbayPayloads.PriceQuantity([(s, offerId)], true, false))],
            SyncOperation.Inventory => [new("POST", $"{root}/bulk_update_price_quantity", EbayPayloads.PriceQuantity([(s, offerId)], false, true))],
            SyncOperation.Deactivate => [new("POST", $"{root}/offer/{offerId}/withdraw", null)],
            _ => [],
        };
    }

    public override async Task<IReadOnlyList<OperationOutcome>> ExecuteAsync(
        SyncOperation operation, ChannelContext context, IReadOnlyList<ListingWork> items, CancellationToken cancellationToken)
    {
        if (operation is not (SyncOperation.Price or SyncOperation.Inventory))
        {
            return await base.ExecuteAsync(operation, context, items, cancellationToken);
        }

        // Price and quantity go out together for up to 25 offers; eBay answers for each offer on its own.
        var outcomes = new OperationOutcome[items.Count];
        var sendable = new List<(int Index, ListingSnapshot Snapshot, string OfferId)>();
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].References.TryGetValue(ExternalResourceType.Offer, out var offerId))
            {
                sendable.Add((i, items[i].Snapshot, offerId));
            }
            else
            {
                outcomes[i] = OperationOutcome.Failed(SyncErrorClass.DataCorrection, "This listing has no eBay offer yet; publish it first.");
            }
        }

        if (sendable.Count > 0)
        {
            try
            {
                var body = EbayPayloads.PriceQuantity(
                    sendable.Select(x => (x.Snapshot, x.OfferId)), operation == SyncOperation.Price, operation == SyncOperation.Inventory);
                var response = await SendAsync(context, HttpMethod.Post, "/sell/inventory/v1/bulk_update_price_quantity", body, cancellationToken);
                var answers = response.Body.TryGetProperty("responses", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().ToList()
                    : [];
                foreach (var (index, snapshot, offerId) in sendable)
                {
                    var answer = answers.FirstOrDefault(a => Text(a, "offerId") == offerId || Text(a, "sku") == snapshot.SellerSku);
                    var status = answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty("statusCode", out var code) ? code.GetInt32() : 0;
                    outcomes[index] = status is >= 200 and < 300
                        ? OperationOutcome.Confirmed(new RemoteState(
                            ListingObservedStatus.Live,
                            operation == SyncOperation.Price ? snapshot.Price : null,
                            operation == SyncOperation.Inventory ? snapshot.Quantity : null))
                        : new OperationOutcome(
                            OutcomeKind.Failed,
                            status == 0 ? SyncErrorClass.Transient : ChannelHttp.Classify(status),
                            $"eBay refused the update for {snapshot.SellerSku} ({status}): {FirstError(answer)}",
                            HttpStatus: status == 0 ? null : status);
                }
            }
            catch (ChannelException ex)
            {
                foreach (var (index, _, _) in sendable)
                {
                    outcomes[index] = OperationOutcome.From(ex);
                }
            }
        }

        return outcomes;
    }

    protected override async Task<OperationOutcome> ExecuteOneAsync(
        SyncOperation operation, ChannelContext context, ListingWork work, CancellationToken cancellationToken)
    {
        var s = work.Snapshot;
        if (operation == SyncOperation.Deactivate)
        {
            if (!work.References.TryGetValue(ExternalResourceType.Offer, out var existingOffer))
            {
                return OperationOutcome.Confirmed(new RemoteState(ListingObservedStatus.NotListed, null, null));
            }

            // 404: the offer is already gone, which is what was wanted.
            await SendAsync(context, HttpMethod.Post, $"/sell/inventory/v1/offer/{Uri.EscapeDataString(existingOffer)}/withdraw", null, cancellationToken, 404);
            return OperationOutcome.Confirmed(new RemoteState(ListingObservedStatus.Inactive, null, 0));
        }

        if (operation != SyncOperation.Content)
        {
            return OperationOutcome.Failed(SyncErrorClass.Permanent, $"eBay has no {operation} operation here.");
        }

        await SendAsync(context, HttpMethod.Put, $"/sell/inventory/v1/inventory_item/{Uri.EscapeDataString(s.SellerSku)}", EbayPayloads.InventoryItem(s), cancellationToken);

        // Look before creating: after a create whose answer never arrived, the
        // offer may already exist, and a second create must not be sent blind.
        var found = await FindOfferAsync(context, s, cancellationToken);
        string offerId;
        if (found is { } offer)
        {
            offerId = Text(offer, "offerId")!;
            await SendAsync(context, HttpMethod.Put, $"/sell/inventory/v1/offer/{Uri.EscapeDataString(offerId)}", EbayPayloads.Offer(s, context), cancellationToken);
        }
        else
        {
            var created = await SendAsync(context, HttpMethod.Post, "/sell/inventory/v1/offer", EbayPayloads.Offer(s, context), cancellationToken);
            offerId = Text(created.Body, "offerId")
                ?? throw new ChannelException(SyncErrorClass.Transient, "eBay created an offer but did not say which.", ambiguous: true);
        }

        var references = new Dictionary<ExternalResourceType, string> { [ExternalResourceType.Offer] = offerId };
        var published = found is { } existing && Text(existing, "status") == "PUBLISHED";
        string? groupListingId = null;
        if (work.DesiredState == ListingDesiredState.Active && !published)
        {
            if (s.Group is null)
            {
                await SendAsync(context, HttpMethod.Post, $"/sell/inventory/v1/offer/{Uri.EscapeDataString(offerId)}/publish", null, cancellationToken);
            }
            else if (!work.GroupReady)
            {
                // The group goes live as one listing, once every variant in it has its offer.
                return OperationOutcome.Confirmed(new RemoteState(ListingObservedStatus.NotListed, null, null), references);
            }
            else
            {
                await SendAsync(context, HttpMethod.Put, $"/sell/inventory/v1/inventory_item_group/{Uri.EscapeDataString(s.Group.GroupKey)}", EbayPayloads.ItemGroup(s), cancellationToken);
                var group = await SendAsync(
                    context, HttpMethod.Post, "/sell/inventory/v1/offer/publish_by_inventory_item_group",
                    new { inventoryItemGroupKey = s.Group.GroupKey, marketplaceId = s.MarketplaceCode }, cancellationToken);
                groupListingId = Text(group.Body, "listingId");
            }
        }

        // Publishing returned; what counts as "live" is what eBay now reports for the offer.
        var (state, listingId) = await ReadOfferAsync(context, offerId, cancellationToken);
        if (listingId is not null)
        {
            references[ExternalResourceType.Listing] = listingId;
        }
        return OperationOutcome.Confirmed(state, references) with { GroupListingId = groupListingId };
    }

    public override async Task<OperationOutcome> GetStatusAsync(
        ChannelContext context, ListingWork work, string? submissionId, CancellationToken cancellationToken)
    {
        try
        {
            var offerId = work.References.GetValueOrDefault(ExternalResourceType.Offer);
            if (offerId is null)
            {
                var found = await FindOfferAsync(context, work.Snapshot, cancellationToken);
                if (found is not { } offer)
                {
                    return OperationOutcome.Confirmed(new RemoteState(ListingObservedStatus.NotListed, null, null));
                }
                offerId = Text(offer, "offerId")!;
            }

            var (state, listingId) = await ReadOfferAsync(context, offerId, cancellationToken);
            var references = new Dictionary<ExternalResourceType, string> { [ExternalResourceType.Offer] = offerId };
            if (listingId is not null)
            {
                references[ExternalResourceType.Listing] = listingId;
            }
            return OperationOutcome.Confirmed(state, references);
        }
        catch (ChannelException ex)
        {
            return OperationOutcome.From(ex);
        }
    }

    public override async Task<IReadOnlyList<ChannelOrder>> GetOrdersAsync(ChannelContext context, DateTime sinceUtc, CancellationToken cancellationToken)
    {
        try
        {
            var orders = await ebay.GetOrdersAsync(Environment(context), await TokenAsync(context, cancellationToken), sinceUtc, cancellationToken);
            return orders.Select(ToChannelOrder).ToList();
        }
        catch (EbayApiException ex)
        {
            throw new ChannelException(
                ex.StatusCode is { } status ? ChannelHttp.Classify(status) : SyncErrorClass.Transient, ex.Message, ex.StatusCode);
        }
    }

    public static ChannelOrder ToChannelOrder(EbayOrder order) => new(
        order.OrderId,
        order.CancelStatus?.CancelState == "CANCELED" ? OrderStatus.Cancelled
            : order.OrderFulfillmentStatus switch
            {
                "FULFILLED" => OrderStatus.Completed,
                "IN_PROGRESS" => OrderStatus.InProgress,
                _ => OrderStatus.New,
            },
        order.CreationDate?.ToUniversalTime(),
        order.LineItems?.FirstOrDefault()?.LineItemCost?.Currency,
        FulfilledByChannel: false,
        (order.LineItems ?? [])
            .Where(l => l.Quantity > 0)
            .Select((l, index) => new ChannelOrderLine(
                l.LineItemId ?? l.LegacyItemId ?? index.ToString(CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(l.Sku) ? null : l.Sku.Trim(),
                l.Title,
                l.Quantity,
                Math.Round((EbayClient.ParseAmount(l.LineItemCost) ?? 0m) / l.Quantity, 2)))
            .ToList());

    /// <summary>The category's item specifics from eBay's Taxonomy API: which are required, and which take listed values only.</summary>
    public override async Task<CategoryRequirements?> FetchRequirementsAsync(ChannelContext context, string externalCategoryId, CancellationToken cancellationToken)
    {
        var tree = await SendAsync(
            context, HttpMethod.Get,
            $"/commerce/taxonomy/v1/get_default_category_tree_id?marketplace_id={Uri.EscapeDataString(context.Market.MarketplaceCode)}", null, cancellationToken);
        var treeId = Text(tree.Body, "categoryTreeId") ?? "0";
        var path = $"/commerce/taxonomy/v1/category_tree/{Uri.EscapeDataString(treeId)}/get_item_aspects_for_category?category_id={Uri.EscapeDataString(externalCategoryId)}";
        var response = await SendAsync(context, HttpMethod.Get, path, null, cancellationToken);

        var required = new List<string>();
        var enums = new Dictionary<string, List<string>>();
        if (response.Body.TryGetProperty("aspects", out var aspects) && aspects.ValueKind == JsonValueKind.Array)
        {
            foreach (var aspect in aspects.EnumerateArray())
            {
                var name = Text(aspect, "localizedAspectName");
                if (name is null || !aspect.TryGetProperty("aspectConstraint", out var constraint))
                {
                    continue;
                }

                if (constraint.TryGetProperty("aspectRequired", out var isRequired) && isRequired.ValueKind == JsonValueKind.True)
                {
                    required.Add(name);
                }

                if (Text(constraint, "aspectMode") == "SELECTION_ONLY" && aspect.TryGetProperty("aspectValues", out var values) && values.ValueKind == JsonValueKind.Array)
                {
                    enums[name] = values.EnumerateArray().Select(v => Text(v, "localizedValue")).OfType<string>().ToList();
                }
            }
        }

        return new CategoryRequirements(required, enums)
        {
            Source = $"{EbayClient.ApiBase(Environment(context))}{path}",
            Version = Text(tree.Body, "categoryTreeVersion") ?? treeId,
        };
    }

    private async Task<JsonElement?> FindOfferAsync(ChannelContext context, ListingSnapshot s, CancellationToken cancellationToken)
    {
        var response = await SendAsync(
            context, HttpMethod.Get,
            $"/sell/inventory/v1/offer?sku={Uri.EscapeDataString(s.SellerSku)}&marketplace_id={Uri.EscapeDataString(s.MarketplaceCode)}&format=FIXED_PRICE",
            null, cancellationToken, 404);
        return response.Status != 404 && response.Body.TryGetProperty("offers", out var offers) && offers.ValueKind == JsonValueKind.Array && offers.GetArrayLength() > 0
            ? offers[0]
            : null;
    }

    private async Task<(RemoteState State, string? ListingId)> ReadOfferAsync(ChannelContext context, string offerId, CancellationToken cancellationToken)
    {
        var response = await SendAsync(context, HttpMethod.Get, $"/sell/inventory/v1/offer/{Uri.EscapeDataString(offerId)}", null, cancellationToken, 404);
        if (response.Status == 404)
        {
            return (new RemoteState(ListingObservedStatus.NotListed, null, null), null);
        }

        var offer = response.Body;
        var listing = offer.TryGetProperty("listing", out var l) ? l : default;
        var listingId = Text(listing, "listingId");
        var status = Text(offer, "status") != "PUBLISHED" ? ListingObservedStatus.NotListed
            : Text(listing, "listingStatus") is "ENDED" or "INACTIVE" ? ListingObservedStatus.Inactive
            : ListingObservedStatus.Live;
        decimal? price = offer.TryGetProperty("pricingSummary", out var pricing) && pricing.TryGetProperty("price", out var amount)
            && decimal.TryParse(Text(amount, "value"), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
        int? quantity = offer.TryGetProperty("availableQuantity", out var available) && available.ValueKind == JsonValueKind.Number ? available.GetInt32() : null;
        return (new RemoteState(status, price, quantity), listingId);
    }

    private async Task<ChannelResponse> SendAsync(
        ChannelContext context, HttpMethod method, string path, object? body, CancellationToken cancellationToken, params int[] alsoAccept)
    {
        var request = ChannelHttp.JsonRequest(method, $"{EbayClient.ApiBase(Environment(context))}{path}", body);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await TokenAsync(context, cancellationToken));
        if (request.Content is not null)
        {
            // The Inventory API requires the language of the content it is given.
            request.Content.Headers.ContentLanguage.Add(context.Market.Language);
        }

        try
        {
            return await http.SendAsync(EbayClient.HttpClientName, Name, request, cancellationToken, alsoAccept);
        }
        catch (ChannelException ex) when (ex.HttpStatus == 401)
        {
            tokens.Invalidate(context.Account.Id);
            throw;
        }
    }

    private Task<string> TokenAsync(ChannelContext context, CancellationToken cancellationToken) =>
        tokens.GetAsync(context.Account.Id, async () =>
        {
            var connection = await db.EbayConnections.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            if (connection?.RefreshTokenProtected is null)
            {
                throw new ChannelException(SyncErrorClass.Authorization, "Connect the eBay account first.");
            }

            try
            {
                var granted = await ebay.RefreshWithGrantedScopesAsync(
                    connection, secrets.Unprotect(connection.ClientSecretProtected), secrets.Unprotect(connection.RefreshTokenProtected), cancellationToken);
                // eBay's user access tokens last two hours.
                return (granted.AccessToken, TimeSpan.FromHours(2));
            }
            catch (EbayApiException ex)
            {
                throw new ChannelException(ex.StatusCode is null ? SyncErrorClass.Transient : SyncErrorClass.Authorization, ex.Message, ex.StatusCode);
            }
        }, cancellationToken);

    private static EbayEnvironment Environment(ChannelContext context) =>
        context.Account.Environment == ChannelEnvironment.Production ? EbayEnvironment.Production : EbayEnvironment.Sandbox;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string FirstError(JsonElement answer) =>
        answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
            ? Text(errors[0], "message") ?? "no reason given"
            : "no reason given";
}
