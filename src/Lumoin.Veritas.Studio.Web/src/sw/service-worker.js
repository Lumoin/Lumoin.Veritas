// Veritas Studio service worker — offline-first for the static (model-A) deployment. The build prepends
// `self.veritasAssets = { version, shell, assets }` to this script, so the worker's bytes change whenever the
// output changes → the browser detects an update and re-installs → no stale serve. It precaches the whole app
// (shell + WASM runtime + datasets) on install, drops prior versions on activate, and serves same-origin GETs
// cache-first. The live engine endpoints (/sparql, /config, /trace) and any cross-origin request (a SPARQL
// SERVICE federation hop) are never cached — they always reach the network.

const CACHE_NAME = `veritas-studio-${self.veritasAssets.version}`;
const LIVE_ENGINE_PATHS = ['/sparql', '/config', '/trace'];

self.addEventListener('install', (event) => {
  // skipWaiting + clients.claim (on activate) make the very FIRST visit immediately offline-capable and let an
  // update take control without a manual reload — the right call for this showcase app. Deliberate trade-off:
  // if a new version deploys while a tab is open, that tab may need one reload to avoid a cache-version mix
  // mid-boot (content-hashed asset names). Acceptable here; deploys are infrequent and a reload self-heals it.
  event.waitUntil((async () => {
    const cache = await caches.open(CACHE_NAME);
    await cache.addAll(self.veritasAssets.assets);
    await self.skipWaiting();
  })());
});

self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    const names = await caches.keys();
    await Promise.all(names
      .filter((name) => name.startsWith('veritas-studio-') && name !== CACHE_NAME)
      .map((name) => caches.delete(name)));
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', (event) => {
  const request = event.request;
  if (request.method !== 'GET') {
    return;
  }

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) {
    return; // cross-origin (e.g. a SPARQL SERVICE federation hop) → straight to the network
  }

  if (LIVE_ENGINE_PATHS.some((path) => url.pathname.endsWith(path))) {
    return; // the live CLI engine endpoints are dynamic — never cached
  }

  event.respondWith(cacheFirst(request));
});

/**
 * Serves a request from the precache, falling back to the network, and — for a navigation that fails offline —
 * to the cached app shell so the single-page app still loads.
 * @param {Request} request The request to satisfy.
 * @returns {Promise<Response>} The cached or fetched response.
 */
async function cacheFirst(request) {
  const cache = await caches.open(CACHE_NAME);
  // ignoreVary: the precache stored each asset under a plain GET, but the page re-requests Vite's `crossorigin`
  // module/stylesheet assets with different headers — honouring Vary would miss the cached entry and break offline.
  const cached = await cache.match(request, { ignoreSearch: true, ignoreVary: true });
  if (cached !== undefined) {
    return cached;
  }

  try {
    return await fetch(request);
  } catch (error) {
    if (request.mode === 'navigate') {
      const shell = await cache.match(self.veritasAssets.shell, { ignoreSearch: true, ignoreVary: true });
      if (shell !== undefined) {
        return shell;
      }
    }

    throw error;
  }
}
