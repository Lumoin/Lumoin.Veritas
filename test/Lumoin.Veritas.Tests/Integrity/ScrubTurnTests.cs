using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Persistence.Sketch;
using Lumoin.Veritas.Core.Reconciliation;
using Microsoft.Extensions.Time.Testing;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The scrub turn: one scrub round shaped as a compute-lane work delegate. Awaited directly (no lane pump, no
/// waits — the lane-cadence integration test defers to the deterministic turn-completion affordance), it
/// brackets the round with began/completed lifecycle events, verifies and repairs the held generation, and
/// hands both reports to the round-result sink, committing nothing. Every event of one round shares its
/// correlation id. Staging and corruption helpers are shared via <see cref="PersistenceStagingFixture"/>, with
/// the artifact-image pool owned by the test and threaded into each builder.
/// </summary>
[TestClass]
internal sealed class ScrubTurnTests
{
    /// <summary>Captures the round's reports through a method group, so the test body holds no closure.</summary>
    private sealed class RoundResultSink
    {
        /// <summary>The verify report the round handed off, if any.</summary>
        public ScrubRoundReport? VerifyReport { get; private set; }

        /// <summary>The repair report the round handed off, if any.</summary>
        public RepairPassReport? RepairReport { get; private set; }

        /// <summary>The number of times the sink was invoked.</summary>
        public int Calls { get; private set; }

        /// <summary>The cancellation token the round forwarded to the sink.</summary>
        public CancellationToken ReceivedToken { get; private set; }

        /// <summary>The round-result handler entry point.</summary>
        /// <param name="verifyReport">The round's verify report.</param>
        /// <param name="repairReport">The round's repair report.</param>
        /// <param name="cancellationToken">The round's cancellation token.</param>
        /// <returns>A completed task.</returns>
        public ValueTask Handle(ScrubRoundReport verifyReport, RepairPassReport repairReport, CancellationToken cancellationToken)
        {
            VerifyReport = verifyReport;
            RepairReport = repairReport;
            ReceivedToken = cancellationToken;
            Calls++;

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A clean round brackets began→completed, hands off clean reports, and commits nothing.</summary>
    [TestMethod]
    public async Task CleanRoundBracketsAndHandsOffCleanReports()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            StorageTraceCapture trace = new();
            RoundResultSink sink = new();
            ScrubTurn turn = new(store, null, RepairConfig(bytePool, triplePool), sink.Handle, trace.Capture, Guid.Empty, new FakeTimeProvider());
            using CancellationTokenSource cts = new();

            await turn.RunAsync(cts.Token).ConfigureAwait(false);

            StorageTraceEvent began = trace.Events[0];
            Assert.AreEqual(StorageTraceEventKind.ScrubRoundBegan, began.Kind, "The round must open with a began marker.");
            //The began marker is round-level: no single artifact, whole-round, generation not yet resolved.
            Assert.AreEqual(0L, began.CommitGeneration);
            Assert.AreEqual(0, began.RoleCode);
            Assert.AreEqual(-1, began.BlockIndex);
            Assert.AreEqual(StorageTraceEventKind.ScrubRoundCompleted, trace.Events[^1].Kind, "The round must close with a completed marker.");
            StorageTraceEvent completed = trace.Events[^1];
            Assert.AreEqual(7, completed.CommitGeneration);
            Assert.AreEqual(0, completed.ItemCount, "A clean round found no corrupt blocks.");
            Assert.AreEqual(1, sink.Calls);
            Assert.AreEqual(cts.Token, sink.ReceivedToken, "The round forwards its cancellation token to the result sink.");
            ScrubRoundReport? verifyReport = sink.VerifyReport;
            Assert.IsNotNull(verifyReport);
            Assert.IsTrue(verifyReport.IsClean);
            RepairPassReport? repairReport = sink.RepairReport;
            Assert.IsNotNull(repairReport);
            Assert.IsFalse(repairReport.Refused);
            Assert.IsTrue(repairReport.IsClean);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A round over a corrupt sidecar re-derives it, narrates the round (began→completed with the corrupt-block count, plus a re-derived event), and hands the repair report to the sink.</summary>
    [TestMethod]
    public async Task CorruptRoundRepairsAndNarrates()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        CorruptSidecarFrontMatter(sidecar);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            StorageTraceCapture trace = new();
            RoundResultSink sink = new();
            ScrubTurn turn = new(store, null, RepairConfig(bytePool, triplePool), sink.Handle, trace.Capture, Guid.Empty, new FakeTimeProvider());

            await turn.RunAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.AreEqual(StorageTraceEventKind.ScrubRoundBegan, trace.Events[0].Kind);
            StorageTraceEvent completed = trace.Events[^1];
            Assert.AreEqual(StorageTraceEventKind.ScrubRoundCompleted, completed.Kind);
            Assert.AreEqual(1, completed.ItemCount, "The completed marker carries the round's corrupt-block count.");
            Assert.ContainsSingle(trace.Events.Where(static e => e.Kind == StorageTraceEventKind.Rederived));
            RepairPassReport? repairReport = sink.RepairReport;
            Assert.IsNotNull(repairReport);
            bool sidecarRederived = repairReport.RederivedArtifacts.Any(static a => a.Role == ManifestFileRole.Sidecar);
            Assert.IsTrue(sidecarRederived);
            ScrubRoundReport? verifyReport = sink.VerifyReport;
            Assert.IsNotNull(verifyReport);
            Assert.IsFalse(verifyReport.IsClean);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Every event of one round — the bracket markers and both passes — shares the round's correlation id.</summary>
    [TestMethod]
    public async Task EveryRoundEventSharesTheCorrelationId()
    {
        Guid roundId = Guid.Parse("0f0f0f0f-0f0f-0f0f-0f0f-0f0f0f0f0f0f");
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        CorruptSidecarFrontMatter(sidecar);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            StorageTraceCapture trace = new();
            RoundResultSink sink = new();
            ScrubTurn turn = new(store, null, RepairConfig(bytePool, triplePool), sink.Handle, trace.Capture, roundId, new FakeTimeProvider());

            await turn.RunAsync(CancellationToken.None).ConfigureAwait(false);

            foreach(StorageTraceEvent evt in trace.Events)
            {
                Assert.AreEqual(roundId, evt.CorrelationId, "Every event of a round shares the round's correlation id.");
            }

            Assert.IsGreaterThan(2, trace.Events.Count, "The round emits at least the bracket plus verify verdicts.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
