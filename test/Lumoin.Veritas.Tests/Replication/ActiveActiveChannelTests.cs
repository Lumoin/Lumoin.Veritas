using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Hypertrie.Storage;
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
    /// <summary>The shared structural dictionary epoch both endpoints stamp in these tests, so a faithful peer's epoch always matches.</summary>
    private const ulong DictionaryEpoch = 7;

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A per-round SketchChannel fetch to a fixed in-process peer index: each call opens a fresh duplex pipe pair, serves the peer's sketch on one side, and pulls it on the other — the one-connection-per-fetch shape the transport requires.</summary>
    /// <param name="peer">The peer replica whose sketch is served.</param>
    /// <param name="pool">The pool the server's transient buffers are rented from.</param>
    private sealed class InProcessChannelPeer(ColumnarTripleIndex peer, MemoryPool<byte> pool): IDisposable
    {
        /// <summary>The buffer pool for the served sketch.</summary>
        private MemoryPool<byte> Pool { get; } = pool;

        /// <summary>The maintained encoder seeded over the peer replica's triples that each fetch serves its sketch from — one maintainer per peer replica, reused across every connection this instance opens.</summary>
        private IncrementalSketchMaintainer Maintainer { get; } = new(new ReplicationIndexFeed(peer.EnumerateTriples(), default), pool, IncrementalSketchMaintainerOptions.Default, DictionaryEpoch);

        /// <summary>Opens a fresh connection, serves and fetches one sketch, and returns it — an <see cref="AsyncSketchFetchDelegate"/>. Ownership of the image flows to the reconcile session, which disposes it.</summary>
        /// <param name="symbolBudget">The fetch's symbol budget.</param>
        /// <param name="cancellationToken">The token that cancels the fetch.</param>
        /// <returns>The peer's owned sketch image.</returns>
        public async ValueTask<SketchFetchResult> FetchAsync(int symbolBudget, CancellationToken cancellationToken)
        {
            Pipe requestPipe = new();
            Pipe responsePipe = new();
            SketchChannelServer server = new(Maintainer, Pool, requestPipe.Reader, responsePipe.Writer, DictionaryEpoch);
            Task serve = server.ServeAsync(cancellationToken);
            SketchChannelClient client = new(requestPipe.Writer, responsePipe.Reader, Pool, SketchChannelDomain.Structural, DictionaryEpoch);

            SketchFetchResult image = await client.FetchAsync(symbolBudget, cancellationToken).ConfigureAwait(false);
            await serve.ConfigureAwait(false);

            return image;
        }

        /// <summary>Advances the served replica by one committed delta — the live-write seam an active-active peer folds its own writes (or a converged pull's recovered additions) through, so every later fetch serves the advanced set.</summary>
        /// <param name="additions">The triples the commit added; each must be absent from the served set.</param>
        /// <param name="removals">The triples the commit removed; each must be present in the served set.</param>
        /// <param name="stateId">The dataset StateId the commit produced.</param>
        public void Advance(IReadOnlyCollection<EncodedTriple> additions, IReadOnlyCollection<EncodedTriple> removals, NodeIdentifier stateId)
        {
            Maintainer.OnDefaultGraphDelta(additions, removals, stateId, causality: null);
        }

        /// <summary>Disposes the maintained encoder built over the peer replica's triples.</summary>
        public void Dispose()
        {
            Maintainer.Dispose();
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
        using InProcessChannelPeer peer = new(ColumnarTripleIndex.Build(triplesB), pool);

        ReplicaReconcileResult result = await ReplicaReconcileLoop.RunUntilConvergedAsync(local, DictionaryEpoch, peer.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, maxRounds: 3, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

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
        using InProcessChannelPeer peer = new(ColumnarTripleIndex.Build(triplesB), pool);
        byte[] peerId = [7];

        NetworkGovernanceController controller = new();
        NetworkPeerKey peerKey = NetworkPeerKey.RentReplicaId(pool, peerId);
        using GovernedSketchFetch governed = new(peer.FetchAsync, controller.Decide, peerKey, null, TimeProvider.System);

        controller.Deny(NetworkPeerKeyKind.ReplicaId, peerId);
        ReplicaReconcileResult blocked = await ReplicaReconcileLoop.RunUntilConvergedAsync(local, DictionaryEpoch, governed.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, maxRounds: 3, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(blocked.Converged, "A firewalled peer never converges.");
        Assert.AreEqual(AntiEntropyOutcome.PeerUnavailable, blocked.LastOutcome);
        Assert.AreSame(local, blocked.Index, "A blocked loop leaves the local index unchanged.");

        controller.Allow(NetworkPeerKeyKind.ReplicaId, peerId);
        ReplicaReconcileResult allowed = await ReplicaReconcileLoop.RunUntilConvergedAsync(blocked.Index, DictionaryEpoch, governed.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, maxRounds: 3, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(allowed.Converged, "Once allowed, the replica converges over the channel.");
        AssertUnion(allowed.Index, triplesA, triplesB);
    }

    /// <summary>
    /// A per-round fetch that lands a peer-side write mid-flight: the FIRST fetch advances the peer's served
    /// replica by the configured racing additions BEFORE serving, so the round's peer sketch includes a write the
    /// local sketch — persisted before the fetch was awaited — predates. The injection state travels as instance
    /// fields and <see cref="FetchAsync"/> binds as a method group, so the seam carries no closure.
    /// </summary>
    /// <param name="peer">The peer whose serve the racing write lands in.</param>
    /// <param name="peerRacingWrites">The additions the first fetch folds into the peer before serving.</param>
    private sealed class RacingWritesFetch(InProcessChannelPeer peer, EncodedTriple[] peerRacingWrites)
    {
        /// <summary>The peer whose serve the racing write lands in.</summary>
        private InProcessChannelPeer Peer { get; } = peer;

        /// <summary>The additions the first fetch folds into the peer before serving.</summary>
        private EncodedTriple[] PeerRacingWrites { get; } = peerRacingWrites;

        /// <summary>Whether the racing write has landed; the injection fires exactly once, on the first fetch.</summary>
        public bool Injected { get; private set; }

        /// <summary>Serves one fetch, landing the racing write first on the initial invocation.</summary>
        /// <param name="symbolBudget">The fetch's symbol budget.</param>
        /// <param name="cancellationToken">The token that cancels the fetch.</param>
        /// <returns>The peer's owned sketch image, reflecting the racing write from the first fetch onward.</returns>
        public async ValueTask<SketchFetchResult> FetchAsync(int symbolBudget, CancellationToken cancellationToken)
        {
            if(!Injected)
            {
                Injected = true;
                Peer.Advance(PeerRacingWrites, [], new NodeIdentifier(97));
            }

            return await Peer.FetchAsync(symbolBudget, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The both-directions active-active row: A pulls from B and converges; A's recovered additions advance A's OWN
    /// serving replica, so A's next serve carries the union; B then pulls from A and converges in its own round.
    /// Both endpoints end holding the identical union — the mechanism proof that a converged side's next serve, not
    /// merely its next index, reflects convergence. (The two-process battery is the production-wiring proof.)
    /// </summary>
    [TestMethod]
    public async Task BothDirectionsConvergeToTheSharedUnionOverTheChannel()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex indexA = ColumnarTripleIndex.Build(triplesA);
        ColumnarTripleIndex indexB = ColumnarTripleIndex.Build(triplesB);
        using InProcessChannelPeer serveA = new(indexA, pool);
        using InProcessChannelPeer serveB = new(indexB, pool);

        ReplicaReconcileResult pullByA = await ReplicaReconcileLoop.RunUntilConvergedAsync(indexA, DictionaryEpoch, serveB.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, maxRounds: 3, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(pullByA.Converged, "The A-side pull converges over the channel.");
        AssertUnion(pullByA.Index, triplesA, triplesB);

        //The recovered additions are the WHOLE symmetric difference (the session applies them idempotently), so
        //the fold reduces them to the net-effective delta first — the same reduction the production write-back
        //gets from the dataset edit session, whose observer only ever sees effective additions. Folding an
        //already-held item would XOR-cancel it out of the served encoder.
        HashSet<EncodedTriple> alreadyHeldByA = [.. triplesA];
        List<EncodedTriple> newlyHeldByA = [];
        foreach(EncodedTriple recovered in pullByA.RecoveredAdditions.ToArray())
        {
            if(!alreadyHeldByA.Contains(recovered))
            {
                newlyHeldByA.Add(recovered);
            }
        }

        serveA.Advance(newlyHeldByA, [], new NodeIdentifier(1));

        ReplicaReconcileResult pullByB = await ReplicaReconcileLoop.RunUntilConvergedAsync(indexB, DictionaryEpoch, serveA.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, maxRounds: 3, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(pullByB.Converged, "The B-side pull converges against A's advanced serve.");
        AssertUnion(pullByB.Index, triplesA, triplesB);

        HashSet<EncodedTriple> heldByA = [.. pullByA.Index.EnumerateTriples()];
        HashSet<EncodedTriple> heldByB = [.. pullByB.Index.EnumerateTriples()];
        Assert.IsTrue(heldByA.SetEquals(heldByB), "Both endpoints hold the identical union after the two pulls.");
    }

    /// <summary>
    /// The concurrent-write race row: writes land on BOTH replicas while reconciliation is in flight — the peer's
    /// write lands inside the first fetch (after the local sketch was persisted, so that round reconciles against a
    /// moved peer), and the local write lands between the pulls — and both endpoints still converge to the full
    /// union including every racing write. Rounds are driven through the public session per pull; injection points
    /// are fixed, so the row is deterministic.
    /// </summary>
    [TestMethod]
    public async Task ConcurrentWritesRacingTheReconcileConvergeToTheFullUnion()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        EncodedTriple[] peerRacing = Line(300, 10);
        EncodedTriple[] localRacing = Line(400, 10);
        ColumnarTripleIndex indexA = ColumnarTripleIndex.Build(triplesA);
        using InProcessChannelPeer serveB = new(ColumnarTripleIndex.Build(triplesB), pool);
        RacingWritesFetch racingFetch = new(serveB, peerRacing);

        //A's pull races B's write: the fetch lands 300..309 on B after A's round has persisted its local sketch,
        //so the round's difference already includes the racing write.
        ReplicaReconcileResult pullByA = await ReplicaReconcileLoop.RunUntilConvergedAsync(indexA, DictionaryEpoch, racingFetch.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, maxRounds: 3, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(pullByA.Converged, "The pull racing the peer write converges.");
        Assert.IsTrue(racingFetch.Injected, "The racing peer write landed inside the round.");

        //A's own racing write lands after its pull; the reverse pull must carry BOTH racing writes to B.
        ColumnarTripleIndex advancedA = pullByA.Index.Apply(localRacing, []);
        using InProcessChannelPeer serveAdvancedA = new(advancedA, pool);

        ColumnarTripleIndex indexB = ColumnarTripleIndex.Build(triplesB).Apply(peerRacing, []);
        ReplicaReconcileResult pullByB = await ReplicaReconcileLoop.RunUntilConvergedAsync(indexB, DictionaryEpoch, serveAdvancedA.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, maxRounds: 3, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(pullByB.Converged, "The reverse pull converges with both racing writes in flight.");

        HashSet<EncodedTriple> expected = [.. triplesA];
        expected.UnionWith(triplesB);
        expected.UnionWith(peerRacing);
        expected.UnionWith(localRacing);
        HashSet<EncodedTriple> heldByA = [.. advancedA.EnumerateTriples()];
        HashSet<EncodedTriple> heldByB = [.. pullByB.Index.EnumerateTriples()];
        Assert.IsTrue(heldByB.SetEquals(expected), "The reverse pull's endpoint holds the full union including every racing write.");
        Assert.IsTrue(heldByA.SetEquals(expected), "The first endpoint holds the full union including every racing write.");
    }

    /// <summary>
    /// An active-active reconcile soak: many full reconcile cycles over the real SketchChannel wire, measuring the
    /// allocation pressure of the active-active hot path. The diverged pairs are built outside the measured window,
    /// so the figure is the reconcile + wire cost (the sketch image crossing the channel, the rateless decode, and
    /// the per-round union rebuild), not index construction. This is the measure-before-refactor data backlog #27
    /// asks for (the pool-aware / streaming wire layer); it also asserts convergence on every cycle, so it doubles
    /// as a sustained-load regression. The figures are written to the test output.
    /// </summary>
    [TestMethod]
    public async Task ActiveActiveReconcileSoakReportsAllocationPressure()
    {
        using VeritasMemoryPool<byte> pool = new();
        const int Cycles = 100;

        //Build the diverged pairs up front so only the reconciles fall inside the measured window. Each cycle uses
        //a disjoint id range, so the 100 reconciles are independent rather than one converging chain.
        (ColumnarTripleIndex Local, InProcessChannelPeer Peer)[] pairs = new (ColumnarTripleIndex, InProcessChannelPeer)[Cycles];
        for(int cycle = 0; cycle < Cycles; cycle++)
        {
            uint baseId = (uint)(cycle * 1000);
            pairs[cycle] = (ColumnarTripleIndex.Build(Line(baseId, 150)), new InProcessChannelPeer(ColumnarTripleIndex.Build(Line(baseId + 50, 150)), pool));
        }

        try
        {
            //Warm the JIT and first-run statics so the measured window is steady state.
            ReplicaReconcileResult warm = await ReplicaReconcileLoop.RunUntilConvergedAsync(pairs[0].Local, DictionaryEpoch, pairs[0].Peer.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, maxRounds: 5, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsTrue(warm.Converged, "The warm-up cycle converges.");

            long before = GC.GetTotalAllocatedBytes(precise: true);
            long startTimestamp = TimeProvider.System.GetTimestamp();
            int totalRounds = 0;
            for(int cycle = 0; cycle < Cycles; cycle++)
            {
                ReplicaReconcileResult result = await ReplicaReconcileLoop.RunUntilConvergedAsync(pairs[cycle].Local, DictionaryEpoch, pairs[cycle].Peer.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, maxRounds: 5, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                Assert.IsTrue(result.Converged, $"Cycle {cycle} converges over the channel.");
                totalRounds += result.Rounds;
            }

            TimeSpan elapsed = TimeProvider.System.GetElapsedTime(startTimestamp);
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
            double perRoundKb = totalRounds > 0 ? allocated / (double)totalRounds / 1024.0 : 0;
            TestContext.WriteLine($"active-active reconcile soak: cycles={Cycles}, rounds={totalRounds}, time={elapsed.TotalMilliseconds:F0}ms, allocated={allocated / (1024.0 * 1024.0):F1}MB, per-cycle={allocated / Cycles / 1024.0:F1}KB, per-round={perRoundKb:F1}KB");
        }
        finally
        {
            foreach((ColumnarTripleIndex _, InProcessChannelPeer peer) in pairs)
            {
                peer.Dispose();
            }
        }
    }
}
