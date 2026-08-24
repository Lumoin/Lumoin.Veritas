import { type Plugin, type ProxyOptions, type UserConfig, defineConfig } from 'vite';
import { relative, resolve } from 'node:path';
import browserslist from 'browserslist';
import { browserslistToTargets } from 'lightningcss';
import { createHash } from 'node:crypto';
import fs from 'node:fs';

// The WASM engine runtime (`/_framework/*`) comes from the Lumoin.Veritas.Studio.Wasm build. Dev serves it
// straight from the DEBUG build output (fast iteration, debuggable, no copy so Vite does not watch hundreds of
// runtime files); the production build copies it from a RELEASE PUBLISH (trimmed, Brotli+gzip precompressed,
// no sourcemaps/HotReload). The in-browser deployment boots from `/_framework/dotnet.js` (src/services/wasm-engine.ts).
// Override either directory with VERITAS_WASM_FRAMEWORK / VERITAS_WASM_FRAMEWORK_RELEASE.
const wasmDevFrameworkDir = process.env.VERITAS_WASM_FRAMEWORK ?? resolve(
  __dirname, '..', 'Lumoin.Veritas.Studio.Wasm', 'bin', 'Debug', 'net10.0', 'wwwroot', '_framework');
const wasmReleaseFrameworkDir = process.env.VERITAS_WASM_FRAMEWORK_RELEASE ?? resolve(
  __dirname, '..', 'Lumoin.Veritas.Studio.Wasm', 'bin', 'Release', 'net10.0', 'publish', 'wwwroot', '_framework');

const wasmContentType = (file: string): string => {
  switch (file.slice(file.lastIndexOf('.'))) {
    case '.js':
    case '.mjs':
      return 'text/javascript';
    case '.wasm':
      return 'application/wasm';
    case '.json':
    case '.map':
      return 'application/json';
    default:
      return 'application/octet-stream';
  }
};

const wasmFrameworkDevPlugin = (): Plugin => ({
  name: 'veritas-wasm-framework-dev',
  apply: 'serve',
  configureServer(server) {
    server.middlewares.use((request, response, next) => {
      const url = request.url ?? '';
      if (!url.startsWith('/_framework/')) {
        next();

        return;
      }

      const file = resolve(wasmDevFrameworkDir, decodeURIComponent(url.slice('/_framework/'.length).split('?')[0]));
      if (!file.startsWith(wasmDevFrameworkDir) || !fs.existsSync(file)) {
        // The Debug framework references its hot-reload support module without shipping it; an empty
        // module keeps the dev console clean, and every other miss stays a real miss.
        if (/\.lib\.module\.js$/.test(url.split('?')[0])) {
          response.setHeader('Content-Type', 'text/javascript');
          response.end('export {}');

          return;
        }

        next();

        return;
      }

      response.setHeader('Content-Type', wasmContentType(file));
      response.setHeader('Cross-Origin-Resource-Policy', 'same-origin');
      fs.createReadStream(file).pipe(response);
    });
  }
});

