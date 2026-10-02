using System.Net;
using System.Net.Http.Json;

namespace MPSellerTools.Tests.Integration;

public class AuthenticationAndAntiforgeryTests(TenantHostFixture fixture) : IClassFixture<TenantHostFixture>
{
    [Fact]
    public async Task Unauthenticated_me_returns_401_json_not_html_redirect()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Login_with_unknown_email_returns_generic_401_not_a_hint_about_existence()
    {
        using var client = fixture.CreateClient();

        var response = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            client, "/api/auth/login", new { email = "nobody@example.com", password = "whatever" });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Invalid email or password", body);
        Assert.DoesNotContain("exist", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task State_changing_request_without_antiforgery_token_is_rejected()
    {
        using var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "x@example.com", password = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_api_route_returns_404_not_the_SPA_shell()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/this-does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Valid_login_with_antiforgery_token_succeeds_and_reports_the_right_role()
    {
        await fixture.CreateUserAsync("valid-login@example.com", "Password123!", "TenantAdmin");
        using var client = fixture.CreateClient();

        var success = await TenantApiHelpers.LoginAsync(client, "valid-login@example.com", "Password123!");
        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/me");

        Assert.True(success);
        Assert.Equal("valid-login@example.com", me.GetProperty("email").GetString());
        Assert.Contains("TenantAdmin", me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Update_profile_changes_display_name_and_is_reflected_on_me()
    {
        await fixture.CreateUserAsync("update-profile@example.com", "Password123!", "TenantAdmin");
        using var client = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(client, "update-profile@example.com", "Password123!");

        var response = await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            client, "/api/auth/me", new { displayName = "Updated Name" });
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Updated Name", body.GetProperty("displayName").GetString());

        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/me");
        Assert.Equal("Updated Name", me.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Update_profile_rejects_blank_display_name()
    {
        await fixture.CreateUserAsync("blank-name@example.com", "Password123!", "TenantAdmin");
        using var client = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(client, "blank-name@example.com", "Password123!");

        var response = await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            client, "/api/auth/me", new { displayName = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_profile_without_authentication_returns_401()
    {
        using var client = fixture.CreateClient();

        var response = await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            client, "/api/auth/me", new { displayName = "Someone" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
