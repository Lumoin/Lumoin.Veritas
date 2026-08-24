using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Network;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Replication;
using Microsoft.Extensions.Time.Testing;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The distributed-repair certification (R-4): two diverged replicas converge to their union over a real
/// <see cref="AntiEntropySession.ReconcileAsync"/> driven through the M1 governance and M2 injection decorators,
/// despite an adverse network. Each adversity is a value-based decline of one round — a dropped fetch, a corrupted
/// sketch, a governance denial — that a later round repairs once the network or policy recovers; a delayed fetch
/// still converges within the round. This exercises governance and injection end-to-end against the shipped,
/// transport-free session, with no randomness and no wall-clock waits.
/// </summary>
[TestClass]
internal sealed class DistributedRepairCertificationTests
{
    /// <summary>The shared structural dictionary epoch both endpoints stamp in these tests, so a faithful peer's epoch always matches.</summary>
    private const ulong DictionaryEpoch = 7;

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

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

    /// <summary>Persists a peer replica's triples as a structural sketch image at the requested budget and wraps it as an owned fetch result — the value the peer fetch hands back.</summary>
    /// <param name="peerTriples">The peer replica's triples.</param>
    /// <param name="symbolBudget">The number of symbols to persist — the budget the session asks for.</param>
    /// <param name="pool">The pool the persist rents its transient buffers from and the owned image is rented from.</param>
    /// <returns>The peer's persisted sketch image as an owned <see cref="SketchFetchResult"/>.</returns>
    private static SketchFetchResult PersistPeerImage(EncodedTriple[] peerTriples, int symbolBudget, MemoryPool<byte> pool)
    {
        ContentKey128[] items = [.. peerTriples.Select(StructuralReconciliationProjection.Project)];
        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(items, SketchContract.Structural, symbolBudget, ChecksumAlgorithm.XxHash3, pool, new RatelessSketchCodec(pool).Encode, writer);

        return SketchChannelStamps.OwnedImage(SketchChannelDomain.Structural, DictionaryEpoch, writer.WrittenMemory, pool);
    }

    /// <summary>Asserts that the session converged and the converged replica holds exactly the union of both replicas' triples.</summary>
    /// <param name="result">The reconcile result to check.</param>
    /// <param name="left">The first replica's triples.</param>
    /// <param name="right">The second replica's triples.</param>
    private static void AssertConvergedToUnion(AntiEntropySessionResult result, EncodedTriple[] left, EncodedTriple[] right)
    {
        Assert.AreEqual(AntiEntropyOutcome.Converged, result.Outcome, "The healed round must converge.");

        HashSet<EncodedTriple> union = [.. left];
        union.UnionWith(right);
        HashSet<EncodedTriple> converged = [.. result.ConvergedIndex.EnumerateTriples()];
        Assert.IsTrue(union.SetEquals(converged), "The replica must converge to the union of both replicas.");
    }

