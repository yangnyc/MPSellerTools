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
    // packages/ui has its own node_modules (no workspace hoisting in this
    // environment — see docs/template-adaptation.md), so without this, files
    // aliased in from packages/ui/src resolve a second copy of these
    // singleton-sensitive libs, breaking React hooks / MUI theme context.
    dedupe: [
      "react",
      "react-dom",
      "react-router-dom",
      "@emotion/react",
      "@emotion/styled",
      "@mui/material",
    ],
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
