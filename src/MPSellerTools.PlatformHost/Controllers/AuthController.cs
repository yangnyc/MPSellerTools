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
        var profile = ThemeProfile.FromJson(user.ThemeSettingsJson);
        return Ok(new CurrentUserResponse(user.Id, user.Email!, user.DisplayName, roles.ToList(), profile.Active, profile.PinnedMenus ?? []));
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var displayName = request.DisplayName.Trim();
        if (displayName.Length is 0 or > 256)
        {
            return Problem("Display name must be between 1 and 256 characters.", statusCode: StatusCodes.Status400BadRequest);
        }

        user.DisplayName = displayName;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Problem(string.Join(" ", result.Errors.Select(e => e.Description)), statusCode: StatusCodes.Status400BadRequest);
        }

        return await Me();
    }

    [HttpPut("me/theme")]
    public async Task<IActionResult> UpdateTheme([FromBody] ThemeSettings request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        if (!request.IsValid())
        {
            return Problem("Invalid theme settings.", statusCode: StatusCodes.Status400BadRequest);
        }

        user.ThemeSettingsJson = ThemeProfile.FromJson(user.ThemeSettingsJson).With(request).ToJson();
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Problem(string.Join(" ", result.Errors.Select(e => e.Description)), statusCode: StatusCodes.Status400BadRequest);
        }

        return await Me();
    }

    /// <summary>
    /// The settings last saved for a named theme, read when the user switches
    /// to it: in light or dark mode when <paramref name="dark"/> says which, else
    /// in the mode the theme was last used in. 204 if never used that way.
    /// </summary>
    [HttpGet("me/theme/{themeName}")]
    public async Task<IActionResult> GetSavedTheme(string themeName, [FromQuery] bool? dark = null)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var saved = ThemeProfile.FromJson(user.ThemeSettingsJson).Find(themeName, dark);
        return saved is null ? NoContent() : Ok(saved);
    }

    /// <summary>Saves which sidebar menu groups the user keeps pinned open.</summary>
    [HttpPut("me/pinned-menus")]
    public async Task<IActionResult> UpdatePinnedMenus([FromBody] PinnedMenusRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        if (!request.IsValid())
        {
            return Problem("Invalid menu names.", statusCode: StatusCodes.Status400BadRequest);
        }

        user.ThemeSettingsJson = (ThemeProfile.FromJson(user.ThemeSettingsJson) with { PinnedMenus = request.Menus }).ToJson();
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return Problem(string.Join(" ", result.Errors.Select(e => e.Description)), statusCode: StatusCodes.Status400BadRequest);
        }

        return await Me();
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
