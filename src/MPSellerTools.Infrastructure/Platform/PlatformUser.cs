using Microsoft.AspNetCore.Identity;

namespace MPSellerTools.Infrastructure.Platform;

/// <summary>
/// A platform administrator account. Lives only in the platform database —
/// never in a tenant database (brief §4).
/// </summary>
public class PlatformUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }
}
