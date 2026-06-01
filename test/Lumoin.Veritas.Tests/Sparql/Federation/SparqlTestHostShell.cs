using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lumoin.Veritas.Tests.Sparql.Federation;

/// <summary>
/// A registry of named SPARQL endpoints, each exposed over an ephemeral loopback Kestrel server, for testing
/// federated (<c>SERVICE</c>) queries. It offers two <see cref="SparqlServiceTransport"/> implementations over
/// the same endpoints — one in-process (the query runs straight against the target engine) and one over real
/// HTTP (the query is POSTed to the endpoint's Kestrel server and the SPARQL Results JSON response is parsed
/// back) — so a federation test can run the identical query under both and assert they agree. This mirrors the
/// sibling project's OAuth <c>TestHostShell</c>; the library itself never references HTTP.
/// </summary>
internal sealed class SparqlTestHostShell : IAsyncDisposable
{
    /// <summary>The registered endpoints, keyed by logical name.</summary>
    private Dictionary<string, SparqlTestEndpoint> Endpoints { get; } = [];

    /// <summary>The client used to POST to an IRI that is not a registered endpoint (so an unreachable endpoint produces a real connection failure).</summary>
    private HttpClient FallbackClient { get; } = new();

    /// <summary>Registers an endpoint over the given graph.</summary>
    /// <param name="name">The endpoint's logical name.</param>
    /// <param name="data">The endpoint's graph.</param>
    /// <param name="cancellationToken">A token that aborts the engine build.</param>
    /// <returns>The registered endpoint.</returns>
    public async ValueTask<SparqlTestEndpoint> AddEndpointAsync(string name, IEnumerable<DataTriple> data, CancellationToken cancellationToken)
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(data, cancellationToken: cancellationToken).ConfigureAwait(false);
        SparqlTestEndpoint endpoint = new(name, engine);
        Endpoints[name] = endpoint;

        return endpoint;
    }

    /// <summary>Starts an ephemeral loopback Kestrel server for every endpoint that is not already started, assigning each a base address and HTTP client.</summary>
    /// <param name="cancellationToken">A token that aborts the start.</param>
    /// <returns>A task that completes when every endpoint is listening.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach(SparqlTestEndpoint endpoint in Endpoints.Values)
        {
            if(endpoint.Server is not null)
            {
                continue;
            }

            //Port 0 lets the OS assign a free port; loopback IPv4 only. The bound address (with the real port)
            //is read back from the server-addresses feature after the server starts. The server is assigned
            //straight onto the endpoint (its owner), which DisposeAsync tears down even if a later start fails.
            KestrelServerOptions options = new();
            options.Listen(IPAddress.Loopback, port: 0);
            SocketTransportFactory transportFactory = new(Options.Create(new SocketTransportOptions()), NullLoggerFactory.Instance);
            endpoint.Server = new KestrelServer(Options.Create(options), transportFactory, NullLoggerFactory.Instance);
            await endpoint.Server.StartAsync(new SparqlEndpointHttpApplication(endpoint), cancellationToken).ConfigureAwait(false);

            IServerAddressesFeature addresses = endpoint.Server.Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("Kestrel did not expose a server-addresses feature.");
            endpoint.BaseAddress = new Uri(addresses.Addresses.First());
            endpoint.Client = new HttpClient { BaseAddress = endpoint.BaseAddress };
        }
    }

    /// <summary>The IRI a federated query uses to reach a started endpoint (its loopback base address).</summary>
    /// <param name="name">The endpoint's logical name.</param>
    /// <returns>The endpoint's base-address IRI.</returns>
    public IriRef EndpointIri(string name)
    {
        Uri baseAddress = Endpoints[name].BaseAddress
            ?? throw new InvalidOperationException($"Endpoint '{name}' has no base address; call StartAsync first.");

        return new IriRef(Utf8String.FromString(baseAddress.AbsoluteUri), default);
    }

    /// <summary>A transport that evaluates the query in-process against the target endpoint's engine (no sockets).</summary>
    public SparqlServiceTransport InProcessTransport
    {
        get
        {
            return async (endpoint, query, accessContext, cancellationToken) =>
            {
                SparqlTestEndpoint target = TryResolve(endpoint)
                    ?? throw new InvalidOperationException($"No test endpoint is registered at '{endpoint.Value}'.");

                return await target.ExecuteAsync(query, cancellationToken).ConfigureAwait(false);
            };
        }
    }

    /// <summary>A transport that POSTs the query to the target endpoint's Kestrel server and parses the SPARQL Results JSON response.</summary>
    public SparqlServiceTransport HttpTransport
    {
        get
        {
            return async (endpoint, query, accessContext, cancellationToken) =>
            {
                //A registered endpoint uses its own client + base address; any other IRI is POSTed verbatim via the
                //fallback client, so an unreachable endpoint surfaces as a real connection failure (which SILENT can absorb).
                SparqlTestEndpoint? target = TryResolve(endpoint);
                HttpClient client = target?.Client ?? FallbackClient;
                Uri uri = target?.BaseAddress ?? new Uri(endpoint.Value.ToString());

                using HttpRequestMessage request = new(HttpMethod.Post, uri)
                {
                    Content = new StringContent(query, Encoding.UTF8)
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("application/sparql-query") }
                    }
                };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/sparql-results+json"));

                using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

                return SparqlResultsJsonReader.Read(body);
            };
        }
    }

    /// <summary>Finds the endpoint registered at the given base-address IRI, or <see langword="null"/> if none is.</summary>
    /// <param name="endpoint">The endpoint IRI from the query.</param>
    /// <returns>The matching endpoint, or <see langword="null"/>.</returns>
    private SparqlTestEndpoint? TryResolve(IriRef endpoint)
    {
        string iri = endpoint.Value.ToString();
        foreach(SparqlTestEndpoint candidate in Endpoints.Values)
        {
            if(candidate.BaseAddress is not null && string.Equals(candidate.BaseAddress.AbsoluteUri, iri, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Stops every endpoint's Kestrel server and disposes its HTTP client.</summary>
    /// <returns>A task that completes when every endpoint is torn down.</returns>
    public async ValueTask DisposeAsync()
    {
        foreach(SparqlTestEndpoint endpoint in Endpoints.Values)
        {
            endpoint.Client?.Dispose();
            if(endpoint.Server is not null)
            {
                await endpoint.Server.StopAsync(CancellationToken.None).ConfigureAwait(false);
                endpoint.Server.Dispose();
            }
        }

        FallbackClient.Dispose();
    }
}
