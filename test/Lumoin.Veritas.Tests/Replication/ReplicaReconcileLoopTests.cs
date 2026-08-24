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
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Replication;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The reconcile loop drives rounds until convergence: a transient drop is absorbed and the loop converges to the
/// union on a later round within the bound, while a peer that never recovers leaves the loop unconverged at the
/// bound with the local index untouched. Driven through the M2 fault injector so the per-round adversity is
/// deterministic.
/// </summary>
[TestClass]
internal sealed class ReplicaReconcileLoopTests
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

    /// <summary>Persists a peer replica's triples as a structural sketch image at the requested budget and wraps it as an owned fetch result.</summary>
    /// <param name="peerTriples">The peer replica's triples.</param>
    /// <param name="symbolBudget">The number of symbols to persist.</param>
    /// <param name="pool">The pool the persist rents its transient buffers from and the owned image is rented from.</param>
    /// <returns>The peer's persisted sketch image as an owned <see cref="SketchFetchResult"/>.</returns>
    private static SketchFetchResult PersistPeerImage(EncodedTriple[] peerTriples, int symbolBudget, MemoryPool<byte> pool)
    {
        ContentKey128[] items = [.. peerTriples.Select(StructuralReconciliationProjection.Project)];
        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(items, SketchContract.Structural, symbolBudget, ChecksumAlgorithm.XxHash3, pool, new RatelessSketchCodec(pool).Encode, writer);

        return SketchChannelStamps.OwnedImage(SketchChannelDomain.Structural, DictionaryEpoch, writer.WrittenMemory, pool);
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

    /// <summary>A transient drop is retried and the loop converges to the union on the next round.</summary>
    [TestMethod]
    public async Task TransientDropConvergesWithinTheBound()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex local = ColumnarTripleIndex.Build(triplesA);

        AsyncSketchFetchDelegate realFetch = (budget, token) => new ValueTask<SketchFetchResult>(PersistPeerImage(triplesB, budget, pool));
        SketchFetchFaultPlan plan = callIndex => callIndex == 1 ? SketchFetchFault.Drop : SketchFetchFault.Pass;
        FaultInjectingSketchFetch injector = new(realFetch, plan, pool, TimeProvider.System);

        ReplicaReconcileResult result = await ReplicaReconcileLoop.RunUntilConvergedAsync(local, DictionaryEpoch, injector.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, maxRounds: 5, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(result.Converged, "The loop converges after the transient drop.");
        Assert.AreEqual(2, result.Rounds, "Round 1 dropped, round 2 converged.");
        Assert.AreEqual(AntiEntropyOutcome.Converged, result.LastOutcome);
        AssertUnion(result.Index, triplesA, triplesB);
    }

    /// <summary>A peer that never recovers leaves the loop unconverged at the bound, with the local index untouched.</summary>
    [TestMethod]
    public async Task PeerNeverRecoversStaysUnconvergedAtTheBound()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex local = ColumnarTripleIndex.Build(triplesA);

        AsyncSketchFetchDelegate realFetch = (budget, token) => new ValueTask<SketchFetchResult>(PersistPeerImage(triplesB, budget, pool));
        SketchFetchFaultPlan plan = callIndex => SketchFetchFault.Drop;
        FaultInjectingSketchFetch injector = new(realFetch, plan, pool, TimeProvider.System);

        ReplicaReconcileResult result = await ReplicaReconcileLoop.RunUntilConvergedAsync(local, DictionaryEpoch, injector.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, maxRounds: 3, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(result.Converged, "An unreachable peer never converges.");
        Assert.AreEqual(3, result.Rounds, "The loop runs the full round bound.");
        Assert.AreEqual(AntiEntropyOutcome.PeerUnavailable, result.LastOutcome);
        Assert.AreSame(local, result.Index, "An unconverged loop leaves the local index unchanged.");
    }
}
