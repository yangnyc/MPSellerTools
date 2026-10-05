using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace MPSellerTools.Infrastructure.Platform;

/// <summary>
/// A platform administrator account. Lives only in the platform database —
/// never in a tenant database (brief §4).
/// </summary>
public class PlatformUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }

    /// <summary>
    /// The user's saved UI theme choices (dark mode, sidenav colors, ...) as a
    /// small JSON document, so they follow the account across browsers. Null
    /// until the user first changes a theme setting.
    /// </summary>
    [MaxLength(1024)]
    public string? ThemeSettingsJson { get; set; }
}
