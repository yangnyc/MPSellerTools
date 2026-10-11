using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPSellerTools.Core.Business;
using MPSellerTools.TenantHost.Marketplace;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// Importing another store's catalog, cleaning the catalog of duplicates,
/// shipping orders and the reports, with a fake storefront standing in for
/// the store imported from. These prove what is read, left out and made of
/// it; they are not calls to a real store.
/// </summary>
public class CatalogToolsTests(MarketplaceFixture fixture) : IClassFixture<MarketplaceFixture>
{
    private const string Shop = "shop.example-otc.test";

    private static string Product(long id, string title, string type, string price, string body = "<p>Plain words.</p>", string sku = "", string extra = "") => $$"""
        {"id":{{id}},"title":"{{title}}","handle":"p-{{id}}","body_html":{{JsonSerializer.Serialize(body)}},"vendor":"Goodcare","product_type":"{{type}}","tags":[],
         "variants":[{"id":{{id}}1,"title":"Default Title","sku":"{{sku}}","price":"{{price}}","compare_at_price":null,"available":true,"grams":120}{{extra}}],
         "images":[{"src":"https://cdn.example-otc.test/{{id}}-a.jpg"},{"src":"https://cdn.example-otc.test/{{id}}-b.jpg"}]}
        """;

    private void Catalog()
    {
        fixture.Storefront.Reset();
        fixture.Storefront.On("GET", $"https://{Shop}/products.json", $$"""
            {"products":[
              {{Product(1, "Ibuprofen 200 mg Tablets, 100 Count", "Pain Relief", "8.00",
                  "<div style=\"color:red\"><h2>About</h2><p>Relieves minor aches.<br></p><p>Order today from Example-Otc for free shipping!</p><script>track()</script><ul><li>Fast acting</li><li></li></ul></div>",
                  "IBU-200-100")}},
              {{Product(2, "Amoxicillin 500 mg Capsules", "Antibiotics", "12.00")}},
              {{Product(3, "IV Catheter 22G", "Clinic Supplies", "3.00")}},
              {{Product(4, "Zorvex 40 mg Tablets", "Medicine", "30.00")}},
              {{Product(5, "Decorative Paperweight", "Gifts", "15.00")}},
              {{Product(6, "Vitamin C 500 mg Tablets", "Vitamins", "0")}},
              {{Product(7, "Children's Allergy Relief Syrup", "Allergy", "9.50", sku: "KID-ALG")}}
            ]}
            """);
    }

    private Task<bool> RunJobAsync() => fixture.WithScopeAsync(services => services.GetRequiredService<BulkJobRunner>().RunNextAsync(default));

    private static async Task<Dictionary<string, string>> ReportAsync(HttpClient admin, Guid jobId) =>
        (await admin.GetFromJsonAsync<JsonElement>($"/api/bulk-jobs/{jobId}")).GetProperty("report").EnumerateArray()
            .ToDictionary(line => line.GetProperty("label").GetString()!, line => line.GetProperty("value").GetString()!);

