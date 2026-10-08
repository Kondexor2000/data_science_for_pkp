import react from "@vitejs/plugin-react";
import { loadEnv } from "vite";
import { defineConfig } from "vitest/config";

export default defineConfig(({ mode }) => {
  const apiProxy = loadEnv(mode, ".", "VITE_").VITE_API_PROXY ?? "http://localhost:5000";
  return {
    plugins: [react()],
    test: {
      environment: "jsdom",
      setupFiles: "./src/test-setup.ts",
    },
    server: {
      host: "0.0.0.0",
      proxy: {
        "/api": apiProxy,
        "/health": apiProxy,
      },
    },
  };
});
