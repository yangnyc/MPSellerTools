import { apiFetch } from "../lib/api";
import type {
  AuditEntry,
  CompanySettings,
  CreateInvitationResponse,
  EbayEnvironment,
  EbayOrderImport,
  EbayProductImport,
  EbayStatus,
  ForcePasswordResetResponse,
  ImportProductRow,
  ImportProductsResult,
  Listings,
  Order,
  OrderStatus,
  PendingInvitation,
  Product,
  SalesChannel,
  TaskStatus,
  TenantDashboard,
  UserSummary,
  WorkItem,
} from "./types";

export const DashboardApi = {
  get: () => apiFetch<TenantDashboard>("/api/dashboard"),
};

export const ProductsApi = {
  list: () => apiFetch<Product[]>("/api/products"),
  create: (data: { sku: string; name: string; price: number; stockQuantity: number }) =>
    apiFetch<Product>("/api/products", { method: "POST", body: JSON.stringify(data) }),
  update: (id: string, data: { name: string; price: number; stockQuantity: number; rowVersion: string }) =>
    apiFetch<Product>(`/api/products/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  archive: (id: string) => apiFetch<void>(`/api/products/${id}/archive`, { method: "POST" }),
  // Matched by SKU: new SKUs are created, known ones updated. A dry run only reports.
  importRows: (rows: ImportProductRow[], dryRun: boolean) =>
    apiFetch<ImportProductsResult>(`/api/products/import?dryRun=${dryRun}`, { method: "POST", body: JSON.stringify({ rows }) }),
};

export const ListingsApi = {
  // With a channel, only that marketplace's postings.
  list: (channel?: SalesChannel) => apiFetch<Listings>(channel === undefined ? "/api/listings" : `/api/listings?channel=${channel}`),
};

export const OrdersApi = {
  list: () => apiFetch<Order[]>("/api/orders"),
  create: (data: { items: { productId: string; quantity: number }[]; assignedUserId: string | null }) =>
    apiFetch<Order>("/api/orders", { method: "POST", body: JSON.stringify(data) }),
  update: (
    id: string,
    data: { items: { productId: string; quantity: number }[]; assignedUserId: string | null; rowVersion: string }
  ) => apiFetch<Order>(`/api/orders/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  changeStatus: (id: string, status: OrderStatus, rowVersion: string) =>
    apiFetch<Order>(`/api/orders/${id}/status`, { method: "POST", body: JSON.stringify({ status, rowVersion }) }),
};

export const TasksApi = {
  list: () => apiFetch<WorkItem[]>("/api/tasks"),
  create: (data: { title: string; description: string | null; assignedUserId: string; dueAtUtc: string | null }) =>
    apiFetch<WorkItem>("/api/tasks", { method: "POST", body: JSON.stringify(data) }),
  update: (
    id: string,
    data: {
      title: string;
      description: string | null;
      assignedUserId: string;
      dueAtUtc: string | null;
      rowVersion: string;
    }
  ) => apiFetch<WorkItem>(`/api/tasks/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  changeStatus: (id: string, status: TaskStatus, rowVersion: string) =>
    apiFetch<WorkItem>(`/api/tasks/${id}/status`, { method: "POST", body: JSON.stringify({ status, rowVersion }) }),
};

export const UsersApi = {
  list: () => apiFetch<UserSummary[]>("/api/users"),
  invite: (data: { email: string; role: string }) =>
    apiFetch<CreateInvitationResponse>("/api/invitations", { method: "POST", body: JSON.stringify(data) }),
  changeRole: (id: string, role: string) =>
    apiFetch<void>(`/api/users/${id}/role`, { method: "PUT", body: JSON.stringify({ role }) }),
  block: (id: string) => apiFetch<void>(`/api/users/${id}/block`, { method: "POST" }),
  unblock: (id: string) => apiFetch<void>(`/api/users/${id}/unblock`, { method: "POST" }),
  signOut: (id: string) => apiFetch<void>(`/api/users/${id}/sign-out`, { method: "POST" }),
  forcePasswordReset: (id: string) =>
    apiFetch<ForcePasswordResetResponse>(`/api/users/${id}/force-password-reset`, { method: "POST" }),
  // Adds a user straight away, with a password that is passed on to them.
  createUser: (data: { email: string; role: string; password: string }) =>
    apiFetch<void>("/api/users", { method: "POST", body: JSON.stringify(data) }),
  pendingInvitations: () => apiFetch<PendingInvitation[]>("/api/invitations"),
  revokeInvitation: (id: string) => apiFetch<void>(`/api/invitations/${id}`, { method: "DELETE" }),
};

export const SettingsApi = {
  get: () => apiFetch<CompanySettings>("/api/settings"),
  update: (data: { companyName: string; lowStockThreshold: number | null; lowStockAssigneeId: string | null; rowVersion: string }) =>
    apiFetch<CompanySettings>("/api/settings", { method: "PUT", body: JSON.stringify(data) }),
};

export const EbayApi = {
  get: () => apiFetch<EbayStatus>("/api/ebay"),
  // An empty clientSecret keeps the Cert ID already saved.
  saveSettings: (data: { environment: EbayEnvironment; clientId: string; clientSecret: string; ruName: string }) =>
    apiFetch<EbayStatus>("/api/ebay/settings", { method: "PUT", body: JSON.stringify(data) }),
  connect: () => apiFetch<{ authorizeUrl: string }>("/api/ebay/connect", { method: "POST" }),
  complete: (codeOrUrl: string) =>
    apiFetch<EbayStatus>("/api/ebay/complete", { method: "POST", body: JSON.stringify({ codeOrUrl }) }),
  disconnect: () => apiFetch<EbayStatus>("/api/ebay/disconnect", { method: "POST" }),
  importOrders: () => apiFetch<EbayOrderImport>("/api/ebay/import/orders", { method: "POST" }),
  importProducts: () => apiFetch<EbayProductImport>("/api/ebay/import/products", { method: "POST" }),
};

export const AuditApi = {
  // The server returns the newest 100 entries unless asked for more (up to 500).
  list: (take?: number) => apiFetch<AuditEntry[]>(take ? `/api/audit?take=${take}` : "/api/audit"),
};