    [Fact]
    public async Task Another_stores_catalog_is_imported_cleaned_up_with_a_dry_run_first()
    {
        using var admin = await fixture.AdminAsync("tools-website@example.com");
        await fixture.WithDbAsync(db => db.BulkJobs.ExecuteDeleteAsync());
        WebsiteImport.PageDelay = TimeSpan.Zero;
        Catalog();

        // An address on this network, or none, is turned down before anything is read.
        foreach (var address in new[] { "", "localhost", "https://10.0.0.5", "intranet" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/catalog-tools/website-import", new { source = address })).StatusCode);
        }
        Assert.Empty(fixture.Storefront.Requests);

        // The dry run reads the store and reports, and makes nothing.
        var dry = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/catalog-tools/website-import", new { source = $"https://{Shop}/collections/all", dryRun = true, priceAdjustPercent = 10, stock = 5 }));
        var dryJob = dry.GetProperty("jobId").GetGuid();
        Assert.True(await RunJobAsync());
        var job = await admin.GetFromJsonAsync<JsonElement>($"/api/bulk-jobs/{dryJob}");
        Assert.Equal((int)BulkJobStatus.Succeeded, job.GetProperty("status").GetInt32());
        Assert.StartsWith("Dry run, nothing was changed: 2 of the 7 products", job.GetProperty("summary").GetString());
        var report = await ReportAsync(admin, dryJob);
        Assert.Equal("7", report["Products in the source"]);
        Assert.Equal("2", report["New to your catalog"]);
        // Each left out for its own reason: a prescription drug and a clinical supply, a medicine nobody knows, a gift, and one with no price.
        Assert.Equal("2", report["Left out: prescription or clinical"]);
        Assert.Equal("1", report["Left out: needs a person to review"]);
        Assert.Equal("1", report["Left out: no category"]);
        Assert.Equal("1", report["Left out: no price"]);
        Assert.Equal("1", report["Category: Medicines / Pain & Fever"]);
        // One for children goes to their category, whatever it treats.
        Assert.Equal("1", report["Category: Infants & Children"]);
        Assert.False(await fixture.WithDbAsync(db => db.Products.AnyAsync(p => p.Sku == "IBU-200-100")));

        // For real, with the same options.
        var real = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/catalog-tools/website-import", new { source = Shop, priceAdjustPercent = 10, stock = 5 }));
        Assert.True(await RunJobAsync());
        var done = await admin.GetFromJsonAsync<JsonElement>($"/api/bulk-jobs/{real.GetProperty("jobId").GetGuid()}");
        Assert.Equal((int)BulkJobStatus.Succeeded, done.GetProperty("status").GetInt32());
        Assert.StartsWith($"2 product(s) imported from {Shop}, 0 brought up to date.", done.GetProperty("summary").GetString());
        // It belongs to no sales channel.
        Assert.Equal(JsonValueKind.Null, done.GetProperty("channelAccountId").ValueKind);

        var made = await fixture.WithDbAsync(db => db.Products.AsNoTracking().SingleAsync(p => p.Sku == "IBU-200-100"));
        Assert.Equal(("Ibuprofen 200 mg Tablets, 100 Count", "Goodcare", "Medicines / Pain & Fever", 8.80m, 5), (made.Name, made.Brand, made.Category, made.Price, made.StockQuantity));
        // The description is rebuilt from plain tags: no styles or scripts, no empty items, and nothing about the source store.
        Assert.Equal("<h3>About</h3><p>Relieves minor aches.</p><ul><li>Fast acting</li></ul>", made.Description);
        var catalog = await admin.GetFromJsonAsync<JsonElement>($"/api/catalog/products/{made.Id}");
        Assert.Equal(["https://cdn.example-otc.test/1-a.jpg", "https://cdn.example-otc.test/1-b.jpg"], catalog.GetProperty("media").EnumerateArray().Select(m => m.GetProperty("url").GetString()));
        Assert.Equal((120m, "g"), (catalog.GetProperty("variants")[0].GetProperty("weightValue").GetDecimal(), catalog.GetProperty("variants")[0].GetProperty("weightUnit").GetString()));
        Assert.Equal("Infants & Children", await fixture.WithDbAsync(db => db.Products.Where(p => p.Sku == "KID-ALG").Select(p => p.Category).SingleAsync()));

        // Again: what is here already is left as it is.
        var again = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/catalog-tools/website-import", new { source = Shop }));
        Assert.True(await RunJobAsync());
        Assert.StartsWith("0 product(s) imported", (await admin.GetFromJsonAsync<JsonElement>($"/api/bulk-jobs/{again.GetProperty("jobId").GetGuid()}")).GetProperty("summary").GetString());

        // A store that is not one says so in words.
        fixture.Storefront.On("GET", $"https://{Shop}/products.json", "{}", HttpStatusCode.NotFound);
        var missing = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/catalog-tools/website-import", new { source = Shop, dryRun = true }));
        Assert.True(await RunJobAsync());
        var failed = await admin.GetFromJsonAsync<JsonElement>($"/api/bulk-jobs/{missing.GetProperty("jobId").GetGuid()}");
        Assert.Equal((int)BulkJobStatus.Failed, failed.GetProperty("status").GetInt32());
        Assert.Contains("has no catalog to read at this address", failed.GetProperty("lastError").GetString());
    }

    [Fact]
    public async Task Duplicates_are_found_the_best_kept_the_rest_archived_and_a_clean_undone()
    {
        using var admin = await fixture.AdminAsync("tools-clean@example.com");
        // The same item three times, in other words and spellings; a bigger pack that is not the same; and one of each kind alone.
        var (keepId, _) = await fixture.CreateProductAsync(admin, "DUP-A", price: 10m, stock: 4);
        var (otherId, _) = await fixture.CreateProductAsync(admin, "DUP-B", price: 10.5m, stock: 0, category: null);
        var (caseId, _) = await fixture.CreateProductAsync(admin, "DUP-CASE", price: 100m);
        await fixture.WithDbAsync(async db =>
        {
            (await db.Products.SingleAsync(p => p.Id == keepId)).Name = "Zentrol Tablets 325 mg, 100 Count";
            (await db.Products.SingleAsync(p => p.Id == otherId)).Name = "zentrol 325mg tabs 100ct";
            (await db.Products.SingleAsync(p => p.Id == caseId)).Name = "Zentrol Tablets 325 mg, 100 Count";
            return await db.SaveChangesAsync();
        });

        var scan = await admin.GetFromJsonAsync<JsonElement>("/api/catalog-tools/clean?strategies=exact,normalized");
        var group = Assert.Single(scan.GetProperty("groups").EnumerateArray(), g => g.GetProperty("products").EnumerateArray().Any(p => p.GetProperty("sku").GetString() == "DUP-A"));
        // The one at ten times the price is a case of them, not a duplicate.
        Assert.Equal(["DUP-A", "DUP-B"], group.GetProperty("products").EnumerateArray().Select(p => p.GetProperty("sku").GetString()).Order());
        // The better filled-in and stocked one is the one to keep.
        Assert.Equal(keepId, group.GetProperty("keepId").GetGuid());
        Assert.True(scan.GetProperty("health").GetProperty("duplicateProducts").GetInt32() >= 1);

        var applied = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/catalog-tools/clean/apply", new
        {
            groups = new[] { new { key = group.GetProperty("key").GetString(), keepId, retireIds = new[] { otherId } } },
        }));
        Assert.Equal(1, applied.GetProperty("retired").GetInt32());
        Assert.True(await fixture.WithDbAsync(db => db.Products.Where(p => p.Id == otherId).Select(p => p.IsArchived).SingleAsync()));
        Assert.False(await fixture.WithDbAsync(db => db.Products.Where(p => p.Id == keepId).Select(p => p.IsArchived).SingleAsync()));
        var after = await admin.GetFromJsonAsync<JsonElement>("/api/catalog-tools/clean?strategies=exact,normalized");
        Assert.True(after.GetProperty("canUndo").GetBoolean());
        Assert.DoesNotContain(after.GetProperty("groups").EnumerateArray(), g => g.GetProperty("key").GetString() == group.GetProperty("key").GetString());

