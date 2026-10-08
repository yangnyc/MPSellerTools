import { test, expect, type Page } from "@playwright/test";

const me = { id: "admin", email: "admin@example.test", displayName: "Admin", roles: ["PlatformAdmin"] };

// Serves a signed-in profile with the given saved theme and records every
// theme the page saves back to it. Like the hosts, it keeps the last settings
// saved for each look (a theme in light or in dark mode) and serves them when
// the page switches to it. `otherLooks` are looks saved earlier, besides the
// one the profile was left in.
async function mockApi(
  page: Page,
  savedTheme: Record<string, unknown> | null,
  otherLooks: Record<string, unknown>[] = [],
) {
  const saves: Record<string, unknown>[] = [];
  const looks = new Map<string, Record<string, unknown>>();
  const lastDark = new Map<string, boolean>();
  const remember = (theme: Record<string, unknown> | null) => {
    if (typeof theme?.themeName === "string") {
      looks.set(`${theme.themeName}:${theme.darkMode}`, theme);
      lastDark.set(theme.themeName, Boolean(theme.darkMode));
    }
  };
  otherLooks.forEach(remember);
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
  await expect(body).toHaveCSS("background-color", "rgb(246, 249, 251)");

  await page.getByLabel("Display settings").click();
  await expect(themePicker(page)).toHaveValue("ocean");
  await expect(themePicker(page).locator("option")).toHaveText(["Default", "Sapphire Ash", "Astro Novalite"]);
  await themePicker(page).selectOption({ label: "Sapphire Ash" });

  await expect(themePicker(page)).toHaveValue("sapphire");
  await expect(body).toHaveCSS("background-color", "rgb(245, 245, 245)");
  // The sidenav is sapphire; its active item is rose with dark text on it.
  await expect(page.locator(".MuiDrawer-paper").first()).toHaveCSS("background-image", /rgb\(53, 98, 122\)/);
  const activeIcon = page.locator('.MuiDrawer-paper a[href="/tenants"] .MuiIcon-root');
  await expect(activeIcon).toHaveCSS("color", "rgb(0, 0, 0)");

  await expect.poll(() => saves.at(-1)?.themeName).toBe("sapphire");
  expect(saves.at(-1)).toMatchObject({ themeName: "sapphire", darkMode: false, sidenavColor: "rose" });
});

test("a theme whose preset is dark switches the app to dark mode when picked", async ({ page }) => {
  const saves = await mockApi(page, null);
  await page.getByLabel("Display settings").click();
  await themePicker(page).selectOption({ label: "Astro Novalite" });

  await expect(themePicker(page)).toHaveValue("astro");
  await expect(page.locator("body")).toHaveCSS("background-color", "rgb(30, 31, 42)");
  // The active sidenav item is cream with dark text on it.
  const activeIcon = page.locator('.MuiDrawer-paper a[href="/tenants"] .MuiIcon-root');
  await expect(activeIcon).toHaveCSS("color", "rgb(0, 0, 0)");

  await expect.poll(() => saves.at(-1)?.themeName).toBe("astro");
  expect(saves.at(-1)).toMatchObject({ themeName: "astro", darkMode: true, sidenavColor: "cream" });
});

test("a theme name saved on the profile is applied on load, and one saved without a name is the default", async ({ page }) => {
  const loaded = await mockApi(page, {
    themeName: "sapphire", darkMode: false, whiteSidenav: false, sidenavTint: null, sidenavColor: "rose", fixedNavbar: true,
  });
  await expect(page.locator("body")).toHaveCSS("background-color", "rgb(245, 245, 245)");
  await page.getByLabel("Display settings").click();
  await expect(themePicker(page)).toHaveValue("sapphire");
  // Applying the saved theme is not a change, so nothing is written back.
  await page.waitForTimeout(800);
  expect(loaded).toHaveLength(0);

  const saves = await mockApi(page, {
    darkMode: false, whiteSidenav: false, sidenavTint: null, sidenavColor: "harbor", fixedNavbar: true,
  });
  await expect(page.locator("body")).toHaveCSS("background-color", "rgb(246, 249, 251)");
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
  await page.getByTitle("coral").first().click();
  await themePicker(page).selectOption({ label: "Sapphire Ash" });
  await expect.poll(() => saves.at(-1)?.themeName).toBe("sapphire");
  expect(saves.find((save) => save.themeName === "ocean")).toMatchObject({ sidenavColor: "coral" });
  // Sapphire Ash was never used, so it starts from its preset.
  expect(saves.at(-1)).toMatchObject({ darkMode: false, sidenavColor: "rose" });

  // Customise Sapphire Ash, switch away and back: each theme returns as it was left.
  await page.getByTitle("iris").first().click();
  await themePicker(page).selectOption({ label: "Default" });
  await expect.poll(() => saves.at(-1)).toMatchObject({ themeName: "ocean", darkMode: false, sidenavColor: "coral" });
  expect(saves.findLast((save) => save.themeName === "sapphire")).toMatchObject({ sidenavColor: "iris" });

  await themePicker(page).selectOption({ label: "Sapphire Ash" });
  await expect.poll(() => saves.at(-1)).toMatchObject({ themeName: "sapphire", darkMode: false, sidenavColor: "iris" });
});

