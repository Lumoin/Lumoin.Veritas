using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Database.Completion;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Completion;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Turtle.Completion;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using SparqlCompletionContext = Lumoin.Veritas.Sparql.Completion.CompletionContext;
using TurtleCompletionContext = Lumoin.Veritas.Turtle.Completion.CompletionContext;

namespace Lumoin.Veritas.Cli;

/// <summary>
/// A minimal SPARQL 1.1 Protocol query endpoint (Kestrel) over a dataset loaded once at startup.
/// This is the HTTP face of the engine — the inbound counterpart to the library's outbound
/// <c>SparqlServiceTransport</c> seam — letting Veritas answer queries like a triplestore. The
/// library itself stays HTTP-free; the dependency on Kestrel lives only in this application.
/// </summary>
/// <remarks>
/// <para>
/// The query operation accepts all three protocol submission forms — <c>GET /sparql?query=…</c>, a
/// <c>POST</c> with an <c>application/sparql-query</c> body, and a form-encoded <c>POST</c> with a
/// <c>query</c> field — plus the protocol dataset parameters <c>default-graph-uri</c> /
/// <c>named-graph-uri</c> (query-string parameters on GET and the direct POST, form fields on the
/// form POST), which override the query's own <c>FROM</c>/<c>FROM NAMED</c> clause and resolve
/// against the LOADED dataset's named graphs only: the engine here opens with <c>GraphSource</c>
/// explicitly nulled, so the store-local resolver is the only dataset source whatever the shared
/// CLI options later carry — the serve surface never fetches over a network on behalf of a request.
/// </para>
/// <para>
/// All four query forms answer. SELECT/ASK negotiate CSV (the default), TSV, SPARQL-results-JSON,
/// and SPARQL-results-XML via <c>Accept</c> (an ASK under a delimited preference answers JSON — no
/// delimited boolean format exists); CONSTRUCT/DESCRIBE answer N-Triples by default and Turtle on
/// <c>Accept: text/turtle</c>. An <c>Accept</c> token weighted <c>q=0</c> is excluded; non-zero
/// weights are not ranked further — server-driven negotiation answers by the fixed precedence XML,
/// JSON, TSV, CSV (and Turtle over N-Triples). Faults follow the protocol: a malformed query or an
/// unacceptable dataset description answers 400, a refused execution 500, an unrecognized POST
/// content type 415. A <c>GET</c> with NO <c>query</c> parameter answers the SPARQL 1.1 Service
/// Description generated from live state, while a present-but-empty <c>query</c> stays the
/// missing-query 400. The unknown-dataset-graph refusal names the submitted IRI, which under an
/// open CORS origin is a graph-name existence oracle equivalent to what SELECT over <c>GRAPH</c>
/// patterns already exposes.
/// </para>
/// <para>
/// Beside the protocol operation the host carries the engine's editor-facing capabilities on their own
/// routes: <c>/trace</c> streams the execution trace, <c>/analytics</c> runs the graph algorithms,
/// <c>POST /literal-diagnostics</c> describes one geometry-literal body against its datatype — the
/// diagnosis an editor turns into an in-literal squiggle — <c>POST /completion</c> and
/// <c>POST /turtle-completion</c> describe the caret's completion context in a SPARQL and a
/// Turtle-family buffer, and <c>GET /editor-vocabulary</c> answers the editor's fixed candidate corpus.
/// Every one of them is mapped unconditionally, so a client reaches the same capability set whether or
/// not this host also serves the Studio page. The SPARQL completion context resolves its variables'
/// datatypes against the dataset this host actually serves, so the editor's answers describe the data
/// in front of it.
/// </para>
/// <para>
/// Authorization is not yet enforced; every public surface is the place the opaque
/// <c>AccessContext</c> / <c>AccessControlDelegate</c> seam threads in, and the default here is
/// allow-all until that is wired.
/// </para>
/// </remarks>
internal static class VeritasSparqlServer
{
    /// <summary>
    /// The editor's fixed completion corpus as the JSON array the vocabulary route answers: the core RDF
    /// vocabularies, this application's geospatial contribution, and the value-datatype IRIs its composition
    /// registered — the a5 DGGS literal among them, which no conventional prefix names and which therefore
    /// rides the corpus as a full IRI. Every input is fixed for the process, so the document is composed once
    /// and written to each request.
    /// </summary>
    private static string EditorVocabularyDocument { get; } = EditorVocabulary.ToJson(GeoEditorVocabulary.Groups, VeritasOperations.EngineOptions.ValueDatatypes.DatatypeIris);

