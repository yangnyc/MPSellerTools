namespace MPSellerTools.TenantHost.Contracts;

public record LoginRequest(string Email, string Password);

public record CurrentUserResponse(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles, ThemeSettings? Theme);

/// <summary>
/// The per-user UI theme choices made in the settings panel. The theme name
/// and color values are the UI's own names (e.g. "ocean", "steel"), never raw
/// CSS. <see cref="ThemeName"/> is null on profiles saved before themes had
/// names; the UI treats that as its default theme.
/// </summary>
public record ThemeSettings(
    bool DarkMode,
    bool WhiteSidenav,
    string? SidenavTint,
    string SidenavColor,
    bool FixedNavbar,
    string? ThemeName = null)
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    public bool IsValid() =>
        IsSwatchName(SidenavColor)
        && (SidenavTint is null || IsSwatchName(SidenavTint))
        && (ThemeName is null || IsSwatchName(ThemeName));

    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this, JsonOptions);

    public static ThemeSettings? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var settings = System.Text.Json.JsonSerializer.Deserialize<ThemeSettings>(json, JsonOptions);
            return settings is not null && settings.IsValid() ? settings : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static bool IsSwatchName(string? value) =>
        value is { Length: > 0 and <= 32 } && value.All(c => c is >= 'a' and <= 'z');
}

public record AntiforgeryTokenResponse(string Token);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record UpdateProfileRequest(string DisplayName);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Email, string Token, string NewPassword);

public record CreateInvitationRequest(string Email, string Role);

public record CreateInvitationResponse(Guid InvitationId, string? DevAcceptUrl);

public record AcceptInvitationRequest(string Token, string DisplayName, string Password);
