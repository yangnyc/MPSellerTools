using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPSellerTools.Infrastructure.Tenants;

namespace MPSellerTools.Tests.Integration;

/// <summary>
/// The platform console's way into a tenant: the tenant's platform access
/// key opens that tenant's user management and nothing else.
/// </summary>
public class PlatformAccessTests(TenantHostFixture fixture) : IClassFixture<TenantHostFixture>
{
    private HttpClient PlatformClient(string? key = null)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Platform-Key", key ?? fixture.PlatformAccessKey);
        client.DefaultRequestHeaders.Add("X-Platform-Actor", "ops@platform.test");
        return client;
    }

    [Fact]
    public async Task The_key_lists_users_and_manages_them_and_the_audit_log_names_the_platform_admin()
    {
        await fixture.CreateUserAsync("pa-admin@example.com", "Password123!", "TenantAdmin");
        var employeeId = await fixture.CreateUserAsync("pa-emp@example.com", "Password123!", "Employee");
        using var platform = PlatformClient();

        var users = await platform.GetFromJsonAsync<JsonElement>("/api/users");
        Assert.Contains(users.EnumerateArray(), u => u.GetProperty("email").GetString() == "pa-emp@example.com");

        // No session cookie and no antiforgery token: the key is the credential.
        var blocked = await platform.PostAsync($"/api/users/{employeeId}/block", null);

        Assert.Equal(HttpStatusCode.NoContent, blocked.StatusCode);
        using var employee = fixture.CreateClient();
        Assert.False(await TenantApiHelpers.LoginAsync(employee, "pa-emp@example.com", "Password123!"));

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var entry = await db.AuditEntries.SingleAsync(a => a.Action == "UserBlocked" && a.Details == "user=pa-emp@example.com");
        Assert.Equal("platform:ops@platform.test", entry.ActorEmail);
    }

    [Fact]
    public async Task The_key_invites_a_user_who_can_then_accept_and_sign_in()
    {
        using var platform = PlatformClient();

        var invited = await platform.PostAsJsonAsync("/api/users/invite", new { email = "pa-new@example.com", role = "Employee" });

        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
        var link = (await invited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("devAcceptUrl").GetString()!;
        var token = Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);

        using var newcomer = fixture.CreateClient();
        var accepted = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            newcomer, "/api/invitations/accept", new { token, displayName = "New Person", password = "Password123!" });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.True(await TenantApiHelpers.LoginAsync(newcomer, "pa-new@example.com", "Password123!"));

        // Inviting an address that already has an account is refused.
        var again = await platform.PostAsJsonAsync("/api/users/invite", new { email = "pa-new@example.com", role = "Employee" });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task The_key_creates_a_user_who_can_sign_in_at_once_and_a_tenant_admin_cannot()
    {
        await fixture.CreateUserAsync("pa-create-admin@example.com", "Password123!", "TenantAdmin");
        using var platform = PlatformClient();

        // A password the tenant's rules refuse creates nobody.
        var weak = await platform.PostAsJsonAsync(
            "/api/users", new { email = "pa-created@example.com", role = "Employee", password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        using var admin = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(admin, "pa-create-admin@example.com", "Password123!"));
        var byAdmin = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, "/api/users", new { email = "pa-created@example.com", role = "Employee", password = "AdminChosen12345" });
        Assert.Equal(HttpStatusCode.Forbidden, byAdmin.StatusCode);

        var created = await platform.PostAsJsonAsync(
            "/api/users", new { email = "pa-created@example.com", role = "Employee", password = "PlatformChosen123" });
        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);

        using var newcomer = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(newcomer, "pa-created@example.com", "PlatformChosen123"));

        var users = await platform.GetFromJsonAsync<JsonElement>("/api/users");
        var user = users.EnumerateArray().Single(u => u.GetProperty("email").GetString() == "pa-created@example.com");
        Assert.Equal("pa-created", user.GetProperty("displayName").GetString());
        Assert.Equal("Employee", user.GetProperty("roles")[0].GetString());

        // The same address a second time is refused.
        var again = await platform.PostAsJsonAsync(
            "/api/users", new { email = "pa-created@example.com", role = "Employee", password = "PlatformChosen123" });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task The_key_edits_a_users_name_and_sign_in_email()
    {
        var userId = await fixture.CreateUserAsync("pa-edit@example.com", "Password123!", "Employee");
        await fixture.CreateUserAsync("pa-taken@example.com", "Password123!", "Employee");
        using var platform = PlatformClient();

        var taken = await platform.PutAsJsonAsync($"/api/users/{userId}", new { displayName = "Edited", email = "pa-taken@example.com" });
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);

        var updated = await platform.PutAsJsonAsync($"/api/users/{userId}", new { displayName = "Edited Person", email = "pa-edited@example.com" });
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);

        var users = await platform.GetFromJsonAsync<JsonElement>("/api/users");
        var user = users.EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == userId);
        Assert.Equal("Edited Person", user.GetProperty("displayName").GetString());
        Assert.Equal("pa-edited@example.com", user.GetProperty("email").GetString());

        using var client = fixture.CreateClient();
        Assert.False(await TenantApiHelpers.LoginAsync(client, "pa-edit@example.com", "Password123!"));
        Assert.True(await TenantApiHelpers.LoginAsync(client, "pa-edited@example.com", "Password123!"));
    }

    [Fact]
    public async Task The_key_deletes_a_user_but_not_one_with_unfinished_tasks()
    {
        var busyId = await fixture.CreateUserAsync("pa-busy@example.com", "Password123!", "Employee");
        var idleId = await fixture.CreateUserAsync("pa-idle@example.com", "Password123!", "Employee");
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            db.WorkItems.Add(new Core.Business.WorkItem
            {
                Id = Guid.NewGuid(),
                Title = "Still open",
                Status = Core.Business.WorkItemStatus.Open,
                AssignedUserId = busyId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        using var platform = PlatformClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await platform.DeleteAsync($"/api/users/{busyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await platform.DeleteAsync($"/api/users/{idleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await platform.DeleteAsync($"/api/users/{idleId}")).StatusCode);

        using var client = fixture.CreateClient();
        Assert.False(await TenantApiHelpers.LoginAsync(client, "pa-idle@example.com", "Password123!"));
        Assert.True(await TenantApiHelpers.LoginAsync(client, "pa-busy@example.com", "Password123!"));

        using var check = fixture.Services.CreateScope();
        var audit = check.ServiceProvider.GetRequiredService<TenantDbContext>().AuditEntries;
        var entry = await audit.SingleAsync(a => a.Action == "UserDeleted" && a.Details!.StartsWith("user=pa-idle@example.com"));
        Assert.Equal("platform:ops@platform.test", entry.ActorEmail);
    }

    [Fact]
    public async Task The_key_sets_a_password_and_a_tenant_admin_cannot()
    {
        var userId = await fixture.CreateUserAsync("pa-pwd@example.com", "Password123!", "Employee");
        await fixture.CreateUserAsync("pa-pwd-admin@example.com", "Password123!", "TenantAdmin");
        using var platform = PlatformClient();

        // A password the tenant's rules refuse leaves the old one working.
        var weak = await platform.PostAsJsonAsync($"/api/users/{userId}/set-password", new { password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        using var session = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(session, "pa-pwd@example.com", "Password123!"));

        using var admin = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(admin, "pa-pwd-admin@example.com", "Password123!"));
        var byAdmin = await TenantApiHelpers.PostJsonWithAntiforgeryAsync(
            admin, $"/api/users/{userId}/set-password", new { password = "AdminChosen12345" });
        Assert.Equal(HttpStatusCode.Forbidden, byAdmin.StatusCode);

        var set = await platform.PostAsJsonAsync($"/api/users/{userId}/set-password", new { password = "PlatformChosen123" });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        // The session opened with the old password is ended.
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/auth/me")).StatusCode);
        using var client = fixture.CreateClient();
        Assert.False(await TenantApiHelpers.LoginAsync(client, "pa-pwd@example.com", "Password123!"));
        Assert.True(await TenantApiHelpers.LoginAsync(client, "pa-pwd@example.com", "PlatformChosen123"));

        using var scope = fixture.Services.CreateScope();
        var entry = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().AuditEntries
            .SingleAsync(a => a.Action == "UserPasswordSet");
        Assert.Equal("user=pa-pwd@example.com", entry.Details);
    }

    [Fact]
    public async Task A_wrong_key_is_refused()
    {
        var employeeId = await fixture.CreateUserAsync("pa-wrong@example.com", "Password123!", "Employee");
        using var intruder = PlatformClient(new string('0', 64));

        Assert.Equal(HttpStatusCode.Unauthorized, (await intruder.GetAsync("/api/users")).StatusCode);
        Assert.False((await intruder.PostAsync($"/api/users/{employeeId}/block", null)).IsSuccessStatusCode);

        using var employee = fixture.CreateClient();
        Assert.True(await TenantApiHelpers.LoginAsync(employee, "pa-wrong@example.com", "Password123!"));
    }

    [Theory]
    [InlineData("/api/products")]
    [InlineData("/api/orders")]
    [InlineData("/api/tasks")]
    [InlineData("/api/settings")]
    [InlineData("/api/audit")]
    [InlineData("/api/invitations")]
    [InlineData("/api/dashboard")]
    public async Task The_key_opens_nothing_but_user_management(string path)
    {
        using var platform = PlatformClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await platform.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task The_key_cannot_write_outside_user_management()
    {
        using var platform = PlatformClient();

        var response = await platform.PostAsJsonAsync("/api/products", new { sku = "PA-1", name = "Nope", price = 1, stockQuantity = 1 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_tenant_still_refuses_to_lose_its_last_active_admin()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        // Other tests in this class may have added admins; block all but one.
        var adminId = await fixture.CreateUserAsync("pa-last@example.com", "Password123!", "TenantAdmin");
        var otherAdmins = await (
            from user in db.Users
            join userRole in db.UserRoles on user.Id equals userRole.UserId
            join role in db.Roles on userRole.RoleId equals role.Id
            where role.Name == "TenantAdmin" && user.Id != adminId
            select user).ToListAsync();
        otherAdmins.ForEach(u => u.IsBlocked = true);
        await db.SaveChangesAsync();
        using var platform = PlatformClient();

        var response = await platform.PostAsync($"/api/users/{adminId}/block", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