// Copies the WASM engine runtime into the production build so the self-contained app boots the in-browser
// engine without the dev middleware. The standalone deployments need /_framework on disk: the offline /
// host-anywhere static build, and `veritas serve` static-hosting dist. The dev build serves it straight
// from the Studio.Wasm output (wasmFrameworkDevPlugin), so this copy runs on build only.
const wasmFrameworkBuildPlugin = (): Plugin => ({
  name: 'veritas-wasm-framework-build',
  apply: 'build',
  closeBundle() {
    const destination = resolve(__dirname, 'dist', '_framework');
    const releaseExists = fs.existsSync(wasmReleaseFrameworkDir);

    // A production build (CI, or an explicit VERITAS_REQUIRE_RELEASE_FRAMEWORK) must use the Release publish —
    // never the Debug fallback. Fail hard rather than silently shipping a Debug runtime when it is missing.
    const requireRelease = process.env.CI === 'true' || process.env.VERITAS_REQUIRE_RELEASE_FRAMEWORK !== undefined;
    if (!releaseExists && requireRelease) {
      this.error(
        `Release WASM publish not found at ${wasmReleaseFrameworkDir}, but a production build is required ` +
        '(CI, or VERITAS_REQUIRE_RELEASE_FRAMEWORK set). Run `dotnet publish -c Release` on Lumoin.Veritas.Studio.Wasm first.');
    }

    // Prefer the Release publish (the real production runtime); locally, fall back to the Debug build with a loud
    // warning so `vite build` still works for a quick check, while making clear it is not a production runtime.
    const source = releaseExists ? wasmReleaseFrameworkDir : wasmDevFrameworkDir;
    if (!fs.existsSync(source)) {
      this.warn(
        `WASM engine runtime not found (Release publish: ${wasmReleaseFrameworkDir}; Debug: ${wasmDevFrameworkDir}); ` +
        'the static build will not include the in-browser engine. Run `dotnet publish -c Release` on ' +
        'Lumoin.Veritas.Studio.Wasm (or set VERITAS_WASM_FRAMEWORK_RELEASE) before `vite build`.');

      return;
    }

    if (!releaseExists) {
      this.warn(
        `Release publish not found at ${wasmReleaseFrameworkDir}; copying the DEBUG _framework — NOT a production ` +
        'runtime (untrimmed, uncompressed, sourcemaps + HotReload). Run `dotnet publish -c Release` for the real build.');
    }

    fs.cpSync(source, destination, { recursive: true });
  }
});

// Generates the production service worker so the static deployment is installable and works offline. It walks
// the finished dist (after the _framework copy above), embeds the precache manifest plus a content version, and
// prepends both to the hand-written worker logic (src/sw/service-worker.js). Embedding the version means the
// worker's bytes change whenever the output changes, so the browser re-installs and never serves a stale shell.
// Build-only: the dev server registers no worker (studio.ts gates registration on import.meta.env.PROD).
const pwaServiceWorkerPlugin = (): Plugin => ({
  name: 'veritas-pwa-service-worker',
  apply: 'build',
  // order 'post' + sequential pins this after (and awaiting) the _framework copy's closeBundle, so the walk
  // always sees a complete dist — even if that copy ever becomes asynchronous.
  closeBundle: {
    order: 'post',
    sequential: true,
    handler() {
      const distDir = resolve(__dirname, 'dist');
      const logicPath = resolve(__dirname, 'src', 'sw', 'service-worker.js');
      if (!fs.existsSync(logicPath)) {
        this.warn(`Service worker logic not found at ${logicPath}; the static build will have no offline worker.`);

        return;
      }

      // Precache every served file except sourcemaps, the pre-compressed twins (the loader fetches the plain URL
      // and the server negotiates encoding), and the worker file itself.
      const assets: string[] = [];
      const collect = (dir: string): void => {
        for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
          const full = resolve(dir, entry.name);
          if (entry.isDirectory()) {
            collect(full);

            continue;
          }

          const rel = relative(distDir, full).replaceAll('\\', '/');
          if (rel.endsWith('.map') || rel.endsWith('.gz') || rel.endsWith('.br') || rel === 'service-worker.js') {
            continue;
          }

          assets.push(rel);
        }
      };

      collect(distDir);
      // Default (code-unit) sort, not localeCompare: locale-independent, so the version hash is reproducible
      // across build environments and does not spuriously change when only the runner's locale differs.
      assets.sort();

      // Version = a content hash over every precached file, so any byte change yields a new worker (re-install).
      const hash = createHash('sha256');
      for (const rel of assets) {
        hash.update(rel);
        hash.update(fs.readFileSync(resolve(distDir, rel)));
      }

      const manifest = { version: hash.digest('hex').slice(0, 12), shell: 'index.html', assets };
      const worker = `self.veritasAssets = ${JSON.stringify(manifest)};\n${fs.readFileSync(logicPath, 'utf8')}`;
      fs.writeFileSync(resolve(distDir, 'service-worker.js'), worker);

      // GitHub Pages runs Jekyll, which omits underscore-prefixed paths — without this marker it 404s the whole
      // _framework runtime. Written AFTER the walk so it is NOT precached: the app never fetches it, and a dev
      // or static server that declines to serve dotfiles would 404 it and abort the atomic precache. Harmless elsewhere.
      fs.writeFileSync(resolve(distDir, '.nojekyll'), '');
    }
  }
});

