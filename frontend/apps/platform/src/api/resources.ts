import { apiFetch } from "../lib/api";
import type { AuditEntry, PlatformDashboard, ProvisioningJob, TenantDetail, TenantSummary } from "./types";

export const DashboardApi = {
  get: () => apiFetch<PlatformDashboard>("/api/dashboard"),
};

export const TenantsApi = {
  list: () => apiFetch<TenantSummary[]>("/api/tenants"),
  get: (id: string) => apiFetch<TenantDetail>(`/api/tenants/${id}`),
  create: (data: { name: string; slug: string; initialAdminEmail: string }) =>
    apiFetch<{ tenantId: string; jobId: string }>("/api/tenants", { method: "POST", body: JSON.stringify(data) }),
  update: (id: string, data: { name: string }) =>
    apiFetch<TenantDetail>(`/api/tenants/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  suspend: (id: string) => apiFetch<void>(`/api/tenants/${id}/suspend`, { method: "POST" }),
  resume: (id: string) => apiFetch<void>(`/api/tenants/${id}/resume`, { method: "POST" }),
  retryProvisioning: (id: string) => apiFetch<void>(`/api/tenants/${id}/retry-provisioning`, { method: "POST" }),
};

export const JobsApi = {
  list: () => apiFetch<ProvisioningJob[]>("/api/jobs"),
};

export const AuditApi = {
  list: () => apiFetch<AuditEntry[]>("/api/audit"),
};
