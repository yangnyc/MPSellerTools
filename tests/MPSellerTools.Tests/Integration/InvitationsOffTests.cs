using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MPSellerTools.Tests.Integration;

/// <summary>A host as it runs unless told otherwise: with invitations switched off.</summary>
public class InvitationsOffFixture : TenantHostFixture
{
    protected override bool InvitationsEnabled => false;
}

/// <summary>
/// With invitations off nobody is invited and no invitation is accepted;
/// a TenantAdmin adds a user with a password instead.
/// </summary>
public class InvitationsOffTests(InvitationsOffFixture fixture) : IClassFixture<InvitationsOffFixture>
{
    [Fact]
    public async Task A_user_is_added_with_a_password_and_nobody_is_invited()
    {
        await fixture.CreateUserAsync("off-admin@example.com", "Password123!", "TenantAdmin");
        using var admin = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(admin, "off-admin@example.com", "Password123!"));

        // The page is told, so it offers adding a user rather than inviting one.
        Assert.False((await admin.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("invitationsEnabled").GetBoolean());

        // Both ways of inviting are refused, and say what to do instead.
        foreach (var url in new[] { "/api/invitations", "/api/users/invite" })
        {
            var refused = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(admin, url, new { email = "invited@example.com", role = "Employee" });
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Contains("with a password", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
        }
        // A link from before they were switched off no longer works.
        using var anonymous = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Conflict, (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            anonymous, "/api/invitations/accept", new { token = "anything", displayName = "Someone", password = "Password123!" })).StatusCode);

        // A TenantAdmin adds the user with a password, and they can sign in with it at once.
        Assert.Equal(HttpStatusCode.NoContent, (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/users", new { email = "added@example.com", role = "Employee", password = "Added-Password-123" })).StatusCode);
        using var added = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(added, "added@example.com", "Added-Password-123"));

        // A password that is too weak is turned down with the reason.
        Assert.Equal(HttpStatusCode.BadRequest, (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/users", new { email = "weak@example.com", role = "Employee", password = "short" })).StatusCode);

        // An employee still cannot add anyone.
        Assert.Equal(HttpStatusCode.Forbidden, (await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            added, "/api/users", new { email = "another@example.com", role = "Employee", password = "Added-Password-123" })).StatusCode);
    }
}
