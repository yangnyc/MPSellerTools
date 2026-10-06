namespace MPSellerTools.TenantHost.Contracts;

public record CompanySettingsResponse(string CompanyName, int? LowStockThreshold, Guid? LowStockAssigneeId, string RowVersion);

/// <summary>Low-stock alerts are on when both the threshold and the assignee are given, and off when both are null.</summary>
public record UpdateCompanySettingsRequest(string CompanyName, int? LowStockThreshold, Guid? LowStockAssigneeId, string RowVersion);
