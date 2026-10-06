import { test, expect, type Page } from "@playwright/test";

const admin = { id: "u1", email: "ada@acme.test", displayName: "Ada Admin", roles: ["TenantAdmin"], isBlocked: false, pinnedMenus: [] };

const account = (over: object) => ({
  id: "a-amazon", channel: 1, name: "Amazon", environment: 0, sellerId: "SELLER1", settings: null, hasCredentials: false, isEnabled: true,
  liveWritesEnabled: false, effectiveLiveWrites: false, inventorySyncEnabled: false, orderImportEnabled: false, priceConflictPolicy: 2,
  lastOrderImportAtUtc: null, lastError: null, markets: [{ id: "m-amazon", marketplaceCode: "ATVPDKIKX0DER", language: "en-US", currency: "USD" }],
  ...over,
});

const listing = {
  id: "l1", channelMarketId: "m-amazon", channelAccountId: "a-amazon", channel: 1, marketplaceCode: "ATVPDKIKX0DER", variantId: "v1",
  sellerSku: "MUG-BLUE", effectiveTitle: "Blue mug", effectivePrice: 12, effectiveQuantity: 4, desiredState: 1, observedStatus: 5, issues: null,
};

const job = (over: object) => ({
  id: "j1", channelAccountId: "a-amazon", channelListingId: "l1", operation: 0, status: 4, attempts: 6, maxAttempts: 6,
  nextAttemptAtUtc: "2026-10-01T00:00:00Z", externalSubmissionId: null, errorClass: 4, lastError: "Amazon refused the request (400): brand is missing",
  dryRun: false, createdAtUtc: "2026-10-01T00:00:00Z", completedAtUtc: "2026-10-01T00:05:00Z", attemptHistory: null, ...over,
});

const health = {
  undispatchedEvents: 0, pendingJobs: 0, runningJobs: 0, awaitingRemoteJobs: 0, failedJobs: 1, needsCorrectionJobs: 0, expiredLeases: 0, retriedJobs: 0,
  oldestPendingAtUtc: null, lastSuccessAtUtc: null, accounts: [],
};

const issue = {
  id: "i1", channelAccountId: "a-amazon", externalOrderId: "111-222", externalLineId: "1", sellerSku: "NO-SUCH-SKU", quantity: 2, reason: 0,
  orderId: null, createdAtUtc: "2026-10-01T00:00:00Z", resolvedAtUtc: null,
};

const stock = {
  accountingEnabled: false,
  items: [{ variantId: "v1", productId: "p1", sku: "MUG-BLUE", productName: "Blue mug", variantName: null, price: 12, onHand: 4, reserved: 1, safetyStock: 1, availableToSell: 2, updatedAtUtc: null }],
};

// Requests the page makes that change something, as "METHOD path body".
let writes: string[];

// Serves `gets` by path for reads and `posts` by path for writes; anything else reads as an empty list.
async function mockApi(page: Page, gets: Record<string, unknown>, posts: Record<string, unknown> = {}) {
  writes = [];
  page.on("pageerror", (error) => { throw error; });
  await page.route(/\/\/[^/]+\/api\//, async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (request.method() !== "GET") {
      writes.push(`${request.method()} ${path}${url.search} ${request.postData() ?? ""}`.trim());
      await route.fulfill({ json: posts[path] ?? {} });
    } else if (path === "/api/auth/me") {
      await route.fulfill({ json: admin });
    } else if (path === "/api/antiforgery/token") {
      await route.fulfill({ json: { token: "test" } });
    } else {
      await route.fulfill({ json: gets[path] ?? [] });
    }
  });
}

