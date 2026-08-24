using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Lumoin.Veritas.Cbor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Cbor;

[TestClass]
internal sealed class CborReaderStreamingTests
{
    [TestMethod]
    public void MultiSegmentSequenceDoesNotAllocateAtConstruction()
    {
        //Build a small multi-segment ReadOnlySequence<byte>. The
        //construction of CborReader over it must not materialise the
        //sequence into a contiguous buffer.
        ReadOnlySequence<byte> sequence = BuildMultiSegmentSequence(
            [[0x01], [0x02], [0x03, 0x04, 0x05]]);

        //Warm up: trigger JIT compilation of the constructor before
        //measuring, so the allocation we measure reflects only the
        //reader instance + its fixed state.
        _ = new CborReader(sequence, CborSerializerOptions.Default(CborConformanceMode.Lax));

        long before = GC.GetAllocatedBytesForCurrentThread();
        CborReader reader = new(sequence, CborSerializerOptions.Default(CborConformanceMode.Lax));
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.IsLessThanOrEqualTo(256L, after - before);
        Assert.AreEqual(0, reader.BytesConsumed);
    }

    [TestMethod]
    public void CrossSegmentIntegerReadProducesCorrectValue()
    {
        //CBOR unsigned int 0x1234 encodes as: 0x19 0x12 0x34.
        //Split the bytes across two segments.
        ReadOnlySequence<byte> sequence = BuildMultiSegmentSequence(
            [[0x19, 0x12], [0x34]]);
        CborReader reader = new(sequence, CborSerializerOptions.Default(CborConformanceMode.Lax));

        ulong value = reader.ReadUInt64();

        Assert.AreEqual(0x1234UL, value);
    }

    [TestMethod]
    public void CrossSegmentByteStringSpanThrows()
    {
        //Byte string of length 4 (0x44 + 4 payload bytes), with bytes
        //split across two segments.
        ReadOnlySequence<byte> sequence = BuildMultiSegmentSequence(
            [[0x44, 0x01, 0x02], [0x03, 0x04]]);
        CborReader reader = new(sequence, CborSerializerOptions.Default(CborConformanceMode.Lax));

        Assert.ThrowsExactly<InvalidOperationException>(() => _ = reader.ReadByteStringSpan());
    }

    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public void CrossSegmentByteStringPooledAssemblesCorrectly()
    {
        ReadOnlySequence<byte> sequence = BuildMultiSegmentSequence(
            [[0x44, 0x01, 0x02], [0x03, 0x04]]);
        CborReader reader = new(sequence, CborSerializerOptions.Default(CborConformanceMode.Lax));

        using IMemoryOwner<byte> owned = reader.ReadByteStringPooled();

        Assert.AreEqual(4, owned.Memory.Length);
        Assert.AreEqual(0x01, owned.Memory.Span[0]);
        Assert.AreEqual(0x04, owned.Memory.Span[3]);
    }

    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public void CrossSegmentByteStringMemoryCopies()
    {
        //ReadByteStringMemory on cross-segment data must produce a
        //correct-content ReadOnlyMemory<byte>, allocated outside the
        //source. The caller doesn't manage disposal; GC handles it.
        ReadOnlySequence<byte> sequence = BuildMultiSegmentSequence(
            [[0x44, 0x01, 0x02], [0x03, 0x04]]);
        CborReader reader = new(sequence, CborSerializerOptions.Default(CborConformanceMode.Lax));

        ReadOnlyMemory<byte> result = reader.ReadByteStringMemory();

        Assert.AreEqual(4, result.Length);
        Assert.AreEqual(0x01, result.Span[0]);
        Assert.AreEqual(0x04, result.Span[3]);
    }

    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public void SingleSegmentByteStringSpanIsZeroCopy()
    {
        byte[] backing = [0x44, 0x01, 0x02, 0x03, 0x04];
        CborReader reader = new(backing, CborSerializerOptions.Default(CborConformanceMode.Lax));

        ReadOnlySpan<byte> result = reader.ReadByteStringSpan();

        Assert.AreEqual(4, result.Length);
        Assert.AreEqual(backing[1], result[0]);
        Assert.AreEqual(backing[4], result[3]);
    }

    [TestMethod]
    public void CrossSegmentTextStringReadsCorrectly()
    {
        //UTF-8 "hello" split across two segments.
        //Header: 0x65 (text string of length 5).
        ReadOnlySequence<byte> sequence = BuildMultiSegmentSequence(
            [[0x65, (byte)'h', (byte)'e'], [(byte)'l', (byte)'l', (byte)'o']]);
        CborReader reader = new(sequence, CborSerializerOptions.Default(CborConformanceMode.Lax));

        string value = reader.ReadTextString();

        Assert.AreEqual("hello", value);
    }

    [TestMethod]
    public void BytesConsumedLongTracksAcrossSegments()
    {
        ReadOnlySequence<byte> sequence = BuildMultiSegmentSequence(
            [[0x19, 0x12], [0x34], [0x18, 0x07]]);
        CborReader reader = new(sequence, CborSerializerOptions.Default(CborConformanceMode.Lax));

        ulong first = reader.ReadUInt64();
        Assert.AreEqual(0x1234UL, first);
        Assert.AreEqual(3L, reader.BytesConsumedLong);

        ulong second = reader.ReadUInt64();
        Assert.AreEqual(7UL, second);
        Assert.AreEqual(5L, reader.BytesConsumedLong);
    }

    private static ReadOnlySequence<byte> BuildMultiSegmentSequence(byte[][] segmentPayloads)
    {
        //Build a linked chain of ReadOnlySequenceSegment<byte>.
        MemorySegment? first = null;
        MemorySegment? current = null;
        foreach(byte[] payload in segmentPayloads)
        {
            MemorySegment seg = new(payload);
            if(first is null)
            {
                first = seg;
                current = seg;
            }
            else
            {
                current = current!.Append(payload);
            }
        }
        return new ReadOnlySequence<byte>(first!, 0, current!, current!.Memory.Length);
    }

    private sealed class MemorySegment: ReadOnlySequenceSegment<byte>
    {
        public MemorySegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public MemorySegment Append(ReadOnlyMemory<byte> memory)
        {
            MemorySegment seg = new(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = seg;
            return seg;
        }
    }
}
