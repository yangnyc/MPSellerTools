import { test, expect, type Page } from "@playwright/test";

const me = { id: "admin", email: "admin@example.test", displayName: "Admin", roles: ["PlatformAdmin"] };

// Serves a signed-in profile with the given saved theme and records every
// theme the page saves back to it. Like the hosts, it keeps the last settings
// saved for each look (a theme in light or in dark mode) and serves them when
// the page switches to it.
async function mockApi(page: Page, savedTheme: Record<string, unknown> | null) {
  const saves: Record<string, unknown>[] = [];
  const looks = new Map<string, Record<string, unknown>>();
  const lastDark = new Map<string, boolean>();
  const remember = (theme: Record<string, unknown> | null) => {
    if (typeof theme?.themeName === "string") {
      looks.set(`${theme.themeName}:${theme.darkMode}`, theme);
      lastDark.set(theme.themeName, Boolean(theme.darkMode));
    }
  };
  remember(savedTheme);
  page.on("pageerror", (error) => { throw error; });
  await page.route(/\/\/[^/]+\/api\//, async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === "/api/auth/me/theme" && request.method() === "PUT") {
      saves.push(request.postDataJSON());
      remember(request.postDataJSON());
      await route.fulfill({ json: { ...me, theme: request.postDataJSON() } });
    } else if (path.startsWith("/api/auth/me/theme/")) {
      const themeName = path.slice("/api/auth/me/theme/".length);
      const dark = new URL(request.url()).searchParams.get("dark") ?? String(lastDark.get(themeName));
      const stored = looks.get(`${themeName}:${dark}`);
      await (stored ? route.fulfill({ json: stored }) : route.fulfill({ status: 204 }));
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

// The theme dropdown in the Display Settings panel.
const themePicker = (page: Page) => page.getByLabel("Theme", { exact: true });

test("picking a theme in display settings restyles the app and saves its name to the profile", async ({ page }) => {
  const saves = await mockApi(page, null);
  const body = page.locator("body");
  await expect(body).toHaveCSS("background-color", "rgb(248, 247, 250)");

  await page.getByLabel("Display settings").click();
  await expect(themePicker(page)).toHaveValue("ocean");
  await themePicker(page).selectOption({ label: "Navy Amber" });

  await expect(themePicker(page)).toHaveValue("stepwise");
  await expect(body).toHaveCSS("background-color", "rgb(8, 23, 41)");
  // The active nav item is amber with dark text on it.
  const activeIcon = page.locator('.MuiDrawer-paper a[href="/tenants"] .MuiIcon-root');
  await expect(activeIcon).toHaveCSS("color", "rgb(0, 0, 0)");

  await expect.poll(() => saves.at(-1)?.themeName).toBe("stepwise");
  expect(saves.at(-1)).toMatchObject({ themeName: "stepwise", darkMode: true, sidenavColor: "gold" });
});

test("the Matrix theme is offered and restyles the app in green on black", async ({ page }) => {
  const saves = await mockApi(page, null);
  await page.getByLabel("Display settings").click();
  await expect(themePicker(page).locator("option")).toHaveText(["Default", "Navy Amber", "Matrix"]);
  await themePicker(page).selectOption({ label: "Matrix" });

  await expect(page.locator("body")).toHaveCSS("background-color", "rgb(0, 0, 0)");
  // The open dropdown list is drawn in the theme's colours, not the browser's white.
  const option = themePicker(page).locator("option").first();
  await expect(option).toHaveCSS("background-color", "rgb(10, 15, 10)");
  await expect(option).toHaveCSS("color", "rgb(0, 255, 65)");
  await expect.poll(() => saves.at(-1)).toMatchObject({ themeName: "matrix", darkMode: true, sidenavColor: "mint" });
});
test("a theme name saved on the profile is applied on load, and one saved without a name is the default", async ({ page }) => {
  await mockApi(page, {
    themeName: "stepwise", darkMode: true, whiteSidenav: false, sidenavTint: null, sidenavColor: "gold", fixedNavbar: true,
  });
  await expect(page.locator("body")).toHaveCSS("background-color", "rgb(8, 23, 41)");
  await page.getByLabel("Display settings").click();
  await expect(themePicker(page)).toHaveValue("stepwise");

  const saves = await mockApi(page, {
    darkMode: false, whiteSidenav: false, sidenavTint: null, sidenavColor: "steel", fixedNavbar: true,
  });
  await expect(page.locator("body")).toHaveCSS("background-color", "rgb(248, 247, 250)");
  await page.getByLabel("Display settings").click();
  await expect(themePicker(page)).toHaveValue("ocean");
  // Nothing changed, so nothing is written back.
  await page.waitForTimeout(800);
  expect(saves).toHaveLength(0);
});

test("switching themes writes the old theme's settings and reads the new theme's back", async ({ page }) => {
  const saves = await mockApi(page, null);
  await page.getByLabel("Display settings").click();

  // Customise Default, then leave it before the debounced save has gone out.
  await page.getByTitle("teal").first().click();
  await themePicker(page).selectOption({ label: "Navy Amber" });
  await expect.poll(() => saves.at(-1)?.themeName).toBe("stepwise");
  expect(saves.find((save) => save.themeName === "ocean")).toMatchObject({ sidenavColor: "teal" });
  // Navy Amber was never used, so it starts from its preset.
  expect(saves.at(-1)).toMatchObject({ darkMode: true, sidenavColor: "gold" });

  // Customise Navy Amber, switch away and back: each theme returns as it was left.
  await page.getByTitle("amber").first().click();
  await themePicker(page).selectOption({ label: "Default" });
  await expect.poll(() => saves.at(-1)).toMatchObject({ themeName: "ocean", darkMode: false, sidenavColor: "teal" });
  expect(saves.findLast((save) => save.themeName === "stepwise")).toMatchObject({ sidenavColor: "amber" });

  await themePicker(page).selectOption({ label: "Navy Amber" });
  await expect.poll(() => saves.at(-1)).toMatchObject({ themeName: "stepwise", darkMode: true, sidenavColor: "amber" });
});

test("light and dark each keep their own colours within a theme", async ({ page }) => {
  const saves = await mockApi(page, null);
  await page.getByLabel("Display settings").click();
  const modeSwitch = page.getByRole("switch").last();

  // Pick teal in light mode, then go dark before the debounced save has gone out.
  await page.getByTitle("teal").first().click();
  await modeSwitch.click();
  await expect.poll(() => saves.at(-1)).toMatchObject({ themeName: "ocean", darkMode: true });
  expect(saves.find((save) => save.darkMode === false)).toMatchObject({ themeName: "ocean", sidenavColor: "teal" });
  // Dark was never used, so it starts with the colours carried over from light.
  expect(saves.at(-1)).toMatchObject({ sidenavColor: "teal" });

  // Pick amber in dark mode, then go back and forth: each mode returns as it was left.
  await page.getByTitle("amber").first().click();
  await modeSwitch.click();
  await expect.poll(() => saves.at(-1)).toMatchObject({ darkMode: false, sidenavColor: "teal" });
  expect(saves.findLast((save) => save.darkMode === true)).toMatchObject({ sidenavColor: "amber" });

  await modeSwitch.click();
  await expect.poll(() => saves.at(-1)).toMatchObject({ darkMode: true, sidenavColor: "amber" });
});