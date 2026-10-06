using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// The two reference products: one with nothing to choose, one with two
/// variants, each offered on all four channels. Every external id that
/// appears here is made up for the test.
/// </summary>
public class MultichannelFixtureTests(MarketplaceFixture fixture) : IClassFixture<MarketplaceFixture>
{
    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url) => TenantApiHelpers.PostJsonWithAntiforgeryAsync(client, url, new { });

    private static async Task<List<JsonElement>> PreviewAsync(HttpClient admin, Guid listingId) =>
        (await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/channel-listings/{listingId}/preview?operation=0")))
            .GetProperty("requests").EnumerateArray().ToList();

    [Fact]
    public async Task A_simple_product_and_a_two_variant_product_are_represented_on_all_four_channels()
    {
        using var admin = await fixture.AdminAsync("fixture-four-channels@example.com");
        await fixture.ConnectEbayAsync(admin);
        var remote = new InMemoryEbay(fixture.EbayApi);

        var markets = new Dictionary<SalesChannel, Guid>
        {
            [SalesChannel.Ebay] = (await fixture.CreateAccountAsync(admin, (int)SalesChannel.Ebay, "eBay", "EBAY_US", MarketplaceFixture.EbaySettings)).MarketId,
            [SalesChannel.Amazon] = (await fixture.CreateAccountAsync(admin, (int)SalesChannel.Amazon, "Amazon", "ATVPDKIKX0DER", sellerId: "SELLERTESTONLY", liveWrites: false)).MarketId,
            [SalesChannel.Walmart] = (await fixture.CreateAccountAsync(
                admin, (int)SalesChannel.Walmart, "Walmart", "WALMART_US", new { itemSpecVersion = "TEST-ONLY-SPEC" }, liveWrites: false)).MarketId,
            [SalesChannel.Website] = (await fixture.CreateAccountAsync(admin, (int)SalesChannel.Website, "Website", "default")).MarketId,
        };
        foreach (var (channel, category) in new[] { (SalesChannel.Ebay, "15687"), (SalesChannel.Amazon, "SHIRT"), (SalesChannel.Walmart, "T-Shirts") })
        {
            await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, "/api/channels/category-mappings", new
            {
                channelMarketId = markets[channel], internalCategory = "Mugs", externalCategoryId = category,
            }));
        }

        // The product with nothing to choose still has its one sellable variant.
        var (_, simpleVariant) = await fixture.CreateProductAsync(admin, "FX-SIMPLE", price: 12m, stock: 5);

        // The product with two variants: its own SKU becomes "Blue", and "Red" is added beside it.
        var (teeId, blueVariant) = await fixture.CreateProductAsync(admin, "FX-TEE-BLUE", price: 20m, stock: 8);
        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, $"/api/catalog/variants/{blueVariant}", new { name = "Blue", options = new Dictionary<string, string> { ["Color"] = "Blue" }, condition = 0, price = 20m }));
        var withRed = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, $"/api/catalog/products/{teeId}/variants", new { sku = "FX-TEE-RED", name = "Red", options = new Dictionary<string, string> { ["Color"] = "Red" }, condition = 0, price = 21m }));
        var redVariant = withRed.GetProperty("variants").EnumerateArray().Single(v => v.GetProperty("sku").GetString() == "FX-TEE-RED").GetProperty("id").GetGuid();
        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, $"/api/catalog/variants/{redVariant}/inventory", new { onHand = 3 }));
        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, $"/api/catalog/products/{teeId}/identifiers", new { type = (int)ProductIdentifierType.Upc, value = "000012345685", variantId = redVariant }));

        var listings = new Dictionary<(SalesChannel Channel, string Sku), Guid>();
        foreach (var (channel, marketId) in markets)
        {
            foreach (var (sku, variantId) in new[] { ("FX-SIMPLE", simpleVariant), ("FX-TEE-BLUE", blueVariant), ("FX-TEE-RED", redVariant) })
            {
                listings[(channel, sku)] = await fixture.SaveListingAsync(admin, marketId, variantId);
            }

            // The website shows variants side by side; the marketplaces each get a variation family of their own.
            if (channel != SalesChannel.Website)
            {
                await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, "/api/channel-listings/groups", new
                {
                    channelMarketId = marketId, productId = teeId, groupKey = "FX-TEE", variationAttributes = new[] { "Color" },
                    listingIds = new[] { listings[(channel, "FX-TEE-BLUE")], listings[(channel, "FX-TEE-RED")] },
                }));
            }
        }

        // What each channel would be sent, built without sending.
        var ebaySimple = await PreviewAsync(admin, listings[(SalesChannel.Ebay, "FX-SIMPLE")]);
        Assert.Equal(["PUT", "POST", "POST"], ebaySimple.Select(r => r.GetProperty("method").GetString()));
        var ebayBlue = await PreviewAsync(admin, listings[(SalesChannel.Ebay, "FX-TEE-BLUE")]);
        var group = ebayBlue.Single(r => r.GetProperty("url").GetString()!.EndsWith("/inventory_item_group/FX-TEE")).GetProperty("body");
        Assert.Equal(["FX-TEE-BLUE", "FX-TEE-RED"], group.GetProperty("variantSKUs").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(["Blue", "Red"], group.GetProperty("variesBy").GetProperty("specifications")[0].GetProperty("values").EnumerateArray().Select(x => x.GetString()));
        Assert.EndsWith("/offer/publish_by_inventory_item_group", ebayBlue[^1].GetProperty("url").GetString());

        Assert.Single(await PreviewAsync(admin, listings[(SalesChannel.Amazon, "FX-SIMPLE")]));
        var amazonRed = await PreviewAsync(admin, listings[(SalesChannel.Amazon, "FX-TEE-RED")]);
        // The parent first, then the child that points at it. The parent is a relationship, not something sold.
        Assert.Contains("/items/SELLERTESTONLY/FX-TEE?", amazonRed[0].GetProperty("url").GetString());
        Assert.Equal("LISTING_PRODUCT_ONLY", amazonRed[0].GetProperty("body").GetProperty("requirements").GetString());
        var child = amazonRed[1].GetProperty("body").GetProperty("attributes");
        Assert.Equal("FX-TEE", child.GetProperty("child_parent_sku_relationship")[0].GetProperty("parent_sku").GetString());
        // The variant's own UPC is used, not the product's.
        Assert.Equal("000012345685", child.GetProperty("externally_assigned_product_identifier")[0].GetProperty("value").GetString());
        Assert.Equal(3, child.GetProperty("fulfillment_availability")[0].GetProperty("quantity").GetInt32());

        var walmartBlue = Assert.Single(await PreviewAsync(admin, listings[(SalesChannel.Walmart, "FX-TEE-BLUE")]));
        Assert.EndsWith("/v3/feeds?feedType=MP_ITEM", walmartBlue.GetProperty("url").GetString());
        Assert.Equal("FX-TEE", walmartBlue.GetProperty("body").GetProperty("MPItem")[0].GetProperty("Visible").GetProperty("T-Shirts").GetProperty("variantGroupId").GetString());
        Assert.Equal("LOCAL", Assert.Single(await PreviewAsync(admin, listings[(SalesChannel.Website, "FX-SIMPLE")])).GetProperty("method").GetString());

        // eBay and the website are live in this fixture; the family goes out on eBay as one listing with two SKUs.
        foreach (var key in listings.Keys.Where(k => k.Channel is SalesChannel.Ebay or SalesChannel.Website))
        {
            Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(admin, $"/api/channel-listings/{listings[key]}/publish")).StatusCode);
        }
        await fixture.SyncAsync();

        Assert.Equal(1, fixture.EbayApi.Count("POST", "/offer/publish_by_inventory_item_group"));
        Assert.Equal(1, fixture.EbayApi.Count("POST", "/offer/o-FX-SIMPLE/publish"));
        Assert.Equal(3, remote.Offers.Count);
        foreach (var key in listings.Keys.Where(k => k.Channel is SalesChannel.Ebay or SalesChannel.Website))
        {
            Assert.Equal(ListingObservedStatus.Live, (await fixture.ListingAsync(listings[key])).ObservedStatus);
        }

        // Two SKUs, two offers, one listing id shared between them.
        var (ebayBlueId, ebayRedId) = (listings[(SalesChannel.Ebay, "FX-TEE-BLUE")], listings[(SalesChannel.Ebay, "FX-TEE-RED")]);
        var references = await fixture.WithDbAsync(db => db.ExternalReferences.AsNoTracking()
            .Where(r => r.OwnerId == ebayBlueId || r.OwnerId == ebayRedId)
            .ToListAsync());
        Assert.Equal(["o-FX-TEE-BLUE", "o-FX-TEE-RED"], references.Where(r => r.ResourceType == ExternalResourceType.Offer).Select(r => r.Value).Order());
        Assert.Equal(["110700009999", "110700009999"], references.Where(r => r.ResourceType == ExternalResourceType.Listing).Select(r => r.Value));
        var groups = await admin.GetFromJsonAsync<JsonElement>("/api/channel-listings/groups");
        Assert.Equal("110700009999", groups.EnumerateArray()
            .Single(g => g.GetProperty("channelMarketId").GetGuid() == markets[SalesChannel.Ebay]).GetProperty("externalListingId").GetString());

        // The marketplaces with live writes off were dry runs: built, not sent, not shown as live.
        Assert.Empty(fixture.Amazon.Requests);
        Assert.Empty(fixture.Walmart.Requests);

        var storefront = (await admin.GetFromJsonAsync<JsonElement>("/api/storefront/products")).EnumerateArray()
            .Where(p => p.GetProperty("sku").GetString()!.StartsWith("FX-")).ToDictionary(p => p.GetProperty("sku").GetString()!);
        Assert.Equal(["FX-SIMPLE", "FX-TEE-BLUE", "FX-TEE-RED"], storefront.Keys.Order());
        // Both variants show the product's shared description, and each its own price, stock and colour.
        Assert.Equal(storefront["FX-TEE-BLUE"].GetProperty("description").GetString(), storefront["FX-TEE-RED"].GetProperty("description").GetString());
        Assert.Equal((21m, 3, "Red"), (
            storefront["FX-TEE-RED"].GetProperty("price").GetDecimal(), storefront["FX-TEE-RED"].GetProperty("availableQuantity").GetInt32(),
            storefront["FX-TEE-RED"].GetProperty("options").GetProperty("Color").GetString()));
        Assert.Equal((20m, 8), (storefront["FX-TEE-BLUE"].GetProperty("price").GetDecimal(), storefront["FX-TEE-BLUE"].GetProperty("availableQuantity").GetInt32()));
    }
}

