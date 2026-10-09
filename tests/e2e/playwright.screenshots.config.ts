import { defineConfig } from "@playwright/test";

const platform = "http://localhost:5100";
const workspace = "http://localhost:5201";

// Takes the README's pictures (see screenshots/readme.spec.ts). Like the UI
// regressions, it runs against the Vite dev servers with mocked API responses.
export default defineConfig({
  testDir: "./screenshots",
  workers: 1,
  use: {
    viewport: { width: 1440, height: 900 },
    channel: process.env.PLAYWRIGHT_CHANNEL,
  },
  projects: [
    { name: "platform", grep: /platform console/, use: { baseURL: platform } },
    { name: "workspace", grepInvert: /platform console/, use: { baseURL: workspace } },
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
