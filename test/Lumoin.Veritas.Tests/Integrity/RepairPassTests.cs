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
using Lumoin.Veritas.Tests.MemoryPool;
using Microsoft.Extensions.Time.Testing;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The storage repair pass: it consumes a verify report, re-derives each damaged re-derivable artifact (the
/// sidecar, the sketch) from the verified system-of-record, names the item loss of each corrupt
/// system-of-record block at the terminal rung, and emits a trace event per outcome — committing nothing (a
/// generation-agnostic producer). A clean report is a no-op; a degraded snapshot is refused. Everything is
/// synchronous and deterministic, so there are no waits. Staging and corruption helpers are shared via
/// <see cref="PersistenceStagingFixture"/>, and the pool every artifact image is rented from is owned by the
/// test and threaded into each builder.
/// </summary>
[TestClass]
internal sealed class RepairPassTests
{
    /// <summary>A misrouting resolver stand-in for the repair pass: answers the CRC-32 id with XxHash3 and resolves every other id honestly, so the witness violation fires exactly at the artifact staged under CRC-32.</summary>
    /// <param name="id">The requested on-disk id.</param>
    /// <returns>XxHash3 for the CRC-32 id; the honest built-in mapping otherwise.</returns>
    private static ChecksumAlgorithm? MisrouteCrc32ToXxHash3(byte id)
    {
        return id == ChecksumAlgorithm.Crc32.Id ? ChecksumAlgorithm.XxHash3 : ChecksumAlgorithm.DefaultResolver(id);
    }

    /// <summary>A witness violation on the OPTIONAL parity source aborts the repair round loudly instead of declining the source: an unknown id there is legitimate version skew and declines gracefully, but a resolver lying about algorithm identity is a composition defect nothing legitimate produces — and the same resolver serves every artifact — so the round refuses to continue inside the compromised composition.</summary>
    [TestMethod]
    public async Task AWitnessViolationOnTheParitySourceAbortsTheRepairRound()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        using ArtifactImage parity = ParityImage(triples, bytePool, ChecksumAlgorithm.Crc32);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, parity, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            bool segmentDetected = verify.CorruptBlocks.Any(static f => f.RoleCode == ManifestFileRole.DataSegment.Code);
            Assert.IsTrue(segmentDetected, "Precondition: the system-of-record block must be detected corrupt so the repair consults parity.");

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            {
                using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfig(bytePool, triplePool), MisrouteCrc32ToXxHash3, null, Guid.Empty, new FakeTimeProvider(), null, null, CancellationToken.None).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A corrupt sidecar is re-derived from the verified system-of-record, the fresh image verifies clean, the outcome is reported clean, and a re-derived event is emitted.</summary>
    [TestMethod]
    public async Task CorruptSidecarIsRederivedAndVerifiesClean()
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
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            bool sidecarDetected = verify.CorruptBlocks.Any(static f => f.RoleCode == ManifestFileRole.Sidecar.Code);
            Assert.IsTrue(sidecarDetected, "Precondition: the sidecar must be detected corrupt.");

            StorageTraceCapture trace = new();
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, new FakeTimeProvider(), null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsTrue(repair.IsClean, "A re-derivable sidecar corruption is fully recoverable.");
            RederivedArtifact artifact = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.Sidecar);
            Assert.IsTrue(ColumnarTripleIndex.RunVerifyRound(artifact.Image.Span).ToArtifactReport().IsClean, "The re-derived sidecar must verify clean.");
            ColumnarTripleIndex rebuilt = ColumnarIndexFile.Read(new ReadOnlySequence<byte>(artifact.Image), bytePool, triplePool);
            bool faithful = new HashSet<EncodedTriple>(rebuilt.EnumerateTriples()).SetEquals(triples);
            Assert.IsTrue(faithful, "The re-derived sidecar must contain exactly the system-of-record triples (I3 RepairIsFaithful).");
            Assert.IsEmpty(repair.NamedLosses);
            StorageTraceEvent rederivedEvent = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.Rederived);
            Assert.AreEqual(30, rederivedEvent.ItemCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The re-derive path is pool-clean: re-deriving a corrupt sidecar from the verified system-of-record hands the fresh image to the report, and disposing the report returns EVERY buffer the re-derive rented from the repair pool — proven under a poisoning pool (the re-derive-writer pooling gate).</summary>
    [TestMethod]
    public async Task RederivingRepairPassReturnsEveryBufferOnReportDispose()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> imagePool = new();
        using PoisoningMemoryPool<byte> repairPool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, imagePool);
        using ArtifactImage sidecar = SidecarImage(triples, imagePool);
        CorruptSidecarFrontMatter(sidecar);
        using ArtifactImage sketch = SketchImage(10, imagePool);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, imagePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfig(repairPool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), null, null, CancellationToken.None).ConfigureAwait(false);
            try
            {
                Assert.IsTrue(repair.IsClean, "The corrupt sidecar must re-derive.");
                Assert.IsGreaterThan(0, repairPool.OutstandingRentals, "The report must keep the re-derived image rented until it is disposed.");
            }
            finally
            {
                repair.Dispose();
            }

            Assert.AreEqual(0, repairPool.OutstandingRentals, "Disposing the report must return every buffer the re-deriving repair pass rented from the repair pool.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A corrupt sketch is re-derived from the system-of-record and the fresh image verifies clean.</summary>
    [TestMethod]
    public async Task CorruptSketchIsRederivedAndVerifiesClean()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        CorruptSketchBlock(sketch, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            bool sketchDetected = verify.CorruptBlocks.Any(static f => f.RoleCode == ManifestFileRole.Sketch.Code);
            Assert.IsTrue(sketchDetected, "Precondition: the sketch must be detected corrupt.");

            StorageTraceCapture trace = new();
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, new FakeTimeProvider(), null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsTrue(repair.IsClean);
            RederivedArtifact artifact = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.Sketch);
            Assert.IsTrue(SketchSegment.RunVerifyRound(artifact.Image.Span).IsClean, "The re-derived sketch must verify clean.");
            StorageTraceEvent rederivedEvent = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.Rederived);
            Assert.AreEqual(30, rederivedEvent.ItemCount, "The sketch re-derive must fold all 30 verified system-of-record items.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A corrupt system-of-record block is named lost exactly (the block's item range and generation) — the source is not re-derivable — and a named-loss event is emitted.</summary>
    [TestMethod]
    public async Task CorruptSystemOfRecordBlockIsNamedLoss()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            bool dataSegmentDetected = verify.CorruptBlocks.Any(static f => f.RoleCode == ManifestFileRole.DataSegment.Code);
            Assert.IsTrue(dataSegmentDetected, "Precondition: the system-of-record must be detected corrupt.");

            StorageTraceCapture trace = new();
            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, new FakeTimeProvider(), null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsFalse(repair.IsClean, "A named system-of-record loss is not a clean outcome.");
            Assert.IsEmpty(repair.RederivedArtifacts);
            UnrecoverableItemReport loss = repair.NamedLosses.Single();
            Assert.AreEqual(UnrecoverableItemReportKind.ItemSet, loss.Kind);
            Assert.AreEqual(7, loss.CommitGeneration);
            //Block 1 covers items [10, 20).
            Assert.AreEqual(10, loss.LostItemStart);
            Assert.AreEqual(10, loss.LostItemCount);
            StorageTraceEvent named = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.NamedLoss);
            Assert.AreEqual(10, named.ItemCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A clean generation is a no-op: nothing re-derived, nothing lost, not refused.</summary>
    [TestMethod]
    public async Task CleanGenerationIsNoOp()
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
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            Assert.IsTrue(verify.IsClean, "Precondition: the staged generation is clean.");

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, verify, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsFalse(repair.Refused);
            Assert.IsTrue(repair.IsClean);
            Assert.IsEmpty(repair.RederivedArtifacts);
            Assert.IsEmpty(repair.NamedLosses);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A report carrying the degraded-snapshot flag is refused without acting (the report-carried guard, distinct from the in-pass recovery guard) — re-deriving atop a possible torn-publish orphan is unsafe.</summary>
    [TestMethod]
    public async Task DegradedReportIsRefused()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-repair-").FullName;
        FileSystemPersistenceStore store = new(directory, NoOpBarrier);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        try
        {
            ScrubRoundReport degraded = new(commitGeneration: 7, isDegradedSnapshot: true, blocksVerified: 0, corruptBlocks: [new ScrubBlockFinding(ManifestFileRole.Sidecar.Code, "sidecar", 0, 0, 0, IsFrontMatter: false)]);

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, degraded, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsTrue(repair.Refused);
            Assert.AreEqual(RepairRefusalReason.DegradedSnapshot, repair.Refusal);
            Assert.IsEmpty(repair.RederivedArtifacts);
            Assert.IsEmpty(repair.NamedLosses);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A report taken against an older generation than the store now holds is refused as stale, so a repair never re-derives against the wrong generation.</summary>
    [TestMethod]
    public async Task StaleFindingsAreRefused()
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
            ScrubRoundReport stale = new(commitGeneration: 6, isDegradedSnapshot: false, blocksVerified: 1, corruptBlocks: [new ScrubBlockFinding(ManifestFileRole.Sidecar.Code, "sidecar", 0, 0, 0, IsFrontMatter: false)]);

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, stale, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsTrue(repair.Refused);
            Assert.AreEqual(RepairRefusalReason.StaleFindings, repair.Refusal);
            Assert.IsEmpty(repair.RederivedArtifacts);
            Assert.IsEmpty(repair.NamedLosses);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A sketch-only report against a framing-damaged system-of-record is refused as unreadable — the sketch re-derive path cannot bypass the system-of-record refusal (the regression guard for the unified refusal boundary).</summary>
    [TestMethod]
    public async Task SketchOnlyReportWithUnreadableSystemOfRecordIsRefused()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        //Corrupt the magic: framing damage makes ReadVerifiedItems throw, which must become the refusal.
        segment.WritableBytes[0] ^= 0xFF;
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ScrubRoundReport sketchOnly = new(commitGeneration: 7, isDegradedSnapshot: false, blocksVerified: 0, corruptBlocks: [new ScrubBlockFinding(ManifestFileRole.Sketch.Code, "sketch", 0, 0, 0, IsFrontMatter: false)]);

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, sketchOnly, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsTrue(repair.Refused);
            Assert.AreEqual(RepairRefusalReason.SystemOfRecordUnreadable, repair.Refusal);
            Assert.IsEmpty(repair.RederivedArtifacts);
            Assert.IsEmpty(repair.NamedLosses);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A generation whose system-of-record file the store no longer holds is refused as unreadable.</summary>
    [TestMethod]
    public async Task MissingSystemOfRecordIsRefused()
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
            store.Delete($"segment-{7:D20}.dat");
            ScrubRoundReport report = new(commitGeneration: 7, isDegradedSnapshot: false, blocksVerified: 0, corruptBlocks: [new ScrubBlockFinding(ManifestFileRole.Sidecar.Code, "sidecar", 0, 0, 0, IsFrontMatter: false)]);

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, report, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsTrue(repair.Refused);
            Assert.AreEqual(RepairRefusalReason.SystemOfRecordUnreadable, repair.Refusal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A store whose CURRENT pointers are gone recovers only through the degraded direct-manifest scan, and the repair pass refuses against that in-pass degraded recovery (the second degraded guard, distinct from the report-carried one).</summary>
    [TestMethod]
    public async Task DegradedRecoveryIsRefused()
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
            //Drop every CURRENT pointer so recovery falls to the degraded direct-manifest scan.
            foreach(string name in store.List("current"))
            {
                store.Delete(name);
            }

            ScrubRoundReport report = new(commitGeneration: 7, isDegradedSnapshot: false, blocksVerified: 0, corruptBlocks: [new ScrubBlockFinding(ManifestFileRole.Sidecar.Code, "sidecar", 0, 0, 0, IsFrontMatter: false)]);

            using RepairPassReport repair = await ScrubRound.RunRepairPassAsync(store, report, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider(), null, null, CancellationToken.None).ConfigureAwait(false);

            Assert.IsTrue(repair.Refused);
            Assert.AreEqual(RepairRefusalReason.DegradedSnapshot, repair.Refusal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
