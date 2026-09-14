using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// Brief §13 check #8: "Employees cannot invoke user management, create
/// tenants, modify products, or read another employee's assigned
/// task/order... Test direct HTTP requests as well as menus." These tests
/// hit the API directly, the same way a malicious or buggy client would,
/// bypassing the UI entirely.
/// </summary>
public class RbacTests(TenantHostFixture fixture) : IClassFixture<TenantHostFixture>
{
    [Fact]
    public async Task Employee_cannot_list_users()
    {
        await fixture.CreateUserAsync("emp-users@example.com", "Password123!", "Employee");
        using var client = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(client, "emp-users@example.com", "Password123!");

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_create_a_product()
    {
        await fixture.CreateUserAsync("emp-products@example.com", "Password123!", "Employee");
        using var client = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(client, "emp-products@example.com", "Password123!");

        var response = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            client, "/api/products", new { sku = "HACK-1", name = "Hacked", price = 1m, stockQuantity = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employee_can_read_the_product_catalog_read_only()
    {
        await fixture.CreateUserAsync("emp-catalog@example.com", "Password123!", "Employee");
        using var client = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(client, "emp-catalog@example.com", "Password123!");

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Employee_gets_404_not_403_for_an_order_assigned_to_someone_else()
    {
        var adminId = await fixture.CreateUserAsync("admin-orders@example.com", "Password123!", "TenantAdmin");
        var otherEmployeeId = await fixture.CreateUserAsync("other-emp@example.com", "Password123!", "Employee");
        await fixture.CreateUserAsync("this-emp@example.com", "Password123!", "Employee");

        using var adminClient = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(adminClient, "admin-orders@example.com", "Password123!");
        var productResponse = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            adminClient, "/api/products", new { sku = $"RBAC-{Guid.NewGuid():N}"[..12], name = "RBAC Test Product", price = 5m, stockQuantity = 10 });
        var product = await productResponse.Content.ReadFromJsonAsync<JsonElement>();
        var productId = product.GetProperty("id").GetString();

        var orderResponse = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            adminClient, "/api/orders",
            new { items = new[] { new { productId, quantity = 1 } }, assignedUserId = otherEmployeeId });
        var order = await orderResponse.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = order.GetProperty("id").GetString();

        using var employeeClient = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(employeeClient, "this-emp@example.com", "Password123!");

        var response = await employeeClient.GetAsync($"/api/orders/{orderId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Employee_cannot_change_order_status_even_when_assigned_to_them()
    {
        var employeeId = await fixture.CreateUserAsync("order-status-emp@example.com", "Password123!", "Employee");
        await fixture.CreateUserAsync("order-status-admin@example.com", "Password123!", "TenantAdmin");

        using var adminClient = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(adminClient, "order-status-admin@example.com", "Password123!");
        var productResponse = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            adminClient, "/api/products", new { sku = $"OS-{Guid.NewGuid():N}"[..12], name = "Status Test Product", price = 5m, stockQuantity = 10 });
        var product = await productResponse.Content.ReadFromJsonAsync<JsonElement>();
        var orderResponse = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            adminClient, "/api/orders",
            new { items = new[] { new { productId = product.GetProperty("id").GetString(), quantity = 1 } }, assignedUserId = employeeId });
        var order = await orderResponse.Content.ReadFromJsonAsync<JsonElement>();

        using var employeeClient = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(employeeClient, "order-status-emp@example.com", "Password123!");

        var response = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            employeeClient, $"/api/orders/{order.GetProperty("id").GetString()}/status",
            new { status = 1, rowVersion = order.GetProperty("rowVersion").GetString() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
