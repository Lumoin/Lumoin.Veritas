using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Integrity;
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
/// in-place corruptions the fault tests inject. Every serialized artifact is an <see cref="ArtifactImage"/>
/// holding pooled, owned bytes rather than a loose array, and every helper that rents takes the caller's pool
/// rather than a shared singleton — so a test owns and disposes every buffer it allocates and the verify,
/// repair, scrub-turn, and combination-matrix tests share one pooled-image currency and one fixture geometry
/// (10-item blocks, 24-byte sketch symbols, XxHash3).
/// </summary>
internal static class PersistenceStagingFixture
{
    /// <summary>The header byte offset of the checksum-algorithm id, uniform across the three block-framed formats: magic (8) + version major (1) + version minor (1) + feature mask (8).</summary>
    internal const int ChecksumAlgorithmIdOffset = 8 + 1 + 1 + 8;

    /// <summary>A checksum-algorithm id no built-in resolver maps, so a reader refuses an image stamped with it as a foreign epoch rather than verifying it under the wrong algorithm.</summary>
    internal const byte ForeignChecksumAlgorithmId = 99;

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

    /// <summary>Serializes a system-of-record item-segment image (10-item blocks, 64-byte aligned) into a buffer rented from <paramref name="pool"/>.</summary>
    /// <param name="triples">The triples.</param>
    /// <param name="pool">The pool the image buffer is rented from.</param>
    /// <returns>The pooled image.</returns>
    internal static ArtifactImage SegmentImage(EncodedTriple[] triples, MemoryPool<byte> pool)
    {
        return SegmentImage(triples, pool, ChecksumAlgorithm.XxHash3);
    }

    /// <summary>Serializes a system-of-record item-segment image under an explicit checksum algorithm, so a test can stage it under a degenerate algorithm to prove the at-rest detection is non-vacuous.</summary>
    /// <param name="triples">The triples.</param>
    /// <param name="pool">The pool the image buffer is rented from.</param>
    /// <param name="checksum">The checksum algorithm the front matter and per-block digests are written under.</param>
    /// <returns>The pooled image.</returns>
    internal static ArtifactImage SegmentImage(EncodedTriple[] triples, MemoryPool<byte> pool, ChecksumAlgorithm checksum)
    {
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        int size = (int)segment.ComputeSerializedSize(checksum);
        IMemoryOwner<byte> owner = pool.Rent(size);
        segment.WriteTo(owner.Memory.Span[..size], checksum);

        return ArtifactImage.Own(owner, size, ManifestFileRole.DataSegment);
    }

    /// <summary>Serializes a columnar sidecar image over the same triples into a buffer rented from <paramref name="pool"/>.</summary>
    /// <param name="triples">The triples.</param>
    /// <param name="pool">The pool the image buffer is rented from.</param>
    /// <returns>The pooled image.</returns>
    internal static ArtifactImage SidecarImage(EncodedTriple[] triples, MemoryPool<byte> pool)
    {
        return SidecarImage(triples, pool, ChecksumAlgorithm.XxHash3);
    }

