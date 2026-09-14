namespace MPSellerTools.TenantHost.Contracts;

public record UserSummaryResponse(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles, bool IsBlocked);

public record ChangeRoleRequest(string Role);
