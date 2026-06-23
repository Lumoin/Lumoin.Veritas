using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Replication;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The <see cref="AntiEntropySession"/> reconciliation driven through the core's public repair-source ladder:
/// two diverged replicas converge to their union when the peer-reconciliation rung completes the rateless peel
/// and the descent stops there; identical replicas yield a complete, empty session that applies nothing; and an
/// unreachable peer makes every restoring rung decline so the ladder names the loss. The session reuses the
/// shipped, transport-free core seams; these tests assert the library-side contract — convergence by
/// repair-as-ingest, the ladder integration, and the decline paths.
/// </summary>
[TestClass]
internal sealed class AntiEntropySessionTests
{
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

    /// <summary>Persists a peer replica's triples as a structural sketch image at the requested budget — the bytes a <see cref="SketchFetchDelegate"/> hands back.</summary>
    /// <param name="peerTriples">The peer replica's triples.</param>
    /// <param name="symbolBudget">The number of symbols to persist — the budget the session asks for.</param>
    /// <param name="pool">The pool the persist rents its transient buffers from.</param>
    /// <returns>The peer's persisted sketch image.</returns>
    private static ReadOnlyMemory<byte> PersistPeerImage(EncodedTriple[] peerTriples, int symbolBudget, MemoryPool<byte> pool)
    {
        ContentKey128[] items = [.. peerTriples.Select(StructuralReconciliationProjection.Project)];
        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(items, SketchContract.Structural, symbolBudget, ChecksumAlgorithm.XxHash3, pool, RatelessSketchCodec.Encode, writer);

        return writer.WrittenMemory;
    }

    /// <summary>Two diverged replicas converge to their union through the peer-reconciliation rung, and the ladder descent stops there: the local rungs decline and the session completes the peel.</summary>
    [TestMethod]
    public void DivergedReplicasConvergeThroughSession()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);

        SketchFetchDelegate fetch = budget => PersistPeerImage(triplesB, budget, pool);
        AntiEntropyRepairLadderBinding binding = new(replicaA, fetch, ReplicationPolicy.Default, pool, TimeProvider.System);

        RepairRung outcome = RepairSourceLadder.Descend(binding.Attempt);

        Assert.AreEqual(RepairRung.PeerReconciliation, outcome, "The descent must stop at the peer-reconciliation rung once the peer completes the peel.");
        Assert.IsNotNull(binding.Result, "Reaching the peer-reconciliation rung must record a session result.");
        AntiEntropySessionResult result = binding.Result.Value;
        Assert.IsTrue(result.IsComplete, "A sufficient budget must yield a complete peel.");

        HashSet<ContentKey128> expectedDifference = [.. triplesA.Select(StructuralReconciliationProjection.Project)];
        expectedDifference.SymmetricExceptWith(triplesB.Select(StructuralReconciliationProjection.Project));
        Assert.AreEqual(expectedDifference.Count, result.RecoveredCount, "The session must recover exactly the symmetric difference.");

        HashSet<EncodedTriple> union = [.. triplesA];
        union.UnionWith(triplesB);
        HashSet<EncodedTriple> converged = [.. result.ConvergedIndex.EnumerateTriples()];
        Assert.IsTrue(union.SetEquals(converged), "Replica A must converge to the union of both replicas.");
    }

    /// <summary>Identical replicas reconcile to a complete, empty peel: nothing is recovered and the local index is returned as-is, never a re-derived equal copy.</summary>
    [TestMethod]
    public void IdenticalReplicasYieldEmptySessionNoApply()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triples = Line(0, 100);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triples);

        SketchFetchDelegate fetch = budget => PersistPeerImage(triples, budget, pool);
        AntiEntropySessionResult result = AntiEntropySession.Reconcile(replicaA, fetch, ReplicationPolicy.Default, pool, TimeProvider.System);

        Assert.IsTrue(result.IsComplete, "Identical replicas reconcile to a complete, empty peel.");
        Assert.AreEqual(0, result.RecoveredCount, "Identical replicas recover no difference.");
        Assert.AreSame(replicaA, result.ConvergedIndex, "An empty peel applies nothing, so the local index is returned unchanged.");
    }

    /// <summary>An unreachable peer (an empty fetch) makes the peer-reconciliation rung decline, so every restoring rung declines and the ladder names the loss; the declined session leaves the local index unchanged.</summary>
    [TestMethod]
    public void LadderNamesLossWhenPeerUnavailable()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);

        SketchFetchDelegate fetch = _ => ReadOnlyMemory<byte>.Empty;
        AntiEntropyRepairLadderBinding binding = new(replicaA, fetch, ReplicationPolicy.Default, pool, TimeProvider.System);

        RepairRung outcome = RepairSourceLadder.Descend(binding.Attempt);

        Assert.AreEqual(RepairRung.NamedLoss, outcome, "An unreachable peer leaves the loss named: every restoring rung declines.");
        Assert.IsNotNull(binding.Result, "The peer-reconciliation rung still runs and records its declined result.");
        Assert.IsFalse(binding.Result.Value.IsComplete, "An unreachable peer yields an incomplete session.");
        Assert.AreEqual(0, binding.Result.Value.RecoveredCount, "An unreachable peer recovers nothing.");
        Assert.AreSame(replicaA, binding.Result.Value.ConvergedIndex, "A declined session leaves the local index unchanged.");
    }

    /// <summary>Re-running the session from the already-converged replica against the same peer recovers the difference again but applies it idempotently, so the converged triple set is unchanged.</summary>
    [TestMethod]
    public void ReRunConvergedSessionIsANoOp()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);
        SketchFetchDelegate fetch = budget => PersistPeerImage(triplesB, budget, pool);

        AntiEntropySessionResult first = AntiEntropySession.Reconcile(replicaA, fetch, ReplicationPolicy.Default, pool, TimeProvider.System);
        Assert.IsTrue(first.IsComplete, "The first session must complete the peel.");

        HashSet<EncodedTriple> union = [.. triplesA];
        union.UnionWith(triplesB);
        HashSet<EncodedTriple> convergedFirst = [.. first.ConvergedIndex.EnumerateTriples()];
        Assert.IsTrue(union.SetEquals(convergedFirst), "The first session converges to the union.");

        AntiEntropySessionResult second = AntiEntropySession.Reconcile(first.ConvergedIndex, fetch, ReplicationPolicy.Default, pool, TimeProvider.System);
        Assert.IsTrue(second.IsComplete, "The re-run must complete the peel.");

        HashSet<EncodedTriple> convergedSecond = [.. second.ConvergedIndex.EnumerateTriples()];
        Assert.IsTrue(union.SetEquals(convergedSecond), "Re-running against the same peer leaves the converged set unchanged — repair-as-ingest is idempotent.");
    }
}
