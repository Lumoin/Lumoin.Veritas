using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
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
/// The asynchronous, transport-facing reconcile: <see cref="AntiEntropySession.ReconcileAsync"/> awaits a peer's
/// sketch through an <see cref="AsyncSketchFetchDelegate"/> and otherwise behaves exactly as the synchronous
/// reconcile, and the <see cref="SketchChannelClient"/>/<see cref="SketchChannelServer"/> pair carries that fetch
/// over a real Verisync message channel — here an in-memory <see cref="Pipe"/> duplex, the same framing a socket
/// would use. Two replicas reconcile to their union over the channel, in one direction and active-active in both.
/// Small replicas keep each sketch well under one frame, so the convergence is the assertion, not backpressure.
/// </summary>
[TestClass]
internal sealed class SketchChannelTransportTests
{
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

    /// <summary>Persists a peer replica's triples as a structural sketch image at the requested budget — the bytes an async fetch hands back in the in-memory tests.</summary>
    /// <param name="peerTriples">The peer replica's triples.</param>
    /// <param name="symbolBudget">The number of symbols to persist.</param>
    /// <param name="pool">The pool the persist rents its transient buffers from.</param>
    /// <returns>The peer's persisted sketch image.</returns>
    private static ReadOnlyMemory<byte> PersistPeerImage(EncodedTriple[] peerTriples, int symbolBudget, MemoryPool<byte> pool)
    {
        ContentKey128[] items = [.. peerTriples.Select(StructuralReconciliationProjection.Project)];
        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(items, SketchContract.Structural, symbolBudget, ChecksumAlgorithm.XxHash3, pool, RatelessSketchCodec.Encode, writer);

        return writer.WrittenMemory;
    }

    /// <summary>The async reconcile converges through an awaited fetch exactly as the synchronous one does: the only difference is the suspension point.</summary>
    [TestMethod]
    public async Task AsyncReconcileConvergesThroughAnAwaitedFetch()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);
        AsyncSketchFetchDelegate fetch = (budget, _) => ValueTask.FromResult(PersistPeerImage(triplesB, budget, pool));

        AntiEntropySessionResult result = await AntiEntropySession.ReconcileAsync(replicaA, fetch, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(AntiEntropyOutcome.Converged, result.Outcome, "An awaited fetch of a diverged peer converges.");
        HashSet<EncodedTriple> union = [.. triplesA];
        union.UnionWith(triplesB);
        Assert.IsTrue(union.SetEquals([.. result.ConvergedIndex.EnumerateTriples()]), "The async reconcile must converge to the union.");
    }

    /// <summary>An async fetch that returns an empty image is an unavailable peer: the async reconcile declines exactly as the synchronous one does.</summary>
    [TestMethod]
    public async Task AsyncReconcileReportsPeerUnavailableOnEmptyFetch()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);
        AsyncSketchFetchDelegate fetch = (_, _) => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);

        AntiEntropySessionResult result = await AntiEntropySession.ReconcileAsync(replicaA, fetch, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(AntiEntropyOutcome.PeerUnavailable, result.Outcome);
        Assert.AreSame(replicaA, result.ConvergedIndex, "An unavailable peer leaves the local index unchanged.");
    }

    /// <summary>Replica A reconciles to the union of both by fetching replica B's sketch over a real message-channel duplex (in-memory pipes), proving the async session works end-to-end over the wire.</summary>
    [TestMethod]
    public async Task ReplicaConvergesOverAMessageChannel()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 30);
        EncodedTriple[] triplesB = Line(20, 30);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);
        ColumnarTripleIndex replicaB = ColumnarTripleIndex.Build(triplesB);

        Pipe requestPipe = new();
        Pipe responsePipe = new();
        SketchChannelServer server = new(replicaB, pool, requestPipe.Reader, responsePipe.Writer);
        SketchChannelClient client = new(requestPipe.Writer, responsePipe.Reader);

        Task serveTask = server.ServeAsync(TestContext.CancellationToken);
        AntiEntropySessionResult result = await AntiEntropySession.ReconcileAsync(replicaA, client.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await serveTask.ConfigureAwait(false);

        Assert.AreEqual(AntiEntropyOutcome.Converged, result.Outcome, "Replica A must converge over the channel.");
        HashSet<EncodedTriple> union = [.. triplesA];
        union.UnionWith(triplesB);
        Assert.IsTrue(union.SetEquals([.. result.ConvergedIndex.EnumerateTriples()]), "Replica A must converge to the union over the channel.");
    }

    /// <summary>Active-active: each replica fetches the other's sketch over its own channel duplex, and both converge to the union of the two original states — the genuine cross-process replication shape.</summary>
    [TestMethod]
    public async Task BothReplicasConvergeOverChannelsToTheUnion()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 30);
        EncodedTriple[] triplesB = Line(20, 30);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);
        ColumnarTripleIndex replicaB = ColumnarTripleIndex.Build(triplesB);

        //A fetches B's sketch over one duplex; B fetches A's over another. Each server serves its original snapshot,
        //so both reconciles recover against the unchanged peer and reach the union of the two originals.
        Pipe requestAtoB = new();
        Pipe responseBtoA = new();
        SketchChannelServer serverB = new(replicaB, pool, requestAtoB.Reader, responseBtoA.Writer);
        SketchChannelClient clientA = new(requestAtoB.Writer, responseBtoA.Reader);

        Pipe requestBtoA = new();
        Pipe responseAtoB = new();
        SketchChannelServer serverA = new(replicaA, pool, requestBtoA.Reader, responseAtoB.Writer);
        SketchChannelClient clientB = new(requestBtoA.Writer, responseAtoB.Reader);

        Task serveB = serverB.ServeAsync(TestContext.CancellationToken);
        Task serveA = serverA.ServeAsync(TestContext.CancellationToken);
        AntiEntropySessionResult resultA = await AntiEntropySession.ReconcileAsync(replicaA, clientA.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        AntiEntropySessionResult resultB = await AntiEntropySession.ReconcileAsync(replicaB, clientB.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await Task.WhenAll(serveA, serveB).ConfigureAwait(false);

        Assert.AreEqual(AntiEntropyOutcome.Converged, resultA.Outcome);
        Assert.AreEqual(AntiEntropyOutcome.Converged, resultB.Outcome);
        HashSet<EncodedTriple> union = [.. triplesA];
        union.UnionWith(triplesB);
        Assert.IsTrue(union.SetEquals([.. resultA.ConvergedIndex.EnumerateTriples()]), "Replica A must converge to the union.");
        Assert.IsTrue(union.SetEquals([.. resultB.ConvergedIndex.EnumerateTriples()]), "Replica B must converge to the union.");
    }
}
