using Microsoft.AspNetCore.Identity;

namespace MPSellerTools.Infrastructure.Tenants;

/// <summary>
/// A user within one company's tenant database. The same email address may
/// exist independently as a different TenantUser row in a different tenant's
/// database (brief §4) — there is no cross-tenant identity linkage.
/// </summary>
public class TenantUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }

    /// <summary>
    /// Blocking revokes access immediately (sessions are checked against this
    /// on every request, not just at login) — brief §5.
    /// </summary>
    public bool IsBlocked { get; set; }
}
