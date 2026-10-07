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
    // Files aliased in from packages/ui/src must share the app's copy of these
    // singleton-sensitive libs: a second copy breaks React hooks / MUI theme
    // context. The hoisted workspace install leaves one copy of each; this
    // keeps it that way if a version ever ends up nested under a package.
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
