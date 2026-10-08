using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// The Magento store, with a fake of its REST API standing in for it: the
/// connection check, a listing on its way to the store's catalog and back,
/// and reading the catalog as the listings there. These prove the pipeline
/// and the request shapes; they are not calls to a real store.
/// </summary>
public class MagentoIntegrationTests(MarketplaceFixture fixture) : IClassFixture<MarketplaceFixture>
{
    private const int Magento = (int)SalesChannel.Magento;
    private const string Store = "https://shop.example.test";

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url) => TenantApiHelpers.PostJsonWithAntiforgeryAsync(client, url, new { });

    /// <summary>The one Magento account of this fixture's database, pointed at the fake store, with a token saved.</summary>
    private async Task<(Guid AccountId, Guid MarketId)> MagentoAsync(HttpClient admin)
    {
        fixture.Magento.Reset();
        var existing = (await admin.GetFromJsonAsync<JsonElement>("/api/channels")).EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("channel").GetInt32() == Magento);
        if (existing.ValueKind == JsonValueKind.Object)
        {
            return (existing.GetProperty("id").GetGuid(), existing.GetProperty("markets")[0].GetProperty("id").GetGuid());
        }

        var created = await fixture.CreateAccountAsync(admin, Magento, "Our store", "default", new { baseUrl = Store });
        var saved = await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, $"/api/channels/{created.AccountId}/credentials", new { credentials = new { accessToken = "magento-integration-token" } });
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        return created;
    }

    [Fact]
    public async Task The_connection_check_reaches_the_store_and_reports_its_refusal()
    {
        using var admin = await fixture.AdminAsync("magento-connect@example.com");
        await MagentoAsync(admin);

        fixture.Magento.On("GET", "/rest/V1/store/storeConfigs", """
            [{"id":1,"code":"default","base_url":"https://shop.example.test/","base_currency_code":"USD"},
             {"id":2,"code":"de","base_url":"https://shop.example.test/de/","base_currency_code":"USD"}]
            """);
        var ok = await MarketplaceFixture.JsonAsync(await PostAsync(admin, "/api/magento/test"));
        Assert.Equal("https://shop.example.test/", ok.GetProperty("storeAddress").GetString());
        Assert.Equal(["default", "de"], ok.GetProperty("storeViews").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal("USD", ok.GetProperty("currency").GetString());
        Assert.Equal(1, fixture.Magento.Count("GET", $"{Store}/rest/V1/store/storeConfigs"));

        // The store turns the token down: its own words come back, as a gateway error.
        fixture.Magento.On("GET", "/rest/V1/store/storeConfigs", """{"message":"The consumer isn't authorized to access %resources."}""", HttpStatusCode.Unauthorized);
        var refused = await PostAsync(admin, "/api/magento/test");
        Assert.Equal(HttpStatusCode.BadGateway, refused.StatusCode);
        Assert.Contains("isn't authorized", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());

        // An employee cannot use it.
        await fixture.CreateUserAsync("magento-employee@example.com", "Password123!", "Employee");
        using var employee = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(employee, "magento-employee@example.com", "Password123!"));
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(employee, "/api/magento/test")).StatusCode);
    }

    [Fact]
    public async Task A_store_address_that_is_not_a_public_https_one_is_never_called()
    {
        using var admin = await fixture.AdminAsync("magento-address@example.com");
        var (accountId, marketId) = await MagentoAsync(admin);
        var (_, variantId) = await fixture.CreateProductAsync(admin, "MAG-ADDRESS");
        var listingId = await fixture.SaveListingAsync(admin, marketId, variantId);

        foreach (var address in new[] { "http://shop.example.test", "https://localhost:7201", "https://10.0.0.5", "https://192.168.1.20/shop", "not an address" })
        {
            await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, $"/api/channels/{accountId}", new
            {
                channel = Magento, name = "Our store", environment = 0, settings = new { baseUrl = address },
                isEnabled = true, liveWritesEnabled = true, inventorySyncEnabled = false, orderImportEnabled = false, priceConflictPolicy = 2,
            }));

            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(admin, "/api/magento/test")).StatusCode);
            var check = await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/channel-listings/{listingId}/validate"));
            Assert.Contains(check.GetProperty("issues").EnumerateArray(), i => i.GetProperty("path").GetString() == "account.settings.baseUrl");
        }
        Assert.Empty(fixture.Magento.Requests);

        // Put back for the other tests of this class.
        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, $"/api/channels/{accountId}", new
        {
            channel = Magento, name = "Our store", environment = 0, settings = new { baseUrl = Store },
            isEnabled = true, liveWritesEnabled = true, inventorySyncEnabled = false, orderImportEnabled = false, priceConflictPolicy = 2,
        }));
    }

    [Fact]
    public async Task Publishing_saves_the_product_in_the_store_and_taking_it_off_sale_disables_it()
    {
        using var admin = await fixture.AdminAsync("magento-publish@example.com");
        var (_, marketId) = await MagentoAsync(admin);
        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, "/api/channels/category-mappings", new
        {
            channelMarketId = marketId, internalCategory = "Mugs", externalCategoryId = "12",
        }));
        var (_, variantId) = await fixture.CreateProductAsync(admin, "MAG-MUG", price: 20m, stock: 10);
        var listingId = await fixture.SaveListingAsync(admin, marketId, variantId, new { attributes = new { color = "Blue" } });

        var check = await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/channel-listings/{listingId}/validate"));
        Assert.True(check.GetProperty("valid").GetBoolean(), check.GetRawText());

        fixture.Magento.On("POST", "/rest/all/V1/products", """
            {"id":77,"sku":"MAG-MUG","name":"Product MAG-MUG","price":20,"status":1,
             "extension_attributes":{"stock_item":{"qty":10,"is_in_stock":true}}}
            """);
        await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/channel-listings/{listingId}/publish"));
        await fixture.SyncAsync();

        var sent = Assert.Single(fixture.Magento.Requests, r => r.StartsWith($"POST {Store}/rest/all/V1/products ", StringComparison.Ordinal));
        Assert.Contains("\"sku\":\"MAG-MUG\"", sent);
        Assert.Contains("\"name\":\"Product MAG-MUG\"", sent);
        Assert.Contains("\"price\":20", sent);
        // Enabled, as it is meant to be on sale; a simple product, visible in catalog and search.
        Assert.Contains("\"status\":1", sent);
        Assert.Contains("\"visibility\":4,\"type_id\":\"simple\",\"attribute_set_id\":4", sent);
        Assert.Contains("\"stock_item\":{\"qty\":10,\"is_in_stock\":true}", sent);
        Assert.Contains("\"category_links\":[{\"category_id\":\"12\",\"position\":0}]", sent);
        Assert.Contains("{\"attribute_code\":\"description\",\"value\":\"A base description.\"}", sent);
        Assert.Contains("{\"attribute_code\":\"color\",\"value\":\"Blue\"}", sent);

        var listing = await fixture.ListingAsync(listingId);
        Assert.Equal(ListingObservedStatus.Live, listing.ObservedStatus);
        Assert.Equal(20m, listing.ObservedPrice);
        Assert.Equal(10, listing.ObservedQuantity);
        // The store's own number for the product is kept.
        Assert.Equal("77", await fixture.WithDbAsync(db => db.ExternalReferences
            .Where(r => r.OwnerId == listingId && r.ResourceType == ExternalResourceType.CatalogItem).Select(r => r.Value).SingleAsync()));

        // Off sale: the product stays in the store, disabled.
        fixture.Magento.On("PUT", "/rest/all/V1/products/MAG-MUG", """{"id":77,"sku":"MAG-MUG","status":2}""");
        await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/channel-listings/{listingId}/deactivate"));
        await fixture.SyncAsync();

        var disabled = Assert.Single(fixture.Magento.Requests, r => r.StartsWith($"PUT {Store}/rest/all/V1/products/MAG-MUG ", StringComparison.Ordinal));
        Assert.EndsWith("{\"product\":{\"status\":2}}", disabled);
        Assert.Equal(ListingObservedStatus.Inactive, (await fixture.ListingAsync(listingId)).ObservedStatus);
    }

    [Fact]
    public async Task Reading_the_store_records_its_products_as_listings_and_drops_the_ones_it_no_longer_has()
    {
        using var admin = await fixture.AdminAsync("magento-listings@example.com");
        await MagentoAsync(admin);
        // A product the company already keeps under the same SKU as one in the store.
        var keptId = (await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/products", new { sku = "STORE-KEPT", name = "Our own name", price = 5.00m, stockQuantity = 9 }))).GetProperty("id").GetString();

        fixture.Magento.On("GET", "/rest/all/V1/products?", """
            {"total_count":3,"items":[
              {"id":501,"sku":"STORE-KEPT","name":"Store lamp","price":24.5,"status":1,"type_id":"simple"},
              {"id":502,"sku":"STORE-NEW","name":"Store vase","price":"8.0000","status":1,"type_id":"simple"},
              {"id":503,"sku":"STORE-OFF","name":"Store rug","price":60,"status":2,"type_id":"simple"}]}
            """);
        fixture.Magento.On("GET", "/rest/V1/stockItems/lowStock/", """
            {"total_count":3,"items":[
              {"product_id":501,"qty":7,"is_in_stock":true},
              {"product_id":502,"qty":0,"is_in_stock":false},
              {"product_id":503,"qty":4,"is_in_stock":true}]}
            """);

        var imported = await MarketplaceFixture.JsonAsync(await PostAsync(admin, "/api/magento/import/listings"));
        Assert.Equal(3, imported.GetProperty("listings").GetInt32());
        // The two products the catalog here did not have.
        Assert.Equal(2, imported.GetProperty("created").GetInt32());

        var page = await admin.GetFromJsonAsync<JsonElement>($"/api/listings?channel={Magento}");
        Assert.True(page.GetProperty("connected").GetBoolean());
        var mine = page.GetProperty("listings").EnumerateArray().OrderBy(l => l.GetProperty("externalId").GetString()).ToList();
        Assert.Equal(["501", "502", "503"], mine.Select(l => l.GetProperty("externalId").GetString()));

        var lamp = mine[0];
        Assert.Equal(keptId, lamp.GetProperty("productId").GetString());
        // The company's own product is left as it was.
        Assert.Equal("Our own name", lamp.GetProperty("productName").GetString());
        Assert.Equal(Magento, lamp.GetProperty("channel").GetInt32());
        Assert.Equal($"{Store}/catalog/product/view/id/501", lamp.GetProperty("url").GetString());
        Assert.Equal((int)ListingStatus.Live, lamp.GetProperty("status").GetInt32());
        Assert.Equal(24.5m, lamp.GetProperty("price").GetDecimal());
        Assert.Equal("USD", lamp.GetProperty("currency").GetString());
        Assert.Equal(7, lamp.GetProperty("availableQuantity").GetInt32());

        Assert.Equal("Store vase", mine[1].GetProperty("productName").GetString());
        Assert.Equal(8.0m, mine[1].GetProperty("price").GetDecimal());
        Assert.Equal((int)ListingStatus.OutOfStock, mine[1].GetProperty("status").GetInt32());
        // Disabled in the store: not on sale, whatever its stock.
        Assert.Equal((int)ListingStatus.Ended, mine[2].GetProperty("status").GetInt32());

        // The vase is deleted from the store, and the stock report is refused this time.
        fixture.Magento.On("GET", "/rest/all/V1/products?", """
            {"total_count":2,"items":[
              {"id":501,"sku":"STORE-KEPT","name":"Store lamp","price":24.5,"status":1,"type_id":"simple"},
              {"id":503,"sku":"STORE-OFF","name":"Store rug","price":60,"status":2,"type_id":"simple"}]}
            """);
        fixture.Magento.On("GET", "/rest/V1/stockItems/lowStock/", """{"message":"Request does not match any route."}""", HttpStatusCode.NotFound);

        var again = await MarketplaceFixture.JsonAsync(await PostAsync(admin, "/api/magento/import/listings"));
        Assert.Equal(2, again.GetProperty("listings").GetInt32());
        Assert.Equal(0, again.GetProperty("created").GetInt32());

        var left = await fixture.WithDbAsync(db => db.Listings.Where(l => l.Channel == SalesChannel.Magento).OrderBy(l => l.ExternalId).ToListAsync());
        Assert.Equal(["501", "503"], left.Select(l => l.ExternalId));
        // Without the stock report a listing is still recorded, with no quantity claimed.
        Assert.Null(left[0].AvailableQuantity);
        Assert.Equal(ListingStatus.Live, left[0].Status);
        // The product made for the vase stays in the catalog; only its listing went.
        Assert.True(await fixture.WithDbAsync(db => db.Products.AnyAsync(p => p.Sku == "STORE-NEW")));
    }

    [Fact]
    public async Task Orders_are_read_from_the_store_once_each_with_the_parent_row_of_a_configurable_product()
    {
        using var admin = await fixture.AdminAsync("magento-orders@example.com");
        var (accountId, _) = await MagentoAsync(admin);
        await fixture.CreateProductAsync(admin, "MAG-ORDERED", stock: 10);

        fixture.Magento.On("GET", "/rest/V1/orders?", """
            {"total_count":1,"items":[{"increment_id":"000000042","state":"processing","created_at":"2026-03-04 10:15:00","order_currency_code":"USD",
              "items":[
                {"item_id":900,"sku":"MAG-ORDERED","name":"Ordered thing","qty_ordered":2,"price":20,"product_type":"configurable"},
                {"item_id":901,"sku":"MAG-ORDERED","name":"Ordered thing","qty_ordered":2,"price":0,"product_type":"simple","parent_item_id":900}]}]}
            """);

        await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/channels/{accountId}/import-orders"));
        await fixture.SyncAsync();
        // A second read of the same order changes nothing.
        await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/channels/{accountId}/import-orders"));
        await fixture.SyncAsync();

        Assert.Contains(fixture.Magento.Requests, r => r.Contains("searchCriteria[filter_groups][0][filters][0][field]=updated_at", StringComparison.Ordinal));
        var orders = (await admin.GetFromJsonAsync<JsonElement>("/api/orders")).EnumerateArray()
            .Where(o => o.GetProperty("orderNumber").GetString()!.Contains("000000042", StringComparison.Ordinal)).ToList();
        var order = Assert.Single(orders);
        Assert.StartsWith("MAG-", order.GetProperty("orderNumber").GetString());
        Assert.Equal((int)OrderStatus.InProgress, order.GetProperty("status").GetInt32());
        var line = Assert.Single(order.GetProperty("items").EnumerateArray());
        Assert.Equal("MAG-ORDERED", line.GetProperty("productSku").GetString());
        Assert.Equal(2, line.GetProperty("quantity").GetInt32());
        Assert.Equal(20m, line.GetProperty("unitPrice").GetDecimal());
    }
}
