using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.TenantHost.Marketplace;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// A small eBay Inventory API kept in memory: inventory items, offers and
/// their publication, answering as eBay does. Every id it hands out is
/// made up for the test.
/// </summary>
public class InMemoryEbay
{
    public Dictionary<string, (string OfferId, bool Published)> Offers { get; } = [];

    /// <summary>The price eBay holds for each SKU's offer, as last sent to it.</summary>
    public Dictionary<string, string> Prices { get; } = [];

    /// <summary>SKUs whose price/quantity update eBay turns down.</summary>
    public HashSet<string> Refuse { get; } = [];

    public InMemoryEbay(ChannelRouter api)
    {
        api.On("PUT", "/inventory_item/", "", HttpStatusCode.NoContent);
        api.On("GET", "/offer?sku=", (uri, _) =>
        {
            var sku = Uri.UnescapeDataString(uri.Query.Split("sku=")[1].Split('&')[0]);
            return Offers.TryGetValue(sku, out var offer)
                ? ChannelRouter.Json($$"""{"offers":[{"offerId":"{{offer.OfferId}}","sku":"{{sku}}","status":"{{(offer.Published ? "PUBLISHED" : "UNPUBLISHED")}}"}]}""")
                : ChannelRouter.Json("""{"errors":[{"errorId":25713,"message":"This Offer is not available."}]}""", HttpStatusCode.NotFound);
        });
        api.On("POST", "/sell/inventory/v1/offer", body =>
        {
            var sku = JsonDocument.Parse(body).RootElement.GetProperty("sku").GetString()!;
            Offers[sku] = ($"o-{sku}", false);
            Prices[sku] = PriceOf(body);
            return ChannelRouter.Json($$"""{"offerId":"o-{{sku}}"}""", HttpStatusCode.Created);
        });
        api.On("PUT", "/offer/o-", (uri, body) =>
        {
            Prices[uri.Segments[^1][2..]] = PriceOf(body);
            return ChannelRouter.Json("", HttpStatusCode.NoContent);
        });
        api.On("GET", "/offer/o-", (uri, _) =>
        {
            var sku = uri.Segments[^1][2..];
            var published = Offers.TryGetValue(sku, out var offer) && offer.Published;
            var status = published ? "PUBLISHED" : "UNPUBLISHED";
            var listing = published ? """{"listingId":"110700009999","listingStatus":"ACTIVE"}""" : "null";
            return ChannelRouter.Json(
                $$"""{"offerId":"o-{{sku}}","sku":"{{sku}}","status":"{{status}}","availableQuantity":3,"""
                + "\"pricingSummary\":{\"price\":{\"value\":\"" + Prices.GetValueOrDefault(sku, "20.00") + "\",\"currency\":\"USD\"}},\"listing\":" + listing + "}");
        });
        api.On("POST", "/publish", (uri, _) =>
        {
            var sku = uri.Segments[^2].TrimEnd('/')[2..];
            Offers[sku] = (Offers[sku].OfferId, true);
            return ChannelRouter.Json("""{"listingId":"110700009999"}""");
        });
        api.On("POST", "/publish_by_inventory_item_group", body =>
        {
            foreach (var sku in Offers.Keys.ToList())
            {
                Offers[sku] = (Offers[sku].OfferId, true);
            }
            return ChannelRouter.Json("""{"listingId":"110700009999"}""");
        });
        api.On("PUT", "/inventory_item_group/", "", HttpStatusCode.NoContent);
        api.On("POST", "/withdraw", """{"listingId":"110700009999"}""");
        api.On("POST", "/bulk_update_price_quantity", body =>
        {
            var answers = JsonDocument.Parse(body).RootElement.GetProperty("requests").EnumerateArray().Select(r =>
            {
                var sku = r.GetProperty("sku").GetString()!;
                var offerId = r.GetProperty("offers")[0].GetProperty("offerId").GetString();
                if (!Refuse.Contains(sku) && r.GetProperty("offers")[0].TryGetProperty("price", out var price))
                {
                    Prices[sku] = price.GetProperty("value").GetString()!;
                }
                return Refuse.Contains(sku)
                    ? $$"""{"offerId":"{{offerId}}","sku":"{{sku}}","statusCode":400,"errors":[{"errorId":25002,"message":"Price is not valid for this category."}]}"""
                    : $$"""{"offerId":"{{offerId}}","sku":"{{sku}}","statusCode":200}""";
            });
            return ChannelRouter.Json($$"""{"responses":[{{string.Join(",", answers)}}]}""");
        });
    }

    private static string PriceOf(string offerBody) =>
        JsonDocument.Parse(offerBody).RootElement.GetProperty("pricingSummary").GetProperty("price").GetProperty("value").GetString()!;
}

