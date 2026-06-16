using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Cbor.Internal;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The whole-index sidecar container: a standalone <see cref="ColumnarTripleIndex"/> round-trips
/// through its self-describing byte image — warm (the restored index re-serializes byte-identically),
/// logically faithful (the same triple set and count), across both order-set modes, both payload
/// backings, and the selectable per-blob checksum algorithms (none / XxHash3 / CRC-32 / a custom one
/// via an injected resolver); a corrupted blob is detected on load; an unknown checksum id is
/// rejected; and foreign/truncated/empty images are refused.
/// </summary>
[TestClass]
internal sealed class ColumnarTripleIndexSerializationTests
{
    /// <summary>The header byte offset of the checksum-algorithm id: magic (8) + major (1) + minor (1) + feature flags (8).</summary>
    private const int ChecksumAlgorithmIdOffset = 8 + 1 + 1 + 8;

    /// <summary>A test-only custom checksum (FNV-1a 64-bit) proving the algorithm seam is pluggable through an injected resolver.</summary>
    private static readonly ChecksumAlgorithm CustomChecksum = ChecksumAlgorithm.Create(200, "test-fnv1a-64", sizeof(ulong), ComputeFnv1a64);

    /// <summary>Computes a 64-bit FNV-1a hash — a self-contained deterministic test checksum.</summary>
    /// <param name="data">The bytes to hash.</param>
    /// <param name="destination">The 8-byte destination.</param>
    private static void ComputeFnv1a64(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        ulong hash = 14695981039346656037UL;
        for(int i = 0; i < data.Length; i++)
        {
            hash ^= data[i];
            hash *= 1099511628211UL;
        }

        BinaryPrimitives.WriteUInt64LittleEndian(destination, hash);
    }

    /// <summary>A resolver that knows the custom algorithm and falls back to the built-ins.</summary>
    /// <param name="id">The on-disk checksum-algorithm id.</param>
    /// <returns>The resolved algorithm, or <see langword="null"/>.</returns>
    private static ChecksumAlgorithm? ResolveWithCustom(byte id)
    {
        return id == CustomChecksum.Id ? CustomChecksum : ChecksumAlgorithm.DefaultResolver(id);
    }

    /// <summary>A few thousand distinct triples with grouped subjects/predicates/objects so the columns are multi-block and the succinct encodings engage.</summary>
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

    /// <summary>Writes the index to a pool-rented image, copies it out, and returns the bytes.</summary>
    /// <param name="index">The index to serialize.</param>
    /// <param name="checksum">The checksum algorithm, or <see langword="null"/>.</param>
    /// <returns>The image bytes.</returns>
    private static byte[] ToImage(ColumnarTripleIndex index, ChecksumAlgorithm? checksum)
    {
        using VeritasMemoryPool<byte> pool = new();
        int size = index.ComputeSerializedSize(checksum);
        using IMemoryOwner<byte> owner = pool.Rent(size);
        Span<byte> image = owner.Memory.Span[..size];
        index.WriteTo(image, checksum);

        return image.ToArray();
    }

    /// <summary>Asserts the index's merged triple set equals <paramref name="expected"/>.</summary>
    /// <param name="expected">The expected triples.</param>
    /// <param name="actual">The index to check.</param>
    private static void AssertSameTriples(IEnumerable<EncodedTriple> expected, ColumnarTripleIndex actual)
    {
        HashSet<EncodedTriple> expectedSet = [.. expected];
        HashSet<EncodedTriple> actualSet = [.. actual.EnumerateTriples()];
        Assert.IsTrue(expectedSet.SetEquals(actualSet), "The triple set differs after the round-trip.");
    }

