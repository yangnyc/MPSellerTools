using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Platform;
using MPSellerTools.Infrastructure.Platform;
using MPSellerTools.PlatformHost.Contracts;
using MPSellerTools.PlatformHost.Services;

namespace MPSellerTools.PlatformHost.Controllers;

/// <summary>
/// The users of every company, for the platform console. Each request is
/// passed to the company's own instance, which applies its own rules (it
/// still refuses to block or demote its last active admin) and writes its
/// own audit entry; what the platform administrator did is also recorded in
/// the platform audit log.
/// </summary>
[ApiController]
[Route("api/tenant-users")]
[Authorize(Policy = Core.Tenancy.Roles.PlatformAdmin)]
public class TenantUsersController(
    PlatformDbContext db,
    UserManager<PlatformUser> userManager,
    TenantUsersClient tenantUsers,
    Microsoft.Extensions.Options.IOptions<MPSellerTools.Core.Tenancy.FeatureOptions> features) : ControllerBase
{
    private string ActorEmail => User.Identity?.Name ?? "unknown";

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken);
        var results = await Task.WhenAll(tenants.Select(tenant => ListForTenantAsync(tenant, cancellationToken)));

        return Ok(new TenantUsersResponse(
            results.SelectMany(r => r.Users).ToList(),
            results.Where(r => r.Unavailable is not null).Select(r => r.Unavailable!).ToList()));
    }

    /// <summary>Adds a user to a company by invitation; they set their own password from the link.</summary>
    [HttpPost("{tenantId:guid}/invite")]
    public Task<IActionResult> Invite(Guid tenantId, [FromBody] InviteTenantUserRequest request, CancellationToken cancellationToken) =>
        !features.Value.InvitationsEnabled
            ? Task.FromResult<IActionResult>(Problem("Invitations are switched off. Add the user with a password instead.", statusCode: StatusCodes.Status409Conflict))
            : ForwardAsync(tenantId, HttpMethod.Post, "/api/users/invite", new { request.Email, request.Role },
            "TenantUserInvited", $"email={request.Email}; role={request.Role}", cancellationToken);

    /// <summary>Adds a user to a company straight away, with a password the administrator passes on. It is not recorded anywhere.</summary>
    [HttpPost("{tenantId:guid}/create")]
    public Task<IActionResult> Create(Guid tenantId, [FromBody] CreateTenantUserRequest request, CancellationToken cancellationToken) =>
        ForwardAsync(tenantId, HttpMethod.Post, "/api/users", new { request.Email, request.Role, request.Password },
            "TenantUserCreated", $"email={request.Email}; role={request.Role}", cancellationToken);

    [HttpPut("{tenantId:guid}/{userId:guid}")]
    public Task<IActionResult> Update(Guid tenantId, Guid userId, [FromBody] UpdateTenantUserRequest request, CancellationToken cancellationToken) =>
        ForwardAsync(tenantId, HttpMethod.Put, $"/api/users/{userId}", new { request.DisplayName, request.Email },
            "TenantUserUpdated", $"userId={userId}; name={request.DisplayName}; email={request.Email}", cancellationToken);

    [HttpDelete("{tenantId:guid}/{userId:guid}")]
    public Task<IActionResult> Delete(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        ForwardAsync(tenantId, HttpMethod.Delete, $"/api/users/{userId}", null, "TenantUserDeleted", $"userId={userId}", cancellationToken);

    [HttpPut("{tenantId:guid}/{userId:guid}/role")]
    public Task<IActionResult> ChangeRole(Guid tenantId, Guid userId, [FromBody] ChangeTenantUserRoleRequest request, CancellationToken cancellationToken) =>
        ForwardAsync(tenantId, HttpMethod.Put, $"/api/users/{userId}/role", new { request.Role },
            "TenantUserRoleChanged", $"userId={userId}; role={request.Role}", cancellationToken);

    [HttpPost("{tenantId:guid}/{userId:guid}/block")]
    public Task<IActionResult> Block(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        ForwardAsync(tenantId, HttpMethod.Post, $"/api/users/{userId}/block", null, "TenantUserBlocked", $"userId={userId}", cancellationToken);

    [HttpPost("{tenantId:guid}/{userId:guid}/unblock")]
    public Task<IActionResult> Unblock(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        ForwardAsync(tenantId, HttpMethod.Post, $"/api/users/{userId}/unblock", null, "TenantUserUnblocked", $"userId={userId}", cancellationToken);

    [HttpPost("{tenantId:guid}/{userId:guid}/sign-out")]
    public Task<IActionResult> SignOutEverywhere(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        ForwardAsync(tenantId, HttpMethod.Post, $"/api/users/{userId}/sign-out", null, "TenantUserSignedOut", $"userId={userId}", cancellationToken);

    [HttpPost("{tenantId:guid}/{userId:guid}/force-password-reset")]
    public Task<IActionResult> ForcePasswordReset(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        ForwardAsync(tenantId, HttpMethod.Post, $"/api/users/{userId}/force-password-reset", null,
            "TenantUserPasswordResetForced", $"userId={userId}", cancellationToken);

    /// <summary>Sets a password the administrator passes on to the user. It is not recorded anywhere.</summary>
    [HttpPost("{tenantId:guid}/{userId:guid}/set-password")]
    public Task<IActionResult> SetPassword(Guid tenantId, Guid userId, [FromBody] SetTenantUserPasswordRequest request, CancellationToken cancellationToken) =>
        ForwardAsync(tenantId, HttpMethod.Post, $"/api/users/{userId}/set-password", new { request.Password },
            "TenantUserPasswordSet", $"userId={userId}", cancellationToken);

    private async Task<(List<TenantUserResponse> Users, UnavailableTenantResponse? Unavailable)> ListForTenantAsync(
        Tenant tenant, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await tenantUsers.SendAsync(tenant, HttpMethod.Get, "/api/users", null, ActorEmail, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ([], new UnavailableTenantResponse(tenant.Id, tenant.Name, $"Returned an error ({(int)response.StatusCode})"));
            }

            var users = await response.Content.ReadFromJsonAsync<List<TenantUser>>(cancellationToken) ?? [];
            return (users
                .Select(u => new TenantUserResponse(tenant.Id, tenant.Name, tenant.Slug, u.Id, u.Email, u.DisplayName, u.Roles, u.IsBlocked))
                .ToList(), null);
        }
        catch (TenantUnavailableException ex)
        {
            return ([], new UnavailableTenantResponse(tenant.Id, tenant.Name, ex.Message));
        }
        catch (JsonException)
        {
            return ([], new UnavailableTenantResponse(tenant.Id, tenant.Name, "Returned an unreadable answer"));
        }
    }

    private async Task<IActionResult> ForwardAsync(
        Guid tenantId, HttpMethod method, string path, object? body,
        string auditAction, string auditDetails, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FindAsync([tenantId], cancellationToken);
        if (tenant is null)
        {
            return NotFound();
        }

        HttpResponseMessage response;
        try
        {
            response = await tenantUsers.SendAsync(tenant, method, path, body, ActorEmail, cancellationToken);
        }
        catch (TenantUnavailableException ex)
        {
            return Problem($"{tenant.Name} could not be reached: {ex.Message}.", statusCode: StatusCodes.Status502BadGateway);
        }

        using (response)
        {
            var payload = await ReadJsonAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // The company's own refusal ("Cannot block the last active TenantAdmin."), passed on as it is.
                var reason = payload is { ValueKind: JsonValueKind.Object } problem && problem.TryGetProperty("detail", out var detail)
                    ? detail.GetString()
                    : null;
                return Problem(reason ?? "The company's instance refused the request.", statusCode: (int)response.StatusCode);
            }

            db.AuditEntries.Add(new PlatformAuditEntry
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = DateTime.UtcNow,
                ActorUserId = userManager.GetUserId(User) is { } id ? Guid.Parse(id) : Guid.Empty,
                ActorEmail = ActorEmail,
                Action = auditAction,
                TenantId = tenant.Id,
                Details = auditDetails,
            });
            await db.SaveChangesAsync(cancellationToken);

            // The instance answers with a path on itself; make it a link that works from here.
            if (payload is { ValueKind: JsonValueKind.Object } result)
            {
                if (result.TryGetProperty("devResetUrl", out var resetLink))
                {
                    return Ok(new TenantUserPasswordResetResponse(resetLink.GetString() is { } resetPath ? $"{tenant.Url}{resetPath}" : null));
                }

                if (result.TryGetProperty("devAcceptUrl", out var acceptLink))
                {
                    return Ok(new TenantUserInviteResponse(acceptLink.GetString() is { } acceptPath ? $"{tenant.Url}{acceptPath}" : null));
                }
            }

            return NoContent();
        }
    }

    private static async Task<JsonElement?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private record TenantUser(Guid Id, string Email, string DisplayName, List<string> Roles, bool IsBlocked);
}
