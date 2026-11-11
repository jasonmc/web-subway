import { defineConfig } from "vitest/config";

export default defineConfig({
  // Resolve generated Nix inputs through their paths inside the project.
  resolve: { preserveSymlinks: true },
  test: {
    environment: "jsdom",
    environmentOptions: { jsdom: { url: "https://example.com/" } },
    include: ["tests/**/*.test.fs.js"],
    setupFiles: ["./tests/setup.ts"],
    restoreMocks: true,
  },
});
