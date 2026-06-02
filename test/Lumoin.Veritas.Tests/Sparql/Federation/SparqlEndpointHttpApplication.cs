using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Sparql.Results;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Lumoin.Veritas.Rdf.Json;

namespace Lumoin.Veritas.Tests.Sparql.Federation;

/// <summary>
/// A minimal SPARQL Protocol endpoint over raw Kestrel: it reads the query (POST <c>application/sparql-query</c>
/// body, POST form <c>query=</c>, or GET <c>?query=</c>), evaluates it against the hosted
/// <see cref="SparqlTestEndpoint"/>, and writes the result set as SPARQL Results JSON. This is the wire side of
/// the federation harness; the matching <see cref="SparqlTestHostShell.HttpTransport"/> POSTs to it.
/// </summary>
internal sealed class SparqlEndpointHttpApplication : IHttpApplication<HttpContext>
{
    private readonly SparqlTestEndpoint endpoint;

    /// <summary>Initialises the application over the endpoint it serves.</summary>
    /// <param name="endpoint">The endpoint whose graph this application queries.</param>
    public SparqlEndpointHttpApplication(SparqlTestEndpoint endpoint)
    {
        this.endpoint = endpoint;
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
        string query = await ReadQueryAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
        SparqlResultSet results = await endpoint.ExecuteAsync(query, context.RequestAborted).ConfigureAwait(false);

        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/sparql-results+json";

        using MemoryStream buffer = new();
        SparqlResultsJsonWriter.Write(results, buffer, indented: false);
        await context.Response.Body.WriteAsync(buffer.ToArray(), context.RequestAborted).ConfigureAwait(false);
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