test("light and dark each keep their own colours within a theme", async ({ page }) => {
  const saves = await mockApi(page, null);
  await page.getByLabel("Display settings").click();
  const modeSwitch = page.getByRole("switch").last();

  // Pick coral in light mode, then go dark before the debounced save has gone out.
  await page.getByTitle("coral").first().click();
  await modeSwitch.click();
  await expect.poll(() => saves.at(-1)).toMatchObject({ themeName: "ocean", darkMode: true });
  expect(saves.find((save) => save.darkMode === false)).toMatchObject({ themeName: "ocean", sidenavColor: "coral" });
  // Dark was never used, so it starts with the colours carried over from light.
  expect(saves.at(-1)).toMatchObject({ sidenavColor: "coral" });

  // Pick sunset in dark mode, then go back and forth: each mode returns as it was left.
  await page.getByTitle("sunset").first().click();
  await modeSwitch.click();
  await expect.poll(() => saves.at(-1)).toMatchObject({ darkMode: false, sidenavColor: "coral" });
  expect(saves.findLast((save) => save.darkMode === true)).toMatchObject({ sidenavColor: "sunset" });

  await modeSwitch.click();
  await expect.poll(() => saves.at(-1)).toMatchObject({ darkMode: true, sidenavColor: "sunset" });
});

test("each theme offers its own sidenav swatches, and a saved one it does not offer is replaced", async ({ page }) => {
  // "steel" was offered before themes had their own swatches; "rose" is Sapphire Ash's.
  const saves = await mockApi(page, {
    themeName: "ocean", darkMode: false, whiteSidenav: false, sidenavTint: "rose", sidenavColor: "steel", fixedNavbar: true,
  });
  // The sidenav keeps its Dark style, and the active item is the theme's own steel blue.
  const drawer = page.locator(".MuiDrawer-paper").first();
  await expect(drawer).toHaveCSS("background-image", /rgb\(18, 31, 69\)/);
  await expect(drawer.locator('a[href="/tenants"] > li > div')).toHaveCSS("background-image", /rgb\(59, 102, 149\)/);

  await page.getByLabel("Display settings").click();
  const swatches = () => page.locator("button[title]").evaluateAll((buttons) => buttons.map((button) => button.title));
  expect(await swatches()).toEqual([
    "harbor", "tide", "mist", "coral", "crimson", "sunset", "Dark", "White", "indigo", "harbor", "crimson", "mist",
  ]);

  await themePicker(page).selectOption({ label: "Astro Novalite" });
  await expect(themePicker(page)).toHaveValue("astro");
  expect(await swatches()).toEqual([
    "cream", "sand", "frost", "balihai", "tempest", "shuttle", "Dark", "White", "graphite", "shuttle", "balihai", "cream",
  ]);

  // A tint fills the whole sidenav with this theme's colour.
  await page.getByTitle("balihai").last().click();
  await expect(page.locator(".MuiDrawer-paper").first()).toHaveCSS("background-image", /rgb\(138, 157, 178\)/);
  await expect.poll(() => saves.at(-1)).toMatchObject({ themeName: "astro", sidenavTint: "balihai", sidenavColor: "cream" });
});

// Every background the page and the sidenav show, frame by frame, until `stop` is called.
async function watchBackgrounds(page: Page) {
  await page.evaluate(() => {
    const seen = { page: [] as string[], sidenav: [] as string[] };
    const note = (list: string[], value: string) => {
      if (list.at(-1) !== value) list.push(value);
    };
    const state = { seen, running: true };
    (window as unknown as { backgrounds: typeof state }).backgrounds = state;
    const sample = () => {
      const drawer = document.querySelector(".MuiDrawer-paper");
      note(seen.page, getComputedStyle(document.body).backgroundColor);
      if (drawer) {
        const style = getComputedStyle(drawer);
        note(seen.sidenav, `${style.backgroundColor} ${style.backgroundImage}`);
      }
      if (state.running) requestAnimationFrame(sample);
    };
    sample();
  });
  return () =>
    page.evaluate(() => {
      const state = (window as unknown as { backgrounds: { seen: { page: string[]; sidenav: string[] }; running: boolean } }).backgrounds;
      state.running = false;
      return state.seen;
    });
}

test("switching to a theme or a mode used before goes straight to its saved look, with no other colours in between", async ({ page }) => {
  // Astro Novalite was last left in light mode with a tinted sidenav: nothing like its dark preset.
  const astroLight = {
    themeName: "astro", darkMode: false, whiteSidenav: false, sidenavTint: "balihai", sidenavColor: "sand", fixedNavbar: true,
  };
  const astroDark = { ...astroLight, darkMode: true, sidenavTint: "graphite", sidenavColor: "frost" };
  await mockApi(page, null, [astroDark, astroLight]);
  await page.getByLabel("Display settings").click();
  // Let the opening panel settle, so only the switch itself is watched.
  await page.waitForTimeout(600);

  let stop = await watchBackgrounds(page);
  await themePicker(page).selectOption({ label: "Astro Novalite" });
  await expect(page.locator("body")).toHaveCSS("background-color", "rgb(250, 246, 234)");
  await page.waitForTimeout(700);
  let seen = await stop();
  expect(seen.page).toEqual(["rgb(246, 249, 251)", "rgb(250, 246, 234)"]);
  expect(seen.sidenav).toHaveLength(2);
  expect(seen.sidenav[1]).toMatch(/rgb\(138, 157, 178\)/);

  // Its dark mode comes back the same way.
  stop = await watchBackgrounds(page);
  await page.getByRole("switch").last().click();
  await expect(page.locator("body")).toHaveCSS("background-color", "rgb(30, 31, 42)");
  await page.waitForTimeout(700);
  seen = await stop();
  expect(seen.page).toEqual(["rgb(250, 246, 234)", "rgb(30, 31, 42)"]);
  expect(seen.sidenav).toHaveLength(2);
  expect(seen.sidenav[1]).toMatch(/rgb\(58, 63, 75\)/);
});
