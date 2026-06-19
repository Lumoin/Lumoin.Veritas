using Lumoin.Veritas.Core.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Parsing;

/// <summary>Tests for <see cref="ReadOnlyMemoryStream"/>, the read-only <see cref="System.IO.Stream"/> over a byte buffer.</summary>
[TestClass]
internal sealed class ReadOnlyMemoryStreamTests
{
    /// <summary>Reading in chunks returns every byte in order, advances the position, then returns 0 at end of buffer.</summary>
    [TestMethod]
    public void ReadsAllBytesInOrderThenReturnsZeroAtEnd()
    {
        byte[] source = [1, 2, 3, 4, 5];
        using ReadOnlyMemoryStream stream = new(source);

        Assert.AreEqual(5L, stream.Length);
        Assert.IsTrue(stream.CanRead);
        Assert.IsFalse(stream.CanSeek);
        Assert.IsFalse(stream.CanWrite);

        byte[] head = new byte[2];
        Assert.AreEqual(2, stream.Read(head, 0, 2));
        Assert.AreEqual((byte)1, head[0]);
        Assert.AreEqual((byte)2, head[1]);
        Assert.AreEqual(2L, stream.Position);

        Span<byte> rest = new byte[8];
        Assert.AreEqual(3, stream.Read(rest));
        Assert.AreEqual((byte)3, rest[0]);
        Assert.AreEqual((byte)5, rest[2]);
        Assert.AreEqual(5L, stream.Position);

        Assert.AreEqual(0, stream.Read(rest));
    }

    /// <summary>The forward-only, read-only contract rejects seeking, length changes, writing, and position assignment.</summary>
    [TestMethod]
    public void SeekWriteAndPositionSetThrow()
    {
        byte[] source = [0];
        using ReadOnlyMemoryStream stream = new(source);

        Assert.ThrowsExactly<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.ThrowsExactly<NotSupportedException>(() => stream.SetLength(1));
        Assert.ThrowsExactly<NotSupportedException>(() => stream.Write(source, 0, 1));
        Assert.ThrowsExactly<NotSupportedException>(() => { stream.Position = 0; });
    }
}
