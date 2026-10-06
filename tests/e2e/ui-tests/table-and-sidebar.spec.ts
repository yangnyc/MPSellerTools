import { test, expect } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  page.on("pageerror", (error) => { throw error; });
  await page.route(/\/\/[^/]+\/api\//, async (route) => {
    const path = new URL(route.request().url()).pathname;
    const me = { id: "admin", email: "admin@example.test", displayName: "Admin", roles: ["PlatformAdmin"] };
    if (path === "/api/auth/me/theme" && route.request().method() === "PUT") {
      // Like the host, answer a saved theme with the profile that now holds it;
      // any other answer makes the page fall back to the default theme.
      await route.fulfill({ json: { ...me, theme: route.request().postDataJSON() } });
    } else if (path.startsWith("/api/auth/me/theme/")) {
      await route.fulfill({ status: 204 });
    } else if (path === "/api/auth/me") {
      await route.fulfill({ json: me });
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

test("entries per page changes how many rows show, up to all of them", async ({ page }) => {
  const table = page.locator(".MuiTableContainer-root");
  const perPage = table.getByLabel("Entries per page");
  await expect(table.locator("tbody tr")).toHaveCount(10);

  await perPage.selectOption("5");
  await expect(page.getByText("Showing 1 to 5 of 12 entries")).toBeVisible();
  await expect(table.locator("tbody tr")).toHaveCount(5);
  await page.getByText("chevron_right", { exact: true }).click();
  await expect(page.getByText("Showing 6 to 10 of 12 entries")).toBeVisible();

  // Fewer pages than the one being viewed: back to the first.
  await perPage.selectOption("25");
  await expect(page.getByText("Showing 1 to 12 of 12 entries")).toBeVisible();

  await perPage.selectOption("5");
  await perPage.selectOption({ label: "All" });
  await expect(page.getByText("Showing 1 to 12 of 12 entries")).toBeVisible();
  await expect(table.locator("tbody tr")).toHaveCount(12);
  await expect(page.getByText("chevron_right", { exact: true })).toHaveCount(0);
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
