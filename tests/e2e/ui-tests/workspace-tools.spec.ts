import { test, expect, type Page } from "@playwright/test";

const admin = { id: "u1", email: "ada@acme.test", displayName: "Ada Admin", roles: ["TenantAdmin"], isBlocked: false, pinnedMenus: [] };

const salesReport = {
  days: 30, revenue: 1250.5, orders: 40, units: 95, averageOrder: 31.26, previousRevenue: 1000, previousOrders: 38, cancelled: 2,
  daily: Array.from({ length: 30 }, (_, i) => ({ date: `2026-09-${String(i + 1).padStart(2, "0")}`, orders: i % 3, units: i % 5, revenue: i * 10 })),
  channels: [{ channel: "Magento", orders: 30, units: 70, revenue: 900 }, { channel: "Created here", orders: 10, units: 25, revenue: 350.5 }],
  topProducts: [{ productId: "p1", sku: "MUG-1", name: "Blue mug", units: 20, revenue: 400, orders: 12 }],
};
const inventoryReport = {
  products: 3, unitsOnHand: 60, value: 900, outOfStock: 1, lowStock: 1, lowStockThreshold: 5, notSelling: 1,
  items: [
    { productId: "p1", sku: "MUG-1", name: "Blue mug", category: "Mugs", price: 20, onHand: 4, reserved: 0, availableToSell: 4, value: 80, soldLast30Days: 20, daysOfStock: 6 },
    { productId: "p2", sku: "CUP-1", name: "Red cup", category: null, price: 10, onHand: 0, reserved: 0, availableToSell: 0, value: 0, soldLast30Days: 3, daysOfStock: 0 },
    { productId: "p3", sku: "JAR-1", name: "Old jar", category: "Jars", price: 15, onHand: 56, reserved: 0, availableToSell: 56, value: 840, soldLast30Days: 0, daysOfStock: null },
  ],
};
const listingsReport = {
  products: 3, notListed: 1,
  channels: [{ accountId: "a1", name: "Magento", channel: 4, total: 2, live: 1, processing: 0, drafts: 0, rejected: 1, offSale: 0, withIssues: 1 }],
  problems: [{ listingId: "l1", channel: "Magento", sku: "CUP-1", problem: "Magento accepts product names of up to 255 characters." }],
};
const queue = [{
  id: "o1", orderNumber: "ORD-1001", status: 0, channel: "Magento", assignedTo: null, total: 40, createdAtUtc: "2026-10-01T10:00:00Z", units: 2, short: true,
  lines: [{ productId: "p2", sku: "CUP-1", name: "Red cup", quantity: 2, onHand: 0 }],
}];
const pickList = [{ productId: "p2", sku: "CUP-1", name: "Red cup", quantity: 2, onHand: 0, orders: 1, orderNumbers: ["ORD-1001"] }];
const shipped = [{ id: "o0", orderNumber: "ORD-0999", channel: "Created here", total: 25, units: 1, carrier: "UPS", trackingNumber: "1Z999", shippedAtUtc: "2026-10-02T10:00:00Z" }];
const product = (id: string, sku: string, score: number) => ({
  id, sku, name: "Zentrol Tablets 325 mg, 100 Count", brand: "Zentrol", category: "Medicines / Pain & Fever", price: 10, stock: 4, pictures: 2, listings: 0, orders: 0, score,
  scoreParts: { Pictures: 6 },
});
const scan = {
  health: { products: 120, archived: 4, noPicture: 3, noDescription: 5, noPrice: 2, noCategory: 7, noBrand: 9, outOfStock: 11, notListed: 40, duplicateGroups: 1, duplicateProducts: 1, retiredAsDuplicates: 0 },
  groups: [{ key: "g1", reason: "Same words", similarity: 100, keepId: "d1", products: [product("d1", "DUP-A", 30), product("d2", "DUP-B", 12)] }],
  canUndo: false, lastCleanAtUtc: null, lastCleanRetired: 0,
};
const dryRun = {
  id: "j-dry", type: 7, status: 2, channelAccountId: null, accountName: null, channel: null, total: 2, processed: 2, succeeded: 2, failed: 0, cancelRequested: false,
  summary: "Dry run, nothing was changed: 2 of the 7 products at shop.example.com would be imported. Left out: 2 prescription or clinical.", lastError: null, errors: [],
  report: [{ label: "Products in the source", value: "7" }, { label: "Category: Medicines / Pain & Fever", value: "1" }],
  createdByEmail: "ada@acme.test", createdAtUtc: "2026-10-09T10:00:00Z", startedAtUtc: null, finishedAtUtc: "2026-10-09T10:01:00Z",
};

