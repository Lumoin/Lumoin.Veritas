using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Studio.Wasm;

/// <summary>
/// The in-browser engine surface exposed to JavaScript. The Studio web app boots this module in a Web
/// Worker and calls <see cref="RunSparqlAsync"/> through <c>window.veritasEngine</c>; the engine runs
/// fully client-side. The durable graph is loaded from web storage (OPFS, the planned ColumnSource) and
/// <c>SERVICE</c> federation is issued over browser <c>fetch</c> through the engine's service-transport
/// seam — both wired here as the in-browser deployment fills in (see the lane HANDOFF).
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class StudioEngineInterop
{
    /// <summary>The opened in-browser engine, after <see cref="InitAsync"/>; one per worker.</summary>
    private static VeritasEngine? Engine { get; set; }

    /// <summary>
    /// Opens the in-browser engine over its default graph. The graph is empty until the durable
    /// OPFS-backed store (or a fed dataset) is loaded — that loading is the next in-browser increment.
    /// </summary>
    [JSExport]
    internal static async Task InitAsync()
    {
        // TODO(in-browser): hydrate the default graph from the OPFS ColumnSource (warm start), or parse a
        // fed dataset (the Turtle reader → DataTriples). Empty graph until then so the surface is callable.
        Engine = await VeritasEngine.OpenAsync(Array.Empty<DataTriple>()).ConfigureAwait(false);
    }

    /// <summary>Runs a SPARQL query and returns the W3C SPARQL results-JSON document (SELECT bindings or an ASK boolean).</summary>
    /// <param name="query">The query text.</param>
    /// <returns>The serialized SPARQL results-JSON.</returns>
    [JSExport]
    internal static async Task<string> RunSparqlAsync(string query)
    {
        if(Engine is null)
        {
            await InitAsync().ConfigureAwait(false);
        }

        // The engine query surface converged on Utf8String (byte-only); promote the JS string here.
        VeritasQueryResult result = await Engine!.QueryAsync(Utf8String.FromString(query)).ConfigureAwait(false);

        return result.IsAsk
            ? SparqlResultsJson.ForAsk(result.Boolean!.Value)
            : SparqlResultsJson.ForSelect(result.Bindings!);
    }
}

/// <summary>The WebAssembly entry point; the runtime stays resident for the JS-exported engine calls.</summary>
internal static class Program
{
    /// <summary>Does nothing but keep the runtime alive; the engine is driven through <see cref="StudioEngineInterop"/>.</summary>
    private static void Main()
    {
    }
}
