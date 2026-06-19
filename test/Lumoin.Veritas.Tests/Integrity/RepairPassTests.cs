using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// The storage repair pass: it consumes a verify report, re-derives each damaged re-derivable artifact (the
/// sidecar, the sketch) from the verified system-of-record, names the item loss of each corrupt
/// system-of-record block at the terminal rung, and emits a trace event per outcome — committing nothing (a
/// generation-agnostic producer). A clean report is a no-op; a degraded snapshot is refused. Everything is
/// synchronous and deterministic, so there are no waits.
/// </summary>
[TestClass]
internal sealed class RepairPassTests
{
    /// <summary>Captures emitted repair events through a method group, so the test body holds no closure.</summary>
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

    /// <summary>A deterministic sketch encoder stub: it fills the symbol bytes by position, so a regenerated sketch is reproducible and block-checksum-valid without depending on the reconciliation library.</summary>
    /// <param name="items">The projected items (unused; the stub does not reconcile).</param>
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

    /// <summary>Rounds up to the next 64-byte boundary.</summary>
    /// <param name="value">The value to align.</param>
    /// <returns>The aligned value.</returns>
    private static int Align(long value)
    {
        return (int)((value + 63) / 64 * 64);
    }

    /// <summary>Flips the first payload byte of the given item-segment block (3 blocks for 30 triples).</summary>
    /// <param name="image">The image to corrupt in place.</param>
    /// <param name="block">The block to corrupt.</param>
    private static void CorruptSegmentBlock(byte[] image, int block)
    {
        int frontMatterEnd = 19 + 12 + (3 * ChecksumAlgorithm.XxHash3.ByteWidth);
        int firstBlock = Align(frontMatterEnd);
        int stride = Align(10L * 12);
        image[firstBlock + (block * stride)] ^= 0xFF;
    }

    /// <summary>Flips the first payload byte of the given sketch block (3 blocks for 10 symbols at 4/block).</summary>
    /// <param name="image">The image to corrupt in place.</param>
    /// <param name="block">The block to corrupt.</param>
    private static void CorruptSketchBlock(byte[] image, int block)
    {
        int frontMatterEnd = 19 + 16 + (3 * ChecksumAlgorithm.XxHash3.ByteWidth);
        int firstBlock = Align(frontMatterEnd);
        int stride = Align(4L * 24);
        image[firstBlock + (block * stride)] ^= 0xFF;
    }

    /// <summary>Flips a byte in the sidecar's front matter (past the 19-byte header, in the scalars/directory the front-matter trailer covers), so the corruption is reliably detected regardless of blob padding.</summary>
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

    /// <summary>The repair configuration for the tests: XxHash3, the structural sketch contract, a small symbol budget, the structural projection, and the deterministic encoder stub.</summary>
    /// <param name="bytePool">The byte pool the sketch re-derive rents from.</param>
    /// <param name="triplePool">The triple pool the feed rents from.</param>
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

