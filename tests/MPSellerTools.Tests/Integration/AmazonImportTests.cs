using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.TenantHost.Marketplace;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// Importing products from Amazon's catalog, with a fake of the Catalog
/// Items API standing in for it: one item looked up and imported, and a
/// pasted list worked through by a background job. These prove the request
/// shapes and what becomes of the answer; they are not calls to Amazon.
/// </summary>
public class AmazonImportTests(MarketplaceFixture fixture) : IClassFixture<MarketplaceFixture>
{
    private const string Market = "ATVPDKIKX0DER";

    private static string Item(string asin, string name) => $$"""
        {"asin":"{{asin}}",
         "attributes":{
           "bullet_point":[{"value":"Holds 12 oz","marketplace_id":"{{Market}}"},{"value":"Dishwasher safe","marketplace_id":"{{Market}}"}],
           "product_description":[{"value":"A sturdy mug.","marketplace_id":"{{Market}}"}],
           "list_price":[{"currency":"USD","value":14.99,"marketplace_id":"{{Market}}"}]},
         "dimensions":[{"marketplaceId":"{{Market}}","item":{
           "height":{"unit":"inches","value":4.0},"length":{"unit":"inches","value":5.5},"width":{"unit":"inches","value":3.5},
           "weight":{"unit":"pounds","value":0.8} } }],
         "identifiers":[{"marketplaceId":"{{Market}}","identifiers":[
           {"identifierType":"UPC","identifier":"012345678905"},{"identifierType":"EAN","identifier":"0012345678905"}]}],
         "images":[{"marketplaceId":"{{Market}}","images":[
           {"variant":"PT01","link":"https://m.media-amazon.example/side.jpg","height":1000,"width":1000},
           {"variant":"MAIN","link":"https://m.media-amazon.example/main-small.jpg","height":75,"width":75},
           {"variant":"MAIN","link":"https://m.media-amazon.example/main.jpg","height":1500,"width":1500}]}],
         "productTypes":[{"marketplaceId":"{{Market}}","productType":"DRINKING_CUP"}],
         "summaries":[{"marketplaceId":"{{Market}}","itemName":"{{name}}","brand":"Mugco","partNumber":"MC-12",
           "browseClassification":{"displayName":"Coffee Mugs","classificationId":"1"} }]}
        """;

    /// <summary>The company's Amazon account, with its token and a catalog that knows two of the three items asked about.</summary>
    private async Task<HttpClient> AmazonAsync(string email)
    {
        var admin = await fixture.AdminAsync(email);
        fixture.Amazon.Reset();
        if ((await admin.GetFromJsonAsync<JsonElement>("/api/channels")).EnumerateArray().All(a => a.GetProperty("channel").GetInt32() != (int)SalesChannel.Amazon))
        {
            var (accountId, _) = await fixture.CreateAccountAsync(admin, (int)SalesChannel.Amazon, "Our Amazon", Market, sellerId: "SELLER1", liveWrites: false);
            Assert.Equal(HttpStatusCode.NoContent, (await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, $"/api/channels/{accountId}/credentials", new
            {
                credentials = new { clientId = "amzn-client", clientSecret = "amzn-client-secret-value", refreshToken = "amzn-refresh-token-value" },
            })).StatusCode);
        }