/// <summary>
/// Listings on their way to the channels and back: content rules, the
/// outbox and worker, and each adapter against a fake of its channel's API.
/// These prove the pipeline and the request shapes; they are not sandbox or
/// production calls.
/// </summary>
public class ChannelSyncTests(MarketplaceFixture fixture) : IClassFixture<MarketplaceFixture>
{
    private const int Ebay = (int)SalesChannel.Ebay, Amazon = (int)SalesChannel.Amazon, Walmart = (int)SalesChannel.Walmart, Website = (int)SalesChannel.Website;

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url) => TenantApiHelpers.PostJsonWithAntiforgeryAsync(client, url, new { });

    private async Task<JsonElement> GetListingAsync(HttpClient client, Guid id) => await client.GetFromJsonAsync<JsonElement>($"/api/channel-listings/{id}");

    private async Task MapCategoryAsync(HttpClient admin, Guid marketId, string external, object? requirements = null) =>
        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, "/api/channels/category-mappings", new
        {
            channelMarketId = marketId,
            internalCategory = "Mugs",
            externalCategoryId = external,
            requirements,
            requirementsSource = requirements is null ? null : "test fixture",
            requirementsVersion = requirements is null ? null : "test-1",
        }));

    /// <summary>The one eBay account of this fixture's database, with the fake eBay behind it.</summary>
    private async Task<(Guid MarketId, InMemoryEbay Ebay)> EbayAsync(HttpClient admin)
    {
        await fixture.ConnectEbayAsync(admin);
        var remote = new InMemoryEbay(fixture.EbayApi);
        var existing = (await admin.GetFromJsonAsync<JsonElement>("/api/channels")).EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("channel").GetInt32() == Ebay);
        if (existing.ValueKind == JsonValueKind.Object)
        {
            return (existing.GetProperty("markets")[0].GetProperty("id").GetGuid(), remote);
        }

        var (_, marketId) = await fixture.CreateAccountAsync(
            admin, Ebay, "eBay store", "EBAY_US", MarketplaceFixture.EbaySettings, orderImport: true, inventorySync: true);
        await MapCategoryAsync(admin, marketId, "11700");
        return (marketId, remote);
    }

    private async Task<(Guid AccountId, Guid MarketId)> AmazonAsync(HttpClient admin, string name, bool liveWrites = true)
    {
        var (accountId, marketId) = await fixture.CreateAccountAsync(
            admin, Amazon, name, "ATVPDKIKX0DER", sellerId: "SELLER1", liveWrites: liveWrites, orderImport: true);
        Assert.Equal(HttpStatusCode.NoContent, (await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, $"/api/channels/{accountId}/credentials", new
        {
            credentials = new { clientId = "amzn-client", clientSecret = "amzn-client-secret-value", refreshToken = "amzn-refresh-token-value" },
        })).StatusCode);
        await MapCategoryAsync(admin, marketId, "MUG");
        fixture.Amazon.On("POST", "/auth/o2/token", """{"access_token":"amzn-access-token-value","expires_in":3600,"token_type":"bearer"}""");
        return (accountId, marketId);
    }

    [Fact]
    public async Task A_channel_override_outlives_a_change_to_the_product_and_clearing_is_not_the_same_as_inheriting()
    {
        using var admin = await fixture.AdminAsync("sync-overrides@example.com");
        var (productId, variantId) = await fixture.CreateProductAsync(admin, "OVR-1");
        var (_, marketId) = await fixture.CreateAccountAsync(admin, Website, "Overrides site", "default");

        var listingId = await fixture.SaveListingAsync(admin, marketId, variantId, new
        {
            content = new { title = new { value = "Site-only title" }, brand = new { cleared = true } },
        });
        var listing = await GetListingAsync(admin, listingId);
        Assert.Equal("Site-only title", listing.GetProperty("effectiveTitle").GetString());
        // Not overridden, so it is the product's.
        Assert.Equal("A base description.", listing.GetProperty("effectiveDescription").GetString());
        // Cleared on purpose: sent empty although the product has a brand.
        Assert.Equal(JsonValueKind.Null, listing.GetProperty("effectiveBrand").ValueKind);

        var product = await admin.GetFromJsonAsync<JsonElement>($"/api/products/{productId}");
        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, $"/api/products/{productId}", new
        {
            name = "Renamed product", price = 20m, stockQuantity = 10, rowVersion = product.GetProperty("rowVersion").GetString(),
        }));
        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, $"/api/catalog/products/{productId}/content", new { brand = "NewBrand", description = "A new base description.", category = "Mugs" }));

        listing = await GetListingAsync(admin, listingId);
        Assert.Equal("Site-only title", listing.GetProperty("effectiveTitle").GetString());
        Assert.Equal("A new base description.", listing.GetProperty("effectiveDescription").GetString());
        Assert.Equal(JsonValueKind.Null, listing.GetProperty("effectiveBrand").ValueKind);

        // Saving other fields leaves the overrides alone; a null puts one field back to inheriting.
        await fixture.SaveListingAsync(admin, marketId, variantId, new { priceOverride = 18m });
        Assert.Equal("Site-only title", (await GetListingAsync(admin, listingId)).GetProperty("effectiveTitle").GetString());
        await fixture.SaveListingAsync(admin, marketId, variantId, new { priceOverride = 18m, content = new { title = (object?)null, brand = (object?)null } });
        listing = await GetListingAsync(admin, listingId);
        Assert.Equal("Renamed product", listing.GetProperty("effectiveTitle").GetString());
        Assert.Equal("NewBrand", listing.GetProperty("effectiveBrand").GetString());
        Assert.Equal(18m, listing.GetProperty("effectivePrice").GetDecimal());
    }

    [Fact]
    public async Task A_missing_required_attribute_stops_that_channel_only_and_the_website_publishes_locally()
    {
        using var admin = await fixture.AdminAsync("sync-validation@example.com");
        var (productId, variantId) = await fixture.CreateProductAsync(admin, "VAL-1", price: 21m, stock: 6);
        var (_, amazonMarket) = await AmazonAsync(admin, "Validation store");
        await MapCategoryAsync(admin, amazonMarket, "MUG", new
        {
            required = new[] { "Color" },
            enums = new { Material = new[] { "Ceramic", "Glass" } },
            conditional = new[] { new { when = "Material", equals = "Glass", require = new[] { "Fragile" } } },
        });
        var (_, siteMarket) = await fixture.CreateAccountAsync(admin, Website, "Validation site", "default");

        var amazonListing = await fixture.SaveListingAsync(admin, amazonMarket, variantId, new { attributes = new { Material = "Glass" } });
        var siteListing = await fixture.SaveListingAsync(admin, siteMarket, variantId);
        fixture.Amazon.Requests.Clear();

        var refused = await PostAsync(admin, $"/api/channel-listings/{amazonListing}/publish");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        var refusal = await refused.Content.ReadFromJsonAsync<JsonElement>();
        var problems = refusal.GetProperty("issues").EnumerateArray().ToList();
        Assert.Contains(problems, i => i.GetProperty("path").GetString() == "attributes.Color" && i.GetProperty("code").GetString() == "required");
        // Glass brings its own requirement with it.
        Assert.Contains(problems, i => i.GetProperty("path").GetString() == "attributes.Fragile");
        Assert.All(problems, i => Assert.Equal(Amazon, i.GetProperty("channel").GetInt32()));
        Assert.Equal("test-1", refusal.GetProperty("requirementsVersion").GetString());

        // Nothing was queued or sent for Amazon, and the listing is still a draft.
        await fixture.SyncAsync();
        Assert.Empty(await fixture.JobsAsync(amazonListing));
        Assert.Empty(fixture.Amazon.Requests);
        Assert.Equal(ListingDesiredState.Draft, (await fixture.ListingAsync(amazonListing)).DesiredState);

        // The catalog itself stays editable, and the other channel is not held up.
        Assert.True((await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, $"/api/catalog/products/{productId}/content", new { brand = "TestBrand", description = "Still editable.", category = "Mugs" })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(admin, $"/api/channel-listings/{siteListing}/publish")).StatusCode);
        await fixture.SyncAsync();
        Assert.Equal(ListingObservedStatus.Live, (await fixture.ListingAsync(siteListing)).ObservedStatus);

        var storefront = await admin.GetFromJsonAsync<JsonElement>("/api/storefront/products");
        var shown = storefront.EnumerateArray().Single(p => p.GetProperty("sku").GetString() == "VAL-1");
        Assert.Equal(("Still editable.", 21m, 6), (shown.GetProperty("description").GetString(), shown.GetProperty("price").GetDecimal(), shown.GetProperty("availableQuantity").GetInt32()));

        // An unaccepted value is caught too; with everything supplied the Amazon listing validates.
        await fixture.SaveListingAsync(admin, amazonMarket, variantId, new { attributes = new { Material = "Wood", Color = "Blue" } });
        var check = await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/channel-listings/{amazonListing}/validate"));
        Assert.Equal("enum", Assert.Single(check.GetProperty("issues").EnumerateArray()).GetProperty("code").GetString());
        await fixture.SaveListingAsync(admin, amazonMarket, variantId, new { attributes = new { Material = "Ceramic", Color = "Blue" } });
        Assert.True((await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/channel-listings/{amazonListing}/validate"))).GetProperty("valid").GetBoolean());

        // Taken off the website, it leaves the storefront but the record stays.
        Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(admin, $"/api/channel-listings/{siteListing}/deactivate")).StatusCode);
        await fixture.SyncAsync();
        Assert.Equal(ListingObservedStatus.Inactive, (await fixture.ListingAsync(siteListing)).ObservedStatus);
        Assert.DoesNotContain((await admin.GetFromJsonAsync<JsonElement>("/api/storefront/products")).EnumerateArray(), p => p.GetProperty("sku").GetString() == "VAL-1");
    }

    [Fact]
    public async Task Ebay_end_to_end_create_confirm_then_price_and_stock_updates_and_an_order_imported_once()
    {
        using var admin = await fixture.AdminAsync("sync-ebay-flow@example.com");
        var (marketId, remote) = await EbayAsync(admin);
        var (productId, variantId) = await fixture.CreateProductAsync(admin, "EB-FLOW", price: 20m, stock: 10);
        var listingId = await fixture.SaveListingAsync(admin, marketId, variantId);

        Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(admin, $"/api/channel-listings/{listingId}/publish")).StatusCode);
        // Publishing only queued the work: no call to eBay was made by the request itself.
        Assert.Equal(0, fixture.EbayApi.Count("PUT", "/inventory_item/EB-FLOW"));
        await fixture.SyncAsync();

        var calls = fixture.EbayApi.Requests.Where(r => r.Contains("/sell/inventory/")).Select(r => string.Join(' ', r.Split(' ').Take(2))).ToList();
        Assert.Equal(
        [
            "PUT https://api.sandbox.ebay.com/sell/inventory/v1/inventory_item/EB-FLOW",
            "GET https://api.sandbox.ebay.com/sell/inventory/v1/offer?sku=EB-FLOW&marketplace_id=EBAY_US&format=FIXED_PRICE",
            "POST https://api.sandbox.ebay.com/sell/inventory/v1/offer",
            "POST https://api.sandbox.ebay.com/sell/inventory/v1/offer/o-EB-FLOW/publish",
            "GET https://api.sandbox.ebay.com/sell/inventory/v1/offer/o-EB-FLOW",
        ], calls);
        var offerBody = fixture.EbayApi.Requests.Single(r => r.StartsWith("POST https://api.sandbox.ebay.com/sell/inventory/v1/offer {"));
        Assert.Contains("\"categoryId\":\"11700\"", offerBody);
        Assert.Contains("\"pricingSummary\":{\"price\":{\"value\":\"20.00\",\"currency\":\"USD\"}}", offerBody);
        Assert.Contains("\"fulfillmentPolicyId\":\"fp-1\"", offerBody);

        var listing = await fixture.ListingAsync(listingId);
        Assert.Equal(ListingObservedStatus.Live, listing.ObservedStatus);
        Assert.Equal((listing.ContentVersion, listing.PriceVersion, listing.InventoryVersion),
            (listing.ConfirmedContentVersion, listing.ConfirmedPriceVersion, listing.ConfirmedInventoryVersion));
        var references = (await GetListingAsync(admin, listingId)).GetProperty("references");
        Assert.Equal("o-EB-FLOW", references.GetProperty("Offer").GetString());
        Assert.Equal("110700009999", references.GetProperty("Listing").GetString());

        // Nothing left to do: a second pass sends nothing.
        var sent = fixture.EbayApi.Requests.Count;
        Assert.Equal(0, await fixture.SyncAsync());
        Assert.Equal(sent, fixture.EbayApi.Requests.Count);

        // Two price changes before the worker runs become one update carrying the latest price.
        foreach (var price in new[] { 22m, 25m })
        {
            var product = await admin.GetFromJsonAsync<JsonElement>($"/api/products/{productId}");
            await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, $"/api/products/{productId}", new
            {
                name = "Product EB-FLOW", price, stockQuantity = 10, rowVersion = product.GetProperty("rowVersion").GetString(),
            }));
        }
        await fixture.SyncAsync();
        var priceUpdate = Assert.Single(fixture.EbayApi.Requests, r => r.Contains("/bulk_update_price_quantity"));
        Assert.Contains("\"offerId\":\"o-EB-FLOW\"", priceUpdate);
        Assert.Contains("\"price\":{\"value\":\"25.00\",\"currency\":\"USD\"}", priceUpdate);
        Assert.DoesNotContain("22.00", priceUpdate);
        listing = await fixture.ListingAsync(listingId);
        Assert.Equal((listing.PriceVersion, 25m), (listing.ConfirmedPriceVersion, listing.ObservedPrice));

        // A stock count goes out as a quantity update of its own.
        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, $"/api/catalog/variants/{variantId}/inventory", new { onHand = 7 }));
        await fixture.SyncAsync();
        Assert.Contains("\"shipToLocationAvailability\":{\"quantity\":7}", fixture.EbayApi.Requests.Last(r => r.Contains("/bulk_update_price_quantity")));
        Assert.Equal(7, (await fixture.ListingAsync(listingId)).ObservedQuantity);

        // An eBay order for two units: imported once, two units held, and eBay told there are five left.
        const string order = """
            {"total":1,"orders":[{"orderId":"27-00001-00001","creationDate":"2026-10-01T10:00:00.000Z","orderFulfillmentStatus":"NOT_STARTED",
              "lineItems":[{"lineItemId":"9001","legacyItemId":"110700009999","sku":"EB-FLOW","title":"Mug","quantity":2,
                            "lineItemCost":{"value":"50.00","currency":"USD"}}]}]}
            """;
        fixture.Ebay.Answer("/sell/fulfillment/v1/order", order);
        var accountId = (await GetListingAsync(admin, listingId)).GetProperty("channelAccountId").GetGuid();
        Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(admin, $"/api/channels/{accountId}/import-orders")).StatusCode);
        await fixture.SyncAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(admin, $"/api/channels/{accountId}/import-orders")).StatusCode);
        await fixture.SyncAsync();

        await fixture.WithDbAsync(async db =>
        {
            var imported = await db.Orders.Include(o => o.Items).SingleAsync(o => o.ExternalOrderId == "27-00001-00001");
            Assert.Equal((accountId, "EBAY-27-00001-00001", 50m), (imported.ChannelAccountId, imported.OrderNumber, imported.Total));
            Assert.Equal(variantId, Assert.Single(imported.Items).VariantId);
            var balance = await db.InventoryBalances.SingleAsync(b => b.VariantId == variantId);
            Assert.Equal((7, 2), (balance.OnHand, balance.Reserved));
            Assert.Contains(await db.AuditEntries.Select(a => a.Action).ToListAsync(), a => a == "ChannelPriceUpdated");
            return 0;
        });
        Assert.Contains("\"shipToLocationAvailability\":{\"quantity\":5}", fixture.EbayApi.Requests.Last(r => r.Contains("/bulk_update_price_quantity")));
        Assert.Single(remote.Offers);
    }

    [Fact]
    public async Task After_a_create_whose_answer_never_came_the_retry_looks_first_and_does_not_create_again()
    {
        using var admin = await fixture.AdminAsync("sync-timeout@example.com");
        var (marketId, remote) = await EbayAsync(admin);
        var (_, variantId) = await fixture.CreateProductAsync(admin, "EB-TIMEOUT");
        var listingId = await fixture.SaveListingAsync(admin, marketId, variantId);

        // eBay creates the offer, but the answer is lost on the way back.
        fixture.EbayApi.On("POST", "/sell/inventory/v1/offer", body =>
        {
            remote.Offers["EB-TIMEOUT"] = ("o-EB-TIMEOUT", false);
            throw new TaskCanceledException("Simulated timeout");
        });
        fixture.EbayApi.On("POST", "/publish", (uri, _) =>
        {
            remote.Offers["EB-TIMEOUT"] = ("o-EB-TIMEOUT", true);
            return ChannelRouter.Json("""{"listingId":"110700009999"}""");
        });

        await PostAsync(admin, $"/api/channel-listings/{listingId}/publish");
        await fixture.SyncAsync();

        var job = Assert.Single(await fixture.JobsAsync(listingId));
        Assert.Equal((SyncJobStatus.Pending, SyncErrorClass.Transient, 1), (job.Status, job.ErrorClass, job.Attempts));
        Assert.True(job.NextAttemptAtUtc > DateTime.UtcNow, "The retry waits for its backoff.");
        Assert.NotEqual(ListingObservedStatus.Live, (await fixture.ListingAsync(listingId)).ObservedStatus);

        await fixture.MakeJobsDueAsync();
        await fixture.SyncAsync();

        // One create in total: the retry found the offer, updated it and published it.
        Assert.Equal(1, fixture.EbayApi.Requests.Count(r => r.StartsWith("POST https://api.sandbox.ebay.com/sell/inventory/v1/offer {")));
        Assert.Equal(1, fixture.EbayApi.Count("PUT", "/offer/o-EB-TIMEOUT"));
        Assert.Equal(ListingObservedStatus.Live, (await fixture.ListingAsync(listingId)).ObservedStatus);
        job = Assert.Single(await fixture.JobsAsync(listingId));
        Assert.Equal((SyncJobStatus.Succeeded, 2), (job.Status, job.Attempts));
    }

    [Fact]
    public async Task One_refused_item_in_a_batch_fails_alone_and_only_it_is_sent_again()
    {
        using var admin = await fixture.AdminAsync("sync-batch@example.com");
        var (marketId, remote) = await EbayAsync(admin);
        var products = new List<(Guid ProductId, Guid ListingId, string Sku)>();
        foreach (var sku in new[] { "EB-BATCH-A", "EB-BATCH-B" })
        {
            var (productId, variantId) = await fixture.CreateProductAsync(admin, sku);
            var listingId = await fixture.SaveListingAsync(admin, marketId, variantId);
            await PostAsync(admin, $"/api/channel-listings/{listingId}/publish");
            products.Add((productId, listingId, sku));
        }
        await fixture.SyncAsync();
        Assert.All(products, p => Assert.Equal(ListingObservedStatus.Live, fixture.ListingAsync(p.ListingId).Result.ObservedStatus));

        remote.Refuse.Add("EB-BATCH-B");
        foreach (var (productId, _, sku) in products)
        {
            var product = await admin.GetFromJsonAsync<JsonElement>($"/api/products/{productId}");
            await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, $"/api/products/{productId}", new
            {
                name = $"Product {sku}", price = 31m, stockQuantity = 10, rowVersion = product.GetProperty("rowVersion").GetString(),
            }));
        }
        await fixture.SyncAsync();

        // Both went in one call, and eBay answered for each.
        var batch = Assert.Single(fixture.EbayApi.Requests, r => r.Contains("/bulk_update_price_quantity") && r.Contains("EB-BATCH-A"));
        Assert.Contains("EB-BATCH-B", batch);
        var (a, b) = (await fixture.ListingAsync(products[0].ListingId), await fixture.ListingAsync(products[1].ListingId));
        Assert.Equal((a.PriceVersion, 31m), (a.ConfirmedPriceVersion, a.ObservedPrice));
        Assert.True(b.ConfirmedPriceVersion < b.PriceVersion);
        Assert.NotEqual(31m, b.ObservedPrice);
        var failed = (await fixture.JobsAsync(products[1].ListingId)).Single(j => j.Operation == SyncOperation.Price);
        Assert.Equal((SyncJobStatus.NeedsCorrection, SyncErrorClass.DataCorrection), (failed.Status, failed.ErrorClass));
        Assert.Contains("Price is not valid", failed.LastError);

        // Corrected and retried: the item that succeeded is not sent a second time.
        remote.Refuse.Clear();
        Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(admin, $"/api/channel-listings/{products[1].ListingId}/retry")).StatusCode);
        await fixture.SyncAsync();
        var updates = fixture.EbayApi.Requests.Where(r => r.Contains("/bulk_update_price_quantity") && r.Contains("\"price\"")).ToList();
        Assert.Equal(1, updates.Count(r => r.Contains("EB-BATCH-A")));
        Assert.DoesNotContain("EB-BATCH-A", updates.Last());
        b = await fixture.ListingAsync(products[1].ListingId);
        Assert.Equal((b.PriceVersion, 31m), (b.ConfirmedPriceVersion, b.ObservedPrice));
    }

    [Fact]
    public async Task Amazon_accepting_a_submission_is_not_shown_as_for_sale_until_amazon_reports_it_buyable()
    {
        using var admin = await fixture.AdminAsync("sync-amazon@example.com");
        var (_, marketId) = await AmazonAsync(admin, "Amazon store");
        var (_, variantId) = await fixture.CreateProductAsync(admin, "AMZ-1", price: 24.5m, stock: 3);
        var listingId = await fixture.SaveListingAsync(admin, marketId, variantId, new { attributes = new { Color = "Blue" } });
        const string item = "/listings/2021-08-01/items/SELLER1/AMZ-1";
        fixture.Amazon.On("PUT", item, """{"sku":"AMZ-1","status":"ACCEPTED","submissionId":"sub-test-only-1","issues":[]}""");
        fixture.Amazon.On("GET", item, """{"errors":[{"code":"NOT_FOUND","message":"SKU not found"}]}""", HttpStatusCode.NotFound);

        await PostAsync(admin, $"/api/channel-listings/{listingId}/publish");
        await fixture.SyncAsync();

        var put = Assert.Single(fixture.Amazon.Requests, r => r.StartsWith("PUT ") && r.Contains(item));
        Assert.Contains("marketplaceIds=ATVPDKIKX0DER", put);
        Assert.Contains("\"productType\":\"MUG\"", put);
        Assert.Contains("\"requirements\":\"LISTING\"", put);
        Assert.Contains("\"externally_assigned_product_identifier\":[{\"type\":\"upc\",\"value\":\"000012345678\"", put);
        Assert.Contains("\"our_price\":[{\"schedule\":[{\"value_with_tax\":24.5", put);
        Assert.Contains("\"fulfillment_availability\":[{\"fulfillment_channel_code\":\"DEFAULT\",\"quantity\":3}]", put);

        // Accepted: a submission id to follow up, and nothing more. Not live, not confirmed.
        var job = Assert.Single(await fixture.JobsAsync(listingId));
        Assert.Equal((SyncJobStatus.AwaitingRemote, "sub-test-only-1"), (job.Status, job.ExternalSubmissionId));
        var listing = await fixture.ListingAsync(listingId);
        Assert.Equal((ListingObservedStatus.Processing, 0L), (listing.ObservedStatus, listing.ConfirmedContentVersion));

        // Amazon now knows the SKU but will not sell it yet: still not live.
        fixture.Amazon.On("GET", item, """
            {"sku":"AMZ-1","summaries":[{"marketplaceId":"ATVPDKIKX0DER","asin":"B0TESTONLY1","productType":"MUG","status":["DISCOVERABLE"]}],"issues":[]}
            """);
        await fixture.MakeJobsDueAsync();
        await fixture.SyncAsync();
        Assert.Equal(ListingObservedStatus.Processing, (await fixture.ListingAsync(listingId)).ObservedStatus);
        Assert.Equal(SyncJobStatus.AwaitingRemote, Assert.Single(await fixture.JobsAsync(listingId)).Status);

        fixture.Amazon.On("GET", item, """
            {"sku":"AMZ-1","summaries":[{"marketplaceId":"ATVPDKIKX0DER","asin":"B0TESTONLY1","productType":"MUG","status":["BUYABLE","DISCOVERABLE"]}],
             "issues":[],"offers":[{"marketplaceId":"ATVPDKIKX0DER","offerType":"B2C","price":{"currencyCode":"USD","amount":"24.50"}}],
             "fulfillmentAvailability":[{"fulfillmentChannelCode":"DEFAULT","quantity":3}]}
            """);
        await fixture.MakeJobsDueAsync();
        await fixture.SyncAsync();

        listing = await fixture.ListingAsync(listingId);
        Assert.Equal((ListingObservedStatus.Live, listing.ContentVersion, 24.5m), (listing.ObservedStatus, listing.ConfirmedContentVersion, listing.ObservedPrice));
        Assert.Equal(SyncJobStatus.Succeeded, Assert.Single(await fixture.JobsAsync(listingId)).Status);
        Assert.Equal("B0TESTONLY1", (await GetListingAsync(admin, listingId)).GetProperty("references").GetProperty("CatalogItem").GetString());
        // The submission was sent once; everything after it was reading.
        Assert.Equal(1, fixture.Amazon.Count("PUT", item));
    }

    [Fact]
    public async Task Amazon_turning_a_submission_down_leaves_its_issues_on_the_listing_for_correction()
    {
        using var admin = await fixture.AdminAsync("sync-amazon-invalid@example.com");
        var (_, marketId) = await AmazonAsync(admin, "Amazon strict store");
        var (_, variantId) = await fixture.CreateProductAsync(admin, "AMZ-BAD");
        var listingId = await fixture.SaveListingAsync(admin, marketId, variantId);
        fixture.Amazon.On("PUT", "/items/SELLER1/AMZ-BAD", """
            {"sku":"AMZ-BAD","status":"INVALID","submissionId":"sub-test-only-2",
             "issues":[{"code":"90220","message":"'Material' is required but not supplied.","severity":"ERROR","attributeNames":["material"]}]}
            """);

        await PostAsync(admin, $"/api/channel-listings/{listingId}/publish");
        await fixture.SyncAsync();

        var job = Assert.Single(await fixture.JobsAsync(listingId));
        Assert.Equal((SyncJobStatus.NeedsCorrection, SyncErrorClass.DataCorrection), (job.Status, job.ErrorClass));
        var listing = await GetListingAsync(admin, listingId);
        Assert.Equal((int)ListingObservedStatus.Rejected, listing.GetProperty("observedStatus").GetInt32());
        var issue = Assert.Single(listing.GetProperty("issues").EnumerateArray());
        Assert.Equal(("attributes.material", "ERROR:90220"), (issue.GetProperty("path").GetString(), issue.GetProperty("code").GetString()));

        // Throttled: the job goes back with the delay Amazon asked for, to be tried again rather than failed.
        fixture.Amazon.On("PUT", "/items/SELLER1/AMZ-BAD", (_, _) =>
        {
            var response = ChannelRouter.Json("""{"errors":[{"code":"QuotaExceeded","message":"You exceeded your quota for the requested resource."}]}""", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return response;
        });
        await PostAsync(admin, $"/api/channel-listings/{listingId}/retry");
        await fixture.SyncAsync();
        var retried = (await fixture.JobsAsync(listingId)).Last();
        Assert.Equal((SyncJobStatus.Pending, SyncErrorClass.Transient), (retried.Status, retried.ErrorClass));
        Assert.InRange((retried.NextAttemptAtUtc - DateTime.UtcNow).TotalSeconds, 100, 125);
    }

    [Fact]
    public async Task Walmart_answers_a_feed_item_by_item_and_ingested_is_not_yet_published()
    {
        using var admin = await fixture.AdminAsync("sync-walmart@example.com");
        var (accountId, marketId) = await fixture.CreateAccountAsync(
            admin, Walmart, "Walmart store", "WALMART_US", new { itemSpecVersion = "TEST-ONLY-SPEC" }, orderImport: true);
        await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, $"/api/channels/{accountId}/credentials", new { credentials = new { clientId = "wm-client", clientSecret = "wm-client-secret-value" } });
        await MapCategoryAsync(admin, marketId, "Drinkware");
        var listings = new Dictionary<string, Guid>();
        foreach (var sku in new[] { "WM-A", "WM-B" })
        {
            var (_, variantId) = await fixture.CreateProductAsync(admin, sku);
            listings[sku] = await fixture.SaveListingAsync(admin, marketId, variantId);
            await PostAsync(admin, $"/api/channel-listings/{listings[sku]}/publish");
        }

        fixture.Walmart.On("POST", "/v3/token", """{"access_token":"wm-access-token-value","token_type":"Bearer","expires_in":900}""");
        fixture.Walmart.On("POST", "/v3/feeds?feedType=MP_ITEM", """{"feedId":"FEED-TEST-ONLY-1"}""");
        await fixture.SyncAsync();

        // Both items in one feed, both merely accepted.
        var feed = Assert.Single(fixture.Walmart.Requests, r => r.StartsWith("POST ") && r.Contains("/v3/feeds"));
        Assert.Contains("\"sku\":\"WM-A\"", feed);
        Assert.Contains("\"sku\":\"WM-B\"", feed);
        Assert.Contains("\"version\":\"TEST-ONLY-SPEC\"", feed);
        Assert.Contains("\"productIdType\":\"UPC\",\"productId\":\"000012345678\"", feed);
        Assert.All(listings.Values, id => Assert.Equal(ListingObservedStatus.Processing, fixture.ListingAsync(id).Result.ObservedStatus));

        // The feed is processed: A was ingested, B has a data error. A is ingested but Walmart has not published it yet.
        fixture.Walmart.On("GET", "/v3/feeds/FEED-TEST-ONLY-1", """
            {"feedId":"FEED-TEST-ONLY-1","feedStatus":"PROCESSED","itemsReceived":2,"itemsSucceeded":1,"itemsFailed":1,
             "itemDetails":{"itemIngestionStatus":[
               {"sku":"WM-A","index":0,"ingestionStatus":"SUCCESS"},
               {"sku":"WM-B","index":1,"ingestionStatus":"DATA_ERROR",
                "ingestionErrors":{"ingestionError":[{"type":"DATA_ERROR","code":"ERR_PDI_0001","field":"brand","description":"Brand is not recognised."}]}}]}}
            """);
        fixture.Walmart.On("GET", "/v3/items/WM-A", """{"ItemResponse":[{"sku":"WM-A","wpid":"WPIDTESTONLY1","publishedStatus":"IN_PROGRESS","lifecycleStatus":"ACTIVE"}]}""");
        await fixture.MakeJobsDueAsync();
        await fixture.SyncAsync();

        Assert.Equal(ListingObservedStatus.Processing, (await fixture.ListingAsync(listings["WM-A"])).ObservedStatus);
        var failed = Assert.Single(await fixture.JobsAsync(listings["WM-B"]));
        Assert.Equal(SyncJobStatus.NeedsCorrection, failed.Status);
        var wmB = await GetListingAsync(admin, listings["WM-B"]);
        Assert.Equal("attributes.brand", Assert.Single(wmB.GetProperty("issues").EnumerateArray()).GetProperty("path").GetString());

        fixture.Walmart.On("GET", "/v3/items/WM-A", """
            {"ItemResponse":[{"sku":"WM-A","wpid":"WPIDTESTONLY1","publishedStatus":"PUBLISHED","lifecycleStatus":"ACTIVE","price":{"currency":"USD","amount":20.00}}]}
            """);
        await fixture.MakeJobsDueAsync();
        await fixture.SyncAsync();
        Assert.Equal(ListingObservedStatus.Live, (await fixture.ListingAsync(listings["WM-A"])).ObservedStatus);

        // B is corrected and sent again, alone; A, which succeeded, is not in the new feed.
        fixture.Walmart.On("POST", "/v3/feeds?feedType=MP_ITEM", """{"feedId":"FEED-TEST-ONLY-2"}""");
        await PostAsync(admin, $"/api/channel-listings/{listings["WM-B"]}/retry");
        await fixture.SyncAsync();
        var feeds = fixture.Walmart.Requests.Where(r => r.StartsWith("POST ") && r.Contains("/v3/feeds")).ToList();
        Assert.Equal(2, feeds.Count);
        Assert.Contains("\"sku\":\"WM-B\"", feeds[1]);
        Assert.DoesNotContain("\"sku\":\"WM-A\"", feeds[1]);
    }

    [Fact]
    public async Task With_live_writes_off_everything_is_built_and_nothing_is_sent_and_no_secret_is_given_back()
    {
        using var admin = await fixture.AdminAsync("sync-dryrun@example.com");
        var (accountId, marketId) = await AmazonAsync(admin, "Amazon dry store", liveWrites: false);
        var (_, variantId) = await fixture.CreateProductAsync(admin, "AMZ-DRY");
        var listingId = await fixture.SaveListingAsync(admin, marketId, variantId);
        fixture.Amazon.Requests.Clear();

        // The preview shows exactly what would go out, with no credentials involved.
        var previewText = await (await PostAsync(admin, $"/api/channel-listings/{listingId}/preview?operation=0")).Content.ReadAsStringAsync();
        var preview = JsonDocument.Parse(previewText).RootElement;
        Assert.False(preview.GetProperty("liveWrites").GetBoolean());
        var request = Assert.Single(preview.GetProperty("requests").EnumerateArray());
        Assert.Equal("PUT", request.GetProperty("method").GetString());
        Assert.EndsWith("/items/SELLER1/AMZ-DRY?marketplaceIds=ATVPDKIKX0DER&issueLocale=en_US", request.GetProperty("url").GetString());
        Assert.Equal("LISTING", request.GetProperty("body").GetProperty("requirements").GetString());

        var queued = await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/channel-listings/{listingId}/publish"));
        Assert.False(queued.GetProperty("liveWrites").GetBoolean());
        await fixture.SyncAsync();

        var job = Assert.Single(await fixture.JobsAsync(listingId));
        Assert.Equal(SyncJobStatus.DryRunCompleted, job.Status);
        Assert.Contains("nothing sent", job.LastError);
        Assert.Empty(fixture.Amazon.Requests);
        // A dry run confirms nothing and shows nothing as live.
        var listing = await fixture.ListingAsync(listingId);
        Assert.Equal((ListingObservedStatus.Unknown, 0L), (listing.ObservedStatus, listing.ConfirmedContentVersion));

        // The secrets went in and do not come back: not from the API, not in the clear in the database, not in the job history.
        var accounts = await admin.GetStringAsync("/api/channels");
        Assert.Contains("\"hasCredentials\":true", accounts);
        var history = await admin.GetStringAsync($"/api/channels/sync/jobs/{job.Id}");
        foreach (var secret in new[] { "amzn-client-secret-value", "amzn-refresh-token-value", "amzn-access-token-value" })
        {
            Assert.DoesNotContain(secret, accounts);
            Assert.DoesNotContain(secret, previewText);
            Assert.DoesNotContain(secret, history);
        }
        var stored = await fixture.WithDbAsync(db => db.ChannelAccounts.AsNoTracking().SingleAsync(a => a.Id == accountId));
        Assert.DoesNotContain("amzn-refresh-token-value", stored.CredentialsProtected);

        var health = await admin.GetFromJsonAsync<JsonElement>("/api/channels/sync/health");
        Assert.Contains(health.GetProperty("accounts").EnumerateArray(), a => a.GetProperty("id").GetGuid() == accountId && !a.GetProperty("liveWrites").GetBoolean());
    }

    [Fact]
    public async Task A_job_left_running_by_a_worker_that_died_is_taken_up_again_and_finished()
    {
        using var admin = await fixture.AdminAsync("sync-recovery@example.com");
        var (_, marketId) = await fixture.CreateAccountAsync(admin, Website, "Recovery site", "default");
        var (_, variantId) = await fixture.CreateProductAsync(admin, "RECOVER-1");
        var listingId = await fixture.SaveListingAsync(admin, marketId, variantId);
        await PostAsync(admin, $"/api/channel-listings/{listingId}/publish");
        await fixture.WithScopeAsync(services => services.GetRequiredService<SyncEngine>().DispatchOutboxAsync(default));

        // A worker claimed the job and then vanished; its lease has run out.
        await fixture.WithDbAsync(db => db.SyncJobs.Where(j => j.ChannelListingId == listingId).ExecuteUpdateAsync(s => s
            .SetProperty(j => j.Status, SyncJobStatus.Running)
            .SetProperty(j => j.LeaseOwner, "worker-that-died")
            .SetProperty(j => j.LeaseExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1))));
        var health = await admin.GetFromJsonAsync<JsonElement>("/api/channels/sync/health");
        Assert.Equal(1, health.GetProperty("expiredLeases").GetInt32());

        await fixture.SyncAsync();

        var job = Assert.Single(await fixture.JobsAsync(listingId));
        Assert.Equal(SyncJobStatus.Succeeded, job.Status);
        Assert.Equal(ListingObservedStatus.Live, (await fixture.ListingAsync(listingId)).ObservedStatus);
        var history = await admin.GetFromJsonAsync<JsonElement>($"/api/channels/sync/jobs/{job.Id}");
        Assert.Contains(history.GetProperty("attemptHistory").EnumerateArray(), a => a.GetProperty("detail").GetString()!.Contains("worker-that-died"));

        // A job that is still within its lease is another worker's, and is left alone.
        await PostAsync(admin, $"/api/channel-listings/{listingId}/deactivate");
        await fixture.WithScopeAsync(services => services.GetRequiredService<SyncEngine>().DispatchOutboxAsync(default));
        await fixture.WithDbAsync(db => db.SyncJobs.Where(j => j.ChannelListingId == listingId && j.Status == SyncJobStatus.Pending).ExecuteUpdateAsync(s => s
            .SetProperty(j => j.Status, SyncJobStatus.Running)
            .SetProperty(j => j.LeaseOwner, "worker-still-alive")
            .SetProperty(j => j.LeaseExpiresAtUtc, DateTime.UtcNow.AddMinutes(5))));
        await fixture.SyncAsync();
        Assert.Equal(ListingObservedStatus.Live, (await fixture.ListingAsync(listingId)).ObservedStatus);
    }

    [Fact]
    public async Task An_answer_for_an_older_version_arriving_late_does_not_replace_what_a_newer_one_confirmed()
    {
        using var admin = await fixture.AdminAsync("sync-stale@example.com");
        var (_, marketId) = await fixture.CreateAccountAsync(admin, Website, "Stale site", "default");
        var (productId, variantId) = await fixture.CreateProductAsync(admin, "STALE-1", price: 10m);
        var listingId = await fixture.SaveListingAsync(admin, marketId, variantId);
        await PostAsync(admin, $"/api/channel-listings/{listingId}/publish");
        await fixture.SyncAsync();

        // A worker reads the listing at this version and is then held up before its answer is recorded.
        var slow = await fixture.WithScopeAsync(services => services.GetRequiredService<ListingService>().LoadAsync(listingId, default));

        var product = await admin.GetFromJsonAsync<JsonElement>($"/api/products/{productId}");
        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, $"/api/products/{productId}", new
        {
            name = "Product STALE-1", price = 14m, stockQuantity = 10, rowVersion = product.GetProperty("rowVersion").GetString(),
        }));
        await fixture.SyncAsync();
        var current = await fixture.ListingAsync(listingId);
        Assert.Equal((current.PriceVersion, 14m), (current.ConfirmedPriceVersion, current.ObservedPrice));
        Assert.True(slow!.Work.PriceVersion < current.PriceVersion);

        // The slow worker's answer, for the old price, finally lands.
        var recorded = await fixture.WithScopeAsync(services => services.GetRequiredService<SyncEngine>().ConfirmAsync(
            listingId, SyncOperation.Price, slow.Work, new TenantHost.Marketplace.Channels.RemoteState(ListingObservedStatus.Live, 10m, null), DateTime.UtcNow, default));

        Assert.False(recorded);
        var after = await fixture.ListingAsync(listingId);
        Assert.Equal((current.ConfirmedPriceVersion, 14m), (after.ConfirmedPriceVersion, after.ObservedPrice));
    }

    [Fact]
    public async Task A_price_changed_on_the_channel_is_handled_by_the_accounts_policy_once()
    {
        using var admin = await fixture.AdminAsync("sync-conflict@example.com");
        var (marketId, _) = await EbayAsync(admin);
        var (_, variantId) = await fixture.CreateProductAsync(admin, "EB-CONFLICT", price: 20m);
        var listingId = await fixture.SaveListingAsync(admin, marketId, variantId);
        await PostAsync(admin, $"/api/channel-listings/{listingId}/publish");
        await fixture.SyncAsync();
        var accountId = (await GetListingAsync(admin, listingId)).GetProperty("channelAccountId").GetGuid();

        // The seller changes the price in eBay's own console.
        fixture.EbayApi.On("GET", "/offer/o-EB-CONFLICT", """
            {"offerId":"o-EB-CONFLICT","sku":"EB-CONFLICT","status":"PUBLISHED","availableQuantity":10,
             "pricingSummary":{"price":{"value":"17.00","currency":"USD"}},"listing":{"listingId":"110700009999","listingStatus":"ACTIVE"}}
            """);

        async Task ReconcileAsync()
        {
            await fixture.WithScopeAsync(async services =>
            {
                await services.GetRequiredService<SyncEngine>().QueueAsync(accountId, listingId, SyncOperation.Reconcile, 0, false, DateTime.UtcNow, default);
                return await services.GetRequiredService<Infrastructure.Tenants.TenantDbContext>().SaveChangesAsync();
            });
            await fixture.SyncAsync();
        }

        // The default policy changes nothing on either side and says so.
        await ReconcileAsync();
        var listing = await fixture.ListingAsync(listingId);
        Assert.Equal((true, 17m, (decimal?)null), (listing.HasPriceConflict, listing.ObservedPrice, listing.PriceOverride));
        Assert.Equal(0, fixture.EbayApi.Count("POST", "/bulk_update_price_quantity"));

        // Switched to restoring the local price: it is sent again, once.
        await fixture.WithDbAsync(db => db.ChannelAccounts.Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.PriceConflictPolicy, PriceConflictPolicy.RestoreLocal)));
        await ReconcileAsync();
        var restore = Assert.Single(fixture.EbayApi.Requests, r => r.Contains("/bulk_update_price_quantity"));
        Assert.Contains("\"price\":{\"value\":\"20.00\"", restore);
        Assert.Equal(0, await fixture.SyncAsync());
    }

    [Fact]
    public async Task Stock_is_sent_to_channels_only_when_orders_come_in_from_every_active_channel()
    {
        using var admin = await fixture.AdminAsync("sync-rules@example.com");
        var (blockerId, _) = await fixture.CreateAccountAsync(admin, Amazon, "Not importing store", "ATVPDKIKX0DER", sellerId: "S2");

        var refused = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/channels", new
        {
            channel = Walmart, name = "Wants stock sync", environment = 0, isEnabled = true, liveWritesEnabled = false,
            inventorySyncEnabled = true, orderImportEnabled = true, priceConflictPolicy = 2,
        });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("Not importing store", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());

        // Out of the way again, so it does not hold up other tests' accounts.
        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, $"/api/channels/{blockerId}", new
        {
            channel = Amazon, name = "Not importing store", environment = 0, sellerId = "S2", isEnabled = false, liveWritesEnabled = false,
            inventorySyncEnabled = false, orderImportEnabled = false, priceConflictPolicy = 2,
        }));
    }
}
