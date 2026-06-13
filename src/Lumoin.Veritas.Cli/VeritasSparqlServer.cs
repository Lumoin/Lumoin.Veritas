using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Execution;
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
        //minimum so a burst of synchronous CPU work cannot starve response continuations. The
        //floor is reported the moment it applies — before the dataset loads or the host binds —
        //so an operator (and the integration test) observes it as the first line of output.
        int workerThreadFloor = HostThreadPoolFloor.Apply();
        await Console.Out.WriteLineAsync($"Worker-thread minimum floored at {workerThreadFloor}.").ConfigureAwait(false);

        SparqlExpressionContext context = SparqlExpressionContext.CreateDefault();
        (SparqlQueryEngine? engine, string? error) = await VeritasOperations.BuildEngineAsync(dataPaths, context, cancellationToken).ConfigureAwait(false);
        if(error is not null)
        {
            await Console.Error.WriteLineAsync(error).ConfigureAwait(false);

            return 1;
        }

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        WebApplication app = builder.Build();
        app.Urls.Add($"http://localhost:{port}");

        app.MapGet("/sparql", (HttpContext httpContext) => HandleAsync(httpContext, engine!, httpContext.Request.Query["query"], cancellationToken));
        app.MapPost("/sparql", (HttpContext httpContext) => HandlePostAsync(httpContext, engine!, cancellationToken));

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

    /// <summary>Extracts the query from a POST (an <c>application/sparql-query</c> body or a form field) and answers it.</summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="engine">The dataset engine.</param>
    /// <param name="cancellationToken">A token that aborts the request.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandlePostAsync(HttpContext httpContext, SparqlQueryEngine engine, CancellationToken cancellationToken)
    {
        string? query;
        string contentType = httpContext.Request.ContentType ?? string.Empty;
        if(contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            query = httpContext.Request.Form["query"];
        }
        else
        {
            using StreamReader reader = new(httpContext.Request.Body);
            query = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        await HandleAsync(httpContext, engine, query, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Answers one query against the dataset, writing CSV/TSV results or a plain-text error.</summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="engine">The dataset engine.</param>
    /// <param name="query">The query text (possibly empty).</param>
    /// <param name="cancellationToken">A token that aborts the request.</param>
    /// <returns>The asynchronous handling.</returns>
    private static async Task HandleAsync(HttpContext httpContext, SparqlQueryEngine engine, string? query, CancellationToken cancellationToken)
    {
        if(string.IsNullOrWhiteSpace(query))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync("Missing 'query'.", cancellationToken).ConfigureAwait(false);

            return;
        }

        string accept = httpContext.Request.Headers.Accept.ToString();
        SparqlDelimitedResultsFormat format = accept.Contains("text/tab-separated-values", StringComparison.OrdinalIgnoreCase)
            ? SparqlDelimitedResultsFormat.Tsv
            : SparqlDelimitedResultsFormat.Csv;

        using Utf8StringPool pool = new();
        OperationResult result = await VeritasOperations.ExecuteQueryAsync(engine, pool, query, baseIri: string.Empty, format, cancellationToken).ConfigureAwait(false);

        if(!result.Succeeded)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsync(result.ErrorMessage!, cancellationToken).ConfigureAwait(false);

            return;
        }

        httpContext.Response.ContentType = format == SparqlDelimitedResultsFormat.Tsv ? "text/tab-separated-values" : "text/csv";
        await httpContext.Response.WriteAsync(result.Output, cancellationToken).ConfigureAwait(false);
    }
}
