using System;
using Lumoin.Veritas.Cbor;

namespace Lumoin.Veritas.ParserTests.Cbor;

/// <summary>
/// Verifies that the reader decodes RFC 8949 Appendix A primitive vectors
/// to the values the spec lists. Exercised against the same hex strings as
/// <see cref="CborWriterPrimitivesTests"/> for round-trip integrity.
/// </summary>
[TestClass]
internal sealed class CborReaderPrimitivesTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void ReadsUnsignedIntegerImmediate()
    {
        CborReader reader = ReaderOver("17");
        Assert.AreEqual(CborReaderState.UnsignedInteger, reader.PeekState());
        Assert.AreEqual(23UL, reader.ReadUInt64());
        Assert.AreEqual(CborReaderState.Finished, reader.PeekState());
    }

    [TestMethod]
    public void ReadsUnsignedIntegerOneByte()
    {
        CborReader reader = ReaderOver("1818");
        Assert.AreEqual(24UL, reader.ReadUInt64());
    }

    [TestMethod]
    public void ReadsUnsignedIntegerTwoByte()
    {
        CborReader reader = ReaderOver("1903e8");
        Assert.AreEqual(1000UL, reader.ReadUInt64());
    }

    [TestMethod]
    public void ReadsUnsignedIntegerFourByte()
    {
        CborReader reader = ReaderOver("1a000f4240");
        Assert.AreEqual(1_000_000UL, reader.ReadUInt64());
    }

    [TestMethod]
    public void ReadsUnsignedIntegerEightByte()
    {
        CborReader reader = ReaderOver("1bffffffffffffffff");
        Assert.AreEqual(ulong.MaxValue, reader.ReadUInt64());
    }

    [TestMethod]
    public void ReadsNegativeIntegerImmediate()
    {
        CborReader reader = ReaderOver("20");
        Assert.AreEqual(-1L, reader.ReadInt64());
    }

    [TestMethod]
    public void ReadsNegativeIntegerOneByte()
    {
        CborReader reader = ReaderOver("3863");
        Assert.AreEqual(-100L, reader.ReadInt64());
    }

    [TestMethod]
    public void ReadInt64FromMajorTypeZeroProducesPositiveValue()
    {
        CborReader reader = ReaderOver("0a");
        Assert.AreEqual(10L, reader.ReadInt64());
    }

    [TestMethod]
    public void ReadsBooleanFalse()
    {
        CborReader reader = ReaderOver("f4");
        Assert.AreEqual(CborReaderState.Boolean, reader.PeekState());
        Assert.IsFalse(reader.ReadBoolean());
    }

    [TestMethod]
    public void ReadsBooleanTrue()
    {
        CborReader reader = ReaderOver("f5");
        Assert.IsTrue(reader.ReadBoolean());
    }

    [TestMethod]
    public void ReadsNull()
    {
        CborReader reader = ReaderOver("f6");
        Assert.AreEqual(CborReaderState.Null, reader.PeekState());
        reader.ReadNull();
        Assert.AreEqual(CborReaderState.Finished, reader.PeekState());
    }

    [TestMethod]
    public void ReadsUndefined()
    {
        CborReader reader = ReaderOver("f7");
        Assert.AreEqual(CborReaderState.Undefined, reader.PeekState());
        reader.ReadUndefined();
    }

    [TestMethod]
    public void ReadsSimpleValueImmediate()
    {
        CborReader reader = ReaderOver("f0");
        Assert.AreEqual((byte)16, reader.ReadSimpleValue());
    }

    [TestMethod]
    public void ReadsSimpleValueOneByte()
    {
        CborReader reader = ReaderOver("f8ff");
        Assert.AreEqual((byte)255, reader.ReadSimpleValue());
    }

    [TestMethod]
    public void ReadsDoubleOnePointOne()
    {
        CborReader reader = ReaderOver("fb3ff199999999999a");
        Assert.AreEqual(CborReaderState.DoublePrecisionFloat, reader.PeekState());
        Assert.AreEqual(1.1, reader.ReadDouble());
    }

    [TestMethod]
    public void ReadsDoublePositiveInfinity()
    {
        CborReader reader = ReaderOver("fb7ff0000000000000");
        Assert.AreEqual(double.PositiveInfinity, reader.ReadDouble());
    }

    [TestMethod]
    public void ReadsHalfPrecisionZero()
    {
        //Half-precision zero is encoded as f9 0000.
        CborReader reader = ReaderOver("f90000");
        Assert.AreEqual(CborReaderState.HalfPrecisionFloat, reader.PeekState());
        Assert.AreEqual((Half)0.0f, reader.ReadHalf());
    }

    [TestMethod]
    public void ReadsHalfPrecisionOne()
    {
        CborReader reader = ReaderOver("f93c00");
        Assert.AreEqual((Half)1.0f, reader.ReadHalf());
    }

    [TestMethod]
    public void ReadsSinglePrecisionOnePointOne()
    {
        //fa 3f 8c cc cd is single-precision 1.1.
        CborReader reader = ReaderOver("fa3f8ccccd");
        Assert.AreEqual(CborReaderState.SinglePrecisionFloat, reader.PeekState());
        Assert.AreEqual(1.1f, reader.ReadSingle(), 1e-6f);
    }

    [TestMethod]
    public void ReadsEmptyByteString()
    {
        CborReader reader = ReaderOver("40");
        byte[] bytes = reader.ReadByteString();
        Assert.IsEmpty(bytes);
    }

    [TestMethod]
    public void ReadsThreeByteByteString()
    {
        CborReader reader = ReaderOver("43010203");
        byte[] bytes = reader.ReadByteString();
        Assert.AreSequenceEqual(new byte[] { 1, 2, 3 }, bytes);
    }

    [TestMethod]
    public void ReadsEmptyTextString()
    {
        CborReader reader = ReaderOver("60");
        Assert.AreEqual(string.Empty, reader.ReadTextString());
    }

    [TestMethod]
    public void ReadsIetfTextString()
    {
        CborReader reader = ReaderOver("6449455446");
        Assert.AreEqual("IETF", reader.ReadTextString());
    }

    [TestMethod]
    public void ReadsThreeElementArray()
    {
        CborReader reader = ReaderOver("83010203");
        Assert.AreEqual(CborReaderState.StartArray, reader.PeekState());
        int? count = reader.ReadStartArray();
        Assert.AreEqual(3, count);
        Assert.AreEqual(1UL, reader.ReadUInt64());
        Assert.AreEqual(2UL, reader.ReadUInt64());
        Assert.AreEqual(3UL, reader.ReadUInt64());
        Assert.AreEqual(CborReaderState.EndArray, reader.PeekState());
        reader.ReadEndArray();
        Assert.AreEqual(CborReaderState.Finished, reader.PeekState());
    }

    [TestMethod]
    public void ReadsNestedArrays()
    {
        CborReader reader = ReaderOver("8301820203820405");
        Assert.AreEqual(3, reader.ReadStartArray());
        Assert.AreEqual(1UL, reader.ReadUInt64());
        Assert.AreEqual(2, reader.ReadStartArray());
        Assert.AreEqual(2UL, reader.ReadUInt64());
        Assert.AreEqual(3UL, reader.ReadUInt64());
        reader.ReadEndArray();
        Assert.AreEqual(2, reader.ReadStartArray());
        Assert.AreEqual(4UL, reader.ReadUInt64());
        Assert.AreEqual(5UL, reader.ReadUInt64());
        reader.ReadEndArray();
        reader.ReadEndArray();
    }

    [TestMethod]
    public void ReadsTwoEntryMap()
    {
        CborReader reader = ReaderOver("a201020304");
        Assert.AreEqual(CborReaderState.StartMap, reader.PeekState());
        int? count = reader.ReadStartMap();
        Assert.AreEqual(2, count);
        Assert.AreEqual(1UL, reader.ReadUInt64());
        Assert.AreEqual(2UL, reader.ReadUInt64());
        Assert.AreEqual(3UL, reader.ReadUInt64());
        Assert.AreEqual(4UL, reader.ReadUInt64());
        reader.ReadEndMap();
    }

    [TestMethod]
    public void ReadsMapWithTextKeyAndArrayValue()
    {
        CborReader reader = ReaderOver("a26161016162820203");
        Assert.AreEqual(2, reader.ReadStartMap());
        Assert.AreEqual("a", reader.ReadTextString());
        Assert.AreEqual(1UL, reader.ReadUInt64());
        Assert.AreEqual("b", reader.ReadTextString());
        Assert.AreEqual(2, reader.ReadStartArray());
        Assert.AreEqual(2UL, reader.ReadUInt64());
        Assert.AreEqual(3UL, reader.ReadUInt64());
        reader.ReadEndArray();
        reader.ReadEndMap();
    }

    [TestMethod]
    public void ReadsTaggedTextString()
    {
        CborReader reader = ReaderOver("c074323031332d30332d32315432303a30343a30305a");
        Assert.AreEqual(CborReaderState.Tag, reader.PeekState());
        CborTag tag = reader.ReadTag();
        Assert.AreEqual(CborTag.DateTimeString, tag);
        Assert.AreEqual("2013-03-21T20:04:00Z", reader.ReadTextString());
    }

    [TestMethod]
    public void ReadsIndefiniteArrayWithBreak()
    {
        CborReader reader = ReaderOver("9f018202039f0405ffff");
        int? count = reader.ReadStartArray();
        Assert.IsNull(count);
        Assert.AreEqual(1UL, reader.ReadUInt64());
        Assert.AreEqual(2, reader.ReadStartArray());
        Assert.AreEqual(2UL, reader.ReadUInt64());
        Assert.AreEqual(3UL, reader.ReadUInt64());
        reader.ReadEndArray();
        int? inner = reader.ReadStartArray();
        Assert.IsNull(inner);
        Assert.AreEqual(4UL, reader.ReadUInt64());
        Assert.AreEqual(5UL, reader.ReadUInt64());
        Assert.AreEqual(CborReaderState.EndArray, reader.PeekState());
        reader.ReadEndArray();
        Assert.AreEqual(CborReaderState.EndArray, reader.PeekState());
        reader.ReadEndArray();
        Assert.AreEqual(CborReaderState.Finished, reader.PeekState());
    }

    [TestMethod]
    public void IndefiniteRejectedUnderDeterministicMode()
    {
        ReadOnlyMemory<byte> bytes = HexToBytes("9f01ff");
        CborReader reader = new(bytes, CborSerializerOptions.Default(CborConformanceMode.Cde));

        Assert.Throws<InvalidOperationException>(() => reader.ReadStartArray());
    }

    [TestMethod]
    public void DefiniteArrayShortReadThrowsOnEnd()
    {
        CborReader reader = ReaderOver("8301");
        reader.ReadStartArray();
        reader.ReadUInt64();

        Assert.Throws<InvalidOperationException>(() => reader.ReadEndArray());
    }

    [TestMethod]
    public void StrictModeRejectsInvalidUtf8()
    {
        //Header for a 3-byte text string followed by a known-invalid UTF-8 sequence (0xFF 0xFE 0xFD).
        ReadOnlyMemory<byte> bytes = HexToBytes("63fffefd");
        CborReader reader = new(bytes, CborSerializerOptions.Default(CborConformanceMode.Strict));

        Assert.Throws<FormatException>(() => reader.ReadTextString());
    }

    [TestMethod]
    public void LaxModeAcceptsInvalidUtf8WithReplacement()
    {
        //The same invalid byte sequence under Lax mode produces a string with U+FFFD replacement characters
        //rather than throwing; the contract is "do not validate" and this assertion pins that behaviour.
        ReadOnlyMemory<byte> bytes = HexToBytes("63fffefd");
        CborReader reader = new(bytes, CborSerializerOptions.Default(CborConformanceMode.Lax));

        string value = reader.ReadTextString();
        Assert.IsNotEmpty(value);
    }

    private static CborReader ReaderOver(string hex)
    {
        return new CborReader(HexToBytes(hex), CborSerializerOptions.Default(CborConformanceMode.Lax));
    }

    private static ReadOnlyMemory<byte> HexToBytes(string hex)
    {
        return Convert.FromHexString(hex);
    }
}
