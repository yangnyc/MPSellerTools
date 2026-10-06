using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// The TenantAdmin account controls: signing a user out everywhere, forcing a
/// password reset, and listing and revoking invitations nobody has accepted.
/// </summary>
public class AccountControlsTests(TenantHostFixture fixture) : IClassFixture<TenantHostFixture>
{
    [Fact]
    public async Task Signing_a_user_out_ends_their_session_but_they_can_sign_in_again()
    {
        await fixture.CreateUserAsync("signout-admin@example.com", "Password123!", "TenantAdmin");
        var employeeId = await fixture.CreateUserAsync("signout-emp@example.com", "Password123!", "Employee");
        using var admin = fixture.CreateClient();
        using var employee = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(admin, "signout-admin@example.com", "Password123!");
        await TenantApiHelpers.LoginAsync(employee, "signout-emp@example.com", "Password123!");
        Assert.Equal(HttpStatusCode.OK, (await employee.GetAsync("/api/auth/me")).StatusCode);

        var response = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, $"/api/users/{employeeId}/sign-out", new { });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await employee.GetAsync("/api/auth/me")).StatusCode);

        using var again = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(again, "signout-emp@example.com", "Password123!"));
    }

    [Fact]
    public async Task Forcing_a_password_reset_ends_the_session_and_stops_the_old_password_working()
    {
        await fixture.CreateUserAsync("reset-admin@example.com", "Password123!", "TenantAdmin");
        var employeeId = await fixture.CreateUserAsync("reset-emp@example.com", "Password123!", "Employee");
        using var admin = fixture.CreateClient();
        using var employee = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(admin, "reset-admin@example.com", "Password123!");
        await TenantApiHelpers.LoginAsync(employee, "reset-emp@example.com", "Password123!");

        var response = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, $"/api/users/{employeeId}/force-password-reset", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await employee.GetAsync("/api/auth/me")).StatusCode);

        using var again = fixture.CreateClient();
        Assert.False(await TenantApiHelpers.LoginAsync(again, "reset-emp@example.com", "Password123!"));
    }

    [Fact]
    public async Task An_admin_cannot_force_a_reset_of_their_own_password()
    {
        var adminId = await fixture.CreateUserAsync("reset-self@example.com", "Password123!", "TenantAdmin");
        using var admin = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(admin, "reset-self@example.com", "Password123!");

        var response = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, $"/api/users/{adminId}/force-password-reset", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var again = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(again, "reset-self@example.com", "Password123!"));
    }

    [Fact]
    public async Task A_pending_invitation_is_listed_until_it_is_revoked()
    {
        await fixture.CreateUserAsync("invites-admin@example.com", "Password123!", "TenantAdmin");
        using var admin = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(admin, "invites-admin@example.com", "Password123!");

        var created = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/invitations", new { email = "invited-revoke@example.com", role = "Employee" });
        var invitationId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("invitationId").GetGuid();

        var pending = await admin.GetFromJsonAsync<JsonElement>("/api/invitations");
        var listed = pending.EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == invitationId);
        Assert.Equal("invited-revoke@example.com", listed.GetProperty("email").GetString());
        Assert.Equal("Employee", listed.GetProperty("role").GetString());
        Assert.False(listed.GetProperty("isExpired").GetBoolean());

        var revoked = await TenantApiHelpers.DeleteWithAntiforgeryAsync(admin, $"/api/invitations/{invitationId}");

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        var after = await admin.GetFromJsonAsync<JsonElement>("/api/invitations");
        Assert.DoesNotContain(after.EnumerateArray(), i => i.GetProperty("id").GetGuid() == invitationId);
        Assert.Equal(HttpStatusCode.NotFound, (await TenantApiHelpers.DeleteWithAntiforgeryAsync(admin, $"/api/invitations/{invitationId}")).StatusCode);
    }

    [Fact]
    public async Task Employees_cannot_use_the_account_controls()
    {
        await fixture.CreateUserAsync("controls-admin@example.com", "Password123!", "TenantAdmin");
        var otherId = await fixture.CreateUserAsync("controls-other@example.com", "Password123!", "Employee");
        await fixture.CreateUserAsync("controls-emp@example.com", "Password123!", "Employee");
        using var employee = fixture.CreateClient();
        await TenantApiHelpers.LoginAsync(employee, "controls-emp@example.com", "Password123!");

        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/invitations")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(employee, $"/api/users/{otherId}/sign-out", new { })).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(employee, $"/api/users/{otherId}/force-password-reset", new { })).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await TenantApiHelpers.DeleteWithAntiforgeryAsync(employee, $"/api/invitations/{Guid.NewGuid()}")).StatusCode);
    }
}