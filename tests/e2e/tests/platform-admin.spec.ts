import { test, expect } from "@playwright/test";
import { getPlatformAdminCredentials } from "./helpers";

// Requires the dev stack to be running (scripts/Start-Dev.ps1) — brief §13
// check #1's "open in Visual Studio and press F5" is a manual/GUI step this
// suite cannot drive directly, but exercising the resulting running
// application through a real browser is exactly what these tests do.
test.describe("PlatformAdmin console", () => {
  test("logs in and reaches the dashboard", async ({ page }) => {
    const { email, password } = getPlatformAdminCredentials();

    await page.goto("https://localhost:7100/login");
    await page.getByLabel("Email").fill(email);
    await page.getByLabel("Password").fill(password);
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page).toHaveURL(/\/dashboard$/);
    await expect(page.getByText("Platform Dashboard")).toBeVisible();
  });

  test("SPA deep link to /tenants works directly, without going through /login first", async ({ page, context }) => {
    const { email, password } = getPlatformAdminCredentials();

    // Establish a session first (deep-linking while anonymous should bounce
    // to /login, which is covered implicitly here too).
    await page.goto("https://localhost:7100/login");
    await page.getByLabel("Email").fill(email);
    await page.getByLabel("Password").fill(password);
    await page.getByRole("button", { name: "Sign in" }).click();
    await expect(page).toHaveURL(/\/dashboard$/);

    await page.goto("https://localhost:7100/tenants");

    await expect(page).toHaveURL(/\/tenants$/);
    await expect(page.getByText("Companies")).toBeVisible();
  });

  test("unauthenticated visitor is redirected to /login", async ({ page, context }) => {
    await context.clearCookies();
    await page.goto("https://localhost:7100/dashboard");

    await expect(page).toHaveURL(/\/login$/);
  });
});
