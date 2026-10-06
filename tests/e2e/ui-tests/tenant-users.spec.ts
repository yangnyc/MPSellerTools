import { test, expect } from "@playwright/test";

const acme = { tenantId: "t1", tenantName: "Acme", tenantSlug: "acme" };
const birch = { tenantId: "t2", tenantName: "Birch", tenantSlug: "birch" };
const users = [
  { ...acme, id: "u1", email: "ada@acme.test", displayName: "Ada Admin", roles: ["TenantAdmin"], isBlocked: false },
  { ...acme, id: "u2", email: "eve@acme.test", displayName: "Eve Employee", roles: ["Employee"], isBlocked: false },
  // The same user id in another company is a different user.
  { ...birch, id: "u1", email: "bob@birch.test", displayName: "Bob Blocked", roles: ["Employee"], isBlocked: true },
];
const unavailable = [{ tenantId: "t3", tenantName: "Cedar", reason: "Suspended" }];

// Requests the page makes that change something, as "METHOD path body".
let writes: string[];

test.beforeEach(async ({ page }) => {
  writes = [];
  page.on("pageerror", (error) => { throw error; });
  await page.route(/\/\/[^/]+\/api\//, async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() !== "GET") {
      writes.push(`${request.method()} ${path} ${request.postData() ?? ""}`.trim());
      if (path.endsWith("/force-password-reset")) {
        await route.fulfill({ json: { devResetUrl: "https://localhost:7201/reset-password?token=abc" } });
      } else if (path.endsWith("/invite")) {
        await route.fulfill({ json: { devAcceptUrl: "https://localhost:7202/accept-invitation?token=xyz" } });
      } else if (request.method() === "DELETE" && path === "/api/tenant-users/t1/u1") {
        await route.fulfill({ status: 400, json: { detail: "Cannot delete the last active TenantAdmin." } });
      } else if (path === "/api/tenant-users/t1/u1/block") {
        await route.fulfill({ status: 400, json: { detail: "Cannot block the last active TenantAdmin." } });
      } else {
        await route.fulfill({ status: 204 });
      }
    } else if (path === "/api/auth/me") {
      await route.fulfill({ json: { id: "admin", email: "admin@example.test", displayName: "Admin", roles: ["PlatformAdmin"], pinnedMenus: [] } });
    } else if (path === "/api/antiforgery/token") {
      await route.fulfill({ json: { token: "test" } });
    } else if (path === "/api/tenant-users") {
      await route.fulfill({ json: { users, unavailable } });
    } else if (path === "/api/tenants") {
      await route.fulfill({
        json: [
          { id: "t1", name: "Acme", slug: "acme", status: 1, url: "https://localhost:7201", createdAtUtc: "2026-01-01T00:00:00Z" },
          { id: "t2", name: "Birch", slug: "birch", status: 1, url: "https://localhost:7202", createdAtUtc: "2026-01-01T00:00:00Z" },
          { id: "t3", name: "Cedar", slug: "cedar", status: 2, url: null, createdAtUtc: "2026-01-01T00:00:00Z" },
        ],
      });
    } else {
      await route.fulfill({ json: [] });
    }
  });
});

test("the Tenant users sidebar group opens its pages and names companies it could not read", async ({ page }) => {
  await page.goto("/tenant-users");
  const sidebar = page.locator(".MuiDrawer-paper").filter({ has: page.getByText("MP Seller Tools", { exact: true }) });
  await expect(sidebar.getByRole("button", { name: /Tenant users/ })).toHaveAttribute("aria-expanded", "true");
  await expect(page.getByRole("heading", { name: "Tenant users" })).toBeVisible();
  await expect(page.getByText("Cedar: Suspended")).toBeVisible();
  await expect(page.getByText("Showing 1 to 3 of 3 entries")).toBeVisible();

  await sidebar.locator('a[href="/tenant-users/bulk"]').click();
  await expect(page.getByRole("heading", { name: "Bulk actions" })).toBeVisible();
  await sidebar.locator('a[href="/tenant-users/export"]').click();
  await expect(page.getByRole("heading", { name: "Filters & export" })).toBeVisible();
  await sidebar.locator('a[href="/tenant-users/controls"]').click();
  await expect(page.getByRole("heading", { name: "Account controls" })).toBeVisible();
});

test("adding a user invites them into the chosen running company and shows the accept link", async ({ page }) => {
  await page.goto("/tenant-users");
  await page.getByRole("button", { name: "Add user" }).click();
  const dialog = page.getByRole("dialog");
  // Cedar is suspended, so it cannot be asked to add anyone.
  await expect(dialog.getByLabel("Company").locator("option")).toHaveText(["Acme", "Birch"]);

  await dialog.getByLabel("Company").selectOption({ label: "Birch" });
  await dialog.getByLabel("Email").fill("new@birch.test");
  await dialog.getByLabel("Role").selectOption({ label: "Company admin" });
  await dialog.getByRole("button", { name: "Send invite" }).click();

  await expect(dialog.getByText("https://localhost:7202/accept-invitation?token=xyz")).toBeVisible();
  expect(writes).toEqual(['POST /api/tenant-users/t2/invite {"email":"new@birch.test","role":"TenantAdmin"}']);
});

test("creating a user adds them straight away and shows the sign-in details once", async ({ page }) => {
  await page.goto("/tenant-users");
  await page.getByRole("button", { name: "Add user" }).click();
  const dialog = page.getByRole("dialog");

  await dialog.getByLabel("Email").fill("new@acme.test");
  await dialog.getByRole("button", { name: "Create" }).click();

  await expect(dialog.getByText("Username: new@acme.test")).toBeVisible();
  // No password was typed, so one was generated.
  const sent = JSON.parse(writes[0].slice("POST /api/tenant-users/t1/create ".length));
  expect(writes).toHaveLength(1);
  expect(sent).toMatchObject({ email: "new@acme.test", role: "Employee" });
  expect(sent.password).toMatch(/^(?=.*[A-Z])(?=.*[a-z])(?=.*\d)[A-Za-z\d]{16}$/);
  await expect(dialog.getByText(`Password: ${sent.password}`)).toBeVisible();
});

