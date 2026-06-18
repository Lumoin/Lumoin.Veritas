using System;
using System.Buffers;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Cbor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Cbor;

[TestClass]
internal sealed class CborReaderByteStringTests
{
    [TestMethod]
    public void DefiniteLengthByteStringReadReturnsSliceOfSource()
    {
        //CBOR byte string of four bytes: 0x44 (major 2, ai 4) followed by payload.
        byte[] source = [0x44, 0xDE, 0xAD, 0xBE, 0xEF];
        CborReader reader = new(source, CborSerializerOptions.Default(CborConformanceMode.Lax));

        ReadOnlyMemory<byte> result = reader.ReadByteStringMemory();

        Assert.IsTrue(MemoryMarshal.TryGetArray(result, out ArraySegment<byte> segment));
        Assert.AreSame(source, segment.Array);
        Assert.AreEqual(1, segment.Offset);
        Assert.HasCount(4, segment);
    }

    [TestMethod]
    public void DefiniteLengthByteStringSpanReadReturnsCorrectContent()
    {
        byte[] source = [0x44, 0xDE, 0xAD, 0xBE, 0xEF];
        CborReader reader = new(source, CborSerializerOptions.Default(CborConformanceMode.Lax));

        ReadOnlySpan<byte> span = reader.ReadByteStringSpan();

        Assert.AreEqual(4, span.Length);
        Assert.AreEqual(0xDE, span[0]);
        Assert.AreEqual(0xEF, span[3]);
    }

    [TestMethod]
    public void IndefiniteByteStringSpanReadThrows()
    {
        //Indefinite byte string with one definite chunk then break.
        byte[] payload = [0x5F, 0x42, 0x01, 0x02, 0xFF];
        CborReader reader = new(payload, CborSerializerOptions.Default(CborConformanceMode.Lax));

        Assert.ThrowsExactly<InvalidOperationException>(() => _ = reader.ReadByteStringSpan());
    }

    [TestMethod]
    public void DefiniteByteStringPooledReadReturnsOwnedBuffer()
    {
        byte[] source = [0x44, 0xDE, 0xAD, 0xBE, 0xEF];
        CborReader reader = new(source, CborSerializerOptions.Default(CborConformanceMode.Lax));

        using IMemoryOwner<byte> owner = reader.ReadByteStringPooled();

        Assert.AreEqual(4, owner.Memory.Length);
        Assert.AreEqual(0xDE, owner.Memory.Span[0]);
        Assert.AreEqual(0xEF, owner.Memory.Span[3]);
    }

    [TestMethod]
    public void IndefiniteByteStringPooledReadAssemblesChunks()
    {
        //Indefinite byte string with chunks "AB" and "CD" then break.
        byte[] payload = [0x5F, 0x42, 0x41, 0x42, 0x42, 0x43, 0x44, 0xFF];
        CborReader reader = new(payload, CborSerializerOptions.Default(CborConformanceMode.Lax));

        using IMemoryOwner<byte> owner = reader.ReadByteStringPooled();

        Assert.AreEqual(4, owner.Memory.Length);
        Assert.AreEqual((byte)'A', owner.Memory.Span[0]);
        Assert.AreEqual((byte)'D', owner.Memory.Span[3]);
    }
}
