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

export type PendingInvitation = {
  id: string;
  email: string;
  role: string;
  createdAtUtc: string;
  expiresAtUtc: string;
  isExpired: boolean;
};

export type ForcePasswordResetResponse = {
  devResetUrl: string | null;
};

export type CreateInvitationResponse = {
  invitationId: string;
  devAcceptUrl: string | null;
};

export type EbayEnvironment = 0 | 1; // Sandbox, Production

// The company's eBay link. The Cert ID and the seller's tokens are never sent to the browser.
export type EbayStatus = {
  configured: boolean;
  environment: EbayEnvironment;
  clientId: string | null;
  ruName: string | null;
  connected: boolean;
  connectedAtUtc: string | null;
  accessExpiresAtUtc: string | null;
  lastOrderSyncAtUtc: string | null;
  lastProductSyncAtUtc: string | null;
  lastSyncError: string | null;
  importedOrders: number;
  callbackPath: string;
};

export type EbayOrderImport = { created: number; updated: number; productsCreated: number };
export type EbayProductImport = { created: number; updated: number; listings: number };

export type SalesChannel = 0; // Ebay
export const SALES_CHANNEL_LABELS: Record<SalesChannel, string> = { 0: "eBay" };

export type ListingStatus = 0 | 1 | 2; // Live, OutOfStock, Ended
export const LISTING_STATUS_LABELS: Record<ListingStatus, string> = {
  0: "Live",
  1: "Out of stock",
  2: "Ended",
};

// One posting of a product on an e-commerce site, as the site last reported it.
export type Listing = {
  id: string;
  productId: string;
  productSku: string;
  productName: string;
  channel: SalesChannel;
  externalId: string;
  marketplace: string | null;
  url: string | null;
  status: ListingStatus;
  price: number | null;
  currency: string | null;
  availableQuantity: number | null;
  soldQuantity: number | null;
  lastSyncedAtUtc: string;
};

export type Listings = { listings: Listing[]; connected: boolean; lastSyncedAtUtc: string | null };