    /// <summary>A delta-free index reloads warm — byte-identical on re-serialization and logically equal — across both modes, both backings, and every checksum selection (none / XxHash3 / CRC-32).</summary>
    [TestMethod]
    public void DeltaFreeIndexRoundTripsWarmAcrossModesBackingsAndChecksums()
    {
        EncodedTriple[] triples = SampleTriples();
        using VeritasMemoryPool<byte> pool = new();
        using VeritasMemoryPool<EncodedTriple> deltaPool = new();

        foreach(ColumnarOrderSetMode mode in (ColumnarOrderSetMode[])[ColumnarOrderSetMode.AllSixOrders, ColumnarOrderSetMode.ThreeRotations])
        {
            foreach(ColumnPayloadBacking backing in (ColumnPayloadBacking[])[ColumnPayloadBacking.Managed, ColumnPayloadBacking.NativeAligned])
            {
                foreach(ChecksumAlgorithm? checksum in (ChecksumAlgorithm?[])[null, ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
                {
                    ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples, mode, backing: backing);
                    int size = index.ComputeSerializedSize(checksum);
                    using IMemoryOwner<byte> imageOwner = pool.Rent(size);
                    Span<byte> image = imageOwner.Memory.Span[..size];
                    index.WriteTo(image, checksum);

                    ColumnarTripleIndex restored = ColumnarTripleIndex.ReadFrom(image, deltaPool);

                    Assert.AreEqual(mode, restored.OrderSetMode);
                    Assert.AreEqual(backing, restored.Backing);
                    Assert.AreEqual(index.TripleCount, restored.TripleCount);

                    int restoredSize = restored.ComputeSerializedSize(checksum);
                    Assert.AreEqual(size, restoredSize);
                    using IMemoryOwner<byte> reimageOwner = pool.Rent(restoredSize);
                    Span<byte> reimage = reimageOwner.Memory.Span[..restoredSize];
                    restored.WriteTo(reimage, checksum);
                    Assert.IsTrue(image.SequenceEqual(reimage), $"The re-serialized image differs (mode {mode}, backing {backing}, checksum {checksum?.Name ?? "none"}).");

                    AssertSameTriples(triples, restored);
                }
            }
        }
    }

    /// <summary>An index carrying an accumulated delta round-trips to the same merged triple set, delta preserved.</summary>
    [TestMethod]
    public void IndexWithDeltaRoundTripsLogically()
    {
        EncodedTriple[] triples = SampleTriples();
        ColumnarTripleIndex baseIndex = ColumnarTripleIndex.Build(triples);
        EncodedTriple[] additions = [EncodedTriple.FromEncoded(9000, 9001, 9002), EncodedTriple.FromEncoded(9000, 9001, 9003)];
        EncodedTriple[] removals = [triples[0], triples[1]];
        ColumnarTripleIndex withDelta = baseIndex.Apply(additions, removals);
        Assert.IsTrue(withDelta.HasDelta);

        using VeritasMemoryPool<byte> pool = new();
        using VeritasMemoryPool<EncodedTriple> deltaPool = new();
        int size = withDelta.ComputeSerializedSize(ChecksumAlgorithm.XxHash3);
        using IMemoryOwner<byte> owner = pool.Rent(size);
        Span<byte> image = owner.Memory.Span[..size];
        withDelta.WriteTo(image, ChecksumAlgorithm.XxHash3);

        ColumnarTripleIndex restored = ColumnarTripleIndex.ReadFrom(image, deltaPool);

        Assert.AreEqual(withDelta.TripleCount, restored.TripleCount);
        Assert.IsTrue(restored.HasDelta, "Warm start must preserve the delta over the base, not re-compact it away.");
        AssertSameTriples(withDelta.EnumerateTriples(), restored);
    }

    /// <summary>An index written into a pooled buffer writer and read back from the resulting sequence through <see cref="ColumnarIndexFile"/> reloads to the same merged triple set.</summary>
    [TestMethod]
    public void BufferRoundTripWarmStarts()
    {
        EncodedTriple[] triples = SampleTriples();
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples, ColumnarOrderSetMode.ThreeRotations);

        using VeritasMemoryPool<byte> pool = new();
        using VeritasMemoryPool<EncodedTriple> deltaPool = new();
        using SlabBufferWriter writer = new(pool);
        ColumnarIndexFile.Write(index, writer, ChecksumAlgorithm.XxHash3);
        using IMemoryOwner<byte> written = writer.Detach();
        ColumnarTripleIndex restored = ColumnarIndexFile.Read(new ReadOnlySequence<byte>(written.Memory), pool, deltaPool);

        Assert.AreEqual(index.OrderSetMode, restored.OrderSetMode);
        Assert.AreEqual(index.TripleCount, restored.TripleCount);
        AssertSameTriples(triples, restored);
    }

    /// <summary>A blob corrupted after writing is detected on load by its checksum rather than silently mis-decoded.</summary>
    [TestMethod]
    public void ChecksumDetectsBlobCorruption()
    {
        EncodedTriple[] triples = SampleTriples();
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);
        byte[] image = ToImage(index, ChecksumAlgorithm.XxHash3);

