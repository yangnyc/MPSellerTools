import { apiFetch } from "../lib/api";
import type {
  AuditEntry,
  CompanySettings,
  CreateInvitationResponse,
  Order,
  OrderStatus,
  Product,
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
};

export const SettingsApi = {
  get: () => apiFetch<CompanySettings>("/api/settings"),
  update: (data: { companyName: string; rowVersion: string }) =>
    apiFetch<CompanySettings>("/api/settings", { method: "PUT", body: JSON.stringify(data) }),
};

export const AuditApi = {
  list: () => apiFetch<AuditEntry[]>("/api/audit"),
};
