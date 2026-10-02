import { defineConfig } from "@playwright/test";

// UI regressions use mocked API responses and need no LocalDB or seeded accounts.
export default defineConfig({
  testDir: "./ui-tests",
  use: {
    baseURL: "http://localhost:5100",
    viewport: { width: 1440, height: 1000 },
    channel: process.env.PLAYWRIGHT_CHANNEL,
  },
  webServer: {
    command: "node ../../node_modules/vite/bin/vite.js",
    cwd: "../../frontend/apps/platform",
    url: "http://localhost:5100",
    reuseExistingServer: !process.env.CI,
  },
});
