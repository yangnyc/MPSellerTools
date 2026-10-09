using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Business;
using MPSellerTools.Core.Notifications;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;
using MPSellerTools.TenantHost.Services;

namespace MPSellerTools.TenantHost.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = PlatformAccess.UserManagementPolicy)]
public class UsersController(
    TenantDbContext db,
    UserManager<TenantUser> userManager,
    IDevOutbox outbox,
    InvitationIssuer invitations,
    IWebHostEnvironment environment,
    Microsoft.Extensions.Options.IOptions<FeatureOptions> features,
    AuditLogger audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var users = await db.Users.OrderBy(u => u.Email).ToListAsync();
        var result = new List<UserSummaryResponse>();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            result.Add(new UserSummaryResponse(user.Id, user.Email!, user.DisplayName, roles.ToList(), user.IsBlocked));
        }
        return Ok(result);
    }

    /// <summary>
    /// Adds a user by invitation: they receive a single-use link and set
    /// their own password, so nobody else ever knows it.
    /// </summary>
    [HttpPost("invite")]
    public async Task<IActionResult> Invite([FromBody] CreateInvitationRequest request)
    {
        if (!features.Value.InvitationsEnabled)
        {
            return Problem("Invitations are switched off. Add the user with a password instead.", statusCode: StatusCodes.Status409Conflict);
        }

        if (request.Role is not (Roles.TenantAdmin or Roles.Employee))
        {
            return Problem("Role must be TenantAdmin or Employee.", statusCode: StatusCodes.Status400BadRequest);
        }

        var email = request.Email?.Trim() ?? "";
        if (!IsValidEmail(email))
        {
            return Problem("Enter a valid email address.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Problem("A user with that email already exists.", statusCode: StatusCodes.Status400BadRequest);
        }

        var (invitation, acceptLink) = await invitations.IssueAsync(email, request.Role);
        return Ok(new CreateInvitationResponse(invitation.Id, environment.IsDevelopment() ? acceptLink : null));
    }

    /// <summary>
    /// Adds a user straight away, with a password that is chosen for them and
    /// passed on, instead of an invitation. While invitations are on this is
    /// the platform console's alone, and a TenantAdmin invites; with them off
    /// it is how a TenantAdmin adds users too. The password is never logged.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        if (features.Value.InvitationsEnabled && !User.IsInRole(Roles.PlatformOperator))
        {
            return Problem("Only the platform console can create a user with a password.", statusCode: StatusCodes.Status403Forbidden);
        }

        if (request.Role is not (Roles.TenantAdmin or Roles.Employee))
        {
            return Problem("Role must be TenantAdmin or Employee.", statusCode: StatusCodes.Status400BadRequest);
        }

        var email = request.Email?.Trim() ?? "";
        if (!IsValidEmail(email))
        {
            return Problem("Enter a valid email address.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Problem("A user with that email already exists.", statusCode: StatusCodes.Status400BadRequest);
        }

        var displayName = request.DisplayName?.Trim() is { Length: > 0 } name ? name : email[..email.IndexOf('@')];
        if (displayName.Length > 256)
        {
            return Problem("Display name must be between 1 and 256 characters.", statusCode: StatusCodes.Status400BadRequest);
        }

        var user = new TenantUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            DisplayName = displayName,
            EmailConfirmed = true,
        };

        var result = await userManager.CreateAsync(user, request.Password ?? "");
        if (!result.Succeeded)
        {
            return Problem(string.Join(" ", result.Errors.Select(e => e.Description)), statusCode: StatusCodes.Status400BadRequest);
        }

        await userManager.AddToRoleAsync(user, request.Role);

        audit.Log("UserCreated", $"user={email}; role={request.Role}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request)
    {
        var displayName = request.DisplayName?.Trim() ?? "";
        if (displayName.Length is 0 or > 256)
        {
            return Problem("Display name must be between 1 and 256 characters.", statusCode: StatusCodes.Status400BadRequest);
        }

        var email = request.Email?.Trim() ?? "";
        if (!IsValidEmail(email))
        {
            return Problem("Enter a valid email address.", statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        if (await userManager.FindByEmailAsync(email) is { } owner && owner.Id != user.Id)
        {
            return Problem("Another user already has that email.", statusCode: StatusCodes.Status400BadRequest);
        }

        var previousEmail = user.Email;
        user.DisplayName = displayName;
        // The email is also the sign-in name.
        user.Email = email;
        user.UserName = email;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Problem(string.Join(" ", result.Errors.Select(e => e.Description)), statusCode: StatusCodes.Status400BadRequest);
        }

        audit.Log("UserUpdated", $"user={previousEmail}; name={displayName}; email={email}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Removes the account for good. Finished tasks and orders keep the
    /// deleted user's id as history; work still in progress must not be left
    /// pointing at nobody.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        if (userManager.GetUserId(User) == user.Id.ToString())
        {
            return Problem("You cannot delete your own account.", statusCode: StatusCodes.Status400BadRequest);
        }

        var roles = await userManager.GetRolesAsync(user);
        if (roles.Contains(Roles.TenantAdmin) && await IsLastActiveTenantAdminAsync(user.Id))
        {
            return Problem(
                "This is the company's only active admin. Make another user a company admin first, then delete this one.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var unfinishedTasks = await db.WorkItems.CountAsync(w =>
            w.AssignedUserId == user.Id && (w.Status == WorkItemStatus.Open || w.Status == WorkItemStatus.InProgress));
        if (unfinishedTasks > 0)
        {
            return Problem(
                $"Reassign or close this user's unfinished tasks first ({unfinishedTasks}).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var unfinishedOrders = await db.Orders
            .Where(o => o.AssignedUserId == user.Id && (o.Status == OrderStatus.New || o.Status == OrderStatus.InProgress))
            .ToListAsync();
        foreach (var order in unfinishedOrders)
        {
            order.AssignedUserId = null;
            order.UpdatedAtUtc = DateTime.UtcNow;
        }

        // Saved together with the deletion below, which writes this context.
        audit.Log("UserDeleted", $"user={user.Email}; unassignedOrders={unfinishedOrders.Count}");
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return Problem(string.Join(" ", result.Errors.Select(e => e.Description)), statusCode: StatusCodes.Status400BadRequest);
        }

        return NoContent();
    }

    [HttpPut("{id:guid}/role")]
    public async Task<IActionResult> ChangeRole(Guid id, [FromBody] ChangeRoleRequest request)
    {
        if (request.Role is not (Roles.TenantAdmin or Roles.Employee))
        {
            return Problem("Role must be TenantAdmin or Employee.", statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        if (currentRoles.Contains(Roles.TenantAdmin) && request.Role != Roles.TenantAdmin)
        {
            if (await IsLastActiveTenantAdminAsync(user.Id))
            {
                return Problem("Cannot demote the last active TenantAdmin.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        await userManager.RemoveFromRolesAsync(user, currentRoles);
        await userManager.AddToRoleAsync(user, request.Role);
        await userManager.UpdateSecurityStampAsync(user); // forces re-login with the new role (brief §5)

        audit.Log("UserRoleChanged", $"user={user.Email}; role={request.Role}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/block")]
    public async Task<IActionResult> Block(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        var roles = await userManager.GetRolesAsync(user);
        if (roles.Contains(Roles.TenantAdmin) && await IsLastActiveTenantAdminAsync(user.Id))
        {
            return Problem("Cannot block the last active TenantAdmin.", statusCode: StatusCodes.Status400BadRequest);
        }

        user.IsBlocked = true;
        await userManager.UpdateAsync(user);
        await userManager.UpdateSecurityStampAsync(user);

        audit.Log("UserBlocked", $"user={user.Email}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/unblock")]
    public async Task<IActionResult> Unblock(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        user.IsBlocked = false;
        await userManager.UpdateAsync(user);

        audit.Log("UserUnblocked", $"user={user.Email}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Ends every session the user has open; they can sign in again straight away.</summary>
    [HttpPost("{id:guid}/sign-out")]
    public async Task<IActionResult> SignOutEverywhere(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        await userManager.UpdateSecurityStampAsync(user);
        audit.Log("UserSignedOut", $"user={user.Email}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Removes the user's password and signs them out, so the only way back
    /// in is the single-use reset link sent to their email.
    /// </summary>
    [HttpPost("{id:guid}/force-password-reset")]
    public async Task<IActionResult> ForcePasswordReset(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        if (userManager.GetUserId(User) == user.Id.ToString())
        {
            return Problem("Change your own password from your profile instead.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (user.IsBlocked)
        {
            return Problem("Unblock the user before resetting their password.", statusCode: StatusCodes.Status400BadRequest);
        }

        // Also replaces the security stamp, which ends the user's sessions.
        var removed = await userManager.RemovePasswordAsync(user);
        if (!removed.Succeeded)
        {
            return Problem(string.Join(" ", removed.Errors.Select(e => e.Description)), statusCode: StatusCodes.Status400BadRequest);
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = $"/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
        await outbox.WriteAsync(user.Email!, "Reset your MPSellerTools password", $"An administrator has reset your password. Set a new one: {link}");

        audit.Log("UserPasswordResetForced", $"user={user.Email}");
        await db.SaveChangesAsync();
        return Ok(new ForcePasswordResetResponse(environment.IsDevelopment() ? link : null));
    }

    /// <summary>
    /// Replaces the user's password with one the platform administrator chose
    /// and passes on, and ends the user's sessions. Platform console only: a
    /// TenantAdmin sends a reset link instead. The password is never logged.
    /// </summary>
    [HttpPost("{id:guid}/set-password")]
    public async Task<IActionResult> SetPassword(Guid id, [FromBody] SetPasswordRequest request)
    {
        if (!User.IsInRole(Roles.PlatformOperator))
        {
            return Problem("Only the platform console can set a user's password.", statusCode: StatusCodes.Status403Forbidden);
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        // Checked before anything is changed, so a refused password leaves the old one working.
        var password = request.Password ?? "";
        var errors = new List<string>();
        foreach (var validator in userManager.PasswordValidators)
        {
            var validation = await validator.ValidateAsync(userManager, user, password);
            errors.AddRange(validation.Errors.Select(e => e.Description));
        }
        if (errors.Count > 0)
        {
            return Problem(string.Join(" ", errors), statusCode: StatusCodes.Status400BadRequest);
        }

        user.PasswordHash = userManager.PasswordHasher.HashPassword(user, password);
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Problem(string.Join(" ", result.Errors.Select(e => e.Description)), statusCode: StatusCodes.Status400BadRequest);
        }
        await userManager.UpdateSecurityStampAsync(user);

        audit.Log("UserPasswordSet", $"user={user.Email}");
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static bool IsValidEmail(string email) =>
        email.Length is > 0 and <= 256 && new EmailAddressAttribute().IsValid(email);

    private async Task<bool> IsLastActiveTenantAdminAsync(Guid excludingUserId)
    {
        var admins = await (
            from user in db.Users
            join userRole in db.UserRoles on user.Id equals userRole.UserId
            join role in db.Roles on userRole.RoleId equals role.Id
            where role.Name == Roles.TenantAdmin && !user.IsBlocked
            select user.Id
        ).ToListAsync();

        return admins.Count <= 1 && admins.Contains(excludingUserId);
    }
}
