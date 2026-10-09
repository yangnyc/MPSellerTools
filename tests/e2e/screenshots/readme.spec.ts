import path from "node:path";
import { test, expect, type Page } from "@playwright/test";

// Takes the pictures the README shows, into docs/screenshots. Every API
// response is made up here, so no backend, database or real company is needed:
//   npm run screenshots        (from tests/e2e)

const outDir = path.resolve(import.meta.dirname, "../../../docs/screenshots");

const ago = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();
const day = (daysAgo: number) => new Date(Date.now() - daysAgo * 86_400_000).toISOString().slice(0, 10);

type Mocks = Record<string, unknown>;

// Serves `gets` by path; a path not listed reads as an empty list. A saved
// theme comes back on the profile, as the server would send it.
async function mockApi(page: Page, me: Record<string, unknown>, gets: Mocks) {
  let user = me;
  await page.route(/\/\/[^/]+\/api\//, async (route) => {
    const request = route.request();
    const urlPath = new URL(request.url()).pathname;
    if (urlPath === "/api/auth/me/theme" && request.method() === "PUT") {
      user = { ...user, theme: request.postDataJSON() };
      await route.fulfill({ json: user });
    } else if (request.method() !== "GET") {
      await route.fulfill({ json: {} });
    } else if (urlPath === "/api/auth/me") {
      await route.fulfill({ json: user });
    } else if (urlPath === "/api/antiforgery/token") {
      await route.fulfill({ json: { token: "screenshot" } });
    } else {
      await route.fulfill({ json: gets[urlPath] ?? [] });
    }
  });
}

async function shoot(page: Page, name: string, fullPage = false) {
  await page.evaluate(() => document.fonts.ready);
  // Lets entrance transitions and the loading bar finish.
  await page.waitForTimeout(600);
  await page.screenshot({ path: path.join(outDir, `${name}.png`), animations: "disabled", fullPage });
}

// ---------------------------------------------------------------- platform

const platformAdmin = { id: "admin", email: "admin@mpsellertools.local", displayName: "Morgan Reyes", roles: ["PlatformAdmin"], theme: null, pinnedMenus: ["tenants"] };

const companies = [
  { name: "Harbor & Pine Goods", slug: "harbor-pine", status: 1, port: 7201, created: 212 },
  { name: "Brightside Pharmacy", slug: "brightside-pharmacy", status: 1, port: 7202, created: 187 },
  { name: "Cedar Ridge Outdoors", slug: "cedar-ridge", status: 1, port: 7203, created: 141 },
  { name: "Lumen Home Supply", slug: "lumen-home", status: 1, port: 7204, created: 96 },
  { name: "Juniper Pet Co.", slug: "juniper-pet", status: 2, port: 7205, created: 73 },
  { name: "Atlas Cycle Works", slug: "atlas-cycle", status: 1, port: 7206, created: 38 },
  { name: "Saffron Kitchenware", slug: "saffron-kitchen", status: 0, port: 7207, created: 0 },
  { name: "Tidewater Tools", slug: "tidewater-tools", status: 3, port: 7208, created: 2 },
];

const tenants = companies.map((company, index) => ({
  id: `00000000-0000-4000-8000-00000000000${index}`,
  name: company.name,
  slug: company.slug,
  status: company.status,
  url: company.status === 1 ? `https://localhost:${company.port}` : null,
  createdAtUtc: ago(company.created * 1440 + 90),
}));

const provisioningJobs = [
  { tenantName: "Saffron Kitchenware", jobType: 0, status: 1, attempts: 1, lastError: null, at: 1 },
  { tenantName: "Tidewater Tools", jobType: 0, status: 3, attempts: 3, lastError: "The instance did not become ready within 30s.", at: 46 },
  { tenantName: "Atlas Cycle Works", jobType: 3, status: 2, attempts: 1, lastError: null, at: 180 },
  { tenantName: "Juniper Pet Co.", jobType: 1, status: 2, attempts: 1, lastError: null, at: 1500 },
  { tenantName: "Lumen Home Supply", jobType: 2, status: 2, attempts: 1, lastError: null, at: 2900 },
  { tenantName: "Atlas Cycle Works", jobType: 0, status: 2, attempts: 1, lastError: null, at: 54_000 },
].map((job, index) => ({ id: `job-${index}`, tenantId: `t-${index}`, ...job, createdAtUtc: ago(job.at + 2), updatedAtUtc: ago(job.at) }));

