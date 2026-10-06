using MPSellerTools.Core.Platform;

namespace MPSellerTools.PlatformHost.Contracts;

public record CreateTenantRequest(string Name, string Slug, string InitialAdminEmail);

public record UpdateTenantRequest(string Name);

public record CreateTenantResponse(Guid TenantId, Guid JobId);

public record TenantSummaryResponse(
    Guid Id,
    string Name,
    string Slug,
    TenantStatus Status,
    string? Url,
    DateTime CreatedAtUtc);

public record TenantDetailResponse(
    Guid Id,
    string Name,
    string Slug,
    TenantStatus Status,
    string? Url,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>
/// Where and how a tenant's instance is running, for the console's tenant
/// controls. Names and ids only — never a connection string or credential.
/// </summary>
public record TenantRuntimeResponse(
    Guid Id,
    string Name,
    string Slug,
    TenantStatus Status,
    string? Url,
    int? Port,
    string? DatabaseName,
    Guid? ApplicationInstanceId,
    int? ProcessId,
    DateTime? ProcessStartTimeUtc,
    DateTime UpdatedAtUtc);

public record ProvisioningJobResponse(
    Guid Id,
    Guid TenantId,
    string TenantName,
    ProvisioningJobType JobType,
    ProvisioningJobStatus Status,
    int Attempts,
    string? LastError,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
