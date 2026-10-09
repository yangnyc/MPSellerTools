import { apiFetch } from "../lib/api";
import type {
  AuditEntry,
  PlatformDashboard,
  ProvisioningJob,
  TenantDetail,
  TenantRuntime,
  TenantSummary,
  TenantUser,
  TenantUsers,
} from "./types";

export const DashboardApi = {
  get: () => apiFetch<PlatformDashboard>("/api/dashboard"),
};

export const TenantsApi = {
  list: () => apiFetch<TenantSummary[]>("/api/tenants"),
  get: (id: string) => apiFetch<TenantDetail>(`/api/tenants/${id}`),
  // The password may be left out when the email already signs in to another company, whose password is then kept.
  create: (data: { name: string; slug: string; initialAdminEmail: string; initialAdminPassword?: string }) =>
    apiFetch<{ tenantId: string; jobId: string }>("/api/tenants", { method: "POST", body: JSON.stringify(data) }),
  update: (id: string, data: { name: string }) =>
    apiFetch<TenantDetail>(`/api/tenants/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  suspend: (id: string) => apiFetch<void>(`/api/tenants/${id}/suspend`, { method: "POST" }),
  resume: (id: string) => apiFetch<void>(`/api/tenants/${id}/resume`, { method: "POST" }),
  runtime: () => apiFetch<TenantRuntime[]>("/api/tenants/runtime"),
  restart: (id: string) => apiFetch<void>(`/api/tenants/${id}/restart`, { method: "POST" }),
  retryProvisioning: (id: string) => apiFetch<void>(`/api/tenants/${id}/retry-provisioning`, { method: "POST" }),
  // The server deletes only when the slug is repeated back to it.
  remove: (id: string, confirmSlug: string) =>
    apiFetch<void>(`/api/tenants/${id}?confirmSlug=${encodeURIComponent(confirmSlug)}`, { method: "DELETE" }),
};

// Users live in each company's own database; these calls go through the
// platform to that company's instance.
const tenantUser = (user: TenantUser, action: string) => `/api/tenant-users/${user.tenantId}/${user.id}/${action}`;

export const TenantUsersApi = {
  list: () => apiFetch<TenantUsers>("/api/tenant-users"),
  invite: (tenantId: string, data: { email: string; role: string }) =>
    apiFetch<{ devAcceptUrl: string | null }>(`/api/tenant-users/${tenantId}/invite`, { method: "POST", body: JSON.stringify(data) }),
  create: (tenantId: string, data: { email: string; role: string; password: string }) =>
    apiFetch<void>(`/api/tenant-users/${tenantId}/create`, { method: "POST", body: JSON.stringify(data) }),
  update: (user: TenantUser, data: { displayName: string; email: string }) =>
    apiFetch<void>(`/api/tenant-users/${user.tenantId}/${user.id}`, { method: "PUT", body: JSON.stringify(data) }),
  remove: (user: TenantUser) => apiFetch<void>(`/api/tenant-users/${user.tenantId}/${user.id}`, { method: "DELETE" }),
  setPassword: (user: TenantUser, password: string) =>
    apiFetch<void>(tenantUser(user, "set-password"), { method: "POST", body: JSON.stringify({ password }) }),
  changeRole: (user: TenantUser, role: string) =>
    apiFetch<void>(tenantUser(user, "role"), { method: "PUT", body: JSON.stringify({ role }) }),
  block: (user: TenantUser) => apiFetch<void>(tenantUser(user, "block"), { method: "POST" }),
  unblock: (user: TenantUser) => apiFetch<void>(tenantUser(user, "unblock"), { method: "POST" }),
  signOut: (user: TenantUser) => apiFetch<void>(tenantUser(user, "sign-out"), { method: "POST" }),
  forcePasswordReset: (user: TenantUser) =>
    apiFetch<{ devResetUrl: string | null }>(tenantUser(user, "force-password-reset"), { method: "POST" }),
};

export const JobsApi = {
  list: () => apiFetch<ProvisioningJob[]>("/api/jobs"),
};

export const AuditApi = {
  list: () => apiFetch<AuditEntry[]>("/api/audit"),
};