let writes: string[];

async function mockApi(page: Page) {
  writes = [];
  page.on("pageerror", (error) => { throw error; });
  await page.route(/\/\/[^/]+\/api\//, async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() !== "GET") {
      writes.push(`${request.method()} ${path} ${request.postData() ?? ""}`.trim());
      if (path === "/api/catalog-tools/website-import") await route.fulfill({ status: 202, json: { jobId: "j-dry", dryRun: true } });
      else if (path === "/api/catalog-tools/clean/apply") await route.fulfill({ json: { retired: 1, skipped: 0 } });
      // Saving the pins answers with the user as saved.
      else if (path === "/api/auth/me/pinned-menus") await route.fulfill({ json: { ...admin, pinnedMenus: request.postDataJSON().menus } });
      else await route.fulfill({ status: 204 });
    } else if (path === "/api/auth/me") await route.fulfill({ json: admin });
    else if (path === "/api/antiforgery/token") await route.fulfill({ json: { token: "test" } });
    else if (path === "/api/reports/sales") await route.fulfill({ json: salesReport });
    else if (path === "/api/reports/inventory") await route.fulfill({ json: inventoryReport });
    else if (path === "/api/reports/listings") await route.fulfill({ json: listingsReport });
    else if (path === "/api/shipping/queue") await route.fulfill({ json: queue });
    else if (path === "/api/shipping/pick-list") await route.fulfill({ json: pickList });
    else if (path === "/api/shipping/shipped") await route.fulfill({ json: shipped });
    else if (path === "/api/catalog-tools/clean") await route.fulfill({ json: scan });
    else if (path === "/api/bulk-jobs/j-dry") await route.fulfill({ json: dryRun });
    else await route.fulfill({ json: [] });
  });
}

test("the Reports menu holds the sales, inventory and listings reports", async ({ page }) => {
  await mockApi(page);
  await page.goto("/reports/sales");
  const sidebar = page.locator(".MuiDrawer-paper").filter({ has: page.getByText("MP Seller Tools", { exact: true }) });
  await expect(sidebar.locator('a[href="/reports/listings"]')).toBeVisible();
  expect(await sidebar.locator('a[href^="/reports"]').evaluateAll((items) => items.map((item) => item.getAttribute("href")))).toEqual(["/reports/sales", "/reports/inventory", "/reports/listings"]);

  await expect(page.getByRole("heading", { name: "Sales report" })).toBeVisible();
  await expect(page.getByText("up 25% on the span before")).toBeVisible();
  await expect(page.getByRole("row").filter({ hasText: "Blue mug" })).toContainText("$400.00");

  await sidebar.locator('a[href="/reports/inventory"]').click();
  await expect(page.getByRole("heading", { name: "Inventory report" })).toBeVisible();
  // What sells and is about to run out comes first.
  await expect(page.getByRole("row").filter({ hasText: "Blue mug" })).toContainText("6 days");
  await page.getByRole("button", { name: /Not selling/ }).click();
  await expect(page.getByRole("row").filter({ hasText: "Old jar" })).toContainText("not selling");

  await sidebar.locator('a[href="/reports/listings"]').click();
  await expect(page.getByRole("heading", { name: "Listings report" })).toBeVisible();
  await expect(page.getByText("Magento accepts product names of up to 255 characters.")).toBeVisible();
});

