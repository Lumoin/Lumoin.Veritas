using System;
using System.Buffers;
using System.Reflection;
using Lumoin.Veritas.Cbor;
using Lumoin.Veritas.Cbor.Dcbor;

namespace Lumoin.Veritas.ParserTests.Cbor;

[TestClass]
internal sealed class DcborWriterTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void DcborWriterCanonicalisesNaN()
    {
        //draft-mcnally-deterministic-cbor §2.5: NaN reduces to the
        //canonical half-precision quiet NaN 0xF9 7E 00. Earlier behaviour
        //(throwing on NaN) was non-spec.
        ArrayBufferWriter<byte> buffer = new();
        DcborWriter writer = new(buffer);

        writer.WriteDouble(double.NaN);

        Assert.AreSequenceEqual(new byte[] { 0xF9, 0x7E, 0x00 }, buffer.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void DcborWriterAcceptsIntegerKeyInMap()
    {
        ArrayBufferWriter<byte> buffer = new();
        DcborWriter writer = new(buffer);
        writer.WriteStartMap(1);
        writer.WriteUInt64(7);
        writer.WriteUInt64(42);
        writer.WriteEndMap();

        DcborReader reader = new((ReadOnlyMemory<byte>)buffer.WrittenMemory);
        int count = reader.ReadStartMap();
        Assert.AreEqual(1, count);
        Assert.AreEqual(7UL, reader.ReadUInt64());
        Assert.AreEqual(42UL, reader.ReadUInt64());
        reader.ReadEndMap();
    }

    [TestMethod]
    public void DcborWriterSortsMapKeysCanonical()
    {
        ArrayBufferWriter<byte> buffer = new();
        DcborWriter writer = new(buffer);
        writer.WriteStartMap(2);
        writer.WriteUInt64(2);
        writer.WriteUInt64(20);
        writer.WriteUInt64(1);
        writer.WriteUInt64(10);
        writer.WriteEndMap();

        byte[] bytes = buffer.WrittenSpan.ToArray();
        Assert.AreSequenceEqual(new byte[] { 0xA2, 0x01, 0x0A, 0x02, 0x14 }, bytes);
    }

    [TestMethod]
    public void DcborWriterAllowsArbitraryTag()
    {
        ArrayBufferWriter<byte> buffer = new();
        DcborWriter writer = new(buffer);
        writer.WriteTag(new CborTag(99));
        writer.WriteUInt64(1);

        byte[] bytes = buffer.WrittenSpan.ToArray();
        //Tag 99 (one-byte argument) packs as 0xD8 0x63, followed by uint 1 (0x01).
        Assert.AreSequenceEqual(new byte[] { 0xD8, 0x63, 0x01 }, bytes);
    }

    [TestMethod]
    public void DcborWriterDoesNotExposeWriteHalf()
    {
        Type t = typeof(DcborWriter);
        MethodInfo? method = t.GetMethod("WriteHalf", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNull(method);
    }

    [TestMethod]
    public void DcborWriterDoesNotExposeWriteSingle()
    {
        Type t = typeof(DcborWriter);
        MethodInfo? method = t.GetMethod("WriteSingle", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNull(method);
    }

    [TestMethod]
    public void DcborReaderAcceptsCanonicalHalfPrecisionFloat()
    {
        //draft-mcnally-deterministic-cbor §2.5 permits half-precision
        //encodings as the canonical form for NaN, infinity, and reduced
        //finite values. Earlier behaviour (rejecting half-precision
        //wholesale) was non-spec.
        ReadOnlyMemory<byte> bytes = new byte[] { 0xF9, 0x00, 0x00 };
        DcborReader reader = new(bytes);

        Assert.AreEqual(CborReaderState.HalfPrecisionFloat, reader.PeekState());
        Half value = reader.ReadHalf();
        Assert.AreEqual((Half)0.0f, value);
    }

    [TestMethod]
    public void DcborReaderRejectsIndefiniteArray()
    {
        ReadOnlyMemory<byte> bytes = new byte[] { 0x9F, 0x01, 0xFF };
        DcborReader reader = new(bytes);

        Assert.Throws<InvalidOperationException>(() => reader.ReadStartArray());
    }

    [TestMethod]
    public void DcborReaderAcceptsIntegerMapKey()
    {
        //a1 01 02 — map of one pair, integer key 1 to integer value 2. dCBOR allows this.
        ReadOnlyMemory<byte> bytes = new byte[] { 0xA1, 0x01, 0x02 };
        DcborReader reader = new(bytes);
        reader.ReadStartMap();
        Assert.AreEqual(1UL, reader.ReadUInt64());
        Assert.AreEqual(2UL, reader.ReadUInt64());
        reader.ReadEndMap();
    }
}
