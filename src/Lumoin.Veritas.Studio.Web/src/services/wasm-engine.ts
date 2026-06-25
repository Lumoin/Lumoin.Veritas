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
            ValidateShaclAsync(shapes: string): Promise<string>;
            RunSparqlAsync(query: string): Promise<string>;
          };
        };
      };
    };
  };
}

/**
 * Boots the WASM engine and installs `window.veritasEngine`. Idempotent: a second call is a no-op once
 * the engine is present. The runtime URL is the WASM build's loader; it is resolved at integration (an
 * import map or a served path), hence the variable import.
 * @param dotnetUrl The URL of the WASM runtime loader (`dotnet.js`).
 * @param initialTurtle An optional Turtle dataset to load into the engine on boot; the engine opens empty without it.
 */
export async function bootWasmEngine(dotnetUrl: string, initialTurtle?: string): Promise<void> {
  if ((globalThis as { veritasEngine?: VeritasWasmEngine }).veritasEngine !== undefined) {
    return;
  }

  const host = (await import(/* @vite-ignore */ dotnetUrl)) as { dotnet: DotnetHost };
  const runtime = await host.dotnet.create();
  const exports = (await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName)) as EngineExports;
  const interop = exports.Lumoin.Veritas.Studio.Wasm.StudioEngineInterop;
  if (initialTurtle !== undefined) {
    const error = await interop.LoadTurtleAsync(initialTurtle);
    if (error.length > 0) {
      throw new Error(`In-browser dataset load failed: ${error}`);
    }
  } else {
    await interop.InitAsync();
  }

  const engine: VeritasWasmEngine = {
    runSparql: (query: string): Promise<string> => interop.RunSparqlAsync(query),
    loadTurtle: async (turtle: string): Promise<void> => {
      const error = await interop.LoadTurtleAsync(turtle);
      if (error.length > 0) {
        throw new Error(`In-browser dataset load failed: ${error}`);
      }
    },
    validateShacl: (shapes: string): Promise<string> => interop.ValidateShaclAsync(shapes),
    onTrace: (_handler: (event: TraceEvent) => void): (() => void) => {
      // TODO(in-browser): bridge the engine trace bus to JS (a [JSImport] callback the engine invokes
      // per event, keyed by CorrelationId). Trace is a no-op in the WASM deployment until then.
      return () => {
        // No subscription to release yet.
      };
    }
  };

  (globalThis as { veritasEngine?: VeritasWasmEngine }).veritasEngine = engine;
}