test("the Shipping menu shows what to ship and pick, and marks an order shipped with its tracking number", async ({ page }) => {
  await mockApi(page);
  await page.goto("/shipping");
  await expect(page.getByRole("heading", { name: "Ready to ship" })).toBeVisible();
  await expect(page.getByText("only 0 on the shelf")).toBeVisible();

  await page.getByRole("button", { name: "Ship ORD-1001" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Carrier").selectOption("FedEx");
  await dialog.getByLabel("Tracking number").fill("7749 0000");
  await dialog.getByRole("button", { name: "Mark as shipped" }).click();
  await expect(page.getByText("ORD-1001 marked as shipped, tracking 7749 0000.")).toBeVisible();
  expect(writes).toEqual(['POST /api/shipping/o1/ship {"carrier":"FedEx","trackingNumber":"7749 0000"}']);

  const sidebar = page.locator(".MuiDrawer-paper").filter({ has: page.getByText("MP Seller Tools", { exact: true }) });
  await sidebar.locator('a[href="/shipping/pick-list"]').click();
  await expect(page.getByRole("heading", { name: "Pick list" })).toBeVisible();
  await expect(page.getByText("0 · short by 2")).toBeVisible();
  await sidebar.locator('a[href="/shipping/shipped"]').click();
  await expect(page.getByRole("heading", { name: "Shipped", exact: true })).toBeVisible();
  await expect(page.getByRole("row").filter({ hasText: "ORD-0999" })).toContainText("1Z999");
});

test("Import & management reads another store as a dry run first, and cleans the catalog of duplicates", async ({ page }) => {
  await mockApi(page);
  await page.goto("/import/website");
  const sidebar = page.locator(".MuiDrawer-paper").filter({ has: page.getByText("MP Seller Tools", { exact: true }) });
  await expect(sidebar.getByRole("button", { name: /Import & management/ })).toHaveAttribute("aria-expanded", "true");
  await expect(sidebar.locator('a[href="/import/clean"]')).toBeVisible();
  expect(await sidebar.locator('a[href^="/import"]').evaluateAll((items) => items.map((item) => item.getAttribute("href")))).toEqual([
    "/import/website", "/import/amazon", "/import/amazon/bulk", "/import/clean",
  ]);

  await expect(page.getByRole("heading", { name: "Import from a website" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Dry run first" })).toBeDisabled();
  await page.getByLabel("Store address").fill("shop.example.com");
  await page.getByLabel("Price adjustment %").fill("10");
  await page.getByRole("button", { name: "Dry run first" }).click();
  // The report of the dry run, and the way on from it.
  await expect(page.getByText("Dry run, nothing was changed: 2 of the 7 products", { exact: false }).first()).toBeVisible();
  await expect(page.getByText("Category: Medicines / Pain & Fever")).toBeVisible();
  await expect(page.getByRole("button", { name: "Import these products" })).toBeEnabled();
  expect(JSON.parse(writes[0].slice("POST /api/catalog-tools/website-import ".length))).toMatchObject({
    source: "shop.example.com", platform: "shopify", rules: "health", priceAdjustPercent: 10, dryRun: true,
  });

  await sidebar.locator('a[href="/import/clean"]').click();
  await expect(page.getByRole("heading", { name: "Clean catalog" })).toBeVisible();
  await expect(page.getByText("Same words · 100% alike")).toBeVisible();
  // The better one is kept unless another is chosen.
  await page.getByRole("button", { name: "Keep DUP-B" }).click();
  await page.getByRole("button", { name: "Archive 1 duplicate(s)" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Archive duplicates" }).click();
  await expect(page.getByText("1 duplicate(s) archived. Undo brings them back.")).toBeVisible();
  expect(writes[1]).toBe('POST /api/catalog-tools/clean/apply {"groups":[{"key":"g1","keepId":"d2","retireIds":["d1"]}]}');
});

test("a menu group is pinned open with a pin, and unpinned with it", async ({ page }) => {
  await mockApi(page);
  await page.goto("/dashboard");
  const sidebar = page.locator(".MuiDrawer-paper").filter({ has: page.getByText("MP Seller Tools", { exact: true }) });
  const pin = sidebar.getByLabel("Keep Reports open");
  await expect(sidebar.locator('a[href="/reports/sales"]')).toHaveCount(0);
  await pin.check();
  await expect(sidebar.locator('a[href="/reports/sales"]')).toBeVisible();
  expect(writes).toEqual(['PUT /api/auth/me/pinned-menus {"menus":["reports"]}']);
});