        //The last byte is the last byte of the last column blob; flipping it must fail that blob's checksum.
        image[^1] ^= 0xFF;

        using VeritasMemoryPool<EncodedTriple> deltaPool = new();
        Assert.ThrowsExactly<InvalidDataException>(() => { _ = ColumnarTripleIndex.ReadFrom(image, deltaPool); });
    }

    /// <summary>An image whose checksum-algorithm id no resolver knows is rejected as unsupported.</summary>
    [TestMethod]
    public void UnknownChecksumAlgorithmIsRejected()
    {
        EncodedTriple[] triples = SampleTriples();
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);
        byte[] image = ToImage(index, ChecksumAlgorithm.XxHash3);

        image[ChecksumAlgorithmIdOffset] = 99;

        using VeritasMemoryPool<EncodedTriple> deltaPool = new();
        Assert.ThrowsExactly<NotSupportedException>(() => { _ = ColumnarTripleIndex.ReadFrom(image, deltaPool); });
    }

    /// <summary>A custom checksum algorithm round-trips when the reader injects a resolver that knows it, and is rejected by the default resolver.</summary>
    [TestMethod]
    public void CustomChecksumRoundTripsViaInjectedResolver()
    {
        EncodedTriple[] triples = SampleTriples();
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);
        byte[] image = ToImage(index, CustomChecksum);

        using VeritasMemoryPool<EncodedTriple> deltaPool = new();
        ColumnarTripleIndex restored = ColumnarTripleIndex.ReadFrom(image, deltaPool, ResolveWithCustom);
        AssertSameTriples(triples, restored);

        Assert.ThrowsExactly<NotSupportedException>(() => { _ = ColumnarTripleIndex.ReadFrom(image, deltaPool); });
    }

    /// <summary>Bytes that are not a columnar index image are rejected rather than mis-read.</summary>
    [TestMethod]
    public void ForeignImageIsRejected()
    {
        byte[] garbage = new byte[64];
        using VeritasMemoryPool<EncodedTriple> deltaPool = new();

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = ColumnarTripleIndex.ReadFrom(garbage, deltaPool); });
    }

    /// <summary>A truncated image — a valid prefix cut short — is rejected as malformed rather than crashing with an out-of-range read.</summary>
    [TestMethod]
    public void TruncatedImageIsRejected()
    {
        EncodedTriple[] triples = SampleTriples();
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);
        byte[] image = ToImage(index, ChecksumAlgorithm.XxHash3);
        byte[] truncated = image[..(image.Length / 2)];

        using VeritasMemoryPool<EncodedTriple> deltaPool = new();
        Assert.ThrowsExactly<InvalidDataException>(() => { _ = ColumnarTripleIndex.ReadFrom(truncated, deltaPool); });
    }

    /// <summary>An empty image — single-segment or segmented — is reported as not-an-image rather than failing inside the buffer rent.</summary>
    [TestMethod]
    public void EmptyImageIsRejected()
    {
        using VeritasMemoryPool<byte> pool = new();
        using VeritasMemoryPool<EncodedTriple> deltaPool = new();

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = ColumnarIndexFile.Read(ReadOnlySequence<byte>.Empty, pool, deltaPool); });

        ByteSegment first = new(ReadOnlyMemory<byte>.Empty);
        ByteSegment last = first.Append(ReadOnlyMemory<byte>.Empty);
        ReadOnlySequence<byte> multiSegmentEmpty = new(first, 0, last, 0);
        Assert.ThrowsExactly<InvalidDataException>(() => { _ = ColumnarIndexFile.Read(multiSegmentEmpty, pool, deltaPool); });
    }

    /// <summary>A minimal multi-segment builder for exercising segmented <see cref="ReadOnlySequence{T}"/> inputs.</summary>
    private sealed class ByteSegment : System.Buffers.ReadOnlySequenceSegment<byte>
    {
        /// <summary>Creates a segment over <paramref name="memory"/>.</summary>
        /// <param name="memory">The segment's memory.</param>
        public ByteSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        /// <summary>Appends a following segment and returns it.</summary>
        /// <param name="memory">The next segment's memory.</param>
        /// <returns>The appended segment.</returns>
        public ByteSegment Append(ReadOnlyMemory<byte> memory)
        {
            ByteSegment next = new(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;

            return next;
        }
    }
}
