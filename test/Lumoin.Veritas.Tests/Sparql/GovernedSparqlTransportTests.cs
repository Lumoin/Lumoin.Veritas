using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Network;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Tests.Sparql;

/// <summary>
/// The governed federation transports: a permitted SERVICE query or graph resolve runs the inner transport and
/// returns its result, while a denied one throws <see cref="NetworkGovernanceDeniedException"/> without running the
/// inner transport — the throw the engine's SILENT handling maps to the same outcome as an unreachable endpoint.
/// </summary>
[TestClass]
internal sealed class GovernedSparqlTransportTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A permitted SERVICE query returns the inner result; a denial added at runtime throws without querying.</summary>
    [TestMethod]
    public async Task ServiceTransportGovernsThenQueriesOrThrows()
    {
        using VeritasMemoryPool<byte> pool = new();
        NetworkFirewall firewall = new();
        IriRef endpoint = new(Utf8Strings.From("http://example.org/sparql"), default);
        SparqlResultSet expected = SparqlResultSet.ForAsk(true);

        int calls = 0;
        SparqlServiceTransport inner = (ep, query, context, token) =>
        {
            calls++;

            return new ValueTask<SparqlResultSet>(expected);
        };
        GovernedSparqlServiceTransport governed = new(inner, firewall.Decide, pool, TimeProvider.System);

        SparqlResultSet permitted = await governed.Transport(endpoint, "ASK {}", null, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreSame(expected, permitted, "A permitted query returns the inner result.");
        Assert.AreEqual(1, calls, "The inner transport ran once on a permit.");

        firewall.Deny(NetworkPeerKeyKind.EndpointIri, endpoint.Value.Span);

        await Assert.ThrowsExactlyAsync<NetworkGovernanceDeniedException>(() => governed.Transport(endpoint, "ASK {}", null, TestContext.CancellationToken).AsTask()).ConfigureAwait(false);
        Assert.AreEqual(1, calls, "The inner transport did not run on a deny.");
    }

    /// <summary>A permitted graph resolve streams the inner triples; a denial added at runtime throws on the first enumeration step, before the inner resolver is invoked.</summary>
    [TestMethod]
    public async Task GraphResolverGovernsThenResolvesOrThrows()
    {
        using VeritasMemoryPool<byte> pool = new();
        NetworkFirewall firewall = new();
        IriRef source = new(Utf8Strings.From("http://example.org/data.ttl"), default);
        DataTriple[] expected = Array.Empty<DataTriple>();

        int calls = 0;
        GraphSourceResolver inner = (src, context, token) =>
        {
            calls++;

            return Stream(expected);
        };
        GovernedGraphSourceResolver governed = new(inner, firewall.Decide, pool, TimeProvider.System);

        List<DataTriple> permitted = [];
        await foreach(DataTriple triple in governed.Resolver(source, null, TestContext.CancellationToken).ConfigureAwait(false))
        {
            permitted.Add(triple);
        }

        Assert.HasCount(expected.Length, permitted, "A permitted resolve streams the inner triples.");
        Assert.AreEqual(1, calls, "The inner resolver ran once on a permit.");

        firewall.Deny(NetworkPeerKeyKind.EndpointIri, source.Value.Span);

        await Assert.ThrowsExactlyAsync<NetworkGovernanceDeniedException>(async () =>
        {
            await foreach(DataTriple _ in governed.Resolver(source, null, TestContext.CancellationToken).ConfigureAwait(false))
            {
            }
        }).ConfigureAwait(false);
        Assert.AreEqual(1, calls, "The inner resolver did not run on a deny.");

        static async IAsyncEnumerable<DataTriple> Stream(IReadOnlyList<DataTriple> triples)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            foreach(DataTriple triple in triples)
            {
                yield return triple;
            }
        }
    }
}
