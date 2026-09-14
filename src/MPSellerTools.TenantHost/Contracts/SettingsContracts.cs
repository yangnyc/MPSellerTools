namespace MPSellerTools.TenantHost.Contracts;

public record CompanySettingsResponse(string CompanyName, string RowVersion);

public record UpdateCompanySettingsRequest(string CompanyName, string RowVersion);
