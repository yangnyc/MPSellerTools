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
[Route("api/users")]
[Authorize(Policy = Roles.TenantAdmin)]
public class UsersController(
    TenantDbContext db,
    UserManager<TenantUser> userManager,
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
