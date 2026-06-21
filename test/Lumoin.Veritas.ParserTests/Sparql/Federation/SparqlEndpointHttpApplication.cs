using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Sparql.Results;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Lumoin.Veritas.Rdf.Json;

namespace Lumoin.Veritas.ParserTests.Sparql.Federation;

/// <summary>
/// A minimal SPARQL Protocol endpoint over raw Kestrel: it reads the query (POST <c>application/sparql-query</c>
/// body, POST form <c>query=</c>, or GET <c>?query=</c>), evaluates it against the hosted
/// <see cref="SparqlTestEndpoint"/>, and writes the result set as SPARQL Results JSON. This is the wire side of
/// the federation harness; the matching <see cref="SparqlTestHostShell.HttpTransport"/> POSTs to it.
/// </summary>
internal sealed class SparqlEndpointHttpApplication : IHttpApplication<HttpContext>
{
    private readonly SparqlTestEndpoint endpoint;

    //The shell's transport timeline; every request marks its entry, its
    //evaluation, its response write, and any failure, so a stalled round-trip
    //shows whether the request ever reached the server.
    private readonly TransportTraceRecorder trace;

    /// <summary>Initialises the application over the endpoint it serves.</summary>
    /// <param name="endpoint">The endpoint whose graph this application queries.</param>
    /// <param name="trace">The shell's transport timeline recorder.</param>
    public SparqlEndpointHttpApplication(SparqlTestEndpoint endpoint, TransportTraceRecorder trace)
    {
        this.endpoint = endpoint;
        this.trace = trace;
    }

    /// <summary>Creates the per-request context from Kestrel's feature collection.</summary>
    /// <param name="contextFeatures">The request's features.</param>
    /// <returns>The request context.</returns>
    public HttpContext CreateContext(IFeatureCollection contextFeatures)
    {
        return new DefaultHttpContext(contextFeatures);
    }

    /// <summary>Reads the query, evaluates it, and writes the result set as SPARQL Results JSON.</summary>
    /// <param name="context">The request context.</param>
    /// <returns>A task that completes when the response is written.</returns>
    public async Task ProcessRequestAsync(HttpContext context)
    {
        trace($"server {endpoint.Name}: request entered");
        try
        {
            string query = await ReadQueryAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
            trace($"server {endpoint.Name}: query read ({query.Length} chars)");
            SparqlResultSet results = await endpoint.ExecuteAsync(query, context.RequestAborted).ConfigureAwait(false);
            trace($"server {endpoint.Name}: evaluated");

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/sparql-results+json";

            Utf8String json = SparqlResultsJsonWriter.WriteToUtf8String(results, indented: false);
            await context.Response.Body.WriteAsync(json.Memory, context.RequestAborted).ConfigureAwait(false);
            trace($"server {endpoint.Name}: response written");
        }
        catch(Exception exception)
        {
            trace($"server {endpoint.Name}: FAILED with {exception.GetType().Name}: {exception.Message}");

            throw;
        }
    }

    /// <summary>Releases the per-request context (no per-request state is held).</summary>
    /// <param name="context">The request context.</param>
    /// <param name="exception">The exception that ended the request, if any.</param>
    public void DisposeContext(HttpContext context, Exception? exception)
    {
    }

    /// <summary>Extracts the SPARQL query from a Protocol request: a <c>query</c> query-string parameter, a form field, or the raw request body.</summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="cancellationToken">A token that aborts reading.</param>
    /// <returns>The query text.</returns>
    private static async ValueTask<string> ReadQueryAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if(request.Query.TryGetValue("query", out StringValues fromQueryString) && fromQueryString.Count > 0)
        {
            return fromQueryString.ToString();
        }

        string contentType = request.ContentType ?? string.Empty;
        if(contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            IFormCollection form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);

            return form.TryGetValue("query", out StringValues fromForm) ? fromForm.ToString() : string.Empty;
        }

        using StreamReader reader = new(request.Body, Encoding.UTF8);

        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
