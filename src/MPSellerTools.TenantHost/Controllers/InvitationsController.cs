using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Controllers;

[ApiController]
[Route("api/invitations")]
public class InvitationsController(
    TenantDbContext db,
    UserManager<TenantUser> userManager,
    InvitationIssuer invitations,
    IWebHostEnvironment environment,
    Microsoft.Extensions.Options.IOptions<FeatureOptions> features,
    AuditLogger audit) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Create([FromBody] CreateInvitationRequest request)
    {
        if (!features.Value.InvitationsEnabled)
        {
            return Problem("Invitations are switched off. Add the user with a password instead.", statusCode: StatusCodes.Status409Conflict);
        }

        if (request.Role is not (Roles.TenantAdmin or Roles.Employee))
        {
            return Problem("Role must be TenantAdmin or Employee.", statusCode: StatusCodes.Status400BadRequest);
        }

        var (invitation, acceptLink) = await invitations.IssueAsync(request.Email, request.Role);

        // The caller (a TenantAdmin) is the authorized initiator of this
        // invitation, so returning the dev link directly here — rather than
        // through any general-purpose "view outbox" endpoint — keeps it
        // scoped to only the person who requested it (brief §7).
        var devAcceptUrl = environment.IsDevelopment() ? acceptLink : null;

        return Ok(new CreateInvitationResponse(invitation.Id, devAcceptUrl));
    }

    /// <summary>Invitations that have been sent but not accepted yet, newest first.</summary>
    [HttpGet]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> ListPending()
    {
        var now = DateTime.UtcNow;
        var pending = await db.Invitations
            .Where(i => i.AcceptedAtUtc == null)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Select(i => new PendingInvitationResponse(i.Id, i.Email, i.Role, i.CreatedAtUtc, i.ExpiresAtUtc, i.ExpiresAtUtc < now))
            .ToListAsync();
        return Ok(pending);
    }

    /// <summary>Revokes a pending invitation, so its link stops working.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Roles.TenantAdmin)]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var invitation = await db.Invitations.FindAsync(id);
        if (invitation is null)
        {
            return NotFound();
        }

        if (invitation.AcceptedAtUtc is not null)
        {
            return Problem("This invitation has already been accepted.", statusCode: StatusCodes.Status400BadRequest);
        }

        db.Invitations.Remove(invitation);
        audit.Log("InvitationRevoked", $"email={invitation.Email}; role={invitation.Role}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("accept")]
    [AllowAnonymous]
    public async Task<IActionResult> Accept([FromBody] AcceptInvitationRequest request)
    {
        if (!features.Value.InvitationsEnabled)
        {
            return Problem("Invitations are switched off, so this link no longer works. Ask your administrator for a password to sign in with.", statusCode: StatusCodes.Status409Conflict);
        }

        var tokenHash = InvitationIssuer.HashToken(request.Token);
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
}
