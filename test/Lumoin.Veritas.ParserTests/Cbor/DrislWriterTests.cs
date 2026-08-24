using System;
using System.Buffers;
using System.Reflection;
using System.Security.Cryptography;
using Lumoin.Veritas.Cbor.Drisl;
using Lumoin.Veritas.Cid;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.ParserTests.Cbor;

[TestClass]
internal sealed class DrislWriterTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void DrislWriterRejectsNaN()
    {
        ArrayBufferWriter<byte> buffer = new();
        DrislWriter writer = new(buffer);

        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteDouble(double.NaN));
    }

    [TestMethod]
    public void DrislWriterRejectsPositiveInfinity()
    {
        ArrayBufferWriter<byte> buffer = new();
        DrislWriter writer = new(buffer);

        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteDouble(double.PositiveInfinity));
    }

    [TestMethod]
    public void DrislWriterAcceptsNegativeZero()
    {
        ArrayBufferWriter<byte> buffer = new();
        DrislWriter writer = new(buffer);

        writer.WriteDouble(-0.0);

        Assert.IsGreaterThan(0, writer.BytesWritten);
    }

    [TestMethod]
    public void DrislWriterRejectsIntegerKeyInMap()
    {
        ArrayBufferWriter<byte> buffer = new();
        DrislWriter writer = new(buffer);
        writer.WriteStartMap(1);

        Assert.Throws<InvalidOperationException>(() => writer.WriteUInt64(1));
    }

    [TestMethod]
    public void DrislWriterAcceptsTextKeyInMap()
    {
        ArrayBufferWriter<byte> buffer = new();
        DrislWriter writer = new(buffer);
        writer.WriteStartMap(1);
        writer.WriteTextString("key");
        writer.WriteUInt64(42);
        writer.WriteEndMap();
    }

    [TestMethod]
    public void DrislWriterSortsMapKeysCanonical()
    {
        ArrayBufferWriter<byte> buffer = new();
        DrislWriter writer = new(buffer);
        writer.WriteStartMap(2);
        writer.WriteTextString("b");
        writer.WriteUInt64(2);
        writer.WriteTextString("a");
        writer.WriteUInt64(1);
        writer.WriteEndMap();

        //CDE bytewise: "a" (0x61 0x61) < "b" (0x61 0x62). Output should have "a" first.
        byte[] bytes = buffer.WrittenSpan.ToArray();
        Assert.AreSequenceEqual(new byte[] { 0xA2, 0x61, 0x61, 0x01, 0x61, 0x62, 0x02 }, bytes);
    }

    [TestMethod]
    public void DrislWriterDoesNotExposeWriteHalf()
    {
        Type t = typeof(DrislWriter);
        MethodInfo? method = t.GetMethod("WriteHalf", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNull(method);
    }

    [TestMethod]
    public void DrislWriterDoesNotExposeWriteSingle()
    {
        Type t = typeof(DrislWriter);
        MethodInfo? method = t.GetMethod("WriteSingle", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNull(method);
    }

    [TestMethod]
    public void DrislWriterDoesNotExposeWriteTag()
    {
        Type t = typeof(DrislWriter);
        MethodInfo? method = t.GetMethod("WriteTag", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNull(method);
    }

    [TestMethod]
    public void DrislWriterDoesNotExposeWriteUndefined()
    {
        Type t = typeof(DrislWriter);
        MethodInfo? method = t.GetMethod("WriteUndefined", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNull(method);
    }

    [TestMethod]
    public void DrislWriterDoesNotExposeWriteSimpleValue()
    {
        Type t = typeof(DrislWriter);
        MethodInfo? method = t.GetMethod("WriteSimpleValue", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNull(method);
    }

    [TestMethod]
    public void WriteCidEmitsTag42WithPrefixedContent()
    {
        HashDelegate sha256 = SHA256.HashData;
        Lumoin.Veritas.Cid.Cid cid = CidHasher.ComputeFromBytes("hello"u8, CidCodec.Raw, sha256);

        ArrayBufferWriter<byte> buffer = new();
        DrislWriter writer = new(buffer);
        writer.WriteCid(cid);

        byte[] bytes = buffer.WrittenSpan.ToArray();
        //Tag 42 packs as 0xD8 0x2A; followed by byte string of length 37 (header 0x58 0x25).
        Assert.AreEqual((byte)0xD8, bytes[0]);
        Assert.AreEqual((byte)0x2A, bytes[1]);
        Assert.AreEqual((byte)0x58, bytes[2]);
        Assert.AreEqual((byte)0x25, bytes[3]);
        Assert.AreEqual((byte)0x00, bytes[4]);  //multibase prefix
    }

    [TestMethod]
    public void DrislRoundTripWriterAndReader()
    {
        ArrayBufferWriter<byte> buffer = new();
        DrislWriter writer = new(buffer);
        writer.WriteStartMap(2);
        writer.WriteTextString("answer");
        writer.WriteUInt64(42);
        writer.WriteTextString("title");
        writer.WriteTextString("hello");
        writer.WriteEndMap();

        DrislReader reader = new((ReadOnlyMemory<byte>)buffer.WrittenMemory);
        int count = reader.ReadStartMap();
        Assert.AreEqual(2, count);
        //CDE-sorted order: "title" (5 chars) precedes "answer" (6 chars) bytewise; on the wire,
        //"title"'s encoded form starts with 0x65 and "answer"'s with 0x66.
        Assert.AreEqual("title", reader.ReadTextString());
        Assert.AreEqual("hello", reader.ReadTextString());
        Assert.AreEqual("answer", reader.ReadTextString());
        Assert.AreEqual(42UL, reader.ReadUInt64());
        reader.ReadEndMap();
    }

    [TestMethod]
    public void DrislReaderRejectsHalfPrecisionFloat()
    {
        //0xF9 0x00 0x00 is a half-precision zero in CBOR.
        ReadOnlyMemory<byte> bytes = new byte[] { 0xF9, 0x00, 0x00 };
        DrislReader reader = new(bytes);

        Assert.Throws<FormatException>(() => reader.PeekState());
    }

    [TestMethod]
    public void DrislReaderRejectsNaN()
    {
        //fb 7f f8 00 00 00 00 00 00 is binary64 NaN.
        ReadOnlyMemory<byte> bytes = new byte[] { 0xFB, 0x7F, 0xF8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        DrislReader reader = new(bytes);

        Assert.Throws<FormatException>(() => reader.ReadDouble());
    }

    [TestMethod]
    public void DrislReaderRejectsIntegerMapKey()
    {
        //a1 01 02 — map of one pair, key=1 (integer), value=2.
        ReadOnlyMemory<byte> bytes = new byte[] { 0xA1, 0x01, 0x02 };
        DrislReader reader = new(bytes);
        reader.ReadStartMap();

        Assert.Throws<FormatException>(() => reader.ReadUInt64());
    }

    [TestMethod]
    public void DrislReaderRejectsIndefiniteArray()
    {
        ReadOnlyMemory<byte> bytes = new byte[] { 0x9F, 0x01, 0xFF };
        DrislReader reader = new(bytes);

        Assert.Throws<InvalidOperationException>(() => reader.ReadStartArray());
    }
}
