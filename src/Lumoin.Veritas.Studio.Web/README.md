# Veritas Studio — web app

Framework-free TS + modern CSS, built with Vite (Rolldown) + Lightning CSS; typechecked with `tsgo`.
The graph view is BabylonJS (lazy-loaded). No UI framework.

## Run it locally

```sh
npm install
npm run dev      # Vite dev server with HMR — open the printed http://localhost:<port>
```

With no engine attached the shell renders the **bundled battery-passport sample**: the editor (SPARQL ·
SHACL · Turtle · JSONata · JSON-LD · SKOS · OWL) with the cross-trust-boundary `SERVICE` query, the
results table/graph/raw/SHACL views, the "why these terms" trace, and the light/dark toggle. **Run ▸**
executes against the resolved transport and falls back to the sample when no engine is present.

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
