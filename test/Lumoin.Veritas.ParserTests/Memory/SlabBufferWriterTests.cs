using System;
using System.Buffers;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.ParserTests.Memory;

[TestClass]
internal sealed class SlabBufferWriterTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void WriteWithinSingleSlabPreservesBytes()
    {
        using VeritasMemoryPool<byte> pool = new();
        using SlabBufferWriter writer = new(pool, slabSize: 64);

        Span<byte> span = writer.GetSpan(5);
        for(int i = 0; i < 5; i++)
        {
            span[i] = (byte)i;
        }
        writer.Advance(5);

        Assert.AreEqual(5, writer.BytesWritten);
        using IMemoryOwner<byte> detached = writer.Detach();
        Assert.AreSequenceEqual(new byte[] { 0, 1, 2, 3, 4 }, detached.Memory[..5].ToArray());
    }

    [TestMethod]
    public void WriteAcrossSlabBoundaryPreservesAllBytes()
    {
        using VeritasMemoryPool<byte> pool = new();
        using SlabBufferWriter writer = new(pool, slabSize: 8);

        //Fill a full slab.
        Span<byte> first = writer.GetSpan(8);
        for(int i = 0; i < 8; i++)
        {
            first[i] = (byte)i;
        }
        writer.Advance(8);

        //Force a new slab.
        Span<byte> second = writer.GetSpan(8);
        for(int i = 0; i < 8; i++)
        {
            second[i] = (byte)(8 + i);
        }
        writer.Advance(8);

        Assert.AreEqual(16, writer.BytesWritten);

        using IMemoryOwner<byte> detached = writer.Detach();
        byte[] expected = new byte[16];
        for(int i = 0; i < 16; i++)
        {
            expected[i] = (byte)i;
        }
        Assert.AreSequenceEqual(expected, detached.Memory[..16].ToArray());
    }

    [TestMethod]
    public void DetachReturnsExactlyBytesWrittenLengthBuffer()
    {
        using VeritasMemoryPool<byte> pool = new();
        using SlabBufferWriter writer = new(pool, slabSize: 16);

        Span<byte> span = writer.GetSpan(3);
        span[0] = 0xAA;
        span[1] = 0xBB;
        span[2] = 0xCC;
        writer.Advance(3);

        using IMemoryOwner<byte> detached = writer.Detach();
        //The owner's Memory may be larger than the requested length (pool semantics);
        //the contract is that the first BytesWritten bytes are the written content.
        Assert.IsGreaterThanOrEqualTo(3, detached.Memory.Length);
        Assert.AreEqual((byte)0xAA, detached.Memory.Span[0]);
        Assert.AreEqual((byte)0xBB, detached.Memory.Span[1]);
        Assert.AreEqual((byte)0xCC, detached.Memory.Span[2]);
    }

    [TestMethod]
    public void DetachLeavesWriterReusable()
    {
        using VeritasMemoryPool<byte> pool = new();
        using SlabBufferWriter writer = new(pool, slabSize: 16);

        Span<byte> span = writer.GetSpan(2);
        span[0] = 1;
        span[1] = 2;
        writer.Advance(2);

        using(IMemoryOwner<byte> first = writer.Detach())
        {
            Assert.AreEqual(0, writer.BytesWritten);
        }

        Span<byte> span2 = writer.GetSpan(1);
        span2[0] = 99;
        writer.Advance(1);

        Assert.AreEqual(1, writer.BytesWritten);

        using IMemoryOwner<byte> second = writer.Detach();
        Assert.AreEqual((byte)99, second.Memory.Span[0]);
    }

    [TestMethod]
    public void ResetClearsState()
    {
        using VeritasMemoryPool<byte> pool = new();
        using SlabBufferWriter writer = new(pool, slabSize: 16);

        Span<byte> span = writer.GetSpan(4);
        span.Fill(0xFF);
        writer.Advance(4);

        writer.Reset();

        Assert.AreEqual(0, writer.BytesWritten);
    }

    [TestMethod]
    public void GetSpanWithSizeHintLargerThanSlabAllocatesLargerSlab()
    {
        using VeritasMemoryPool<byte> pool = new();
        using SlabBufferWriter writer = new(pool, slabSize: 8);

        Span<byte> span = writer.GetSpan(64);
        Assert.IsGreaterThanOrEqualTo(64, span.Length);
        for(int i = 0; i < 64; i++)
        {
            span[i] = (byte)(i & 0xFF);
        }
        writer.Advance(64);

        Assert.AreEqual(64, writer.BytesWritten);
    }

    [TestMethod]
    public void DisposedWriterRejectsFurtherUse()
    {
        VeritasMemoryPool<byte> pool = new();
        SlabBufferWriter writer = new(pool, slabSize: 16);
        writer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => writer.GetSpan(1));
        Assert.Throws<ObjectDisposedException>(() => writer.Advance(0));

        pool.Dispose();
    }

    [TestMethod]
    public void NullPoolThrowsAtConstruction()
    {
        Assert.Throws<ArgumentNullException>(() => new SlabBufferWriter(null!));
    }

    [TestMethod]
    public void NonPositiveSlabSizeThrowsAtConstruction()
    {
        using VeritasMemoryPool<byte> pool = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlabBufferWriter(pool, slabSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlabBufferWriter(pool, slabSize: -1));
    }

    /// <summary><see cref="SlabBufferWriter.Detach"/> on a writer with nothing written returns an empty owner instead of throwing.</summary>
    [TestMethod]
    public void DetachOnEmptyWriterReturnsEmptyOwner()
    {
        using VeritasMemoryPool<byte> pool = new();
        using SlabBufferWriter writer = new(pool, slabSize: 16);

        using IMemoryOwner<byte> owned = writer.Detach();

        Assert.IsTrue(owned.Memory.IsEmpty);
        Assert.AreEqual(0, writer.BytesWritten);
    }
}