const platformMocks: Mocks = {
  "/api/dashboard": { tenantCounts: { provisioning: 1, active: 5, suspended: 1, failed: 1 }, recentJobs: provisioningJobs.slice(0, 5) },
  "/api/tenants": tenants,
  "/api/jobs": provisioningJobs,
};

test.describe("platform console", () => {
  test.beforeEach(({ page }) => mockApi(page, platformAdmin, platformMocks));

  test("dashboard", async ({ page }) => {
    await page.goto("/dashboard");
    await expect(page.getByText("Welcome back, Morgan")).toBeVisible();
    await expect(page.getByText("Saffron Kitchenware")).toBeVisible();
    await shoot(page, "platform-dashboard");
  });

  test("companies", async ({ page }) => {
    await page.goto("/tenants");
    await expect(page.getByText("Brightside Pharmacy")).toBeVisible();
    await shoot(page, "platform-companies");
  });
});

// --------------------------------------------------------------- workspace

const tenantAdmin = { id: "u1", email: "dana@harborpine.example", displayName: "Dana Whitfield", roles: ["TenantAdmin"], isBlocked: false, theme: null, pinnedMenus: ["inventory"] };

const users = [
  tenantAdmin,
  { id: "u2", email: "leo@harborpine.example", displayName: "Leo Marsh", roles: ["Employee"], isBlocked: false },
  { id: "u3", email: "priya@harborpine.example", displayName: "Priya Nair", roles: ["Employee"], isBlocked: false },
];

const catalog = [
  { sku: "HP-MUG-STONE", name: "Stoneware Camp Mug, 12 oz", price: 18, stock: 142 },
  { sku: "HP-BLKT-WOOL", name: "Merino Wool Throw Blanket", price: 89, stock: 37 },
  { sku: "HP-LNTN-BRASS", name: "Brass Storm Lantern", price: 64.5, stock: 8 },
  { sku: "HP-BTL-STEEL-1L", name: "Insulated Steel Bottle, 1 L", price: 32, stock: 210 },
  { sku: "HP-AXE-HATCH", name: "Forged Camp Hatchet", price: 74, stock: 5 },
  { sku: "HP-TOTE-WAXED", name: "Waxed Canvas Tote", price: 58, stock: 66 },
  { sku: "HP-CNDL-CEDAR", name: "Cedar & Smoke Soy Candle", price: 24, stock: 0 },
  { sku: "HP-KNF-FOLD", name: "Walnut Folding Knife", price: 49, stock: 91 },
  { sku: "HP-PAN-CAST-10", name: "Cast Iron Skillet, 10 in", price: 45, stock: 54 },
  { sku: "HP-HAT-BEANIE", name: "Ribbed Wool Beanie", price: 28, stock: 173 },
  { sku: "HP-STV-POCKET", name: "Pocket Camp Stove", price: 39.5, stock: 3 },
  { sku: "HP-ENML-PLATE", name: "Enamel Plate Set of 4", price: 36, stock: 48 },
];

const products = catalog.map((item, index) => ({
  id: `p${index}`,
  sku: item.sku,
  name: item.name,
  price: item.price,
  stockQuantity: item.stock,
  isArchived: false,
  rowVersion: "AAAAAAAAB9E=",
}));

const line = (index: number, quantity: number) => ({
  productId: products[index].id,
  productName: products[index].name,
  productSku: products[index].sku,
  quantity,
  unitPrice: products[index].price,
});

const orders = [
  { number: "ORD-10482", status: 0, user: null, items: [line(0, 4), line(3, 2)], at: 14 },
  { number: "ORD-10481", status: 0, user: "u2", items: [line(1, 1)], at: 52 },
  { number: "ORD-10480", status: 1, user: "u2", items: [line(8, 1), line(11, 1), line(7, 1)], at: 135 },
  { number: "ORD-10479", status: 1, user: "u3", items: [line(5, 2)], at: 260 },
  { number: "ORD-10478", status: 2, user: "u3", items: [line(9, 3), line(0, 2)], at: 1500 },
  { number: "ORD-10477", status: 2, user: "u2", items: [line(2, 1)], at: 1720 },
  { number: "ORD-10476", status: 3, user: null, items: [line(4, 1)], at: 2950 },
  { number: "ORD-10475", status: 2, user: "u3", items: [line(3, 6)], at: 3300 },
].map((order, index) => ({
  id: `o${index}`,
  orderNumber: order.number,
  status: order.status,
  assignedUserId: order.user,
  items: order.items,
  total: order.items.reduce((sum, item) => sum + item.quantity * item.unitPrice, 0),
  rowVersion: "AAAAAAAAB9E=",
  createdAtUtc: ago(order.at),
  updatedAtUtc: ago(order.at),
}));

