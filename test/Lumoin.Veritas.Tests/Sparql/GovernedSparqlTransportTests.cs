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
        IriRef endpoint = new(Utf8String.FromString("http://example.org/sparql"), default);
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

    /// <summary>A permitted graph resolve returns the inner triples; a denial added at runtime throws without resolving.</summary>
    [TestMethod]
    public async Task GraphResolverGovernsThenResolvesOrThrows()
    {
        using VeritasMemoryPool<byte> pool = new();
        NetworkFirewall firewall = new();
        IriRef source = new(Utf8String.FromString("http://example.org/data.ttl"), default);
        IReadOnlyList<DataTriple> expected = Array.Empty<DataTriple>();

        int calls = 0;
        GraphSourceResolver inner = (src, context, token) =>
        {
            calls++;

            return new ValueTask<IReadOnlyList<DataTriple>>(expected);
        };
        GovernedGraphSourceResolver governed = new(inner, firewall.Decide, pool, TimeProvider.System);

        IReadOnlyList<DataTriple> permitted = await governed.Resolver(source, null, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreSame(expected, permitted, "A permitted resolve returns the inner triples.");
        Assert.AreEqual(1, calls, "The inner resolver ran once on a permit.");

        firewall.Deny(NetworkPeerKeyKind.EndpointIri, source.Value.Span);

        await Assert.ThrowsExactlyAsync<NetworkGovernanceDeniedException>(() => governed.Resolver(source, null, TestContext.CancellationToken).AsTask()).ConfigureAwait(false);
        Assert.AreEqual(1, calls, "The inner resolver did not run on a deny.");
    }
}
