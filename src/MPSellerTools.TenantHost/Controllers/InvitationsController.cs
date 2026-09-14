using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Notifications;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;

namespace MPSellerTools.TenantHost.Controllers;

[ApiController]
[Route("api/invitations")]
public class InvitationsController(
    TenantDbContext db,
    UserManager<TenantUser> userManager,
    IDevOutbox outbox,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Create([FromBody] CreateInvitationRequest request)
    {
        if (request.Role is not (Roles.TenantAdmin or Roles.Employee))
        {
            return Problem("Role must be TenantAdmin or Employee.", statusCode: StatusCodes.Status400BadRequest);
        }

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var tokenHash = HashToken(rawToken);

        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            Role = request.Role,
            TokenHash = tokenHash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedByUserId = userManager.GetUserId(User) is { } id ? Guid.Parse(id) : Guid.Empty,
            CreatedAtUtc = DateTime.UtcNow,
        };

        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();

        var acceptLink = $"/accept-invitation?token={Uri.EscapeDataString(rawToken)}";
        await outbox.WriteAsync(request.Email, "You've been invited to MPSellerTools", $"Accept your invitation: {acceptLink}");

        // The caller (a TenantAdmin) is the authorized initiator of this
        // invitation, so returning the dev link directly here — rather than
        // through any general-purpose "view outbox" endpoint — keeps it
        // scoped to only the person who requested it (brief §7).
        var devAcceptUrl = environment.IsDevelopment() ? acceptLink : null;

        return Ok(new CreateInvitationResponse(invitation.Id, devAcceptUrl));
    }

    [HttpPost("accept")]
    [AllowAnonymous]
    public async Task<IActionResult> Accept([FromBody] AcceptInvitationRequest request)
    {
        var tokenHash = HashToken(request.Token);
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == tokenHash);

        if (invitation is null || invitation.AcceptedAtUtc is not null || invitation.ExpiresAtUtc < DateTime.UtcNow)
        {
            return Problem("Invalid or expired invitation.", statusCode: StatusCodes.Status400BadRequest);
        }

        var user = new TenantUser
        {
            Id = Guid.NewGuid(),
            UserName = invitation.Email,
            Email = invitation.Email,
            DisplayName = request.DisplayName,
            EmailConfirmed = true,
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return Problem(string.Join(" ", createResult.Errors.Select(e => e.Description)), statusCode: StatusCodes.Status400BadRequest);
        }

        await userManager.AddToRoleAsync(user, invitation.Role);

        invitation.AcceptedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return NoContent();
    }

    private static string HashToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
}
