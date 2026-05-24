using System;
using System.Buffers;
using System.Text;
using Lumoin.Veritas.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core;

[TestClass]
internal sealed class Utf8StringPoolTests
{
    [TestMethod]
    public void InternReturnsSameInstanceForDuplicateBytes()
    {
        using Utf8StringPool pool = new();

        Utf8String first = pool.Intern("http://example.org/resource"u8);
        Utf8String second = pool.Intern("http://example.org/resource"u8);

        Assert.AreEqual(first, second);
        Assert.AreEqual(1, pool.Count);
    }

    [TestMethod]
    public void InternDistinguishesDifferentValues()
    {
        using Utf8StringPool pool = new();

        Utf8String a = pool.Intern("alpha"u8);
        Utf8String b = pool.Intern("beta"u8);

        Assert.AreNotEqual(a, b);
        Assert.AreEqual(2, pool.Count);
    }

    [TestMethod]
    public void InternStringEncodesAsUtf8()
    {
        using Utf8StringPool pool = new();

        Utf8String interned = pool.Intern("hello");

        Assert.AreEqual("hello", interned.ToString());
        Assert.AreEqual(5, interned.Length);
    }

    [TestMethod]
    public void InternStringDeduplicatesWithByteVersion()
    {
        using Utf8StringPool pool = new();

        Utf8String fromBytes = pool.Intern("test"u8);
        Utf8String fromString = pool.Intern("test");

        Assert.AreEqual(fromBytes, fromString);
        Assert.AreEqual(1, pool.Count);
    }

    [TestMethod]
    public void DisposePreventsFurtherInterning()
    {
        Utf8StringPool pool = new();
        pool.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => pool.Intern("test"u8));
    }

    [TestMethod]
    public void HandlesLargeValuesSpanningMultipleBuffers()
    {
        //Use a tiny initial buffer to force multiple allocations.
        using Utf8StringPool pool = new(initialBufferSize: 16);

        Utf8String a = pool.Intern("this-is-a-longer-string-than-the-buffer"u8);
        Utf8String b = pool.Intern("another-long-string-that-exceeds-capacity"u8);

        Assert.AreEqual("this-is-a-longer-string-than-the-buffer", a.ToString());
        Assert.AreEqual("another-long-string-that-exceeds-capacity", b.ToString());
        Assert.AreEqual(2, pool.Count);
    }

    [TestMethod]
    public void DoubleDisposeDoesNotThrow()
    {
        Utf8StringPool pool = new();
        pool.Dispose();
        pool.Dispose();
    }

    [TestMethod]
    public void InternSingleSegmentSequenceMatchesSpan()
    {
        using Utf8StringPool pool = new();
        byte[] bytes = Encoding.UTF8.GetBytes("http://example.org/resource");

        Utf8String fromSpan = pool.Intern(bytes);
        Utf8String fromSequence = pool.Intern(new ReadOnlySequence<byte>(bytes));

        Assert.AreEqual(fromSpan, fromSequence);
        Assert.AreEqual(1, pool.Count);
    }

    [TestMethod]
    public void InternMultiSegmentSequenceDeduplicatesWithSpan()
    {
        using Utf8StringPool pool = new();
        byte[] bytes = Encoding.UTF8.GetBytes("http://example.org/resource");

        Utf8String fromSpan = pool.Intern(bytes);
        Utf8String fromSegments = pool.Intern(MultiSegment(bytes, chunkSize: 4));

        //The spanning gather must produce the identical contiguous bytes, so interning the
        //fragmented form returns the same interned value rather than a duplicate.
        Assert.AreEqual(fromSpan, fromSegments);
        Assert.AreEqual(1, pool.Count);
    }

    [TestMethod]
    public void InternMultiSegmentSequencePreservesContent()
    {
        using Utf8StringPool pool = new();
        byte[] bytes = Encoding.UTF8.GetBytes("a value that is split across several buffer segments");

        Utf8String interned = pool.Intern(MultiSegment(bytes, chunkSize: 7));

        Assert.AreEqual("a value that is split across several buffer segments", interned.ToString());
    }

    private static ReadOnlySequence<byte> MultiSegment(ReadOnlyMemory<byte> data, int chunkSize)
    {
        BufferSegment first = new(data.Slice(0, Math.Min(chunkSize, data.Length)));
        BufferSegment last = first;
        for(int offset = chunkSize; offset < data.Length; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, data.Length - offset);
            last = last.Append(data.Slice(offset, length));
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class BufferSegment: ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public BufferSegment Append(ReadOnlyMemory<byte> memory)
        {
            BufferSegment next = new(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };

            Next = next;

            return next;
        }
    }
}