using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MPSellerTools.Infrastructure.Platform;
using MPSellerTools.PlatformHost.Contracts;

namespace MPSellerTools.PlatformHost.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    SignInManager<PlatformUser> signInManager,
    UserManager<PlatformUser> userManager) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);

        // Deliberately generic: do not reveal whether the email is registered,
        // whether the password was wrong, or whether the account is locked
        // out (brief §7 — errors must not enumerate registered emails).
        const string genericError = "Invalid email or password.";

        if (user is null)
        {
            return Problem(genericError, statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await signInManager.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return Problem(genericError, statusCode: StatusCodes.Status401Unauthorized);
        }

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
        if (user is null)
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
}
