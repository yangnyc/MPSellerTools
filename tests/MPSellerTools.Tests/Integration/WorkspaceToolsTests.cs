using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.TenantHost.Marketplace;
using MPSellerTools.TenantHost.Marketplace.Channels;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// The day-to-day tools around the catalog: importing products from a file,
/// the stock overview and ledger, low-stock tasks, the sales dashboard,
/// looking an item up on Amazon, and the pacing of calls to a channel.
/// </summary>
public class WorkspaceToolsTests(MarketplaceFixture fixture) : IClassFixture<MarketplaceFixture>
{
    private static Task<HttpResponseMessage> ImportAsync(HttpClient admin, bool dryRun, params object[] rows) =>
        TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, $"/api/products/import?dryRun={dryRun}", new { rows });

    [Fact]
    public async Task Importing_products_reports_first_and_then_creates_and_updates_by_sku_leaving_bad_rows_out()
    {
        using var admin = await fixture.AdminAsync("tools-import@example.com");
        var (existingId, variantId) = await fixture.CreateProductAsync(admin, "IMP-OLD", price: 10m, stock: 3);
        object[] rows =
        [
            new { sku = "IMP-OLD", name = "Renamed", price = 12.5m, stockQuantity = 8 },
            new { sku = "IMP-NEW", name = "Brand new", price = 4m, stockQuantity = 2 },
            new { sku = "IMP-NEW", name = "Twice in the file", price = 4m, stockQuantity = 2 },
            new { sku = "IMP-BAD", name = "", price = 1m, stockQuantity = 1 },
            new { sku = "IMP-NEG", name = "Negative", price = -1m, stockQuantity = 1 },
        ];

        var preview = await MarketplaceFixture.JsonAsync(await ImportAsync(admin, dryRun: true, rows));
        Assert.Equal((1, 1, 3), (preview.GetProperty("created").GetInt32(), preview.GetProperty("updated").GetInt32(), preview.GetProperty("errors").GetArrayLength()));
        Assert.Equal([3, 4, 5], preview.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("row").GetInt32()));
        // A dry run saved nothing.
        Assert.False(await fixture.WithDbAsync(db => db.Products.AnyAsync(p => p.Sku == "IMP-NEW")));
        Assert.Equal(10m, await fixture.WithDbAsync(db => db.Products.Where(p => p.Id == existingId).Select(p => p.Price).SingleAsync()));

        var result = await MarketplaceFixture.JsonAsync(await ImportAsync(admin, dryRun: false, rows));
        Assert.Equal((1, 1), (result.GetProperty("created").GetInt32(), result.GetProperty("updated").GetInt32()));
        await fixture.WithDbAsync(async db =>
        {
            var updated = await db.Products.SingleAsync(p => p.Id == existingId);
            Assert.Equal(("Renamed", 12.5m, 8), (updated.Name, updated.Price, updated.StockQuantity));
            // The default variant and its balance follow, as with any other edit of the product.
            Assert.Equal(12.5m, (await db.ProductVariants.SingleAsync(v => v.Id == variantId)).Price);
            Assert.Equal(8, (await db.InventoryBalances.SingleAsync(b => b.VariantId == variantId)).OnHand);
            var created = await db.Products.SingleAsync(p => p.Sku == "IMP-NEW");
            Assert.Equal(2, (await db.InventoryBalances.SingleAsync(b => db.ProductVariants.Any(v => v.Id == b.VariantId && v.ProductId == created.Id))).OnHand);
            Assert.False(await db.Products.AnyAsync(p => p.Sku == "IMP-BAD" || p.Sku == "IMP-NEG"));
            return 0;
        });

        // The same file again changes nothing.
        var again = await MarketplaceFixture.JsonAsync(await ImportAsync(admin, dryRun: false, rows));
        Assert.Equal((0, 0, 2), (again.GetProperty("created").GetInt32(), again.GetProperty("updated").GetInt32(), again.GetProperty("unchanged").GetInt32()));

        Assert.Equal(HttpStatusCode.BadRequest, (await ImportAsync(admin, dryRun: true)).StatusCode);
    }

    [Fact]
    public async Task The_stock_overview_and_ledger_show_a_count_and_what_is_left_to_sell()
    {
        using var admin = await fixture.AdminAsync("tools-stock@example.com");
        var (_, variantId) = await fixture.CreateProductAsync(admin, "STOCK-1", stock: 6);
        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, $"/api/catalog/variants/{variantId}/inventory", new { onHand = 9, safetyStock = 2 }));

        var overview = await admin.GetFromJsonAsync<JsonElement>("/api/catalog/inventory");
        Assert.True(overview.GetProperty("accountingEnabled").GetBoolean());
        var item = overview.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("sku").GetString() == "STOCK-1");
        Assert.Equal((9, 2, 7), (item.GetProperty("onHand").GetInt32(), item.GetProperty("safetyStock").GetInt32(), item.GetProperty("availableToSell").GetInt32()));
        Assert.Equal("Product STOCK-1", item.GetProperty("productName").GetString());

        var movements = await admin.GetFromJsonAsync<JsonElement>($"/api/catalog/inventory/movements?variantId={variantId}");
        var count = movements.EnumerateArray().First();
        Assert.Equal(("STOCK-1", 3), (count.GetProperty("sku").GetString(), count.GetProperty("onHandDelta").GetInt32()));
    }

    [Fact]
    public async Task Low_stock_creates_one_restocking_task_per_variant_until_that_task_is_closed()
    {
        using var admin = await fixture.AdminAsync("tools-lowstock@example.com");
        var me = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var adminId = me.GetProperty("id").GetGuid();
        await fixture.CreateProductAsync(admin, "LOW-1", stock: 2);
        await fixture.CreateProductAsync(admin, "PLENTY-1", stock: 500);
        Task<int> RunAsync() => fixture.WithScopeAsync(services => services.GetRequiredService<LowStockMonitor>().RunAsync(default));
        Task<List<WorkItem>> TasksAsync(string sku) => fixture.WithDbAsync(db => db.WorkItems.AsNoTracking().Where(t => t.Title == "Restock " + sku).ToListAsync());

        // Off until the company sets it up.
        Assert.Equal(0, await RunAsync());

        // Provisioning gives every company its settings row; this host was not provisioned.
        await fixture.WithDbAsync(async db =>
        {
            if (!await db.CompanySettings.AnyAsync())
            {
                db.CompanySettings.Add(new CompanySettings { Id = Guid.NewGuid(), CompanyName = "Tools Co", UpdatedAtUtc = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            return 0;
        });

        var settings = await admin.GetFromJsonAsync<JsonElement>("/api/settings");
        object Update(int? threshold, Guid? assignee, string rowVersion) =>
            new { companyName = settings.GetProperty("companyName").GetString(), lowStockThreshold = threshold, lowStockAssigneeId = assignee, rowVersion };
        var rowVersion = settings.GetProperty("rowVersion").GetString()!;
        // A level without a person, or a person who does not exist, is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, "/api/settings", Update(3, null, rowVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, "/api/settings", Update(3, Guid.NewGuid(), rowVersion))).StatusCode);
        var saved = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, "/api/settings", Update(3, adminId, rowVersion)));
        Assert.Equal(3, saved.GetProperty("lowStockThreshold").GetInt32());

        Assert.True(await RunAsync() >= 1);
        var task = Assert.Single(await TasksAsync("LOW-1"));
        Assert.Equal((adminId, WorkItemStatus.Open), (task.AssignedUserId, task.Status));
        Assert.Empty(await TasksAsync("PLENTY-1"));

        // Still low, still one task.
        Assert.Equal(0, await RunAsync());
        Assert.Single(await TasksAsync("LOW-1"));

        // Once that task is done and the stock is still low, the next pass raises it again.
        await fixture.WithDbAsync(db => db.WorkItems.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, WorkItemStatus.Done)));
        Assert.Equal(1, await RunAsync());
        Assert.Equal(2, (await TasksAsync("LOW-1")).Count);

        // The admin's dashboard lists it among the lowest.
        var dashboard = await admin.GetFromJsonAsync<JsonElement>("/api/dashboard");
        var sales = dashboard.GetProperty("sales");
        Assert.Equal(3, sales.GetProperty("lowStockThreshold").GetInt32());
        Assert.Contains(sales.GetProperty("lowStock").EnumerateArray(), i => i.GetProperty("sku").GetString() == "LOW-1");
        Assert.Equal(30, sales.GetProperty("daily").GetArrayLength());
    }

    [Fact]
    public async Task The_dashboard_counts_revenue_by_where_the_order_came_from_and_leaves_cancelled_orders_out()
    {
        using var admin = await fixture.AdminAsync("tools-dashboard@example.com");
        var (productId, _) = await fixture.CreateProductAsync(admin, "DASH-1", price: 25m, stock: 50);
        var before = (await admin.GetFromJsonAsync<JsonElement>("/api/dashboard")).GetProperty("sales").GetProperty("revenue").GetDecimal();

        var kept = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/orders", new { items = new[] { new { productId, quantity = 2 } }, assignedUserId = (Guid?)null }));
        var cancelled = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/orders", new { items = new[] { new { productId, quantity = 1 } }, assignedUserId = (Guid?)null }));
        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, $"/api/orders/{cancelled.GetProperty("id").GetGuid()}/status",
            new { status = (int)OrderStatus.Cancelled, rowVersion = cancelled.GetProperty("rowVersion").GetString() }));

        var sales = (await admin.GetFromJsonAsync<JsonElement>("/api/dashboard")).GetProperty("sales");
        Assert.Equal(before + kept.GetProperty("total").GetDecimal(), sales.GetProperty("revenue").GetDecimal());
        Assert.Contains(sales.GetProperty("channels").EnumerateArray(), c => c.GetProperty("channel").GetString() == "Created here");
        // Today is the last of the thirty days, and holds the order.
        Assert.True(sales.GetProperty("daily").EnumerateArray().Last().GetProperty("revenue").GetDecimal() >= 50m);

        // An employee gets their own numbers and none of the company's sales.
        await fixture.CreateUserAsync("tools-dashboard-employee@example.com", "Password123!", "Employee");
        using var employee = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(employee, "tools-dashboard-employee@example.com", "Password123!"));
        Assert.Equal(JsonValueKind.Null, (await employee.GetFromJsonAsync<JsonElement>("/api/dashboard")).GetProperty("sales").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/catalog/inventory")).StatusCode);
    }

    [Fact]
    public async Task Searching_amazons_catalog_returns_its_items_and_is_refused_while_live_access_is_off()
    {
        using var admin = await fixture.AdminAsync("tools-asin@example.com");
        var (offId, _) = await fixture.CreateAccountAsync(admin, (int)SalesChannel.Amazon, "Search off", "ATVPDKIKX0DER", sellerId: "SELLER1", liveWrites: false);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.GetAsync($"/api/channels/{offId}/catalog-search?q=mug")).StatusCode);

        var (accountId, _) = await fixture.CreateAccountAsync(admin, (int)SalesChannel.Amazon, "Search on", "ATVPDKIKX0DER", sellerId: "SELLER1");
        Assert.Equal(HttpStatusCode.NoContent, (await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, $"/api/channels/{accountId}/credentials", new
        {
            credentials = new { clientId = "amzn-client", clientSecret = "amzn-client-secret-value", refreshToken = "amzn-refresh-token-value" },
        })).StatusCode);
        fixture.Amazon.On("POST", "/auth/o2/token", """{"access_token":"amzn-access-token-value","expires_in":3600,"token_type":"bearer"}""");
        fixture.Amazon.On("GET", "/catalog/2022-04-01/items", """
            {"items":[
              {"asin":"B00TEST001","summaries":[{"marketplaceId":"ATVPDKIKX0DER","itemName":"Blue mug","brand":"Mugco"}]},
              {"asin":"B00TEST002","summaries":[]}
            ]}
            """);

        var found = await admin.GetFromJsonAsync<JsonElement>($"/api/channels/{accountId}/catalog-search?q=blue%20mug");
        Assert.Equal(["B00TEST001", "B00TEST002"], found.EnumerateArray().Select(i => i.GetProperty("catalogItemId").GetString()));
        Assert.Equal("Blue mug", found[0].GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/channels/{accountId}/catalog-search?q=x")).StatusCode);
    }

    [Fact]
    public async Task A_marketplaces_listings_are_what_it_reported_as_posted_and_nothing_that_was_only_prepared()
    {
        using var admin = await fixture.AdminAsync("tools-listings@example.com");
        var (_, liveVariant) = await fixture.CreateProductAsync(admin, "POSTED-1");
        var (_, draftVariant) = await fixture.CreateProductAsync(admin, "DRAFT-1");
        var (accountId, marketId) = await fixture.CreateAccountAsync(admin, (int)SalesChannel.Amazon, "Listings store", "ATVPDKIKX0DER", sellerId: "SELLER1");
        var liveId = await fixture.SaveListingAsync(admin, marketId, liveVariant);
        await fixture.SaveListingAsync(admin, marketId, draftVariant);
        // Amazon confirming the first one as buyable, as a status read would record it.
        await fixture.WithDbAsync(async db =>
        {
            await db.ChannelListings.Where(l => l.Id == liveId).ExecuteUpdateAsync(s => s
                .SetProperty(l => l.ObservedStatus, ListingObservedStatus.Live)
                .SetProperty(l => l.ObservedPrice, 19.5m)
                .SetProperty(l => l.ObservedQuantity, 0)
                .SetProperty(l => l.ObservedAtUtc, DateTime.UtcNow));
            db.ExternalReferences.Add(new ExternalReference
            {
                Id = Guid.NewGuid(), ChannelAccountId = accountId, ChannelMarketId = marketId, OwnerType = ExternalOwnerType.ChannelListing,
                OwnerId = liveId, ResourceType = ExternalResourceType.CatalogItem, Value = "B00TESTLIVE", IsTestOnly = true, CreatedAtUtc = DateTime.UtcNow,
            });
            return await db.SaveChangesAsync();
        });

        var amazon = await admin.GetFromJsonAsync<JsonElement>($"/api/listings?channel={(int)SalesChannel.Amazon}");
        Assert.True(amazon.GetProperty("connected").GetBoolean());
        var posted = Assert.Single(amazon.GetProperty("listings").EnumerateArray(), l => l.GetProperty("productSku").GetString() is "POSTED-1" or "DRAFT-1");
        Assert.Equal(("POSTED-1", "B00TESTLIVE", 19.5m), (posted.GetProperty("productSku").GetString(), posted.GetProperty("externalId").GetString(), posted.GetProperty("price").GetDecimal()));
        // Posted with nothing left reads as out of stock; a sandbox, test-only number gets no link to the public site.
        Assert.Equal((int)ListingStatus.OutOfStock, posted.GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.Null, posted.GetProperty("url").ValueKind);

        // Another marketplace's page does not show it; the page for all of them does.
        var walmart = await admin.GetFromJsonAsync<JsonElement>($"/api/listings?channel={(int)SalesChannel.Walmart}");
        Assert.DoesNotContain(walmart.GetProperty("listings").EnumerateArray(), l => l.GetProperty("productSku").GetString() == "POSTED-1");
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/listings");
        Assert.Contains(all.GetProperty("listings").EnumerateArray(), l => l.GetProperty("productSku").GetString() == "POSTED-1");
    }

    [Fact]
    public async Task A_listings_own_title_price_cap_and_asin_are_saved_and_read_back_in_the_shape_the_edit_page_uses()
    {
        using var admin = await fixture.AdminAsync("tools-listing-edit@example.com");
        var (_, variantId) = await fixture.CreateProductAsync(admin, "EDIT-1", price: 20m, stock: 10);
        var (_, marketId) = await fixture.CreateAccountAsync(admin, (int)SalesChannel.Amazon, "Edit store", "ATVPDKIKX0DER", sellerId: "SELLER1");
        var listingId = await fixture.SaveListingAsync(admin, marketId, variantId);

        var saved = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, "/api/channel-listings", new
        {
            channelMarketId = marketId,
            variantId,
            sellerSku = "EDIT-1",
            fulfillmentMode = 0,
            externalCategoryId = "DRINKING_CUP",
            content = new { title = new { value = "A title for Amazon" } },
            priceOverride = 24.5m,
            quantityCap = 3,
            existingCatalogItemId = "B00EDIT001",
        }));
        Assert.Equal(listingId, saved.GetProperty("id").GetGuid());
        Assert.Equal("A title for Amazon", saved.GetProperty("contentOverrides").GetProperty("title").GetProperty("value").GetString());
        Assert.Equal("B00EDIT001", saved.GetProperty("references").GetProperty("CatalogItem").GetString());
        Assert.Equal(("A title for Amazon", 24.5m, 3), (saved.GetProperty("effectiveTitle").GetString(), saved.GetProperty("effectivePrice").GetDecimal(), saved.GetProperty("effectiveQuantity").GetInt32()));

        // Emptied again, it follows the product: its title, its price, all of its stock, and no ASIN.
        var cleared = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, "/api/channel-listings", new
        {
            channelMarketId = marketId,
            variantId,
            sellerSku = "EDIT-1",
            fulfillmentMode = 0,
            externalCategoryId = (string?)null,
            content = new Dictionary<string, object?> { ["title"] = null },
            priceOverride = (decimal?)null,
            quantityCap = (int?)null,
            existingCatalogItemId = "",
        }));
        Assert.False(cleared.GetProperty("contentOverrides").TryGetProperty("title", out _));
        Assert.False(cleared.GetProperty("references").TryGetProperty("CatalogItem", out _));
        Assert.Equal(("Product EDIT-1", 20m, 10), (cleared.GetProperty("effectiveTitle").GetString(), cleared.GetProperty("effectivePrice").GetDecimal(), cleared.GetProperty("effectiveQuantity").GetInt32()));

        var preview = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, $"/api/channel-listings/{listingId}/preview", new { }));
        Assert.True(preview.GetProperty("requests").GetArrayLength() >= 1);
        Assert.Equal("PUT", preview.GetProperty("requests")[0].GetProperty("method").GetString());
    }

    [Fact]
    public async Task Calls_to_a_channel_are_paced_and_another_channel_does_not_wait_for_them()
    {
        using var limiter = new ChannelRateLimiter(Options.Create(new MarketplaceOptions { RequestsPerSecond = 2, RequestBurst = 2 }));
        var watch = Stopwatch.StartNew();
        // Two go straight out; the third has to wait for the bucket to refill.
        Assert.True(await limiter.WaitAsync("amazon", default));
        Assert.True(await limiter.WaitAsync("amazon", default));
        Assert.True(watch.ElapsedMilliseconds < 400);
        Assert.True(await limiter.WaitAsync("walmart", default));
        Assert.True(watch.ElapsedMilliseconds < 400);
        Assert.True(await limiter.WaitAsync("amazon", default));
        Assert.True(watch.ElapsedMilliseconds >= 400, $"The third call went out after {watch.ElapsedMilliseconds} ms.");

        using var off = new ChannelRateLimiter(Options.Create(new MarketplaceOptions { RequestsPerSecond = 0 }));
        watch.Restart();
        for (var i = 0; i < 50; i++)
        {
            Assert.True(await off.WaitAsync("amazon", default));
        }
        Assert.True(watch.ElapsedMilliseconds < 400);
    }
}