const tasks = [
  { title: "Reorder Brass Storm Lantern — 8 left", status: 0, user: "u2", due: -1 },
  { title: "Photograph the autumn blanket colours", status: 1, user: "u3", due: 2 },
  { title: "Fix rejected Amazon listing: brand missing", status: 0, user: "u1", due: 1 },
  { title: "Count the stockroom shelves B1–B6", status: 0, user: "u2", due: 4 },
  { title: "Reply to the Walmart returns request", status: 2, user: "u3", due: -3 },
].map((task, index) => ({
  id: `t${index}`,
  title: task.title,
  description: null,
  status: task.status,
  assignedUserId: task.user,
  dueAtUtc: ago(-task.due * 1440),
  rowVersion: "AAAAAAAAB9E=",
  createdAtUtc: ago(3000 + index * 400),
  updatedAtUtc: ago(200 + index * 90),
}));

const dailyRevenue = [1180, 940, 1420, 1650, 1310, 2080, 2390, 1720, 1260, 1540, 1890, 2210, 2640, 1980];

const dashboard = {
  productCount: 248,
  openOrderCount: 17,
  openTaskCount: 6,
  totalOrderCount: 1284,
  totalTaskCount: 93,
  // channel: 0 eBay, 1 Amazon, 2 Walmart, 4 Magento
  workspace: {
    channels: [
      { id: "a-amazon", name: "Amazon", channel: 1, isEnabled: true, liveWrites: true, orderImport: true, stockSync: true, listings: 240, live: 231, drafts: 6, rejected: 3 },
      { id: "a-ebay", name: "eBay", channel: 0, isEnabled: true, liveWrites: true, orderImport: true, stockSync: true, listings: 187, live: 179, drafts: 8, rejected: 0 },
      { id: "a-walmart", name: "Walmart", channel: 2, isEnabled: true, liveWrites: true, orderImport: true, stockSync: false, listings: 142, live: 124, drafts: 18, rejected: 0 },
      { id: "a-magento", name: "Magento", channel: 4, isEnabled: true, liveWrites: false, orderImport: false, stockSync: false, listings: 119, live: 77, drafts: 42, rejected: 0 },
    ],
    jobsRunning: 1,
    jobsWaiting: 1,
    jobsNeedingALook: 1,
    jobDays: 7,
    lastJobSummary: "187 listings checked; all can be published.",
    catalog: { noPrice: 2, noStock: 9, noCategory: 14, noPictures: 5 },
    productsAddedThisWeek: 23,
  },
  sales: {
    days: 14,
    revenue: dailyRevenue.reduce((sum, value) => sum + value, 0),
    orders: 412,
    channels: [
      { channel: "Amazon", orders: 168, revenue: 9840 },
      { channel: "eBay", orders: 104, revenue: 5730 },
      { channel: "Walmart", orders: 71, revenue: 4120 },
      { channel: "Magento", orders: 46, revenue: 3110 },
      { channel: "Website", orders: 23, revenue: 1410 },
    ],
    daily: dailyRevenue.map((revenue, index) => ({ date: day(13 - index), orders: Math.round(revenue / 58), revenue })),
    lowStockThreshold: 10,
    lowStockCount: 4,
    lowStock: [
      { variantId: "v6", sku: "HP-CNDL-CEDAR", productName: "Cedar & Smoke Soy Candle", availableToSell: 0, onHand: 0 },
      { variantId: "v10", sku: "HP-STV-POCKET", productName: "Pocket Camp Stove", availableToSell: 2, onHand: 3 },
      { variantId: "v4", sku: "HP-AXE-HATCH", productName: "Forged Camp Hatchet", availableToSell: 4, onHand: 5 },
      { variantId: "v2", sku: "HP-LNTN-BRASS", productName: "Brass Storm Lantern", availableToSell: 6, onHand: 8 },
    ],
    listings: { draft: 38, live: 611, processing: 14, rejected: 3, offSale: 22 },
    failedSyncJobs: 0,
    openOrderIssues: 0,
    staleChannels: [],
  },
};

