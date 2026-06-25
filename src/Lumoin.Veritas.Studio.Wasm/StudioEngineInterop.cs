using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Rdf.Json;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Turtle;

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
        // The empty default graph; LoadTurtleAsync replaces it with a dataset, and the OPFS-backed journal
        // (warm start by replay) is the next durability increment.
        Engine = await VeritasEngine.OpenAsync(Array.Empty<DataTriple>()).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a Turtle dataset into the in-browser engine, replacing the current graph, so queries run over
    /// real data. Parses the document with the byte-native Turtle reader (the same path the CLI loader uses).
    /// </summary>
    /// <param name="turtle">The Turtle document text.</param>
    /// <returns>An empty string on success; a short message when the document does not parse.</returns>
    [JSExport]
    internal static async Task<string> LoadTurtleAsync(string turtle)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(turtle);
        // The parse pool interns token payloads; it stays alive through OpenAsync (which re-interns the
        // terms into the engine's own dictionary) and is released after, so no engine term aliases it.
        using Utf8StringPool pool = new();
        DiagnosticBag diagnostics = new();
        List<DataTriple> triples = [];
        await foreach(Quad quad in TurtleReader.ReadAsync(bytes, TurtleSyntax.Turtle, diagnostics, pool, baseIri: "https://veritas.app/studio/", cancellationToken: CancellationToken.None).ConfigureAwait(false))
        {
            triples.Add(new DataTriple(quad.Subject, quad.Predicate, quad.Object));
        }

        if(diagnostics.HasErrors)
        {
            return "the dataset did not parse";
        }

        Engine = await VeritasEngine.OpenAsync(triples).ConfigureAwait(false);

        return string.Empty;
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

        // The engine's canonical SPARQL results-JSON writer renders both SELECT bindings and the ASK boolean.
        SparqlResultSet resultSet = result.IsAsk ? SparqlResultSet.ForAsk(result.Boolean!.Value) : result.Bindings!;

        return SparqlResultsJsonWriter.WriteToUtf8String(resultSet, indented: false).ToString();
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
