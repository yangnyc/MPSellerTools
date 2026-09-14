using MPSellerTools.Core.Platform;

namespace MPSellerTools.PlatformHost.Contracts;

public record CreateTenantRequest(string Name, string Slug, string InitialAdminEmail);

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
