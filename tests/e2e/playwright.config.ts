import { defineConfig } from "@playwright/test";

// Assumes the dev stack is already running (scripts/Start-Dev.ps1) and demo
// data has been seeded (scripts/Setup-Dev.ps1) — these are browser smoke
// tests against the real running application, not something that spins up
// its own server (the .NET hosts + LocalDB + provisioned tenants are not
// something Playwright's webServer option can reasonably bootstrap).
export default defineConfig({
  testDir: "./tests",
  fullyParallel: false,
  retries: 0,
  reporter: "list",
  use: {
    ignoreHTTPSErrors: true,
    trace: "retain-on-failure",
  },
});