    /// <summary>Stages a system-of-record, sidecar, and sketch (each possibly corrupted by the caller) into a fresh temp-dir store and commits a generation manifest naming all three.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <param name="segmentImage">The data-segment image.</param>
    /// <param name="sidecarImage">The sidecar image.</param>
    /// <param name="sketchImage">The sketch image.</param>
    /// <param name="directory">The created temp directory.</param>
    /// <returns>The store.</returns>
    private static FileSystemPersistenceStore StageGeneration(long generation, byte[] segmentImage, byte[] sidecarImage, byte[] sketchImage, out string directory)
    {
        directory = Directory.CreateTempSubdirectory("veritas-repair-").FullName;
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

    /// <summary>A corrupt sidecar is re-derived from the verified system-of-record, the fresh image verifies clean, the outcome is reported clean, and a re-derived event is emitted.</summary>
    [TestMethod]
    public void CorruptSidecarIsRederivedAndVerifiesClean()
    {
        EncodedTriple[] triples = SampleTriples(30);
        byte[] sidecar = SidecarImage(triples);
        CorruptSidecarFrontMatter(sidecar);
        FileSystemPersistenceStore store = StageGeneration(7, SegmentImage(triples), sidecar, SketchImage(10), out string directory);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            bool sidecarDetected = verify.CorruptBlocks.Any(static f => f.RoleCode == ManifestFileRole.Sidecar.Code);
            Assert.IsTrue(sidecarDetected, "Precondition: the sidecar must be detected corrupt.");

            TraceCapture trace = new();
            RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, RepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, new FakeTimeProvider());

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

    /// <summary>A corrupt sketch is re-derived from the system-of-record and the fresh image verifies clean.</summary>
    [TestMethod]
    public void CorruptSketchIsRederivedAndVerifiesClean()
    {
        EncodedTriple[] triples = SampleTriples(30);
        byte[] sketch = SketchImage(10);
        CorruptSketchBlock(sketch, block: 1);
        FileSystemPersistenceStore store = StageGeneration(7, SegmentImage(triples), SidecarImage(triples), sketch, out string directory);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            bool sketchDetected = verify.CorruptBlocks.Any(static f => f.RoleCode == ManifestFileRole.Sketch.Code);
            Assert.IsTrue(sketchDetected, "Precondition: the sketch must be detected corrupt.");

            TraceCapture trace = new();
            RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, RepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, new FakeTimeProvider());

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
    public void CorruptSystemOfRecordBlockIsNamedLoss()
    {
        EncodedTriple[] triples = SampleTriples(30);
        byte[] segment = SegmentImage(triples);
        CorruptSegmentBlock(segment, block: 1);
        FileSystemPersistenceStore store = StageGeneration(7, segment, SidecarImage(triples), SketchImage(10), out string directory);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            bool dataSegmentDetected = verify.CorruptBlocks.Any(static f => f.RoleCode == ManifestFileRole.DataSegment.Code);
            Assert.IsTrue(dataSegmentDetected, "Precondition: the system-of-record must be detected corrupt.");

            TraceCapture trace = new();
            RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, RepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, new FakeTimeProvider());

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
    public void CleanGenerationIsNoOp()
    {
        EncodedTriple[] triples = SampleTriples(30);
        FileSystemPersistenceStore store = StageGeneration(7, SegmentImage(triples), SidecarImage(triples), SketchImage(10), out string directory);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            Assert.IsTrue(verify.IsClean, "Precondition: the staged generation is clean.");

            RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider());

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
    public void DegradedReportIsRefused()
    {
        string directory = Directory.CreateTempSubdirectory("veritas-repair-").FullName;
        FileSystemPersistenceStore store = new(directory, NoOpBarrier);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        try
        {
            ScrubRoundReport degraded = new(commitGeneration: 7, isDegradedSnapshot: true, blocksVerified: 0, corruptBlocks: [new ScrubBlockFinding(ManifestFileRole.Sidecar.Code, "sidecar", 0, 0, 0, IsFrontMatter: false)]);

            RepairPassReport repair = ScrubRound.RunRepairPass(store, degraded, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider());

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
    public void StaleFindingsAreRefused()
    {
        EncodedTriple[] triples = SampleTriples(30);
        FileSystemPersistenceStore store = StageGeneration(7, SegmentImage(triples), SidecarImage(triples), SketchImage(10), out string directory);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        try
        {
            ScrubRoundReport stale = new(commitGeneration: 6, isDegradedSnapshot: false, blocksVerified: 1, corruptBlocks: [new ScrubBlockFinding(ManifestFileRole.Sidecar.Code, "sidecar", 0, 0, 0, IsFrontMatter: false)]);

            RepairPassReport repair = ScrubRound.RunRepairPass(store, stale, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider());

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
    public void SketchOnlyReportWithUnreadableSystemOfRecordIsRefused()
    {
        EncodedTriple[] triples = SampleTriples(30);
        byte[] segment = SegmentImage(triples);
        //Corrupt the magic: framing damage makes ReadVerifiedItems throw, which must become the refusal.
        segment[0] ^= 0xFF;
        FileSystemPersistenceStore store = StageGeneration(7, segment, SidecarImage(triples), SketchImage(10), out string directory);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        try
        {
            ScrubRoundReport sketchOnly = new(commitGeneration: 7, isDegradedSnapshot: false, blocksVerified: 0, corruptBlocks: [new ScrubBlockFinding(ManifestFileRole.Sketch.Code, "sketch", 0, 0, 0, IsFrontMatter: false)]);

            RepairPassReport repair = ScrubRound.RunRepairPass(store, sketchOnly, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider());

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
    public void MissingSystemOfRecordIsRefused()
    {
        EncodedTriple[] triples = SampleTriples(30);
        FileSystemPersistenceStore store = StageGeneration(7, SegmentImage(triples), SidecarImage(triples), SketchImage(10), out string directory);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        try
        {
            store.Delete($"segment-{7:D20}.dat");
            ScrubRoundReport report = new(commitGeneration: 7, isDegradedSnapshot: false, blocksVerified: 0, corruptBlocks: [new ScrubBlockFinding(ManifestFileRole.Sidecar.Code, "sidecar", 0, 0, 0, IsFrontMatter: false)]);

            RepairPassReport repair = ScrubRound.RunRepairPass(store, report, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider());

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
    public void DegradedRecoveryIsRefused()
    {
        EncodedTriple[] triples = SampleTriples(30);
        FileSystemPersistenceStore store = StageGeneration(7, SegmentImage(triples), SidecarImage(triples), SketchImage(10), out string directory);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        try
        {
            //Drop every CURRENT pointer so recovery falls to the degraded direct-manifest scan.
            foreach(string name in store.List("current"))
            {
                store.Delete(name);
            }

            ScrubRoundReport report = new(commitGeneration: 7, isDegradedSnapshot: false, blocksVerified: 0, corruptBlocks: [new ScrubBlockFinding(ManifestFileRole.Sidecar.Code, "sidecar", 0, 0, 0, IsFrontMatter: false)]);

            RepairPassReport repair = ScrubRound.RunRepairPass(store, report, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsTrue(repair.Refused);
            Assert.AreEqual(RepairRefusalReason.DegradedSnapshot, repair.Refusal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
