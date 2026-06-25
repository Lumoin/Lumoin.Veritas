# Veritas Studio — the native host (Photino-like, our own way)

The desktop shell that gives Studio what a sandboxed browser cannot: local/domain filesystem access,
sitting alongside CAE/CAD tooling, large local models, native interop, offline. A **thin native window
hosting the OS webview, the Studio web app on top, bridged to the in-process `VeritasEngine`.** This is
a skeleton (staged; not yet in the `.slnx`) — faithful to the Photino.NET API but verify versions at
implementation.

## Layering (the seam is the point)

```
Program (boot)                         opens the window, picks dev/prod loading, wires the bridge
   │
StudioWindow  (our thin wrapper)       OUR managed surface: Title/Size, MessageReceived, Send(),
   │  wraps ▼                          LoadDevServer()/LoadBundled(). The native layer is swappable
PhotinoWindow (Photino.Native, Apache-2.0)   here — only StudioWindow changes if we replace it.
   │  embeds ▼
OS webview     WebView2 (Win) · WKWebView (mac) · WebKitGTK (Linux)
   │  loads ▼
Studio web app (Vite build)            speaks `window.veritas` (host-bridge.ts) → no HTTP
```

Why wrap Photino rather than use it raw: the app and the bridge protocol are ours; Photino.Native is
just the cheapest way to get three OS webviews today. Wrapping it keeps "replace the native layer"
(our own native lib, or a different one) a one-file change behind `StudioWindow`, not a rewrite.

## Loading

- **Dev**: `StudioWindow.LoadDevServer(http://localhost:5173)` — the Vite dev server (HMR). The engine
  is still in-process via the bridge; only the assets come from Vite.
- **Prod**: `StudioWindow.LoadBundled()` registers a custom **`veritas://`** scheme handler that serves
  the Vite `dist/` (shipped as embedded resources or beside the exe). No localhost server, no ports.

## The bridge protocol (`window.veritas` ⇄ host)

A line-delimited JSON request/response over Photino's `WebMessageReceived` (JS→.NET) and
`SendWebMessage` (.NET→JS); the page side is `app/src/services/host-bridge.ts`.

| Direction | Message | Meaning |
|---|---|---|
| JS→.NET | `{ id, method: "runSparql", query }` | run a query against the in-process engine |
| .NET→JS | `{ id, ok: true, results }` | W3C SPARQL-results-JSON |
| .NET→JS | `{ id, ok: false, error }` | `{ error, diagnostics[] }` (span-bearing) |
| JS→.NET | `{ method: "subscribeTrace" }` | start forwarding trace events |
| .NET→JS | `{ kind: "trace", event }` | one `TraceEvent` (correlation-keyed) |

The same DTOs the CLI server's planned SPARQL-results-JSON + trace-SSE surfaces use — the bridge and the
HTTP path are two transports over one engine, so `VeritasTransport` stays the only thing the app knows.

## Files (skeleton)

- `Program.cs` — `[STAThread]` boot: build the engine over a store, create `StudioWindow`, wire
  `VeritasBridge`, dev-or-prod load, run the message loop.
- `StudioWindow.cs` — the thin managed wrapper over `PhotinoWindow`.
- `VeritasBridge.cs` — parses requests, calls the engine, serializes SPARQL-results-JSON, forwards trace.
- `Lumoin.Veritas.Studio.Host.csproj` — references `Photino.NET` + `Lumoin.Veritas.Database`.

## Open items at implementation

- Engine wiring: `VeritasBridge` takes a `Func<string, CancellationToken, Task<string>>` that returns
  SPARQL-results-JSON — point it at `VeritasEngine` + the engine's `SparqlResultsJson` writer.
- Trace: subscribe a `TraceHandler<QueryTraceEvent>` (+ `NetworkGovernanceTraceEvent`) and project each
  to the wire DTO, keyed by `CorrelationId`.
- `dist/` packaging for the `veritas://` scheme (embedded resources vs. content files).
- Access context: the bridge carries the operator role/credential into the engine's `AccessContext`
  so the access-gated SERVICE path behaves as in the demo.
- Worktree/placement (per the decision record §6) before this enters `src/` + the `.slnx`.
