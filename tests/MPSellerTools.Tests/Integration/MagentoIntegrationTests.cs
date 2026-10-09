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
        // The listing was left recorded as held back by the address.
        Assert.Contains("account.settings.baseUrl", (await fixture.ListingAsync(listingId)).IssuesJson);

        // Put back for the other tests of this class.
        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, $"/api/channels/{accountId}", new
        {
            channel = Magento, name = "Our store", environment = 0, settings = new { baseUrl = Store },
            isEnabled = true, liveWritesEnabled = true, inventorySyncEnabled = false, orderImportEnabled = false, priceConflictPolicy = 2,
        }));
        // Saving a good address clears that by itself, without each listing being checked again by hand.
        Assert.Null((await fixture.ListingAsync(listingId)).IssuesJson);
    }

    [Theory]
    [InlineData("https://shop.example.test/", "https://shop.example.test")]
    // The address of the API itself is taken for the store's.
    [InlineData("https://shop.example.test/rest/V1/", "https://shop.example.test")]
    [InlineData("https://shop.example.test:8443/store/rest", "https://shop.example.test:8443/store")]
    // A folder that only starts with "rest" is part of the store's address.
    [InlineData("https://shop.example.test/restaurant", "https://shop.example.test/restaurant")]
    public void The_store_address_is_reduced_to_the_store_root(string entered, string expected) =>
        Assert.Equal(expected, TenantHost.Marketplace.Channels.MagentoApi.Root(entered));

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

        // Its page in the store, for the link shown beside the listing.
        var shown = await admin.GetFromJsonAsync<JsonElement>($"/api/channel-listings/{listingId}");
        Assert.True(shown.TryGetProperty("storeUrl", out var storeUrl), shown.GetRawText());
        Assert.Equal($"{Store}/catalog/product/view/id/77", storeUrl.GetString());

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

    private const string Tree = """
        {"id":1,"parent_id":0,"name":"Root Catalog","level":0,"product_count":0,"children_data":[
          {"id":2,"parent_id":1,"name":"Default Category","is_active":true,"level":1,"product_count":9,"children_data":[
            {"id":10,"parent_id":2,"name":"Medicine","is_active":true,"level":2,"product_count":4,"children_data":[
              {"id":11,"parent_id":10,"name":"Pain","is_active":false,"level":3,"product_count":1,"children_data":[]}]}]}]}
        """;

    [Fact]
    public async Task Categories_are_matched_by_name_created_in_the_store_where_missing_and_fall_back_to_a_default()
    {
        using var admin = await fixture.AdminAsync("magento-categories@example.com");
        var (_, marketId) = await MagentoAsync(admin);
        await fixture.WithDbAsync(db => db.CategoryMappings.Where(m => m.ChannelMarketId == marketId).ExecuteDeleteAsync());
        // Three categories of the company's own: one the store has by its path, one it has nothing of, one inside another.
        await fixture.CreateProductAsync(admin, "CAT-A", category: "Medicine / Pain");
        await fixture.CreateProductAsync(admin, "CAT-B", category: "Garden");
        var (_, nestedVariant) = await fixture.CreateProductAsync(admin, "CAT-C", category: "Medicine / Sleep");

        fixture.Magento.On("GET", "/rest/all/V1/categories", Tree);
        var page = await admin.GetFromJsonAsync<JsonElement>("/api/magento/categories");
        Assert.True(page.GetProperty("storeReachable").GetBoolean());
        // The store's tree, parents first, with each category's path below the store's root.
        Assert.Equal(["Default Category", "Medicine", "Medicine / Pain"], page.GetProperty("storeCategories").EnumerateArray().Select(c => c.GetProperty("path").GetString()));
        var mine = page.GetProperty("categories").EnumerateArray().Where(c => c.GetProperty("category").GetString() is "Medicine / Pain" or "Garden" or "Medicine / Sleep").ToList();
        Assert.Equal(3, mine.Count);
        Assert.All(mine, c => Assert.Equal(JsonValueKind.Null, c.GetProperty("storeCategoryId").ValueKind));

        // Matching takes the one the store already has, and creates nothing.
        var matched = await MarketplaceFixture.JsonAsync(await PostAsync(admin, "/api/magento/categories/match"));
        Assert.Contains(matched.GetProperty("items").EnumerateArray(), i => i.GetProperty("category").GetString() == "Medicine / Pain" && i.GetProperty("storeCategoryId").GetString() == "11");
        Assert.Equal(0, fixture.Magento.Count("POST", "/rest/all/V1/categories"));

        // A dry run of creating the rest says what it would do and does none of it.
        var plan = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/magento/categories/create-missing", new { dryRun = true }));
        Assert.True(plan.GetProperty("dryRun").GetBoolean());
        Assert.Equal(0, fixture.Magento.Count("POST", "/rest/all/V1/categories"));

        // For real: "Garden" under the root; "Sleep" under the "Medicine" the store already has, which is not made twice.
        var next = 100;
        fixture.Magento.On("POST", "/rest/all/V1/categories", _ => ChannelRouter.Json($$"""{"id":{{next++}}}"""));
        var created = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/magento/categories/create-missing", new { isActive = true, includeInMenu = false }));
        var mineCreated = created.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("category").GetString() is "Garden" or "Medicine / Sleep").ToList();
        Assert.Equal(2, mineCreated.Count);
        Assert.All(mineCreated, i => Assert.Equal(1, i.GetProperty("created").GetInt32()));
        var posts = fixture.Magento.Requests.Where(r => r.StartsWith($"POST {Store}/rest/all/V1/categories ", StringComparison.Ordinal)).ToList();
        Assert.Contains(posts, r => r.Contains("\"parent_id\":2,\"name\":\"Garden\",\"is_active\":true,\"include_in_menu\":false"));
        Assert.Contains(posts, r => r.Contains("\"parent_id\":10,\"name\":\"Sleep\""));
        Assert.DoesNotContain(posts, r => r.Contains("\"name\":\"Medicine\""));

        // A product in the new category is sent into it.
        var sleepId = mineCreated.Single(i => i.GetProperty("category").GetString() == "Medicine / Sleep").GetProperty("storeCategoryId").GetString();
        var listingId = await fixture.SaveListingAsync(admin, marketId, nestedVariant);
        var preview = await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/channel-listings/{listingId}/preview"));
        Assert.Contains($"\"category_id\":\"{sleepId}\"", preview.GetRawText());

        // With its mapping removed, it goes to the account's default category instead; with no default, to none.
        var mappingId = (await admin.GetFromJsonAsync<JsonElement>("/api/magento/categories")).GetProperty("categories").EnumerateArray()
            .Single(c => c.GetProperty("category").GetString() == "Medicine / Sleep").GetProperty("mappingId").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await TenantApiHelpers.DeleteWithAntiforgeryAsync(admin, $"/api/channels/category-mappings/{mappingId}")).StatusCode);
        Assert.DoesNotContain("category_links", (await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/channel-listings/{listingId}/preview"))).GetRawText());
        Assert.Equal(HttpStatusCode.NoContent, (await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, "/api/magento/categories/default", new { categoryId = "2" })).StatusCode);
        Assert.Contains("\"category_id\":\"2\"", (await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/channel-listings/{listingId}/preview"))).GetRawText());
        Assert.Equal("2", (await admin.GetFromJsonAsync<JsonElement>("/api/magento/categories")).GetProperty("defaultCategoryId").GetString());
        // The account's other settings are untouched by that.
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(admin, $"/api/channel-listings/{listingId}/validate")).StatusCode);
        await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, "/api/magento/categories/default", new { categoryId = (string?)null });

        // The store's categories rebuilt under new numbers: a mapping to one that is gone goes to its namesake, and one with no namesake is removed.
        await fixture.WithDbAsync(async db =>
        {
            var pain = await db.CategoryMappings.SingleAsync(m => m.ChannelMarketId == marketId && m.InternalCategory == "Medicine / Pain");
            pain.ExternalCategoryId = "7001";
            db.CategoryMappings.Add(new CategoryMapping { Id = Guid.NewGuid(), ChannelMarketId = marketId, InternalCategory = "Old Range", ExternalCategoryId = "7002" });
            return await db.SaveChangesAsync();
        });
        var before = (await admin.GetFromJsonAsync<JsonElement>("/api/magento/categories")).GetProperty("categories").EnumerateArray().ToList();
        var gone = before.Where(c => c.GetProperty("storeCategoryMissing").GetBoolean()).Select(c => c.GetProperty("category").GetString()).ToList();
        Assert.Contains("Medicine / Pain", gone);
        Assert.Contains("Old Range", gone);
        // While they are broken it is a standing alert, which names them and where to put them right.
        var alert = Assert.Single((await admin.GetFromJsonAsync<JsonElement>("/api/alerts")).EnumerateArray(), a => a.GetProperty("key").GetString() == "magento-categories");
        Assert.Contains("Old Range", alert.GetProperty("message").GetString());
        Assert.Equal("/magento/categories", alert.GetProperty("link").GetString());
        var repaired = await MarketplaceFixture.JsonAsync(await PostAsync(admin, "/api/magento/categories/repair"));
        // Resolved, it is gone by itself.
        Assert.DoesNotContain((await admin.GetFromJsonAsync<JsonElement>("/api/alerts")).EnumerateArray(), a => a.GetProperty("key").GetString() == "magento-categories");
        Assert.Equal(gone.Count, repaired.GetProperty("repointed").GetInt32() + repaired.GetProperty("removed").GetInt32());
        // Only a category that still has products is named as left with nowhere to go.
        Assert.DoesNotContain(repaired.GetProperty("unresolved").EnumerateArray(), c => c.GetString() is "Old Range" or "Medicine / Pain");
        var after = (await admin.GetFromJsonAsync<JsonElement>("/api/magento/categories")).GetProperty("categories").EnumerateArray().ToList();
        Assert.DoesNotContain(after, c => c.GetProperty("storeCategoryMissing").GetBoolean());
        Assert.Equal("11", after.Single(c => c.GetProperty("category").GetString() == "Medicine / Pain").GetProperty("storeCategoryId").GetString());
        Assert.DoesNotContain(after, c => c.GetProperty("category").GetString() == "Old Range");

        // The store unreachable: the company's own side of the page still comes.
        fixture.Magento.On("GET", "/rest/all/V1/categories", """{"message":"Service unavailable"}""", HttpStatusCode.ServiceUnavailable);
        var offline = await admin.GetFromJsonAsync<JsonElement>("/api/magento/categories");
        Assert.False(offline.GetProperty("storeReachable").GetBoolean());
        Assert.NotEmpty(offline.GetProperty("categories").EnumerateArray());
    }

    [Fact]
    public async Task Publishing_a_product_whose_category_is_not_mapped_finds_or_creates_it_in_the_store()
    {
        using var admin = await fixture.AdminAsync("magento-publish-category@example.com");
        var (_, marketId) = await MagentoAsync(admin);
        var (_, firstVariant) = await fixture.CreateProductAsync(admin, "MAG-VIT-1", category: "Medicine / Vitamins");
        var (_, secondVariant) = await fixture.CreateProductAsync(admin, "MAG-VIT-2", category: "Medicine / Vitamins");
        var first = await fixture.SaveListingAsync(admin, marketId, firstVariant);
        var second = await fixture.SaveListingAsync(admin, marketId, secondVariant);

        fixture.Magento.On("GET", "/rest/all/V1/categories", Tree);
        fixture.Magento.On("POST", "/rest/all/V1/categories", """{"id":120}""");
        fixture.Magento.On("POST", "/rest/all/V1/products", body => ChannelRouter.Json(
            $$"""{"id":{{(body.Contains("MAG-VIT-2", StringComparison.Ordinal) ? 92 : 91)}},"price":20,"status":1,"extension_attributes":{"stock_item":{"qty":10,"is_in_stock":true} } }"""));

        await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/channel-listings/{first}/publish"));
        await fixture.SyncAsync();

        // "Vitamins" is created under the "Medicine" the store already has, and the product is sent into it.
        var made = Assert.Single(fixture.Magento.Requests, r => r.StartsWith($"POST {Store}/rest/all/V1/categories ", StringComparison.Ordinal));
        Assert.Contains("\"parent_id\":10,\"name\":\"Vitamins\",\"is_active\":true,\"include_in_menu\":true", made);
        Assert.Contains("\"category_links\":[{\"category_id\":\"120\",\"position\":0}]",
            Assert.Single(fixture.Magento.Requests, r => r.StartsWith($"POST {Store}/rest/all/V1/products ", StringComparison.Ordinal)));
        Assert.Equal(ListingObservedStatus.Live, (await fixture.ListingAsync(first)).ObservedStatus);
        Assert.Equal("120", await fixture.WithDbAsync(db => db.CategoryMappings
            .Where(m => m.ChannelMarketId == marketId && m.InternalCategory == "Medicine / Vitamins").Select(m => m.ExternalCategoryId).SingleAsync()));

        // The next product of that category follows the mapping: the store is not asked or added to again.
        await MarketplaceFixture.JsonAsync(await PostAsync(admin, $"/api/channel-listings/{second}/publish"));
        await fixture.SyncAsync();
        Assert.Equal(1, fixture.Magento.Count("GET", "/rest/all/V1/categories"));
        Assert.Equal(1, fixture.Magento.Count("POST", "/rest/all/V1/categories"));
        Assert.Contains(fixture.Magento.Requests, r => r.Contains("\"sku\":\"MAG-VIT-2\"") && r.Contains("\"category_id\":\"120\""));
    }

    [Fact]
    public async Task Reading_the_store_waits_out_a_store_that_is_restarting()
    {
        using var admin = await fixture.AdminAsync("magento-restarting@example.com");
        await MagentoAsync(admin);
        TenantHost.Services.MagentoSync.ReadRetryDelay = TimeSpan.Zero;
        try
        {
            // Down for the first two tries, as while it is being redeployed; then it answers.
            var asked = 0;
            fixture.Magento.On("GET", "/rest/all/V1/products?", _ => ++asked <= 2
                ? ChannelRouter.Json("""{"message":"Service unavailable"}""", HttpStatusCode.ServiceUnavailable)
                : ChannelRouter.Json("""{"total_count":1,"items":[{"id":501,"sku":"MAG-RESTART","name":"Came back","price":5,"status":1}]}"""));
            var read = await MarketplaceFixture.JsonAsync(await PostAsync(admin, "/api/magento/import/listings"));
            Assert.Equal(3, asked);
            Assert.True(read.GetProperty("listings").GetInt32() >= 1);
            Assert.True(await fixture.WithDbAsync(db => db.Products.AnyAsync(p => p.Sku == "MAG-RESTART")));

            // A refusal that waiting will not change is not tried again.
            asked = 0;
            fixture.Magento.On("GET", "/rest/all/V1/products?", _ =>
            {
                asked++;
                return ChannelRouter.Json("""{"message":"The consumer isn't authorized to access %resources."}""", HttpStatusCode.Unauthorized);
            });
            Assert.Equal(HttpStatusCode.BadGateway, (await PostAsync(admin, "/api/magento/import/listings")).StatusCode);
            Assert.Equal(1, asked);
        }
        finally
        {
            TenantHost.Services.MagentoSync.ReadRetryDelay = TimeSpan.FromSeconds(10);
        }
    }

    [Fact]
    public async Task Store_products_are_removed_by_sku_prefix_except_the_ones_still_listed_here()
    {
        using var admin = await fixture.AdminAsync("magento-remove@example.com");
        var (_, marketId) = await MagentoAsync(admin);
        // One of the old products is still listed from here; it is not to be deleted.
        var (_, variantId) = await fixture.CreateProductAsync(admin, "OLD-0002");
        await fixture.SaveListingAsync(admin, marketId, variantId);

        fixture.Magento.On("GET", "/rest/all/V1/products?", """
            {"total_count":4,"items":[{"sku":"OLD-0001"},{"sku":"OLD-0002"},{"sku":"OLD-0003"},{"sku":"old-lower"}]}
            """);
        fixture.Magento.On("DELETE", "/rest/all/V1/products/", "true");

        // A prefix too short to be meant is refused, and the default is only to count.
        Assert.Equal(HttpStatusCode.BadRequest, (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/magento/store-products/remove", new { skuPrefix = "O" })).StatusCode);
        var count = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/magento/store-products/remove", new { skuPrefix = "OLD-" }));
        Assert.True(count.GetProperty("dryRun").GetBoolean());
        Assert.Equal((2, 1), (count.GetProperty("removed").GetInt32(), count.GetProperty("kept").GetInt32()));
        Assert.Equal(0, fixture.Magento.Count("DELETE", "/rest/all/V1/products/"));
        // The search asks the store for SKUs beginning with the prefix.
        Assert.Contains(fixture.Magento.Requests, r => r.Contains("[value]=OLD-%25") && r.Contains("[condition_type]=like"));

        var removed = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/magento/store-products/remove", new { skuPrefix = "OLD-", dryRun = false, limit = 50 }));
        Assert.Equal((2, 1), (removed.GetProperty("removed").GetInt32(), removed.GetProperty("kept").GetInt32()));
        var deletes = fixture.Magento.Requests.Where(r => r.StartsWith("DELETE ", StringComparison.Ordinal)).ToList();
        Assert.Equal([$"DELETE {Store}/rest/all/V1/products/OLD-0001", $"DELETE {Store}/rest/all/V1/products/OLD-0003"], deletes);
    }
}
