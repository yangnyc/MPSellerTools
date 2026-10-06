import { test, expect, type Page } from "@playwright/test";

const admin = { id: "u1", email: "ada@acme.test", displayName: "Ada Admin", roles: ["TenantAdmin"], isBlocked: false, pinnedMenus: [] };

const notSetUp = {
  configured: false, environment: 0, clientId: null, ruName: null, connected: false, connectedAtUtc: null, accessExpiresAtUtc: null,
  lastOrderSyncAtUtc: null, lastProductSyncAtUtc: null, lastSyncError: null, importedOrders: 0, callbackPath: "/ebay",
};
const configured = { ...notSetUp, configured: true, clientId: "App-123", ruName: "My-RuName" };
const connected = { ...configured, connected: true, connectedAtUtc: "2026-10-01T00:00:00Z" };

// Requests the page makes that change something, as "METHOD path body".
let writes: string[];

// Serves the eBay link in the given state; a write that returns the link returns `after`.
async function mockApi(page: Page, status: object, after: object = status) {
  writes = [];
  page.on("pageerror", (error) => { throw error; });
  await page.route(/\/\/[^/]+\/api\//, async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() !== "GET") {
      writes.push(`${request.method()} ${path} ${request.postData() ?? ""}`.trim());
      if (path === "/api/ebay/import/orders") {
        await route.fulfill({ json: { created: 3, updated: 1, productsCreated: 2 } });
      } else if (path === "/api/ebay/import/products") {
        await route.fulfill({ status: 502, json: { title: "Bad Gateway", detail: "eBay refused the request (401): Invalid access token" } });
      } else {
        status = after;
        await route.fulfill({ json: after });
      }
    } else if (path === "/api/auth/me") {
      await route.fulfill({ json: admin });
    } else if (path === "/api/antiforgery/token") {
      await route.fulfill({ json: { token: "test" } });
    } else if (path === "/api/ebay") {
      await route.fulfill({ json: status });
    } else {
      await route.fulfill({ json: [] });
    }
  });
}

test("saving the application keys sends them and shows where eBay must send the seller back", async ({ page }) => {
  await mockApi(page, notSetUp, configured);
  await page.goto("/ebay");
  await expect(page.getByText("Not set up")).toBeVisible();
  await expect(page.getByText(/^https?:\/\/[^/]+\/ebay$/)).toBeVisible();

  await page.getByLabel("Environment").selectOption({ label: "Production (live eBay)" });
  await page.getByLabel("App ID (Client ID)").fill("App-123");
  await page.getByLabel("Cert ID (Client Secret)").fill("cert-secret");
  await page.getByLabel("RuName (eBay Redirect URL name)").fill("My-RuName");
  await page.getByRole("button", { name: "Save keys" }).click();

  await expect(page.getByText("Not connected")).toBeVisible();
  expect(writes).toEqual(['PUT /api/ebay/settings {"environment":1,"clientId":"App-123","clientSecret":"cert-secret","ruName":"My-RuName"}']);
  // The Cert ID is not kept on the page once saved.
  await expect(page.getByLabel("Cert ID (Client Secret)")).toHaveValue("");
  await expect(page.getByRole("button", { name: "Connect eBay account" })).toBeEnabled();
});

test("coming back from eBay hands the address to the server and clears the code from the address bar", async ({ page }) => {
  await mockApi(page, configured, connected);
  await page.goto("/ebay?code=the-code&state=abc");

  await expect(page.getByText("Connected", { exact: true })).toBeVisible();
  expect(writes).toHaveLength(1);
  expect(writes[0]).toMatch(/^POST \/api\/ebay\/complete \{"codeOrUrl":"http[^"]+\/ebay\?code=the-code&state=abc"\}$/);
  expect(new URL(page.url()).search).toBe("");
});

test("imports report what came in, and eBay's own reason when it refuses", async ({ page }) => {
  await mockApi(page, connected);
  await page.goto("/ebay");

  await page.getByRole("button", { name: "Import orders" }).click();
  await expect(page.getByText("Orders imported: 3 new, 1 updated, 2 products added.")).toBeVisible();

  await page.getByRole("button", { name: "Import products" }).click();
  await expect(page.getByText("eBay refused the request (401): Invalid access token")).toBeVisible();
  expect(writes).toEqual(["POST /api/ebay/import/orders", "POST /api/ebay/import/products"]);
});
