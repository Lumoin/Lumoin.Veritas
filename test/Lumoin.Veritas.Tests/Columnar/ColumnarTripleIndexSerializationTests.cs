using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The whole-index sidecar container: a standalone <see cref="ColumnarTripleIndex"/> round-trips
/// through its self-describing byte image — warm (the restored index re-serializes byte-identically,
/// proving every column reloaded without re-packing), logically faithful (the same triple set and
/// count), across both order-set modes and both payload backings, over a span and over a stream;
/// an accumulated delta round-trips logically; and a foreign image is rejected.
/// </summary>
[TestClass]
internal sealed class ColumnarTripleIndexSerializationTests
{
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

    /// <summary>Asserts the index's merged triple set equals <paramref name="expected"/>.</summary>
    /// <param name="expected">The expected triples.</param>
    /// <param name="actual">The index to check.</param>
    private static void AssertSameTriples(IEnumerable<EncodedTriple> expected, ColumnarTripleIndex actual)
    {
        HashSet<EncodedTriple> expectedSet = [.. expected];
        HashSet<EncodedTriple> actualSet = [.. actual.EnumerateTriples()];
        Assert.IsTrue(expectedSet.SetEquals(actualSet), "The triple set differs after the round-trip.");
    }

    /// <summary>A delta-free index reloads warm — byte-identical on re-serialization and logically equal — across both modes and both backings.</summary>
    [TestMethod]
    public void DeltaFreeIndexRoundTripsWarmAcrossModesAndBackings()
    {
        EncodedTriple[] triples = SampleTriples();
        using VeritasMemoryPool<byte> pool = new();
        using VeritasMemoryPool<EncodedTriple> deltaPool = new();

        foreach(ColumnarOrderSetMode mode in (ColumnarOrderSetMode[])[ColumnarOrderSetMode.AllSixOrders, ColumnarOrderSetMode.ThreeRotations])
        {
            foreach(ColumnPayloadBacking backing in (ColumnPayloadBacking[])[ColumnPayloadBacking.Managed, ColumnPayloadBacking.NativeAligned])
            {
                ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples, mode, backing: backing);
                int size = index.SerializedSize;
                using IMemoryOwner<byte> imageOwner = pool.Rent(size);
                Span<byte> image = imageOwner.Memory.Span[..size];
                index.WriteTo(image);

                ColumnarTripleIndex restored = ColumnarTripleIndex.ReadFrom(image, deltaPool);

                Assert.AreEqual(mode, restored.OrderSetMode);
                Assert.AreEqual(backing, restored.Backing);
                Assert.AreEqual(index.TripleCount, restored.TripleCount);

                int restoredSize = restored.SerializedSize;
                Assert.AreEqual(size, restoredSize);
                using IMemoryOwner<byte> reimageOwner = pool.Rent(restoredSize);
                Span<byte> reimage = reimageOwner.Memory.Span[..restoredSize];
                restored.WriteTo(reimage);
                Assert.IsTrue(image.SequenceEqual(reimage), $"The re-serialized image differs (mode {mode}, backing {backing}).");

                AssertSameTriples(triples, restored);
            }
        }
    }

    /// <summary>An index carrying an accumulated delta round-trips to the same merged triple set.</summary>
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
        int size = withDelta.SerializedSize;
        using IMemoryOwner<byte> owner = pool.Rent(size);
        Span<byte> image = owner.Memory.Span[..size];
        withDelta.WriteTo(image);

        ColumnarTripleIndex restored = ColumnarTripleIndex.ReadFrom(image, deltaPool);

        Assert.AreEqual(withDelta.TripleCount, restored.TripleCount);
        Assert.IsTrue(restored.HasDelta, "Warm start must preserve the delta over the base, not re-compact it away.");
        AssertSameTriples(withDelta.EnumerateTriples(), restored);
    }

    /// <summary>An index written and read back over a stream through <see cref="ColumnarIndexFile"/> reloads to the same merged triple set.</summary>
    [TestMethod]
    public void StreamRoundTripWarmStarts()
    {
        EncodedTriple[] triples = SampleTriples();
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples, ColumnarOrderSetMode.ThreeRotations);

        using VeritasMemoryPool<byte> pool = new();
        using VeritasMemoryPool<EncodedTriple> deltaPool = new();
        using MemoryStream stream = new();
        ColumnarIndexFile.Write(index, stream, pool);
        stream.Position = 0;
        ColumnarTripleIndex restored = ColumnarIndexFile.Read(stream, pool, deltaPool);

        Assert.AreEqual(index.OrderSetMode, restored.OrderSetMode);
        Assert.AreEqual(index.TripleCount, restored.TripleCount);
        AssertSameTriples(triples, restored);
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

        using VeritasMemoryPool<byte> pool = new();
        int size = index.SerializedSize;
        using IMemoryOwner<byte> owner = pool.Rent(size);
        Span<byte> image = owner.Memory.Span[..size];
        index.WriteTo(image);
        byte[] truncated = image[..(size / 2)].ToArray();
        using VeritasMemoryPool<EncodedTriple> deltaPool = new();

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = ColumnarTripleIndex.ReadFrom(truncated, deltaPool); });
    }

    /// <summary>An empty stream is reported as not-an-image rather than failing inside the buffer rent.</summary>
    [TestMethod]
    public void EmptyStreamIsRejected()
    {
        using MemoryStream stream = new();
        using VeritasMemoryPool<byte> pool = new();
        using VeritasMemoryPool<EncodedTriple> deltaPool = new();

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = ColumnarIndexFile.Read(stream, pool, deltaPool); });
    }
}
