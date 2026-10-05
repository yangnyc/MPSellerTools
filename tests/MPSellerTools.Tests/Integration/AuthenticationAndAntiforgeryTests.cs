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

    [Fact]
    public async Task Theme_settings_are_saved_on_the_profile_and_returned_on_me()
    {
        await fixture.CreateUserAsync("theme-save@example.com", "Password123!", "Employee");
        using var client = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(client, "theme-save@example.com", "Password123!");

        var before = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/me");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, before.GetProperty("theme").ValueKind);

        var response = await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            client,
            "/api/auth/me/theme",
            new { themeName = "noir", darkMode = true, whiteSidenav = false, sidenavTint = "teal", sidenavColor = "amber", fixedNavbar = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/me");
        var theme = me.GetProperty("theme");
        Assert.Equal("noir", theme.GetProperty("themeName").GetString());
        Assert.True(theme.GetProperty("darkMode").GetBoolean());
        Assert.False(theme.GetProperty("whiteSidenav").GetBoolean());
        Assert.Equal("teal", theme.GetProperty("sidenavTint").GetString());
        Assert.Equal("amber", theme.GetProperty("sidenavColor").GetString());
        Assert.False(theme.GetProperty("fixedNavbar").GetBoolean());
    }

    [Fact]
    public async Task Theme_settings_reject_values_that_are_not_swatch_names()
    {
        await fixture.CreateUserAsync("theme-invalid@example.com", "Password123!", "Employee");
        using var client = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(client, "theme-invalid@example.com", "Password123!");

        var response = await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            client,
            "/api/auth/me/theme",
            new { darkMode = false, whiteSidenav = false, sidenavTint = (string?)null, sidenavColor = "url(javascript:1)", fixedNavbar = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Theme_settings_without_a_theme_name_are_still_accepted()
    {
        await fixture.CreateUserAsync("theme-unnamed@example.com", "Password123!", "Employee");
        using var client = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(client, "theme-unnamed@example.com", "Password123!");

        var response = await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            client,
            "/api/auth/me/theme",
            new { darkMode = false, whiteSidenav = false, sidenavTint = (string?)null, sidenavColor = "steel", fixedNavbar = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/me");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, me.GetProperty("theme").GetProperty("themeName").ValueKind);
    }

    [Fact]
    public async Task Theme_settings_reject_a_theme_name_that_is_not_a_plain_name()
    {
        await fixture.CreateUserAsync("theme-badname@example.com", "Password123!", "Employee");
        using var client = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(client, "theme-badname@example.com", "Password123!");

        var response = await TenantApiHelpers.PutJsonWithAntiforgeryAsync(
            client,
            "/api/auth/me/theme",
            new { themeName = "<script>", darkMode = false, whiteSidenav = false, sidenavTint = (string?)null, sidenavColor = "steel", fixedNavbar = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
