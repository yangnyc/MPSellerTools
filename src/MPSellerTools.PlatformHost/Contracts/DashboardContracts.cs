using MPSellerTools.Core.Platform;

namespace MPSellerTools.PlatformHost.Contracts;

public record TenantStatusCounts(int Provisioning, int Active, int Suspended, int Failed);

public record RecentJobSummary(Guid Id, string TenantName, ProvisioningJobType JobType, ProvisioningJobStatus Status, DateTime UpdatedAtUtc);

public record PlatformDashboardResponse(TenantStatusCounts TenantCounts, IReadOnlyList<RecentJobSummary> RecentJobs);
