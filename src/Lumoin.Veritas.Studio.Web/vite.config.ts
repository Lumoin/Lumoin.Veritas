import { type Plugin, type UserConfig, defineConfig } from 'vite';
import { relative, resolve } from 'node:path';
import browserslist from 'browserslist';
import { browserslistToTargets } from 'lightningcss';
import fs from 'node:fs';

// Serves the WASM engine runtime (`/_framework/*`) straight from the Lumoin.Veritas.Studio.Wasm build
// output in dev — no copy into the app, so Vite does not watch hundreds of runtime files. The in-browser
// deployment boots the engine from `/_framework/dotnet.js` (src/services/wasm-engine.ts). Override the
// build dir with VERITAS_WASM_FRAMEWORK.
const wasmFrameworkDir = process.env.VERITAS_WASM_FRAMEWORK ?? resolve(
  __dirname, '..', 'Lumoin.Veritas.Studio.Wasm', 'bin', 'Debug', 'net10.0', 'wwwroot', '_framework');

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

      const file = resolve(wasmFrameworkDir, decodeURIComponent(url.slice('/_framework/'.length).split('?')[0]));
      if (!file.startsWith(wasmFrameworkDir) || !fs.existsSync(file)) {
        next();

        return;
      }

      response.setHeader('Content-Type', wasmContentType(file));
      response.setHeader('Cross-Origin-Resource-Policy', 'same-origin');
      fs.createReadStream(file).pipe(response);
    });
  }
});

// Veritas Studio — Vite (Rolldown) multi-page build. Mirrors the Verifable.Web conventions:
// Vite is the sole bundler, Lightning CSS (Rust) is the CSS transform, output is the static the
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

  // Cross-origin isolation so a future WASM/worker engine can use SharedArrayBuffer; the native
  // host and any production origin must serve the same headers.
  const crossOriginIsolation = {
    'Cross-Origin-Opener-Policy': 'same-origin',
    'Cross-Origin-Embedder-Policy': 'require-corp'
  };

  const config: UserConfig = {
    root: 'src',
    publicDir: '../public',
    plugins: isDev ? [wasmFrameworkDevPlugin()] : [],
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
              '/sparql': { target: engineOrigin, changeOrigin: true, secure: false },
              '/trace': { target: engineOrigin, changeOrigin: true, secure: false }
            }
          }
        }
      : {}),
    logLevel: 'info'
  };

  return config;
});