    /// <summary>Loads the dataset, starts the endpoint (optionally also serving the in-browser Studio UI), and runs until cancelled.</summary>
    /// <param name="dataPaths">The RDF data document paths forming the served dataset.</param>
    /// <param name="port">The loopback port to listen on.</param>
    /// <param name="uiDirectory">The directory of the built Studio web app to serve at the root, or <see langword="null"/> to serve only the SPARQL endpoint.</param>
    /// <param name="openBrowser">Whether to open a browser at the served address once the endpoint is listening.</param>
    /// <param name="corsOrigins">The origins granted cross-origin (CORS) access to the endpoints — e.g. a remotely hosted Studio page targeting this server; an entry of <c>*</c> grants any origin, and an empty list grants no cross-origin access.</param>
    /// <param name="cancellationToken">A token that stops the server.</param>
    /// <returns>The process exit code (0 on a clean stop, 1 when the dataset or the UI directory could not be loaded).</returns>
    public static async Task<int> RunAsync(IReadOnlyList<string> dataPaths, int port, string? uiDirectory, bool openBrowser, IReadOnlyList<string> corsOrigins, CancellationToken cancellationToken)
    {
        //The endpoint serves latency-sensitive I/O from the shared worker pool; floor the pool's
        //minimum so a burst of synchronous CPU work is far less likely to starve response
        //continuations behind the pool's gradual thread injection. The floor is reported the moment
        //it applies — before the dataset loads or the host binds — so an operator (and the
        //integration test) observes it as the first line of output.
        int workerThreadFloor = HostThreadPoolFloor.Apply(ExecutionPolicy.Default.HostPoolFloorMultiplier);
        await Console.Out.WriteLineAsync($"Worker-thread minimum floored at {workerThreadFloor}.").ConfigureAwait(false);

        //Open the database over the data: one engine composing storage, query, reasoning, and its own
        //compute lane (on-demand view builds run as lane turns off the serve pool). Disposed on
        //shutdown, which drains any in-flight build before the process exits. The engine opens under the
        //shared composed options extended with the trace hub's handler, so every query this server answers
        //streams its execution trace to the /trace subscribers — the same wire contract the in-browser
        //engine bridges to the page.
        TraceStreamHub traceHub = new();

        //GraphSource is explicitly nulled at this composition point: the HTTP-exposed surface must never
        //carry a network-capable graph resolver, so even if the shared CLI options later gain one, dataset
        //clauses here keep resolving through the engine's store-local source alone.
        //The open is MUTABLE: this host answers the first-party worlds face (fork, world-scoped query and
        //update, diff), which only the mutable open carries. The loaded documents stay untouched on disk —
        //every mutation lives in the served process alone.
        (VeritasEngine? database, string? error) = await VeritasOperations.OpenDatabaseAsync(dataPaths, VeritasOperations.EngineOptions with { SparqlExecutionTrace = traceHub.Handle, GraphSource = null }, mutable: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        if(error is not null)
        {
            await Console.Error.WriteLineAsync(error).ConfigureAwait(false);

            return 1;
        }

        await using var databaseScope = database!.ConfigureAwait(false);

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        if(corsOrigins.Count > 0)
        {
            builder.Services.AddCors(new CorsPolicyRegistration(corsOrigins).Configure);
        }

        WebApplication app = builder.Build();
        app.Urls.Add($"http://localhost:{port}");

        //Cross-origin access exists only when origins were allowlisted: the CORS middleware answers the
        //preflight an application/sparql-query POST requires (a non-safelisted content type) and stamps the
        //allow-origin header on the endpoint responses. Without the flag no middleware is added, so the
        //server stays same-origin-only.
        if(corsOrigins.Count > 0)
        {
            app.UseCors();
        }

        //A UI directory (the in-browser Studio's built dist) is served as static files at the root, so the same
        //origin hosts both the editor and the /sparql endpoint its HTTP transport calls — the browser tab talks
        //to this in-process engine. The endpoints below take precedence; any other path falls through to a file
        //(index.html by default).
        if(uiDirectory is not null)
        {
            string resolvedUiDirectory = Path.GetFullPath(uiDirectory);
            if(!Directory.Exists(resolvedUiDirectory))
            {
                await Console.Error.WriteLineAsync($"UI directory not found: {resolvedUiDirectory}").ConfigureAwait(false);

                return 1;
            }

            PhysicalFileProvider uiFiles = new(resolvedUiDirectory);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = uiFiles });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = uiFiles });

            //The served page probes GET /config at boot to learn this origin hosts a server-side engine; it is
            //registered only while the UI is served, since that is its only consumer. The mapped endpoint takes
            //precedence over the static files above (no file is named "config"), so it answers the marker.
            app.MapGet("/config", HandleConfig);
        }

        SparqlRequestHandler handler = new(database!, dataPaths, cancellationToken);
        app.MapGet("/sparql", handler.HandleGet);
        app.MapPost("/sparql", handler.HandlePost);
        app.MapGet("/analytics", handler.HandleAnalyticsList);
        app.MapPost("/analytics", handler.HandleAnalyticsPost);

        //The trace stream is a capability of this first-party host (not gated on the UI directory): any
        //client — the served Studio page, a remote Studio under --cors-origin, a curl — can watch the
        //engine's execution trace live over Server-Sent Events.
        TraceStreamRequestHandler traceHandler = new(traceHub, cancellationToken);
        app.MapGet("/trace", traceHandler.HandleTrace);

        //Literal diagnostics is a stateless projection over the composed datatype family, so the route needs
        //no server state at all and is mapped as a bound static method group. It is named format-neutrally:
        //a future literal family joins the same route rather than earning a second one. GET is not mapped —
        //a literal body does not belong in a query string.
        app.MapPost("/literal-diagnostics", HandleLiteralDiagnosticsAsync);

        //SPARQL completion resolves the caret's variables against the dataset this host serves, so it binds
        //through the handler that already carries the open database. The Turtle-family context and the fixed
        //vocabulary are store-free — the grammar and the vocabulary constants are their whole authority — so
        //they map as bound static method groups. The vocabulary answers GET because it takes no input; the two
        //completion routes answer POST because a buffer does not belong in a query string.
        app.MapPost("/completion", handler.HandleCompletion);
        app.MapPost("/turtle-completion", HandleTurtleCompletionAsync);
        app.MapGet("/editor-vocabulary", HandleEditorVocabulary);

        //The worlds face is a first-party capability of this host, like /trace and /completion — /sparql
        //stays the pure SPARQL 1.1 Protocol endpoint. It exists because the serve open is mutable: the
        //listing, fork, drop, and diff answer the wire documents the transport seam reads, and the
        //world-scoped query and update run against the named world's dataset.
        app.MapGet("/worlds", handler.HandleWorlds);
        app.MapPost("/worlds/fork", handler.HandleWorldFork);
        app.MapPost("/worlds/drop", handler.HandleWorldDrop);
        app.MapPost("/worlds/query", handler.HandleWorldQuery);
        app.MapPost("/worlds/update", handler.HandleWorldUpdate);
        app.MapGet("/worlds/diff", handler.HandleWorldDiff);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        //After binding, the server features carry the resolved address (the real port when 0 was requested);
        //print it so a caller — including a test harness — can reach the endpoint.
        ICollection<string>? addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        if(addresses is not null)
        {
            foreach(string address in addresses)
            {
                await Console.Out.WriteLineAsync($"Veritas SPARQL endpoint listening on {address}/sparql ({dataPaths.Count} data file(s)).").ConfigureAwait(false);
                if(uiDirectory is not null)
                {
                    await Console.Out.WriteLineAsync($"Veritas Studio UI at {address}/").ConfigureAwait(false);
                }
            }
        }

        if(openBrowser && addresses?.FirstOrDefault() is { } uiAddress)
        {
            OpenBrowser(uiAddress);
        }

        await app.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);

        return 0;
    }

    /// <summary>
    /// Answers SPARQL requests against the open database, carrying the database and the server-lifetime token
    /// as explicit state so the minimal-API route handlers are bound method groups rather than lambdas closing
    /// over the enclosing database and token.
    /// </summary>
    /// <param name="database">The open database the requests query.</param>
    /// <param name="cancellationToken">The server-lifetime token that aborts in-flight requests on shutdown.</param>
    private sealed class SparqlRequestHandler(VeritasEngine database, IReadOnlyList<string> dataPaths, CancellationToken cancellationToken)
    {
        /// <summary>The open database the requests query.</summary>
        private VeritasEngine Database { get; } = database;

        /// <summary>The data document paths the dataset was loaded from; the analytics endpoint reads them to build its index.</summary>
        private IReadOnlyList<string> DataPaths { get; } = dataPaths;

        /// <summary>The server-lifetime token that aborts in-flight requests on shutdown.</summary>
        private CancellationToken CancellationToken { get; } = cancellationToken;

        /// <summary>Answers a GET request: the service description when NO <c>query</c> parameter is present, otherwise the query from the <c>query</c> parameter with any protocol dataset parameters.</summary>
        /// <param name="httpContext">The request context.</param>
        /// <returns>The asynchronous handling.</returns>
        public Task HandleGet(HttpContext httpContext)
        {
            //The Service Description SHOULD fires only when the query parameter is ABSENT; a
            //present-but-empty query stays the missing-query 400 in HandleAsync.
            if(!httpContext.Request.Query.ContainsKey("query"))
            {
                return HandleServiceDescriptionAsync(httpContext, CancellationToken);
            }

            return HandleAsync(httpContext, Database, ToQuery(httpContext.Request.Query["query"]), ProtocolDataset(httpContext.Request.Query["default-graph-uri"], httpContext.Request.Query["named-graph-uri"]), cancellationToken: CancellationToken);
        }

        /// <summary>Answers a POST request, reading the query from the request body or a form field.</summary>
        /// <param name="httpContext">The request context.</param>
        /// <returns>The asynchronous handling.</returns>
        public Task HandlePost(HttpContext httpContext)
        {
            return HandlePostAsync(httpContext, Database, CancellationToken);
        }

        /// <summary>Answers a <c>GET /analytics</c> request by listing the available graph-analytics algorithms.</summary>
        /// <param name="httpContext">The request context.</param>
        /// <returns>The asynchronous handling.</returns>
        public Task HandleAnalyticsList(HttpContext httpContext)
        {
            return HandleAnalyticsListAsync(httpContext, CancellationToken);
        }

        /// <summary>Answers a <c>POST /analytics</c> request, running the form's <c>algorithm</c> over the served dataset.</summary>
        /// <param name="httpContext">The request context.</param>
        /// <returns>The asynchronous handling.</returns>
        public Task HandleAnalyticsPost(HttpContext httpContext)
        {
            return HandleAnalyticsPostAsync(httpContext, DataPaths, CancellationToken);
        }

        /// <summary>Answers a <c>POST /completion</c> request, describing the caret's SPARQL completion context and resolving its variables' datatypes against the served dataset.</summary>
        /// <param name="httpContext">The request context.</param>
        /// <returns>The asynchronous handling.</returns>
        public Task HandleCompletion(HttpContext httpContext)
        {
            return HandleCompletionAsync(httpContext, Database);
        }

        /// <summary>Answers a <c>GET /worlds</c> request with the worlds listing document.</summary>
        /// <param name="httpContext">The request context.</param>
        /// <returns>The asynchronous handling.</returns>
        public Task HandleWorlds(HttpContext httpContext)
        {
            return HandleWorldsAsync(httpContext, Database, CancellationToken);
        }

        /// <summary>Answers a <c>POST /worlds/fork</c> request, forking the document's source world under its new name.</summary>
        /// <param name="httpContext">The request context.</param>
        /// <returns>The asynchronous handling.</returns>
        public Task HandleWorldFork(HttpContext httpContext)
        {
            return HandleWorldForkAsync(httpContext, Database, CancellationToken);
        }

        /// <summary>Answers a <c>POST /worlds/drop</c> request, dropping the document's named world.</summary>
        /// <param name="httpContext">The request context.</param>
        /// <returns>The asynchronous handling.</returns>
        public Task HandleWorldDrop(HttpContext httpContext)
        {
            return HandleWorldDropAsync(httpContext, Database, CancellationToken);
        }

        /// <summary>Answers a <c>POST /worlds/query</c> request, running the document's query in its named world.</summary>
        /// <param name="httpContext">The request context.</param>
        /// <returns>The asynchronous handling.</returns>
        public Task HandleWorldQuery(HttpContext httpContext)
        {
            return HandleWorldQueryAsync(httpContext, Database, CancellationToken);
        }

        /// <summary>Answers a <c>POST /worlds/update</c> request, committing the document's update into its named world.</summary>
        /// <param name="httpContext">The request context.</param>
        /// <returns>The asynchronous handling.</returns>
        public Task HandleWorldUpdate(HttpContext httpContext)
        {
            return HandleWorldUpdateAsync(httpContext, Database, CancellationToken);
        }

        /// <summary>Answers a <c>GET /worlds/diff</c> request with the bounded diff document between the <c>from</c> and <c>to</c> worlds.</summary>
        /// <param name="httpContext">The request context.</param>
        /// <returns>The asynchronous handling.</returns>
        public Task HandleWorldDiff(HttpContext httpContext)
        {
            return HandleWorldDiffAsync(httpContext, Database, CancellationToken);
        }
    }

    /// <summary>
    /// Registers the serve command's cross-origin allowlist as the default CORS policy, carrying the origins
    /// as explicit state so the options callback passed to <c>AddCors</c> is a bound method group rather than
    /// a lambda closing over them.
    /// </summary>
    /// <param name="allowedOrigins">The origins granted cross-origin access; an entry of <c>*</c> grants any origin.</param>
    private sealed class CorsPolicyRegistration(IReadOnlyList<string> allowedOrigins)
    {
        /// <summary>The origins granted cross-origin access; an entry of <c>*</c> grants any origin.</summary>
        private IReadOnlyList<string> AllowedOrigins { get; } = allowedOrigins;

        /// <summary>Builds and registers the default policy: the allowlisted origins (or any origin on <c>*</c>), any request header, and the endpoint methods.</summary>
        /// <param name="options">The CORS options the default policy registers into.</param>
        public void Configure(CorsOptions options)
        {
            CorsPolicyBuilder policy = new();
            if(AllowedOrigins.Contains("*"))
            {
                policy.AllowAnyOrigin();
            }
            else
            {
                policy.WithOrigins([.. AllowedOrigins]);
            }

            policy.AllowAnyHeader().WithMethods(HttpMethods.Get, HttpMethods.Post);
            options.AddDefaultPolicy(policy.Build());
        }
    }

    /// <summary>
    /// Streams the served engine's trace bus to one client as Server-Sent Events: each wire event is an
    /// <c>event: trace</c> frame flushed as it is written, so the client's <c>EventSource</c> sees it live.
    /// Carries the hub and the server-lifetime token as explicit state so the route handler is a bound
    /// method group rather than a lambda closing over them.
    /// </summary>
    /// <param name="hub">The hub the served engine's trace events fan out through.</param>
    /// <param name="cancellationToken">The server-lifetime token that ends open streams on shutdown.</param>
    private sealed class TraceStreamRequestHandler(TraceStreamHub hub, CancellationToken cancellationToken)
    {
        /// <summary>The hub the served engine's trace events fan out through.</summary>
        private TraceStreamHub Hub { get; } = hub;

        /// <summary>The server-lifetime token that ends open streams on shutdown.</summary>
        private CancellationToken CancellationToken { get; } = cancellationToken;

        /// <summary>Answers a <c>GET /trace</c> request with the live trace stream; the response ends when the client disconnects or the server stops.</summary>
        /// <param name="httpContext">The request context.</param>
        /// <returns>The asynchronous handling.</returns>
        public async Task HandleTrace(HttpContext httpContext)
        {
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, httpContext.RequestAborted);
            using TraceStreamSubscription subscription = Hub.Subscribe();
            StringBuilder frame = new();
            try
            {
                //Flush the headers first so the client's EventSource opens before any event arrives.
                await httpContext.Response.Body.FlushAsync(linked.Token).ConfigureAwait(false);
                while(await subscription.Reader.WaitToReadAsync(linked.Token).ConfigureAwait(false))
                {
                    while(subscription.Reader.TryRead(out TraceWireEvent wire))
                    {
                        frame.Clear();
                        AppendTraceFrame(frame, in wire);
                        await httpContext.Response.WriteAsync(frame.ToString(), linked.Token).ConfigureAwait(false);
                    }

                    await httpContext.Response.Body.FlushAsync(linked.Token).ConfigureAwait(false);
                }
            }
            catch(OperationCanceledException)
            {
                //The client disconnected or the server is stopping; the stream simply ends.
            }
        }
    }

    /// <summary>Appends one Server-Sent-Events trace frame — the <c>event: trace</c> line and the wire event's JSON data line — to <paramref name="frameToAppendTo"/>.</summary>
    /// <param name="frameToAppendTo">The builder the frame is appended to.</param>
    /// <param name="wire">The wire event to encode.</param>
    private static void AppendTraceFrame(StringBuilder frameToAppendTo, in TraceWireEvent wire)
    {
        frameToAppendTo.Append("event: trace\ndata: {\"correlationId\":\"");
        frameToAppendTo.Append(wire.CorrelationId.ToString("D"));
        frameToAppendTo.Append("\",\"sequence\":");
        frameToAppendTo.Append(wire.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
        frameToAppendTo.Append(",\"kind\":\"");
        AppendJsonEscaped(frameToAppendTo, wire.Kind);
        frameToAppendTo.Append('"');
        if(wire.Term is not null)
        {
            frameToAppendTo.Append(",\"term\":\"");
            AppendJsonEscaped(frameToAppendTo, wire.Term);
            frameToAppendTo.Append('"');
        }

        frameToAppendTo.Append(",\"detail\":\"");
        AppendJsonEscaped(frameToAppendTo, wire.Detail);
        frameToAppendTo.Append("\"}\n\n");
    }

    /// <summary>
    /// Answers <c>GET /config</c> with the served front-end's runtime configuration. The CLI-served Studio
    /// page fetches this once at boot: the <c>{"engine":"http"}</c> marker tells it a server-side engine is
    /// answering at this origin, so it keeps the HTTP transport. A static host (GitHub Pages / offline) has
    /// no such endpoint — the resulting 404 (or an HTML fallback) tells the page to boot the in-browser WASM
    /// engine instead. The body is a fixed literal, so it is written directly rather than through a JSON writer.
    /// </summary>
    /// <param name="httpContext">The request context.</param>
    /// <returns>The asynchronous handling.</returns>
    private static Task HandleConfig(HttpContext httpContext)
    {
        httpContext.Response.ContentType = "application/json; charset=utf-8";

        return httpContext.Response.WriteAsync("{\"engine\":\"http\"}", httpContext.RequestAborted);
    }

    /// <summary>Extracts the query and any protocol dataset parameters from a POST — an <c>application/sparql-query</c> body (dataset parameters on the URL query string) or a form (query and dataset parameters as fields) — and answers it; any other content type answers 415.</summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="database">The open database.</param>
    /// <param name="cancellationToken">A token that aborts the request.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandlePostAsync(HttpContext httpContext, VeritasEngine database, CancellationToken cancellationToken)
    {
        string contentType = httpContext.Request.ContentType ?? string.Empty;
        if(contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            IFormCollection form = await httpContext.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            await HandleAsync(httpContext, database, ToQuery(form["query"]), ProtocolDataset(form["default-graph-uri"], form["named-graph-uri"]), cancellationToken: cancellationToken).ConfigureAwait(false);

            return;
        }

        //An application/sparql-query body is the query's UTF-8 bytes; route them straight to the byte-native
        //parse with no intervening string, the inbound counterpart to the byte-native readers the loader uses.
        //The StartsWith match tolerates a charset parameter, mirroring the form-urlencoded check above.
        if(contentType.StartsWith("application/sparql-query", StringComparison.OrdinalIgnoreCase))
        {
            Utf8String body = await ReadBodyAsync(httpContext.Request.Body, httpContext.Request.ContentLength, cancellationToken).ConfigureAwait(false);
            await HandleAsync(httpContext, database, body, ProtocolDataset(httpContext.Request.Query["default-graph-uri"], httpContext.Request.Query["named-graph-uri"]), cancellationToken: cancellationToken).ConfigureAwait(false);

            return;
        }

        //The protocol defines exactly the two POST forms above; anything else is an unsupported media type.
        httpContext.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
        await httpContext.Response.WriteAsync("POST /sparql expects application/x-www-form-urlencoded (a 'query' field) or application/sparql-query (the query as the body).", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the available graph-analytics algorithms as plain text — the HTTP discovery counterpart to the CLI <c>--list</c>.</summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="cancellationToken">A token that aborts the request.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandleAnalyticsListAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        httpContext.Response.ContentType = "text/plain; charset=utf-8";
        await httpContext.Response.WriteAsync(VeritasOperations.DescribeAnalytics().Output, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a graph-analytics algorithm over the served dataset from a form POST (<c>algorithm</c> and repeated <c>param</c> fields), writing CSV/TSV results or a plain-text error.</summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="dataPaths">The served dataset's document paths the analytics index is built from.</param>
    /// <param name="cancellationToken">A token that aborts the request.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandleAnalyticsPostAsync(HttpContext httpContext, IReadOnlyList<string> dataPaths, CancellationToken cancellationToken)
    {
        if(!httpContext.Request.HasFormContentType)
        {
            httpContext.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            await httpContext.Response.WriteAsync("POST /analytics expects application/x-www-form-urlencoded with an 'algorithm' field and optional 'param' fields.", cancellationToken).ConfigureAwait(false);

            return;
        }

        IFormCollection form = await httpContext.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        string algorithm = form["algorithm"].ToString();
        if(string.IsNullOrEmpty(algorithm))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("Missing 'algorithm'.", cancellationToken).ConfigureAwait(false);

            return;
        }

        string[] parameters = [.. form["param"].Where(static value => !string.IsNullOrEmpty(value)).Select(static value => value!)];

        string accept = httpContext.Request.Headers.Accept.ToString();
        SparqlDelimitedResultsFormat format = accept.Contains("text/tab-separated-values", StringComparison.OrdinalIgnoreCase)
            ? SparqlDelimitedResultsFormat.Tsv
            : SparqlDelimitedResultsFormat.Csv;

        OperationResult result = await VeritasOperations.RunGraphAnalyticsAsync(algorithm, dataPaths, parameters, format, cancellationToken).ConfigureAwait(false);

        if(!result.Succeeded)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync(result.ErrorMessage!, cancellationToken).ConfigureAwait(false);

            return;
        }

        httpContext.Response.ContentType = format == SparqlDelimitedResultsFormat.Tsv ? "text/tab-separated-values" : "text/csv";
        await httpContext.Response.WriteAsync(result.Output, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers a <c>POST /literal-diagnostics</c> request: the request document's <c>datatype</c> and
    /// <c>body</c> fields are handed to the Geo literal-diagnostics projection and its verdict is written
    /// back as the four-state diagnosis document. A datatype outside the geometry-literal family is an
    /// abstention — status <c>unsupported</c> — and never a fault, so an editor can send every typed
    /// literal it sees without classifying them first. The faults split as the protocol routes do: a
    /// content type other than <c>application/json</c> answers 415, and a document that is not the
    /// two-field object answers 400. The serve command always composes the Geo module, so all six
    /// geometry datatypes answer here.
    /// </summary>
    /// <param name="httpContext">The request context.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandleLiteralDiagnosticsAsync(HttpContext httpContext)
    {
        CancellationToken cancellationToken = httpContext.RequestAborted;
        string contentType = httpContext.Request.ContentType ?? string.Empty;

        //The StartsWith match tolerates a charset parameter, mirroring the protocol route's content-type checks.
        if(!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            httpContext.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            await httpContext.Response.WriteAsync("POST /literal-diagnostics expects application/json carrying a 'datatype' field and a 'body' field.", cancellationToken).ConfigureAwait(false);

            return;
        }

        Utf8String requestDocument = await ReadBodyAsync(httpContext.Request.Body, httpContext.Request.ContentLength, cancellationToken).ConfigureAwait(false);
        if(!TryReadLiteralDiagnosticsRequest(requestDocument.Memory.Span, out Utf8String datatypeIri, out ReadOnlyMemory<byte> literalBody))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("POST /literal-diagnostics expects a JSON object carrying exactly the string fields 'datatype' and 'body'.", cancellationToken).ConfigureAwait(false);

            return;
        }

        GeoLiteralDiagnosis diagnosis = GeoLiteralDiagnostics.Describe(datatypeIri, literalBody.Span, VeritasOperations.GeoJsonReader);

        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await httpContext.Response.WriteAsync(GeoLiteralDiagnosisJson.Write(datatypeIri, in diagnosis), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the literal-diagnostics request document — a JSON object carrying exactly the two string
    /// fields <c>datatype</c> and <c>body</c> — straight from its UTF-8 bytes, so neither the datatype IRI
    /// nor the literal body round-trips through a managed string on its way to the diagnostics face. A
    /// missing field, a duplicated field, an unrecognized field, a non-string value, a non-object
    /// document, trailing content, and any document the reader cannot scan are all refusals, which the
    /// caller answers as the protocol 400.
    /// </summary>
    /// <param name="utf8Request">The request body's UTF-8 bytes.</param>
    /// <param name="datatypeIri">The <c>datatype</c> field's value, or the default value on refusal.</param>
    /// <param name="literalBody">The <c>body</c> field's unescaped UTF-8 value, or the default value on refusal.</param>
    /// <returns><see langword="true"/> when the document is the expected two-field object.</returns>
    [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Justification = "The serialization quarantine keeps the System.Text.Json dependency inside the binding projects that own it; this application is a composition root that already references them, and the endpoint's inbound document is scanned with the in-box UTF-8 reader alone — no serializer, no converter, no reflection — so the two field values reach the diagnostics face as bytes.")]
    private static bool TryReadLiteralDiagnosticsRequest(ReadOnlySpan<byte> utf8Request, out Utf8String datatypeIri, out ReadOnlyMemory<byte> literalBody)
    {
        datatypeIri = default;
        literalBody = default;
        bool datatypeRead = false;
        bool bodyRead = false;

        System.Text.Json.Utf8JsonReader reader = new(utf8Request, new System.Text.Json.JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = System.Text.Json.JsonCommentHandling.Disallow
        });

        try
        {
            if(!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.StartObject)
            {
                return false;
            }

            while(reader.Read() && reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
            {
                bool datatypeField = reader.ValueTextEquals("datatype"u8);
                if(!datatypeField && !reader.ValueTextEquals("body"u8))
                {
                    return false;
                }

                if(!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.String)
                {
                    return false;
                }

                if(datatypeField)
                {
                    if(datatypeRead)
                    {
                        return false;
                    }

                    datatypeIri = new Utf8String(ReadLiteralDiagnosticsValue(ref reader));
                    datatypeRead = true;
                }
                else
                {
                    if(bodyRead)
                    {
                        return false;
                    }

                    literalBody = ReadLiteralDiagnosticsValue(ref reader);
                    bodyRead = true;
                }
            }

            //The loop stops on the token that ended the members: only the object's own end token, with both
            //fields read and nothing following the document, is the shape this endpoint answers.
            if(reader.TokenType != System.Text.Json.JsonTokenType.EndObject || !datatypeRead || !bodyRead || reader.Read())
            {
                return false;
            }
        }
        catch(System.Text.Json.JsonException)
        {
            //A document the reader cannot scan is an expected client fault, not an invariant violation: the
            //in-box reader reports it by exception, and this is the seam that turns it back into a value.
            return false;
        }

        return true;
    }

    /// <summary>Copies the current string token's unescaped UTF-8 value into an owned buffer; the escaped token length bounds the unescaped value, so one exactly-sliced allocation holds it.</summary>
    /// <param name="reader">The reader positioned on a string token.</param>
    /// <returns>The unescaped UTF-8 value.</returns>
    [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Justification = "The serialization quarantine keeps the System.Text.Json dependency inside the binding projects that own it; this application is a composition root that already references them, and the endpoint's inbound document is scanned with the in-box UTF-8 reader alone — no serializer, no converter, no reflection — so the two field values reach the diagnostics face as bytes.")]
    private static ReadOnlyMemory<byte> ReadLiteralDiagnosticsValue(ref System.Text.Json.Utf8JsonReader reader)
    {
        int maximumLength = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
        byte[] buffer = new byte[maximumLength];
        int written = reader.CopyString(buffer);

        return new ReadOnlyMemory<byte>(buffer, 0, written);
    }

    /// <summary>
    /// Answers a <c>POST /completion</c> request: the request document's <c>query</c> and <c>caret</c> fields
    /// name an editor buffer and a position in it, and the answer is the completion context the popup reads —
    /// the token kinds the grammar admits next, the enclosing productions, the in-scope variables each with the
    /// datatype the SERVED dataset determines for it, and the variable-predicate pairs. The faults split as the
    /// protocol routes do: a content type other than <c>application/json</c> answers 415, and a document that is
    /// not the two-field object answers 400.
    /// </summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="database">The open database the in-scope variables' datatypes resolve against.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandleCompletionAsync(HttpContext httpContext, VeritasEngine database)
    {
        CancellationToken cancellationToken = httpContext.RequestAborted;
        string contentType = httpContext.Request.ContentType ?? string.Empty;

        //The StartsWith match tolerates a charset parameter, mirroring the protocol route's content-type checks.
        if(!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            httpContext.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            await httpContext.Response.WriteAsync("POST /completion expects application/json carrying a 'query' field and a 'caret' field.", cancellationToken).ConfigureAwait(false);

            return;
        }

        Utf8String requestDocument = await ReadBodyAsync(httpContext.Request.Body, httpContext.Request.ContentLength, cancellationToken).ConfigureAwait(false);
        if(!TryReadCompletionRequest(requestDocument.Memory.Span, out string query, out int caretCharOffset))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("POST /completion expects a JSON object carrying exactly the string field 'query' and the 32-bit-integer field 'caret'.", cancellationToken).ConfigureAwait(false);

            return;
        }

        SparqlCompletionContext context = SparqlCompletion.Describe(Encoding.UTF8.GetBytes(query), CaretByteOffset(query, caretCharOffset));

        //The variables' datatypes come from the dataset this host serves — the SHACL shape, the declared range,
        //then a sampled value — so the editor's answer describes the data the same endpoint queries.
        SparqlCompletionContext resolved = await SparqlCompletionDatatypes.ResolveAsync(database, context, cancellationToken: cancellationToken).ConfigureAwait(false);

        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await httpContext.Response.WriteAsync(CompletionContextJson.Write(resolved), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers a <c>POST /turtle-completion</c> request: the request document's <c>source</c>, <c>caret</c>, and
    /// <c>syntax</c> fields name a Turtle-family buffer, a position in it, and the flavour to parse it as, and
    /// the answer is the completion context the popup reads — the token kinds the grammar admits next and the
    /// enclosing productions. The grammar is the whole authority here, so the route needs no server state. The
    /// faults split as the SPARQL completion route's do: 415 outside <c>application/json</c>, 400 for a document
    /// that is not the three-field object.
    /// </summary>
    /// <param name="httpContext">The request context.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandleTurtleCompletionAsync(HttpContext httpContext)
    {
        CancellationToken cancellationToken = httpContext.RequestAborted;
        string contentType = httpContext.Request.ContentType ?? string.Empty;

        //The StartsWith match tolerates a charset parameter, mirroring the protocol route's content-type checks.
        if(!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            httpContext.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            await httpContext.Response.WriteAsync("POST /turtle-completion expects application/json carrying a 'source' field, a 'caret' field, and a 'syntax' field.", cancellationToken).ConfigureAwait(false);

            return;
        }

        Utf8String requestDocument = await ReadBodyAsync(httpContext.Request.Body, httpContext.Request.ContentLength, cancellationToken).ConfigureAwait(false);
        if(!TryReadTurtleCompletionRequest(requestDocument.Memory.Span, out string source, out int caretCharOffset, out string syntax))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("POST /turtle-completion expects a JSON object carrying exactly the string fields 'source' and 'syntax' and the 32-bit-integer field 'caret'.", cancellationToken).ConfigureAwait(false);

            return;
        }

        TurtleCompletionContext context = TurtleCompletion.Describe(Encoding.UTF8.GetBytes(source), CaretByteOffset(source, caretCharOffset), TurtleCompletionJson.ParseSyntax(syntax));

        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await httpContext.Response.WriteAsync(TurtleCompletionJson.Write(context), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers <c>GET /editor-vocabulary</c> with the editor's fixed candidate corpus as a JSON array. The
    /// corpus takes no input and never changes over a process, so the route answers the document composed once
    /// at startup.
    /// </summary>
    /// <param name="httpContext">The request context.</param>
    /// <returns>The asynchronous handling.</returns>
    private static Task HandleEditorVocabulary(HttpContext httpContext)
    {
        httpContext.Response.ContentType = "application/json; charset=utf-8";

        return httpContext.Response.WriteAsync(EditorVocabularyDocument, httpContext.RequestAborted);
    }

    /// <summary>Answers a <c>GET /worlds</c> request with the worlds listing document: each world's name, its content-addressed state identifier, and its fork parent, the primary world first.</summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="database">The open database whose worlds are listed.</param>
    /// <param name="cancellationToken">A token that aborts the request.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandleWorldsAsync(HttpContext httpContext, VeritasEngine database, CancellationToken cancellationToken)
    {
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await httpContext.Response.WriteAsync(WorldsJson.WriteWorlds(database.DescribeWorlds()), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers a <c>POST /worlds/fork</c> request: the request document's <c>source</c> and <c>name</c>
    /// fields name the world to fork and the fork's new name, and the answer is the outcome document — an
    /// unknown source and a taken name are expected conditions and cross as outcome tokens, never faults.
    /// The faults split as the other first-party routes do: a content type other than
    /// <c>application/json</c> answers 415, and a document that is not the two-field object answers 400.
    /// </summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="database">The open database the fork registers on.</param>
    /// <param name="cancellationToken">A token that aborts the request.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandleWorldForkAsync(HttpContext httpContext, VeritasEngine database, CancellationToken cancellationToken)
    {
        string contentType = httpContext.Request.ContentType ?? string.Empty;
        if(!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            httpContext.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            await httpContext.Response.WriteAsync("POST /worlds/fork expects application/json carrying a 'source' field and a 'name' field.", cancellationToken).ConfigureAwait(false);

            return;
        }

        Utf8String requestDocument = await ReadBodyAsync(httpContext.Request.Body, httpContext.Request.ContentLength, cancellationToken).ConfigureAwait(false);
        if(!TryReadTwoStringFields(requestDocument.Memory.Span, "source"u8, "name"u8, out string source, out string name))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("POST /worlds/fork expects a JSON object carrying exactly the string fields 'source' and 'name'.", cancellationToken).ConfigureAwait(false);

            return;
        }

        WorldForkOutcome outcome = await database.ForkWorldAsync(source, name, cancellationToken).ConfigureAwait(false);
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await httpContext.Response.WriteAsync(WorldsJson.WriteForkOutcome(outcome), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers a <c>POST /worlds/drop</c> request: the request document's <c>world</c> field names the world
    /// to drop, and the answer is the outcome document — an unknown name and the never-droppable primary
    /// world are expected conditions and cross as outcome tokens. The faults split as the fork route's do.
    /// </summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="database">The open database the drop applies to.</param>
    /// <param name="cancellationToken">A token that aborts the request.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandleWorldDropAsync(HttpContext httpContext, VeritasEngine database, CancellationToken cancellationToken)
    {
        string contentType = httpContext.Request.ContentType ?? string.Empty;
        if(!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            httpContext.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            await httpContext.Response.WriteAsync("POST /worlds/drop expects application/json carrying a 'world' field.", cancellationToken).ConfigureAwait(false);

            return;
        }

        Utf8String requestDocument = await ReadBodyAsync(httpContext.Request.Body, httpContext.Request.ContentLength, cancellationToken).ConfigureAwait(false);
        if(!TryReadOneStringField(requestDocument.Memory.Span, "world"u8, out string world))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("POST /worlds/drop expects a JSON object carrying exactly the string field 'world'.", cancellationToken).ConfigureAwait(false);

            return;
        }

        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await httpContext.Response.WriteAsync(WorldsJson.WriteDropOutcome(database.DropWorld(world)), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers a <c>POST /worlds/query</c> request: the request document's <c>world</c> and <c>query</c>
    /// fields name a registered world and the query to run in it, and the answer rides the same
    /// content-negotiated rendering the protocol endpoint uses — SPARQL-results-JSON for the Studio's
    /// transport, with the <c>{"error":…}</c> failure document on a bad query or an unknown world. The
    /// faults split as the fork route's do.
    /// </summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="database">The open database the query runs against.</param>
    /// <param name="cancellationToken">A token that aborts the request.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandleWorldQueryAsync(HttpContext httpContext, VeritasEngine database, CancellationToken cancellationToken)
    {
        string contentType = httpContext.Request.ContentType ?? string.Empty;
        if(!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            httpContext.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            await httpContext.Response.WriteAsync("POST /worlds/query expects application/json carrying a 'world' field and a 'query' field.", cancellationToken).ConfigureAwait(false);

            return;
        }

        Utf8String requestDocument = await ReadBodyAsync(httpContext.Request.Body, httpContext.Request.ContentLength, cancellationToken).ConfigureAwait(false);
        if(!TryReadTwoStringFields(requestDocument.Memory.Span, "world"u8, "query"u8, out string world, out string query))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("POST /worlds/query expects a JSON object carrying exactly the string fields 'world' and 'query'.", cancellationToken).ConfigureAwait(false);

            return;
        }

        await HandleAsync(httpContext, database, ToQuery(query), protocolDataset: null, world: world, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers a <c>POST /worlds/update</c> request: the request document's <c>world</c> and <c>update</c>
    /// fields name a registered world and the SPARQL Update to commit into it, and the answer is the
    /// acknowledgement document. An update that does not parse, a query where an update belongs, and an
    /// unknown world all answer 400 with the <c>{"error":…}</c> document. The faults split as the fork
    /// route's do.
    /// </summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="database">The open database the update commits into.</param>
    /// <param name="cancellationToken">A token that aborts the request.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandleWorldUpdateAsync(HttpContext httpContext, VeritasEngine database, CancellationToken cancellationToken)
    {
        string contentType = httpContext.Request.ContentType ?? string.Empty;
        if(!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            httpContext.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            await httpContext.Response.WriteAsync("POST /worlds/update expects application/json carrying a 'world' field and an 'update' field.", cancellationToken).ConfigureAwait(false);

            return;
        }

        Utf8String requestDocument = await ReadBodyAsync(httpContext.Request.Body, httpContext.Request.ContentLength, cancellationToken).ConfigureAwait(false);
        if(!TryReadTwoStringFields(requestDocument.Memory.Span, "world"u8, "update"u8, out string world, out string update))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("POST /worlds/update expects a JSON object carrying exactly the string fields 'world' and 'update'.", cancellationToken).ConfigureAwait(false);

            return;
        }

        try
        {
            await database.UpdateAsync(ToQuery(update), world: world, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch(ArgumentException ex)
        {
            //An update that does not parse, a query where an update belongs, or an unknown world: the
            //engine names them all by argument refusal, and this seam turns that into the failure document.
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            httpContext.Response.ContentType = "application/json; charset=utf-8";
            await httpContext.Response.WriteAsync(ErrorJson(ex.Message), cancellationToken).ConfigureAwait(false);

            return;
        }

        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await httpContext.Response.WriteAsync(WorldsJson.UpdatedDocument, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers a <c>GET /worlds/diff</c> request: the <c>from</c> and <c>to</c> query parameters name the
    /// baseline world and the world whose state the transitions produce, and the answer is the bounded diff
    /// document — exact totals always, listed triples capped. An unknown world on either side crosses as
    /// the outcome document; missing parameters answer 400.
    /// </summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="database">The open database the worlds are diffed on.</param>
    /// <param name="cancellationToken">A token that aborts the request.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandleWorldDiffAsync(HttpContext httpContext, VeritasEngine database, CancellationToken cancellationToken)
    {
        string from = httpContext.Request.Query["from"].ToString();
        string to = httpContext.Request.Query["to"].ToString();
        if(string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("GET /worlds/diff expects non-empty 'from' and 'to' query parameters naming the baseline world and the diffed world.", cancellationToken).ConfigureAwait(false);

            return;
        }

        WorldDiff diff = database.DiffWorlds(to, from);
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await httpContext.Response.WriteAsync(WorldsJson.WriteDiff(in diff), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a request document that must be a JSON object carrying exactly two string fields with the given
    /// names, in any order, each exactly once. Anything else — a document the reader cannot scan, a missing
    /// or repeated field, an unrecognized field, a non-string value, trailing content — answers
    /// <see langword="false"/>, so every worlds route draws one strict 400 boundary.
    /// </summary>
    /// <param name="utf8Request">The request document bytes.</param>
    /// <param name="firstField">The first field's name.</param>
    /// <param name="secondField">The second field's name.</param>
    /// <param name="firstValue">Receives the first field's value.</param>
    /// <param name="secondValue">Receives the second field's value.</param>
    /// <returns>Whether the document is exactly the two-field object.</returns>
    [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Justification = "The serialization quarantine keeps the System.Text.Json dependency inside the binding projects that own it; this application is a composition root that already references them, and the endpoint's inbound document is scanned with the in-box UTF-8 reader alone — no serializer, no converter, no reflection.")]
    private static bool TryReadTwoStringFields(ReadOnlySpan<byte> utf8Request, ReadOnlySpan<byte> firstField, ReadOnlySpan<byte> secondField, out string firstValue, out string secondValue)
    {
        firstValue = string.Empty;
        secondValue = string.Empty;
        bool firstRead = false;
        bool secondRead = false;

        System.Text.Json.Utf8JsonReader reader = new(utf8Request, new System.Text.Json.JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = System.Text.Json.JsonCommentHandling.Disallow
        });

        try
        {
            if(!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.StartObject)
            {
                return false;
            }

            while(reader.Read() && reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
            {
                bool isFirst = reader.ValueTextEquals(firstField);
                if(!isFirst && !reader.ValueTextEquals(secondField))
                {
                    return false;
                }

                if(!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.String)
                {
                    return false;
                }

                if(isFirst)
                {
                    if(firstRead)
                    {
                        return false;
                    }

                    firstValue = reader.GetString() ?? string.Empty;
                    firstRead = true;
                }
                else
                {
                    if(secondRead)
                    {
                        return false;
                    }

                    secondValue = reader.GetString() ?? string.Empty;
                    secondRead = true;
                }
            }

            //The loop stops on the token that ended the members: only the object's own end token, with both
            //fields read and nothing following the document, is the shape these endpoints answer.
            if(reader.TokenType != System.Text.Json.JsonTokenType.EndObject || !firstRead || !secondRead || reader.Read())
            {
                return false;
            }
        }
        catch(System.Text.Json.JsonException)
        {
            //A document the reader cannot scan is an expected client fault, not an invariant violation: the
            //in-box reader reports it by exception, and this is the seam that turns it back into a value.
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads a request document that must be a JSON object carrying exactly one string field with the given
    /// name. The boundary is the same strict one <see cref="TryReadTwoStringFields"/> draws.
    /// </summary>
    /// <param name="utf8Request">The request document bytes.</param>
    /// <param name="field">The field's name.</param>
    /// <param name="value">Receives the field's value.</param>
    /// <returns>Whether the document is exactly the one-field object.</returns>
    [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Justification = "The serialization quarantine keeps the System.Text.Json dependency inside the binding projects that own it; this application is a composition root that already references them, and the endpoint's inbound document is scanned with the in-box UTF-8 reader alone — no serializer, no converter, no reflection.")]
    private static bool TryReadOneStringField(ReadOnlySpan<byte> utf8Request, ReadOnlySpan<byte> field, out string value)
    {
        value = string.Empty;
        bool valueRead = false;

        System.Text.Json.Utf8JsonReader reader = new(utf8Request, new System.Text.Json.JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = System.Text.Json.JsonCommentHandling.Disallow
        });

        try
        {
            if(!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.StartObject)
            {
                return false;
            }

            while(reader.Read() && reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
            {
                if(!reader.ValueTextEquals(field) || valueRead)
                {
                    return false;
                }

                if(!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.String)
                {
                    return false;
                }

                value = reader.GetString() ?? string.Empty;
                valueRead = true;
            }

            if(reader.TokenType != System.Text.Json.JsonTokenType.EndObject || !valueRead || reader.Read())
            {
                return false;
            }
        }
        catch(System.Text.Json.JsonException)
        {
            //A document the reader cannot scan is an expected client fault, not an invariant violation: the
            //in-box reader reports it by exception, and this is the seam that turns it back into a value.
            return false;
        }

        return true;
    }

    /// <summary>
    /// Converts an editor caret — a UTF-16 code-unit index into <paramref name="text"/>, clamped to it — into
    /// the UTF-8 byte offset the completion parsers address. The caret is the one currency the wire carries on
    /// every tier, so both hosts run this same conversion and a caret inside an astral character or a multi-byte
    /// sequence still lands on a buffer position.
    /// </summary>
    /// <param name="text">The editor buffer the caret indexes.</param>
    /// <param name="caretCharOffset">The caret as a UTF-16 code-unit index, possibly outside the buffer.</param>
    /// <returns>The caret's byte offset into the buffer's UTF-8 encoding.</returns>
    private static int CaretByteOffset(string text, int caretCharOffset)
    {
        int caret = Math.Clamp(caretCharOffset, 0, text.Length);

        return Encoding.UTF8.GetByteCount(text.AsSpan(0, caret));
    }

    /// <summary>
    /// Reads the SPARQL completion request document — a JSON object carrying exactly the string field
    /// <c>query</c> and the number field <c>caret</c>. The query text is taken as a managed string because the
    /// caret that comes with it indexes UTF-16 code units, the editor's own currency, and the two are
    /// meaningless apart. A missing field, a duplicated field, an unrecognized field, a value of the wrong
    /// token kind, a caret no 32-bit integer represents, a non-object document, trailing content, and any
    /// document the reader cannot scan are all refusals, which the caller answers as the protocol 400.
    /// </summary>
    /// <param name="utf8Request">The request body's UTF-8 bytes.</param>
    /// <param name="query">The <c>query</c> field's value, or the empty string on refusal.</param>
    /// <param name="caretCharOffset">The <c>caret</c> field's value, or zero on refusal.</param>
    /// <returns><see langword="true"/> when the document is the expected two-field object.</returns>
    [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Justification = "The serialization quarantine keeps the System.Text.Json dependency inside the binding projects that own it; this application is a composition root that already references them, and the endpoint's inbound document is scanned with the in-box UTF-8 reader alone — no serializer, no converter, no reflection.")]
    private static bool TryReadCompletionRequest(ReadOnlySpan<byte> utf8Request, out string query, out int caretCharOffset)
    {
        query = string.Empty;
        caretCharOffset = 0;
        bool queryRead = false;
        bool caretRead = false;

        System.Text.Json.Utf8JsonReader reader = new(utf8Request, new System.Text.Json.JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = System.Text.Json.JsonCommentHandling.Disallow
        });

        try
        {
            if(!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.StartObject)
            {
                return false;
            }

            while(reader.Read() && reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
            {
                if(reader.ValueTextEquals("query"u8))
                {
                    if(queryRead || !reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.String)
                    {
                        return false;
                    }

                    query = reader.GetString()!;
                    queryRead = true;
                }
                else if(reader.ValueTextEquals("caret"u8))
                {
                    if(caretRead || !reader.Read() || !TryReadCaret(ref reader, out caretCharOffset))
                    {
                        return false;
                    }

                    caretRead = true;
                }
                else
                {
                    return false;
                }
            }

            //The loop stops on the token that ended the members: only the object's own end token, with both
            //fields read and nothing following the document, is the shape this endpoint answers.
            if(reader.TokenType != System.Text.Json.JsonTokenType.EndObject || !queryRead || !caretRead || reader.Read())
            {
                return false;
            }
        }
        catch(System.Text.Json.JsonException)
        {
            //A document the reader cannot scan is an expected client fault, not an invariant violation: the
            //in-box reader reports it by exception, and this is the seam that turns it back into a value.
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads the Turtle-family completion request document — a JSON object carrying exactly the string fields
    /// <c>source</c> and <c>syntax</c> and the number field <c>caret</c>. The buffer text is taken as a managed
    /// string for the same reason the SPARQL request's is: the caret beside it indexes UTF-16 code units. The
    /// refusals are that request's exactly, and the caller answers them as the protocol 400.
    /// </summary>
    /// <param name="utf8Request">The request body's UTF-8 bytes.</param>
    /// <param name="source">The <c>source</c> field's value, or the empty string on refusal.</param>
    /// <param name="caretCharOffset">The <c>caret</c> field's value, or zero on refusal.</param>
    /// <param name="syntax">The <c>syntax</c> field's value, or the empty string on refusal.</param>
    /// <returns><see langword="true"/> when the document is the expected three-field object.</returns>
    [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Justification = "The serialization quarantine keeps the System.Text.Json dependency inside the binding projects that own it; this application is a composition root that already references them, and the endpoint's inbound document is scanned with the in-box UTF-8 reader alone — no serializer, no converter, no reflection.")]
    private static bool TryReadTurtleCompletionRequest(ReadOnlySpan<byte> utf8Request, out string source, out int caretCharOffset, out string syntax)
    {
        source = string.Empty;
        syntax = string.Empty;
        caretCharOffset = 0;
        bool sourceRead = false;
        bool caretRead = false;
        bool syntaxRead = false;

        System.Text.Json.Utf8JsonReader reader = new(utf8Request, new System.Text.Json.JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = System.Text.Json.JsonCommentHandling.Disallow
        });

        try
        {
            if(!reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.StartObject)
            {
                return false;
            }

            while(reader.Read() && reader.TokenType == System.Text.Json.JsonTokenType.PropertyName)
            {
                if(reader.ValueTextEquals("source"u8))
                {
                    if(sourceRead || !reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.String)
                    {
                        return false;
                    }

                    source = reader.GetString()!;
                    sourceRead = true;
                }
                else if(reader.ValueTextEquals("syntax"u8))
                {
                    if(syntaxRead || !reader.Read() || reader.TokenType != System.Text.Json.JsonTokenType.String)
                    {
                        return false;
                    }

                    syntax = reader.GetString()!;
                    syntaxRead = true;
                }
                else if(reader.ValueTextEquals("caret"u8))
                {
                    if(caretRead || !reader.Read() || !TryReadCaret(ref reader, out caretCharOffset))
                    {
                        return false;
                    }

                    caretRead = true;
                }
                else
                {
                    return false;
                }
            }

            //The loop stops on the token that ended the members: only the object's own end token, with all three
            //fields read and nothing following the document, is the shape this endpoint answers.
            if(reader.TokenType != System.Text.Json.JsonTokenType.EndObject || !sourceRead || !caretRead || !syntaxRead || reader.Read())
            {
                return false;
            }
        }
        catch(System.Text.Json.JsonException)
        {
            //A document the reader cannot scan is an expected client fault, not an invariant violation: the
            //in-box reader reports it by exception, and this is the seam that turns it back into a value.
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads a caret field from the token the reader stands on: the editor's UTF-16 code-unit index, whose
    /// contract is a JSON number a 32-bit integer represents. A token of any other kind, a fractional or
    /// exponent-form number, and a magnitude outside the 32-bit range are refusals the caller answers as the
    /// protocol 400 — the value never reaches the completion face as something other than a position.
    /// </summary>
    /// <param name="reader">The reader positioned on the caret field's value token.</param>
    /// <param name="caret">The caret as a UTF-16 code-unit index, or zero on refusal.</param>
    /// <returns><see langword="true"/> when the token is a number the 32-bit range holds.</returns>
    [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Justification = "The serialization quarantine keeps the System.Text.Json dependency inside the binding projects that own it; this application is a composition root that already references them, and the endpoint's inbound document is scanned with the in-box UTF-8 reader alone — no serializer, no converter, no reflection.")]
    private static bool TryReadCaret(ref System.Text.Json.Utf8JsonReader reader, out int caret)
    {
        if(reader.TokenType != System.Text.Json.JsonTokenType.Number)
        {
            caret = 0;

            return false;
        }

        return reader.TryGetInt32(out caret);
    }

    /// <summary>Answers one query against the database: content negotiation picks the tabular and graph format preferences, the shared operation renders shape-first, and failures map onto the protocol's status codes (400 malformed / unacceptable dataset, 500 refused).</summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="database">The open database.</param>
    /// <param name="query">The query as an owned UTF-8 string (possibly empty).</param>
    /// <param name="protocolDataset">The protocol-supplied dataset description, or <see langword="null"/> when the request carried no dataset parameter.</param>
    /// <param name="world">The registered world the query runs in, or <see langword="null"/> for the primary path — the protocol routes always pass none; only the first-party worlds route names one.</param>
    /// <param name="cancellationToken">A token that aborts the request.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandleAsync(HttpContext httpContext, VeritasEngine database, Utf8String query, DatasetClause? protocolDataset, string? world = null, CancellationToken cancellationToken = default)
    {
        if(IsBlank(query.Memory.Span))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("Missing 'query'.", cancellationToken).ConfigureAwait(false);

            return;
        }

        string accept = httpContext.Request.Headers.Accept.ToString();
        SparqlTabularResultsFormat tabularFormat = TabularFormatFrom(accept);
        SparqlGraphResultsFormat graphFormat = GraphFormatFrom(accept);

        QueryAnswer answer = await VeritasOperations.ExecuteQueryAsync(database, query, baseIri: string.Empty, tabularFormat, graphFormat, protocolDataset, world, cancellationToken).ConfigureAwait(false);
        if(!answer.Result.Succeeded)
        {
            httpContext.Response.StatusCode = answer.Result.FailureKind == OperationFailureKind.Refused
                ? StatusCodes.Status500InternalServerError
                : StatusCodes.Status400BadRequest;

            //The in-browser Studio's HTTP transport asks for SPARQL-results-JSON and parses a {"error":…}
            //document on failure; every other caller reads the plain-text message.
            if(tabularFormat == SparqlTabularResultsFormat.Json)
            {
                httpContext.Response.ContentType = VeritasOperations.JsonResultsContentType;
                await httpContext.Response.WriteAsync(ErrorJson(answer.Result.ErrorMessage!), cancellationToken).ConfigureAwait(false);

                return;
            }

            await httpContext.Response.WriteAsync(answer.Result.ErrorMessage!, cancellationToken).ConfigureAwait(false);

            return;
        }

        httpContext.Response.ContentType = answer.ContentType;
        await httpContext.Response.WriteAsync(answer.Result.Output, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Selects the SELECT/ASK result format from the Accept header: the first of SPARQL-results-XML, SPARQL-results-JSON, and TSV whose token is acceptable; otherwise CSV, the default.</summary>
    /// <param name="accept">The raw Accept header (possibly empty).</param>
    /// <returns>The tabular format preference.</returns>
    private static SparqlTabularResultsFormat TabularFormatFrom(string accept)
    {
        if(AcceptsMediaType(accept, VeritasOperations.XmlResultsContentType))
        {
            return SparqlTabularResultsFormat.Xml;
        }

        if(AcceptsMediaType(accept, VeritasOperations.JsonResultsContentType))
        {
            return SparqlTabularResultsFormat.Json;
        }

        if(AcceptsMediaType(accept, "text/tab-separated-values"))
        {
            return SparqlTabularResultsFormat.Tsv;
        }

        return SparqlTabularResultsFormat.Csv;
    }

    /// <summary>Selects the CONSTRUCT/DESCRIBE serialization from the Accept header: Turtle when its token is acceptable, otherwise N-Triples, the default.</summary>
    /// <param name="accept">The raw Accept header (possibly empty).</param>
    /// <returns>The graph format preference.</returns>
    private static SparqlGraphResultsFormat GraphFormatFrom(string accept)
    {
        return AcceptsMediaType(accept, "text/turtle") ? SparqlGraphResultsFormat.Turtle : SparqlGraphResultsFormat.NTriples;
    }

    /// <summary>
    /// Whether the Accept header lists <paramref name="mediaType"/> as acceptable: the token is present as a
    /// whole media range (terminated by the segment end or a parameter list) and not weighted <c>q=0</c>,
    /// which RFC 7231 defines as "not acceptable". Non-zero weights are deliberately not ranked further —
    /// server-driven negotiation answers by the endpoint's fixed precedence over the acceptable formats.
    /// </summary>
    /// <param name="accept">The raw Accept header.</param>
    /// <param name="mediaType">The media type token to look for.</param>
    /// <returns><see langword="true"/> when the media type is acceptable.</returns>
    private static bool AcceptsMediaType(string accept, string mediaType)
    {
        foreach(string part in accept.Split(','))
        {
            ReadOnlySpan<char> segment = part.AsSpan().Trim();
            if(!segment.StartsWith(mediaType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ReadOnlySpan<char> parameters = segment[mediaType.Length..].TrimStart();
            if(parameters.Length > 0 && parameters[0] != ';')
            {
                continue;
            }

            if(!HasZeroQuality(parameters))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a media range's parameter list carries a <c>q=0</c> weight (any <c>0</c> / <c>0.0</c> / <c>0.00</c> / <c>0.000</c> spelling).</summary>
    /// <param name="parameters">The media range's remainder after the type token, beginning at its first <c>;</c> when parameters exist.</param>
    /// <returns><see langword="true"/> when a zero quality weight is present.</returns>
    private static bool HasZeroQuality(ReadOnlySpan<char> parameters)
    {
        while(true)
        {
            int semicolon = parameters.IndexOf(';');
            if(semicolon < 0)
            {
                return false;
            }

            parameters = parameters[(semicolon + 1)..].TrimStart();
            if(parameters.Length >= 2 && parameters[0] is 'q' or 'Q' && parameters[1] == '=')
            {
                ReadOnlySpan<char> value = parameters[2..];
                int end = value.IndexOf(';');
                if(end >= 0)
                {
                    value = value[..end];
                }

                value = value.Trim();
                if(value.Length > 0 && value[0] == '0')
                {
                    bool zero = true;
                    for(int i = 1; i < value.Length; i++)
                    {
                        if(value[i] is not ('.' or '0'))
                        {
                            zero = false;

                            break;
                        }
                    }

                    if(zero)
                    {
                        return true;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Builds the protocol dataset from the request's repeatable dataset parameters, or
    /// <see langword="null"/> when neither parameter carries a value — the null keeps the query's own
    /// <c>FROM</c>/<c>FROM NAMED</c> clause in force, since the protocol's precedence rule applies only
    /// when a protocol dataset is actually supplied.
    /// </summary>
    /// <param name="defaultGraphs">The repeatable <c>default-graph-uri</c> values.</param>
    /// <param name="namedGraphs">The repeatable <c>named-graph-uri</c> values.</param>
    /// <returns>The protocol dataset clause, or <see langword="null"/>.</returns>
    private static DatasetClause? ProtocolDataset(StringValues defaultGraphs, StringValues namedGraphs)
    {
        List<IriRef> defaults = new(defaultGraphs.Count);
        foreach(string? value in defaultGraphs)
        {
            if(!string.IsNullOrEmpty(value))
            {
                defaults.Add(new IriRef(Utf8Strings.From(value), SourceSpan.None));
            }
        }

        List<IriRef> named = new(namedGraphs.Count);
        foreach(string? value in namedGraphs)
        {
            if(!string.IsNullOrEmpty(value))
            {
                named.Add(new IriRef(Utf8Strings.From(value), SourceSpan.None));
            }
        }

        if(defaults.Count == 0 && named.Count == 0)
        {
            return null;
        }

        return new DatasetClause(SourceSpan.None, defaults, named);
    }

    /// <summary>Answers a query-less GET with the endpoint's SPARQL 1.1 Service Description (Turtle), generated from live state per request so the address and the registered extension surface are always current.</summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="cancellationToken">A token that aborts the request.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandleServiceDescriptionAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        string endpoint = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.Path}";
        httpContext.Response.ContentType = "text/turtle; charset=utf-8";
        await httpContext.Response.WriteAsync(ServiceDescriptionDocument.Render(endpoint, VeritasOperations.EngineOptions), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Encodes a query taken from the URL query string or a form field to UTF-8 bytes; an absent value yields an empty buffer, read downstream as a missing query.</summary>
    /// <param name="query">The query string, or <see langword="null"/> when the parameter is absent.</param>
    /// <returns>The query as an owned UTF-8 string, or the empty string when there is none.</returns>
    private static Utf8String ToQuery(string? query)
    {
        return string.IsNullOrEmpty(query) ? default : Utf8Strings.From(query);
    }

    /// <summary>Reads the entire request body into an owned <see cref="Utf8String"/>, so a byte-native request document — an <c>application/sparql-query</c> POST's query, a literal-diagnostics request — reaches its byte-native entry without a string round-trip. A declared length reads once into an exactly-sized buffer; an unknown length buffers then copies once.</summary>
    /// <param name="body">The request body stream.</param>
    /// <param name="contentLength">The declared body length, if known, to size the read in a single allocation.</param>
    /// <param name="cancellationToken">A token that aborts the read.</param>
    /// <returns>The body as an owned UTF-8 string.</returns>
    private static async Task<Utf8String> ReadBodyAsync(Stream body, long? contentLength, CancellationToken cancellationToken)
    {
        //A declared Content-Length (the normal case) reads once into a single exactly-sized owned buffer — no
        //intermediate MemoryStream and no second copy. An unknown length falls back to buffer-then-copy.
        if(contentLength is > 0 and <= int.MaxValue)
        {
            byte[] owned = new byte[(int)contentLength.Value];
            await body.ReadExactlyAsync(owned, cancellationToken).ConfigureAwait(false);

            return Utf8String.WithoutPrecomputedHash(owned);
        }

        using MemoryStream buffer = new();
        await body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        return Utf8String.WithoutPrecomputedHash(buffer.ToArray());
    }

    /// <summary>Whether a query buffer is blank — empty or only ASCII whitespace — the byte equivalent of the missing-or-whitespace-only check on a query string.</summary>
    /// <param name="query">The query bytes.</param>
    /// <returns><see langword="true"/> when the buffer holds no non-whitespace byte.</returns>
    private static bool IsBlank(ReadOnlySpan<byte> query)
    {
        foreach(byte b in query)
        {
            if(b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Opens the default browser at the served address; best-effort, so a headless host that cannot launch one still serves.</summary>
    /// <param name="url">The address to open.</param>
    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch(Exception)
        {
            //Launching a browser is a convenience; the address is printed regardless, so a failure is non-fatal.
        }
    }

    /// <summary>Renders a one-field <c>{"error":…}</c> document — the shape the Studio's HTTP transport parses for a failed query — with the message JSON-escaped.</summary>
    /// <param name="message">The error message.</param>
    /// <returns>The JSON error document.</returns>
    private static string ErrorJson(string message)
    {
        StringBuilder builder = new();
        builder.Append("{\"error\":\"");
        AppendJsonEscaped(builder, message);
        builder.Append("\"}");

        return builder.ToString();
    }

    /// <summary>Appends <paramref name="text"/> JSON-string-escaped to <paramref name="builderToAppendTo"/>.</summary>
    /// <param name="builderToAppendTo">The builder the escaped text is appended to.</param>
    /// <param name="text">The text to escape.</param>
    private static void AppendJsonEscaped(StringBuilder builderToAppendTo, string text)
    {
        foreach(char character in text)
        {
            AppendJsonEscaped(builderToAppendTo, character);
        }
    }

    /// <summary>Appends one character JSON-string-escaped to <paramref name="builderToAppendTo"/>: the named escapes for the forms JSON spells that way, the six-character form for the remaining control characters, and the character itself otherwise.</summary>
    /// <param name="builderToAppendTo">The builder the escaped character is appended to.</param>
    /// <param name="character">The character to escape.</param>
    private static void AppendJsonEscaped(StringBuilder builderToAppendTo, char character)
    {
        builderToAppendTo.Append(character switch
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
}
