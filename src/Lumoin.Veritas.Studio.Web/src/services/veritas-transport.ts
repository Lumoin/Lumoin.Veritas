// The seam between the Studio app and the Veritas engine. The app never reaches the engine directly:
// it codes against VeritasTransport, and the right implementation is resolved once at boot —
//   • NativeVeritasTransport — the desktop shell injects `window.veritas`, a typed message channel
//     over the host's WebMessageReceived/SendWebMessage bridge into the IN-PROCESS engine (no HTTP).
//   • HttpVeritasTransport — a plain browser / dev front door talks to the local CLI server over the
//     SPARQL Protocol (the Vite dev server proxies /sparql, /trace to it).
// Both return discriminated unions (never throw a string), mirroring the Verifable.Web seams. A new
// surface (a WASM-in-worker engine, say) is a third implementation, not a change to any caller.

import type { SparqlQueryError, SparqlResultsDocument } from '../components/sparql-results';

export type FetchLike = typeof globalThis.fetch;

export type SparqlOutcome =
  | { ok: true; results: SparqlResultsDocument }
  | { ok: false; error: SparqlQueryError };

/** A trace event the "why these terms" panel renders; the engine's trace bus, projected to the wire. */
export interface TraceEvent {
  correlationId: string;
  sequence: number;
  kind: string;
  term?: string;
  detail: string;
}

export interface VeritasTransport {
  /** Runs a SPARQL query, returning W3C SPARQL-results-JSON or a span-bearing error. */
  runSparql(query: string, signal?: AbortSignal): Promise<SparqlOutcome>;
  /** Subscribes to the engine trace stream for one query run; returns an unsubscribe handle. */
  subscribeTrace(onEvent: (event: TraceEvent) => void): () => void;
}

/** The bridge the native shell injects onto the page (over WebMessageReceived/SendWebMessage). */
export interface VeritasBridge {
  runSparql(query: string): Promise<SparqlOutcome>;
  onTrace(handler: (event: TraceEvent) => void): () => void;
}

declare global {
  interface Window {
    /** The bridge the desktop shell injects (over the host message channel). */
    veritas?: VeritasBridge;

    /** The in-browser WASM engine, once booted; present in the no-scaffolding web deployment. */
    veritasEngine?: VeritasWasmEngine;
  }
}

/** Desktop-shell transport: forwards to the in-process engine through the host bridge. */
export class NativeVeritasTransport implements VeritasTransport {
  /** @param bridge The host bridge the requests forward to. */
  constructor(private readonly bridge: VeritasBridge) {}

  /** Runs a query through the host bridge. */
  runSparql(query: string): Promise<SparqlOutcome> {
    return this.bridge.runSparql(query);
  }

  /** Subscribes to the engine trace stream through the host bridge. */
  subscribeTrace(onEvent: (event: TraceEvent) => void): () => void {
    return this.bridge.onTrace(onEvent);
  }
}

/** Browser/dev transport: the SPARQL Protocol against the CLI server, results as SPARQL-JSON. */
export class HttpVeritasTransport implements VeritasTransport {
  /** @param fetchImpl The fetch used for requests; injectable so tests pass a canned delegate. */
  constructor(private readonly fetchImpl: FetchLike = globalThis.fetch) {}

  /** Runs a query over the SPARQL Protocol, returning SPARQL-results-JSON or a span-bearing error. */
  async runSparql(query: string, signal?: AbortSignal): Promise<SparqlOutcome> {
    const response = await this.fetchImpl('/sparql', {
      method: 'POST',
      headers: { 'content-type': 'application/sparql-query', accept: 'application/sparql-results+json' },
      body: query,
      signal
    });
    const text = await response.text();

    return response.ok
      ? { ok: true, results: JSON.parse(text) as SparqlResultsDocument }
      : { ok: false, error: JSON.parse(text) as SparqlQueryError };
  }

  /** Subscribes to the engine trace stream, projected as Server-Sent Events (a planned CLI-server surface). */
  subscribeTrace(onEvent: (event: TraceEvent) => void): () => void {
    const source = new EventSource('/trace');
    const listener = (message: MessageEvent<string>): void => onEvent(JSON.parse(message.data) as TraceEvent);
    source.addEventListener('trace', listener as EventListener);

    return () => source.close();
  }
}

/**
 * The engine compiled to WebAssembly, running fully client-side — no .NET host, no server. It exposes a
 * JS-interop surface (the WASM module's exported methods); its durable tier is backed by web storage
 * (the Origin Private File System `ColumnSource`), and its SPARQL `SERVICE` transport is implemented over
 * browser `fetch`, so federation across trust boundaries works from the browser (subject to CORS + the
 * access/governance seam). Booted lazily in a Web Worker; this adapts its surface to the transport seam.
 */
export interface VeritasWasmEngine {
  runSparql(query: string): Promise<string>;
  /** Loads (replacing) a Turtle dataset into the engine, so subsequent queries run over it. */
  loadTurtle(turtle: string): Promise<void>;
  onTrace(handler: (event: TraceEvent) => void): () => void;
}

/** Browser transport over the in-browser WASM engine; results are the engine's SPARQL-results-JSON. */
export class WasmVeritasTransport implements VeritasTransport {
  /** @param engine The booted in-browser WASM engine surface. */
  constructor(private readonly engine: VeritasWasmEngine) {}

  /** Runs a query on the in-browser engine and decodes its SPARQL-results-JSON. */
  async runSparql(query: string): Promise<SparqlOutcome> {
    const document = JSON.parse(await this.engine.runSparql(query)) as SparqlResultsDocument | SparqlQueryError;

    return 'error' in document
      ? { ok: false, error: document }
      : { ok: true, results: document };
  }

  /** Subscribes to the in-browser engine trace stream. */
  subscribeTrace(onEvent: (event: TraceEvent) => void): () => void {
    return this.engine.onTrace(onEvent);
  }
}

/**
 * Picks the transport at boot: the native bridge when the desktop shell injected it; otherwise the
 * in-browser WASM engine once it is booted (`window.veritasEngine`); otherwise the HTTP front door to
 * the CLI server. One seam, three deployments — desktop, no-scaffolding web, and dev/server — with the
 * same app code above the line.
 */
export function resolveTransport(): VeritasTransport {
  if (window.veritas !== undefined) {
    return new NativeVeritasTransport(window.veritas);
  }

  if (window.veritasEngine !== undefined) {
    return new WasmVeritasTransport(window.veritasEngine);
  }

  return new HttpVeritasTransport();
}
