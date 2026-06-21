using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Parity;
using Lumoin.Veritas.Core.Persistence.Segment;
using Microsoft.Extensions.Time.Testing;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The local-parity repair rung: <see cref="ItemSegment.TryRestoreBlockFromParity"/> restores a corrupt
/// system-of-record block as the parity XORed with the surviving blocks and self-checks the result against the
/// block's stored checksum (a stale, wrong-geometry, wrong-count, or unverifiable parity is refused), and a
/// repair pass over a generation that carries a verifying parity sidecar restores a single lost block, re-ingests
/// the healed system-of-record, and re-derives any co-damaged view from the healed full item set — while more
/// than one lost block, a corrupt parity, a clean-but-stale parity, or no parity descends to a named loss.
/// Geometry is the fixture's (10-item blocks, 64-byte aligned, XxHash3); every buffer is pool-rented.
/// </summary>
[TestClass]
internal sealed class LocalParityRepairTests
{
    /// <summary>The fixture's items-per-block.</summary>
    private const int BlockItemCount = 10;

    /// <summary>The fixture's block alignment.</summary>
    private const int BlockAlignment = 64;

    /// <summary>Restoring each block of a 25-item segment from its parity reproduces the original image byte for byte, including the short last block.</summary>
    [TestMethod]
    public void RestoringEachBlockReproducesTheOriginalImage()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triples = SampleTriples(25);
        using ArtifactImage original = SegmentImage(triples, pool);
        using ParityBlock parity = BuildParityBlock(triples, pool);
        int blockCount = new ItemSegment(triples, BlockItemCount, BlockAlignment).BlockCount;

