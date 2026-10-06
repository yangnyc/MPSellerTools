using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPSellerTools.Core.Business;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// Products and order lines from before variants existed, written straight
/// to the tables as the old code left them, and what the backfill makes of them.
/// </summary>
public class CatalogBackfillTests(TenantHostFixture fixture) : IClassFixture<TenantHostFixture>
{
    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> work)
    {
        using var scope = fixture.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    /// <summary>Inserts a product row the way the schema held it before this feature: no variant, no balance.</summary>
    private Task<int> InsertLegacyProductAsync(Guid id, string sku, decimal price, int stock) => WithDbAsync(db =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Products (Id, Sku, Name, Price, StockQuantity, IsArchived, CreatedAtUtc, UpdatedAtUtc)
            VALUES ({id}, {sku}, {"Legacy " + sku}, {price}, {stock}, 0, SYSUTCDATETIME(), SYSUTCDATETIME())
            """));

    [Fact]
    public async Task Backfill_can_be_previewed_run_and_run_again_keeps_old_references_and_publishes_nothing()
    {
        var (first, second, clash) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await InsertLegacyProductAsync(first, "LEGACY-1", 12.5m, 7);
        await InsertLegacyProductAsync(second, "LEGACY-2", 3m, 0);
        await InsertLegacyProductAsync(clash, "LEGACY-CLASH", 1m, 1);
        var (orderId, itemId) = (Guid.NewGuid(), Guid.NewGuid());
        await WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Orders (Id, OrderNumber, Status, Total, CreatedAtUtc, UpdatedAtUtc)
                VALUES ({orderId}, {"ORD-LEGACY-1"}, 0, 25, SYSUTCDATETIME(), SYSUTCDATETIME());
                INSERT INTO OrderItems (Id, OrderId, ProductId, Quantity, UnitPrice) VALUES ({itemId}, {orderId}, {first}, 2, 12.5);
                """);
            // Another product's variant already answers to the third product's SKU.
            db.ProductVariants.Add(new Core.Marketplace.ProductVariant
            {
                Id = Guid.NewGuid(), ProductId = second, Sku = "LEGACY-CLASH", Name = "Mislabelled", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
            });
            return await db.SaveChangesAsync();
        });

        await fixture.CreateUserAsync("backfill-admin@example.com", "Password123!", "TenantAdmin");
        using var admin = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(admin, "backfill-admin@example.com", "Password123!"));

        // A dry run counts and changes nothing.
        var preview = await (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/catalog/backfill?dryRun=true", new { })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(preview.GetProperty("dryRun").GetBoolean());
        Assert.Equal(2, preview.GetProperty("variantsCreated").GetInt32());
        Assert.Equal("LEGACY-CLASH", Assert.Single(preview.GetProperty("conflicts").EnumerateArray()).GetProperty("sku").GetString());
        Assert.False(await WithDbAsync(db => db.ProductVariants.AnyAsync(v => v.ProductId == first)));

        var run = await (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/catalog/backfill?dryRun=false", new { })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((2, 1, 1), (run.GetProperty("variantsCreated").GetInt32(), run.GetProperty("orderLinesLinked").GetInt32(), run.GetProperty("remaining").GetInt32()));

        await WithDbAsync(async db =>
        {
            var variant = await db.ProductVariants.SingleAsync(v => v.ProductId == first);
            // The product keeps its id and SKU; the variant takes the same SKU, price and stock.
            Assert.Equal(("LEGACY-1", 12.5m, true), (variant.Sku, variant.Price, variant.IsDefault));
            Assert.Equal(7, (await db.InventoryBalances.SingleAsync(b => b.VariantId == variant.Id)).OnHand);
            var item = await db.OrderItems.SingleAsync(i => i.Id == itemId);
            Assert.Equal((first, (Guid?)variant.Id), (item.ProductId, item.VariantId));
            // The conflict is left for a person: no variant was made up for it.
            Assert.False(await db.ProductVariants.AnyAsync(v => v.ProductId == clash));
            // Nothing was listed, queued or given an external id on the strength of a backfill.
            Assert.Equal((0, 0, 0), (await db.ChannelListings.CountAsync(), await db.SyncJobs.CountAsync(), await db.ExternalReferences.CountAsync()));
            return 0;
        });

        // Running it again finds nothing more to do.
        var again = await (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/catalog/backfill?dryRun=false", new { })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((0, 0), (again.GetProperty("variantsCreated").GetInt32(), again.GetProperty("orderLinesLinked").GetInt32()));
        Assert.Equal(1, await WithDbAsync(db => db.ProductVariants.CountAsync(v => v.ProductId == first)));
    }

    [Fact]
    public async Task A_legacy_product_edited_before_the_backfill_reaches_it_gets_its_variant_from_the_edit()
    {
        var id = Guid.NewGuid();
        await InsertLegacyProductAsync(id, "LEGACY-EDITED", 5m, 2);
        await fixture.CreateUserAsync("backfill-edit@example.com", "Password123!", "TenantAdmin");
        using var admin = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(admin, "backfill-edit@example.com", "Password123!"));

        // The product screen works on it as it always did.
        var product = await admin.GetFromJsonAsync<JsonElement>($"/api/products/{id}");
        var updated = await TenantApiHelpers.PutJsonWithAntiforgeryAsync(admin, $"/api/products/{id}", new
        {
            name = "Legacy LEGACY-EDITED", price = 6m, stockQuantity = 9, rowVersion = product.GetProperty("rowVersion").GetString(),
        });
        Assert.True(updated.IsSuccessStatusCode);

        var variantId = await WithDbAsync(async db =>
        {
            var variant = await db.ProductVariants.SingleAsync(v => v.ProductId == id);
            Assert.Equal((6m, 9), (variant.Price, (await db.InventoryBalances.SingleAsync(b => b.VariantId == variant.Id)).OnHand));
            return variant.Id;
        });

        // The backfill then has nothing to add for it, and does not make a second variant.
        using var scope = fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CatalogBackfill>().RunAsync(dryRun: false, default);
        Assert.Equal(variantId, await WithDbAsync(async db => (await db.ProductVariants.SingleAsync(v => v.ProductId == id)).Id));

        // With stock accounting off, as it is by default, an order leaves stock alone, as before.
        var order = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, "/api/orders", new { items = new[] { new { productId = id, quantity = 50 } } });
        Assert.True(order.IsSuccessStatusCode);
        Assert.Equal((9, 0), await WithDbAsync(async db =>
        {
            var balance = await db.InventoryBalances.SingleAsync(b => b.VariantId == variantId);
            return (balance.OnHand, balance.Reserved);
        }));
        Assert.Equal(OrderStatus.New, await WithDbAsync(async db => (await db.Orders.SingleAsync(o => o.Items.Any(i => i.ProductId == id))).Status));
    }
}
