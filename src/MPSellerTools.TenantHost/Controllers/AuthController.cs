using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MPSellerTools.Core.Notifications;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Contracts;

namespace MPSellerTools.TenantHost.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    SignInManager<TenantUser> signInManager,
    UserManager<TenantUser> userManager,
    IDevOutbox outbox) : ControllerBase
{
    private const string GenericLoginError = "Invalid email or password.";

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Problem(GenericLoginError, statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return Problem(GenericLoginError, statusCode: StatusCodes.Status401Unauthorized);
        }

        // Blocking must revoke access immediately, not just hide UI (brief §5) —
        // checked after password validation so the response stays generic either way.
        if (user.IsBlocked)
        {
            return Problem(GenericLoginError, statusCode: StatusCodes.Status401Unauthorized);
        }

        await signInManager.SignInAsync(user, isPersistent: true);
        return await Me();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (!(User.Identity?.IsAuthenticated ?? false))
        {
            return Unauthorized();
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null || user.IsBlocked)
        {
            return Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);
        return Ok(new CurrentUserResponse(user.Id, user.Email!, user.DisplayName, roles.ToList()));
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return Problem(string.Join(" ", result.Errors.Select(e => e.Description)), statusCode: StatusCodes.Status400BadRequest);
        }

        return NoContent();
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);

        // Always return 202 regardless of whether the account exists —
        // otherwise this endpoint enumerates registered emails (brief §7).
        if (user is not null && !user.IsBlocked)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var link = $"/reset-password?email={Uri.EscapeDataString(request.Email)}&token={Uri.EscapeDataString(token)}";
            await outbox.WriteAsync(request.Email, "Reset your MPSellerTools password", $"Reset link: {link}");
        }

        return Accepted();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Problem("Invalid or expired reset link.", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return Problem("Invalid or expired reset link.", statusCode: StatusCodes.Status400BadRequest);
        }

        return NoContent();
    }
}
