using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Marketplace;
using MPSellerTools.TenantHost.Marketplace;
using MPSellerTools.TenantHost.Marketplace.Channels;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// Variants, stock and orders, with stock accounting switched on: what has
/// to hold whichever channel an order comes from.
/// </summary>
public class CatalogAndInventoryTests(MarketplaceFixture fixture) : IClassFixture<MarketplaceFixture>
{
    private Task<InventoryBalance> BalanceAsync(Guid variantId) =>
        fixture.WithDbAsync(db => db.InventoryBalances.AsNoTracking().FirstAsync(b => b.VariantId == variantId));

    private Task<T> InventoryAsync<T>(Func<InventoryService, Task<T>> work) =>
        fixture.WithScopeAsync(services => work(services.GetRequiredService<InventoryService>()));

    [Fact]
    public async Task Two_variants_of_a_product_have_their_own_skus_and_stock_and_share_the_product_data()
    {
        using var admin = await fixture.AdminAsync("cat-variants@example.com");
        var (productId, defaultVariantId) = await fixture.CreateProductAsync(admin, "TEE-BLUE", price: 15m, stock: 4);

        var added = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, $"/api/catalog/products/{productId}/variants",
            new { sku = "TEE-RED", name = "Red", options = new Dictionary<string, string> { ["Color"] = "Red" }, condition = 0, price = 17m }));
        var variants = added.GetProperty("variants").EnumerateArray().ToList();
        Assert.Equal(["TEE-BLUE", "TEE-RED"], variants.Select(v => v.GetProperty("sku").GetString()));
        // The product's own SKU and stock became its default variant.
        Assert.True(variants[0].GetProperty("isDefault").GetBoolean());
        Assert.Equal(4, variants[0].GetProperty("onHand").GetInt32());
        var redId = variants[1].GetProperty("id").GetGuid();

        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, $"/api/catalog/variants/{redId}/inventory", new { onHand = 9, safetyStock = 2 }));

        var after = await admin.GetFromJsonAsync<JsonElement>($"/api/catalog/products/{productId}");
        var byId = after.GetProperty("variants").EnumerateArray().ToDictionary(v => v.GetProperty("id").GetGuid());
        Assert.Equal((9, 7), (byId[redId].GetProperty("onHand").GetInt32(), byId[redId].GetProperty("availableToSell").GetInt32()));
        Assert.Equal(4, byId[defaultVariantId].GetProperty("onHand").GetInt32());
        // One brand and description, held once, for both.
        Assert.Equal("TestBrand", after.GetProperty("brand").GetString());

        // A second variant cannot take a SKU already in use.
        var duplicate = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, $"/api/catalog/products/{productId}/variants", new { sku = "TEE-RED", condition = 0, price = 1m });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Editing_the_product_keeps_its_default_variant_and_balance_in_step_and_writes_the_events_with_the_change()
    {
        using var admin = await fixture.AdminAsync("cat-mirror@example.com");
        var (productId, variantId) = await fixture.CreateProductAsync(admin, "MIRROR-1", price: 10m, stock: 5);
        var product = await admin.GetFromJsonAsync<JsonElement>($"/api/products/{productId}");

        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, $"/api/products/{productId}", new
        {
            name = "Product MIRROR-1",
            price = 12.5m,
            stockQuantity = 8,
            rowVersion = product.GetProperty("rowVersion").GetString(),
        }));

        await fixture.WithDbAsync(async db =>
        {
            Assert.Equal(12.5m, (await db.ProductVariants.SingleAsync(v => v.Id == variantId)).Price);
            Assert.Equal(8, (await db.InventoryBalances.SingleAsync(b => b.VariantId == variantId)).OnHand);
            var events = await db.OutboxEvents.Where(e => e.SubjectId == variantId).Select(e => e.Type).ToListAsync();
            Assert.Contains(OutboxEvent.VariantPriceChanged, events);
            Assert.Contains(OutboxEvent.InventoryChanged, events);
            return 0;
        });

        // A save that fails takes its events down with it: here a price change rides along with a product that cannot be inserted.
        var eventsBefore = await fixture.WithDbAsync(db => db.OutboxEvents.CountAsync(e => e.SubjectId == variantId));
        await fixture.WithDbAsync(async db =>
        {
            var tracked = await db.Products.SingleAsync(p => p.Id == productId);
            tracked.Price = 99m;
            db.Products.Add(new Product { Id = Guid.NewGuid(), Sku = "MIRROR-1", Name = "Duplicate SKU", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            return 0;
        });

        await fixture.WithDbAsync(async db =>
        {
            Assert.Equal(12.5m, (await db.Products.SingleAsync(p => p.Id == productId)).Price);
            Assert.Equal(12.5m, (await db.ProductVariants.SingleAsync(v => v.Id == variantId)).Price);
            Assert.Equal(eventsBefore, await db.OutboxEvents.CountAsync(e => e.SubjectId == variantId));
            return 0;
        });
    }

    [Fact]
    public async Task Two_requests_at_once_for_the_last_unit_cannot_both_reserve_it()
    {
        using var admin = await fixture.AdminAsync("inv-race@example.com");
        var (_, variantId) = await fixture.CreateProductAsync(admin, "LAST-UNIT", stock: 1);

        // Each request has its own scope, connection and transaction, as two web requests would.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => InventoryAsync(
            inventory => inventory.ReserveAsync(variantId, 1, $"race-{i}", null, honorSafetyStock: true, null, default)))));

        Assert.Equal(1, attempts.Count(r => r == ReserveResult.Reserved));
        Assert.Equal(7, attempts.Count(r => r == ReserveResult.Insufficient));
        var balance = await BalanceAsync(variantId);
        Assert.Equal((1, 1, 0), (balance.OnHand, balance.Reserved, balance.AvailableToSell));
    }

    [Fact]
    public async Task Reserving_shipping_and_receiving_a_return_each_count_once_however_often_they_are_repeated()
    {
        using var admin = await fixture.AdminAsync("inv-idem@example.com");
        var (productId, variantId) = await fixture.CreateProductAsync(admin, "IDEM-1", stock: 5);

        Assert.Equal(ReserveResult.Reserved, await InventoryAsync(i => i.ReserveAsync(variantId, 2, "evt-1", null, true, null, default)));
        Assert.Equal(ReserveResult.AlreadyReserved, await InventoryAsync(i => i.ReserveAsync(variantId, 2, "evt-1", null, true, null, default)));
        Assert.Equal(2, (await BalanceAsync(variantId)).Reserved);

        Assert.True(await InventoryAsync(i => i.ShipAsync("evt-1", default)));
        Assert.False(await InventoryAsync(i => i.ShipAsync("evt-1", default)));
        var shipped = await BalanceAsync(variantId);
        // Shipping takes the units off hand and out of reserved together: sold once, deducted once.
        Assert.Equal((3, 0), (shipped.OnHand, shipped.Reserved));
        Assert.Equal(3, await fixture.WithDbAsync(async db => (await db.Products.SingleAsync(p => p.Id == productId)).StockQuantity));

        // A cancelled hold frees its units and cannot then be shipped.
        Assert.Equal(ReserveResult.Reserved, await InventoryAsync(i => i.ReserveAsync(variantId, 1, "evt-2", null, true, null, default)));
        Assert.True(await InventoryAsync(i => i.ReleaseAsync("evt-2", default)));
        Assert.False(await InventoryAsync(i => i.ShipAsync("evt-2", default)));
        Assert.Equal((3, 0), ((await BalanceAsync(variantId)).OnHand, (await BalanceAsync(variantId)).Reserved));

        // Stock comes back only on a receipt at the warehouse, and one receipt is one receipt.
        Assert.True(await InventoryAsync(i => i.ReceiveReturnAsync(variantId, 1, "return:RMA-7", default)));
        Assert.False(await InventoryAsync(i => i.ReceiveReturnAsync(variantId, 1, "return:RMA-7", default)));
        Assert.Equal(4, (await BalanceAsync(variantId)).OnHand);
    }

    [Fact]
    public async Task Safety_stock_is_held_back_from_buyers_and_an_expired_hold_is_released()
    {
        using var admin = await fixture.AdminAsync("inv-safety@example.com");
        var (_, variantId) = await fixture.CreateProductAsync(admin, "SAFETY-1", stock: 3);
        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, $"/api/catalog/variants/{variantId}/inventory", new { safetyStock = 2 }));

        Assert.Equal(ReserveResult.Insufficient, await InventoryAsync(i => i.ReserveAsync(variantId, 2, "safety-a", null, true, null, default)));
        Assert.Equal(ReserveResult.Reserved, await InventoryAsync(
            i => i.ReserveAsync(variantId, 1, "safety-b", null, true, DateTime.UtcNow.AddMinutes(-5), default)));
        Assert.Equal(0, (await BalanceAsync(variantId)).AvailableToSell);

        Assert.Equal(1, await InventoryAsync(i => i.ExpireAsync(default)));
        Assert.Equal((0, 1), ((await BalanceAsync(variantId)).Reserved, (await BalanceAsync(variantId)).AvailableToSell));
    }

    [Fact]
    public async Task An_order_made_here_holds_stock_ships_it_on_completion_and_is_refused_when_there_is_not_enough()
    {
        using var admin = await fixture.AdminAsync("inv-orders@example.com");
        var (productId, variantId) = await fixture.CreateProductAsync(admin, "ORDER-STOCK", stock: 3);

        var tooMany = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/orders", new { items = new[] { new { productId, quantity = 4 } } });
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);
        // The refused order left nothing behind: no order, nothing held.
        Assert.Equal(0, (await BalanceAsync(variantId)).Reserved);
        Assert.False(await fixture.WithDbAsync(db => db.OrderItems.AnyAsync(i => i.ProductId == productId)));

        var order = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/orders", new { items = new[] { new { productId, quantity = 2 } } }));
        var orderId = order.GetProperty("id").GetString();
        Assert.Equal((3, 2), ((await BalanceAsync(variantId)).OnHand, (await BalanceAsync(variantId)).Reserved));

        var inProgress = await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, $"/api/orders/{orderId}/status", new { status = (int)OrderStatus.InProgress, rowVersion = order.GetProperty("rowVersion").GetString() }));
        await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, $"/api/orders/{orderId}/status", new { status = (int)OrderStatus.Completed, rowVersion = inProgress.GetProperty("rowVersion").GetString() }));

        var done = await BalanceAsync(variantId);
        Assert.Equal((1, 0), (done.OnHand, done.Reserved));
    }

    [Fact]
    public async Task The_order_list_shows_every_order_of_a_company_with_several()
    {
        using var admin = await fixture.AdminAsync("inv-order-list@example.com");
        var (productId, _) = await fixture.CreateProductAsync(admin, "ORDER-LIST", stock: 10);
        for (var quantity = 1; quantity <= 3; quantity++)
        {
            await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
                admin, "/api/orders", new { items = new[] { new { productId, quantity } } }));
        }

        var list = await MarketplaceFixture.JsonAsync(await admin.GetAsync("/api/orders"));

        var mine = list.EnumerateArray()
            .Where(order => order.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("productSku").GetString() == "ORDER-LIST"))
            .ToList();
        Assert.Equal(3, mine.Count);
    }

    [Fact]
    public async Task A_marketplace_order_delivered_twice_is_one_order_holding_and_deducting_its_stock_once()
    {
        using var admin = await fixture.AdminAsync("inv-import@example.com");
        var (_, variantId) = await fixture.CreateProductAsync(admin, "IMPORT-1", stock: 5);
        var (accountId, _) = await fixture.CreateAccountAsync(admin, (int)SalesChannel.Walmart, "Import store", "WALMART_US");

        var incoming = new ChannelOrder(
            "PO-1001", OrderStatus.New, DateTime.UtcNow, "USD", FulfilledByChannel: false,
            [new ChannelOrderLine("1", "IMPORT-1", "Thing", 2, 19.99m), new ChannelOrderLine("2", "NO-SUCH-SKU", "Mystery", 1, 5m)]);

        async Task<IngestResult> IngestAsync(ChannelOrder order) => await fixture.WithScopeAsync(async services =>
        {
            var account = await services.GetRequiredService<Infrastructure.Tenants.TenantDbContext>().ChannelAccounts.SingleAsync(a => a.Id == accountId);
            return await services.GetRequiredService<OrderIngestionService>()
                .IngestAsync(account, SalesChannel.Walmart, order, UnknownSkuPolicy.RouteForResolution, default);
        });

        var first = await IngestAsync(incoming);
        Assert.Equal((IngestOutcome.Created, 1), (first.Outcome, first.Issues));
        Assert.Equal(IngestOutcome.Unchanged, (await IngestAsync(incoming)).Outcome);

        await fixture.WithDbAsync(async db =>
        {
            var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.ChannelAccountId == accountId && o.ExternalOrderId == "PO-1001");
            Assert.Equal("WMT-PO-1001", order.OrderNumber);
            // The line whose SKU matched nothing is not guessed at: it is left off and put up for resolution.
            Assert.Equal(variantId, Assert.Single(order.Items).VariantId);
            var issue = await db.OrderLineIssues.SingleAsync(i => i.ExternalOrderId == "PO-1001");
            Assert.Equal((OrderLineIssueReason.UnknownSku, "NO-SUCH-SKU"), (issue.Reason, issue.SellerSku));
            Assert.Equal(1, await db.InventoryReservations.CountAsync(r => r.OrderId == order.Id));
            return 0;
        });
        Assert.Equal((5, 2), ((await BalanceAsync(variantId)).OnHand, (await BalanceAsync(variantId)).Reserved));

        // Shipped, and told so twice.
        var shipped = incoming with { Status = OrderStatus.Completed };
        Assert.Equal(IngestOutcome.Updated, (await IngestAsync(shipped)).Outcome);
        Assert.Equal(IngestOutcome.Unchanged, (await IngestAsync(shipped)).Outcome);
        Assert.Equal((3, 0), ((await BalanceAsync(variantId)).OnHand, (await BalanceAsync(variantId)).Reserved));

        // An order for more than there is still stands, as the channel already sold it; the shortfall is flagged.
        var oversold = new ChannelOrder("PO-1002", OrderStatus.New, DateTime.UtcNow, "USD", false, [new ChannelOrderLine("1", "IMPORT-1", "Thing", 9, 19.99m)]);
        var over = await IngestAsync(oversold);
        Assert.Equal((IngestOutcome.Created, 1), (over.Outcome, over.Issues));
        Assert.True(await fixture.WithDbAsync(db => db.OrderLineIssues.AnyAsync(
            i => i.ExternalOrderId == "PO-1002" && i.Reason == OrderLineIssueReason.InventoryShortfall)));
        Assert.Equal(0, (await BalanceAsync(variantId)).Reserved);
    }

    [Fact]
    public async Task A_seller_sku_that_leads_to_two_variants_is_put_up_for_resolution_not_guessed()
    {
        using var admin = await fixture.AdminAsync("inv-ambiguous@example.com");
        var (_, firstVariant) = await fixture.CreateProductAsync(admin, "AMB-A");
        var (_, secondVariant) = await fixture.CreateProductAsync(admin, "AMB-B");
        var (accountId, usMarket) = await fixture.CreateAccountAsync(admin, (int)SalesChannel.Amazon, "Ambiguous store", "ATVPDKIKX0DER", sellerId: "SELLER1");
        var caMarket = (await MarketplaceFixture.JsonAsync(await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, $"/api/channels/{accountId}/markets", new { marketplaceCode = "A2EUQ1WTGCTBG2", currency = "CAD" }))).GetProperty("id").GetGuid();

        // The same seller SKU on two marketplaces of one account, standing for different variants.
        await fixture.SaveListingAsync(admin, usMarket, firstVariant, new { sellerSku = "SHARED-SKU" });
        await fixture.SaveListingAsync(admin, caMarket, secondVariant, new { sellerSku = "SHARED-SKU" });

        var result = await fixture.WithScopeAsync(async services =>
        {
            var account = await services.GetRequiredService<Infrastructure.Tenants.TenantDbContext>().ChannelAccounts.SingleAsync(a => a.Id == accountId);
            return await services.GetRequiredService<OrderIngestionService>().IngestAsync(
                account, SalesChannel.Amazon,
                new ChannelOrder("111-222", OrderStatus.New, null, "USD", false, [new ChannelOrderLine("L1", "SHARED-SKU", null, 1, 3m)]),
                UnknownSkuPolicy.RouteForResolution, default);
        });

        Assert.Equal(1, result.Issues);
        await fixture.WithDbAsync(async db =>
        {
            Assert.Equal(OrderLineIssueReason.AmbiguousSku, (await db.OrderLineIssues.SingleAsync(i => i.ExternalOrderId == "111-222")).Reason);
            Assert.Empty((await db.Orders.Include(o => o.Items).SingleAsync(o => o.ExternalOrderId == "111-222")).Items);
            return 0;
        });

        // Within one marketplace the same seller SKU cannot be given to a second variant at all.
        var clash = await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            admin, "/api/channel-listings", new { channelMarketId = usMarket, variantId = secondVariant, sellerSku = "SHARED-SKU", fulfillmentMode = 0 });
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
    }

    [Fact]
    public async Task An_employee_can_read_the_catalog_but_cannot_change_stock_or_channels()
    {
        using var admin = await fixture.AdminAsync("cat-rbac-admin@example.com");
        var (productId, variantId) = await fixture.CreateProductAsync(admin, "RBAC-1");
        await fixture.CreateUserAsync("cat-rbac-employee@example.com", "Password123!", "Employee");
        using var employee = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(employee, "cat-rbac-employee@example.com", "Password123!"));

        Assert.Equal(HttpStatusCode.OK, (await employee.GetAsync($"/api/catalog/products/{productId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            employee, $"/api/catalog/variants/{variantId}/inventory", new { onHand = 100 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/channels")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            employee, "/api/channels", new { channel = 3, name = "Mine", environment = 0 })).StatusCode);

        using var anonymous = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/channel-listings")).StatusCode);
    }
}
