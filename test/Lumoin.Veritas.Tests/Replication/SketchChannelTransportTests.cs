using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Verisync.Core;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Tests.MemoryPool;

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

    /// <summary>Persists a peer replica's triples as a structural sketch image at the requested budget and wraps it as an owned fetch result — the value an async fetch hands back in the in-memory tests.</summary>
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

    /// <summary>The async reconcile converges through an awaited fetch exactly as the synchronous one does: the only difference is the suspension point.</summary>
    [TestMethod]
    public async Task AsyncReconcileConvergesThroughAnAwaitedFetch()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);
        AsyncSketchFetchDelegate fetch = (budget, _) => new ValueTask<SketchFetchResult>(PersistPeerImage(triplesB, budget, pool));

        AntiEntropySessionResult result = await AntiEntropySession.ReconcileAsync(replicaA, DictionaryEpoch, fetch, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

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
        AsyncSketchFetchDelegate fetch = (_, _) => new ValueTask<SketchFetchResult>(SketchFetchResult.Unavailable);

        AntiEntropySessionResult result = await AntiEntropySession.ReconcileAsync(replicaA, DictionaryEpoch, fetch, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

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

        Pipe requestPipe = new();
        Pipe responsePipe = new();
        ReplicationIndexFeed feedB = new(triplesB, default);
        using IncrementalSketchMaintainer maintainerB = new(feedB, pool, IncrementalSketchMaintainerOptions.Default, DictionaryEpoch);
        SketchChannelServer server = new(maintainerB, pool, requestPipe.Reader, responsePipe.Writer, DictionaryEpoch);
        SketchChannelClient client = new(requestPipe.Writer, responsePipe.Reader, pool, SketchChannelDomain.Structural, DictionaryEpoch);

        Task serveTask = server.ServeAsync(TestContext.CancellationToken);
        AntiEntropySessionResult result = await AntiEntropySession.ReconcileAsync(replicaA, DictionaryEpoch, client.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
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
        ReplicationIndexFeed feedB = new(triplesB, default);
        using IncrementalSketchMaintainer maintainerB = new(feedB, pool, IncrementalSketchMaintainerOptions.Default, DictionaryEpoch);
        Pipe requestAtoB = new();
        Pipe responseBtoA = new();
        SketchChannelServer serverB = new(maintainerB, pool, requestAtoB.Reader, responseBtoA.Writer, DictionaryEpoch);
        SketchChannelClient clientA = new(requestAtoB.Writer, responseBtoA.Reader, pool, SketchChannelDomain.Structural, DictionaryEpoch);

        ReplicationIndexFeed feedA = new(triplesA, default);
        using IncrementalSketchMaintainer maintainerA = new(feedA, pool, IncrementalSketchMaintainerOptions.Default, DictionaryEpoch);
        Pipe requestBtoA = new();
        Pipe responseAtoB = new();
        SketchChannelServer serverA = new(maintainerA, pool, requestBtoA.Reader, responseAtoB.Writer, DictionaryEpoch);
        SketchChannelClient clientB = new(requestBtoA.Writer, responseAtoB.Reader, pool, SketchChannelDomain.Structural, DictionaryEpoch);

        Task serveB = serverB.ServeAsync(TestContext.CancellationToken);
        Task serveA = serverA.ServeAsync(TestContext.CancellationToken);
        AntiEntropySessionResult resultA = await AntiEntropySession.ReconcileAsync(replicaA, DictionaryEpoch, clientA.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        AntiEntropySessionResult resultB = await AntiEntropySession.ReconcileAsync(replicaB, DictionaryEpoch, clientB.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await Task.WhenAll(serveA, serveB).ConfigureAwait(false);

        Assert.AreEqual(AntiEntropyOutcome.Converged, resultA.Outcome);
        Assert.AreEqual(AntiEntropyOutcome.Converged, resultB.Outcome);
        HashSet<EncodedTriple> union = [.. triplesA];
        union.UnionWith(triplesB);
        Assert.IsTrue(union.SetEquals([.. resultA.ConvergedIndex.EnumerateTriples()]), "Replica A must converge to the union.");
        Assert.IsTrue(union.SetEquals([.. resultB.ConvergedIndex.EnumerateTriples()]), "Replica B must converge to the union.");
    }

    /// <summary>A budget the server cannot serve within one frame (here a deliberately tiny frame) is declined by value: the server writes no response and its loop survives, and the client reads no frame and reconciles as an unavailable peer rather than hanging — the failure path a missing response completion would deadlock. A regression of that hang blocks on the test cancellation token and surfaces at the runner-level hang guard.</summary>
    [TestMethod]
    public async Task ServerDeclinesAnUnserveableBudgetAndTheClientReportsPeerUnavailable()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 30);
        EncodedTriple[] triplesB = Line(20, 30);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);

        //A frame far smaller than any real sketch makes the client's budget unserveable, so the server declines it.
        const int tinyFrameLength = 256;
        Pipe requestPipe = new();
        Pipe responsePipe = new();
        ReplicationIndexFeed feedB = new(triplesB, default);
        using IncrementalSketchMaintainer maintainerB = new(feedB, pool, IncrementalSketchMaintainerOptions.Default, DictionaryEpoch);
        SketchChannelServer server = new(maintainerB, pool, requestPipe.Reader, responsePipe.Writer, DictionaryEpoch, tinyFrameLength);
        SketchChannelClient client = new(requestPipe.Writer, responsePipe.Reader, pool, SketchChannelDomain.Structural, DictionaryEpoch, tinyFrameLength);

        Task serveTask = server.ServeAsync(TestContext.CancellationToken);
        AntiEntropySessionResult result = await AntiEntropySession.ReconcileAsync(replicaA, DictionaryEpoch, client.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await serveTask.ConfigureAwait(false);

        Assert.AreEqual(AntiEntropyOutcome.PeerUnavailable, result.Outcome, "An unserveable budget must decline as peer-unavailable, not hang.");
        Assert.AreSame(replicaA, result.ConvergedIndex, "A declined fetch leaves the local index unchanged.");
    }

    /// <summary>A sketch past the default pause threshold and spanning multiple pipe segments still converges over the channel when the duplex is sized for the frame — exercising the multi-segment frame read and image copy the small-replica tests stay under. The pipes are built with a pause-writer threshold above the frame, the constraint the transport documents.</summary>
    [TestMethod]
    public async Task ALargeSketchConvergesOverAFrameSizedChannel()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);

        //The sketch for 150 items exceeds the default 64 KiB pause threshold; size the pipes above the frame so the
        //whole-frame buffering does not deadlock — the constraint the client and server document.
        PipeOptions frameSized = new(pauseWriterThreshold: 16 * 1024 * 1024, resumeWriterThreshold: 8 * 1024 * 1024);
        Pipe requestPipe = new(frameSized);
        Pipe responsePipe = new(frameSized);
        ReplicationIndexFeed feedB = new(triplesB, default);
        using IncrementalSketchMaintainer maintainerB = new(feedB, pool, IncrementalSketchMaintainerOptions.Default, DictionaryEpoch);
        SketchChannelServer server = new(maintainerB, pool, requestPipe.Reader, responsePipe.Writer, DictionaryEpoch);
        SketchChannelClient client = new(requestPipe.Writer, responsePipe.Reader, pool, SketchChannelDomain.Structural, DictionaryEpoch);

        Task serveTask = server.ServeAsync(TestContext.CancellationToken);
        AntiEntropySessionResult result = await AntiEntropySession.ReconcileAsync(replicaA, DictionaryEpoch, client.FetchAsync, ReplicationPolicy.Default, pool, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await serveTask.ConfigureAwait(false);

        Assert.AreEqual(AntiEntropyOutcome.Converged, result.Outcome, "A frame-sized channel must converge a large sketch.");
        HashSet<EncodedTriple> union = [.. triplesA];
        union.UnionWith(triplesB);
        Assert.IsTrue(union.SetEquals([.. result.ConvergedIndex.EnumerateTriples()]), "Replica A must converge to the union across a multi-segment frame.");
    }

    /// <summary>A transport fault QUEUED BEHIND the captured sketch frame never reaches the fetch: the first frame IS the whole fetch — the liveness contract on a raw duplex transport, where no writer completion propagates and a further read would wait forever — so the client returns the intact owned image without touching the faulting read, and disposing the result returns the one rental.</summary>
    [TestMethod]
    public async Task ATransportFaultBehindTheCapturedFrameNeverReachesTheFetch()
    {
        using PoisoningMemoryPool<byte> pool = new();
        Pipe requestPipe = new();
        Pipe responsePipe = new();

        //Stage exactly one valid stamped response frame, then leave the writer open: the client captures the
        //frame's owned image and RETURNS; the faulting reader would raise only on a further read the one-shot
        //fetch never issues.
        MessageChannelWriter<SketchChannelResponse> stagedResponse = new(responsePipe.Writer, SketchChannelFraming.WriteStampedImage, MessageChannel.DefaultMaxFrameLength);
        byte[] imageBytes = new byte[32];
        Array.Fill(imageBytes, (byte)0x5A);
        await stagedResponse.WriteAsync(new SketchChannelResponse(SketchChannelDomain.Structural, DictionaryEpoch, imageBytes), TestContext.CancellationToken).ConfigureAwait(false);

        SketchChannelClient client = new(requestPipe.Writer, new FaultingAfterBufferedPipeReader(responsePipe.Reader), pool, SketchChannelDomain.Structural, DictionaryEpoch);

        SketchFetchResult result = await client.FetchAsync(64, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(result.HasImage, "The captured frame is the whole fetch.");
        Assert.AreEqual(DictionaryEpoch, result.DictionaryEpoch, "The captured frame carries the peer's stamp.");
        Assert.IsTrue(result.Image.Span.SequenceEqual(imageBytes), "The captured image must be intact — the queued fault never touched it.");
        result.Dispose();
        Assert.AreEqual(0, pool.OutstandingRentals, "Disposing the fetched result returns the one rental.");
    }

    /// <summary>A transport fault BEFORE any frame propagates as itself — cancellation and connection faults are the wire's ordinary failure modes, never laundered into the malformed-input decline type — and no rental leaks, since no frame was ever captured.</summary>
    [TestMethod]
    public async Task ATransportFaultBeforeAnyFramePropagatesAsItself()
    {
        using PoisoningMemoryPool<byte> pool = new();
        Pipe requestPipe = new();
        Pipe responsePipe = new();
        SketchChannelClient client = new(requestPipe.Writer, new FaultingAfterBufferedPipeReader(responsePipe.Reader), pool, SketchChannelDomain.Structural, DictionaryEpoch);

        await Assert.ThrowsAsync<IOException>(async () => await client.FetchAsync(64, TestContext.CancellationToken).ConfigureAwait(false), "The transport fault must propagate as itself, not as the malformed-input decline type.").ConfigureAwait(false);
        Assert.AreEqual(0, pool.OutstandingRentals, "No rental exists to leak on a pre-frame fault.");
    }

    /// <summary>A pipe reader that serves whatever its inner pipe has buffered and raises an <see cref="IOException"/> on the first read that would block — the deterministic stand-in for a connection reset arriving after the buffered frames were consumed.</summary>
    /// <param name="inner">The pipe reader holding the staged bytes.</param>
    private sealed class FaultingAfterBufferedPipeReader(PipeReader inner) : PipeReader
    {
        /// <inheritdoc/>
        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            return inner.TryRead(out ReadResult buffered)
                ? new ValueTask<ReadResult>(buffered)
                : throw new IOException("The connection was reset.");
        }

        /// <inheritdoc/>
        public override bool TryRead(out ReadResult result)
        {
            return inner.TryRead(out result);
        }

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed)
        {
            inner.AdvanceTo(consumed);
        }

        /// <inheritdoc/>
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            inner.AdvanceTo(consumed, examined);
        }

        /// <inheritdoc/>
        public override void CancelPendingRead()
        {
            inner.CancelPendingRead();
        }

        /// <inheritdoc/>
        public override void Complete(Exception? exception = null)
        {
            inner.Complete(exception);
        }
    }
}
