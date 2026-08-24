using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Journal;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Parity;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Persistence.Sketch;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Replication;

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

    /// <summary>Serializes a local-parity image over the same triples (the capacity-1 XOR of the system-of-record's 10-item blocks, 64-byte aligned) into a buffer rented from <paramref name="pool"/>.</summary>
    /// <param name="triples">The triples whose system-of-record blocks the parity protects.</param>
    /// <param name="pool">The pool the parity and image buffers are rented from.</param>
    /// <returns>The pooled image.</returns>
    internal static ArtifactImage ParityImage(EncodedTriple[] triples, MemoryPool<byte> pool)
    {
        return ParityImage(triples, pool, ChecksumAlgorithm.XxHash3);
    }

    /// <summary>Serializes a local-parity image under an explicit checksum algorithm, so a test can stage it under a degenerate algorithm to prove the at-rest detection is non-vacuous.</summary>
    /// <param name="triples">The triples whose system-of-record blocks the parity protects.</param>
    /// <param name="pool">The pool the parity and image buffers are rented from.</param>
    /// <param name="checksum">The checksum algorithm the front matter and per-block digest are written under.</param>
    /// <returns>The pooled image.</returns>
    internal static ArtifactImage ParityImage(EncodedTriple[] triples, MemoryPool<byte> pool, ChecksumAlgorithm checksum)
    {
        ItemSegment systemOfRecord = new(triples, blockItemCount: 10, blockAlignment: 64);
        using ParityBlock parity = ParityBlock.Rent(pool, systemOfRecord.MaxBlockPayloadByteCount);
        int protectedBlockCount = ParitySegment.BuildParity(systemOfRecord, parity.WritableSpan, pool);

        ParitySegment segment = new(parity.Memory, protectedBlockCount, blockAlignment: 64);
        int size = (int)segment.ComputeSerializedSize(checksum);
        IMemoryOwner<byte> owner = pool.Rent(size);
        segment.WriteTo(owner.Memory.Span[..size], checksum);

        return ArtifactImage.Own(owner, size, ManifestFileRole.Parity);
    }

    /// <summary>Builds a term dictionary of <paramref name="termCount"/> distinct named nodes, so a production-shaped generation can stage a real dictionary segment (the Dictionary role, absent from the two- and four-artifact stagings).</summary>
    /// <param name="termCount">The number of distinct terms.</param>
    /// <returns>The dictionary.</returns>
    internal static TermDictionary SampleDictionary(uint termCount)
    {
        TermDictionary dictionary = new(epoch: 0x1234);
        for(uint i = 0; i < termCount; i++)
        {
            dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(string.Create(CultureInfo.InvariantCulture, $"http://example.org/term/{i}"))));
        }

        return dictionary;
    }

    /// <summary>Serializes a term-dictionary segment image (the Dictionary role) under a given block term count into a buffer rented from <paramref name="pool"/>, so a test can stage a multi-block dictionary and corrupt one block while the others verify.</summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="blockTermCount">The terms per block; a small value gives a multi-block dictionary.</param>
    /// <param name="pool">The pool the image buffer is rented from.</param>
    /// <returns>The pooled image.</returns>
    internal static ArtifactImage DictionaryImage(TermDictionary dictionary, int blockTermCount, MemoryPool<byte> pool)
    {
        DictionarySegment segment = new(dictionary, blockTermCount);
        int size = segment.ComputeSerializedSize(ChecksumAlgorithm.XxHash3);
        IMemoryOwner<byte> owner = pool.Rent(size);
        segment.WriteTo(owner.Memory.Span[..size], ChecksumAlgorithm.XxHash3);

        return ArtifactImage.Own(owner, size, ManifestFileRole.Dictionary);
    }

    /// <summary>Serializes a named-graph system-of-record segment image (the NamedGraphSegment role, sharing the item-segment format and geometry with the default graph) into a buffer rented from <paramref name="pool"/>.</summary>
    /// <param name="triples">The named graph's triples.</param>
    /// <param name="pool">The pool the image buffer is rented from.</param>
    /// <returns>The pooled image.</returns>
    internal static ArtifactImage NamedGraphSegmentImage(EncodedTriple[] triples, MemoryPool<byte> pool)
    {
        ItemSegment segment = new(triples, blockItemCount: 10, blockAlignment: 64);
        int size = (int)segment.ComputeSerializedSize(ChecksumAlgorithm.XxHash3);
        IMemoryOwner<byte> owner = pool.Rent(size);
        segment.WriteTo(owner.Memory.Span[..size], ChecksumAlgorithm.XxHash3);

        return ArtifactImage.Own(owner, size, ManifestFileRole.NamedGraphSegment);
    }

    /// <summary>Overwrites a dictionary block's whole payload with its bitwise complement, using the block coordinates a clean verify round reports, so a real block (epoch prefix and term records, not the front-matter directory) fails its per-block checksum — the dictionary's per-block detection seam. The variable-length, back-to-back block layout is read from the verify round rather than assumed, so this stays correct across block geometries.</summary>
    /// <param name="image">The image to corrupt in place.</param>
    /// <param name="block">The block to garbage.</param>
    internal static void GarbageDictionaryBlock(ArtifactImage image, int block)
    {
        ArtifactVerifyReport report = DictionarySegment.RunVerifyRound(image.Bytes);
        BlockVerdict verdict = report.Blocks.Span[block];
        Span<byte> bytes = image.WritableBytes;
        for(long i = verdict.ByteOffset; i < verdict.ByteOffset + verdict.ByteLength; i++)
        {
            bytes[(int)i] = (byte)~bytes[(int)i];
        }
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

    /// <summary>Overwrites an item-segment block's whole payload with its bitwise complement IN THE FILE, using the block coordinates a clean verify round reports over the file's own bytes — the on-disk face of <see cref="GarbageSegmentBlock"/> for a store artifact another process persisted, whose block geometry this fixture must not assume.</summary>
    /// <param name="filePath">The item-segment artifact file to corrupt in place.</param>
    /// <param name="block">The block to garbage.</param>
    internal static void GarbageSegmentBlockInFile(string filePath, int block)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        ArtifactVerifyReport report = ItemSegment.RunVerifyRound(bytes);
        BlockVerdict verdict = report.Blocks.Span[block];
        for(long i = verdict.ByteOffset; i < verdict.ByteOffset + verdict.ByteLength; i++)
        {
            bytes[(int)i] = (byte)~bytes[(int)i];
        }

        File.WriteAllBytes(filePath, bytes);
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

    /// <summary>Flips the first byte of the parity block — a capacity-1 parity is a single block, so its one checksum domain covers the whole parity.</summary>
    /// <param name="image">The image to corrupt in place.</param>
    internal static void CorruptParityBlock(ArtifactImage image)
    {
        Span<byte> bytes = image.WritableBytes;
        int frontMatterEnd = 19 + 12 + ChecksumAlgorithm.XxHash3.ByteWidth;
        int firstBlock = Align(frontMatterEnd);
        bytes[firstBlock] ^= 0xFF;
    }

    /// <summary>Overwrites the parity block's whole payload (the 10-item block stride) with its bitwise complement, so every byte differs and the block's at-rest checksum fails.</summary>
    /// <param name="image">The image to corrupt in place.</param>
    internal static void GarbageParityBlock(ArtifactImage image)
    {
        Span<byte> bytes = image.WritableBytes;
        int frontMatterEnd = 19 + 12 + ChecksumAlgorithm.XxHash3.ByteWidth;
        int firstBlock = Align(frontMatterEnd);
        int stride = 10 * 12;
        for(int i = 0; i < stride; i++)
        {
            bytes[firstBlock + i] = (byte)~bytes[firstBlock + i];
        }
    }

    /// <summary>Flips the low byte of the parity's protected-block-count scalar — front matter the trailer covers but the block digest and geometry parse do not — so the front-matter verdict fails while the block stays readable, the parity's front-matter detection seam.</summary>
    /// <param name="image">The image to corrupt in place.</param>
    internal static void CorruptParityFrontMatter(ArtifactImage image)
    {
        image.WritableBytes[19 + sizeof(int)] ^= 0xFF;
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

    /// <summary>Flips a byte of the given item-segment block's STORED checksum digest — the per-block digest in the front-matter checksum section, not the block's payload — so the block's recomputed payload digest no longer matches its stored digest. The digest section is covered by the front-matter trailer, so this fails both the trailer and the block's own comparison: the digest-side detection seam, distinct from corrupting the payload.</summary>
    /// <param name="image">The image to corrupt in place.</param>
    /// <param name="block">The block whose stored digest is tampered.</param>
    internal static void CorruptSegmentChecksumField(ArtifactImage image, int block)
    {
        Span<byte> bytes = image.WritableBytes;
        int checksumSectionOffset = 19 + 12;
        bytes[checksumSectionOffset + (block * ChecksumAlgorithm.XxHash3.ByteWidth)] ^= 0xFF;
    }

    /// <summary>Flips a byte of the given sketch block's STORED checksum digest — the per-block digest in the front-matter checksum section, not the block's payload — so the block's recomputed payload digest no longer matches its stored digest. The digest section is covered by the front-matter trailer, so this fails both the trailer and the block's own comparison.</summary>
    /// <param name="image">The image to corrupt in place.</param>
    /// <param name="block">The block whose stored digest is tampered.</param>
    internal static void CorruptSketchChecksumField(ArtifactImage image, int block)
    {
        Span<byte> bytes = image.WritableBytes;
        int checksumSectionOffset = 19 + 16;
        bytes[checksumSectionOffset + (block * ChecksumAlgorithm.XxHash3.ByteWidth)] ^= 0xFF;
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

    /// <summary>Persists a generation's OWN integrity sketch over its pre-damage triples through the REAL rateless codec — the at-rest record a repair pass's faithfulness peel verifies a healed set against — as a stageable artifact image.</summary>
    /// <param name="triples">The generation's pre-damage triple set.</param>
    /// <param name="pool">The pool the persist rents its transient buffers from and the image is copied into.</param>
    /// <param name="symbolCap">The symbol budget the sketch is persisted at; far above the item count, so a faithful composed set always peels completely.</param>
    /// <returns>The pooled sketch artifact image.</returns>
    internal static ArtifactImage GenerationSketchImage(EncodedTriple[] triples, MemoryPool<byte> pool, int symbolCap)
    {
        ContentKey128[] items = new ContentKey128[triples.Length];
        for(int i = 0; i < triples.Length; i++)
        {
            items[i] = StructuralReconciliationProjection.Project(triples[i]);
        }

        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(items, SketchContract.Structural, symbolCap, ChecksumAlgorithm.XxHash3, pool, new RatelessSketchCodec(pool).Encode, writer);

        return ArtifactImage.Copy(writer.WrittenSpan, ManifestFileRole.Sketch, pool);
    }

    /// <summary>Projects a triple set into its structural reconciliation keys as sixteen-byte handles — the shape the sharded fetch seam and the shard-difference wire speak.</summary>
    /// <param name="triples">The triples to project.</param>
    /// <returns>One key handle per triple.</returns>
    internal static List<ReadOnlyMemory<byte>> ProjectedKeys(IEnumerable<EncodedTriple> triples)
    {
        List<ReadOnlyMemory<byte>> keys = [];
        foreach(EncodedTriple triple in triples)
        {
            byte[] bytes = new byte[ContentKey128.ByteWidth];
            StructuralReconciliationProjection.Project(triple).WriteBytes(bytes);
            keys.Add(bytes);
        }

        return keys;
    }

    /// <summary>The items of the given blocks — the content a multi-block loss takes.</summary>
    /// <param name="triples">The full triple set.</param>
    /// <param name="blockItemCount">The items per block.</param>
    /// <param name="blocks">The lost block indexes.</param>
    /// <returns>The lost items.</returns>
    internal static EncodedTriple[] BlockItems(EncodedTriple[] triples, int blockItemCount, params int[] blocks)
    {
        List<EncodedTriple> lost = [];
        foreach(int block in blocks)
        {
            int start = block * blockItemCount;
            int end = Math.Min(start + blockItemCount, triples.Length);
            lost.AddRange(triples[start..end]);
        }

        return [.. lost];
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

    /// <summary>Stages a system-of-record, sidecar, sketch, and local-parity sidecar (each possibly corrupted by the caller) into a fresh temp-dir store and commits a generation manifest naming all four — the staging a parity-restore repair pass reads.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <param name="segment">The data-segment image.</param>
    /// <param name="sidecar">The sidecar image.</param>
    /// <param name="sketch">The sketch image.</param>
    /// <param name="parity">The local-parity image.</param>
    /// <param name="pool">The pool the manifest writer and digest buffers are rented from.</param>
    /// <param name="directory">The created temp directory.</param>
    /// <returns>The store.</returns>
    internal static FileSystemPersistenceStore StageGeneration(long generation, ArtifactImage segment, ArtifactImage sidecar, ArtifactImage sketch, ArtifactImage parity, MemoryPool<byte> pool, out string directory)
    {
        directory = Directory.CreateTempSubdirectory("veritas-staging-").FullName;
        FileSystemPersistenceStore store = new(directory, NoOpBarrier);
        CommitGeneration(store, generation, segment, sidecar, sketch, parity, pool);

        return store;
    }

    /// <summary>Stages the four artifacts under their generation-stamped names into an existing store and commits a manifest naming them, so a repair pass over the generation can read its parity sidecar.</summary>
    /// <param name="store">The store committed into.</param>
    /// <param name="generation">The commit generation.</param>
    /// <param name="segment">The data-segment image.</param>
    /// <param name="sidecar">The sidecar image.</param>
    /// <param name="sketch">The sketch image.</param>
    /// <param name="parity">The local-parity image.</param>
    /// <param name="pool">The pool the manifest writer and digest buffers are rented from.</param>
    internal static void CommitGeneration(PersistenceStore store, long generation, ArtifactImage segment, ArtifactImage sidecar, ArtifactImage sketch, ArtifactImage parity, MemoryPool<byte> pool)
    {
        int digestWidth = ChecksumAlgorithm.XxHash3.ByteWidth;
        string segmentName = $"segment-{generation:D20}.dat";
        string sidecarName = $"sidecar-{generation:D20}.idx";
        string sketchName = $"sketch-{generation:D20}.skt";
        string parityName = $"parity-{generation:D20}.par";
        store.WriteStaged(segmentName, segment.Bytes);
        store.WriteStaged(sidecarName, sidecar.Bytes);
        store.WriteStaged(sketchName, sketch.Bytes);
        store.WriteStaged(parityName, parity.Bytes);

        using IMemoryOwner<byte> segmentDigest = Digest(segment.Bytes, pool);
        using IMemoryOwner<byte> sidecarDigest = Digest(sidecar.Bytes, pool);
        using IMemoryOwner<byte> sketchDigest = Digest(sketch.Bytes, pool);
        using IMemoryOwner<byte> parityDigest = Digest(parity.Bytes, pool);
        ManifestEntry[] entries =
        [
            new(ManifestFileRole.DataSegment, segmentName, 0, segment.Length, segmentDigest.Memory[..digestWidth]),
            new(ManifestFileRole.Sidecar, sidecarName, 0, sidecar.Length, sidecarDigest.Memory[..digestWidth]),
            new(ManifestFileRole.Sketch, sketchName, 0, sketch.Length, sketchDigest.Memory[..digestWidth]),
            new(ManifestFileRole.Parity, parityName, 0, parity.Length, parityDigest.Memory[..digestWidth]),
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

    /// <summary>Builds a non-mutating initial journal entry from <paramref name="parent"/> to <paramref name="child"/>; the journal overwrites the placeholder sequence and timestamp on append. Shared by the journal recovery and detection-mutation tests.</summary>
    /// <param name="parent">The parent identifier.</param>
    /// <param name="child">The child identifier.</param>
    /// <returns>The entry.</returns>
    internal static JournalEntry MakeJournalEntry(NodeIdentifier parent, NodeIdentifier child)
    {
        return new JournalEntry(
            ParentId: parent,
            ChildId: child,
            EntryKind: EditSessionEntryKind.Initial,
            SessionId: null,
            EditCommitment: null,
            Additions: ImmutableArray<EncodedTriple>.Empty,
            Removals: ImmutableArray<EncodedTriple>.Empty,
            Timestamp: default,
            SequenceNumber: 0);
    }

    /// <summary>Appends a chain of <paramref name="count"/> non-mutating initial entries from the empty head, each pointing to the next, so the durable log holds a known number of intact records. Shared by the journal recovery and detection-mutation tests.</summary>
    /// <param name="journal">The journal to append to.</param>
    /// <param name="count">The number of records to append.</param>
    /// <returns>The append task.</returns>
    internal static async Task AppendJournalChain(FileBackedJournal journal, int count)
    {
        NodeIdentifier previous = NodeIdentifier.Empty;
        for(ulong i = 1; i <= (ulong)count; i++)
        {
            NodeIdentifier next = new(i);
            await journal.AppendDelegate(MakeJournalEntry(previous, next), previous, CancellationToken.None).ConfigureAwait(false);
            previous = next;
        }
    }

    /// <summary>The store name of a dictionary artifact for a generation, matching the durable store's production naming.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <returns>The artifact name.</returns>
    internal static string DictionaryArtifactName(long generation)
    {
        return "dict-" + generation.ToString("D20", CultureInfo.InvariantCulture) + ".dic";
    }

    /// <summary>The store name of a default-graph system-of-record artifact for a generation, matching the durable store's production naming.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <returns>The artifact name.</returns>
    internal static string RecordArtifactName(long generation)
    {
        return "sor-" + generation.ToString("D20", CultureInfo.InvariantCulture) + ".sor";
    }

    /// <summary>The store name of a named-graph system-of-record artifact for a generation and graph-name term id, matching the durable store's production naming (<c>nsor-&lt;generation&gt;-g&lt;graphId&gt;.sor</c>).</summary>
    /// <param name="generation">The commit generation.</param>
    /// <param name="graphId">The graph-name term id.</param>
    /// <returns>The artifact name.</returns>
    internal static string NamedGraphArtifactName(long generation, uint graphId)
    {
        return "nsor-" + generation.ToString("D20", CultureInfo.InvariantCulture) + "-g" + graphId.ToString("D10", CultureInfo.InvariantCulture) + ".sor";
    }

    /// <summary>The store name of a columnar-sidecar artifact for a generation, matching the durable store's production naming.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <returns>The artifact name.</returns>
    internal static string SidecarArtifactName(long generation)
    {
        return "cidx-" + generation.ToString("D20", CultureInfo.InvariantCulture) + ".cidx";
    }

    /// <summary>Stages a PRODUCTION-SHAPED generation — a dictionary (role 6), a default-graph data segment (role 1), zero or more named-graph segments (role 7), and a columnar sidecar (role 2), under the durable store's own artifact names — into a fresh temp-dir store and commits a manifest naming them all. This is the manifest shape <see cref="Lumoin.Veritas.Core.Persistence.DurableSystemOfRecordStore"/> commits, exercising the Dictionary and NamedGraphSegment scrub/repair coverage the two- and four-artifact stagings never reach. Each image may be corrupted by the caller before staging, so the manifest binds its recorded digest to the staged (possibly corrupt) bytes and the per-block detection is isolated from the whole-image binding.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <param name="dictionary">The dictionary image.</param>
    /// <param name="dataSegment">The default-graph data-segment image.</param>
    /// <param name="namedGraphs">The named-graph segments, each its graph-name term id paired with its image.</param>
    /// <param name="sidecar">The columnar sidecar image.</param>
    /// <param name="pool">The pool the manifest writer and digest buffers are rented from.</param>
    /// <param name="directory">The created temp directory.</param>
    /// <returns>The store.</returns>
    internal static FileSystemPersistenceStore StageProductionGeneration(long generation, ArtifactImage dictionary, ArtifactImage dataSegment, IReadOnlyList<(uint GraphId, ArtifactImage Segment)> namedGraphs, ArtifactImage sidecar, MemoryPool<byte> pool, out string directory)
    {
        directory = Directory.CreateTempSubdirectory("veritas-prodshape-").FullName;
        FileSystemPersistenceStore store = new(directory, NoOpBarrier);

        string dictionaryName = DictionaryArtifactName(generation);
        string recordName = RecordArtifactName(generation);
        string sidecarName = SidecarArtifactName(generation);
        store.WriteStaged(dictionaryName, dictionary.Bytes);
        store.WriteStaged(recordName, dataSegment.Bytes);
        store.WriteStaged(sidecarName, sidecar.Bytes);

        //Every entry's digest must stay rented until the manifest commit copies it into the manifest image; they
        //are released together in the finally after the commit publishes.
        List<IMemoryOwner<byte>> digests = [];
        try
        {
            List<ManifestEntry> entries =
            [
                new(ManifestFileRole.Dictionary, dictionaryName, 0, dictionary.Length, StageDigest(dictionary.Bytes, pool, digests)),
                new(ManifestFileRole.DataSegment, recordName, 0, dataSegment.Length, StageDigest(dataSegment.Bytes, pool, digests)),
            ];
            foreach((uint graphId, ArtifactImage segment) in namedGraphs)
            {
                string namedName = NamedGraphArtifactName(generation, graphId);
                store.WriteStaged(namedName, segment.Bytes);
                entries.Add(new(ManifestFileRole.NamedGraphSegment, namedName, 0, segment.Length, StageDigest(segment.Bytes, pool, digests)));
            }

            entries.Add(new(ManifestFileRole.Sidecar, sidecarName, 0, sidecar.Length, StageDigest(sidecar.Bytes, pool, digests)));
            new ManifestWriter(store, ChecksumAlgorithm.XxHash3, pool, retainedCurrentPointerCount: 4)
                .Commit(new Manifest(generation, generation * 11, generation * 13, entries));
        }
        finally
        {
            foreach(IMemoryOwner<byte> digest in digests)
            {
                digest.Dispose();
            }
        }

        return store;
    }

    /// <summary>Computes an artifact's manifest-entry digest into a buffer rented from <paramref name="pool"/>, records the owner in <paramref name="owners"/> for release after the commit, and returns the digest view — the variable-arity companion of <see cref="Digest"/> for the production staging's named-graph list.</summary>
    /// <param name="image">The artifact bytes.</param>
    /// <param name="pool">The pool the digest buffer is rented from.</param>
    /// <param name="owners">The list the rented digest owner is appended to for release after the commit.</param>
    /// <returns>The digest view over the rented buffer.</returns>
    private static ReadOnlyMemory<byte> StageDigest(ReadOnlySpan<byte> image, MemoryPool<byte> pool, List<IMemoryOwner<byte>> owners)
    {
        IMemoryOwner<byte> owner = Digest(image, pool);
        owners.Add(owner);

        return owner.Memory[..ChecksumAlgorithm.XxHash3.ByteWidth];
    }
}
