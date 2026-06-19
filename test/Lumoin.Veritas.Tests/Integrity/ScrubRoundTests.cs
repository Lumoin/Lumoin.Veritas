using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Persistence.Sketch;
using Microsoft.Extensions.Time.Testing;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The storage scrub's verify pass: it holds a committed manifest snapshot, verifies each artifact the
/// generation names through the format-neutral seam, emits a trace event per verdict, and records every
/// at-rest failure — a clean generation verifies every block; a corrupt system-of-record or sketch block is
/// detected and named with its role; a missing artifact is a whole-artifact loss. Coordination is none — the
/// pass is synchronous and deterministic, so there are no waits.
/// </summary>
[TestClass]
internal sealed class ScrubRoundTests
{
    /// <summary>The item segment's header (19) + scalars (3×4) before the per-block checksum section.</summary>
    private const int ItemFrontMatterBase = 19 + 12;

    /// <summary>The byte size of one item record: subject, predicate, object as three little-endian 32-bit ids.</summary>
    private const int ItemByteSize = 3 * sizeof(uint);

    /// <summary>Captures emitted scrub events through a method group, so the test body holds no closure.</summary>
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

    /// <summary>A line of triples for a system-of-record segment.</summary>
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

    /// <summary>Serializes a sketch image over deterministic opaque symbol bytes (4 symbols/block, 64-byte aligned).</summary>
    /// <param name="symbolCount">The number of symbols.</param>
    /// <returns>The image bytes.</returns>
    private static byte[] SketchImageBytes(int symbolCount)
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

