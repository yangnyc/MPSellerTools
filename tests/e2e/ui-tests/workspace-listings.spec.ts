import { test, expect, type Page } from "@playwright/test";

const admin = { id: "u1", email: "ada@acme.test", displayName: "Ada Admin", roles: ["TenantAdmin"], isBlocked: false, pinnedMenus: [] };
const employee = { ...admin, id: "u2", email: "eve@acme.test", displayName: "Eve Employee", roles: ["Employee"] };

const listing = (over: object) => ({
  id: "l1", productId: "p1", productSku: "EB-LAMP", productName: "Desk lamp", channel: 0, externalId: "110551234567",
  marketplace: "EBAY_US", url: "https://www.ebay.com/itm/110551234567", status: 0, price: 19.5, currency: "USD",
  availableQuantity: 7, soldQuantity: 2, lastSyncedAtUtc: "2026-10-01T00:00:00Z", ...over,
});

const posted = {
  connected: true,
  lastSyncedAtUtc: "2026-10-01T00:00:00Z",
  listings: [
    listing({}),
    listing({ id: "l2", productSku: "EB-MUG", productName: "Blue mug", externalId: "110559999999", marketplace: "EBAY_GB",
      url: "https://www.ebay.com/itm/110559999999", status: 1, price: 8, currency: "GBP", availableQuantity: 0, soldQuantity: 5 }),
    listing({ id: "l3", productSku: "EB-OLD", productName: "Old clock", externalId: "110550000001", status: 2, availableQuantity: null, soldQuantity: null }),
  ],
};

// Requests the page makes that change something, as "METHOD path".
let writes: string[];

async function mockApi(page: Page, user: object, listings: object) {
  writes = [];
  page.on("pageerror", (error) => { throw error; });
  await page.route(/\/\/[^/]+\/api\//, async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() !== "GET") {
      writes.push(`${request.method()} ${path}`);
      await route.fulfill({ json: { created: 0, updated: 1, listings: 3 } });
    } else if (path === "/api/auth/me") {
      await route.fulfill({ json: user });
    } else if (path === "/api/antiforgery/token") {
      await route.fulfill({ json: { token: "test" } });
    } else if (path === "/api/listings") {
      await route.fulfill({ json: listings });
    } else {
      await route.fulfill({ json: [] });
    }
  });
}

test("Listings shows what is posted, where, and links to the item on the site", async ({ page }) => {
  await mockApi(page, admin, posted);
  await page.goto("/dashboard");
  const sidebar = page.locator(".MuiDrawer-paper").filter({ has: page.getByText("MP Seller Tools", { exact: true }) });
  // Listings sits in the Inventory group, with Products and Stock.
  await sidebar.getByRole("button", { name: /Inventory/ }).click();
  await sidebar.locator('a[href="/listings"]').click();
  await expect(page.getByRole("heading", { name: "Listings" })).toBeVisible();

  const lamp = page.getByRole("row").filter({ hasText: "Desk lamp" });
  await expect(lamp).toContainText("eBay US");
  await expect(lamp).toContainText("$19.50");
  await expect(lamp).toContainText("Live");
  await expect(lamp.getByRole("link", { name: "Open Desk lamp on eBay US" })).toHaveAttribute("href", "https://www.ebay.com/itm/110551234567");
  // The price is shown in the currency the site asks it in.
  await expect(page.getByRole("row").filter({ hasText: "Blue mug" })).toContainText("£8.00");

  await page.getByRole("tab", { name: /Out of stock/ }).or(page.getByRole("button", { name: /Out of stock/ })).first().click();
  await expect(page.getByRole("row").filter({ hasText: "Blue mug" })).toBeVisible();
  await expect(page.getByRole("row").filter({ hasText: "Desk lamp" })).toHaveCount(0);

  // Refreshing is the eBay product import.
  await page.getByRole("button", { name: "Refresh from eBay" }).click();
  await expect(page.getByText("Read from eBay: 3 posted.")).toBeVisible();
  expect(writes).toEqual(["POST /api/ebay/import/products"]);
});

test("an employee sees the listings but cannot refresh them, and an empty list says why", async ({ page }) => {
  await mockApi(page, employee, posted);
  await page.goto("/listings");
  await expect(page.getByRole("row").filter({ hasText: "Desk lamp" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Refresh from eBay" })).toHaveCount(0);

  await mockApi(page, admin, { connected: false, lastSyncedAtUtc: null, listings: [] });
  await page.goto("/listings");
  await expect(page.getByText("Nothing posted yet")).toBeVisible();
  await expect(page.getByRole("link", { name: "Set up eBay" })).toHaveAttribute("href", "/ebay");
});