test("the sync queue shows what failed and why, retries it, and clears an order line that was sorted out", async ({ page }) => {
  await mockApi(
    page,
    {
      "/api/channels": [account({})],
      "/api/channel-listings": [listing],
      "/api/channels/sync/health": health,
      "/api/channels/sync/jobs": [job({}), job({ id: "j2", status: 3, lastError: null, errorClass: 0 })],
      "/api/channels/sync/jobs/j1": job({ attemptHistory: [{ number: 1, startedAtUtc: "2026-10-01T00:00:00Z", finishedAtUtc: "2026-10-01T00:00:01Z", outcome: 4, errorClass: 4, httpStatus: 400, externalRequestId: "req-9", detail: "brand is missing" }] }),
      "/api/channels/order-issues": [issue],
    },
    { "/api/channel-listings/l1/retry": { listingId: "l1", desiredState: 1, liveWrites: false } }
  );
  await page.goto("/sync");

  // Only what needs attention is listed to begin with.
  const failed = page.getByRole("row").filter({ hasText: "MUG-BLUE" });
  await expect(failed).toHaveCount(1);
  await expect(failed).toContainText("Amazon refused the request (400): brand is missing");
  await expect(failed).toContainText("6 of 6");

  await failed.getByRole("button", { name: "Details" }).click();
  await expect(page.getByText("HTTP 400")).toBeVisible();
  await expect(page.getByText("Refused for good by the marketplace")).toBeVisible();
  await page.getByRole("dialog").getByRole("button", { name: "Retry" }).click();
  await expect(page.getByText("Queued again as a dry run: live writes are off.")).toBeVisible();

  const line = page.getByRole("row").filter({ hasText: "NO-SUCH-SKU" });
  await expect(line).toContainText("Unknown SKU");
  await line.getByRole("button", { name: "Mark sorted out" }).click();
  await expect(page.getByText("Marked as sorted out.")).toBeVisible();
  expect(writes).toEqual(["POST /api/channel-listings/l1/retry", "POST /api/channels/order-issues/i1/resolve"]);

  await page.getByRole("button", { name: /^All/ }).click();
  await expect(page.getByRole("row").filter({ hasText: "Succeeded" })).toHaveCount(1);
});

test("inventory shows what is left to sell, takes a count, and says when orders do not move stock", async ({ page }) => {
  await mockApi(page, { "/api/catalog/inventory": stock });
  await page.goto("/inventory");

  await expect(page.getByText("Orders do not change stock yet")).toBeVisible();
  const row = page.getByRole("row").filter({ hasText: "MUG-BLUE" });
  await expect(row).toContainText("Blue mug");
  await row.getByRole("button", { name: "Count MUG-BLUE" }).click();
  await page.getByLabel("On hand").fill("9");
  await page.getByLabel("Safety stock").fill("");
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByText("On hand and safety stock are whole numbers, 0 or more.")).toBeVisible();
  await page.getByLabel("Safety stock").fill("2");
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByText("Stock of MUG-BLUE updated.")).toBeVisible();
  expect(writes).toEqual(['PUT /api/catalog/variants/v1/inventory {"onHand":9,"safetyStock":2}']);
});

test("a products file is checked first and only saved on confirming", async ({ page }) => {
  await mockApi(page, {}, {
    "/api/products/import": { dryRun: true, created: 1, updated: 1, unchanged: 0, errors: [{ row: 3, sku: "BAD", message: "Name is required and must be 200 characters or fewer." }] },
  });
  await page.goto("/products");

  const csv = 'SKU,Name,Price,Stock\nMUG-BLUE,"Blue mug, large",12.50,4\nMUG-RED,Red mug,$9,0\nBAD,,1,1\n';
  await page.getByLabel("Products CSV file").setInputFiles({ name: "products.csv", mimeType: "text/csv", buffer: Buffer.from(csv) });

  const dialog = page.getByRole("dialog");
  await expect(dialog.getByText("1 to add")).toBeVisible();
  await expect(dialog.getByText("Row 4 (BAD): Name is required and must be 200 characters or fewer.")).toBeVisible();
  await dialog.getByRole("button", { name: "Import" }).click();
  await expect(page.getByText("Import finished: 1 added, 1 updated.")).toBeVisible();

  const rows = '{"rows":[{"sku":"MUG-BLUE","name":"Blue mug, large","price":12.5,"stockQuantity":4},{"sku":"MUG-RED","name":"Red mug","price":9,"stockQuantity":0},{"sku":"BAD","name":"","price":1,"stockQuantity":1}]}';
  expect(writes).toEqual([`POST /api/products/import?dryRun=true ${rows}`, `POST /api/products/import?dryRun=false ${rows}`]);

  // A file whose numbers cannot be read is turned away before anything is sent.
  await page.getByLabel("Products CSV file").setInputFiles({ name: "bad.csv", mimeType: "text/csv", buffer: Buffer.from("sku,name,price,stock\nA,Thing,cheap,1\n") });
  await expect(page.getByText('Row 2: the price "cheap" is not a number.')).toBeVisible();
  expect(writes).toHaveLength(2);
});