// A stub of a federated SPARQL endpoint (a materials registry) for the SERVICE-across-a-trust-boundary
// demo: the in-browser engine POSTs a sub-query here over fetch and joins the result into its local graph.
// Returns a fixed result set; the real endpoint is the CLI server. Dev-only.
const federatedRegistry = JSON.stringify({
  head: { vars: ['cathode', 'certifier'] },
  results: {
    bindings: [
      { cathode: { type: 'uri', value: 'https://veritas.app/ns/battery#NMC811' }, certifier: { type: 'literal', value: 'IEC 62133' } },
      { cathode: { type: 'uri', value: 'https://veritas.app/ns/battery#NMC622' }, certifier: { type: 'literal', value: 'UL 1642' } }
    ]
  }
});

const federatedSparqlStubPlugin = (): Plugin => ({
  name: 'veritas-federated-sparql-stub',
  apply: 'serve',
  configureServer(server) {
    server.middlewares.use((request, response, next) => {
      if (!(request.url ?? '').startsWith('/federated/sparql')) {
        next();

        return;
      }

      // Drain the sub-query body, then answer with the registry's result set.
      request.on('data', () => undefined);
      request.on('end', () => {
        response.setHeader('Content-Type', 'application/sparql-results+json');
        response.setHeader('Cross-Origin-Resource-Policy', 'same-origin');
        response.end(federatedRegistry);
      });
    });
  }
});

// Veritas Studio — Vite (Rolldown) multi-page build. Vite is the sole bundler,
// Lightning CSS (Rust) is the CSS transform, output is the static the
// native host serves. The engine is reached through the host's window.veritas bridge in the native
// shell; in browser/dev the same VeritasTransport interface is satisfied over HTTP, so the dev
// server proxies the engine's SPARQL Protocol + trace endpoints to the local CLI server.

const projectRoot = resolve(__dirname);

// Recursively discover HTML files under src/ and map them to multi-page route keys (one app today;
// MPA-capable so a standalone view — e.g. a kiosk graph — costs only a new .html).
const getHtmlInputs = (root: string): Record<string, string> => {
  const inputs: Record<string, string> = {};
  const traverse = (dir: string): void => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = resolve(dir, entry.name);
      if (entry.isDirectory()) {
        traverse(full);
      } else if (entry.name.endsWith('.html')) {
        inputs[relative(root, full).replace(/\.html$/, '').replaceAll('\\', '/')] = full;
      }
    }
  };

  traverse(root);
  return inputs;
};

