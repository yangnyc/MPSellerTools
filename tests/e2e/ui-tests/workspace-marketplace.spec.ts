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
test("a product's description, barcode and image are entered from the catalog, and its category is mapped per marketplace", async ({ page }) => {
  const product = { id: "p1", sku: "MUG-BLUE", name: "Blue mug", price: 12, stockQuantity: 4, isArchived: false, rowVersion: "AAAA" };
  const detail = { id: "p1", sku: "MUG-BLUE", name: "Blue mug", brand: null, description: null, category: null, variants: [], identifiers: [], media: [] };
  const described = { ...detail, brand: "Mugco", category: "Mugs", description: "A blue mug." };
  await mockApi(
    page,
    { "/api/products": [product], "/api/catalog/products/p1": detail, "/api/channels": [account({})] },
    {
      "/api/catalog/products/p1/content": described,
      "/api/catalog/products/p1/identifiers": { ...described, identifiers: [{ id: "i1", type: 1, value: "000012345678", variantId: null }] },
      "/api/catalog/products/p1/media": { ...described, media: [{ id: "m1", url: "https://img.example.com/mug.jpg", altText: null, variantId: null, purpose: 0, position: 0 }] },
    }
  );
  await page.goto("/products");
  await page.getByRole("button", { name: "Details of Blue mug" }).click();
  const dialog = page.getByRole("dialog");

  await dialog.getByLabel("Brand").fill("Mugco");
  await dialog.getByLabel("Category").fill("Mugs");
  await dialog.getByLabel("Description").fill("A blue mug.");
  await dialog.getByRole("button", { name: "Save details" }).click();
  await expect(page.getByText("Details saved.")).toBeVisible();

  await expect(dialog.getByRole("button", { name: "Save identifiers" })).toBeDisabled();
  await dialog.getByLabel("UPC").fill("000012345678");
  await dialog.getByRole("button", { name: "Save identifiers" }).click();
  await expect(page.getByText("Identifiers saved.")).toBeVisible();

  await dialog.getByLabel("Image address").fill("https://img.example.com/mug.jpg");
  await dialog.getByRole("button", { name: "Add", exact: true }).click();
  await expect(dialog.getByText("Main")).toBeVisible();

  expect(writes).toEqual([
    'PUT /api/catalog/products/p1/content {"brand":"Mugco","description":"A blue mug.","category":"Mugs"}',
    'PUT /api/catalog/products/p1/identifiers {"type":1,"value":"000012345678"}',
    'POST /api/catalog/products/p1/media {"url":"https://img.example.com/mug.jpg","purpose":0,"position":0}',
  ]);

  await page.goto("/amazon/settings");
  await expect(page.getByText("No categories mapped yet.")).toBeVisible();
  await page.getByLabel("Your category").fill("Mugs");
  await page.getByLabel("Amazon product type").fill("DRINKING_CUP");
  await page.getByRole("button", { name: "Save category" }).click();
  await expect(page.getByText("Category saved.")).toBeVisible();
  expect(writes.at(-1)).toBe('PUT /api/channels/category-mappings {"channelMarketId":"m-amazon","internalCategory":"Mugs","externalCategoryId":"DRINKING_CUP"}');
});
test("a listing's own price, cap, title and ASIN are edited on its marketplace page, with a check of what is still missing", async ({ page }) => {
  const draft = {
    ...listing, desiredState: 0, observedStatus: 0, externalCategoryId: null, contentOverrides: {}, priceOverride: null, fulfillmentMode: 0, quantityCap: null,
    references: {}, hasPriceConflict: false, observedPrice: null,
    availableImages: [{ id: "img-1", url: "https://img.example.com/front.jpg" }, { id: "img-2", url: "https://img.example.com/back.jpg" }, { id: "img-3", url: "https://img.example.com/box.jpg" }],
    imageIds: null, effectiveImageUrls: [],
    imageRules: { minImages: 0, maxImages: 2, mainImage: "The main picture is on white.", formats: "JPEG or PNG.", size: "1000 px or more.", source: "the test" },
  };
  await mockApi(
    page,
    { "/api/channels": [account({})], "/api/channel-listings": [draft] },
    {
      "/api/channel-listings": draft,
      "/api/channel-listings/l1/validate": { valid: false, issues: [{ channel: 1, path: "category", code: "required", message: "Map the product's category to an Amazon product type." }] },
      "/api/channel-listings/l1/preview": { operation: 0, liveWrites: false, issues: [], requests: [{ method: "PUT", url: "https://sandbox.example/listings/MUG-BLUE", body: { productType: "DRINKING_CUP" } }] },
    }
  );
  await page.goto("/amazon");
  await page.getByRole("button", { name: "Edit MUG-BLUE" }).click();
  const dialog = page.getByRole("dialog");

  await dialog.getByLabel("Price on Amazon").fill("-1");
  await dialog.getByRole("button", { name: "Save", exact: true }).click();
  await expect(dialog.getByText("The price is a number, 0 or more.")).toBeVisible();
  expect(writes).toEqual([]);

  await dialog.getByLabel("Title on Amazon").fill("Blue mug, 12 oz");
  await dialog.getByLabel("Price on Amazon").fill("14.5");
  await dialog.getByLabel("Quantity cap").fill("3");
  await dialog.getByLabel("ASIN").fill("B00TEST001");
  await dialog.getByRole("button", { name: "Check" }).click();
  await expect(dialog.getByText("Map the product's category to an Amazon product type.")).toBeVisible();
  expect(writes).toEqual([
    'PUT /api/channel-listings {"channelMarketId":"m-amazon","variantId":"v1","sellerSku":"MUG-BLUE","fulfillmentMode":0,"externalCategoryId":null,"content":{"title":{"value":"Blue mug, 12 oz"}},"priceOverride":14.5,"quantityCap":3,"existingCatalogItemId":"B00TEST001","imageIds":[]}',
    "POST /api/channel-listings/l1/validate",
  ]);

  // Pictures: the marketplace's own limit is shown, and three is one too many until one is left out.
  await expect(dialog.getByText("Up to 2 pictures. The main picture is on white. JPEG or PNG. 1000 px or more.")).toBeVisible();
  await expect(dialog.getByText("3 pictures would be sent and Amazon takes 2.")).toBeVisible();
  await dialog.getByLabel("Send all of the product's pictures, in the product's order").uncheck();
  await dialog.getByLabel("Send https://img.example.com/front.jpg").uncheck();
  await expect(dialog.getByText("3 pictures would be sent")).toHaveCount(0);
  // The back of the product becomes the main picture; then the box is moved ahead of it.
  await dialog.getByRole("button", { name: "Move picture 2 up" }).click();
  writes = [];
  await dialog.getByRole("button", { name: "Check" }).click();
  await expect.poll(() => writes.length).toBe(2);
  expect(writes[0]).toContain('"imageIds":["img-3","img-2"]');

  await dialog.getByRole("button", { name: "Preview" }).click();
  await expect(dialog.getByText("Live writes are off: publishing builds this and sends nothing to Amazon.")).toBeVisible();
  await expect(dialog.getByText("PUT https://sandbox.example/listings/MUG-BLUE")).toBeVisible();
  await expect(dialog.getByText('"productType": "DRINKING_CUP"')).toBeVisible();
});
test("a product's own page shows it on every marketplace, and adds, edits and publishes from there", async ({ page }) => {
  const detail = {
    id: "p1", sku: "MUG-BLUE", name: "Blue mug", brand: "Mugco", description: "A blue mug.", category: "Mugs",
    variants: [{ id: "v1", sku: "MUG-BLUE", name: null, price: 12.5, isDefault: true, isArchived: false, onHand: 9, reserved: 1, safetyStock: 2, availableToSell: 6 }],
    identifiers: [{ id: "i1", type: 1, value: "000012345678", variantId: null }],
    media: [{ id: "img-1", url: "https://img.example.com/front.jpg", altText: null, variantId: null, purpose: 0, position: 0 }],
  };
  const onAmazon = {
    ...listing, productId: "p1", desiredState: 0, observedStatus: 0, externalCategoryId: null, contentOverrides: {}, priceOverride: 14.5, fulfillmentMode: 0, quantityCap: 3,
    effectivePrice: 14.5, effectiveQuantity: 3, references: { CatalogItem: "B00TEST001" }, hasPriceConflict: false, observedPrice: null, observedAtUtc: null,
    issues: [{ channel: 1, path: "category", code: "required", message: "Map the product's category to an Amazon product type." }],
    availableImages: [{ id: "img-1", url: "https://img.example.com/front.jpg" }], imageIds: null, effectiveImageUrls: ["https://img.example.com/front.jpg"],
    imageRules: { minImages: 0, maxImages: 9, mainImage: "", formats: "", size: "", source: "" },
  };
  const walmart = account({ id: "a-walmart", channel: 2, name: "Walmart", markets: [{ id: "m-walmart", marketplaceCode: "WALMART_US", language: "en-US", currency: "USD" }] });
  await mockApi(
    page,
    { "/api/products": [{ id: "p1", sku: "MUG-BLUE", name: "Blue mug", price: 12.5, stockQuantity: 9, isArchived: false, rowVersion: "A" }], "/api/catalog/products/p1": detail, "/api/channel-listings": [onAmazon], "/api/channels": [account({}), walmart] },
    { "/api/channel-listings/l1/publish": { listingId: "l1", desiredState: 1, liveWrites: false } }
  );

  // Reached from the product's name in the catalog.
  await page.goto("/products");
  await page.getByRole("link", { name: "Blue mug" }).click();
  await expect(page).toHaveURL(/\/products\/p1$/);
  await expect(page.getByRole("heading", { name: "Blue mug", level: 1 })).toBeVisible();
  await expect(page.getByText("A blue mug.")).toBeVisible();
  await expect(page.getByText("000012345678")).toBeVisible();
  await expect(page.getByRole("img", { name: "Blue mug, picture 1" })).toBeVisible();

  // Amazon: the draft with its own price and cap, its ASIN, and what still stops it being published.
  await expect(page.getByText("$14.50 (its own)")).toBeVisible();
  await expect(page.getByText("3 (capped at 3)")).toBeVisible();
  await expect(page.getByText("B00TEST001")).toBeVisible();
  await expect(page.getByText("Map the product's category to an Amazon product type.")).toBeVisible();
  await page.getByRole("button", { name: "Publish on Amazon" }).click();
  await expect(page.getByText("Queued as a dry run: live writes to Amazon are off.")).toBeVisible();

  // Walmart is set up but does not have the product; eBay is not set up at all.
  await page.getByRole("button", { name: "Add to Walmart" }).click();
  await expect(page.getByText("Added to Walmart as a draft.")).toBeVisible();
  await expect(page.getByRole("link", { name: "Set up eBay" })).toHaveAttribute("href", "/ebay/products");
  expect(writes).toEqual(["POST /api/channel-listings/l1/publish", 'PUT /api/channel-listings {"channelMarketId":"m-walmart","variantId":"v1","fulfillmentMode":0}']);

  // The same dialog as on the marketplace's own page.
  await page.getByRole("button", { name: "Edit on Amazon" }).click();
  await expect(page.getByRole("dialog").getByLabel("Price on Amazon")).toHaveValue("14.5");
});
test("Magento has its own menu, led by the connection page, where the store's address and token are saved and tried out", async ({ page }) => {
  const magento = account({
    id: "a-magento", channel: 4, name: "Magento", sellerId: null, hasCredentials: true,
    markets: [{ id: "m-magento", marketplaceCode: "default", language: "en-US", currency: "USD" }],
  });
  await mockApi(
    page,
    { "/api/channels": [magento], "/api/listings": { listings: [], connected: true, lastSyncedAtUtc: null } },
    {
      "/api/channels/a-magento": { ...magento, settings: { baseUrl: "https://shop.example.test" } },
      "/api/magento/test": { storeAddress: "https://shop.example.test/", storeViews: ["default", "de"], currency: "USD" },
      "/api/magento/import/listings": { created: 2, listings: 5 },
    },
  );
  await page.goto("/magento/connection");

  // Connection first, then its products, adding one, and what the store itself reports.
  // The group is open, as the page shown is one of its own.
  const sidebar = page.locator(".MuiDrawer-paper").filter({ has: page.getByText("MP Seller Tools", { exact: true }) });
  const links = sidebar.locator('a[href^="/magento"]');
  await expect(links).toHaveCount(4);
  expect(await links.evaluateAll((items) => items.map((item) => item.getAttribute("href")))).toEqual([
    "/magento/connection", "/magento", "/magento/add-product", "/magento/listings",
  ]);
  await expect(sidebar.locator('a[href="/magento/connection"]')).toHaveText(/Connection/);

  await expect(page.getByRole("heading", { name: "Magento connection" })).toBeVisible();
  // A store is one site: there is no sandbox to choose.
  await expect(page.getByLabel("Environment")).toHaveCount(0);
  await page.getByLabel("Store address").fill("https://shop.example.test");
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.getByText("Magento connection saved.")).toBeVisible();

  await page.getByLabel("Integration access token").fill("token-from-magento");
  await page.getByRole("button", { name: "Save credentials" }).click();
  await expect(page.getByText("Magento credentials saved.")).toBeVisible();

  await page.getByRole("button", { name: "Test connection" }).click();
  await expect(page.getByText("Connected to https://shop.example.test/. Store views: default, de.")).toBeVisible();

  // Its listings are read from the store on request.
  await sidebar.locator('a[href="/magento/listings"]').click();
  await expect(page.getByRole("heading", { name: "Magento listings" })).toBeVisible();
  await page.getByRole("button", { name: "Refresh from Magento" }).first().click();
  await expect(page.getByText("Read from Magento: 5 in the store, 2 new to your catalog.")).toBeVisible();

  expect(writes).toEqual([
    'PUT /api/channels/a-magento {"name":"Magento","environment":0,"sellerId":null,"settings":{"baseUrl":"https://shop.example.test"},"isEnabled":true,"liveWritesEnabled":false,"inventorySyncEnabled":false,"orderImportEnabled":false,"priceConflictPolicy":2,"channel":4}',
    'PUT /api/channels/a-magento/credentials {"credentials":{"accessToken":"token-from-magento"}}',
    "POST /api/magento/test",
    "POST /api/magento/import/listings",
  ]);
});

