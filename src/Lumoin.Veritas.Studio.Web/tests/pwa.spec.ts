// PWA coverage for the static (model-A) deployment. Unlike the in-browser suite (which runs against the dev
// server), this runs against the PRODUCTION build via `vite preview`, where the service worker is registered
// (studio.ts gates registration on import.meta.env.PROD). It asserts the install prerequisites (manifest,
// icon, theme-color, an active worker) and — the point of the PWA — that the engine boots FULLY OFFLINE from
// the worker's precache. Driven through playwright.pwa.config.ts. Chromium only.
import { test, expect } from '@playwright/test';

/** The in-browser engine surface the page exposes once booted. */
interface StudioWindow {
  veritasEngine?: { runSparql(query: string): Promise<string> };
}

/** A SPARQL results document, as the engine returns it. */
interface ResultsDocument {
  results?: { bindings: unknown[] };
}

test.describe('PWA (static deployment)', () => {
  test('is installable: manifest, icon, theme-color, and a registered service worker', async ({ page }) => {
    await page.goto('/');

    const manifestHref = await page.getAttribute('link[rel="manifest"]', 'href');
    expect(manifestHref).not.toBeNull();

    const manifest = await page.evaluate(async (href) => {
      const response = await fetch(href as string);

      return { ok: response.ok, body: (await response.json()) as Record<string, unknown> };
    }, manifestHref);
    expect(manifest.ok).toBe(true);
    expect(manifest.body.name).toBeTruthy();
    expect(manifest.body.start_url).toBeTruthy();
    expect(['standalone', 'fullscreen', 'minimal-ui']).toContain(manifest.body.display);
    expect((manifest.body.icons as unknown[]).length).toBeGreaterThan(0);

    expect(await page.getAttribute('meta[name="theme-color"]', 'content')).toBeTruthy();
    expect(await page.evaluate(async () => (await fetch('icon.svg')).status)).toBe(200);

    await page.waitForFunction(async () => {
      const registration = await navigator.serviceWorker.getRegistration();

      return registration?.active != null;
    }, undefined, { timeout: 90_000 });
  });

  test('boots the in-browser engine fully offline from the service-worker cache', async ({ page, context }) => {
    await page.goto('/');
    // The worker claims the page on activate, which runs only after install's atomic precache completes — so
    // waiting for it to control the page also gates on the precache being done before we cut the network.
    await page.waitForFunction(() => navigator.serviceWorker.controller != null, undefined, { timeout: 90_000 });

    await context.setOffline(true);
    await page.reload();

    // With no network, the shell + WASM runtime + default dataset all come from the precache; /config can't be
    // reached, so the page auto-boots the in-browser engine (the static-host path) entirely from cache.
    await page.waitForFunction(() => (globalThis as unknown as StudioWindow).veritasEngine !== undefined, undefined, { timeout: 90_000 });
    // The engine boots on an empty graph and the startup dataset follows down the ordinary loading path, so
    // the readout naming it is the signal that its Turtle — served from the precache here — is loaded.
    await expect(page.locator('[data-testid="active-dataset"]')).not.toBeEmpty({ timeout: 90_000 });

    // The worlds face rides the same tier: the strip presents the primary world from the in-browser engine —
    // fully offline, on the production bundle a static host (GitHub Pages) serves.
    await expect(page.locator('[data-testid="worlds-strip"]')).toBeVisible();
    await expect(page.locator('[data-testid="world-select"]')).toHaveValue('main');
    await expect(page.locator('[data-testid="world-state"]')).toHaveText(/^[0-9a-f]{16}$/);

    const json = await page.evaluate(() => (globalThis as unknown as StudioWindow).veritasEngine!.runSparql(
      'PREFIX bat: <https://veritas.app/ns/battery#> SELECT ?p WHERE { ?p a bat:Battery }'));
    const document = JSON.parse(json) as ResultsDocument;
    expect(document.results?.bindings.length ?? 0).toBeGreaterThan(0);

    // A shared dataset link is just another navigation: still offline, the worker answers it with the cached
    // app shell (it matches ignoring the query string) and the linked dataset comes from the precache too.
    await page.goto('/?dataset=campus');
    await expect(page.locator('[data-testid="active-dataset"]')).toHaveText('University campus');

    await context.setOffline(false);
  });
});
