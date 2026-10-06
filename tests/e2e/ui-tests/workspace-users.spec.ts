import { test, expect } from "@playwright/test";

const users = [
  { id: "u1", email: "ada@acme.test", displayName: "Ada Admin", roles: ["TenantAdmin"], isBlocked: false },
  { id: "u2", email: "eve@acme.test", displayName: "Eve Employee", roles: ["Employee"], isBlocked: false },
  { id: "u3", email: "bob@acme.test", displayName: "Bob Blocked", roles: ["Employee"], isBlocked: true },
];

const invitations = [
  { id: "i1", email: "new@acme.test", role: "Employee", createdAtUtc: "2026-10-01T00:00:00Z", expiresAtUtc: "2099-01-01T00:00:00Z", isExpired: false },
  { id: "i2", email: "late@acme.test", role: "TenantAdmin", createdAtUtc: "2026-01-01T00:00:00Z", expiresAtUtc: "2026-01-08T00:00:00Z", isExpired: true },
];

const audit = [
  { id: "a1", occurredAtUtc: "2026-10-04T10:00:00Z", actorEmail: "ada@acme.test", action: "UserBlocked", details: "user=bob@acme.test" },
  { id: "a2", occurredAtUtc: "2026-10-03T10:00:00Z", actorEmail: "ada@acme.test", action: "ProductCreated", details: "sku=A-1" },
  { id: "a3", occurredAtUtc: "2026-10-02T10:00:00Z", actorEmail: "eve@acme.test", action: "TaskStatusChanged", details: "title=Pack; status=Done" },
];

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
      if (request.method() === "POST" && path === "/api/invitations") {
        const { email } = request.postDataJSON();
        await route.fulfill({ json: { invitationId: email, devAcceptUrl: `/accept-invitation?token=${email}` } });
      } else {
        await route.fulfill({ status: 204 });
      }
    } else if (path === "/api/auth/me") {
      await route.fulfill({ json: { ...users[0], pinnedMenus: [] } });
    } else if (path === "/api/antiforgery/token") {
      await route.fulfill({ json: { token: "test" } });
    } else if (path === "/api/users") {
      await route.fulfill({ json: users });
    } else if (path === "/api/invitations") {
      await route.fulfill({ json: invitations });
    } else if (path === "/api/audit") {
      await route.fulfill({ json: audit });
    } else {
      await route.fulfill({ json: [] });
    }
  });
});

test("the Users sidebar group lists the advanced pages and opens them", async ({ page }) => {
  await page.goto("/users");
  const sidebar = page.locator(".MuiDrawer-paper").filter({ has: page.getByText("MP Seller Tools", { exact: true }) });
  await expect(sidebar.getByRole("button", { name: /Users/ })).toHaveAttribute("aria-expanded", "true");

  await sidebar.locator('a[href="/users/invitations"]').click();
  await expect(page.getByRole("heading", { name: "Invitations", exact: true })).toBeVisible();
  await sidebar.locator('a[href="/users/roles"]').click();
  await expect(page.getByRole("heading", { name: "Roles & access" })).toBeVisible();
  await sidebar.locator('a[href="/users/activity"]').click();
  await expect(page.getByRole("heading", { name: "User activity" })).toBeVisible();
});

test("inviting several people skips the addresses that cannot be invited", async ({ page }) => {
  await page.goto("/users/invitations");
  await page.getByLabel("Email addresses").fill("one@acme.test, Two@acme.test\neve@acme.test new@acme.test nonsense one@acme.test");

  await expect(page.getByText("eve@acme.test: already a user")).toBeVisible();
  await expect(page.getByText("new@acme.test: already invited")).toBeVisible();
  await expect(page.getByText("nonsense: not an email address")).toBeVisible();

  await page.getByLabel("Role").selectOption({ label: "Company admin" });
  await page.getByRole("button", { name: "Send invitations (2)" }).click();

  await expect(page.getByText("2 invitations sent")).toBeVisible();
  await expect(page.getByText(/accept-invitation\?token=two@acme\.test/)).toBeVisible();
  expect(writes).toEqual([
    'POST /api/invitations {"email":"one@acme.test","role":"TenantAdmin"}',
    'POST /api/invitations {"email":"two@acme.test","role":"TenantAdmin"}',
  ]);
});

test("resending an invitation sends a new one before revoking the old one", async ({ page }) => {
  await page.goto("/users/invitations");
  await page.getByRole("button", { name: /Expired/ }).click();
  await expect(page.getByLabel("Select new@acme.test")).toHaveCount(0);

  await page.getByLabel("Select late@acme.test").check();
  await page.getByRole("button", { name: "Resend (1)" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Resend" }).click();

  await expect(page.getByText("1 invitation resent")).toBeVisible();
  expect(writes).toEqual([
    'POST /api/invitations {"email":"late@acme.test","role":"TenantAdmin"}',
    "DELETE /api/invitations/i2",
  ]);
});

test("roles & access warns about a single admin and lists each role's members", async ({ page }) => {
  await page.goto("/users/roles");
  await expect(page.getByText("Only one active company admin")).toBeVisible();
  await expect(page.getByText("View orders assigned to them")).toBeVisible();

  const members = page.locator("table").last();
  await expect(members.getByText("Ada Admin")).toBeVisible();
  await page.getByRole("group", { name: "Filter by role" }).getByRole("button", { name: /Employee/ }).click();
  await expect(members.getByText("Eve Employee")).toBeVisible();
  await expect(members.getByText("Bob Blocked")).toBeVisible();
  await expect(members.getByText("Ada Admin")).toHaveCount(0);
});

test("user activity narrows to what one user did and what was done to them", async ({ page }) => {
  await page.goto("/users/activity");
  await expect(page.getByText("3 of 3 events")).toBeVisible();

  await page.getByRole("button", { name: "Show activity for Ada Admin" }).click();
  await expect(page.getByText("2 of 3 events")).toBeVisible();

  await page.getByLabel("User", { exact: true }).selectOption({ label: "Bob Blocked (bob@acme.test)" });
  await expect(page.getByText("1 of 3 events")).toBeVisible();
  await page.getByLabel("Involvement").selectOption({ label: "Actions they took" });
  await expect(page.getByText("0 of 3 events")).toBeVisible();
  await expect(page.getByText("No matching activity")).toBeVisible();

  await page.getByRole("button", { name: "Clear filters" }).click();
  await expect(page.getByText("3 of 3 events")).toBeVisible();
});

test("the sidebar is in three groups separated by visible lines", async ({ page }) => {
  await page.goto("/dashboard");
  const sidebar = page.locator(".MuiDrawer-paper").filter({ has: page.getByText("MP Seller Tools", { exact: true }) });
  await expect(sidebar.locator('a[href="/dashboard"]')).toBeVisible();

  // Each entry's name, with the divider as a line, in the order they are drawn.
  const entries = await sidebar.locator(".MuiList-root .MuiListItemText-root, .MuiList-root > hr").evaluateAll((nodes) =>
    nodes.map((node) => (node.tagName === "HR" ? "---" : node.textContent)));
  // A solid line, not the theme's faded one.
  const line = sidebar.locator(".MuiList-root > hr").first();
  await expect(line).toHaveCSS("background-image", "none");
  await expect(line).not.toHaveCSS("background-color", "rgba(0, 0, 0, 0)");
  expect(entries).toEqual(["Dashboard", "Products", "Listings", "Orders", "Inventory", "eBay", "Amazon", "Walmart", "Sync queue", "---", "Tasks", "Audit", "---", "Settings", "Profile", "Users"]);
});