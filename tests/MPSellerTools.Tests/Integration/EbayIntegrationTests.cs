using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPSellerTools.Core.Business;
using MPSellerTools.Infrastructure.Tenants;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// The eBay link, with <see cref="FakeEbay"/> standing in for eBay: saving
/// the keys, the seller's consent, and importing orders and products.
/// </summary>
public class EbayIntegrationTests(TenantHostFixture fixture) : IClassFixture<TenantHostFixture>
{
    private const string TokenJson =
        """{"access_token":"access-1","expires_in":7200,"refresh_token":"refresh-1","refresh_token_expires_in":47304000}""";

    /// <summary>The Trading API's answer for a seller with these listings on sale; each is the inside of an Item element.</summary>
    private static string OnSaleXml(params string[] items) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <GetMyeBaySellingResponse xmlns="urn:ebay:apis:eBLBaseComponents">
          <Ack>Success</Ack>
          <ActiveList>
            <ItemArray>{string.Concat(items.Select(item => $"<Item>{item}</Item>"))}</ItemArray>
            <PaginationResult><TotalNumberOfPages>1</TotalNumberOfPages><TotalNumberOfEntries>{items.Length}</TotalNumberOfEntries></PaginationResult>
          </ActiveList>
        </GetMyeBaySellingResponse>
        """;

    private async Task<HttpClient> AdminAsync(string email)
    {
        await fixture.CreateUserAsync(email, "Password123!", "TenantAdmin");
        var client = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(client, email, "Password123!"));
        return client;
    }

    /// <summary>Saves keys and takes the seller through consent, leaving the account connected.</summary>
    private async Task ConnectAsync(HttpClient admin)
    {
        fixture.Ebay.Reset();
        fixture.Ebay.Answer("/identity/v1/oauth2/token", TokenJson);

        var saved = await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, "/api/ebay/settings", new { environment = 0, clientId = "App-123", clientSecret = "cert-secret", ruName = "My-RuName" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var connect = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/ebay/connect", new { });
        var authorizeUrl = (await connect.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("authorizeUrl").GetString()!;
        var state = authorizeUrl[(authorizeUrl.IndexOf("&state=", StringComparison.Ordinal) + 7)..];

        var completed = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/ebay/complete", new { codeOrUrl = $"https://localhost/ebay?code=the-code&state={state}" });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
    }

    [Fact]
    public async Task Consent_goes_to_the_sandbox_with_read_only_scopes_and_the_secrets_never_come_back()
    {
        using var admin = await AdminAsync("ebay-connect@example.com");
        fixture.Ebay.Reset();
        fixture.Ebay.Answer("/identity/v1/oauth2/token", TokenJson);

        await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, "/api/ebay/settings", new { environment = 0, clientId = "App-123", clientSecret = "cert-secret", ruName = "My-RuName" });
        var connect = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/ebay/connect", new { });
        var authorizeUrl = (await connect.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("authorizeUrl").GetString()!;

        Assert.StartsWith("https://auth.sandbox.ebay.com/oauth2/authorize?client_id=App-123&redirect_uri=My-RuName&response_type=code", authorizeUrl);
        Assert.Contains("sell.fulfillment.readonly", authorizeUrl);
        Assert.Contains("sell.inventory.readonly", authorizeUrl);
        // The basic scope, for reading what is on sale through the Trading API.
        Assert.Contains(Uri.EscapeDataString("https://api.ebay.com/oauth/api_scope "), authorizeUrl);

        // An answer carrying some other state is not the answer to this request.
        var forged = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/ebay/complete", new { codeOrUrl = "https://localhost/ebay?code=the-code&state=someone-elses" });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Empty(fixture.Ebay.Requests);

        var state = authorizeUrl[(authorizeUrl.IndexOf("&state=", StringComparison.Ordinal) + 7)..];
        var completed = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/ebay/complete", new { codeOrUrl = $"https://localhost/ebay?code=the-code&state={state}" });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);

        var exchange = Assert.Single(fixture.Ebay.Requests);
        Assert.StartsWith("POST https://api.sandbox.ebay.com/identity/v1/oauth2/token grant_type=authorization_code&code=the-code&redirect_uri=My-RuName", exchange);

        var statusText = await admin.GetStringAsync("/api/ebay");
        var status = JsonDocument.Parse(statusText).RootElement;
        Assert.True(status.GetProperty("connected").GetBoolean());
        Assert.Equal("App-123", status.GetProperty("clientId").GetString());
        Assert.DoesNotContain("cert-secret", statusText);
        Assert.DoesNotContain("refresh-1", statusText);

        // Neither secret is stored as it was given.
        using var scope = fixture.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().EbayConnections.SingleAsync();
        Assert.DoesNotContain("cert-secret", stored.ClientSecretProtected);
        Assert.DoesNotContain("refresh-1", stored.RefreshTokenProtected);
    }

    [Fact]
    public async Task Importing_orders_creates_them_once_with_their_products_and_later_only_updates_the_status()
    {
        using var admin = await AdminAsync("ebay-orders@example.com");
        await ConnectAsync(admin);

        const string order = """
            {"orderId":"11-22222-33333","creationDate":"2026-09-01T10:00:00.000Z","orderFulfillmentStatus":"NOT_STARTED",
             "cancelStatus":{"cancelState":"NONE_REQUESTED"},
             "lineItems":[
               {"lineItemId":"1","legacyItemId":"1100","sku":"EB-MUG","title":"Blue mug","quantity":2,"lineItemCost":{"value":"25.00","currency":"USD"}},
               {"lineItemId":"2","legacyItemId":"1200","title":"Listing without a SKU","quantity":1,"lineItemCost":{"value":"9.99","currency":"USD"}}]}
            """;
        fixture.Ebay.Answer("/sell/fulfillment/v1/order", $$"""{"total":1,"orders":[{{order}}]}""");

        var first = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/ebay/import/orders", new { });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstResult = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, firstResult.GetProperty("created").GetInt32());
        Assert.Equal(2, firstResult.GetProperty("productsCreated").GetInt32());

        // The call used an access token obtained from the refresh token.
        Assert.Contains(fixture.Ebay.Requests, r => r.Contains("grant_type=refresh_token&refresh_token=refresh-1"));
        Assert.Contains(fixture.Ebay.Requests, r => r.StartsWith("GET https://api.sandbox.ebay.com/sell/fulfillment/v1/order?filter=lastmodifieddate"));

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var imported = await db.Orders.Include(o => o.Items).SingleAsync(o => o.EbayOrderId == "11-22222-33333");
            Assert.Equal("EBAY-11-22222-33333", imported.OrderNumber);
            Assert.Equal(OrderStatus.New, imported.Status);
            Assert.Equal(34.99m, imported.Total);
            Assert.Equal(12.50m, imported.Items.Single(i => i.Quantity == 2).UnitPrice);
            Assert.Equal("Blue mug", (await db.Products.SingleAsync(p => p.Sku == "EB-MUG")).Name);
            Assert.True(await db.Products.AnyAsync(p => p.Sku == "EBAY-1200"));
        }

        // The same order again, now shipped: nothing new, only the status moves.
        fixture.Ebay.Reset();
        fixture.Ebay.Answer("/identity/v1/oauth2/token", TokenJson);
        fixture.Ebay.Answer("/sell/fulfillment/v1/order",
            $$"""{"total":1,"orders":[{{order.Replace("NOT_STARTED", "FULFILLED")}}]}""");

        var second = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/ebay/import/orders", new { });
        var secondResult = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, secondResult.GetProperty("created").GetInt32());
        Assert.Equal(1, secondResult.GetProperty("updated").GetInt32());

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            Assert.Equal(OrderStatus.Completed, (await db.Orders.SingleAsync(o => o.EbayOrderId == "11-22222-33333")).Status);
        }
    }

    [Fact]
    public async Task Importing_products_matches_by_sku_and_takes_the_offer_price()
    {
        using var admin = await AdminAsync("ebay-products@example.com");
        await ConnectAsync(admin);

        var created = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/products", new { sku = "EB-LAMP", name = "Old name", price = 5m, stockQuantity = 1 });
        Assert.True(created.IsSuccessStatusCode);

        fixture.Ebay.Answer("/sell/inventory/v1/inventory_item", """
            {"total":2,"inventoryItems":[
              {"sku":"EB-LAMP","product":{"title":"Desk lamp"},"availability":{"shipToLocationAvailability":{"quantity":7}}},
              {"sku":"EB-NEW","product":{"title":"New thing"},"availability":{"shipToLocationAvailability":{"quantity":3}}}]}
            """);
        fixture.Ebay.Answer("offer?sku=EB-LAMP", """{"offers":[{"pricingSummary":{"price":{"value":"19.50","currency":"USD"}}}]}""");

        var imported = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/ebay/import/products", new { });
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var result = await imported.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        Assert.Equal(1, result.GetProperty("updated").GetInt32());

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var lamp = await db.Products.SingleAsync(p => p.Sku == "EB-LAMP");
        Assert.Equal(("Desk lamp", 19.50m, 7), (lamp.Name, lamp.Price, lamp.StockQuantity));
        // No offer, so no price is known yet.
        var added = await db.Products.SingleAsync(p => p.Sku == "EB-NEW");
        Assert.Equal((0m, 3), (added.Price, added.StockQuantity));
    }

    [Fact]
    public async Task Importing_products_records_published_offers_as_listings_and_drops_ones_ebay_no_longer_reports()
    {
        using var admin = await AdminAsync("ebay-listings@example.com");
        await ConnectAsync(admin);

        const string inventory = """
            {"total":3,"inventoryItems":[
              {"sku":"LS-LIVE","product":{"title":"Posted thing"},"availability":{"shipToLocationAvailability":{"quantity":4}}},
              {"sku":"LS-DRAFT","product":{"title":"Draft thing"},"availability":{"shipToLocationAvailability":{"quantity":1}}},
              {"sku":"LS-DOWN","product":{"title":"Unreadable thing"},"availability":{"shipToLocationAvailability":{"quantity":1}}}]}
            """;
        const string liveOffer = """
            {"offers":[{"offerId":"o-1","sku":"LS-LIVE","marketplaceId":"EBAY_US","availableQuantity":4,"status":"PUBLISHED",
              "pricingSummary":{"price":{"value":"12.00","currency":"USD"}},
              "listing":{"listingId":"110700000001","listingStatus":"ACTIVE","soldQuantity":3}}]}
            """;
        const string downOffer = """
            {"offers":[{"offerId":"o-3","sku":"LS-DOWN","marketplaceId":"EBAY_US","status":"PUBLISHED",
              "listing":{"listingId":"110700000003","listingStatus":"OUT_OF_STOCK"}}]}
            """;
        fixture.Ebay.Answer("/sell/inventory/v1/inventory_item", inventory);
        fixture.Ebay.Answer("offer?sku=LS-LIVE", liveOffer);
        // An offer that was never published is not a listing.
        fixture.Ebay.Answer("offer?sku=LS-DRAFT", """{"offers":[{"offerId":"o-2","sku":"LS-DRAFT","marketplaceId":"EBAY_US","status":"UNPUBLISHED"}]}""");
        fixture.Ebay.Answer("offer?sku=LS-DOWN", downOffer);

        var imported = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/ebay/import/products", new { });
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        Assert.Equal(2, (await imported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("listings").GetInt32());

        // Employees can read the listings, like the catalog.
        await fixture.CreateUserAsync("ebay-listings-employee@example.com", "Password123!", "Employee");
        using var employee = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(employee, "ebay-listings-employee@example.com", "Password123!"));

        var page = await employee.GetFromJsonAsync<JsonElement>("/api/listings");
        Assert.True(page.GetProperty("connected").GetBoolean());
        var mine = page.GetProperty("listings").EnumerateArray().Where(l => l.GetProperty("productSku").GetString()!.StartsWith("LS-")).ToList();
        Assert.Equal(["LS-LIVE", "LS-DOWN"], mine.Select(l => l.GetProperty("productSku").GetString()));
        var live = mine[0];
        Assert.Equal("110700000001", live.GetProperty("externalId").GetString());
        Assert.Equal("https://www.sandbox.ebay.com/itm/110700000001", live.GetProperty("url").GetString());
        Assert.Equal("EBAY_US", live.GetProperty("marketplace").GetString());
        Assert.Equal((int)ListingStatus.Live, live.GetProperty("status").GetInt32());
        Assert.Equal(12.00m, live.GetProperty("price").GetDecimal());
        Assert.Equal(3, live.GetProperty("soldQuantity").GetInt32());
        Assert.Equal((int)ListingStatus.OutOfStock, mine[1].GetProperty("status").GetInt32());

        // Next time eBay has ended the first listing's offer, and will not show the third item's offers at all.
        fixture.Ebay.Reset();
        fixture.Ebay.Answer("/identity/v1/oauth2/token", TokenJson);
        fixture.Ebay.Answer("/sell/inventory/v1/inventory_item", inventory);
        fixture.Ebay.Answer("offer?sku=LS-DOWN", """{"errors":[{"message":"Internal error"}]}""", HttpStatusCode.InternalServerError);
        fixture.Ebay.Answer("/ws/api.dll", OnSaleXml());

        var again = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/ebay/import/products", new { });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var left = await db.Listings.Where(l => l.ExternalId.StartsWith("1107000000")).Select(l => l.ExternalId).ToListAsync();
        // The one eBay stopped reporting is gone; the one it could not answer about is kept as it was.
        Assert.Equal(["110700000003"], left);
    }

    [Fact]
    public async Task Importing_products_also_records_the_listings_made_on_the_ebay_site()
    {
        using var admin = await AdminAsync("ebay-site-listings@example.com");
        await ConnectAsync(admin);
        // A product the company already keeps, which one of the listings names by its SKU.
        var keptId = (await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/products", new { sku = "SITE-KEPT", name = "Our own name", price = 5.00m, stockQuantity = 9 }))).GetProperty("id").GetString();

        const string withSku = """
            <ItemID>110800000001</ItemID><Title>Hand-listed lamp</Title><SKU>SITE-KEPT</SKU>
            <Quantity>10</Quantity><QuantityAvailable>7</QuantityAvailable>
            <SellingStatus><CurrentPrice currencyID="USD">24.50</CurrentPrice></SellingStatus>
            <ListingDetails><ViewItemURL>https://www.sandbox.ebay.com/itm/Hand-listed-lamp/110800000001</ViewItemURL></ListingDetails>
            """;
        const string withoutSku = """
            <ItemID>110800000002</ItemID><Title>Hand-listed vase</Title>
            <Quantity>2</Quantity><QuantityAvailable>0</QuantityAvailable>
            <SellingStatus><CurrentPrice currencyID="GBP">8.00</CurrentPrice></SellingStatus>
            """;
        fixture.Ebay.Answer("/sell/inventory/v1/inventory_item", """{"total":0,"inventoryItems":[]}""");
        fixture.Ebay.Answer("/ws/api.dll", OnSaleXml(withSku, withoutSku));

        var imported = await (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/ebay/import/products", new { }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, imported.GetProperty("listings").GetInt32());
        // Only the listing without a SKU needed a product made for it.
        Assert.Equal(1, imported.GetProperty("created").GetInt32());
        Assert.Equal(JsonValueKind.Null, imported.GetProperty("warning").ValueKind);

        // The call goes to the Trading API with the seller's token in its own header.
        Assert.Contains(fixture.Ebay.Requests, r => r.StartsWith("POST https://api.sandbox.ebay.com/ws/api.dll", StringComparison.Ordinal) && r.Contains("<GetMyeBaySellingRequest"));

        var page = await admin.GetFromJsonAsync<JsonElement>("/api/listings?channel=0");
        var mine = page.GetProperty("listings").EnumerateArray()
            .Where(l => l.GetProperty("externalId").GetString()!.StartsWith("1108000000", StringComparison.Ordinal))
            .OrderBy(l => l.GetProperty("externalId").GetString()).ToList();
        Assert.Equal(2, mine.Count);

        var lamp = mine[0];
        Assert.Equal(keptId, lamp.GetProperty("productId").GetString());
        // The company's own product is left as it was.
        Assert.Equal("Our own name", lamp.GetProperty("productName").GetString());
        Assert.Equal("https://www.sandbox.ebay.com/itm/Hand-listed-lamp/110800000001", lamp.GetProperty("url").GetString());
        Assert.Equal((int)ListingStatus.Live, lamp.GetProperty("status").GetInt32());
        Assert.Equal(24.50m, lamp.GetProperty("price").GetDecimal());
        Assert.Equal("USD", lamp.GetProperty("currency").GetString());
        Assert.Equal(7, lamp.GetProperty("availableQuantity").GetInt32());
        Assert.Equal(3, lamp.GetProperty("soldQuantity").GetInt32());

        var vase = mine[1];
        Assert.Equal("EBAY-110800000002", vase.GetProperty("productSku").GetString());
        Assert.Equal("Hand-listed vase", vase.GetProperty("productName").GetString());
        Assert.Equal("https://www.sandbox.ebay.com/itm/110800000002", vase.GetProperty("url").GetString());
        Assert.Equal((int)ListingStatus.OutOfStock, vase.GetProperty("status").GetInt32());

        // eBay refuses the Trading call: the import still succeeds, says so, and keeps what it knew.
        fixture.Ebay.Reset();
        fixture.Ebay.Answer("/identity/v1/oauth2/token", TokenJson);
        fixture.Ebay.Answer("/sell/inventory/v1/inventory_item", """{"total":0,"inventoryItems":[]}""");
        fixture.Ebay.Answer("/ws/api.dll", """
            <?xml version="1.0" encoding="UTF-8"?>
            <GetMyeBaySellingResponse xmlns="urn:ebay:apis:eBLBaseComponents">
              <Ack>Failure</Ack>
              <Errors><ShortMessage>Invalid token.</ShortMessage><LongMessage>The token does not allow this call.</LongMessage></Errors>
            </GetMyeBaySellingResponse>
            """);

        var refused = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/ebay/import/products", new { });
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("The token does not allow this call.", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("warning").GetString());
        Assert.Contains("The token does not allow this call.", (await admin.GetFromJsonAsync<JsonElement>("/api/ebay")).GetProperty("lastSyncError").GetString());
        Assert.Equal(2, await CountAsync());

        // The vase has sold out and ended: eBay no longer reports it, so it goes from here too.
        fixture.Ebay.Reset();
        fixture.Ebay.Answer("/identity/v1/oauth2/token", TokenJson);
        fixture.Ebay.Answer("/sell/inventory/v1/inventory_item", """{"total":0,"inventoryItems":[]}""");
        fixture.Ebay.Answer("/ws/api.dll", OnSaleXml(withSku));

        await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/ebay/import/products", new { });
        Assert.Equal(1, await CountAsync());

        async Task<int> CountAsync()
        {
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            return await db.Listings.CountAsync(l => l.ExternalId.StartsWith("1108000000"));
        }
    }

    [Fact]
    public async Task A_refusal_from_ebay_is_reported_and_remembered_and_an_employee_cannot_use_the_link()
    {
        using var admin = await AdminAsync("ebay-refused@example.com");
        await ConnectAsync(admin);

        fixture.Ebay.Reset();
        fixture.Ebay.Answer("/identity/v1/oauth2/token",
            """{"error":"invalid_grant","error_description":"the provided authorization refresh token is invalid or was issued to another client"}""",
            HttpStatusCode.BadRequest);

        var refused = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/ebay/import/orders", new { });
        Assert.Equal(HttpStatusCode.BadGateway, refused.StatusCode);
        Assert.Contains("refresh token is invalid", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());

        var status = await admin.GetFromJsonAsync<JsonElement>("/api/ebay");
        Assert.Contains("refresh token is invalid", status.GetProperty("lastSyncError").GetString());

        await fixture.CreateUserAsync("ebay-employee@example.com", "Password123!", "Employee");
        using var employee = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(employee, "ebay-employee@example.com", "Password123!"));
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/ebay")).StatusCode);
    }
}
