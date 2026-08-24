import { defineConfig, devices } from '@playwright/test';

// PWA / offline coverage runs against the PRODUCTION build (vite build → vite preview), where the service
// worker is registered (studio.ts gates registration on import.meta.env.PROD) — unlike the dev server the
// main config uses. A separate config so the dev-mode suite never runs these (and these never run in dev).
// One worker: the offline test toggles the whole browser context offline, so it must not run beside others.
export default defineConfig({
  testDir: './tests',
  testMatch: '**/pwa.spec.ts',
  fullyParallel: false,
  workers: 1,
  reporter: 'list',
  timeout: 180_000,
  expect: { timeout: 90_000 },
  use: { baseURL: 'http://localhost:4318' },
  // All three engines, selectable with `--project=chromium|firefox|webkit`; a bare run does the full matrix.
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
    { name: 'webkit', use: { ...devices['Desktop Safari'] } }
  ],
  webServer: {
    command: 'npm run build && npm run preview -- --port 4318 --strictPort',
    url: 'http://localhost:4318',
    reuseExistingServer: false,
    timeout: 180_000
  }
});
