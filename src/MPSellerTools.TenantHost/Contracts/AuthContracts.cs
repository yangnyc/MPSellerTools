namespace MPSellerTools.TenantHost.Contracts;

public record LoginRequest(string Email, string Password);

public record CurrentUserResponse(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles);

public record AntiforgeryTokenResponse(string Token);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Email, string Token, string NewPassword);

public record CreateInvitationRequest(string Email, string Role);

public record CreateInvitationResponse(Guid InvitationId, string? DevAcceptUrl);

public record AcceptInvitationRequest(string Token, string DisplayName, string Password);
