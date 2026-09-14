// Mirrors the response/request DTOs in MPSellerTools.TenantHost.Contracts.

export type Product = {
  id: string;
  sku: string;
  name: string;
  price: number;
  stockQuantity: number;
  isArchived: boolean;
  rowVersion: string;
};

export type OrderStatus = 0 | 1 | 2 | 3; // New, InProgress, Completed, Cancelled

export const ORDER_STATUS_LABELS: Record<OrderStatus, string> = {
  0: "New",
  1: "In Progress",
  2: "Completed",
  3: "Cancelled",
};

export type OrderItemLine = {
  productId: string;
  productName: string;
  productSku: string;
  quantity: number;
  unitPrice: number;
};

export type Order = {
  id: string;
  orderNumber: string;
  status: OrderStatus;
  assignedUserId: string | null;
  items: OrderItemLine[];
  total: number;
  rowVersion: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type TaskStatus = 0 | 1 | 2 | 3; // Open, InProgress, Done, Cancelled

export const TASK_STATUS_LABELS: Record<TaskStatus, string> = {
  0: "Open",
  1: "In Progress",
  2: "Done",
  3: "Cancelled",
};

export type WorkItem = {
  id: string;
  title: string;
  description: string | null;
  status: TaskStatus;
  assignedUserId: string;
  dueAtUtc: string | null;
  rowVersion: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type UserSummary = {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  isBlocked: boolean;
};

export type CompanySettings = {
  companyName: string;
  rowVersion: string;
};

export type AuditEntry = {
  id: string;
  occurredAtUtc: string;
  actorEmail: string;
  action: string;
  details: string | null;
};

export type TenantDashboard = {
  productCount: number;
  openOrderCount: number;
  openTaskCount: number;
  totalOrderCount: number;
  totalTaskCount: number;
};

export type CreateInvitationResponse = {
  invitationId: string;
  devAcceptUrl: string | null;
};
