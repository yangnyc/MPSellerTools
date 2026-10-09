import { test, expect } from "@playwright/test";

const tenants = [
  { id: "0", name: "Acme", slug: "acme", status: 1, url: "https://localhost:7201", createdAtUtc: "2026-01-01T00:00:00Z" },
  { id: "1", name: "Birch", slug: "birch", status: 2, url: null, createdAtUtc: "2026-02-01T00:00:00Z" },
  { id: "2", name: "Cedar", slug: "cedar", status: 1, url: "https://localhost:7203", createdAtUtc: "2026-03-01T00:00:00Z" },
];

// Requests the page makes that change something, as "METHOD path".
let writes: string[];
// What the mocked profile has saved as pinned sidebar menus.
let pinnedMenus: string[];

// Whether the page has asked to delete Acme, and how often it has re-read it since.
let deleteRequested: boolean;
let readsSinceDelete: number;

test.beforeEach(async ({ page }) => {
  writes = [];
  pinnedMenus = [];
  deleteRequested = false;
  readsSinceDelete = 0;
  page.on("pageerror", (error) => { throw error; });
  await page.route(/\/\/[^/]+\/api\//, async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const me = { id: "admin", email: "admin@example.test", displayName: "Admin", roles: ["PlatformAdmin"] };
    if (path === "/api/auth/me/pinned-menus") {
      pinnedMenus = request.postDataJSON().menus;
      await route.fulfill({ json: { ...me, pinnedMenus } });
    } else if (request.method() === "DELETE" && path === "/api/tenants/0") {
      writes.push(`DELETE ${path}${new URL(request.url()).search}`);
      deleteRequested = true;
      await route.fulfill({ status: 202 });
    } else if (path === "/api/tenants/0") {
      // Once deletion is asked for: "Deleting" on the first read, gone after it.
      if (deleteRequested && readsSinceDelete++ > 0) {
        await route.fulfill({ status: 404, json: { title: "Not Found" } });
      } else {
        await route.fulfill({ json: {
          ...tenants[0], status: deleteRequested ? 4 : 1, failureReason: null, updatedAtUtc: tenants[0].createdAtUtc,
        } });
      }
    } else if (request.method() !== "GET") {
      writes.push(`${request.method()} ${path}`);
      await route.fulfill({ status: 202 });
    } else if (path === "/api/auth/me") {
      await route.fulfill({ json: { ...me, pinnedMenus } });
    } else if (path === "/api/antiforgery/token") {
      await route.fulfill({ json: { token: "test" } });
    } else if (path === "/api/tenants") {
      await route.fulfill({ json: tenants });
    } else if (path === "/api/tenants/runtime") {
      await route.fulfill({ json: tenants.map((t, i) => ({
        ...t, port: 7201 + i, databaseName: `MPSellerTools_Tenant_${t.slug}`, applicationInstanceId: null,
        processId: t.status === 1 ? 4000 + i : null, processStartTimeUtc: null, updatedAtUtc: t.createdAtUtc,
      })) });
    } else {
      await route.fulfill({ json: [] });
    }
  });
  await page.goto("/tenants");
  await expect(page.getByRole("heading", { name: "Companies" })).toBeVisible();
});

test("the Tenants sidebar group expands to its sub-items and opens their pages", async ({ page }) => {
  const sidebar = page.locator(".MuiDrawer-paper").filter({ has: page.getByText("MP Seller Tools", { exact: true }) });
  const group = sidebar.getByRole("button", { name: /Tenants/ });
  // Open already, because the page being shown is one of its sub-items.
  await expect(group).toHaveAttribute("aria-expanded", "true");
  await expect(sidebar.locator('a[href="/tenants"]')).toBeVisible();

  await group.click();
  await expect(group).toHaveAttribute("aria-expanded", "false");
  await expect(sidebar.locator('a[href="/tenants/bulk"]')).toHaveCount(0);

  await group.click();
  await expect(group).toHaveAttribute("aria-expanded", "true");
  await sidebar.locator('a[href="/tenants/controls"]').click();
  await expect(page.getByRole("heading", { name: "Tenant controls" })).toBeVisible();
  await expect(page.getByText("PID 4000")).toBeVisible();

  // Loading a sub-item's page directly opens its group.
  await page.goto("/tenants/export");
  await expect(page.getByRole("heading", { name: "Filters & export" })).toBeVisible();
  await expect(sidebar.getByRole("button", { name: /Tenants/ })).toHaveAttribute("aria-expanded", "true");
});

