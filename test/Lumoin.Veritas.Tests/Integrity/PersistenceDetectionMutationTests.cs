using System;
using System.Buffers;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Persistence.Sketch;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The non-vacuousness proof for the storage self-heal content-detection seams: the per-artifact at-rest
/// verdicts the combination matrix relies on (a corrupt system-of-record, sketch, or sidecar block reported
/// corrupt) are only meaningful if the block checksum is doing real work. This mutates the detector itself — a
/// degenerate algorithm that writes an all-zero digest and recomputes an all-zero digest, wired at both write
/// and read through a <see cref="ResolveChecksumAlgorithmDelegate"/> — and proves the exact corruption the real
/// algorithm detects goes UNDETECTED under the degenerate one. So a green matrix cell cannot be passing because
/// the verify is a no-op: neutering the checksum flips the verdict.
/// <para>
/// The degenerate algorithm shares the real width (8 bytes) so the on-disk framing is byte-identical and the
/// same corruption offsets apply; it carries a distinct id (<see cref="DegenerateChecksumAlgorithmId"/>, not the
/// foreign-epoch marker 99) that only this test's resolver maps. Each seam asserts four points — under the real
/// algorithm a clean image verifies clean and a corrupt image is detected; under the degenerate algorithm a
/// clean image still verifies clean (so the degenerate read path is exercised, not refused as a foreign epoch)
/// and the same corruption verifies clean (the mutation). The two clean baselines make the final point airtight:
/// the corrupt-degenerate "clean" is because the checksum was neutered, not because the verify was skipped.
/// </para>
/// </summary>
[TestClass]
internal sealed class PersistenceDetectionMutationTests
{
    /// <summary>The triple count each segment/sidecar exemplar stages (three ten-item system-of-record blocks).</summary>
    private const uint TripleCount = 30;

    /// <summary>The system-of-record block count for the exemplar (<see cref="TripleCount"/> over ten items per block).</summary>
    private const int SegmentBlockCount = 3;

    /// <summary>The sketch exemplar's symbol count (ten four-symbol blocks).</summary>
    private const int SketchSymbolCount = 40;

    /// <summary>The sketch exemplar's block count (<see cref="SketchSymbolCount"/> over four symbols per block).</summary>
    private const int SketchBlockCount = 10;

    /// <summary>The middle block every exemplar corrupts, so the damaged range is a non-edge interval.</summary>
    private const int CorruptBlock = 1;

    /// <summary>A test-only checksum-algorithm id this test's resolver maps to the degenerate algorithm — distinct from the foreign-epoch marker (99), which no resolver maps.</summary>
    private const byte DegenerateChecksumAlgorithmId = 200;

    /// <summary>A degenerate checksum algorithm that writes an all-zero digest regardless of its input, at the real 8-byte width so the framing is byte-identical; with it at both write and read every block verifies (zeros equal zeros) no matter how the payload is mutated.</summary>
    private static readonly ChecksumAlgorithm Degenerate = ChecksumAlgorithm.Create(DegenerateChecksumAlgorithmId, "degenerate-zero", sizeof(ulong), WriteZeros);

    /// <summary>The content-detection seams the proof runs over.</summary>
    internal enum MutationSeam
    {
        /// <summary>A durable system-of-record item-segment block.</summary>
        SystemOfRecordBlock,

        /// <summary>An integrity sketch block.</summary>
        SketchBlock,

        /// <summary>A columnar sidecar column blob.</summary>
        SidecarBlob,
    }

    /// <summary>Writes an all-zero digest, ignoring the input — the degenerate compute that neuters detection.</summary>
    /// <param name="data">The bytes that would be checksummed (ignored).</param>
    /// <param name="destination">The digest destination, cleared to zero.</param>
    private static void WriteZeros(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        destination.Clear();
    }

    /// <summary>Resolves the degenerate id to the degenerate algorithm and every other id through the built-in resolver, so one resolver verifies both a real (XxHash3) and a degenerate image.</summary>
    /// <param name="id">The on-disk algorithm id.</param>
    /// <returns>The algorithm, or <see langword="null"/> for an id no resolver maps.</returns>
    private static ChecksumAlgorithm? DegenerateResolver(byte id)
    {
        return id == DegenerateChecksumAlgorithmId ? Degenerate : ChecksumAlgorithm.DefaultResolver(id);
    }

    /// <summary>One seam identity per row, so the seam crosses the public test-method boundary as a string rather than the internal enum.</summary>
    /// <returns>The data rows.</returns>
    private static IEnumerable<object[]> SeamRows()
    {
        foreach(MutationSeam seam in Enum.GetValues<MutationSeam>())
        {
            yield return [seam.ToString()];
        }
    }

