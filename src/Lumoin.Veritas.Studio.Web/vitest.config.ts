import { defineConfig } from 'vitest/config';

// The unit lane: pure-module tests living beside their sources under src/. The Playwright suites
// under tests/ are end-to-end and run by their own configs (playwright.config.ts and
// playwright.pwa.config.ts); the include keeps the two lanes structurally disjoint, so neither
// runner ever picks up the other's files.
export default defineConfig({
  test: {
    include: ['src/**/*.test.ts'],
    environment: 'node'
  }
});