    /// <summary>Flips the first payload byte of the given item-segment block, corrupting exactly that block.</summary>
    /// <param name="image">The image to corrupt in place.</param>
    /// <param name="blockCount">The segment's block count.</param>
    /// <param name="block">The block to corrupt.</param>
    private static void CorruptSegmentBlock(byte[] image, int blockCount, int block)
    {
        int checksumWidth = ChecksumAlgorithm.XxHash3.ByteWidth;
        int frontMatterEnd = ItemFrontMatterBase + (blockCount * checksumWidth);
        int firstBlock = (frontMatterEnd + 63) / 64 * 64;
        long blockStride = ((10L * ItemByteSize) + 63) / 64 * 64;
        image[firstBlock + (int)(block * blockStride)] ^= 0xFF;
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

    /// <summary>Stages a data segment and a sketch into a fresh temp-dir store and commits a generation manifest naming them; returns the store and its directory.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <param name="segmentImage">The data-segment image (possibly corrupted by the caller).</param>
    /// <param name="sketchImage">The sketch image, or <see langword="null"/> to omit staging it (a missing artifact).</param>
    /// <param name="directory">The created temp directory.</param>
    /// <returns>The store.</returns>
    private static FileSystemPersistenceStore StageGeneration(long generation, byte[] segmentImage, byte[]? sketchImage, out string directory)
    {
        directory = Directory.CreateTempSubdirectory("veritas-scrub-").FullName;
        FileSystemPersistenceStore store = new(directory, NoOpBarrier);
        using VeritasMemoryPool<byte> pool = new();

        string segmentName = $"segment-{generation:D20}.dat";
        string sketchName = $"sketch-{generation:D20}.skt";
        store.WriteStaged(segmentName, segmentImage);
        if(sketchImage is not null)
        {
            store.WriteStaged(sketchName, sketchImage);
        }

        ManifestEntry[] entries =
        [
            new(ManifestFileRole.DataSegment, segmentName, 0, segmentImage.Length, FileChecksum(segmentImage)),
            new(ManifestFileRole.Sketch, sketchName, 0, sketchImage?.Length ?? 0, FileChecksum(sketchImage ?? segmentImage)),
        ];
        new ManifestWriter(store, ChecksumAlgorithm.XxHash3, pool, retainedCurrentPointerCount: 4)
            .Commit(new Manifest(generation, generation * 11, generation * 13, entries));

        return store;
    }

    /// <summary>A clean generation verifies every block, reports clean, and emits only verified verdicts.</summary>
    [TestMethod]
    public void CleanGenerationVerifiesEveryBlock()
    {
        FileSystemPersistenceStore store = StageGeneration(7, SegmentImage(SampleTriples(30)), SketchImageBytes(10), out string directory);
        try
        {
            TraceCapture trace = new();
            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, trace.Capture, Guid.Empty, new FakeTimeProvider());

            Assert.IsTrue(report.IsClean, "A clean generation must scrub clean.");
            Assert.AreEqual(7, report.CommitGeneration);
            Assert.IsFalse(report.IsDegradedSnapshot);
            //3 system-of-record blocks (30 triples / 10) + 3 sketch blocks (10 symbols / 4).
            Assert.AreEqual(6, report.BlocksVerified);
            Assert.IsEmpty(report.CorruptBlocks);
            Assert.IsTrue(trace.Events.All(static e => e.Kind == StorageTraceEventKind.BlockVerified), "Every emitted verdict must be a verified block.");
            Assert.HasCount(6, trace.Events);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A corrupt system-of-record block is detected, named with its role and block index, and emitted as a corrupt verdict — while every other block still verifies.</summary>
    [TestMethod]
    public void CorruptSystemOfRecordBlockIsDetectedAndNamed()
    {
        byte[] segment = SegmentImage(SampleTriples(30));
        CorruptSegmentBlock(segment, blockCount: 3, block: 1);
        FileSystemPersistenceStore store = StageGeneration(7, segment, SketchImageBytes(10), out string directory);
        try
        {
            TraceCapture trace = new();
            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, trace.Capture, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(report.IsClean);
            Assert.HasCount(1, report.CorruptBlocks);
            ScrubBlockFinding finding = report.CorruptBlocks[0];
            Assert.AreEqual(ManifestFileRole.DataSegment.Code, finding.RoleCode);
            Assert.AreEqual(1, finding.BlockIndex);
            Assert.IsFalse(finding.IsFrontMatter);
            //The other two system-of-record blocks plus the three sketch blocks still verify.
            Assert.AreEqual(5, report.BlocksVerified);
            Assert.ContainsSingle(trace.Events.Where(static e => e.Kind == StorageTraceEventKind.BlockCorrupt));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A corrupt sketch block is detected and named with the sketch role.</summary>
    [TestMethod]
    public void CorruptSketchBlockIsDetected()
    {
        byte[] sketch = SketchImageBytes(10);
        //Flip the first payload byte of sketch block 1: front matter 19+16 + 3 blocks × 8, aligned to 64; stride Align(4×24,64).
        int frontMatterEnd = 19 + 16 + (3 * ChecksumAlgorithm.XxHash3.ByteWidth);
        int firstBlock = (frontMatterEnd + 63) / 64 * 64;
        long stride = ((4L * 24) + 63) / 64 * 64;
        sketch[firstBlock + (int)stride] ^= 0xFF;

        FileSystemPersistenceStore store = StageGeneration(7, SegmentImage(SampleTriples(30)), sketch, out string directory);
        try
        {
            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(report.IsClean);
            Assert.HasCount(1, report.CorruptBlocks);
            Assert.AreEqual(ManifestFileRole.Sketch.Code, report.CorruptBlocks[0].RoleCode);
            Assert.AreEqual(1, report.CorruptBlocks[0].BlockIndex);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>An artifact the manifest names but the store no longer holds is a whole-artifact loss, named with block index -1.</summary>
    [TestMethod]
    public void MissingArtifactIsNamedAsAWholeArtifactLoss()
    {
        FileSystemPersistenceStore store = StageGeneration(7, SegmentImage(SampleTriples(30)), sketchImage: null, out string directory);
        try
        {
            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(report.IsClean);
            ScrubBlockFinding missing = report.CorruptBlocks.Single(static f => f.RoleCode == ManifestFileRole.Sketch.Code);
            Assert.AreEqual(-1, missing.BlockIndex);
            Assert.IsTrue(missing.IsFrontMatter);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Framing damage (a magic mismatch) makes the artifact unreadable, named as a whole-artifact loss rather than crashing the pass.</summary>
    [TestMethod]
    public void FramingDamageIsNamedAsAWholeArtifactLoss()
    {
        byte[] segment = SegmentImage(SampleTriples(30));
        segment[0] ^= 0xFF;
        FileSystemPersistenceStore store = StageGeneration(7, segment, SketchImageBytes(10), out string directory);
        try
        {
            ScrubRoundReport report = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, new FakeTimeProvider());

            Assert.IsFalse(report.IsClean);
            ScrubBlockFinding framing = report.CorruptBlocks.Single(static f => f.RoleCode == ManifestFileRole.DataSegment.Code);
            Assert.AreEqual(-1, framing.BlockIndex);
            Assert.IsTrue(framing.IsFrontMatter);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
