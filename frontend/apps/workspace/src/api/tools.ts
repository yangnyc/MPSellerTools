import { apiFetch } from "../lib/api";
import type { OrderStatus, SalesChannel } from "./types";

// Mirrors ReportsController, ShippingController and CatalogToolsController in MPSellerTools.TenantHost.

// --- Reports -------------------------------------------------------------------------------------------

// Orders of the last `days` days, cancelled ones left out, beside the same span before it.
export type SalesReport = {
  days: number;
  revenue: number;
  orders: number;
  units: number;
  averageOrder: number;
  previousRevenue: number;
  previousOrders: number;
  cancelled: number;
  daily: { date: string; orders: number; units: number; revenue: number }[];
  channels: { channel: string; orders: number; units: number; revenue: number }[];
  topProducts: { productId: string; sku: string; name: string; units: number; revenue: number; orders: number }[];
};

export type InventoryReportItem = {
  productId: string;
  sku: string;
  name: string;
  category: string | null;
  price: number;
  onHand: number;
  reserved: number;
  availableToSell: number;
  // What the stock on hand would sell for at the product's own price.
  value: number;
  soldLast30Days: number;
  // At the pace of the last thirty days; null for something that has not sold.
  daysOfStock: number | null;
};

export type InventoryReport = {
  products: number;
  unitsOnHand: number;
  value: number;
  outOfStock: number;
  lowStock: number;
  lowStockThreshold: number;
  // In stock, with nothing sold in thirty days.
  notSelling: number;
  items: InventoryReportItem[];
};

export type ListingsReport = {
  products: number;
  // Active products that are on no sales channel at all.
  notListed: number;
  channels: {
    accountId: string;
    name: string;
    channel: SalesChannel;
    total: number;
    live: number;
    processing: number;
    drafts: number;
    rejected: number;
    offSale: number;
    withIssues: number;
  }[];
  problems: { listingId: string; channel: string; sku: string; problem: string }[];
};

export const ReportsApi = {
  sales: (days: number) => apiFetch<SalesReport>(`/api/reports/sales?days=${days}`),
  inventory: () => apiFetch<InventoryReport>("/api/reports/inventory"),
  listings: () => apiFetch<ListingsReport>("/api/reports/listings"),
};

// --- Shipping ------------------------------------------------------------------------------------------

export type ShippingLine = { productId: string; sku: string; name: string; quantity: number; onHand: number };

// An order waiting to go out. `short` says a line asks for more than is on the shelf.
export type ShippingOrder = {
  id: string;
  orderNumber: string;
  status: OrderStatus;
  channel: string;
  assignedTo: string | null;
  total: number;
  createdAtUtc: string;
  units: number;
  short: boolean;
  lines: ShippingLine[];
};

// One product to fetch from the shelf, for every open order together.
export type PickLine = { productId: string; sku: string; name: string; quantity: number; onHand: number; orders: number; orderNumbers: string[] };

export type ShippedOrder = {
  id: string;
  orderNumber: string;
  channel: string;
  total: number;
  units: number;
  carrier: string | null;
  trackingNumber: string | null;
  shippedAtUtc: string;
};

export const ShippingApi = {
  queue: () => apiFetch<ShippingOrder[]>("/api/shipping/queue"),
  pickList: () => apiFetch<PickLine[]>("/api/shipping/pick-list"),
  shipped: (days: number) => apiFetch<ShippedOrder[]>(`/api/shipping/shipped?days=${days}`),
  // Completes the order, with who carried it and the tracking number when given.
  ship: (orderId: string, data: { carrier: string; trackingNumber: string }) =>
    apiFetch<void>(`/api/shipping/${orderId}/ship`, { method: "POST", body: JSON.stringify(data) }),
};

// --- Import from a website, and cleaning the catalog -----------------------------------------------------

export type WebsiteImportOptions = {
  // The store's name or address.
  source: string;
  platform: "shopify" | "magento";
  // "health": prescription and clinical items left out, products sorted into health categories. "general": no such rules.
  rules: "health" | "general";
  limit: number | null;
  existing: "skip" | "refresh";
  updatePrices: boolean;
  priceAdjustPercent: number;
  stock: number;
  maxPictures: number;
  inStockOnly: boolean;
  variants: "first" | "all" | "skip";
  allowClinical: boolean;
  keepUncategorised: boolean;
  includeNeedsReview: boolean;
  skuPrefix: string | null;
  plainTextDescriptions: boolean;
  dryRun: boolean;
};

export type CatalogHealth = {
  products: number;
  archived: number;
  noPicture: number;
  noDescription: number;
  noPrice: number;
  noCategory: number;
  noBrand: number;
  outOfStock: number;
  notListed: number;
  duplicateGroups: number;
  duplicateProducts: number;
  retiredAsDuplicates: number;
};

export type DuplicateProduct = {
  id: string;
  sku: string;
  name: string;
  brand: string | null;
  category: string | null;
  price: number;
  stock: number;
  pictures: number;
  listings: number;
  orders: number;
  // How complete and established it is; the highest in a group is the one to keep.
  score: number;
  scoreParts: Record<string, number>;
};

export type DuplicateGroup = { key: string; reason: string; similarity: number; keepId: string; products: DuplicateProduct[] };

export type CatalogScan = { health: CatalogHealth; groups: DuplicateGroup[]; canUndo: boolean; lastCleanAtUtc: string | null; lastCleanRetired: number };

export type CleanStrategy = "exact" | "sku" | "normalized" | "fuzzy";

export const CatalogToolsApi = {
  // Queues the import (or, with dryRun, only a reading that reports); answers with the background job.
  importWebsite: (options: WebsiteImportOptions) =>
    apiFetch<{ jobId: string; dryRun: boolean }>("/api/catalog-tools/website-import", { method: "POST", body: JSON.stringify(options) }),
  scan: (strategies: CleanStrategy[], threshold: number) =>
    apiFetch<CatalogScan>(`/api/catalog-tools/clean?strategies=${strategies.join(",")}&threshold=${threshold}`),
  // Archives the products chosen as duplicates; `skipped` were listed for sale or already gone.
  apply: (groups: { key: string; keepId: string; retireIds: string[] }[]) =>
    apiFetch<{ retired: number; skipped: number }>("/api/catalog-tools/clean/apply", { method: "POST", body: JSON.stringify({ groups }) }),
  undo: () => apiFetch<{ restored: number }>("/api/catalog-tools/clean/undo", { method: "POST" }),
  ignore: (key: string) => apiFetch<void>("/api/catalog-tools/clean/ignore", { method: "POST", body: JSON.stringify({ key }) }),
};