test("a bulk action runs only on the selected companies it applies to", async ({ page }) => {
  await page.goto("/tenants/bulk");
  await page.getByLabel("Select all").check();
  await expect(page.getByRole("button", { name: "Suspend (2)" })).toBeEnabled();
  await expect(page.getByRole("button", { name: "Resume (1)" })).toBeEnabled();

  await page.getByLabel("Select Cedar").uncheck();
  await page.getByRole("button", { name: "Suspend (1)" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Suspend" }).click();

  await expect(page.getByText("1 company queued to suspend")).toBeVisible();
  expect(writes).toEqual(["POST /api/tenants/0/suspend"]);
});

test("deleting a company needs its slug typed, then follows the deletion to the end", async ({ page }) => {
  await page.goto("/tenants/0");
  await page.getByRole("button", { name: "Delete company" }).click();

  const dialog = page.getByRole("dialog");
  const confirm = dialog.getByRole("button", { name: "Delete permanently" });
  await expect(confirm).toBeDisabled();
  await dialog.getByLabel("Company slug").fill("acm");
  await expect(confirm).toBeDisabled();
  await dialog.getByLabel("Company slug").fill("acme");
  await confirm.click();

  await expect.poll(() => writes).toEqual(["DELETE /api/tenants/0?confirmSlug=acme"]);
  // While the worker removes it, the page shows the state and offers no actions.
  await expect(page.getByText("Deleting this company")).toBeVisible();
  await expect(page.getByRole("button", { name: "Delete company" })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Suspend" })).toHaveCount(0);

  // Once it is gone, back to the list.
  await expect(page.getByText("Company deleted.")).toBeVisible({ timeout: 10000 });
  await expect(page).toHaveURL(/\/tenants$/);
});

test("filters narrow the export list", async ({ page }) => {
  await page.goto("/tenants/export");
  await expect(page.getByText("3 of 3 companies")).toBeVisible();

  await page.getByLabel("Status").selectOption({ label: "Active" });
  await expect(page.getByText("2 of 3 companies")).toBeVisible();
  await page.getByLabel("Created from").fill("2026-02-15");
  await expect(page.getByText("1 of 3 companies")).toBeVisible();
  await expect(page.getByRole("button", { name: "Export CSV (1)" })).toBeEnabled();

  await page.getByRole("button", { name: "Clear filters" }).click();
  await expect(page.getByText("3 of 3 companies")).toBeVisible();
});

test("pinning a sidebar group keeps it open, and unpinning closes it", async ({ page }) => {
  const sidebar = page.locator(".MuiDrawer-paper").filter({ has: page.getByText("MP Seller Tools", { exact: true }) });
  const group = sidebar.getByRole("button", { name: /Tenants/ });
  const pin = sidebar.getByLabel("Keep Tenants open");

  // Ticking the box does not also toggle the group it sits in.
  await pin.check();
  await expect(sidebar.locator('a[href="/tenants/bulk"]')).toBeVisible();
  // The row is no longer a button, and clicking it changes nothing.
  await expect(group).toHaveCount(0);
  await sidebar.getByText("expand_less", { exact: true }).click();
  await expect(sidebar.locator('a[href="/tenants/bulk"]')).toBeVisible();

  // Saved on the profile, so still open after a reload, on a page outside the group.
  expect(pinnedMenus).toEqual(["tenants"]);
  await page.goto("/jobs");
  await expect(sidebar.getByLabel("Keep Tenants open")).toBeChecked();
  await expect(sidebar.locator('a[href="/tenants/bulk"]')).toBeVisible();

  // Unpinning closes it, and the arrow works again.
  await sidebar.getByLabel("Keep Tenants open").uncheck();
  await expect.poll(() => pinnedMenus).toEqual([]);
  await expect(sidebar.locator('a[href="/tenants/bulk"]')).toHaveCount(0);
  await sidebar.getByRole("button", { name: /Tenants/ }).click();
  await expect(sidebar.locator('a[href="/tenants/bulk"]')).toBeVisible();

  // Also on a page inside the group, where it would otherwise be open.
  await page.goto("/tenants/bulk");
  await sidebar.getByLabel("Keep Tenants open").check();
  await sidebar.getByLabel("Keep Tenants open").uncheck();
  await expect(sidebar.locator('a[href="/tenants/bulk"]')).toHaveCount(0);
});

test("a notification disappears by itself after five seconds", async ({ page }) => {
  await page.goto("/tenants/controls");
  await page.getByRole("button", { name: "Restart" }).first().click();
  await page.getByRole("dialog").getByRole("button", { name: "Restart" }).click();

  const notification = page.getByText("Acme queued to restart.");
  await expect(notification).toBeVisible();
  // Still there well inside the five seconds, gone shortly after them.
  await page.waitForTimeout(3000);
  await expect(notification).toBeVisible();
  await expect(notification).toBeHidden({ timeout: 4000 });
});

test("the notifications view beside display settings keeps what was shown", async ({ page }) => {
  await page.goto("/tenants/controls");
  const bell = page.getByRole("button", { name: "Notifications" });
  const unread = page.getByTestId("notifications-unread");

  // Nothing yet.
  await expect(unread).toHaveCount(0);
  await bell.click();
  const panel = page.getByRole("dialog", { name: "Notifications" });
  await expect(panel.getByText("No notifications yet")).toBeVisible();
  await page.keyboard.press("Escape");

  await page.getByRole("button", { name: "Restart" }).first().click();
  await page.getByRole("dialog").getByRole("button", { name: "Restart" }).click();
  await expect(unread).toHaveText("1");

  // Still listed after moving to another page; opening it clears the count.
  await page.getByRole("link", { name: "Jobs" }).first().click();
  await expect(page.getByRole("heading", { name: "Provisioning jobs" })).toBeVisible();
  await bell.click();
  await expect(panel.getByText("Acme queued to restart.")).toBeVisible();
  await expect(unread).toHaveCount(0);

  // One can be dismissed by itself.
  await panel.getByRole("button", { name: "Dismiss: Acme queued to restart." }).click();
  await expect(panel.getByText("Acme queued to restart.")).toHaveCount(0);
  await expect(panel.getByText("No notifications yet")).toBeVisible();
});