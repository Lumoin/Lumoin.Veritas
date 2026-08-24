// Boots the Veritas engine compiled to WebAssembly and installs it as window.veritasEngine, so the
// transport seam resolves the in-browser engine (WasmVeritasTransport) — the no-scaffolding deployment:
// no native shell, no server, the engine running client-side. Called only when that deployment is
// selected; the desktop and server deployments never load this. The .NET WASM runtime (dotnet.js) and
// the exported engine surface come from the Lumoin.Veritas.Studio.Wasm build's _framework.

import type { TraceEvent, VeritasWasmEngine } from './veritas-transport';

/** The minimal slice of the .NET WASM host this boot uses. */
interface DotnetHost {
  create(): Promise<DotnetRuntime>;
}

/** The minimal slice of a created .NET WASM runtime this boot uses. */
interface DotnetRuntime {
  getConfig(): { mainAssemblyName: string };
  getAssemblyExports(assemblyName: string): Promise<unknown>;
}

/** The shape of the engine interop the WASM assembly exports (mirrors StudioEngineInterop). */
interface EngineExports {
  Lumoin: {
    Veritas: {
      Studio: {
        Wasm: {
          StudioEngineInterop: {
            InitAsync(): Promise<void>;
            LoadTurtleAsync(turtle: string): Promise<string>;
            ValidateShaclAsync(shapes: string, world: string | null): Promise<string>;
            RunSparqlAsync(query: string): Promise<string>;
            DescribeCompletionAsync(query: string, caretCharOffset: number): Promise<string>;
            EditorVocabularyJson(): string;
            DescribeTurtleCompletionJson(source: string, caretCharOffset: number, syntax: string): string;
            DescribeGeoLiteralJson(datatypeIri: string, body: string): string;
            ListWorldsJsonAsync(): Promise<string>;
            ForkWorldAsync(source: string, name: string): Promise<string>;
            DropWorldAsync(name: string): Promise<string>;
            RunSparqlInWorldAsync(world: string, query: string): Promise<string>;
            RunUpdateInWorldAsync(world: string, update: string): Promise<string>;
            DiffWorldsJsonAsync(from: string, to: string): Promise<string>;
            EnableTraceBridge(): void;
          };
        };
      };
    };
  };
}

/** The subscribed trace handlers; the page sink dispatches each bridged engine event to every handler. */
const traceHandlers = new Set<(event: TraceEvent) => void>();

/**
 * Installs the global sink the WASM host's per-event trace bridge calls (a [JSImport] the engine invokes
 * synchronously mid-evaluation, keyed by correlation id). Installed BEFORE the runtime loads and enabled
 * on the host afterwards, so the first traced query already finds the sink in place.
 */
function installTraceSink(): void {
  (globalThis as {
    veritasStudioTraceSink?: (correlationId: string, sequence: number, kind: string, term: string | null, detail: string) => void;
  }).veritasStudioTraceSink = (correlationId, sequence, kind, term, detail) => {
    const event: TraceEvent = { correlationId, sequence, kind, detail };
    if (term !== null) {
      event.term = term;
    }

    for (const handler of traceHandlers) {
      handler(event);
    }
  };
}

/**
 * Boots the WASM engine on an empty graph and installs `window.veritasEngine`; the caller then opens a
 * dataset through its own loading path, so data reaches the engine one way only. Idempotent: a second call
 * is a no-op once the engine is present. The runtime URL is the WASM build's loader; it is resolved at
 * integration (an import map or a served path), hence the variable import.
 * @param dotnetUrl The URL of the WASM runtime loader (`dotnet.js`).
 */
export async function bootWasmEngine(dotnetUrl: string): Promise<void> {
  if ((globalThis as { veritasEngine?: VeritasWasmEngine }).veritasEngine !== undefined) {
    return;
  }

  installTraceSink();
  const host = (await import(/* @vite-ignore */ dotnetUrl)) as { dotnet: DotnetHost };
  const runtime = await host.dotnet.create();
  const exports = (await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName)) as EngineExports;
  const interop = exports.Lumoin.Veritas.Studio.Wasm.StudioEngineInterop;
  interop.EnableTraceBridge();
  await interop.InitAsync();

  const engine: VeritasWasmEngine = {
    runSparql: (query: string): Promise<string> => interop.RunSparqlAsync(query),
    loadTurtle: async (turtle: string): Promise<void> => {
      const error = await interop.LoadTurtleAsync(turtle);
      if (error.length > 0) {
        throw new Error(`In-browser dataset load failed: ${error}`);
      }
    },
    validateShacl: (shapes: string, world: string | null): Promise<string> => interop.ValidateShaclAsync(shapes, world),
    describeCompletion: (query: string, caretOffset: number): Promise<string> => interop.DescribeCompletionAsync(query, caretOffset),
    editorVocabulary: (): Promise<string> => Promise.resolve(interop.EditorVocabularyJson()),
    describeTurtleCompletion: (source: string, caretOffset: number, syntax: string): Promise<string> => Promise.resolve(interop.DescribeTurtleCompletionJson(source, caretOffset, syntax)),
    describeGeoLiteral: (datatypeIri: string, body: string): Promise<string> => Promise.resolve(interop.DescribeGeoLiteralJson(datatypeIri, body)),
    listWorldsJson: (): Promise<string> => interop.ListWorldsJsonAsync(),
    forkWorld: (source: string, name: string): Promise<string> => interop.ForkWorldAsync(source, name),
    dropWorld: (name: string): Promise<string> => interop.DropWorldAsync(name),
    runSparqlIn: (world: string, query: string): Promise<string> => interop.RunSparqlInWorldAsync(world, query),
    runUpdateIn: (world: string, update: string): Promise<string> => interop.RunUpdateInWorldAsync(world, update),
    diffWorldsJson: (from: string, to: string): Promise<string> => interop.DiffWorldsJsonAsync(from, to),
    onTrace: (handler: (event: TraceEvent) => void): (() => void) => {
      traceHandlers.add(handler);

      return () => {
        traceHandlers.delete(handler);
      };
    }
  };

  (globalThis as { veritasEngine?: VeritasWasmEngine }).veritasEngine = engine;
}
