using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Network;
using Lumoin.Veritas.Replication;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The whole active-active stack composed over the real SketchChannel framing: the reconcile loop drives a fetch
/// that, each round, opens a fresh duplex pipe connection to a peer's SketchChannelServer and pulls its sketch
/// through a SketchChannelClient, governed by the live control surface. Two diverged replicas converge to their
/// union over the wire; a firewall denial blocks reconciliation entirely until the peer is allowed, then it
/// converges. In-process over pipes (no socket), deterministic, no wall-clock waits.
/// </summary>
[TestClass]
internal sealed class ActiveActiveChannelTests
{
    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A per-round SketchChannel fetch to a fixed in-process peer index: each call opens a fresh duplex pipe pair, serves the peer's sketch on one side, and pulls it on the other — the one-connection-per-fetch shape the transport requires.</summary>
    /// <param name="peer">The peer replica whose sketch is served.</param>
    /// <param name="pool">The pool the server's transient buffers are rented from.</param>
    private sealed class InProcessChannelPeer(ColumnarTripleIndex peer, MemoryPool<byte> pool)
    {
        /// <summary>The served peer replica.</summary>
        private ColumnarTripleIndex Peer { get; } = peer;

        /// <summary>The buffer pool for the served sketch.</summary>
        private MemoryPool<byte> Pool { get; } = pool;

        /// <summary>Opens a fresh connection, serves and fetches one sketch, and returns it — an <see cref="AsyncSketchFetchDelegate"/>.</summary>
        /// <param name="symbolBudget">The fetch's symbol budget.</param>
        /// <param name="cancellationToken">The token that cancels the fetch.</param>
        /// <returns>The peer's sketch image.</returns>
        public async ValueTask<ReadOnlyMemory<byte>> FetchAsync(int symbolBudget, CancellationToken cancellationToken)
        {
            Pipe requestPipe = new();
            Pipe responsePipe = new();
            SketchChannelServer server = new(Peer, Pool, requestPipe.Reader, responsePipe.Writer);
            Task serve = server.ServeAsync(cancellationToken);
            SketchChannelClient client = new(requestPipe.Writer, responsePipe.Reader);

            ReadOnlyMemory<byte> image = await client.FetchAsync(symbolBudget, cancellationToken).ConfigureAwait(false);
            await serve.ConfigureAwait(false);

            return image;
        }
    }

    /// <summary>A line of triples with a shared predicate: subjects <c>[start, start + count)</c>, each linked to the next identifier.</summary>
    /// <param name="start">The first subject identifier.</param>
    /// <param name="count">The number of triples.</param>
    /// <returns>The triples.</returns>
    private static EncodedTriple[] Line(uint start, uint count)
    {
        EncodedTriple[] triples = new EncodedTriple[count];
        for(uint i = 0; i < count; i++)
        {
            uint subject = start + i;
            triples[i] = EncodedTriple.FromEncoded(subject, 10, subject + 1);
        }

        return triples;
    }

    /// <summary>Asserts the index holds exactly the union of both replicas' triples.</summary>
    /// <param name="index">The index to check.</param>
    /// <param name="left">The first replica's triples.</param>
    /// <param name="right">The second replica's triples.</param>
    private static void AssertUnion(ColumnarTripleIndex index, EncodedTriple[] left, EncodedTriple[] right)
    {
        HashSet<EncodedTriple> union = [.. left];
        union.UnionWith(right);
        HashSet<EncodedTriple> converged = [.. index.EnumerateTriples()];
        Assert.IsTrue(union.SetEquals(converged), "The index must hold the union of both replicas.");
    }

    /// <summary>Two diverged replicas converge to their union over the real SketchChannel framing, driven by the reconcile loop.</summary>
    [TestMethod]
    public async Task ConvergesToUnionOverTheChannel()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex local = ColumnarTripleIndex.Build(triplesA);
        InProcessChannelPeer peer = new(ColumnarTripleIndex.Build(triplesB), pool);

        ReplicaReconcileResult result = await ReplicaReconcileLoop.RunUntilConvergedAsync(local, peer.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, maxRounds: 3, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(result.Converged, "The replica converges over the channel.");
        AssertUnion(result.Index, triplesA, triplesB);
    }

    /// <summary>A firewall denial blocks reconciliation over the channel — the loop never converges and the index is untouched — and once the peer is allowed, a fresh loop converges.</summary>
    [TestMethod]
    public async Task FirewallBlocksTheChannelThenConvergesOnceAllowed()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex local = ColumnarTripleIndex.Build(triplesA);
        InProcessChannelPeer peer = new(ColumnarTripleIndex.Build(triplesB), pool);
        byte[] peerId = [7];

        NetworkGovernanceController controller = new();
        NetworkPeerKey peerKey = NetworkPeerKey.RentReplicaId(pool, peerId);
        using GovernedSketchFetch governed = new(peer.FetchAsync, controller.Decide, peerKey, null, TimeProvider.System);

        controller.Deny(NetworkPeerKeyKind.ReplicaId, peerId);
        ReplicaReconcileResult blocked = await ReplicaReconcileLoop.RunUntilConvergedAsync(local, governed.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, maxRounds: 3, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(blocked.Converged, "A firewalled peer never converges.");
        Assert.AreEqual(AntiEntropyOutcome.PeerUnavailable, blocked.LastOutcome);
        Assert.AreSame(local, blocked.Index, "A blocked loop leaves the local index unchanged.");

        controller.Allow(NetworkPeerKeyKind.ReplicaId, peerId);
        ReplicaReconcileResult allowed = await ReplicaReconcileLoop.RunUntilConvergedAsync(blocked.Index, governed.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, maxRounds: 3, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(allowed.Converged, "Once allowed, the replica converges over the channel.");
        AssertUnion(allowed.Index, triplesA, triplesB);
    }
}
