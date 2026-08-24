import { defineConfig, devices } from '@playwright/test';

// All three engines are defined, so any one is selectable with `--project=chromium|firefox|webkit` and a bare
// `playwright test` runs the full matrix. Install them with `playwright install` (the pretest hook does this).
// The dev server serves the app and the WASM /_framework middleware, so the in-browser engine boots under ?engine=wasm.
const projects = [
  { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
  { name: 'webkit', use: { ...devices['Desktop Safari'] } }
];

export default defineConfig({
  testDir: './tests',
  // The PWA suite runs against the production build (vite preview) via playwright.pwa.config.ts, not the dev
  // server this config uses — exclude it here so it is only ever run by that config.
  testIgnore: '**/pwa.spec.ts',
  fullyParallel: true,
  // Each test boots a full .NET WASM runtime (~430 framework files); running one-per-CPU thrashes and
  // starves boots/fetches (timeouts). Cap concurrency so boots are reliable — fewer on a 2-core CI runner.
  workers: process.env.CI ? 2 : 4,
  reporter: 'list',
  timeout: 120_000,
  expect: { timeout: 90_000 },
  use: { baseURL: 'http://localhost:4317' },
  projects,
  // A dedicated strict port, sibling to the PWA config's 4318: the suite owns its server, and a
  // foreign process squatting the port fails the run loudly at startup instead of being silently
  // reused as the system under test.
  webServer: {
    command: 'npm run dev -- --port 4317 --strictPort',
    url: 'http://localhost:4317',
    reuseExistingServer: false,
    timeout: 120_000
  }
});