test("a marketplace's settings save its switches and its credentials separately, and eBay sits in the same menu shape", async ({ page }) => {
  const saved = account({ liveWritesEnabled: true, orderImportEnabled: true });
  await mockApi(page, { "/api/channels": [account({})] }, { "/api/channels/a-amazon": saved });
  await page.goto("/amazon/settings");

  await expect(page.getByText("Dry run only")).toBeVisible();
  await page.getByLabel("Live writes").check();
  await page.getByLabel("Import orders", { exact: true }).check();
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.getByText("Amazon settings saved.")).toBeVisible();
  // On for the account but not for the workspace is still a dry run, and the page says so.
  await expect(page.getByText("Still a dry run")).toBeVisible();

  await expect(page.getByRole("button", { name: "Save credentials" })).toBeDisabled();
  await page.getByLabel("LWA client ID").fill("id");
  await page.getByLabel("LWA client secret").fill("secret");
  await page.getByLabel("Refresh token").fill("token");
  await page.getByRole("button", { name: "Save credentials" }).click();
  await expect(page.getByText("Amazon credentials saved.")).toBeVisible();
  await expect(page.getByLabel("LWA client secret")).toHaveValue("");

  expect(writes).toEqual([
    'PUT /api/channels/a-amazon {"name":"Amazon","environment":0,"sellerId":"SELLER1","settings":{},"isEnabled":true,"liveWritesEnabled":true,"inventorySyncEnabled":false,"orderImportEnabled":true,"priceConflictPolicy":2,"channel":1}',
    'PUT /api/channels/a-amazon/credentials {"credentials":{"clientId":"id","clientSecret":"secret","refreshToken":"token"}}',
  ]);

  // eBay: its connection page first, then the same three pages as the others.
  const sidebar = page.locator(".MuiDrawer-paper").filter({ has: page.getByText("MP Seller Tools", { exact: true }) });
  await sidebar.getByRole("button", { name: /eBay/ }).click();
  for (const href of ["/ebay", "/ebay/products", "/ebay/products/add-product", "/ebay/listings", "/ebay/products/settings"]) {
    await expect(sidebar.locator(`a[href="${href}"]`)).toBeVisible();
  }
  await sidebar.locator('a[href="/ebay/products/settings"]').click();
  // Only Amazon was added here, so eBay's own settings page offers to set it up.
  await expect(page.getByText("eBay is not set up yet")).toBeVisible();
});

test("each marketplace has its own listings page, showing only what that marketplace reported", async ({ page }) => {
  const posted = {
    id: "l1", productId: "p1", productSku: "MUG-BLUE", productName: "Blue mug", channel: 1, externalId: "B00TEST001", marketplace: "ATVPDKIKX0DER",
    url: "https://www.amazon.com/dp/B00TEST001", status: 0, price: 12, currency: "USD", availableQuantity: 4, soldQuantity: null, lastSyncedAtUtc: "2026-10-01T00:00:00Z",
  };
  const asked: string[] = [];
  page.on("request", (request) => { if (request.url().includes("/api/listings")) asked.push(new URL(request.url()).search); });
  await mockApi(page, { "/api/listings": { connected: true, lastSyncedAtUtc: "2026-10-01T00:00:00Z", listings: [posted] } });
  await page.goto("/amazon/listings");

  await expect(page.getByRole("heading", { name: "Amazon listings" })).toBeVisible();
  const row = page.getByRole("row").filter({ hasText: "Blue mug" });
  await expect(row).toContainText("Amazon US");
  await expect(row.getByRole("link", { name: "Open Blue mug on Amazon US" })).toHaveAttribute("href", "https://www.amazon.com/dp/B00TEST001");
  // Reading on request is eBay's alone.
  await expect(page.getByRole("button", { name: "Refresh from eBay" })).toHaveCount(0);
  expect(asked).toContain("?channel=1");

  await mockApi(page, { "/api/listings": { connected: false, lastSyncedAtUtc: null, listings: [] } });
  await page.goto("/walmart/listings");
  await expect(page.getByRole("heading", { name: "Walmart listings" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Set up Walmart" })).toHaveAttribute("href", "/walmart");
});