        // Undone, the archived one is back, and so is the group; told they are not duplicates, it is not shown again.
        Assert.Equal(1, (await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/catalog-tools/clean/undo", new { }))).GetProperty("restored").GetInt32());
        Assert.False(await fixture.WithDbAsync(db => db.Products.Where(p => p.Id == otherId).Select(p => p.IsArchived).SingleAsync()));
        Assert.Equal(HttpStatusCode.NoContent, (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/catalog-tools/clean/ignore", new { key = group.GetProperty("key").GetString() })).StatusCode);
        var ignored = await admin.GetFromJsonAsync<JsonElement>("/api/catalog-tools/clean?strategies=exact,normalized");
        Assert.DoesNotContain(ignored.GetProperty("groups").EnumerateArray(), g => g.GetProperty("key").GetString() == group.GetProperty("key").GetString());
    }

    [Fact]
    public async Task An_order_is_picked_shipped_with_its_tracking_number_and_shows_in_the_reports()
    {
        using var admin = await fixture.AdminAsync("tools-shipping@example.com");
        var (productId, _) = await fixture.CreateProductAsync(admin, "SHIP-1", price: 20m, stock: 3);
        var order = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/orders", new { items = new[] { new { productId, quantity = 2 } } }));
        var orderId = order.GetProperty("id").GetGuid();
        var number = order.GetProperty("orderNumber").GetString();

        var queued = Assert.Single((await admin.GetFromJsonAsync<JsonElement>("/api/shipping/queue")).EnumerateArray(), o => o.GetProperty("id").GetGuid() == orderId);
        Assert.Equal((2, false, "Created here"), (queued.GetProperty("units").GetInt32(), queued.GetProperty("short").GetBoolean(), queued.GetProperty("channel").GetString()));
        var pick = Assert.Single((await admin.GetFromJsonAsync<JsonElement>("/api/shipping/pick-list")).EnumerateArray(), l => l.GetProperty("sku").GetString() == "SHIP-1");
        Assert.Equal(2, pick.GetProperty("quantity").GetInt32());
        Assert.Contains(number, pick.GetProperty("orderNumbers").EnumerateArray().Select(n => n.GetString()));

        // Shipped straight from new: completed, with who carried it, and what it held leaves the shelf.
        Assert.Equal(HttpStatusCode.NoContent, (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, $"/api/shipping/{orderId}/ship", new { carrier = "UPS", trackingNumber = " 1Z999AA10123456784 " })).StatusCode);
        Assert.DoesNotContain((await admin.GetFromJsonAsync<JsonElement>("/api/shipping/queue")).EnumerateArray(), o => o.GetProperty("id").GetGuid() == orderId);
        var shipped = Assert.Single((await admin.GetFromJsonAsync<JsonElement>("/api/shipping/shipped?days=7")).EnumerateArray(), o => o.GetProperty("id").GetGuid() == orderId);
        Assert.Equal(("UPS", "1Z999AA10123456784"), (shipped.GetProperty("carrier").GetString(), shipped.GetProperty("trackingNumber").GetString()));
        Assert.Equal((int)OrderStatus.Completed, (await admin.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}")).GetProperty("status").GetInt32());
        Assert.Equal(1, await fixture.WithDbAsync(db => db.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync()));
        // Not twice.
        Assert.Equal(HttpStatusCode.Conflict, (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, $"/api/shipping/{orderId}/ship", new { carrier = "UPS", trackingNumber = "" })).StatusCode);

        // The reports count it.
        var sales = await admin.GetFromJsonAsync<JsonElement>("/api/reports/sales?days=7");
        Assert.True(sales.GetProperty("orders").GetInt32() >= 1);
        Assert.Equal(7, sales.GetProperty("daily").GetArrayLength());
        Assert.Contains(sales.GetProperty("topProducts").EnumerateArray(), p => p.GetProperty("sku").GetString() == "SHIP-1" && p.GetProperty("units").GetInt32() == 2);
        Assert.Contains(sales.GetProperty("channels").EnumerateArray(), ch => ch.GetProperty("channel").GetString() == "Created here");
        var inventory = await admin.GetFromJsonAsync<JsonElement>("/api/reports/inventory");
        var item = Assert.Single(inventory.GetProperty("items").EnumerateArray(), i => i.GetProperty("sku").GetString() == "SHIP-1");
        // One left, two sold in the month: gone in a fortnight at that pace.
        Assert.Equal((1, 2, 15, 20m), (item.GetProperty("onHand").GetInt32(), item.GetProperty("soldLast30Days").GetInt32(), item.GetProperty("daysOfStock").GetInt32(), item.GetProperty("value").GetDecimal()));
        var listings = await admin.GetFromJsonAsync<JsonElement>("/api/reports/listings");
        Assert.True(listings.GetProperty("notListed").GetInt32() >= 1);

        // An employee sees none of it.
        await fixture.CreateUserAsync("tools-shipping-employee@example.com", "Password123!", "Employee");
        using var employee = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(employee, "tools-shipping-employee@example.com", "Password123!"));
        foreach (var url in new[] { "/api/shipping/queue", "/api/reports/sales", "/api/catalog-tools/clean" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync(url)).StatusCode);
        }
    }
}
