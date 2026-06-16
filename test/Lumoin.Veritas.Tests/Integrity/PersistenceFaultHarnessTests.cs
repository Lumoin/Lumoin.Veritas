using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The P1-reachable subset of the persistence fault-injection matrix: seeded, deterministic
/// damage is injected into a checksummed columnar index image and the verify round must DETECT it.
/// Byte-changing damage to any column blob is caught per blob (<see cref="PersistenceInvariant.DetectionPrecedesUse"/>)
/// and refused by the warm reader before any byte is decoded; a foreign checksum-algorithm id is
/// refused (<see cref="PersistenceInvariant.EpochConsistency"/>). Cells whose correct outcome is
/// repair, named loss, atomic publish, or peer convergence are out of P1 scope (they need the
/// repair ladder, manifest, or a peer) and land with those tiers. No wall-clock, no GUID — the
/// injector is a seeded xorshift; the cells are enumerated by construction, not sampled.
/// </summary>
[TestClass]
internal sealed class PersistenceFaultHarnessTests
{
    /// <summary>The header byte offset of the checksum-algorithm id: magic (8) + major (1) + minor (1) + feature flags (8).</summary>
    private const int ChecksumAlgorithmIdOffset = 8 + 1 + 1 + 8;

    /// <summary>The byte-changing damage kinds the P1 byte sources (the in-memory image) admit.</summary>
    private enum CorruptionKind
    {
        /// <summary>A single bit flipped within the blob.</summary>
        BitFlip,

        /// <summary>The whole blob overwritten with garbage.</summary>
        WholeBlockGarbage,

        /// <summary>The image truncated so the blob runs past the end.</summary>
        Truncate,
    }

    /// <summary>A few thousand distinct triples so the index has multiple non-trivial column blobs to damage.</summary>
    /// <returns>The sample triples.</returns>
    private static EncodedTriple[] SampleTriples()
    {
        List<EncodedTriple> triples = [];
        for(uint s = 0; s < 200; s++)
        {
            for(uint p = 0; p < 5; p++)
            {
                for(uint o = 0; o < 3; o++)
                {
                    triples.Add(EncodedTriple.FromEncoded(s, p * 10, (s * 7) + o));
                }
            }
        }

        return [.. triples];
    }

    /// <summary>Serializes an index to a checksummed image and returns the bytes.</summary>
    /// <param name="index">The index to serialize.</param>
    /// <returns>The image bytes.</returns>
    private static byte[] ToImage(ColumnarTripleIndex index)
    {
        using VeritasMemoryPool<byte> pool = new();
        int size = index.ComputeSerializedSize(ChecksumAlgorithm.XxHash3);
        using IMemoryOwner<byte> owner = pool.Rent(size);
        Span<byte> image = owner.Memory.Span[..size];
        index.WriteTo(image, ChecksumAlgorithm.XxHash3);

        return image.ToArray();
    }

    /// <summary>Advances a non-zero xorshift64 state and returns the next value.</summary>
    /// <param name="state">The generator state; mutated in place.</param>
    /// <returns>The next pseudo-random value.</returns>
    private static ulong NextXorshift(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;

        return state;
    }

    /// <summary>Produces a damaged copy of <paramref name="clean"/> for one cell, driven by a seed so the run is reproducible.</summary>
    /// <param name="clean">The clean image.</param>
    /// <param name="blob">The blob to damage.</param>
    /// <param name="kind">The damage kind.</param>
    /// <param name="seed">The xorshift seed.</param>
    /// <returns>The damaged image.</returns>
    private static byte[] Corrupt(byte[] clean, BlobVerdict blob, CorruptionKind kind, ulong seed)
    {
        ulong state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        if(kind == CorruptionKind.Truncate)
        {
            return clean[..((int)blob.ByteOffset + (int)blob.ByteLength - 1)];
        }

        byte[] copy = [.. clean];
        if(kind == CorruptionKind.BitFlip)
        {
            int position = (int)blob.ByteOffset + (int)(NextXorshift(ref state) % (ulong)blob.ByteLength);
            copy[position] ^= (byte)(1 << (int)(NextXorshift(ref state) & 7));

            return copy;
        }

        for(long i = blob.ByteOffset; i < blob.ByteOffset + blob.ByteLength; i++)
        {
            copy[i] = (byte)NextXorshift(ref state);
        }

        //Guarantee at least one byte differs from the original so the damage is real even if the garbage coincided with the clean bytes.
        copy[blob.ByteOffset] = (byte)(clean[blob.ByteOffset] ^ 0xFF);

        return copy;
    }