        fixture.Amazon.On("POST", "/auth/o2/token", """{"access_token":"amzn-access-token-value","expires_in":3600,"token_type":"bearer"}""");
        fixture.Amazon.On("GET", "/catalog/2022-04-01/items", (uri, _) =>
        {
            var asked = Uri.UnescapeDataString(uri.Query);
            var items = new[] { ("B0TESTAAA1", "Blue mug"), ("B0TESTAAA3", "Red mug") }.Where(i => asked.Contains(i.Item1, StringComparison.Ordinal)).Select(i => Item(i.Item1, i.Item2));
            return ChannelRouter.Json($$"""{"numberOfResults":1,"items":[{{string.Join(",", items)}}]}""");
        });
        return admin;
    }

    [Fact]
    public async Task One_item_is_looked_up_and_becomes_a_product_with_everything_amazon_has_for_it()
    {
        using var admin = await AmazonAsync("amazon-import-one@example.com");

        var status = await admin.GetFromJsonAsync<JsonElement>("/api/amazon/import/status");
        Assert.True(status.GetProperty("ready").GetBoolean());
        Assert.Equal("Our Amazon", status.GetProperty("accountName").GetString());

        // Something that is no ASIN, address or barcode is turned down before Amazon is asked.
        Assert.Equal(HttpStatusCode.BadRequest, (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/amazon/import/lookup", new { query = "a blue mug" })).StatusCode);
        Assert.Equal(0, fixture.Amazon.Count("GET", "/catalog/2022-04-01/items"));

        // The address of the item's page is as good as its ASIN.
        var found = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/amazon/import/lookup", new { query = "https://www.amazon.com/Blue-Mug/dp/B0TESTAAA1/ref=sr_1_1?th=1" }));
        Assert.Equal("B0TESTAAA1", found.GetProperty("asin").GetString());
        Assert.Equal("Blue mug", found.GetProperty("title").GetString());
        Assert.Equal("AMZ-B0TESTAAA1", found.GetProperty("suggestedSku").GetString());
        Assert.Equal(14.99m, found.GetProperty("listPrice").GetDecimal());
        Assert.Equal(JsonValueKind.Null, found.GetProperty("existingProductId").ValueKind);
        var asked = Assert.Single(fixture.Amazon.Requests, r => r.Contains("/catalog/2022-04-01/items", StringComparison.Ordinal));
        Assert.Contains("identifiers=B0TESTAAA1&identifiersType=ASIN", asked);
        Assert.Contains("includedData=attributes,dimensions,identifiers,images,productTypes,summaries", asked);

        // One Amazon does not know.
        Assert.Equal(HttpStatusCode.NotFound, (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/amazon/import/lookup", new { query = "B0TESTAAA2" })).StatusCode);

        var imported = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/amazon/import/item", new { asin = "B0TESTAAA1", sku = "", price = 12.5m, stockQuantity = 4 }));
        Assert.Equal((int)AmazonImportOutcome.Created, imported.GetProperty("outcome").GetInt32());
        Assert.Equal("AMZ-B0TESTAAA1", imported.GetProperty("sku").GetString());

        var product = await admin.GetFromJsonAsync<JsonElement>($"/api/catalog/products/{imported.GetProperty("productId").GetGuid()}");
        Assert.Equal("Blue mug", product.GetProperty("name").GetString());
        Assert.Equal("Mugco", product.GetProperty("brand").GetString());
        Assert.Equal("Coffee Mugs", product.GetProperty("category").GetString());
        Assert.Equal("A sturdy mug.\n\n- Holds 12 oz\n- Dishwasher safe", product.GetProperty("description").GetString());
        var variant = Assert.Single(product.GetProperty("variants").EnumerateArray());
        Assert.Equal(12.5m, variant.GetProperty("price").GetDecimal());
        Assert.Equal(4, variant.GetProperty("onHand").GetInt32());
        Assert.Equal((0.8m, "lb"), (variant.GetProperty("weightValue").GetDecimal(), variant.GetProperty("weightUnit").GetString()));
        Assert.Equal((5.5m, 3.5m, 4m, "in"), (
            variant.GetProperty("length").GetDecimal(), variant.GetProperty("width").GetDecimal(), variant.GetProperty("height").GetDecimal(), variant.GetProperty("dimensionUnit").GetString()));
        Assert.Equal(
            [((int)ProductIdentifierType.Upc, "012345678905"), ((int)ProductIdentifierType.Ean, "0012345678905"), ((int)ProductIdentifierType.Mpn, "MC-12")],
            product.GetProperty("identifiers").EnumerateArray().Select(i => (i.GetProperty("type").GetInt32(), i.GetProperty("value").GetString()!)));
        // The largest of each picture, the main one first.
        Assert.Equal(
            ["https://m.media-amazon.example/main.jpg", "https://m.media-amazon.example/side.jpg"],
            product.GetProperty("media").EnumerateArray().Select(m => m.GetProperty("url").GetString()));

        // A second import under the same SKU is refused unless the product is to be brought up to date; then its price and stock stay its own.
        Assert.Equal(HttpStatusCode.Conflict, (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/amazon/import/item", new { asin = "B0TESTAAA1", sku = "" })).StatusCode);
        var again = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/amazon/import/item", new { asin = "B0TESTAAA1", sku = "", updateExisting = true }));
        Assert.Equal((int)AmazonImportOutcome.Updated, again.GetProperty("outcome").GetInt32());
        var kept = await admin.GetFromJsonAsync<JsonElement>($"/api/catalog/products/{imported.GetProperty("productId").GetGuid()}");
        Assert.Equal(12.5m, kept.GetProperty("variants")[0].GetProperty("price").GetDecimal());
        Assert.Equal(2, kept.GetProperty("media").GetArrayLength());
        Assert.Equal(3, kept.GetProperty("identifiers").GetArrayLength());

        // An employee cannot use it.
        await fixture.CreateUserAsync("amazon-import-employee@example.com", "Password123!", "Employee");
        using var employee = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(employee, "amazon-import-employee@example.com", "Password123!"));
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/amazon/import/status")).StatusCode);
    }

    [Fact]
    public async Task A_pasted_list_is_imported_by_a_background_job_that_names_what_it_held_back()
    {
        using var admin = await AmazonAsync("amazon-import-bulk@example.com");
        await fixture.WithDbAsync(db => db.BulkJobs.ExecuteDeleteAsync());
        // Already here under the SKU the first item would get.
        await fixture.CreateProductAsync(admin, "BULK-B0TESTAAA1");

        // A line that cannot be read is said at once, and nothing is queued.
        var refused = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/amazon/import/bulk", new { lines = "B0TESTAAA1\nnot an item", skuPrefix = "BULK-" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("not an item", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());

        var queued = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/amazon/import/bulk", new
        {
            // The same item twice counts once; the third has a SKU of its own.
            lines = "B0TESTAAA1\r\nB0TESTAAA2\nb0testaaa1\nB0TESTAAA3\tOWN-SKU\n",
            skuPrefix = "BULK-",
        }));
        Assert.Equal(3, queued.GetProperty("total").GetInt32());
        var jobId = queued.GetProperty("jobId").GetGuid();
        // One import at a time.
        Assert.Equal(HttpStatusCode.Conflict, (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/amazon/import/bulk", new { lines = "B0TESTAAA3" })).StatusCode);

        Assert.True(await fixture.WithScopeAsync(services => services.GetRequiredService<BulkJobRunner>().RunNextAsync(default)));

        var job = await admin.GetFromJsonAsync<JsonElement>($"/api/bulk-jobs/{jobId}");
        Assert.Equal((int)BulkJobStatus.CompletedWithErrors, job.GetProperty("status").GetInt32());
        Assert.Equal((3, 3, 1, 2), (job.GetProperty("total").GetInt32(), job.GetProperty("processed").GetInt32(), job.GetProperty("succeeded").GetInt32(), job.GetProperty("failed").GetInt32()));
        Assert.Equal("1 product(s) imported, 0 brought up to date; 2 held back.", job.GetProperty("summary").GetString());
        var errors = job.GetProperty("errors").EnumerateArray().ToDictionary(e => e.GetProperty("item").GetString()!, e => e.GetProperty("message").GetString()!);
        Assert.Contains("already here", errors["B0TESTAAA1"]);
        Assert.Contains("no item", errors["B0TESTAAA2"]);
        // The three were asked for in one call.
        Assert.Equal(1, fixture.Amazon.Count("GET", "/catalog/2022-04-01/items"));

        var made = await fixture.WithDbAsync(db => db.Products.AsNoTracking().SingleAsync(p => p.Sku == "OWN-SKU"));
        Assert.Equal(("Red mug", "Mugco", "Coffee Mugs", 14.99m, 0), (made.Name, made.Brand, made.Category, made.Price, made.StockQuantity));
        Assert.Equal(2, await fixture.WithDbAsync(db => db.ProductMedia.CountAsync(m => m.ProductId == made.Id)));

        // Run again, it works through the same list: what was made the first time is now already here.
        var rerun = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, $"/api/bulk-jobs/{jobId}/run-again", new { }));
        Assert.True(await fixture.WithScopeAsync(services => services.GetRequiredService<BulkJobRunner>().RunNextAsync(default)));
        var second = await admin.GetFromJsonAsync<JsonElement>($"/api/bulk-jobs/{rerun.GetProperty("id").GetGuid()}");
        Assert.Equal((3, 0, 3), (second.GetProperty("processed").GetInt32(), second.GetProperty("succeeded").GetInt32(), second.GetProperty("failed").GetInt32()));

        // It is not offered from the Jobs page, which has no list to give it.
        var account = (await admin.GetFromJsonAsync<JsonElement>("/api/channels")).EnumerateArray().First(a => a.GetProperty("channel").GetInt32() == (int)SalesChannel.Amazon);
        Assert.Equal(HttpStatusCode.BadRequest, (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/bulk-jobs", new { type = (int)BulkJobType.ImportFromAmazon, channelAccountId = account.GetProperty("id").GetGuid() })).StatusCode);
    }
}
