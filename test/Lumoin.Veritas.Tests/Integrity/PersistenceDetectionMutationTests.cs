using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence.Journal;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Persistence.Sketch;
using Microsoft.Extensions.Time.Testing;
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

    /// <summary>The number of records the journal exemplar appends before tampering one.</summary>
    private const int JournalRecordCount = 3;

    /// <summary>The file byte offset of the first journal record's parent-identifier field — past the four-byte length prefix and the version, sequence, and kind payload fields. The parent id carries no replay validation, so a flipped byte there is caught only by the record checksum (and decodes cleanly when the checksum is neutered, rather than tripping the sequence or kind validation).</summary>
    private const int JournalFirstRecordParentByteOffset = 4 + 1 + 8 + 1;

    /// <summary>The generation the manifest exemplar stamps; a small positive value so a tampered generation byte stays positive and parsing does not reject it before the self-checksum is checked.</summary>
    private const long ManifestGeneration = 7;

    /// <summary>The manifest byte the exemplar tampers — a generation byte covered by the self-checksum and past the algorithm-id, so a flip is caught only by the self-checksum.</summary>
    private const int ManifestCorruptionOffset = 12;

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

        /// <summary>A durable journal record's checksum.</summary>
        JournalRecord,

        /// <summary>A generation manifest's self-checksum.</summary>
        ManifestSelf,
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

    /// <summary>Proves the seam's at-rest detection is non-vacuous: under the real algorithm a clean exemplar verifies clean and a corrupt one is detected; under the degenerate zero-checksum a clean exemplar still verifies clean and the same corruption verifies clean — so the matrix's corresponding positive cell is not passing on a no-op verify.</summary>
    /// <param name="seamId">The seam to prove.</param>
    /// <returns>The proof task.</returns>
    [TestMethod]
    [DynamicData(nameof(SeamRows))]
    public async Task MutationFlipsDetectedCorruptionToClean(string seamId)
    {
        MutationSeam seam = Enum.Parse<MutationSeam>(seamId);
        using VeritasMemoryPool<byte> pool = new();

        Assert.IsTrue(await VerifyIsClean(seam, ChecksumAlgorithm.XxHash3, corrupt: false, pool).ConfigureAwait(false), "A clean exemplar must verify clean under the real algorithm.");
        Assert.IsFalse(await VerifyIsClean(seam, ChecksumAlgorithm.XxHash3, corrupt: true, pool).ConfigureAwait(false), "The real algorithm must detect the seam's corruption.");
        Assert.IsTrue(await VerifyIsClean(seam, Degenerate, corrupt: false, pool).ConfigureAwait(false), "A clean degenerate exemplar must verify clean, so the degenerate read path is exercised rather than refused.");
        Assert.IsTrue(await VerifyIsClean(seam, Degenerate, corrupt: true, pool).ConfigureAwait(false), "The zero-checksum mutation must flip the detected corruption to clean, proving the positive matrix cells are non-vacuous.");
    }

    /// <summary>The ECC meta-cell: the operator's memory-protection assumption is a real, consumed policy knob — AssumeProtected and AssumeUnprotected resolve to opposite protection verdicts, which the scrub cadence then derives from — yet the at-rest verify takes no <see cref="ExecutionPolicy"/>, so a corrupt artifact is detected regardless of the assumption. ECC steers cadence, not correctness: it is correctness-inert for the verify verdict, so no detection cell needs to vary it.</summary>
    [TestMethod]
    public void EccAssumptionSteersProtectionNotTheVerifyVerdict()
    {
        ExecutionEnvironment environment = new(ProcessorCount: 8, CpuQuotaCores: null, IsBrowser: false, MemoryErrorCorrectionDetected: null);
        ResolvedExecutionPlan protectedPlan = (ExecutionPolicy.Default with { EccAssumption = MemoryProtectionAssumption.AssumeProtected }).Resolve(environment);
        ResolvedExecutionPlan unprotectedPlan = (ExecutionPolicy.Default with { EccAssumption = MemoryProtectionAssumption.AssumeUnprotected }).Resolve(environment);
        Assert.AreNotEqual(protectedPlan.Protection.MemoryIsProtected, unprotectedPlan.Protection.MemoryIsProtected, "The ECC assumption must resolve the protection verdict, proving it is a consumed knob rather than dead.");

        using VeritasMemoryPool<byte> pool = new();
        using ArtifactImage segment = SegmentImage(SampleTriples(TripleCount), pool);
        CorruptSegmentBlock(segment, CorruptBlock, SegmentBlockCount);
        Assert.IsFalse(ItemSegment.RunVerifyRound(segment.Bytes).IsClean, "The at-rest verify takes no ExecutionPolicy, so corruption is detected regardless of the ECC assumption — it is correctness-inert.");
    }

    /// <summary>Dispatches the seam to its detector — the block seams run a decode-free verify round, the journal seam reopens a durable log, and the manifest seam re-reads a self-checksummed image, each resolving both the real and degenerate ids — and returns whether the detector reports the exemplar clean (no corruption surfaced).</summary>
    /// <param name="seam">The seam to verify.</param>
    /// <param name="checksum">The checksum algorithm the exemplar is written under.</param>
    /// <param name="corrupt">Whether to corrupt the exemplar before verifying.</param>
    /// <param name="pool">The pool the exemplar is rented from.</param>
    /// <returns>Whether the detector reports the exemplar clean.</returns>
    private static async Task<bool> VerifyIsClean(MutationSeam seam, ChecksumAlgorithm checksum, bool corrupt, MemoryPool<byte> pool)
    {
        if(seam == MutationSeam.JournalRecord)
        {
            return await VerifyJournalIsClean(checksum, corrupt, pool).ConfigureAwait(false);
        }

        if(seam == MutationSeam.ManifestSelf)
        {
            return VerifyManifestIsClean(checksum, corrupt, pool);
        }

        return VerifyBlockSeamIsClean(seam, checksum, corrupt, pool);
    }

    /// <summary>Stages the block seam's artifact under <paramref name="checksum"/>, optionally applies the seam's block corruption, and returns whether a decode-free verify round — resolving both the real and degenerate ids — reports it clean.</summary>
    /// <param name="seam">The block seam.</param>
    /// <param name="checksum">The checksum algorithm the image is written under.</param>
    /// <param name="corrupt">Whether to corrupt the seam's block before verifying.</param>
    /// <param name="pool">The pool the image is rented from.</param>
    /// <returns>Whether the verify round reports the image clean.</returns>
    private static bool VerifyBlockSeamIsClean(MutationSeam seam, ChecksumAlgorithm checksum, bool corrupt, MemoryPool<byte> pool)
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
            _ => throw new InvalidOperationException($"Seam {seam} is not a block seam."),
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

    /// <summary>Builds a durable journal log under <paramref name="checksum"/>, optionally tampers the first record's parent-id byte, reopens under the same algorithm, and returns whether replay found the log intact (no recovery report). Under the real algorithm a tampered record fails its checksum so replay reports a recovered tail; under the degenerate zero-checksum the tampered record verifies (zeros equal zeros) and decodes cleanly, so replay finds the log intact — the mutation.</summary>
    /// <param name="checksum">The record checksum algorithm.</param>
    /// <param name="corrupt">Whether to tamper a record before reopening.</param>
    /// <param name="pool">The pool the journal rents from.</param>
    /// <returns>Whether the reopened journal reports no recovery.</returns>
    private static async Task<bool> VerifyJournalIsClean(ChecksumAlgorithm checksum, bool corrupt, MemoryPool<byte> pool)
    {
        string directory = Directory.CreateTempSubdirectory("veritas-mutation-journal-").FullName;
        try
        {
            string path = Path.Combine(directory, "journal.log");
            FakeTimeProvider clock = new();
            using(FileBackedJournal journal = new(path, checksum, clock, pool))
            {
                await AppendJournalChain(journal, JournalRecordCount).ConfigureAwait(false);
            }

            if(corrupt)
            {
                byte[] bytes = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false);
                bytes[JournalFirstRecordParentByteOffset] ^= 0xFF;
                await File.WriteAllBytesAsync(path, bytes, CancellationToken.None).ConfigureAwait(false);
            }

            using FileBackedJournal reopened = new(path, checksum, clock, pool);

            return reopened.RecoveryReport is null;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Serializes a one-entry generation manifest under <paramref name="checksum"/>, optionally tampers a generation byte covered by the self-checksum, and returns whether the self-verifying read accepts it. Under the real algorithm a tampered byte fails the self-checksum and the read refuses it; under the degenerate zero-checksum the tampered image verifies (zeros equal zeros), so the read accepts it — the mutation.</summary>
    /// <param name="checksum">The manifest self-checksum algorithm.</param>
    /// <param name="corrupt">Whether to tamper a generation byte before reading.</param>
    /// <param name="pool">The pool the manifest buffer and digest rent from.</param>
    /// <returns>Whether the self-verifying read accepts the manifest.</returns>
    private static bool VerifyManifestIsClean(ChecksumAlgorithm checksum, bool corrupt, MemoryPool<byte> pool)
    {
        int digestWidth = checksum.ByteWidth;
        using IMemoryOwner<byte> digestOwner = pool.Rent(digestWidth);
        digestOwner.Memory.Span[..digestWidth].Clear();
        ManifestEntry[] entries = [new(ManifestFileRole.DataSegment, "segment.dat", 0, 100, digestOwner.Memory[..digestWidth])];
        Manifest manifest = new(ManifestGeneration, ManifestGeneration * 11, ManifestGeneration * 13, entries);

        int size = manifest.ComputeSerializedSize(checksum);
        using IMemoryOwner<byte> owner = pool.Rent(size);
        Span<byte> span = owner.Memory.Span[..size];
        manifest.WriteTo(span, checksum);
        if(corrupt)
        {
            span[ManifestCorruptionOffset] ^= 0xFF;
        }

        try
        {
            _ = Manifest.ReadFrom(span, DegenerateResolver);

            return true;
        }
        catch(InvalidDataException)
        {
            return false;
        }
    }
}
