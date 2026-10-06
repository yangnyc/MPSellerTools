namespace MPSellerTools.PlatformHost.Contracts;

/// <summary>
/// A user of one company, as that company's own instance reports it. Read
/// from the instance on request and never stored in the platform database.
/// </summary>
public record TenantUserResponse(
    Guid TenantId,
    string TenantName,
    string TenantSlug,
    Guid Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    bool IsBlocked);

/// <summary>A company whose users could not be read, and why.</summary>
public record UnavailableTenantResponse(Guid TenantId, string TenantName, string Reason);

public record TenantUsersResponse(
    IReadOnlyList<TenantUserResponse> Users,
    IReadOnlyList<UnavailableTenantResponse> Unavailable);

public record ChangeTenantUserRoleRequest(string Role);

public record InviteTenantUserRequest(string Email, string Role);

public record CreateTenantUserRequest(string Email, string Role, string Password);

public record UpdateTenantUserRequest(string DisplayName, string Email);

public record SetTenantUserPasswordRequest(string Password);

/// <summary><see cref="DevAcceptUrl"/> is only set in Development, where there is no email delivery.</summary>
public record TenantUserInviteResponse(string? DevAcceptUrl);

/// <summary><see cref="DevResetUrl"/> is only set in Development, where there is no email delivery.</summary>
public record TenantUserPasswordResetResponse(string? DevResetUrl);
