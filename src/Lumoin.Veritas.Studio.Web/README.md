# Veritas Studio — web app

Framework-free TS + modern CSS, built with Vite (Rolldown) + Lightning CSS; typechecked with `tsgo`.
The graph view is BabylonJS (lazy-loaded). No UI framework.

## Run it locally

```sh
npm install
npm run dev      # Vite dev server with HMR — open the printed http://localhost:<port>
```

There is one dataset-loading path, and startup takes it: the first dataset in `public/datasets/manifest.json`
is fetched, loaded into the engine, and its showcase query run — exactly what picking a dataset from the
switch, or opening a local RDF file, does. Every panel paints from those runs: the table and the **Raw**
answer document from the showcase query, the graph from the dataset's graph query, the **SHACL** view from
validating the data against the shapes the dataset ships, the "why these terms" trace from the engine's live
decisions, and the status bar from the run's own rows, timing and traced plan. Nothing is painted from
content that did not come through the engine — where a source cannot answer, the panel says so. **Run ▸**
re-executes the active editor buffer (SPARQL, or SHACL against the in-browser engine).

**Shareable links.** The address bar always names the dataset on screen — `?dataset=social` — so copying it at
any moment shares that session, and opening such a link starts the page on that dataset. The same parameter
also takes an absolute `https://` URL of a Turtle document: the page fetches it (a plain cross-origin request
with no credentials, so the host must allow the origin through CORS) and opens it the way an opened file
opens, named by the URL's last path segment, with a generic explore query and no shapes. Switching datasets
rewrites the parameter in place (no browser-history entries to walk back through), a value that is neither a
listed id nor an `https://` URL falls back to the first dataset and corrects the address to match, and a
dataset opened from a local file drops the parameter rather than offering a link that cannot load.

```sh
npm run build    # bundle to dist/ (Lightning CSS, content-hashed)
npm run preview  # serve the built dist/
npm run typecheck# tsgo --noEmit
npm run test     # vitest in browser mode (real Chromium)
```

## Where the engine comes from (one seam, three deployments)

`src/services/veritas-transport.ts` resolves the transport at boot:
- **desktop shell** → `window.veritas` (the native host bridge to the in-process engine),
- **in-browser** → `window.veritasEngine` (the WASM engine; boot via `src/services/wasm-engine.ts`),
- **server / dev** → HTTP to the CLI server (the Vite dev server proxies `/sparql`, `/trace`).

The app code above the seam is identical across all three.
