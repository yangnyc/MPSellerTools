import { test, expect, type Page } from "@playwright/test";

const me = { id: "admin", email: "admin@example.test", displayName: "Admin", roles: ["PlatformAdmin"] };

// Serves a signed-in profile with the given saved theme and records every
// theme the page saves back to it.
async function mockApi(page: Page, savedTheme: Record<string, unknown> | null) {
  const saves: Record<string, unknown>[] = [];
  page.on("pageerror", (error) => { throw error; });
  await page.route(/\/\/[^/]+\/api\//, async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === "/api/auth/me/theme" && request.method() === "PUT") {
      saves.push(request.postDataJSON());
      await route.fulfill({ json: { ...me, theme: request.postDataJSON() } });
    } else if (path === "/api/auth/me") {
      await route.fulfill({ json: { ...me, theme: savedTheme } });
    } else if (path === "/api/antiforgery/token") {
      await route.fulfill({ json: { token: "test" } });
    } else {
      await route.fulfill({ json: [] });
    }
  });
  await page.goto("/tenants");
  await expect(page.getByRole("heading", { name: "Companies" })).toBeVisible();
  return saves;
}

test("picking a theme in display settings restyles the app and saves its name to the profile", async ({ page }) => {
  const saves = await mockApi(page, null);
  const body = page.locator("body");
  await expect(body).toHaveCSS("background-color", "rgb(248, 250, 252)");

  await page.getByLabel("Display settings").click();
  await expect(page.getByRole("button", { name: /Ocean/ })).toHaveAttribute("aria-pressed", "true");
  await page.getByRole("button", { name: /Noir Gold/ }).click();

  await expect(page.getByRole("button", { name: /Noir Gold/ })).toHaveAttribute("aria-pressed", "true");
  await expect(body).toHaveCSS("background-color", "rgb(10, 10, 10)");
  // The active nav item is gold with dark text on it.
  const activeIcon = page.locator('.MuiDrawer-paper a[href="/tenants"] .MuiIcon-root');
  await expect(activeIcon).toHaveCSS("color", "rgb(0, 0, 0)");

  await expect.poll(() => saves.at(-1)?.themeName).toBe("noir");
  expect(saves.at(-1)).toMatchObject({ themeName: "noir", darkMode: true, sidenavColor: "gold" });
});

test("a theme name saved on the profile is applied on load, and one saved without a name is the default", async ({ page }) => {
  await mockApi(page, {
    themeName: "noir", darkMode: true, whiteSidenav: false, sidenavTint: null, sidenavColor: "gold", fixedNavbar: true,
  });
  await expect(page.locator("body")).toHaveCSS("background-color", "rgb(10, 10, 10)");
  await page.getByLabel("Display settings").click();
  await expect(page.getByRole("button", { name: /Noir Gold/ })).toHaveAttribute("aria-pressed", "true");

  const saves = await mockApi(page, {
    darkMode: false, whiteSidenav: false, sidenavTint: null, sidenavColor: "steel", fixedNavbar: true,
  });
  await expect(page.locator("body")).toHaveCSS("background-color", "rgb(248, 250, 252)");
  await page.getByLabel("Display settings").click();
  await expect(page.getByRole("button", { name: /Ocean/ })).toHaveAttribute("aria-pressed", "true");
  // Nothing changed, so nothing is written back.
  await page.waitForTimeout(800);
  expect(saves).toHaveLength(0);
});
