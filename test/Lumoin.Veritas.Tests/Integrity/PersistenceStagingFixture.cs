using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
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

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// Captures emitted storage trace events through a method group, so a test body holds no closure. Shared by
/// the storage self-heal tests (verify, repair, scrub-turn, and the combination matrix).
/// </summary>
internal sealed class StorageTraceCapture
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

/// <summary>
/// Shared staging and corruption helpers for the storage self-heal tests: builds a committed generation
/// (system-of-record, columnar sidecar, integrity sketch, and its manifest) in a temp-dir store, and the
/// in-place byte corruptions the fault tests inject. One copy the verify, repair, scrub-turn, and combination-
/// matrix tests all build on, so the fixture geometry (10-item blocks, 24-byte sketch symbols, XxHash3) stays
/// single-sourced.
/// </summary>
internal static class PersistenceStagingFixture
{
    /// <summary>A deterministic sketch encoder stub: fills the symbol bytes by position so a regenerated sketch is reproducible and block-checksum-valid without depending on the reconciliation library.</summary>
    /// <param name="items">The projected items (unused; the stub does not reconcile).</param>
    /// <param name="symbolCount">The symbol count (unused).</param>
    /// <param name="symbolWidth">The symbol width (unused).</param>
    /// <param name="destination">The symbol-byte buffer to fill.</param>
    internal static void FillSymbols(ReadOnlySpan<ContentKey128> items, int symbolCount, int symbolWidth, Span<byte> destination)
    {
        for(int i = 0; i < destination.Length; i++)
        {
            destination[i] = (byte)((i * 37) + 11);
        }
    }

    /// <summary>A line of triples for a generation.</summary>
    /// <param name="count">The triple count.</param>
    /// <returns>The triples.</returns>
    internal static EncodedTriple[] SampleTriples(uint count)
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
    internal static byte[] SegmentImage(EncodedTriple[] triples)
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
    internal static byte[] SidecarImage(EncodedTriple[] triples)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);
        ArrayBufferWriter<byte> writer = new();
        ColumnarIndexFile.Write(index, writer, ChecksumAlgorithm.XxHash3);

        return writer.WrittenMemory.ToArray();
    }

    /// <summary>Serializes a sketch image over deterministic opaque symbol bytes (4 symbols/block, 64-byte aligned).</summary>
    /// <param name="symbolCount">The number of symbols.</param>
    /// <returns>The image bytes.</returns>
    internal static byte[] SketchImage(int symbolCount)
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
    internal static int Align(long value)
    {
        return (int)((value + 63) / 64 * 64);
    }

    /// <summary>Flips the first payload byte of the given item-segment block.</summary>
    /// <param name="image">The image to corrupt in place.</param>
    /// <param name="block">The block to corrupt.</param>
    /// <param name="blockCount">The segment's block count (sizes the per-block checksum section).</param>
    internal static void CorruptSegmentBlock(byte[] image, int block, int blockCount)
    {
        int frontMatterEnd = 19 + 12 + (blockCount * ChecksumAlgorithm.XxHash3.ByteWidth);
        int firstBlock = Align(frontMatterEnd);
        int stride = Align(10L * 12);
        image[firstBlock + (block * stride)] ^= 0xFF;
    }

    /// <summary>Flips the first payload byte of the given sketch block.</summary>
    /// <param name="image">The image to corrupt in place.</param>
    /// <param name="block">The block to corrupt.</param>
    /// <param name="blockCount">The sketch's block count (sizes the per-block checksum section).</param>
    internal static void CorruptSketchBlock(byte[] image, int block, int blockCount)
    {
        int frontMatterEnd = 19 + 16 + (blockCount * ChecksumAlgorithm.XxHash3.ByteWidth);
        int firstBlock = Align(frontMatterEnd);
        int stride = Align(4L * 24);
        image[firstBlock + (block * stride)] ^= 0xFF;
    }

    /// <summary>Flips a byte in the sidecar's front matter (past the 19-byte header, in the scalars/directory the front-matter trailer covers), so the corruption is reliably detected regardless of blob padding.</summary>
    /// <param name="image">The image to corrupt in place.</param>
    internal static void CorruptSidecarFrontMatter(byte[] image)
    {
        image[30] ^= 0xFF;
    }

    /// <summary>A directory durability barrier that does nothing.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    internal static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>Computes an artifact's whole-image manifest-entry checksum.</summary>
    /// <param name="image">The artifact bytes.</param>
    /// <returns>The digest.</returns>
    internal static byte[] FileChecksum(byte[] image)
    {
        byte[] digest = new byte[ChecksumAlgorithm.XxHash3.ByteWidth];
        ChecksumAlgorithm.XxHash3.Compute(image, digest);

        return digest;
    }

    /// <summary>The repair configuration for the tests: XxHash3, the structural sketch contract, a small symbol budget, the structural projection, and the deterministic encoder stub.</summary>
    /// <param name="bytePool">The byte pool the sketch re-derive rents from.</param>
    /// <param name="triplePool">The triple pool the feed rents from.</param>
    /// <returns>The configuration.</returns>
    internal static RepairConfiguration RepairConfig(MemoryPool<byte> bytePool, MemoryPool<EncodedTriple> triplePool)
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
    internal static FileSystemPersistenceStore StageGeneration(long generation, byte[] segmentImage, byte[] sidecarImage, byte[] sketchImage, out string directory)
    {
        directory = Directory.CreateTempSubdirectory("veritas-staging-").FullName;
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
}
