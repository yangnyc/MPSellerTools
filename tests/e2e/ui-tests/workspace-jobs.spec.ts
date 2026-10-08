import { test, expect, type Page } from "@playwright/test";

const admin = { id: "u1", email: "ada@acme.test", displayName: "Ada Admin", roles: ["TenantAdmin"], isBlocked: false, pinnedMenus: [] };

const magento = {
  id: "a-magento", channel: 4, name: "Magento", environment: 0, sellerId: null, settings: { baseUrl: "https://shop.example.test" }, hasCredentials: true,
  isEnabled: true, liveWritesEnabled: true, effectiveLiveWrites: true, inventorySyncEnabled: false, orderImportEnabled: false, priceConflictPolicy: 2,
  lastOrderImportAtUtc: null, lastError: null, markets: [{ id: "m-magento", marketplaceCode: "default", language: "en-US", currency: "USD" }],
};

const job = (over: object) => ({
  id: "j-running", type: 0, status: 1, channelAccountId: "a-magento", accountName: "Magento", channel: 4,
  total: 2500, processed: 600, succeeded: 598, failed: 2, cancelRequested: false, summary: null, lastError: null, errors: [],
  createdByEmail: "ada@acme.test", createdAtUtc: "2026-10-01T00:00:00Z", startedAtUtc: "2026-10-01T00:00:05Z", finishedAtUtc: null, ...over,
});

const heldBack = job({
  id: "j-done", type: 3, status: 3, total: 40, processed: 40, succeeded: 38, failed: 2, summary: "38 ready to publish; 2 held back.",
  errors: [{ item: "MUG-BLUE", message: "A Magento category is its number, as shown in the store's admin." }, { item: "MUG-RED", message: "Magento accepts SKUs of up to 64 characters." }],
  finishedAtUtc: "2026-10-01T00:01:00Z",
});

// Requests the page makes that change something, as "METHOD path body".
let writes: string[];

async function mockApi(page: Page, jobs: object[]) {
  writes = [];
  page.on("pageerror", (error) => { throw error; });
  await page.route(/\/\/[^/]+\/api\//, async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() !== "GET") {
      writes.push(`${request.method()} ${path} ${request.postData() ?? ""}`.trim());
      await route.fulfill({ json: job({ id: "j-new", status: 0, processed: 0, succeeded: 0, failed: 0, total: 0 }) });
    } else if (path === "/api/auth/me") {
      await route.fulfill({ json: admin });
    } else if (path === "/api/antiforgery/token") {
      await route.fulfill({ json: { token: "test" } });
    } else if (path === "/api/bulk-jobs") {
      await route.fulfill({ json: jobs });
    } else if (path === "/api/channels") {
      await route.fulfill({ json: [magento] });
    } else {
      await route.fulfill({ json: [] });
    }
  });
}

test("the jobs page shows each job's progress and what was held back, and lets a job be stopped, started and run again", async ({ page }) => {
  await mockApi(page, [job({}), heldBack]);
  await page.goto("/jobs");

  // In the sidebar, after the sync queue.
  const sidebar = page.locator(".MuiDrawer-paper").filter({ has: page.getByText("MP Seller Tools", { exact: true }) });
  await expect(sidebar.locator('a[href="/jobs"]')).toHaveText(/Jobs/);

  // The running job: how far it has got, and that some were held back.
  const running = page.getByRole("row").filter({ hasText: "Publish every draft" });
  await expect(running.getByText("600 of 2,500 · 2 held back")).toBeVisible();
  await expect(running.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "24");
  await expect(running.getByText("Running")).toBeVisible();
  await running.getByRole("button", { name: "Stop" }).click();
  await expect(page.getByText("The job stops after the items it is on.")).toBeVisible();

  // The finished one: its result, and in its details exactly which items were held back and why.
  const finished = page.getByRole("row").filter({ hasText: "Check every listing" });
  await expect(finished.getByText("Done, some held back")).toBeVisible();
  await expect(finished.getByText("38 ready to publish; 2 held back.")).toBeVisible();
  await finished.getByRole("button", { name: "Details of Check every listing" }).click();
  const details = page.getByRole("dialog");
  await expect(details.getByText("MUG-BLUE")).toBeVisible();
  await expect(details.getByText("A Magento category is its number, as shown in the store's admin.")).toBeVisible();
  await details.getByRole("button", { name: "Run again" }).click();
  await expect(page.getByText("Queued again. It works on whatever is left to do.")).toBeVisible();

  // A new job: which sales channel, and what to do there.
  await page.getByRole("button", { name: "New job" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("What to do").selectOption({ label: "Take everything off sale" });
  await expect(dialog.getByText("Nothing is deleted.")).toBeVisible();
  await dialog.getByRole("button", { name: "Start job" }).click();
  await expect(page.getByText("The job was queued. It starts when the ones ahead of it are done.")).toBeVisible();

  expect(writes).toEqual([
    "POST /api/bulk-jobs/j-running/cancel",
    "POST /api/bulk-jobs/j-done/run-again",
    'POST /api/bulk-jobs {"type":1,"channelAccountId":"a-magento"}',
  ]);
});

test("with no jobs yet the page says what a job is for and offers to start one", async ({ page }) => {
  await mockApi(page, []);
  await page.goto("/jobs");

  await expect(page.getByText("No jobs yet")).toBeVisible();
  await expect(page.getByText("Start a job to publish every draft on a sales channel, take everything off sale, or read what a store has.")).toBeVisible();
  await expect(page.getByRole("button", { name: "New job" })).toHaveCount(2);
});
