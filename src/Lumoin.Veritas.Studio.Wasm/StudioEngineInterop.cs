using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Database.Completion;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.Json.Stj;
using Lumoin.Veritas.Rdf.Json;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Completion;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Turtle;
// The Turtle completion types arrive as aliases because their namespace also declares a CompletionContext,
// while the context this file names directly is the SPARQL one.
using TurtleCompletion = Lumoin.Veritas.Turtle.Completion.TurtleCompletion;
using TurtleCompletionJson = Lumoin.Veritas.Turtle.Completion.TurtleCompletionJson;

namespace Lumoin.Veritas.Studio.Wasm;

/// <summary>
/// The in-browser engine surface exposed to JavaScript. The Studio web app boots this module on the
/// page's main thread and calls <see cref="RunSparqlAsync"/> through <c>window.veritasEngine</c>; the
/// engine runs fully client-side over the dataset the page loads into it.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class StudioEngineInterop
{
    /// <summary>The opened in-browser engine, after <see cref="InitAsync"/>; one per page.</summary>
    private static VeritasEngine? Engine { get; set; }

    /// <summary>The browser-fetch-backed HTTP client the SERVICE federation transport posts through.</summary>
    private static HttpClient Http { get; } = new();

    /// <summary>The engine options wiring the SERVICE federation transport and the page trace bridge; reused for every open.</summary>
    private static VeritasEngineOptions EngineOptions { get; } = new() { ServiceTransport = FederatedServiceAsync, SparqlExecutionTrace = EmitTraceToPage };

    /// <summary>Whether the page installed its trace sink and enabled the bridge; the handler emits nothing before then, so a host without the sink never calls into JS.</summary>
    private static bool TraceBridgeEnabled { get; set; }

    /// <summary>
    /// Enables the per-event trace bridge. The page calls this after installing its
    /// <c>globalThis.veritasStudioTraceSink</c> dispatcher, so emission starts only once the sink exists.
    /// </summary>
    [JSExport]
    internal static void EnableTraceBridge()
    {
        TraceBridgeEnabled = true;
    }

    /// <summary>
    /// The engine-side execution-trace handler: projects each event to the transport-neutral wire shape and
    /// delivers it to the page's sink synchronously mid-evaluation — the in-browser engine is the server of
    /// its tier and gives its trace to the UI over this bridge. Inert until <see cref="EnableTraceBridge"/>.
    /// </summary>
    /// <param name="evt">The engine event.</param>
    private static void EmitTraceToPage(in SparqlExecutionTraceEvent evt)
    {
        if(!TraceBridgeEnabled)
        {
            return;
        }

        TraceWireEvent wire = SparqlExecutionTraceWire.ToWire(in evt);
        TraceSink(wire.CorrelationId.ToString("D"), wire.Sequence, wire.Kind, wire.Term, wire.Detail);
    }

    /// <summary>The page's trace sink: one call per wire event, dispatched to the shell's subscribed handlers.</summary>
    /// <param name="correlationId">The event's correlation id.</param>
    /// <param name="sequence">The event's sequence number (a JS number; per-evaluation counts stay far below the 2^53 integer ceiling).</param>
    /// <param name="kind">The wire kind token.</param>
    /// <param name="term">The term the event centres on, or <see langword="null"/>.</param>
    /// <param name="detail">The human-readable payload summary.</param>
    [JSImport("globalThis.veritasStudioTraceSink")]
    private static partial void TraceSink(string correlationId, double sequence, string kind, string? term, string detail);

    /// <summary>
    /// The in-browser <c>SERVICE</c> federation transport: POSTs the self-contained sub-query to the remote
    /// endpoint over browser fetch (HttpClient) and parses its SPARQL-results-JSON. The opaque access context
    /// is where an outbound credential (OAuth/DPoP) attaches for a cross-trust-boundary call.
    /// </summary>
    /// <param name="endpoint">The remote endpoint IRI.</param>
    /// <param name="query">The self-contained sub-query to evaluate at the endpoint.</param>
    /// <param name="accessContext">The opaque access context to authorize the call with, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that aborts the remote call.</param>
    /// <returns>The endpoint's result set.</returns>
    private static async ValueTask<SparqlResultSet> FederatedServiceAsync(IriRef endpoint, string query, AccessContext? accessContext, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, endpoint.Value.ToString());
        request.Content = new StringContent(query, Encoding.UTF8, "application/sparql-query");
        request.Headers.Accept.ParseAdd("application/sparql-results+json");
        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        return SparqlResultsJsonReader.Read(bytes);
    }

    /// <summary>
    /// Opens the in-browser engine over its default graph. The graph is empty until the durable
    /// OPFS-backed store (or a fed dataset) is loaded — that loading is the next in-browser increment.
    /// </summary>
    [JSExport]
    internal static async Task InitAsync()
    {
        // The empty default graph; LoadTurtleAsync replaces it with a dataset, and the OPFS-backed journal
        // (warm start by replay) is the next durability increment. The open is MUTABLE: the in-browser
        // engine answers the worlds face (fork, world-scoped query and update, diff), which only the
        // mutable open carries.
        Engine = await VeritasEngine.OpenMutableAsync(Array.Empty<DataTriple>(), EngineOptions).ConfigureAwait(false);
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

        // The replacing open is mutable for the same reason the boot open is: the worlds face rides only
        // the mutable engine, and a load starts a fresh primary world over the parsed dataset.
        Engine = await VeritasEngine.OpenMutableAsync(triples, EngineOptions).ConfigureAwait(false);

        return string.Empty;
    }

    /// <summary>
    /// Validates a world's dataset (the primary world when <paramref name="world"/> is <see langword="null"/>)
    /// against a SHACL shapes graph (Turtle) and returns the UI report JSON (a conformance flag and one row
    /// per result). Parses the shapes with the byte-native Turtle reader.
    /// </summary>
    /// <param name="shapesTurtle">The SHACL shapes graph as a Turtle document.</param>
    /// <param name="world">The registered world whose dataset is validated, or <see langword="null"/> for the primary world.</param>
    /// <returns>The report JSON, or a single synthetic result when the shapes do not parse or the world is not registered.</returns>
    [JSExport]
    internal static async Task<string> ValidateShaclAsync(string shapesTurtle, string? world)
    {
        if(Engine is null)
        {
            await InitAsync().ConfigureAwait(false);
        }

        byte[] bytes = Encoding.UTF8.GetBytes(shapesTurtle);
        // The parse pool stays alive through ValidateAsync (which re-interns the shape terms into the
        // engine's dictionary) and is released after.
        using Utf8StringPool pool = new();
        DiagnosticBag diagnostics = new();
        List<DataTriple> shapes = [];
        await foreach(Quad quad in TurtleReader.ReadAsync(bytes, TurtleSyntax.Turtle, diagnostics, pool, baseIri: "https://veritas.app/studio/", cancellationToken: CancellationToken.None).ConfigureAwait(false))
        {
            shapes.Add(new DataTriple(quad.Subject, quad.Predicate, quad.Object));
        }

        if(diagnostics.HasErrors)
        {
            return "{\"conforms\":false,\"results\":[{\"focusNode\":\"\",\"severity\":\"Violation\",\"constraint\":\"ParseError\",\"message\":\"the shapes did not parse\"}]}";
        }

        ValidationReport report;
        try
        {
            report = await Engine!.ValidateAsync(shapes, world).ConfigureAwait(false);
        }
        catch(ArgumentException)
        {
            // The named world left the registry between the page's listing and this call: a synthetic result
            // in the report shape, so the conformance tab renders the refusal like any other row.
            return "{\"conforms\":false,\"results\":[{\"focusNode\":\"\",\"severity\":\"Violation\",\"constraint\":\"UnknownWorld\",\"message\":\"the named world is not registered\"}]}";
        }

        return ShaclReportJson.From(report, Engine.Dictionary);
    }

    /// <summary>Runs a SPARQL query and returns the W3C SPARQL results-JSON document (SELECT bindings or an ASK boolean), or a value-based <c>{"error":…}</c> document — the same failure shape the HTTP transport parses — for a failed query or a graph-form result; nothing throws across the interop boundary.</summary>
    /// <param name="query">The query text.</param>
    /// <returns>The serialized SPARQL results-JSON, or the error document.</returns>
    [JSExport]
    internal static Task<string> RunSparqlAsync(string query)
    {
        return RunSparqlCoreAsync(query, world: null);
    }

    /// <summary>Runs a SPARQL query in a registered world, answering exactly what <see cref="RunSparqlAsync"/> answers; an unknown world crosses as the <c>{"error":…}</c> document.</summary>
    /// <param name="world">The registered world the query runs in.</param>
    /// <param name="query">The query text.</param>
    /// <returns>The serialized SPARQL results-JSON, or the error document.</returns>
    [JSExport]
    internal static Task<string> RunSparqlInWorldAsync(string world, string query)
    {
        return RunSparqlCoreAsync(query, world);
    }

    /// <summary>The shared query body of the two SPARQL exports: the query runs in the named world (or the primary path) and every failure crosses as the value-based error document.</summary>
    /// <param name="query">The query text.</param>
    /// <param name="world">The registered world the query runs in, or <see langword="null"/> for the primary path.</param>
    /// <returns>The serialized SPARQL results-JSON, or the error document.</returns>
    private static async Task<string> RunSparqlCoreAsync(string query, string? world)
    {
        if(Engine is null)
        {
            await InitAsync().ConfigureAwait(false);
        }

        // The engine query surface converged on Utf8String (byte-only); promote the JS string here.
        VeritasQueryResult result;
        try
        {
            result = await Engine!.QueryAsync(Utf8Strings.From(query), world: world).ConfigureAwait(false);
        }
        catch(UnknownGraphSourceException ex)
        {
            return ErrorDocument(ex.Message);
        }
        catch(ArgumentException ex)
        {
            return ErrorDocument(ex.Message);
        }
        catch(NotSupportedException ex)
        {
            return ErrorDocument(ex.Message);
        }

        // The results-JSON wire carries SELECT bindings and the ASK boolean; a CONSTRUCT/DESCRIBE graph
        // has no rendering on this wire, so it refuses value-based rather than crashing the boundary.
        if(result.IsGraph)
        {
            return ErrorDocument("CONSTRUCT and DESCRIBE results are not rendered in the Studio yet; run the query against a served endpoint for an RDF serialization.");
        }

        // The engine's canonical SPARQL results-JSON writer renders both SELECT bindings and the ASK boolean.
        SparqlResultSet resultSet = result.IsAsk ? SparqlResultSet.ForAsk(result.Boolean!.Value) : result.Bindings!;

        return SparqlResultsJsonWriter.WriteToUtf8String(resultSet, indented: false).ToString();
    }

    /// <summary>Renders the one-field <c>{"error":…}</c> document with the message JSON-escaped.</summary>
    /// <param name="message">The error message.</param>
    /// <returns>The JSON error document.</returns>
    private static string ErrorDocument(string message)
    {
        StringBuilder builder = new();
        builder.Append("{\"error\":\"");
        foreach(char character in message)
        {
            builder.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => "\\u" + ((int)character).ToString("x4", System.Globalization.CultureInfo.InvariantCulture),
                _ => character.ToString()
            });
        }

        builder.Append("\"}");

        return builder.ToString();
    }

    /// <summary>
    /// Describes the SPARQL completion context at a caret: the token kinds the grammar admits next, the
    /// enclosing production chain, the in-scope variables — each with its datatype resolved against the loaded
    /// dataset where determinable — and the variable→predicate pairs. The caret arrives as the editor's UTF-16
    /// code-unit index and is converted here to the UTF-8 byte offset the parser addresses.
    /// </summary>
    /// <param name="query">The query text.</param>
    /// <param name="caretCharOffset">The caret position as a UTF-16 code-unit index into <paramref name="query"/>.</param>
    /// <returns>The completion-context JSON.</returns>
    [JSExport]
    internal static async Task<string> DescribeCompletionAsync(string query, int caretCharOffset)
    {
        if(Engine is null)
        {
            await InitAsync().ConfigureAwait(false);
        }

        int caret = Math.Clamp(caretCharOffset, 0, query.Length);
        int byteOffset = Encoding.UTF8.GetByteCount(query.AsSpan(0, caret));
        CompletionContext context = SparqlCompletion.Describe(Encoding.UTF8.GetBytes(query), byteOffset);

        // Fill the variables' datatypes from the loaded dataset (SHACL sh:datatype, then rdfs:range, then a
        // DATATYPE() sample); with no data loaded they stay Unknown and the rest of the context still stands.
        CompletionContext resolved = await SparqlCompletionDatatypes.ResolveAsync(Engine!, context).ConfigureAwait(false);

        return CompletionContextJson.Write(resolved);
    }

    /// <summary>
    /// Returns the editor's fixed RDF-vocabulary corpus: the SHACL (<c>sh:</c>), OWL (<c>owl:</c>), RDF
    /// (<c>rdf:</c>), RDFS (<c>rdfs:</c>), and XSD-datatype (<c>xsd:</c>) terms the corpus writer carries
    /// itself, the GeoSPARQL ontology (<c>geo:</c>), GeoSPARQL function (<c>geof:</c>), Simple Features
    /// (<c>sf:</c>), and GML (<c>gml:</c>) groups the geospatial library contributes, and the registered
    /// geometry value-datatype IRIs no group already covers, as angle-bracketed full IRIs — one JSON array of
    /// completion candidates, for completing a Turtle, SHACL, OWL, or SPARQL buffer where those vocabularies
    /// are not present in the loaded data. Static (the corpus is fixed), so it needs no opened engine.
    /// </summary>
    /// <returns>A JSON array of candidate strings, e.g. <c>["sh:NodeShape", …]</c>.</returns>
    [JSExport]
    internal static string EditorVocabularyJson()
        => EditorVocabulary.ToJson(GeoEditorVocabulary.Groups, StudioValueDatatypes.Registry.DatatypeIris);

    /// <summary>
    /// Describes the caret-aware Turtle / SHACL / TriG completion context at a caret — the token kinds the
    /// grammar admits next and the enclosing-production chain — as JSON, for completing a Turtle, SHACL, or TriG
    /// buffer. The caret arrives as the editor's UTF-16 code-unit index. Store-free (the grammar is the corpus),
    /// so it needs no opened engine.
    /// </summary>
    /// <param name="source">The Turtle / SHACL / TriG buffer text.</param>
    /// <param name="caretCharOffset">The caret as a UTF-16 code-unit index into <paramref name="source"/>.</param>
    /// <param name="syntax">The syntax flavour: <c>trig</c> for TriG, otherwise Turtle.</param>
    /// <returns>The completion-context JSON.</returns>
    [JSExport]
    internal static string DescribeTurtleCompletionJson(string source, int caretCharOffset, string syntax)
    {
        int caret = Math.Clamp(caretCharOffset, 0, source.Length);
        int byteOffset = Encoding.UTF8.GetByteCount(source.AsSpan(0, caret));

        return TurtleCompletionJson.Write(TurtleCompletion.Describe(Encoding.UTF8.GetBytes(source), byteOffset, TurtleCompletionJson.ParseSyntax(syntax)));
    }

    /// <summary>
    /// Diagnoses one geometry literal's body against its datatype IRI: whether the body is valid, breaks the
    /// datatype's certified grammar (invalid), or is tolerated by that grammar yet unreadable by the engine's
    /// codec (warning) — the latter two carrying the refusal kind and the offset of the first offending byte.
    /// A datatype outside the geometry-literal family abstains. The body arrives as the editor's text and is
    /// transcoded here, so the reported offset indexes its UTF-8 bytes. Store-free (the datatype's own lexical
    /// layer and codec reader are the whole authority), so it needs no opened engine.
    /// </summary>
    /// <param name="datatypeIri">The literal's datatype IRI.</param>
    /// <param name="body">The literal's value text (unescaped), as the editor holds it.</param>
    /// <returns>The literal-diagnosis JSON.</returns>
    [JSExport]
    internal static string DescribeGeoLiteralJson(string datatypeIri, string body)
    {
        // The GeoJSON family reads through the System.Text.Json binding's reader, the delegate contract the
        // Geo library takes from its composing host.
        Utf8String datatype = new(Encoding.UTF8.GetBytes(datatypeIri));
        GeoLiteralDiagnosis diagnosis = GeoLiteralDiagnostics.Describe(datatype, Encoding.UTF8.GetBytes(body), GeoJsonGeometryReader.TryRead);

        return GeoLiteralDiagnosisJson.Write(datatype, in diagnosis);
    }

    /// <summary>Lists the engine's registered worlds as the worlds listing document — each world's name, its content-addressed state identifier, and its fork parent, the primary world first. The same document the CLI server's <c>GET /worlds</c> answers, so the transport seam reads one shape on both tiers.</summary>
    /// <returns>The worlds listing JSON.</returns>
    [JSExport]
    internal static async Task<string> ListWorldsJsonAsync()
    {
        if(Engine is null)
        {
            await InitAsync().ConfigureAwait(false);
        }

        return WorldsJson.WriteWorlds(Engine!.DescribeWorlds());
    }

    /// <summary>Forks a registered world's current committed state under a new name and answers the outcome document; an unknown source and a taken name cross as outcome tokens, never faults.</summary>
    /// <param name="source">The world to fork from.</param>
    /// <param name="name">The new world's name.</param>
    /// <returns>The fork outcome JSON.</returns>
    [JSExport]
    internal static async Task<string> ForkWorldAsync(string source, string name)
    {
        if(Engine is null)
        {
            await InitAsync().ConfigureAwait(false);
        }

        return WorldsJson.WriteForkOutcome(await Engine!.ForkWorldAsync(source, name).ConfigureAwait(false));
    }

    /// <summary>Drops a registered world's name and answers the outcome document; an unknown name and the never-droppable primary world cross as outcome tokens.</summary>
    /// <param name="name">The world's name.</param>
    /// <returns>The drop outcome JSON.</returns>
    [JSExport]
    internal static async Task<string> DropWorldAsync(string name)
    {
        if(Engine is null)
        {
            await InitAsync().ConfigureAwait(false);
        }

        return WorldsJson.WriteDropOutcome(Engine!.DropWorld(name));
    }

    /// <summary>Commits a SPARQL Update into a registered world and answers the acknowledgement document; an update that does not parse, a query where an update belongs, and an unknown world all cross as the <c>{"error":…}</c> document.</summary>
    /// <param name="world">The registered world the update commits into.</param>
    /// <param name="update">The update text.</param>
    /// <returns>The acknowledgement JSON, or the error document.</returns>
    [JSExport]
    internal static async Task<string> RunUpdateInWorldAsync(string world, string update)
    {
        if(Engine is null)
        {
            await InitAsync().ConfigureAwait(false);
        }

        try
        {
            await Engine!.UpdateAsync(Utf8Strings.From(update), world: world).ConfigureAwait(false);
        }
        catch(ArgumentException ex)
        {
            return ErrorDocument(ex.Message);
        }

        return WorldsJson.UpdatedDocument;
    }

    /// <summary>Diffs two registered worlds and answers the bounded diff document — exact totals always, listed triples capped, terms decoded to their lexical forms; an unknown world on either side crosses as the outcome document. The same document the CLI server's <c>GET /worlds/diff</c> answers.</summary>
    /// <param name="from">The baseline world the transitions start from.</param>
    /// <param name="to">The world whose state the transitions produce.</param>
    /// <returns>The diff JSON.</returns>
    [JSExport]
    internal static async Task<string> DiffWorldsJsonAsync(string from, string to)
    {
        if(Engine is null)
        {
            await InitAsync().ConfigureAwait(false);
        }

        WorldDiff diff = Engine!.DiffWorlds(to, from);

        return WorldsJson.WriteDiff(in diff);
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