export default defineConfig(({ mode }) => {
  const isDev = mode === 'development' || mode === 'test';
  const srcRoot = resolve(projectRoot, 'src');

  // The local Veritas CLI server (`veritas serve`, default :3030) for the browser/dev transport.
  const engineOrigin = process.env.VERITAS_DEV_ENGINE ?? 'http://localhost:3030';

  // When no CLI answers at the engine origin, a proxied first-party face answers an empty JSON
  // document instead of a connection error: the shell reads it as face-absent and degrades, and the
  // browser console stays clean of dev-only noise. With a CLI listening the proxy passes through.
  const quietWhenEngineDown: ProxyOptions = {
    target: engineOrigin,
    changeOrigin: true,
    secure: false,
    configure: (proxy) => {
      proxy.on('error', (_error, _request, response) => {
        if ('writeHead' in response) {
          if (!response.headersSent) {
            response.writeHead(200, { 'content-type': 'application/json' });
          }

          response.end('{}');
        }
      });
    }
  };

  // Cross-origin isolation so a future WASM/worker engine can use SharedArrayBuffer; the native
  // host and any production origin must serve the same headers.
  const crossOriginIsolation = {
    'Cross-Origin-Opener-Policy': 'same-origin',
    'Cross-Origin-Embedder-Policy': 'require-corp'
  };

  const config: UserConfig = {
    root: 'src',
    publicDir: '../public',
    // Root by default (CLI-serve / root hosting). Override (e.g. VITE_BASE=/Veritas/) for a subpath host
    // such as GitHub Pages; the base flows through on its own — studio.ts boots _framework from
    // import.meta.env.BASE_URL and registers the service worker under the same base-derived scope.
    base: process.env.VITE_BASE ?? '/',
    plugins: isDev ? [wasmFrameworkDevPlugin(), federatedSparqlStubPlugin()] : [wasmFrameworkBuildPlugin(), pwaServiceWorkerPlugin()],
    build: {
      outDir: '../dist',
      emptyOutDir: true,
      assetsInlineLimit: 0,
      minify: true,
      cssMinify: 'lightningcss',
      cssCodeSplit: true,
      rollupOptions: {
        input: getHtmlInputs(srcRoot),
        output: {
          entryFileNames: 'assets/[name]-[hash].js',
          chunkFileNames: 'assets/[name]-[hash].js',
          assetFileNames: 'assets/[name]-[hash][extname]'
        }
      }
    },
    css: {
      devSourcemap: true,
      // Author modern native CSS (nesting, @layer, color-mix, container queries, OKLCH); Lightning
      // CSS downlevels/prefixes to the browserslist targets.
      transformer: 'lightningcss',
      lightningcss: { targets: browserslistToTargets(browserslist()) }
    },
    ...(isDev
      ? {
          server: {
            fs: { strict: true },
            headers: {
              ...crossOriginIsolation,
              'X-Content-Type-Options': 'nosniff',
              'Referrer-Policy': 'strict-origin-when-cross-origin'
            },
            // Browser/dev front door: proxy the engine's SPARQL Protocol + (planned) streaming and
            // trace surfaces to the local CLI server so the page runs same-origin against it. The
            // native shell does not use this path — it bridges to the in-process engine directly.
            // SSE is plain HTTP (no socket upgrade), so no ws:true — streaming + trace ride EventSource.
            proxy: {
              '/sparql': quietWhenEngineDown,
              '/trace': quietWhenEngineDown,
              // Exact-path (regex) so the literal-diagnostics.ts module URL never matches the proxy prefix.
              '^/literal-diagnostics$': quietWhenEngineDown,
              // Exact-path likewise for the editor faces: completion-popup.ts, turtle-completion.ts and
              // query-completion.ts are served module URLs a prefix rule would swallow.
              '^/completion$': quietWhenEngineDown,
              '^/turtle-completion$': quietWhenEngineDown,
              '^/editor-vocabulary$': quietWhenEngineDown,
              // The worlds face: the listing and its sub-routes (fork, drop, query, update, diff) are
              // first-party like /trace; anchored so served module URLs (worlds-strip.ts) never match.
              '^/worlds(/.*)?$': quietWhenEngineDown,
              // The page probes /config at boot to learn the origin hosts a server-side engine; proxy it
              // too so the dev page selects the CLI's in-process engine (HTTP) when a CLI is running. The
              // empty-document fallback carries no engine marker, so the probe still answers "no server
              // engine" and the page boots the in-browser WASM engine.
              '/config': quietWhenEngineDown
            }
          }
        }
      : {}),
    logLevel: 'info'
  };

  return config;
});
