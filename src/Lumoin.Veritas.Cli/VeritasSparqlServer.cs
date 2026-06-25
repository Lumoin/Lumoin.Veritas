using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Sparql.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lumoin.Veritas.Cli;

/// <summary>
/// A minimal SPARQL 1.1 Protocol query endpoint (Kestrel) over a dataset loaded once at startup.
/// This is the HTTP face of the engine — the inbound counterpart to the library's outbound
/// <c>SparqlServiceTransport</c> seam — letting Veritas answer queries like a triplestore. The
/// library itself stays HTTP-free; the dependency on Kestrel lives only in this application.
/// </summary>
/// <remarks>
/// The endpoint accepts <c>GET /sparql?query=…</c>, a <c>POST</c> with an
/// <c>application/sparql-query</c> body, or a form-encoded <c>POST</c> with a <c>query</c> field,
/// and renders <c>SELECT</c>/<c>ASK</c> results as CSV (or TSV when the <c>Accept</c> header asks
/// for <c>text/tab-separated-values</c>). Authorization is not yet enforced; every public surface
/// is the place the opaque <c>AccessContext</c> / <c>AccessControlDelegate</c> seam threads in,
/// and the default here is allow-all until that is wired.
/// </remarks>
internal static class VeritasSparqlServer
{
    /// <summary>Loads the dataset, starts the endpoint, and runs until cancelled.</summary>
    /// <param name="dataPaths">The RDF data document paths forming the served dataset.</param>
    /// <param name="port">The loopback port to listen on.</param>
    /// <param name="cancellationToken">A token that stops the server.</param>
    /// <returns>The process exit code (0 on a clean stop, 1 when the dataset could not be loaded).</returns>
    public static async Task<int> RunAsync(IReadOnlyList<string> dataPaths, int port, CancellationToken cancellationToken)
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
        //shutdown, which drains any in-flight build before the process exits.
        (VeritasEngine? database, string? error) = await VeritasOperations.OpenDatabaseAsync(dataPaths, cancellationToken).ConfigureAwait(false);
        if(error is not null)
        {
            await Console.Error.WriteLineAsync(error).ConfigureAwait(false);

            return 1;
        }

        await using var databaseScope = database!.ConfigureAwait(false);

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        WebApplication app = builder.Build();
        app.Urls.Add($"http://localhost:{port}");

        SparqlRequestHandler handler = new(database!, cancellationToken);
        app.MapGet("/sparql", handler.HandleGet);
        app.MapPost("/sparql", handler.HandlePost);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        //After binding, the server features carry the resolved address (the real port when 0 was requested);
        //print it so a caller — including a test harness — can reach the endpoint.
        ICollection<string>? addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        if(addresses is not null)
        {
            foreach(string address in addresses)
            {
                await Console.Out.WriteLineAsync($"Veritas SPARQL endpoint listening on {address}/sparql ({dataPaths.Count} data file(s)).").ConfigureAwait(false);
            }
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
    private sealed class SparqlRequestHandler(VeritasEngine database, CancellationToken cancellationToken)
    {
        /// <summary>The open database the requests query.</summary>
        private VeritasEngine Database { get; } = database;

        /// <summary>The server-lifetime token that aborts in-flight requests on shutdown.</summary>
        private CancellationToken CancellationToken { get; } = cancellationToken;

        /// <summary>Answers a GET request, reading the query from the <c>query</c> query-string parameter.</summary>
        /// <param name="httpContext">The request context.</param>
        /// <returns>The asynchronous handling.</returns>
        public Task HandleGet(HttpContext httpContext)
        {
            return HandleAsync(httpContext, Database, ToQuery(httpContext.Request.Query["query"]), CancellationToken);
        }

        /// <summary>Answers a POST request, reading the query from the request body or a form field.</summary>
        /// <param name="httpContext">The request context.</param>
        /// <returns>The asynchronous handling.</returns>
        public Task HandlePost(HttpContext httpContext)
        {
            return HandlePostAsync(httpContext, Database, CancellationToken);
        }
    }

    /// <summary>Extracts the query from a POST (an <c>application/sparql-query</c> body or a form field) and answers it.</summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="database">The open database.</param>
    /// <param name="cancellationToken">A token that aborts the request.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandlePostAsync(HttpContext httpContext, VeritasEngine database, CancellationToken cancellationToken)
    {
        string contentType = httpContext.Request.ContentType ?? string.Empty;
        if(contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            await HandleAsync(httpContext, database, ToQuery(httpContext.Request.Form["query"]), cancellationToken).ConfigureAwait(false);

            return;
        }

        //An application/sparql-query body is the query's UTF-8 bytes; route them straight to the byte-native
        //parse with no intervening string, the inbound counterpart to the byte-native readers the loader uses.
        Utf8String body = await ReadBodyAsync(httpContext.Request.Body, httpContext.Request.ContentLength, cancellationToken).ConfigureAwait(false);
        await HandleAsync(httpContext, database, body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Answers one query against the database, writing CSV/TSV results or a plain-text error.</summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="database">The open database.</param>
    /// <param name="query">The query as an owned UTF-8 string (possibly empty).</param>
    /// <param name="cancellationToken">A token that aborts the request.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandleAsync(HttpContext httpContext, VeritasEngine database, Utf8String query, CancellationToken cancellationToken)
    {
        if(IsBlank(query.Memory.Span))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("Missing 'query'.", cancellationToken).ConfigureAwait(false);

            return;
        }

        string accept = httpContext.Request.Headers.Accept.ToString();
        SparqlDelimitedResultsFormat format = accept.Contains("text/tab-separated-values", StringComparison.OrdinalIgnoreCase)
            ? SparqlDelimitedResultsFormat.Tsv
            : SparqlDelimitedResultsFormat.Csv;

        OperationResult result = await VeritasOperations.ExecuteQueryAsync(database, query, baseIri: string.Empty, format, cancellationToken).ConfigureAwait(false);

        if(!result.Succeeded)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync(result.ErrorMessage!, cancellationToken).ConfigureAwait(false);

            return;
        }

        httpContext.Response.ContentType = format == SparqlDelimitedResultsFormat.Tsv ? "text/tab-separated-values" : "text/csv";
        await httpContext.Response.WriteAsync(result.Output, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Encodes a query taken from the URL query string or a form field to UTF-8 bytes; an absent value yields an empty buffer, read downstream as a missing query.</summary>
    /// <param name="query">The query string, or <see langword="null"/> when the parameter is absent.</param>
    /// <returns>The query as an owned UTF-8 string, or the empty string when there is none.</returns>
    private static Utf8String ToQuery(string? query)
    {
        return string.IsNullOrEmpty(query) ? default : Utf8String.FromString(query);
    }

    /// <summary>Reads the entire request body into an owned <see cref="Utf8String"/>, so an <c>application/sparql-query</c> POST body reaches the byte-native query entry without a string round-trip. A declared length reads once into an exactly-sized buffer; an unknown length buffers then copies once.</summary>
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
}
