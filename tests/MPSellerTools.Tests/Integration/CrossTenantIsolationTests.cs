using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// Brief §13 checks #5/#6: two tenants' data never crosses, and one
/// instance's session cookie is rejected by another — verified here with
/// two real TenantHost instances (separate LocalDB databases, separate
/// Data Protection key rings, separate cookie names — see
/// TenantDbContext/Program.cs), not two rows in one shared table.
/// </summary>
public class CrossTenantIsolationTests : IAsyncLifetime
{
    private readonly TenantHostFixture _tenantA = new();
    private readonly TenantHostFixture _tenantB = new();

    public async Task InitializeAsync()
    {
        await ((IAsyncLifetime)_tenantA).InitializeAsync();
        await ((IAsyncLifetime)_tenantB).InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await ((IAsyncLifetime)_tenantA).DisposeAsync();
        await ((IAsyncLifetime)_tenantB).DisposeAsync();
    }

    [Fact]
    public async Task A_product_created_in_tenant_A_is_absent_from_tenant_B_even_with_the_same_SKU()
    {
        await _tenantA.CreateUserAsync("admin@a.example.com", "Password123!", "TenantAdmin");
        await _tenantB.CreateUserAsync("admin@b.example.com", "Password123!", "TenantAdmin");

        using var clientA = _tenantA.CreateClient();
        await TenantApiHelpers.LoginAsync(clientA, "admin@a.example.com", "Password123!");
        await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            clientA, "/api/products", new { sku = "SHARED-SKU-001", name = "Tenant A's Product", price = 1m, stockQuantity = 1 });

        using var clientB = _tenantB.CreateClient();
        await TenantApiHelpers.LoginAsync(clientB, "admin@b.example.com", "Password123!");
        var productsInB = await clientB.GetFromJsonAsync<JsonElement>("/api/products");

        Assert.DoesNotContain(
            productsInB.EnumerateArray(),
            p => p.GetProperty("sku").GetString() == "SHARED-SKU-001");
    }

    [Fact]
    public async Task Tenant_A_session_cookie_is_rejected_when_submitted_to_tenant_B()
    {
        await _tenantA.CreateUserAsync("cookie-test@a.example.com", "Password123!", "TenantAdmin");

        // Login manually (rather than via the LoginAsync helper) so we can
        // capture the raw Set-Cookie header values and replay just the auth
        // cookie against a completely different tenant instance below.
        // Uses CreateClient() (not Server.CreateClient()) so the fixture's
        // https:// base address applies — required by our Secure-cookie
        // config, see TenantHostFixture's constructor comment.
        using var rawClientA = _tenantA.CreateClient();
        var tokenResponse = await rawClientA.GetAsync("/api/antiforgery/token");
        var antiforgeryCookie = tokenResponse.Headers.GetValues("Set-Cookie")
            .First(c => c.Contains("Xsrf"));

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "cookie-test@a.example.com", password = "Password123!" }),
        };
        loginRequest.Headers.Add("Cookie", antiforgeryCookie.Split(';')[0]);
        var tokenJson = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        loginRequest.Headers.Add("X-CSRF-TOKEN", tokenJson.GetProperty("token").GetString());
        var loginResponse = await rawClientA.SendAsync(loginRequest);
        var authCookie = loginResponse.Headers.GetValues("Set-Cookie").First(c => c.Contains("Auth"));

        // Submit tenant A's raw auth cookie to tenant B directly.
        using var clientB = _tenantB.CreateClient();
        var probeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        probeRequest.Headers.Add("Cookie", authCookie.Split(';')[0]);
        var probeResponse = await clientB.SendAsync(probeRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, probeResponse.StatusCode);
    }
}
