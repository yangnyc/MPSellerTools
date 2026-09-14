// Mirrors the response/request DTOs in MPSellerTools.PlatformHost.Contracts.

export type TenantStatus = 0 | 1 | 2 | 3; // Provisioning, Active, Suspended, Failed

export const TENANT_STATUS_LABELS: Record<TenantStatus, string> = {
  0: "Provisioning",
  1: "Active",
  2: "Suspended",
  3: "Failed",
};

export type TenantSummary = {
  id: string;
  name: string;
  slug: string;
  status: TenantStatus;
  url: string | null;
  createdAtUtc: string;
};

export type TenantDetail = {
  id: string;
  name: string;
  slug: string;
  status: TenantStatus;
  url: string | null;
  failureReason: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type ProvisioningJobType = 0 | 1 | 2; // CreateTenant, Suspend, Resume
export const JOB_TYPE_LABELS: Record<ProvisioningJobType, string> = {
  0: "Create",
  1: "Suspend",
  2: "Resume",
};

export type ProvisioningJobStatus = 0 | 1 | 2 | 3; // Pending, Running, Succeeded, Failed
export const JOB_STATUS_LABELS: Record<ProvisioningJobStatus, string> = {
  0: "Pending",
  1: "Running",
  2: "Succeeded",
  3: "Failed",
};

export type ProvisioningJob = {
  id: string;
  tenantId: string;
  tenantName: string;
  jobType: ProvisioningJobType;
  status: ProvisioningJobStatus;
  attempts: number;
  lastError: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type TenantStatusCounts = { provisioning: number; active: number; suspended: number; failed: number };
export type RecentJobSummary = {
  id: string;
  tenantName: string;
  jobType: ProvisioningJobType;
  status: ProvisioningJobStatus;
  updatedAtUtc: string;
};
export type PlatformDashboard = { tenantCounts: TenantStatusCounts; recentJobs: RecentJobSummary[] };

export type AuditEntry = {
  id: string;
  occurredAtUtc: string;
  actorEmail: string;
  action: string;
  tenantId: string | null;
  details: string | null;
};