    /// <summary>Proves the seam's at-rest detection is non-vacuous: under the real algorithm a clean image verifies clean and a corrupt image is detected; under the degenerate zero-checksum a clean image still verifies clean and the same corruption verifies clean — so the matrix's corresponding positive cell is not passing on a no-op verify.</summary>
    /// <param name="seamId">The seam to prove.</param>
    [TestMethod]
    [DynamicData(nameof(SeamRows))]
    public void MutationFlipsDetectedCorruptionToClean(string seamId)
    {
        MutationSeam seam = Enum.Parse<MutationSeam>(seamId);
        using VeritasMemoryPool<byte> pool = new();

        Assert.IsTrue(VerifyIsClean(seam, ChecksumAlgorithm.XxHash3, corrupt: false, pool), "A clean image must verify clean under the real algorithm.");
        Assert.IsFalse(VerifyIsClean(seam, ChecksumAlgorithm.XxHash3, corrupt: true, pool), "The real algorithm must detect the seam's corruption.");
        Assert.IsTrue(VerifyIsClean(seam, Degenerate, corrupt: false, pool), "A clean degenerate image must verify clean, so the degenerate read path is exercised rather than refused.");
        Assert.IsTrue(VerifyIsClean(seam, Degenerate, corrupt: true, pool), "The zero-checksum mutation must flip the detected corruption to clean, proving the positive matrix cells are non-vacuous.");
    }

    /// <summary>Stages the seam's artifact under <paramref name="checksum"/>, optionally applies the seam's block corruption, and returns whether a decode-free verify round — resolving both the real and degenerate ids — reports it clean.</summary>
    /// <param name="seam">The seam.</param>
    /// <param name="checksum">The checksum algorithm the image is written under.</param>
    /// <param name="corrupt">Whether to corrupt the seam's block before verifying.</param>
    /// <param name="pool">The pool the image is rented from.</param>
    /// <returns>Whether the verify round reports the image clean.</returns>
    private static bool VerifyIsClean(MutationSeam seam, ChecksumAlgorithm checksum, bool corrupt, MemoryPool<byte> pool)
    {
        using ArtifactImage image = BuildImage(seam, checksum, pool);
        if(corrupt)
        {
            ApplyCorruption(seam, image);
        }

        return seam switch
        {
            MutationSeam.SystemOfRecordBlock => ItemSegment.RunVerifyRound(image.Bytes, DegenerateResolver).IsClean,
            MutationSeam.SketchBlock => SketchSegment.RunVerifyRound(image.Bytes, DegenerateResolver).IsClean,
            MutationSeam.SidecarBlob => ColumnarTripleIndex.RunVerifyRound(image.Bytes, DegenerateResolver).ToArtifactReport().IsClean,
            _ => throw new InvalidOperationException($"Seam {seam} has no verify round."),
        };
    }

    /// <summary>Builds the seam's artifact image under the given checksum algorithm.</summary>
    /// <param name="seam">The seam.</param>
    /// <param name="checksum">The checksum algorithm.</param>
    /// <param name="pool">The pool the image is rented from.</param>
    /// <returns>The pooled image (the caller disposes it).</returns>
    private static ArtifactImage BuildImage(MutationSeam seam, ChecksumAlgorithm checksum, MemoryPool<byte> pool)
    {
        return seam switch
        {
            MutationSeam.SystemOfRecordBlock => SegmentImage(SampleTriples(TripleCount), pool, checksum),
            MutationSeam.SketchBlock => SketchImage(SketchSymbolCount, pool, checksum),
            MutationSeam.SidecarBlob => SidecarImage(SampleTriples(TripleCount), pool, checksum),
            _ => throw new InvalidOperationException($"Seam {seam} has no image builder."),
        };
    }

    /// <summary>Applies the seam's content corruption in place: a flipped block byte for the system-of-record and sketch, a garbaged column blob for the sidecar.</summary>
    /// <param name="seam">The seam.</param>
    /// <param name="image">The image to corrupt in place.</param>
    private static void ApplyCorruption(MutationSeam seam, ArtifactImage image)
    {
        switch(seam)
        {
            case MutationSeam.SystemOfRecordBlock:
            {
                CorruptSegmentBlock(image, CorruptBlock, SegmentBlockCount);

                break;
            }
            case MutationSeam.SketchBlock:
            {
                CorruptSketchBlock(image, CorruptBlock, SketchBlockCount);

                break;
            }
            case MutationSeam.SidecarBlob:
            {
                GarbageFirstSidecarBlob(image);

                break;
            }
            default:
            {
                throw new InvalidOperationException($"Seam {seam} has no corruption.");
            }
        }
    }

    /// <summary>Overwrites the sidecar's first column blob with its bitwise complement, locating the blob through a degenerate-aware verify round so it reads a sidecar written under either the real or the degenerate algorithm.</summary>
    /// <param name="image">The sidecar image to corrupt in place.</param>
    private static void GarbageFirstSidecarBlob(ArtifactImage image)
    {
        VerifyRoundReport report = ColumnarTripleIndex.RunVerifyRound(image.Bytes, DegenerateResolver);
        BlobVerdict blob = report.Blobs[0];
        Span<byte> bytes = image.WritableBytes;
        for(int i = (int)blob.ByteOffset; i < (int)(blob.ByteOffset + blob.ByteLength); i++)
        {
            bytes[i] = (byte)~bytes[i];
        }
    }
}
