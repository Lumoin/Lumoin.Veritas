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
        SketchPersistence.PersistSketch(items, SketchContract.Structural, symbolBudget, ChecksumAlgorithm.XxHash3, pool, new RatelessSketchCodec(pool).Encode, writer);

        return writer.WrittenMemory;
    }

    /// <summary>Two diverged replicas converge to their union through the peer-reconciliation rung, and the ladder descent stops there: the local rungs decline and the session completes the peel.</summary>
    [TestMethod]
    public async Task DivergedReplicasConvergeThroughSession()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);

        SketchFetchDelegate fetch = budget => PersistPeerImage(triplesB, budget, pool);
        AntiEntropyRepairLadderBinding binding = new(replicaA, fetch, ReplicationPolicy.Default, pool, TimeProvider.System);

        RepairRung outcome = await RepairSourceLadder.DescendAsync(binding.Attempt, CancellationToken.None).ConfigureAwait(false);

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
    public async Task LadderNamesLossWhenPeerUnavailable()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);

        SketchFetchDelegate fetch = _ => ReadOnlyMemory<byte>.Empty;
        AntiEntropyRepairLadderBinding binding = new(replicaA, fetch, ReplicationPolicy.Default, pool, TimeProvider.System);

        RepairRung outcome = await RepairSourceLadder.DescendAsync(binding.Attempt, CancellationToken.None).ConfigureAwait(false);

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

    /// <summary>A budget too small to peel the difference yields a partial peel that the session declines: it absorbs symbols but never converges, applies nothing, leaves the local index unchanged, and the ladder names the loss — the decline path distinct from an unreachable peer.</summary>
    [TestMethod]
    public async Task PartialPeelDeclinesAndNamesLoss()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);

        //The two replicas differ by 100 triples, but this policy budgets 50 coded symbols regardless of size:
        //50 symbols cannot peel a 100-item difference, so the decoder cannot converge and the session must
        //decline rather than apply a partial difference.
        ReplicationPolicy starvedBudget = new(50, 0);
        SketchFetchDelegate fetch = budget => PersistPeerImage(triplesB, budget, pool);
        AntiEntropyRepairLadderBinding binding = new(replicaA, fetch, starvedBudget, pool, TimeProvider.System);

        RepairRung outcome = await RepairSourceLadder.DescendAsync(binding.Attempt, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(RepairRung.NamedLoss, outcome, "A budget too small to peel the difference must leave the loss named.");
        Assert.IsNotNull(binding.Result, "The peer-reconciliation rung still runs and records its declined result.");
        AntiEntropySessionResult result = binding.Result.Value;
        Assert.IsFalse(result.IsComplete, "A partial peel must report an incomplete session.");
        Assert.IsGreaterThan(0, result.AbsorbedSymbols, "A partial peel absorbs symbols before giving up — unlike an unreachable peer, which absorbs none.");
        Assert.AreSame(replicaA, result.ConvergedIndex, "A declined partial peel applies nothing: the local index is returned unchanged.");
    }

    /// <summary>Captures replication trace events through a method group, so a test body holds no closure over the captured list.</summary>
    private sealed class ReplicationTraceCapture
    {
        /// <summary>The events captured, in emission order.</summary>
        public List<ReplicationTraceEvent> Events { get; } = [];

        /// <summary>The handler entry point.</summary>
        /// <param name="evt">The emitted event.</param>
        public void Capture(in ReplicationTraceEvent evt)
        {
            Events.Add(evt);
        }
    }

    /// <summary>A converging reconcile reports <see cref="AntiEntropyOutcome.Converged"/> and emits one matching trace event carrying the recovered count and the budget.</summary>
    [TestMethod]
    public void ConvergedReconcileReportsConvergedAndEmitsEvent()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);
        SketchFetchDelegate fetch = budget => PersistPeerImage(triplesB, budget, pool);
        ReplicationTraceCapture trace = new();
        Guid correlation = new("11111111-2222-3333-4444-555555555555");

        AntiEntropySessionResult result = AntiEntropySession.Reconcile(replicaA, fetch, ReplicationPolicy.Default, pool, TimeProvider.System, trace.Capture, correlation);

        Assert.AreEqual(AntiEntropyOutcome.Converged, result.Outcome, "Diverged replicas with a sufficient budget converge.");
        ReplicationTraceEvent emitted = trace.Events.Single();
        Assert.AreEqual(AntiEntropyOutcome.Converged, emitted.Outcome, "The emitted event must carry the converged outcome.");
        Assert.AreEqual(correlation, emitted.CorrelationId, "The event must carry the reconcile's correlation id.");
        HashSet<ContentKey128> expectedDifference = [.. triplesA.Select(StructuralReconciliationProjection.Project)];
        expectedDifference.SymmetricExceptWith(triplesB.Select(StructuralReconciliationProjection.Project));
        Assert.AreEqual(expectedDifference.Count, emitted.RecoveredCount, "The event must report exactly the symmetric-difference count, independently of the result.");
        Assert.IsGreaterThan(0, emitted.SymbolBudget, "The event must carry the symbol budget the sketches were built at.");
    }

    /// <summary>Identical replicas report <see cref="AntiEntropyOutcome.AlreadyConsistent"/> — a complete, empty peel — and emit it.</summary>
    [TestMethod]
    public void IdenticalReplicasReportAlreadyConsistent()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triples = Line(0, 100);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triples);
        SketchFetchDelegate fetch = budget => PersistPeerImage(triples, budget, pool);
        ReplicationTraceCapture trace = new();

        AntiEntropySessionResult result = AntiEntropySession.Reconcile(replicaA, fetch, ReplicationPolicy.Default, pool, TimeProvider.System, trace.Capture);

        Assert.AreEqual(AntiEntropyOutcome.AlreadyConsistent, result.Outcome, "Replicas that already agree are already consistent.");
        Assert.IsTrue(result.IsComplete, "AlreadyConsistent is a completing outcome.");
        Assert.AreEqual(0, result.RecoveredCount);
        ReplicationTraceEvent emitted = trace.Events.Single();
        Assert.AreEqual(AntiEntropyOutcome.AlreadyConsistent, emitted.Outcome);
        Assert.IsGreaterThan(0, emitted.AbsorbedSymbols, "An already-consistent peel is a post-combine empty peel — symbols were absorbed, unlike an unavailable peer which absorbs none.");
    }

    /// <summary>An unreachable peer reports <see cref="AntiEntropyOutcome.PeerUnavailable"/>, absorbs no symbols, and emits the decline.</summary>
    [TestMethod]
    public void UnavailablePeerReportsPeerUnavailable()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);
        SketchFetchDelegate fetch = _ => ReadOnlyMemory<byte>.Empty;
        ReplicationTraceCapture trace = new();

        AntiEntropySessionResult result = AntiEntropySession.Reconcile(replicaA, fetch, ReplicationPolicy.Default, pool, TimeProvider.System, trace.Capture);

        Assert.AreEqual(AntiEntropyOutcome.PeerUnavailable, result.Outcome);
        Assert.IsFalse(result.IsComplete);
        Assert.AreEqual(0, result.AbsorbedSymbols, "No combine happened, so no symbols were absorbed.");
        Assert.AreSame(replicaA, result.ConvergedIndex, "An unavailable peer leaves the local index unchanged.");
        Assert.AreEqual(AntiEntropyOutcome.PeerUnavailable, trace.Events.Single().Outcome);
    }

    /// <summary>A corrupt peer sketch is refused as a value-based <see cref="AntiEntropyOutcome.PeerSketchRejected"/> decline, not a propagated exception, and the local index is left unchanged — the operational face of detection-precedes-combine.</summary>
    [TestMethod]
    public void CorruptPeerSketchIsRejectedAsAValueNotAThrow()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);

        //A non-empty buffer that is not a valid sketch image: the verifying load refuses it (magic/geometry), so
        //the session declines by value rather than letting the load's exception escape.
        SketchFetchDelegate fetch = _ => new byte[64];
        ReplicationTraceCapture trace = new();

        AntiEntropySessionResult result = AntiEntropySession.Reconcile(replicaA, fetch, ReplicationPolicy.Default, pool, TimeProvider.System, trace.Capture);

        Assert.AreEqual(AntiEntropyOutcome.PeerSketchRejected, result.Outcome, "A peer sketch that fails its verifying load is refused by value.");
        Assert.IsFalse(result.IsComplete);
        Assert.AreSame(replicaA, result.ConvergedIndex, "A refused peer sketch leaves the local index unchanged.");
        Assert.AreEqual(AntiEntropyOutcome.PeerSketchRejected, trace.Events.Single().Outcome);
    }

    /// <summary>A budget too small to peel the difference reports <see cref="AntiEntropyOutcome.IncompletePeel"/>, having absorbed symbols without converging, and applies nothing.</summary>
    [TestMethod]
    public void StarvedBudgetReportsIncompletePeel()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);
        SketchFetchDelegate fetch = budget => PersistPeerImage(triplesB, budget, pool);
        ReplicationTraceCapture trace = new();

        AntiEntropySessionResult result = AntiEntropySession.Reconcile(replicaA, fetch, new ReplicationPolicy(50, 0), pool, TimeProvider.System, trace.Capture);

        Assert.AreEqual(AntiEntropyOutcome.IncompletePeel, result.Outcome, "A starved budget cannot peel the difference.");
        Assert.IsFalse(result.IsComplete);
        Assert.IsGreaterThan(0, result.AbsorbedSymbols);
        Assert.AreSame(replicaA, result.ConvergedIndex, "An incomplete peel applies nothing.");
        Assert.AreEqual(AntiEntropyOutcome.IncompletePeel, trace.Events.Single().Outcome);
    }
}
