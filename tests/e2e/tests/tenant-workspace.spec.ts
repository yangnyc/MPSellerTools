import { test, expect } from "@playwright/test";
import { getTenantCredentials } from "./helpers";

// Requires scripts/Setup-Dev.ps1 to have seeded Company A (see README.md).
const company = getTenantCredentials("company-a");

test.describe("Company workspace — TenantAdmin", () => {
  test("logs in and sees the full menu", async ({ page }) => {
    await page.goto(`${company.url}/login`);
    await page.getByLabel("Email").fill(company.tenantAdmin.email);
    await page.getByLabel("Password", { exact: true }).fill(company.tenantAdmin.password);
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page).toHaveURL(/\/dashboard$/);
    // TenantAdmin-only menu items (brief §8) are all visible.
    await expect(page.getByRole("link", { name: "Users" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Settings" })).toBeVisible();
    await expect(page.getByRole("link", { name: "Audit" })).toBeVisible();
  });

  test("SPA deep link to /products works directly", async ({ page }) => {
    await page.goto(`${company.url}/login`);
    await page.getByLabel("Email").fill(company.tenantAdmin.email);
    await page.getByLabel("Password", { exact: true }).fill(company.tenantAdmin.password);
    await page.getByRole("button", { name: "Sign in" }).click();
    await expect(page).toHaveURL(/\/dashboard$/);

    await page.goto(`${company.url}/products`);

    await expect(page).toHaveURL(/\/products$/);
    await expect(page.getByRole("heading", { name: "Products" })).toBeVisible();
  });
});

test.describe("Company workspace — Employee", () => {
  test("logs in and does NOT see TenantAdmin-only menu items", async ({ page }) => {
    await page.goto(`${company.url}/login`);
    await page.getByLabel("Email").fill(company.employee.email);
    await page.getByLabel("Password", { exact: true }).fill(company.employee.password);
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page).toHaveURL(/\/dashboard$/);
    // brief §8: "Do not show tenant management, user management, or company
    // settings menus" to Employees.
    await expect(page.getByRole("link", { name: "Users" })).toHaveCount(0);
    await expect(page.getByRole("link", { name: "Settings" })).toHaveCount(0);
    await expect(page.getByRole("link", { name: "Audit" })).toHaveCount(0);
    // But their own dashboard/products/orders/tasks/profile remain.
    await expect(page.getByRole("link", { name: "Products" })).toBeVisible();
  });

  test("direct navigation to /users redirects away even though the menu item is hidden", async ({ page }) => {
    await page.goto(`${company.url}/login`);
    await page.getByLabel("Email").fill(company.employee.email);
    await page.getByLabel("Password", { exact: true }).fill(company.employee.password);
    await page.getByRole("button", { name: "Sign in" }).click();
    await expect(page).toHaveURL(/\/dashboard$/);

    await page.goto(`${company.url}/users`);

    // Client-side route guard bounces back to /dashboard (brief §5/§8 —
    // the real enforcement is server-side; this is the UX-level backstop).
    await expect(page).toHaveURL(/\/dashboard$/);
  });
});