    /// <summary>A clean image verifies clean — every blob passes and nothing is reported corrupt.</summary>
    [TestMethod]
    public void CleanImageVerifiesClean()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(SampleTriples());
        byte[] image = ToImage(index);

        VerifyRoundReport report = ColumnarTripleIndex.RunVerifyRound(image);

        Assert.IsTrue(report.IsClean);
        Assert.IsGreaterThan(0, report.BlobCount);
        Assert.AreEqual(0, report.CorruptCount);
    }

    /// <summary>I1: a bit flip or whole-block garbage in ANY blob is detected — exactly that blob is reported corrupt by the round, and the warm reader refuses the image before decoding.</summary>
    [TestMethod]
    public void EveryBlobByteCorruptionIsDetected()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(SampleTriples());
        byte[] clean = ToImage(index);
        VerifyRoundReport cleanReport = ColumnarTripleIndex.RunVerifyRound(clean);
        Assert.IsTrue(cleanReport.IsClean);

        using VeritasMemoryPool<EncodedTriple> deltaPool = new();
        ulong seed = 1;
        foreach(BlobVerdict blob in cleanReport.Blobs)
        {
            foreach(CorruptionKind kind in (CorruptionKind[])[CorruptionKind.BitFlip, CorruptionKind.WholeBlockGarbage])
            {
                byte[] corrupt = Corrupt(clean, blob, kind, seed);
                seed++;

                VerifyRoundReport report = ColumnarTripleIndex.RunVerifyRound(corrupt);
                Assert.IsFalse(report.Blobs[blob.Index].IsValid, $"blob {blob.Index} kind {kind} not reported corrupt");
                Assert.AreEqual(1, report.CorruptCount, $"blob {blob.Index} kind {kind} corrupted more than one verdict");

                Assert.ThrowsExactly<InvalidDataException>(() => { _ = ColumnarTripleIndex.ReadFrom(corrupt, deltaPool); });
            }
        }
    }

    /// <summary>I1 (framing): a truncated blob is refused outright rather than mis-read — the directory claims bytes the image no longer has.</summary>
    [TestMethod]
    public void TruncatedBlobIsRefused()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(SampleTriples());
        byte[] clean = ToImage(index);
        VerifyRoundReport cleanReport = ColumnarTripleIndex.RunVerifyRound(clean);

        BlobVerdict last = cleanReport.Blobs[cleanReport.BlobCount - 1];
        byte[] truncated = Corrupt(clean, last, CorruptionKind.Truncate, 1);

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = ColumnarTripleIndex.RunVerifyRound(truncated); });
    }

    /// <summary>I5 (epoch consistency): an image stamped with a checksum-algorithm id no resolver knows is refused, not verified under the wrong algorithm.</summary>
    [TestMethod]
    public void ForeignChecksumAlgorithmIsRefused()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(SampleTriples());
        byte[] image = ToImage(index);
        image[ChecksumAlgorithmIdOffset] = 99;

        Assert.ThrowsExactly<NotSupportedException>(() => { _ = ColumnarTripleIndex.RunVerifyRound(image); });
    }

    /// <summary>I1 (front matter): a byte corrupted in the persisted delta — which the per-blob checksums do not cover — is caught by the front-matter checksum: reported by the verify round and refused by the warm reader before the delta is used.</summary>
    [TestMethod]
    public void FrontMatterCorruptionIsDetected()
    {
        ColumnarTripleIndex withDelta = ColumnarTripleIndex.Build(SampleTriples()).Apply([EncodedTriple.FromEncoded(900_000, 1, 2)], []);
        byte[] clean = ToImage(withDelta);

        //A byte inside the added-delta triple data: header (19) + scalars (8) + added-count (4) = 31.
        byte[] corrupt = [.. clean];
        corrupt[31] ^= 0xFF;

        VerifyRoundReport report = ColumnarTripleIndex.RunVerifyRound(corrupt);
        Assert.IsFalse(report.FrontMatterValid);
        Assert.IsFalse(report.IsClean);

        using VeritasMemoryPool<EncodedTriple> deltaPool = new();
        Assert.ThrowsExactly<InvalidDataException>(() => { _ = ColumnarTripleIndex.ReadFrom(corrupt, deltaPool); });
    }
}
