using System;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Tests.Memory;

/// <summary>
/// The native byte-source primitive: a 64-byte-aligned unmanaged block that round-trips
/// its words across block boundaries, treats the empty block as a zero-length span, frees
/// idempotently, and turns a read after free into an <see cref="ObjectDisposedException"/>
/// rather than touching freed memory.
/// </summary>
[TestClass]
internal sealed class AlignedNativeBufferTests
{
    /// <summary>Words written through the span read back identically.</summary>
    [TestMethod]
    public void RoundTripsWords()
    {
        using AlignedNativeBuffer buffer = AlignedNativeBuffer.Allocate(257);

        Assert.AreEqual(257, buffer.Length);

        Span<ulong> span = buffer.Span;
        for(int i = 0; i < span.Length; i++)
        {
            span[i] = unchecked((ulong)i * 0x9E3779B97F4A7C15UL);
        }

        Span<ulong> reread = buffer.Span;
        for(int i = 0; i < reread.Length; i++)
        {
            Assert.AreEqual(unchecked((ulong)i * 0x9E3779B97F4A7C15UL), reread[i]);
        }
    }

    /// <summary>An empty block allocates nothing and presents a zero-length span.</summary>
    [TestMethod]
    public void EmptyBlockIsZeroLength()
    {
        using AlignedNativeBuffer buffer = AlignedNativeBuffer.Allocate(0);

        Assert.AreEqual(0, buffer.Length);
        Assert.IsTrue(buffer.Span.IsEmpty);
    }

    /// <summary>A negative length is rejected.</summary>
    [TestMethod]
    public void NegativeLengthThrows()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AlignedNativeBuffer.Allocate(-1));
    }

    /// <summary>Reading a freed non-empty block throws rather than touching freed memory.</summary>
    [TestMethod]
    public void SpanAfterDisposeThrows()
    {
        AlignedNativeBuffer buffer = AlignedNativeBuffer.Allocate(4);
        buffer.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = buffer.Span.Length);
    }

    /// <summary>Disposing twice is safe — the block frees exactly once.</summary>
    [TestMethod]
    public void DoubleDisposeIsSafe()
    {
        AlignedNativeBuffer buffer = AlignedNativeBuffer.Allocate(4);

        buffer.Dispose();
        buffer.Dispose();
    }
}
