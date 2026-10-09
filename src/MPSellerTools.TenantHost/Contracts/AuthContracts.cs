namespace MPSellerTools.TenantHost.Contracts;

public record LoginRequest(string Email, string Password);

public record CurrentUserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    ThemeSettings? Theme,
    IReadOnlyList<string> PinnedMenus,
    // Whether users are added by invitation; off, they are added with a password.
    bool InvitationsEnabled = false);

/// <summary>The sidebar menu groups the user keeps pinned open, by the UI's own group keys (e.g. "tenants").</summary>
public record PinnedMenusRequest(IReadOnlyList<string>? Menus)
{
    private const int MaxPinnedMenus = 8;

    public bool IsValid() =>
        Menus is { Count: <= MaxPinnedMenus } && Menus.All(ThemeSettings.IsPlainName) && Menus.Distinct().Count() == Menus.Count;
}

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
        IsPlainName(SidenavColor)
        && (SidenavTint is null || IsPlainName(SidenavTint))
        && (ThemeName is null || IsPlainName(ThemeName));

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

    internal static bool IsPlainName(string? value) =>
        value is { Length: > 0 and <= 32 } && value.All(c => c is >= 'a' and <= 'z');
}

/// <summary>
/// What a user's <c>ThemeSettingsJson</c> holds: the settings in use
/// (<see cref="Active"/>) plus the settings last saved for each look, a look
/// being a named theme in light or in dark mode. Switching theme or mode
/// therefore restores the colours the user left there. <see cref="LastDark"/>
/// remembers which mode each theme was last used in, and
/// <see cref="PinnedMenus"/> the sidebar groups the user keeps pinned open. A
/// profile saved before this existed holds a bare <see cref="ThemeSettings"/>,
/// which is read as the active settings.
/// </summary>
public record ThemeProfile(
    ThemeSettings? Active,
    IReadOnlyDictionary<string, ThemeSettings>? Saved = null,
    IReadOnlyDictionary<string, bool>? LastDark = null,
    IReadOnlyList<string>? PinnedMenus = null)
{
    // The ThemeSettingsJson column's length.
    private const int MaxJsonLength = 1024;

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static string LookKey(string themeName, bool darkMode) => $"{themeName}:{(darkMode ? "dark" : "light")}";

    /// <summary>
    /// The settings last saved for a named theme: in the given mode, or, when
    /// no mode is given, in the mode the theme was last used in. Null if the
    /// user has never used that look.
    /// </summary>
    public ThemeSettings? Find(string themeName, bool? darkMode = null)
    {
        var saved = Saved ?? new Dictionary<string, ThemeSettings>();
        var activeForTheme = Active?.ThemeName == themeName ? Active : null;

        if (darkMode is { } mode)
        {
            return saved.TryGetValue(LookKey(themeName, mode), out var look)
                ? look
                : activeForTheme?.DarkMode == mode ? activeForTheme : null;
        }

        if (LastDark is not null && LastDark.TryGetValue(themeName, out var lastDark)
            && saved.TryGetValue(LookKey(themeName, lastDark), out var last))
        {
            return last;
        }

        return activeForTheme
            ?? saved.GetValueOrDefault(LookKey(themeName, false))
            ?? saved.GetValueOrDefault(LookKey(themeName, true));
    }

    /// <summary>Makes these the settings in use, and the ones remembered for their look.</summary>
    public ThemeProfile With(ThemeSettings settings)
    {
        var saved = new Dictionary<string, ThemeSettings>(Saved ?? new Dictionary<string, ThemeSettings>());
        var lastDark = new Dictionary<string, bool>(LastDark ?? new Dictionary<string, bool>());

        // The settings in use before this were never filed under their look
        // on a profile saved before per-look settings existed.
        if (Active?.ThemeName is { } previousName)
        {
            saved.TryAdd(LookKey(previousName, Active.DarkMode), Active);
            lastDark.TryAdd(previousName, Active.DarkMode);
        }

        if (settings.ThemeName is { } themeName)
        {
            saved[LookKey(themeName, settings.DarkMode)] = settings;
            lastDark[themeName] = settings.DarkMode;
        }

        return new ThemeProfile(settings, saved, lastDark, PinnedMenus);
    }

    public string ToJson()
    {
        var saved = new Dictionary<string, ThemeSettings>(Saved ?? new Dictionary<string, ThemeSettings>());
        var json = System.Text.Json.JsonSerializer.Serialize(new ThemeProfile(Active, saved, LastDark, PinnedMenus), JsonOptions);

        // Forget other themes' looks rather than overflow the column.
        foreach (var key in saved.Keys.Where(key => saved[key].ThemeName != Active?.ThemeName).ToList())
        {
            if (json.Length <= MaxJsonLength)
            {
                break;
            }

            saved.Remove(key);
            json = System.Text.Json.JsonSerializer.Serialize(new ThemeProfile(Active, saved, LastDark, PinnedMenus), JsonOptions);
        }

        return json;
    }

    public static ThemeProfile FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ThemeProfile(Active: null);
        }

        try
        {
            var profile = System.Text.Json.JsonSerializer.Deserialize<ThemeProfile>(json, JsonOptions);
            if (profile is null || (profile.Active is null && profile.Saved is null && profile.PinnedMenus is null))
            {
                return new ThemeProfile(ThemeSettings.FromJson(json));
            }

            var saved = new Dictionary<string, ThemeSettings>();
            var lastDark = new Dictionary<string, bool>(profile.LastDark ?? new Dictionary<string, bool>());
            foreach (var (key, look) in profile.Saved ?? new Dictionary<string, ThemeSettings>())
            {
                if (look?.ThemeName is not { } themeName || !look.IsValid())
                {
                    continue;
                }

                // Keyed by theme name alone before light and dark were kept apart.
                if (key == themeName)
                {
                    saved.TryAdd(LookKey(themeName, look.DarkMode), look);
                    lastDark.TryAdd(themeName, look.DarkMode);
                }
                else if (key == LookKey(themeName, look.DarkMode))
                {
                    saved[key] = look;
                }
            }

var pinnedMenus = (profile.PinnedMenus ?? []).Where(ThemeSettings.IsPlainName).Distinct().ToList();
            return new ThemeProfile(profile.Active is { } active && active.IsValid() ? active : null, saved, lastDark, pinnedMenus);
        }
        catch (System.Text.Json.JsonException)
        {
            return new ThemeProfile(Active: null);
        }
    }
}

public record AntiforgeryTokenResponse(string Token);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record UpdateProfileRequest(string DisplayName);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Email, string Token, string NewPassword);

public record CreateInvitationRequest(string Email, string Role);

public record CreateInvitationResponse(Guid InvitationId, string? DevAcceptUrl);

public record AcceptInvitationRequest(string Token, string DisplayName, string Password);