    /// <summary>Serializes a columnar sidecar image under an explicit checksum algorithm, so a test can stage it under a degenerate algorithm to prove the at-rest detection is non-vacuous.</summary>
    /// <param name="triples">The triples.</param>
    /// <param name="pool">The pool the image buffer is rented from.</param>
    /// <param name="checksum">The checksum algorithm the front matter and per-blob digests are written under.</param>
    /// <returns>The pooled image.</returns>
    internal static ArtifactImage SidecarImage(EncodedTriple[] triples, MemoryPool<byte> pool, ChecksumAlgorithm checksum)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);
        ArrayBufferWriter<byte> writer = new();
        ColumnarIndexFile.Write(index, writer, checksum);

        return ArtifactImage.Copy(writer.WrittenSpan, ManifestFileRole.Sidecar, pool);
    }

    /// <summary>Serializes a sketch image over deterministic opaque symbol bytes (4 symbols/block, 64-byte aligned) into a buffer rented from <paramref name="pool"/>.</summary>
    /// <param name="symbolCount">The number of symbols.</param>
    /// <param name="pool">The pool the symbol and image buffers are rented from.</param>
    /// <returns>The pooled image.</returns>
    internal static ArtifactImage SketchImage(int symbolCount, MemoryPool<byte> pool)
    {
        return SketchImage(symbolCount, pool, ChecksumAlgorithm.XxHash3);
    }

    /// <summary>Serializes a sketch image under an explicit checksum algorithm, so a test can stage it under a degenerate algorithm to prove the at-rest detection is non-vacuous.</summary>
    /// <param name="symbolCount">The number of symbols.</param>
    /// <param name="pool">The pool the symbol and image buffers are rented from.</param>
    /// <param name="checksum">The checksum algorithm the front matter and per-block digests are written under.</param>
    /// <returns>The pooled image.</returns>
    internal static ArtifactImage SketchImage(int symbolCount, MemoryPool<byte> pool, ChecksumAlgorithm checksum)
    {
        int symbolBytes = symbolCount * 24;
        using IMemoryOwner<byte> symbolOwner = pool.Rent(Math.Max(1, symbolBytes));
        Span<byte> symbols = symbolOwner.Memory.Span[..symbolBytes];
        for(int i = 0; i < symbols.Length; i++)
        {
            symbols[i] = (byte)((i * 31) + 7);
        }

        SketchSegment segment = new(symbolOwner.Memory[..symbolBytes], symbolWidth: 24, symbolsPerBlock: 4, blockAlignment: 64);
        int size = (int)segment.ComputeSerializedSize(checksum);
        IMemoryOwner<byte> owner = pool.Rent(size);
        segment.WriteTo(owner.Memory.Span[..size], checksum);

        return ArtifactImage.Own(owner, size, ManifestFileRole.Sketch);
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
    internal static void CorruptSegmentBlock(ArtifactImage image, int block, int blockCount)
    {
        Span<byte> bytes = image.WritableBytes;
        int frontMatterEnd = 19 + 12 + (blockCount * ChecksumAlgorithm.XxHash3.ByteWidth);
        int firstBlock = Align(frontMatterEnd);
        int stride = Align(10L * 12);
        bytes[firstBlock + (block * stride)] ^= 0xFF;
    }

    /// <summary>Flips the first payload byte of the given sketch block.</summary>
    /// <param name="image">The image to corrupt in place.</param>
    /// <param name="block">The block to corrupt.</param>
    /// <param name="blockCount">The sketch's block count (sizes the per-block checksum section).</param>
    internal static void CorruptSketchBlock(ArtifactImage image, int block, int blockCount)
    {
        Span<byte> bytes = image.WritableBytes;
        int frontMatterEnd = 19 + 16 + (blockCount * ChecksumAlgorithm.XxHash3.ByteWidth);
        int firstBlock = Align(frontMatterEnd);
        int stride = Align(4L * 24);
        bytes[firstBlock + (block * stride)] ^= 0xFF;
    }

    /// <summary>Flips a byte in the sidecar's front matter (past the 19-byte header, in the scalars/directory the front-matter trailer covers), so the corruption is reliably detected regardless of blob padding.</summary>
    /// <param name="image">The image to corrupt in place.</param>
    internal static void CorruptSidecarFrontMatter(ArtifactImage image)
    {
        image.WritableBytes[30] ^= 0xFF;
    }

    /// <summary>Overwrites the given item-segment block's whole payload with its bitwise complement, so every payload byte differs and the block's at-rest checksum fails (the whole-block-garbage damage kind, distinct from a single bit flip).</summary>
    /// <param name="image">The image to corrupt in place.</param>
    /// <param name="block">The block to garbage.</param>
    /// <param name="blockCount">The segment's block count (sizes the per-block checksum section).</param>
    internal static void GarbageSegmentBlock(ArtifactImage image, int block, int blockCount)
    {
        Span<byte> bytes = image.WritableBytes;
        int frontMatterEnd = 19 + 12 + (blockCount * ChecksumAlgorithm.XxHash3.ByteWidth);
        int firstBlock = Align(frontMatterEnd);
        int stride = Align(10L * 12);
        int start = firstBlock + (block * stride);
        for(int i = 0; i < 10 * 12; i++)
        {
            bytes[start + i] = (byte)~bytes[start + i];
        }
    }

    /// <summary>Overwrites the given sketch block's whole payload with its bitwise complement, so every payload byte differs and the block's at-rest checksum fails.</summary>
    /// <param name="image">The image to corrupt in place.</param>
    /// <param name="block">The block to garbage.</param>
    /// <param name="blockCount">The sketch's block count (sizes the per-block checksum section).</param>
    internal static void GarbageSketchBlock(ArtifactImage image, int block, int blockCount)
    {
        Span<byte> bytes = image.WritableBytes;
        int frontMatterEnd = 19 + 16 + (blockCount * ChecksumAlgorithm.XxHash3.ByteWidth);
        int firstBlock = Align(frontMatterEnd);
        int stride = Align(4L * 24);
        int start = firstBlock + (block * stride);
        for(int i = 0; i < 4 * 24; i++)
        {
            bytes[start + i] = (byte)~bytes[start + i];
        }
    }

    /// <summary>Overwrites the first column blob of a sidecar image with its bitwise complement, using the blob coordinates a clean verify round reports, so a real column blob (not padding) fails its per-blob checksum — the sidecar's per-blob detection seam, distinct from its front-matter seam.</summary>
    /// <param name="image">The image to corrupt in place.</param>
    internal static void GarbageSidecarBlob(ArtifactImage image)
    {
        VerifyRoundReport report = ColumnarTripleIndex.RunVerifyRound(image.Bytes);
        BlobVerdict blob = report.Blobs[0];
        Span<byte> bytes = image.WritableBytes;
        for(int i = (int)blob.ByteOffset; i < (int)(blob.ByteOffset + blob.ByteLength); i++)
        {
            bytes[i] = (byte)~bytes[i];
        }
    }

    /// <summary>Stamps the image's checksum-algorithm id with a foreign value no resolver maps, in place, so a reader refuses it under <see cref="PersistenceInvariant.EpochConsistency"/> before any block is verified. The id is read to select the algorithm before any checksum is recomputed, so the refusal precedes the front-matter and per-block verdicts.</summary>
    /// <param name="image">The image to stamp in place.</param>
    internal static void SetForeignChecksumAlgorithmId(ArtifactImage image)
    {
        image.WritableBytes[ChecksumAlgorithmIdOffset] = ForeignChecksumAlgorithmId;
    }

    /// <summary>The header byte offset of the checksum-algorithm id in the manifest and CURRENT-pointer images, which carry no feature mask: magic (8) + version major (1) + version minor (1).</summary>
    internal const int ManifestChecksumAlgorithmIdOffset = 8 + 1 + 1;

    /// <summary>Flips a generation byte of the live CURRENT pointer in the store so its self-checksum fails — at-rest CURRENT-pointer rot recovery's retained fallback recovers past.</summary>
    /// <param name="store">The store holding the live CURRENT pointer.</param>
    internal static void CorruptCurrentPointer(PersistenceStore store)
    {
        byte[] pointer = store.Read(ManifestNaming.CurrentPointerName) ?? throw new InvalidOperationException("The live CURRENT pointer is missing.");
        pointer[12] ^= 0xFF;
        store.WriteStaged(ManifestNaming.CurrentPointerName, pointer);
    }

    /// <summary>Flips a byte of a generation's manifest image in the store so its self-checksum fails — at-rest manifest rot recovery skips past to an earlier committed generation.</summary>
    /// <param name="store">The store holding the manifest image.</param>
    /// <param name="generation">The generation whose manifest is corrupted.</param>
    internal static void CorruptManifestBlob(PersistenceStore store, long generation)
    {
        byte[] manifest = store.Read(ManifestNaming.ManifestName(generation)) ?? throw new InvalidOperationException($"The manifest for generation {generation} is missing.");
        manifest[12] ^= 0xFF;
        store.WriteStaged(ManifestNaming.ManifestName(generation), manifest);
    }

    /// <summary>Stamps the live CURRENT pointer with a foreign checksum-algorithm id no resolver maps, so recovery refuses it as an unsupported epoch rather than masking it as at-rest rot.</summary>
    /// <param name="store">The store holding the live CURRENT pointer.</param>
    internal static void SetForeignCurrentAlgorithmId(PersistenceStore store)
    {
        byte[] pointer = store.Read(ManifestNaming.CurrentPointerName) ?? throw new InvalidOperationException("The live CURRENT pointer is missing.");
        pointer[ManifestChecksumAlgorithmIdOffset] = ForeignChecksumAlgorithmId;
        store.WriteStaged(ManifestNaming.CurrentPointerName, pointer);
    }

    /// <summary>Stamps a generation's manifest image with a foreign checksum-algorithm id, so recovery refuses it as an unsupported epoch rather than skipping past it as at-rest rot.</summary>
    /// <param name="store">The store holding the manifest image.</param>
    /// <param name="generation">The generation whose manifest is stamped.</param>
    internal static void SetForeignManifestAlgorithmId(PersistenceStore store, long generation)
    {
        byte[] manifest = store.Read(ManifestNaming.ManifestName(generation)) ?? throw new InvalidOperationException($"The manifest for generation {generation} is missing.");
        manifest[ManifestChecksumAlgorithmIdOffset] = ForeignChecksumAlgorithmId;
        store.WriteStaged(ManifestNaming.ManifestName(generation), manifest);
    }

    /// <summary>A directory durability barrier that does nothing.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    internal static void NoOpBarrier(string directoryPath)
    {
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

    /// <summary>Stages a system-of-record, sidecar, and sketch (each possibly corrupted by the caller) into a fresh temp-dir store and commits a generation manifest naming all three, renting the manifest writer's and digest buffers from <paramref name="pool"/>.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <param name="segment">The data-segment image.</param>
    /// <param name="sidecar">The sidecar image.</param>
    /// <param name="sketch">The sketch image.</param>
    /// <param name="pool">The pool the manifest writer and digest buffers are rented from.</param>
    /// <param name="directory">The created temp directory.</param>
    /// <returns>The store.</returns>
    internal static FileSystemPersistenceStore StageGeneration(long generation, ArtifactImage segment, ArtifactImage sidecar, ArtifactImage sketch, MemoryPool<byte> pool, out string directory)
    {
        directory = Directory.CreateTempSubdirectory("veritas-staging-").FullName;
        FileSystemPersistenceStore store = new(directory, NoOpBarrier);
        CommitGeneration(store, generation, segment, sidecar, sketch, pool);

        return store;
    }

    /// <summary>Stages the three artifacts under their generation-stamped names into an existing store and commits a manifest naming them — the recovery cells commit several generations into one store, and a wrapping fault store models a torn publish.</summary>
    /// <param name="store">The store committed into.</param>
    /// <param name="generation">The commit generation.</param>
    /// <param name="segment">The data-segment image.</param>
    /// <param name="sidecar">The sidecar image.</param>
    /// <param name="sketch">The sketch image.</param>
    /// <param name="pool">The pool the manifest writer and digest buffers are rented from.</param>
    internal static void CommitGeneration(PersistenceStore store, long generation, ArtifactImage segment, ArtifactImage sidecar, ArtifactImage sketch, MemoryPool<byte> pool)
    {
        int digestWidth = ChecksumAlgorithm.XxHash3.ByteWidth;
        string segmentName = $"segment-{generation:D20}.dat";
        string sidecarName = $"sidecar-{generation:D20}.idx";
        string sketchName = $"sketch-{generation:D20}.skt";
        store.WriteStaged(segmentName, segment.Bytes);
        store.WriteStaged(sidecarName, sidecar.Bytes);
        store.WriteStaged(sketchName, sketch.Bytes);

        using IMemoryOwner<byte> segmentDigest = Digest(segment.Bytes, pool);
        using IMemoryOwner<byte> sidecarDigest = Digest(sidecar.Bytes, pool);
        using IMemoryOwner<byte> sketchDigest = Digest(sketch.Bytes, pool);
        ManifestEntry[] entries =
        [
            new(ManifestFileRole.DataSegment, segmentName, 0, segment.Length, segmentDigest.Memory[..digestWidth]),
            new(ManifestFileRole.Sidecar, sidecarName, 0, sidecar.Length, sidecarDigest.Memory[..digestWidth]),
            new(ManifestFileRole.Sketch, sketchName, 0, sketch.Length, sketchDigest.Memory[..digestWidth]),
        ];
        new ManifestWriter(store, ChecksumAlgorithm.XxHash3, pool, retainedCurrentPointerCount: 4)
            .Commit(new Manifest(generation, generation * 11, generation * 13, entries));
    }

    /// <summary>Computes an artifact's whole-image XxHash3 manifest-entry digest into a buffer rented from <paramref name="pool"/>, valid until the rented owner is returned (after the manifest is committed). Shared with the scrub tests' two-artifact staging.</summary>
    /// <param name="image">The artifact bytes.</param>
    /// <param name="pool">The pool the digest buffer is rented from.</param>
    /// <returns>The rented digest owner; its first <see cref="ChecksumAlgorithm.ByteWidth"/> bytes are the digest.</returns>
    internal static IMemoryOwner<byte> Digest(ReadOnlySpan<byte> image, MemoryPool<byte> pool)
    {
        int width = ChecksumAlgorithm.XxHash3.ByteWidth;
        IMemoryOwner<byte> owner = pool.Rent(width);
        ChecksumAlgorithm.XxHash3.Compute(image, owner.Memory.Span[..width]);

        return owner;
    }
}
