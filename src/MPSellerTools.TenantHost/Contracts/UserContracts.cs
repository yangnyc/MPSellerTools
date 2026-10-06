namespace MPSellerTools.TenantHost.Contracts;

public record UserSummaryResponse(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles, bool IsBlocked);

public record ChangeRoleRequest(string Role);

public record UpdateUserRequest(string DisplayName, string Email);

public record SetPasswordRequest(string Password);

/// <summary><see cref="DisplayName"/> defaults to the part of the email before the @.</summary>
public record CreateUserRequest(string Email, string Role, string Password, string? DisplayName);

/// <summary>
/// <see cref="DevResetUrl"/> is only set in Development, where there is no
/// email delivery, and only for the TenantAdmin who forced the reset.
/// </summary>
public record ForcePasswordResetResponse(string? DevResetUrl);

public record PendingInvitationResponse(Guid Id, string Email, string Role, DateTime CreatedAtUtc, DateTime ExpiresAtUtc, bool IsExpired);