        for(int lostBlock = 0; lostBlock < blockCount; lostBlock++)
        {
            using ArtifactImage corrupt = SegmentImage(triples, pool);
            CorruptSegmentBlock(corrupt, lostBlock, blockCount);

            byte[]? repaired = ItemSegment.TryRestoreBlockFromParity(corrupt.Bytes, lostBlock, parity.Span, blockCount, pool);

            Assert.IsNotNull(repaired, $"block {lostBlock} must restore.");
            Assert.IsTrue(repaired.AsSpan().SequenceEqual(original.Bytes), $"block {lostBlock} restore must reproduce the original image byte for byte.");
        }
    }

    /// <summary>A parity folded over a different block-0 than the segment being repaired fails the restore's self-check — even though its block count matches — and yields null rather than a wrong block.</summary>
    [TestMethod]
    public void RestoringWithAStaleParityRefuses()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triples = SampleTriples(25);

        EncodedTriple[] stale = SampleTriples(25);
        stale[5] = EncodedTriple.FromEncoded(999, 999, 999);
        using ParityBlock staleParity = BuildParityBlock(stale, pool);

        using ArtifactImage corrupt = SegmentImage(triples, pool);
        CorruptSegmentBlock(corrupt, block: 1, blockCount: 3);

        byte[]? repaired = ItemSegment.TryRestoreBlockFromParity(corrupt.Bytes, 1, staleParity.Span, 3, pool);
        Assert.IsNull(repaired, "A stale parity must fail the restore's self-check.");
    }

    /// <summary>A parity that is not the block stride wide cannot restore the segment and yields null.</summary>
    [TestMethod]
    public void RestoringWithAMismatchedParityWidthRefuses()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triples = SampleTriples(25);
        using ArtifactImage corrupt = SegmentImage(triples, pool);
        CorruptSegmentBlock(corrupt, block: 1, blockCount: 3);
        using ParityBlock wrongWidth = ParityBlock.Rent(pool, 7);

        byte[]? repaired = ItemSegment.TryRestoreBlockFromParity(corrupt.Bytes, 1, wrongWidth.Span, 3, pool);
        Assert.IsNull(repaired, "A parity that is not the block stride cannot restore this segment.");
    }

    /// <summary>A parity whose protected block count does not match the segment's block count is a co-version mismatch and yields null.</summary>
    [TestMethod]
    public void RestoringWithAMismatchedProtectedBlockCountRefuses()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triples = SampleTriples(25);
        using ArtifactImage corrupt = SegmentImage(triples, pool);
        CorruptSegmentBlock(corrupt, block: 1, blockCount: 3);
        using ParityBlock parity = BuildParityBlock(triples, pool);

        byte[]? repaired = ItemSegment.TryRestoreBlockFromParity(corrupt.Bytes, 1, parity.Span, 2, pool);
        Assert.IsNull(repaired, "A parity built over a different block count must decline as a co-version mismatch.");
    }

    /// <summary>A checksum-free image cannot self-verify a restore, so the restore is declined rather than returned unverified.</summary>
    [TestMethod]
    public void RestoringAChecksumFreeImageRefuses()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triples = SampleTriples(25);
        ItemSegment segment = new(triples, BlockItemCount, BlockAlignment);
        int size = (int)segment.ComputeSerializedSize(null);
        IMemoryOwner<byte> owner = pool.Rent(size);
        segment.WriteTo(owner.Memory.Span[..size], null);
        using ArtifactImage image = ArtifactImage.Own(owner, size, ManifestFileRole.DataSegment);
        using ParityBlock parity = BuildParityBlock(triples, pool);

        byte[]? repaired = ItemSegment.TryRestoreBlockFromParity(image.Bytes, 1, parity.Span, 3, pool);
        Assert.IsNull(repaired, "A checksum-free image cannot self-verify a restore and is declined.");
    }

    /// <summary>A block index outside the segment's blocks is a contract violation and throws.</summary>
    [TestMethod]
    public void RestoringAnOutOfRangeBlockThrows()
    {
        using VeritasMemoryPool<byte> pool = new();
        EncodedTriple[] triples = SampleTriples(25);
        using ArtifactImage image = SegmentImage(triples, pool);
        using ParityBlock parity = BuildParityBlock(triples, pool);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ItemSegment.TryRestoreBlockFromParity(image.Bytes, 3, parity.Span, 3, pool));
    }

    /// <summary>A repair pass over a generation carrying a verifying parity restores the single lost system-of-record block, re-ingests the healed image, and recovers exactly the original triples.</summary>
    [TestMethod]
    public void LocalParityRestoresACorruptSystemOfRecordBlock()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        using ArtifactImage parity = ParityImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, parity, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            bool dataSegmentDetected = verify.CorruptBlocks.Any(static f => f.RoleCode == ManifestFileRole.DataSegment.Code);
            Assert.IsTrue(dataSegmentDetected, "Precondition: the system-of-record block must be detected corrupt.");

            StorageTraceCapture trace = new();
            RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, RepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(repair.Refused);
            Assert.IsTrue(repair.IsClean, "A parity-restorable system-of-record block is fully recoverable.");
            Assert.IsEmpty(repair.NamedLosses);
            RederivedArtifact restored = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.DataSegment);
            Assert.IsTrue(ItemSegment.RunVerifyRound(restored.Image.Span).IsClean, "The restored system-of-record must verify clean.");
            using DecodedItemSegment recovered = ItemSegment.ReadFrom(restored.Image.Span, triplePool);
            bool faithful = recovered.Items.Span.SequenceEqual(triples);
            Assert.IsTrue(faithful, "The restored system-of-record must hold exactly the original triples (a faithful recovery).");
            StorageTraceEvent reingested = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.Reingested);
            Assert.AreEqual(ManifestFileRole.DataSegment.Code, reingested.RoleCode);
            Assert.AreEqual(30, reingested.ItemCount, "The healed system-of-record holds all 30 items.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A repair pass restores the SHORT last block from parity and re-ingests the full item set.</summary>
    [TestMethod]
    public void LocalParityRestoresTheShortLastBlock()
    {
        EncodedTriple[] triples = SampleTriples(25);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        using ArtifactImage parity = ParityImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, parity, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            StorageTraceCapture trace = new();
            RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, RepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, new FakeTimeProvider());

            Assert.IsTrue(repair.IsClean);
            RederivedArtifact restored = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.DataSegment);
            using DecodedItemSegment recovered = ItemSegment.ReadFrom(restored.Image.Span, triplePool);
            bool faithful = recovered.Items.Span.SequenceEqual(triples);
            Assert.IsTrue(faithful, "The restored short-last-block segment must hold all 25 original triples.");
            StorageTraceEvent reingested = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.Reingested);
            Assert.AreEqual(25, reingested.ItemCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>When a parity restore heals a lost system-of-record block AND a view is damaged in the same pass, the re-derived view folds the healed full item set, so the published system-of-record and view stay consistent.</summary>
    [TestMethod]
    public void RestoredSystemOfRecordAndCoDamagedViewStayConsistent()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        using ArtifactImage parity = ParityImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        CorruptSidecarFrontMatter(sidecar);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, parity, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsTrue(repair.IsClean);
            Assert.IsEmpty(repair.NamedLosses);
            //The system-of-record is restored to all 30 items, and the co-damaged sidecar is re-derived from
            //that healed set — not the block-excluded feed — so both index exactly the same triples.
            RederivedArtifact restoredSegment = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.DataSegment);
            using DecodedItemSegment recovered = ItemSegment.ReadFrom(restoredSegment.Image.Span, triplePool);
            bool segmentFaithful = recovered.Items.Span.SequenceEqual(triples);
            Assert.IsTrue(segmentFaithful, "The restored system-of-record must hold all 30 triples.");

            RederivedArtifact rederivedSidecar = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.Sidecar);
            ColumnarTripleIndex rebuilt = ColumnarIndexFile.Read(new ReadOnlySequence<byte>(rederivedSidecar.Image), bytePool, triplePool);
            bool sidecarFaithful = new HashSet<EncodedTriple>(rebuilt.EnumerateTriples()).SetEquals(triples);
            Assert.IsTrue(sidecarFaithful, "The co-damaged sidecar must be re-derived from the restored full item set, not the pruned feed.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A capacity-1 parity cannot restore two lost blocks, so both are named lost and nothing is re-derived.</summary>
    [TestMethod]
    public void LocalParityDeclinesWhenMoreThanOneBlockIsLost()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        using ArtifactImage parity = ParityImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 0, blockCount: 3);
        CorruptSegmentBlock(segment, block: 2, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, parity, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(repair.Refused);
            Assert.IsFalse(repair.IsClean, "A capacity-1 parity cannot restore two lost blocks.");
            Assert.IsEmpty(repair.RederivedArtifacts);
            Assert.HasCount(2, repair.NamedLosses);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A parity that does not itself verify clean at rest is no source, so the lost block is named, not restored.</summary>
    [TestMethod]
    public void LocalParityDeclinesWhenTheParityIsItselfCorrupt()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        using ArtifactImage parity = ParityImage(triples, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        CorruptParityBlock(parity);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, parity, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(repair.IsClean);
            Assert.IsEmpty(repair.RederivedArtifacts);
            UnrecoverableItemReport loss = repair.NamedLosses.Single();
            Assert.AreEqual(10, loss.LostItemCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A parity that verifies clean at rest but was folded over a different item set is rejected by the restore's own self-check (not the upstream parity verify), so the lost block is named, not restored.</summary>
    [TestMethod]
    public void LocalParityDeclinesWhenTheParityIsCleanButStale()
    {
        EncodedTriple[] triples = SampleTriples(30);
        EncodedTriple[] stale = SampleTriples(30);
        stale[0] = EncodedTriple.FromEncoded(777, 777, 777);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        using ArtifactImage parity = ParityImage(stale, bytePool);
        CorruptSegmentBlock(segment, block: 1, blockCount: 3);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, parity, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, RepairConfig(bytePool, triplePool), null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(repair.IsClean);
            Assert.IsEmpty(repair.RederivedArtifacts);
            UnrecoverableItemReport loss = repair.NamedLosses.Single();
            Assert.AreEqual(10, loss.LostItemCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A corrupt parity sidecar is re-derived from the verified system-of-record — it is re-derivable like a view — and the regenerated parity verifies clean and equals the parity over the system-of-record.</summary>
    [TestMethod]
    public void CorruptParityIsRederivedFromTheSystemOfRecord()
    {
        EncodedTriple[] triples = SampleTriples(30);
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(10, bytePool);
        using ArtifactImage parity = ParityImage(triples, bytePool);
        CorruptParityBlock(parity);
        FileSystemPersistenceStore store = StageGeneration(7, segment, sidecar, sketch, parity, bytePool, out string directory);
        try
        {
            ScrubRoundReport verify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());
            bool parityDetected = verify.CorruptBlocks.Any(static f => f.RoleCode == ManifestFileRole.Parity.Code);
            Assert.IsTrue(parityDetected, "Precondition: the parity must be detected corrupt.");

            StorageTraceCapture trace = new();
            RepairPassReport repair = ScrubRound.RunRepairPass(store, verify, RepairConfig(bytePool, triplePool), null, trace.Capture, Guid.Empty, new FakeTimeProvider());

            Assert.IsTrue(repair.IsClean, "A re-derivable parity corruption is fully recoverable.");
            Assert.IsEmpty(repair.NamedLosses);
            RederivedArtifact rederived = repair.RederivedArtifacts.Single(static a => a.Role == ManifestFileRole.Parity);
            Assert.IsTrue(ParitySegment.RunVerifyRound(rederived.Image.Span).IsClean, "The re-derived parity must verify clean.");

            //The re-derived parity equals a fresh parity folded over the same system-of-record — the differential twin.
            using ParityBlock rederivedParity = ParitySegment.ReadFrom(rederived.Image.Span, bytePool);
            using ParityBlock reference = BuildParityBlock(triples, bytePool);
            bool faithful = rederivedParity.Span.SequenceEqual(reference.Span);
            Assert.IsTrue(faithful, "The re-derived parity must equal the parity over the system-of-record.");
            StorageTraceEvent rederivedEvent = trace.Events.Single(static e => e.Kind == StorageTraceEventKind.Rederived);
            Assert.AreEqual(ManifestFileRole.Parity.Code, rederivedEvent.RoleCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Builds the capacity-1 parity block over the triples' system-of-record blocks into a pooled block the caller disposes.</summary>
    /// <param name="triples">The triples whose system-of-record blocks the parity protects.</param>
    /// <param name="pool">The pool the parity and a transient scratch are rented from.</param>
    /// <returns>The pooled parity block.</returns>
    private static ParityBlock BuildParityBlock(EncodedTriple[] triples, MemoryPool<byte> pool)
    {
        ItemSegment systemOfRecord = new(triples, BlockItemCount, BlockAlignment);
        ParityBlock parity = ParityBlock.Rent(pool, systemOfRecord.MaxBlockPayloadByteCount);
        ParitySegment.BuildParity(systemOfRecord, parity.WritableSpan, pool);

        return parity;
    }
}
