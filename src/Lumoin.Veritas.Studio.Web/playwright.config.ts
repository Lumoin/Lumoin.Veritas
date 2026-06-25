import { defineConfig, devices } from '@playwright/test';

// Browser-selectable to keep runs light: chromium only by default; set ALL_BROWSERS=1 for the full matrix
// (firefox/webkit need `npx playwright install firefox webkit` first). The dev server serves the app and
// the WASM /_framework middleware, so the in-browser engine boots under ?engine=wasm.
const allBrowsers = process.env.ALL_BROWSERS === '1' || process.env.ALL_BROWSERS === 'true';

const projects = [
  { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ...(allBrowsers
    ? [
        { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
        { name: 'webkit', use: { ...devices['Desktop Safari'] } }
      ]
    : [])
];

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  reporter: 'list',
  timeout: 120_000,
  expect: { timeout: 90_000 },
  use: { baseURL: 'http://localhost:5173' },
  projects,
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:5173',
    reuseExistingServer: true,
    timeout: 120_000
  }
});