// channel: 0 eBay, 1 Amazon, 2 Walmart, 4 Magento
const posted = [
  { product: 0, channel: 1, id: "B0CK4M7Q2X", marketplace: "US", status: 0, sold: 318 },
  { product: 0, channel: 0, id: "256481937204", marketplace: "EBAY_US", status: 0, sold: 97 },
  { product: 1, channel: 1, id: "B0CJ8T5N1R", marketplace: "US", status: 0, sold: 64 },
  { product: 1, channel: 4, id: "1042", marketplace: "Default store", status: 0, sold: 21 },
  { product: 2, channel: 2, id: "5083317746", marketplace: "US", status: 0, sold: 33 },
  { product: 3, channel: 1, id: "B0CL2W9H6D", marketplace: "US", status: 0, sold: 402 },
  { product: 3, channel: 2, id: "5083317802", marketplace: "US", status: 0, sold: 118 },
  { product: 4, channel: 0, id: "256481940118", marketplace: "EBAY_US", status: 0, sold: 12 },
  { product: 5, channel: 4, id: "1057", marketplace: "Default store", status: 0, sold: 45 },
  { product: 6, channel: 1, id: "B0CM6P3K8V", marketplace: "US", status: 1, sold: 276 },
  { product: 7, channel: 0, id: "256481951330", marketplace: "EBAY_US", status: 0, sold: 58 },
  { product: 8, channel: 2, id: "5083318155", marketplace: "US", status: 0, sold: 87 },
  { product: 9, channel: 1, id: "B0CN1R7T4B", marketplace: "US", status: 0, sold: 190 },
  { product: 10, channel: 0, id: "256481960442", marketplace: "EBAY_US", status: 2, sold: 140 },
];

const listings = {
  connected: true,
  lastSyncedAtUtc: ago(6),
  listings: posted.map((entry, index) => ({
    id: `l${index}`,
    productId: products[entry.product].id,
    productSku: products[entry.product].sku,
    productName: products[entry.product].name,
    channel: entry.channel,
    externalId: entry.id,
    marketplace: entry.marketplace,
    url: `https://example.com/item/${entry.id}`,
    status: entry.status,
    price: products[entry.product].price,
    currency: "USD",
    availableQuantity: entry.status === 0 ? products[entry.product].stockQuantity : 0,
    soldQuantity: entry.sold,
    lastSyncedAtUtc: ago(6 + index),
  })),
};

const reservedBySku = [6, 2, 2, 14, 1, 3, 0, 5, 4, 9, 1, 2];

const inventory = {
  accountingEnabled: true,
  items: catalog.map((item, index) => {
    const safetyStock = item.stock > 100 ? 10 : item.stock > 0 ? 2 : 0;
    return {
      variantId: `v${index}`,
      productId: `p${index}`,
      sku: item.sku,
      productName: item.name,
      variantName: null,
      price: item.price,
      onHand: item.stock,
      reserved: Math.min(reservedBySku[index], item.stock),
      safetyStock,
      availableToSell: Math.max(0, item.stock - Math.min(reservedBySku[index], item.stock) - safetyStock),
      updatedAtUtc: ago(30 + index * 47),
    };
  }),
};

const movements = [
  { sku: "HP-MUG-STONE", type: 1, onHand: 0, reserved: 4, reference: "ORD-10482", at: 14 },
  { sku: "HP-BTL-STEEL-1L", type: 1, onHand: 0, reserved: 2, reference: "ORD-10482", at: 14 },
  { sku: "HP-BLKT-WOOL", type: 1, onHand: 0, reserved: 1, reference: "ORD-10481", at: 52 },
  { sku: "HP-HAT-BEANIE", type: 3, onHand: -3, reserved: -3, reference: "ORD-10478", at: 1400 },
  { sku: "HP-LNTN-BRASS", type: 0, onHand: -2, reserved: 0, reference: "Stock count", at: 1650 },
  { sku: "HP-TOTE-WAXED", type: 4, onHand: 1, reserved: 0, reference: "RMA-2231", at: 2100 },
].map((movement, index) => ({
  id: `m${index}`,
  variantId: `v${index}`,
  sku: movement.sku,
  type: movement.type,
  onHandDelta: movement.onHand,
  reservedDelta: movement.reserved,
  reference: movement.reference,
  occurredAtUtc: ago(movement.at),
}));

const account = (id: string, channel: number, name: string, marketplaceCode: string) => ({
  id, channel, name, environment: 1, sellerId: null, settings: null, hasCredentials: true, isEnabled: true,
  liveWritesEnabled: true, effectiveLiveWrites: true, inventorySyncEnabled: true, orderImportEnabled: true, priceConflictPolicy: 2,
  lastOrderImportAtUtc: ago(9), lastError: null, markets: [{ id: `m-${id}`, marketplaceCode, language: "en-US", currency: "USD" }],
});

