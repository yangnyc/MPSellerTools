import { defineConfig } from "@playwright/test";

const platform = "http://localhost:5100";
const workspace = "http://localhost:5201";

// UI regressions use mocked API responses and need no LocalDB or seeded accounts.
// Specs named workspace-* run against the tenant workspace, the rest against
// the platform console.
export default defineConfig({
  testDir: "./ui-tests",
  use: {
    viewport: { width: 1440, height: 1000 },
    channel: process.env.PLAYWRIGHT_CHANNEL,
  },
  projects: [
    { name: "platform", testIgnore: /workspace-.*\.spec\.ts/, use: { baseURL: platform } },
    { name: "workspace", testMatch: /workspace-.*\.spec\.ts/, use: { baseURL: workspace } },
  ],
  webServer: [
    {
      command: "npm run dev",
      cwd: "../../frontend/apps/platform",
      url: platform,
      reuseExistingServer: !process.env.CI,
    },
    {
      command: "npm run dev",
      cwd: "../../frontend/apps/workspace",
      url: workspace,
      reuseExistingServer: !process.env.CI,
    },
  ],
});