/// <summary>A host with the background worker really running, on a one-second tick.</summary>
public class RunningWorkerFixture : MarketplaceFixture
{
    protected override object MarketplaceSettings =>
        new { WorkerEnabled = true, WorkerIntervalSeconds = 1, AutoBackfill = true, LiveWritesEnabled = true, InventoryAccountingEnabled = true };
}

public class ChannelSyncWorkerTests(RunningWorkerFixture fixture) : IClassFixture<RunningWorkerFixture>
{
    [Fact]
    public async Task The_background_worker_picks_up_a_published_listing_without_being_asked()
    {
        using var admin = await fixture.AdminAsync("worker-running@example.com");
        var (_, marketId) = await fixture.CreateAccountAsync(admin, (int)SalesChannel.Website, "Worker site", "default");
        var (_, variantId) = await fixture.CreateProductAsync(admin, "WORKER-1");
        var listingId = await fixture.SaveListingAsync(admin, marketId, variantId);

        var published = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, $"/api/channel-listings/{listingId}/publish", new { });
        // The request returned at once, with the work only queued.
        Assert.Equal(HttpStatusCode.Accepted, published.StatusCode);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((await fixture.ListingAsync(listingId)).ObservedStatus != ListingObservedStatus.Live && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
        }

        Assert.Equal(ListingObservedStatus.Live, (await fixture.ListingAsync(listingId)).ObservedStatus);
        var health = await admin.GetFromJsonAsync<JsonElement>("/api/channels/sync/health");
        Assert.Equal(0, health.GetProperty("pendingJobs").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, health.GetProperty("lastSuccessAtUtc").ValueKind);
    }
}
