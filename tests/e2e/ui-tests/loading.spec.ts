import { test, expect } from "@playwright/test";

const me = { id: "admin", email: "admin@example.test", displayName: "Admin", roles: ["PlatformAdmin"], theme: null };

test("something shows that the app is loading, from the first moment until the page has its data", async ({ page }) => {
  page.on("pageerror", (error) => { throw error; });
  // Released by the test, so each stage of loading can be looked at while it lasts.
  let answerProfile = () => {};
  let answerLists = () => {};
  const profile = new Promise<void>((resolve) => { answerProfile = resolve; });
  const lists = new Promise<void>((resolve) => { answerLists = resolve; });
  await page.route(/\/\/[^/]+\/api\//, async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/auth/me") {
      await profile;
      await route.fulfill({ json: me });
    } else if (path === "/api/antiforgery/token") {
      await route.fulfill({ json: { token: "test" } });
    } else if (path.startsWith("/api/auth/me/theme/")) {
      await route.fulfill({ status: 204 });
    } else {
      await lists;
      await route.fulfill({ json: [] });
    }
  });

  await page.goto("/tenants");
  // Who is signed in is not known yet: the whole window is a spinner, not a blank page.
  await expect(page.getByRole("status", { name: "Loading" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Companies" })).toHaveCount(0);

  // Signed in; the page is up and still waiting for its data, and the bar across the top says so.
  answerProfile();
  await expect(page.getByRole("heading", { name: "Companies" })).toBeVisible();
  const bar = page.getByRole("progressbar", { name: "Loading" });
  await expect(bar).toBeVisible();
  await expect(bar).toHaveCSS("position", "fixed");

  answerLists();
  await expect(bar).toHaveCount(0);
});