    /// <summary>A converged reconcile surfaces the recovered additions (the delta the bridge journals back to the dataset): the symmetric difference applied to converge, which re-applied to the local replica reproduces the union.</summary>
    [TestMethod]
    public async Task SurfacesRecoveredAdditionsOnConvergence()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);
        AsyncSketchFetchDelegate fetch = (budget, token) => new ValueTask<SketchFetchResult>(PersistPeerImage(triplesB, budget, pool));

        AntiEntropySessionResult converged = await AntiEntropySession.ReconcileAsync(replicaA, DictionaryEpoch, fetch, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(AntiEntropyOutcome.Converged, converged.Outcome, "The round converges.");

        HashSet<EncodedTriple> symmetricDifference = [.. triplesA];
        symmetricDifference.SymmetricExceptWith(triplesB);
        HashSet<EncodedTriple> recovered = [.. converged.RecoveredAdditions.ToArray()];
        Assert.IsTrue(symmetricDifference.SetEquals(recovered), "RecoveredAdditions is exactly the symmetric difference applied to converge.");

        ColumnarTripleIndex reapplied = replicaA.Apply(converged.RecoveredAdditions.ToArray(), []);
        HashSet<EncodedTriple> union = [.. triplesA];
        union.UnionWith(triplesB);
        Assert.IsTrue(union.SetEquals([.. reapplied.EnumerateTriples()]), "Re-applying RecoveredAdditions to the local replica reproduces the union — the write-back contract.");
    }

    /// <summary>An already-consistent reconcile (identical replicas) recovers nothing, so RecoveredAdditions is empty.</summary>
    [TestMethod]
    public async Task AlreadyConsistentSurfacesNoRecoveredAdditions()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triples = Line(0, 100);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triples);
        AsyncSketchFetchDelegate fetch = (budget, token) => new ValueTask<SketchFetchResult>(PersistPeerImage(triples, budget, pool));

        AntiEntropySessionResult result = await AntiEntropySession.ReconcileAsync(replicaA, DictionaryEpoch, fetch, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(AntiEntropyOutcome.AlreadyConsistent, result.Outcome, "Identical replicas are already consistent.");
        Assert.IsTrue(result.RecoveredAdditions.IsEmpty, "An already-consistent reconcile applies nothing, so RecoveredAdditions is empty.");
    }

    /// <summary>A transient dropped fetch leaves the round unavailable, and the next round — the peer now reachable — converges to the union.</summary>
    [TestMethod]
    public async Task TransientDropConvergesOnRetry()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);

        AsyncSketchFetchDelegate realFetch = (budget, token) => new ValueTask<SketchFetchResult>(PersistPeerImage(triplesB, budget, pool));
        SketchFetchFaultPlan plan = callIndex => callIndex == 1 ? SketchFetchFault.Drop : SketchFetchFault.Pass;
        FaultInjectingSketchFetch injector = new(realFetch, plan, pool, TimeProvider.System);

        AntiEntropySessionResult dropped = await AntiEntropySession.ReconcileAsync(replicaA, DictionaryEpoch, injector.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(AntiEntropyOutcome.PeerUnavailable, dropped.Outcome, "A dropped fetch leaves the peer unavailable for that round.");
        Assert.AreSame(replicaA, dropped.ConvergedIndex, "An unavailable round applies nothing.");

        AntiEntropySessionResult healed = await AntiEntropySession.ReconcileAsync(dropped.ConvergedIndex, DictionaryEpoch, injector.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        AssertConvergedToUnion(healed, triplesA, triplesB);
    }

    /// <summary>A sketch corrupted in flight is refused as a value-based rejection, and the next clean round converges — the corruption never reaches the index.</summary>
    [TestMethod]
    public async Task CorruptedSketchIsRejectedThenConverges()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);

        AsyncSketchFetchDelegate realFetch = (budget, token) => new ValueTask<SketchFetchResult>(PersistPeerImage(triplesB, budget, pool));
        SketchFetchFaultPlan plan = callIndex => callIndex == 1 ? SketchFetchFault.Corrupt : SketchFetchFault.Pass;
        FaultInjectingSketchFetch injector = new(realFetch, plan, pool, TimeProvider.System);

        AntiEntropySessionResult corrupted = await AntiEntropySession.ReconcileAsync(replicaA, DictionaryEpoch, injector.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(AntiEntropyOutcome.PeerSketchRejected, corrupted.Outcome, "A corrupted sketch is refused by value, not applied.");
        Assert.AreSame(replicaA, corrupted.ConvergedIndex, "A rejected sketch leaves the index unchanged — corruption never reaches it.");

        AntiEntropySessionResult healed = await AntiEntropySession.ReconcileAsync(corrupted.ConvergedIndex, DictionaryEpoch, injector.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        AssertConvergedToUnion(healed, triplesA, triplesB);
    }

    /// <summary>A fetch delayed by injected latency still converges within the round once the clock advances — bounded latency does not break repair.</summary>
    [TestMethod]
    public async Task DelayedFetchConvergesWithinTheRound()
    {
        using VeritasMemoryPool<byte> pool = new();
        FakeTimeProvider clock = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);

        AsyncSketchFetchDelegate realFetch = (budget, token) => new ValueTask<SketchFetchResult>(PersistPeerImage(triplesB, budget, pool));
        TimeSpan latency = TimeSpan.FromSeconds(2);
        SketchFetchFaultPlan plan = callIndex => SketchFetchFault.After(latency, SketchFetchFaultKind.Pass);
        FaultInjectingSketchFetch injector = new(realFetch, plan, pool, clock);

        Task<AntiEntropySessionResult> pending = AntiEntropySession.ReconcileAsync(replicaA, DictionaryEpoch, injector.FetchAsync, ReplicationPolicy.Default, pool, clock, cancellationToken: TestContext.CancellationToken).AsTask();
        Assert.IsFalse(pending.IsCompleted, "Reconcile waits on the delayed fetch.");

        clock.Advance(latency);
        AntiEntropySessionResult result = await pending.ConfigureAwait(false);
        AssertConvergedToUnion(result, triplesA, triplesB);
    }

    /// <summary>A sustained partition keeps several rounds unavailable, and the first round after the partition heals converges — no progress is lost across the outage.</summary>
    [TestMethod]
    public async Task SustainedPartitionHealsAndConverges()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);

        AsyncSketchFetchDelegate realFetch = (budget, token) => new ValueTask<SketchFetchResult>(PersistPeerImage(triplesB, budget, pool));
        const int partitionRounds = 3;
        SketchFetchFaultPlan plan = callIndex => callIndex <= partitionRounds ? SketchFetchFault.Drop : SketchFetchFault.Pass;
        FaultInjectingSketchFetch injector = new(realFetch, plan, pool, TimeProvider.System);

        ColumnarTripleIndex current = replicaA;
        for(int round = 1; round <= partitionRounds; round++)
        {
            AntiEntropySessionResult severed = await AntiEntropySession.ReconcileAsync(current, DictionaryEpoch, injector.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(AntiEntropyOutcome.PeerUnavailable, severed.Outcome, "A round during the partition is unavailable.");
            Assert.AreSame(replicaA, severed.ConvergedIndex, "No round during the partition applies anything.");
            current = severed.ConvergedIndex;
        }

        AntiEntropySessionResult healed = await AntiEntropySession.ReconcileAsync(current, DictionaryEpoch, injector.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        AssertConvergedToUnion(healed, triplesA, triplesB);
    }

    /// <summary>A firewall denial leaves the round unavailable; once the peer is allowed, the next round converges — a governance decline is repaired exactly like a transport one.</summary>
    [TestMethod]
    public async Task FirewallDeniedPeerConvergesOnceAllowed()
    {
        using VeritasMemoryPool<byte> pool = new();
        NetworkFirewall firewall = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);
        byte[] peerId = [42];

        AsyncSketchFetchDelegate realFetch = (budget, token) => new ValueTask<SketchFetchResult>(PersistPeerImage(triplesB, budget, pool));
        NetworkPeerKey peer = NetworkPeerKey.RentReplicaId(pool, peerId);
        using GovernedSketchFetch governed = new(realFetch, firewall.Decide, peer, null, TimeProvider.System);

        firewall.Deny(NetworkPeerKeyKind.ReplicaId, peerId);
        AntiEntropySessionResult denied = await AntiEntropySession.ReconcileAsync(replicaA, DictionaryEpoch, governed.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(AntiEntropyOutcome.PeerUnavailable, denied.Outcome, "A firewalled peer is unavailable for that round.");
        Assert.AreSame(replicaA, denied.ConvergedIndex, "A denied round applies nothing.");

        firewall.Allow(NetworkPeerKeyKind.ReplicaId, peerId);
        AntiEntropySessionResult healed = await AntiEntropySession.ReconcileAsync(denied.ConvergedIndex, DictionaryEpoch, governed.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        AssertConvergedToUnion(healed, triplesA, triplesB);
    }
}
