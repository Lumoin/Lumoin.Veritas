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

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The scrub turn: one scrub round shaped as a compute-lane work delegate. Awaited directly (no lane pump, no
/// waits — the lane-cadence integration test defers to the deterministic turn-completion affordance), it
/// brackets the round with began/completed lifecycle events, verifies and repairs the held generation, and
/// hands both reports to the round-result sink, committing nothing. Every event of one round shares its
/// correlation id.
/// </summary>
[TestClass]
internal sealed class ScrubTurnTests
{
    /// <summary>Captures emitted events through a method group, so the test body holds no closure.</summary>
    private sealed class TraceCapture
    {
        /// <summary>The events captured, in emission order.</summary>
        public List<StorageTraceEvent> Events { get; } = [];

        /// <summary>The handler entry point.</summary>
        /// <param name="evt">The emitted event.</param>
        public void Capture(in StorageTraceEvent evt)
        {
            Events.Add(evt);
        }
    }

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

    /// <summary>A deterministic sketch encoder stub: fills the symbol bytes by position so a regenerated sketch is block-checksum-valid without the reconciliation library.</summary>
    /// <param name="items">The projected items (unused).</param>
    /// <param name="symbolCount">The symbol count (unused).</param>
    /// <param name="symbolWidth">The symbol width (unused).</param>
    /// <param name="destination">The symbol-byte buffer to fill.</param>
    private static void FillSymbols(ReadOnlySpan<ContentKey128> items, int symbolCount, int symbolWidth, Span<byte> destination)
    {
        for(int i = 0; i < destination.Length; i++)
        {
            destination[i] = (byte)((i * 37) + 11);
        }
    }

    /// <summary>A line of triples for a generation.</summary>
    /// <param name="count">The triple count.</param>
    /// <returns>The triples.</returns>
    private static EncodedTriple[] SampleTriples(uint count)
    {
        EncodedTriple[] triples = new EncodedTriple[count];
        for(uint i = 0; i < count; i++)
        {
            triples[i] = EncodedTriple.FromEncoded(i, (i * 7) + 1, (i * 13) + 2);
        }

        return triples;
    }

    /// <summary>Serializes a system-of-record item-segment image (10-item blocks, 64-byte aligned).</summary>
    /// <param name="triples">The triples.</param>
    /// <returns>The image bytes.</returns>
    private static byte[] SegmentImage(EncodedTriple[] triples)
    {
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        using VeritasMemoryPool<byte> pool = new();
        int size = (int)segment.ComputeSerializedSize(ChecksumAlgorithm.XxHash3);
        using IMemoryOwner<byte> owner = pool.Rent(size);
        Span<byte> image = owner.Memory.Span[..size];
        segment.WriteTo(image, ChecksumAlgorithm.XxHash3);

        return image.ToArray();
    }

    /// <summary>Serializes a columnar sidecar image over the same triples.</summary>
    /// <param name="triples">The triples.</param>
    /// <returns>The image bytes.</returns>
    private static byte[] SidecarImage(EncodedTriple[] triples)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);
        ArrayBufferWriter<byte> writer = new();
        ColumnarIndexFile.Write(index, writer, ChecksumAlgorithm.XxHash3);

        return writer.WrittenMemory.ToArray();
    }

    /// <summary>Serializes a sketch image over deterministic opaque symbol bytes (4 symbols/block, 64-byte aligned).</summary>
    /// <param name="symbolCount">The number of symbols.</param>
    /// <returns>The image bytes.</returns>
    private static byte[] SketchImage(int symbolCount)
    {
        byte[] symbols = new byte[symbolCount * 24];
        for(int i = 0; i < symbols.Length; i++)
        {
            symbols[i] = (byte)((i * 31) + 7);
        }

        SketchSegment segment = new(symbols, symbolWidth: 24, symbolsPerBlock: 4, blockAlignment: 64);
        using VeritasMemoryPool<byte> pool = new();
        int size = (int)segment.ComputeSerializedSize(ChecksumAlgorithm.XxHash3);
        using IMemoryOwner<byte> owner = pool.Rent(size);
        Span<byte> image = owner.Memory.Span[..size];
        segment.WriteTo(image, ChecksumAlgorithm.XxHash3);

        return image.ToArray();
    }

    /// <summary>Flips a byte in the sidecar's front matter so the corruption is reliably detected.</summary>
    /// <param name="image">The image to corrupt in place.</param>
    private static void CorruptSidecarFrontMatter(byte[] image)
    {
        image[30] ^= 0xFF;
    }

    /// <summary>A directory durability barrier that does nothing.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>Computes an artifact's whole-image manifest-entry checksum.</summary>
    /// <param name="image">The artifact bytes.</param>
    /// <returns>The digest.</returns>
    private static byte[] FileChecksum(byte[] image)
    {
        byte[] digest = new byte[ChecksumAlgorithm.XxHash3.ByteWidth];
        ChecksumAlgorithm.XxHash3.Compute(image, digest);

        return digest;
    }

    /// <summary>The repair configuration for the tests.</summary>
    /// <param name="bytePool">The byte pool.</param>
    /// <param name="triplePool">The triple pool.</param>
    /// <returns>The configuration.</returns>
    private static RepairConfiguration RepairConfig(MemoryPool<byte> bytePool, MemoryPool<EncodedTriple> triplePool)
    {
        return new RepairConfiguration(
            ChecksumAlgorithm.XxHash3,
            bytePool,
            triplePool,
            SketchContract.Structural,
            symbolBudget: 16,
            StructuralReconciliationProjection.Projection,
            FillSymbols);
    }

    /// <summary>Stages a generation (system-of-record, sidecar, sketch — each possibly corrupted) and commits its manifest.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <param name="segmentImage">The data-segment image.</param>
    /// <param name="sidecarImage">The sidecar image.</param>
    /// <param name="sketchImage">The sketch image.</param>
    /// <param name="directory">The created temp directory.</param>
    /// <returns>The store.</returns>
    private static FileSystemPersistenceStore StageGeneration(long generation, byte[] segmentImage, byte[] sidecarImage, byte[] sketchImage, out string directory)
    {
        directory = Directory.CreateTempSubdirectory("veritas-scrubturn-").FullName;
        FileSystemPersistenceStore store = new(directory, NoOpBarrier);
        using VeritasMemoryPool<byte> pool = new();

        string segmentName = $"segment-{generation:D20}.dat";
        string sidecarName = $"sidecar-{generation:D20}.idx";
        string sketchName = $"sketch-{generation:D20}.skt";
        store.WriteStaged(segmentName, segmentImage);
        store.WriteStaged(sidecarName, sidecarImage);
        store.WriteStaged(sketchName, sketchImage);

        ManifestEntry[] entries =
        [
            new(ManifestFileRole.DataSegment, segmentName, 0, segmentImage.Length, FileChecksum(segmentImage)),
            new(ManifestFileRole.Sidecar, sidecarName, 0, sidecarImage.Length, FileChecksum(sidecarImage)),
            new(ManifestFileRole.Sketch, sketchName, 0, sketchImage.Length, FileChecksum(sketchImage)),
        ];
        new ManifestWriter(store, ChecksumAlgorithm.XxHash3, pool, retainedCurrentPointerCount: 4)
            .Commit(new Manifest(generation, generation * 11, generation * 13, entries));

        return store;
    }

    /// <summary>A clean round brackets began→completed, hands off clean reports, and commits nothing.</summary>
    [TestMethod]
    public async Task CleanRoundBracketsAndHandsOffCleanReports()
    {
        EncodedTriple[] triples = SampleTriples(30);
        FileSystemPersistenceStore store = StageGeneration(7, SegmentImage(triples), SidecarImage(triples), SketchImage(10), out string directory);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        try
        {
            TraceCapture trace = new();
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
        byte[] sidecar = SidecarImage(triples);
        CorruptSidecarFrontMatter(sidecar);
        FileSystemPersistenceStore store = StageGeneration(7, SegmentImage(triples), sidecar, SketchImage(10), out string directory);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        try
        {
            TraceCapture trace = new();
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
        byte[] sidecar = SidecarImage(triples);
        CorruptSidecarFrontMatter(sidecar);
        FileSystemPersistenceStore store = StageGeneration(7, SegmentImage(triples), sidecar, SketchImage(10), out string directory);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        try
        {
            TraceCapture trace = new();
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
