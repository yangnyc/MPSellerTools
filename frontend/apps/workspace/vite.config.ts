import path from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// Shared Creative-Tim-derived UI source lives in frontend/packages/ui/src and is
// consumed via the same bare-specifier style ("components/...", "examples/...")
// the upstream template used internally (see docs/template-adaptation.md).
const uiSrc = path.resolve(import.meta.dirname, "../../packages/ui/src");

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      components: path.resolve(uiSrc, "components"),
      examples: path.resolve(uiSrc, "examples"),
      context: path.resolve(uiSrc, "context"),
      assets: path.resolve(uiSrc, "assets"),
    },
  },
  server: {
    port: 5201,
    strictPort: true,
    proxy: {
      "/api": {
        // Points at Company A's TenantHost in dev; each tenant workspace build is
        // served by its own TenantHost instance in production (see docs/architecture.md).
        target: "https://localhost:7201",
        changeOrigin: true,
        secure: false,
      },
    },
  },
  build: {
    outDir: "../../../src/MPSellerTools.TenantHost/wwwroot",
    emptyOutDir: true,
  },
});
