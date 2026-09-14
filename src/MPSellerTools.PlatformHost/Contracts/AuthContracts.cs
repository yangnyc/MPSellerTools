namespace MPSellerTools.PlatformHost.Contracts;

public record LoginRequest(string Email, string Password);

public record CurrentUserResponse(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles);

public record AntiforgeryTokenResponse(string Token);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
