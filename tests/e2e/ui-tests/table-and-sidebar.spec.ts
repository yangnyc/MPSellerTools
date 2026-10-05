import { test, expect } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  page.on("pageerror", (error) => { throw error; });
  await page.route(/\/\/[^/]+\/api\//, async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/auth/me") {
      await route.fulfill({ json: { id: "admin", email: "admin@example.test", displayName: "Admin", roles: ["PlatformAdmin"] } });
    } else if (path === "/api/tenants") {
      await route.fulfill({ json: Array.from({ length: 12 }, (_, i) => ({
        id: String(i), name: `Company ${i}`, slug: `company-${i}`, status: 1,
        url: null, createdAtUtc: "2026-01-01T00:00:00Z",
      })) });
    } else {
      await route.fulfill({ json: [] });
    }
  });
  await page.goto("/tenants");
  await expect(page.getByText("Showing 1 to 10 of 12 entries")).toBeVisible();
});

test("search stays controlled and row counts reflect filtered results", async ({ page }) => {
  const search = page.locator(".MuiTableContainer-root").getByPlaceholder("Search...");
  await search.fill("company-11");
  await expect(search).toHaveValue("company-11");
  await expect(page.getByText("Showing 1 to 1 of 1 entries")).toBeVisible();
  await search.fill("no-match");
  await expect(page.getByText("Showing 0 to 0 of 0 entries")).toBeVisible();
  await search.fill("");
  await expect(page.getByText("Showing 1 to 10 of 12 entries")).toBeVisible();
  await page.getByText("chevron_right", { exact: true }).click();
  await expect(page.getByText("Showing 11 to 12 of 12 entries")).toBeVisible();
});

test("sidebar icons follow text contrast and style survives resizing", async ({ page }) => {
  const sidebar = page.locator(".MuiDrawer-paper").filter({ has: page.getByText("MP Seller Tools", { exact: true }) });
  const dashboardIcon = sidebar.locator('a[href="/dashboard"] .MuiIcon-root');
  await expect(dashboardIcon).toHaveCSS("color", "rgb(255, 255, 255)");
  await page.getByText("settings", { exact: true }).last().click();
  await page.getByTitle("White", { exact: true }).click();
  await expect(sidebar).toHaveCSS("background-color", "rgb(255, 255, 255)");
  const selectedIcon = sidebar.locator('a[href="/tenants"] .MuiIcon-root');
  await expect(selectedIcon).toHaveCSS("color", "rgb(255, 255, 255)");
  await expect(dashboardIcon).not.toHaveCSS("color", "rgb(255, 255, 255)");
  await page.setViewportSize({ width: 800, height: 1000 });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await expect(sidebar).toHaveCSS("background-color", "rgb(255, 255, 255)");
});