test("a listing that was published but never reached the marketplace can be sent again from the product's page", async ({ page }) => {
  const detail = {
    id: "p1", sku: "MUG-BLUE", name: "Blue mug", brand: "Mugco", description: "A blue mug.", category: "Mugs",
    variants: [{ id: "v1", sku: "MUG-BLUE", name: null, price: 12.5, isDefault: true, isArchived: false, onHand: 9, reserved: 1, safetyStock: 2, availableToSell: 6 }],
    identifiers: [], media: [],
  };
  // Published while live writes were off: wanted on sale, and the marketplace has never been told.
  const unsent = {
    ...listing, productId: "p1", desiredState: 1, observedStatus: 0, externalCategoryId: null, contentOverrides: {}, priceOverride: null, fulfillmentMode: 0, quantityCap: null,
    references: {}, hasPriceConflict: false, observedPrice: null, observedAtUtc: null, issues: null,
    availableImages: [], imageIds: null, effectiveImageUrls: [],
    imageRules: { minImages: 0, maxImages: 9, mainImage: "", formats: "", size: "", source: "" },
  };
  await mockApi(
    page,
    { "/api/catalog/products/p1": detail, "/api/channel-listings": [unsent], "/api/channels": [account({ liveWritesEnabled: true, effectiveLiveWrites: true })] },
    { "/api/channel-listings/l1/retry": { listingId: "l1", desiredState: 1, liveWrites: true } }
  );
  await page.goto("/products/p1");

  await expect(page.getByText("Not sent")).toBeVisible();
  // There is nothing to publish again, so without this the page offered no way to send it.
  await expect(page.getByRole("button", { name: "Publish on Amazon" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Take off sale on Amazon" })).toBeVisible();
  await page.getByRole("button", { name: "Send to Amazon again" }).click();
  await expect(page.getByText("Queued for Amazon again.")).toBeVisible();
  expect(writes).toEqual(["POST /api/channel-listings/l1/retry"]);
});