test("editing a user saves the details and the role to that user's company", async ({ page }) => {
  await page.goto("/tenant-users");
  await page.getByRole("button", { name: "Edit Eve Employee at Acme" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByLabel("Email")).toHaveValue("eve@acme.test");

  await dialog.getByLabel("Display name").fill("Eve Edited");
  await dialog.getByLabel("Role").selectOption({ label: "Company admin" });
  await dialog.getByRole("button", { name: "Save changes" }).click();

  await expect(dialog).toBeHidden();
  expect(writes).toEqual([
    'PUT /api/tenant-users/t1/u2 {"displayName":"Eve Edited","email":"eve@acme.test"}',
    'PUT /api/tenant-users/t1/u2/role {"role":"TenantAdmin"}',
  ]);
});

test("setting a password shows the username and the new password once", async ({ page }) => {
  await page.goto("/tenant-users");
  await page.getByRole("button", { name: "Set password for Eve Employee at Acme" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByLabel("Username")).toHaveValue("eve@acme.test");
  await expect(dialog.getByLabel("New password")).toHaveAttribute("type", "password");

  await dialog.getByRole("button", { name: "Generate" }).click();
  await expect(dialog.getByLabel("New password")).toHaveAttribute("type", "text");
  const generated = await dialog.getByLabel("New password").inputValue();
  expect(generated).toMatch(/^(?=.*[A-Z])(?=.*[a-z])(?=.*\d)[A-Za-z\d]{16}$/);
  await dialog.getByRole("button", { name: "Set password" }).click();

  await expect(dialog.getByText("Username: eve@acme.test")).toBeVisible();
  await expect(dialog.getByText(`Password: ${generated}`)).toBeVisible();
  expect(writes).toEqual([`POST /api/tenant-users/t1/u2/set-password {"password":"${generated}"}`]);
});

test("deleting a user asks first and reports the company's refusal", async ({ page }) => {
  await page.goto("/tenant-users");
  await page.getByRole("button", { name: "Delete Bob Blocked at Birch" }).click();
  await expect(page.getByRole("dialog").getByText(/Permanently delete Bob Blocked \(bob@birch\.test\) from Birch\?/)).toBeVisible();
  await page.getByRole("dialog").getByRole("button", { name: "Delete" }).click();
  await expect(page.getByText("Bob Blocked deleted from Birch.")).toBeVisible();

  await page.getByRole("button", { name: "Delete Ada Admin at Acme" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Delete" }).click();
  await expect(page.getByText("Cannot delete the last active TenantAdmin.")).toBeVisible();

  expect(writes).toEqual(["DELETE /api/tenant-users/t2/u1", "DELETE /api/tenant-users/t1/u1"]);
});

test("a bulk block goes to each user's own company and reports a company's refusal", async ({ page }) => {
  await page.goto("/tenant-users/bulk");
  await page.getByLabel("Select all").check();
  // Bob is blocked already, so only two would change.
  await page.getByRole("button", { name: "Block (2)" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Block" }).click();

  await expect(page.getByText("1 user blocked, 1 failed:")).toBeVisible();
  await expect(page.getByText("Ada Admin (Acme): Cannot block the last active TenantAdmin.")).toBeVisible();
  expect(writes).toEqual(["POST /api/tenant-users/t1/u1/block", "POST /api/tenant-users/t1/u2/block"]);
});

test("a role change is sent for the selected user in the selected company only", async ({ page }) => {
  await page.goto("/tenant-users/bulk");
  await page.getByLabel("Company").selectOption({ label: "Birch" });
  await expect(page.getByLabel("Select Ada Admin at Acme")).toHaveCount(0);

  await page.getByLabel("Select Bob Blocked at Birch").check();
  await page.getByLabel("Role to set").selectOption({ label: "Company admin" });
  await page.getByRole("button", { name: "Set role (1)" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Make Company admin" }).click();

  await expect(page.getByText("1 user changed to Company admin")).toBeVisible();
  expect(writes).toEqual(['PUT /api/tenant-users/t2/u1/role {"role":"TenantAdmin"}']);
});

test("filters narrow the export list across companies", async ({ page }) => {
  await page.goto("/tenant-users/export");
  await expect(page.getByText("3 of 3 users")).toBeVisible();

  await page.getByLabel("Company").selectOption({ label: "Acme" });
  await expect(page.getByText("2 of 3 users")).toBeVisible();
  await page.getByLabel("Role").selectOption({ label: "Employee" });
  await expect(page.getByText("1 of 3 users")).toBeVisible();
  await expect(page.getByRole("button", { name: "Export CSV (1)" })).toBeEnabled();

  await page.getByRole("button", { name: "Clear filters" }).click();
  await expect(page.getByText("3 of 3 users")).toBeVisible();
});

test("forcing a password reset shows the link from the user's company", async ({ page }) => {
  await page.goto("/tenant-users/controls");
  await expect(page.getByRole("button", { name: "Sign out Bob Blocked at Birch" })).toBeDisabled();

  await page.getByRole("button", { name: "Reset password for Eve Employee at Acme" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Reset password" }).click();

  await expect(page.getByText("https://localhost:7201/reset-password?token=abc")).toBeVisible();
  expect(writes).toEqual(["POST /api/tenant-users/t1/u2/force-password-reset"]);
});