const channels = [
  account("a-ebay", 0, "eBay", "EBAY_US"),
  account("a-amazon", 1, "Amazon", "ATVPDKIKX0DER"),
  account("a-walmart", 2, "Walmart", "US"),
  account("a-magento", 4, "Magento", "default"),
];

const bulkJob = (over: Record<string, unknown>) => ({
  cancelRequested: false, summary: null, lastError: null, errors: [], createdByEmail: tenantAdmin.email, startedAtUtc: null, finishedAtUtc: null, ...over,
});

const bulkJobs = [
  bulkJob({
    id: "b1", type: 0, status: 1, channelAccountId: "a-amazon", accountName: "Amazon", channel: 1, total: 240, processed: 156, succeeded: 153, failed: 3,
    createdAtUtc: ago(9), startedAtUtc: ago(8),
    errors: [{ item: "HP-CNDL-CEDAR", message: "Amazon asks for a brand, and this product has none." }],
  }),
  bulkJob({
    id: "b2", type: 4, status: 0, channelAccountId: "a-magento", accountName: "Magento", channel: 4, total: 0, processed: 0, succeeded: 0, failed: 0,
    createdAtUtc: ago(2),
  }),
  bulkJob({
    id: "b3", type: 3, status: 2, channelAccountId: "a-ebay", accountName: "eBay", channel: 0, total: 187, processed: 187, succeeded: 187, failed: 0,
    summary: "187 listings checked; all can be published.", createdAtUtc: ago(190), startedAtUtc: ago(189), finishedAtUtc: ago(184),
  }),
  bulkJob({
    id: "b4", type: 2, status: 3, channelAccountId: "a-walmart", accountName: "Walmart", channel: 2, total: 42, processed: 42, succeeded: 39, failed: 3,
    summary: "39 sent again, 3 held back.", createdAtUtc: ago(1500), startedAtUtc: ago(1499), finishedAtUtc: ago(1490),
    errors: [{ item: "HP-AXE-HATCH", message: "Walmart refused the item: shipping weight is missing." }],
  }),
  bulkJob({
    id: "b5", type: 6, status: 2, channelAccountId: "a-amazon", accountName: "Amazon", channel: 1, total: 60, processed: 60, succeeded: 60, failed: 0,
    summary: "60 products made from Amazon's catalog.", createdAtUtc: ago(2900), startedAtUtc: ago(2899), finishedAtUtc: ago(2880),
  }),
];

const workspaceMocks: Mocks = {
  "/api/dashboard": dashboard,
  "/api/orders": orders,
  "/api/tasks": tasks,
  "/api/products": products,
  "/api/users": users,
  "/api/listings": listings,
  "/api/catalog/inventory": inventory,
  "/api/catalog/inventory/movements": movements,
  "/api/channels": channels,
  "/api/bulk-jobs": bulkJobs,
};

test.describe("workspace", () => {
  test.beforeEach(({ page }) => mockApi(page, tenantAdmin, workspaceMocks));

  test("dashboard, in light and dark", async ({ page }) => {
    await page.goto("/dashboard");
    await expect(page.getByText("Welcome back, Dana")).toBeVisible();
    await expect(page.getByText("ORD-10482")).toBeVisible();
    await shoot(page, "workspace-dashboard");
    await shoot(page, "workspace-dashboard-full", true);

    await page.getByRole("button", { name: "Display settings" }).click();
    const settings = page.locator(".MuiDrawer-paper").filter({ hasText: "Display Settings" });
    await settings.getByRole("switch").last().click();
    await shoot(page, "workspace-display-settings");
    await settings.getByText("close", { exact: true }).click();
    await shoot(page, "workspace-dashboard-dark");
  });

  test("products", async ({ page }) => {
    await page.goto("/products");
    await expect(page.getByText("Merino Wool Throw Blanket")).toBeVisible();
    await shoot(page, "workspace-products");
  });

  test("listings", async ({ page }) => {
    await page.goto("/listings");
    await expect(page.getByText("B0CK4M7Q2X")).toBeVisible();
    await shoot(page, "workspace-listings");
  });

  test("stock", async ({ page }) => {
    await page.goto("/inventory");
    await expect(page.getByText("HP-BLKT-WOOL").first()).toBeVisible();
    await shoot(page, "workspace-stock");
  });

  test("orders", async ({ page }) => {
    await page.goto("/orders");
    await expect(page.getByText("ORD-10482")).toBeVisible();
    await shoot(page, "workspace-orders");
  });

  test("jobs", async ({ page }) => {
    await page.goto("/jobs");
    await expect(page.getByText("Publish every draft").first()).toBeVisible();
    await shoot(page, "workspace-jobs");
  });
});
