using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MPSellerTools.Tests.Integration;

public class ConcurrencyTests(TenantHostFixture fixture) : IClassFixture<TenantHostFixture>
{
    [Fact]
    public async Task Updating_a_product_with_a_stale_row_version_returns_409()
    {
        await fixture.CreateUserAsync("concurrency-admin@example.com", "Password123!", "TenantAdmin");
        using var client = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(client, "concurrency-admin@example.com", "Password123!");

        var createResponse = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            client, "/api/products", new { sku = $"CONC-{Guid.NewGuid():N}"[..12], name = "Concurrency Test", price = 10m, stockQuantity = 5 });
        var product = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var productId = product.GetProperty("id").GetString();
        var staleRowVersion = product.GetProperty("rowVersion").GetString();

        // First update succeeds and moves the row version forward.
        var firstUpdate = await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            client, $"/api/products/{productId}",
            new { name = "Concurrency Test (updated once)", price = 11m, stockQuantity = 5, rowVersion = staleRowVersion });
        Assert.Equal(HttpStatusCode.OK, firstUpdate.StatusCode);

        // Second update reuses the now-stale row version from the original create.
        var secondUpdate = await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            client, $"/api/products/{productId}",
            new { name = "Concurrency Test (conflicting)", price = 12m, stockQuantity = 5, rowVersion = staleRowVersion });

        Assert.Equal(HttpStatusCode.Conflict, secondUpdate.StatusCode);
    }
}
