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
    port: 5100,
    strictPort: true,
    proxy: {
      "/api": {
        target: "https://localhost:7100",
        changeOrigin: true,
        secure: false,
      },
    },
  },
  build: {
    outDir: "../../../src/MPSellerTools.PlatformHost/wwwroot",
    emptyOutDir: true,
  },
});
