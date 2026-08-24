using System;
using System.Buffers;
using Lumoin.Veritas.Cbor;

namespace Lumoin.Veritas.ParserTests.Cbor;

/// <summary>
/// Verifies that the writer emits the byte sequences for primitive data
/// items listed in RFC 8949 Appendix A. The hex strings in the assertions
/// are copied directly from that appendix.
/// </summary>
[TestClass]
internal sealed class CborWriterPrimitivesTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void UnsignedIntegerZeroEncodesAsImmediate()
    {
        AssertEncoded("00", w => w.WriteUInt64(0));
    }

    [TestMethod]
    public void UnsignedIntegerOneEncodesAsImmediate()
    {
        AssertEncoded("01", w => w.WriteUInt64(1));
    }

    [TestMethod]
    public void UnsignedIntegerTenEncodesAsImmediate()
    {
        AssertEncoded("0a", w => w.WriteUInt64(10));
    }

    [TestMethod]
    public void UnsignedIntegerTwentyThreeEncodesAsImmediate()
    {
        AssertEncoded("17", w => w.WriteUInt64(23));
    }

    [TestMethod]
    public void UnsignedIntegerTwentyFourEncodesAsOneByte()
    {
        AssertEncoded("1818", w => w.WriteUInt64(24));
    }

    [TestMethod]
    public void UnsignedIntegerOneHundredEncodesAsOneByte()
    {
        AssertEncoded("1864", w => w.WriteUInt64(100));
    }

    [TestMethod]
    public void UnsignedIntegerOneThousandEncodesAsTwoBytes()
    {
        AssertEncoded("1903e8", w => w.WriteUInt64(1000));
    }

    [TestMethod]
    public void UnsignedIntegerOneMillionEncodesAsFourBytes()
    {
        AssertEncoded("1a000f4240", w => w.WriteUInt64(1_000_000));
    }

    [TestMethod]
    public void UnsignedIntegerOneTrillionEncodesAsEightBytes()
    {
        AssertEncoded("1b000000e8d4a51000", w => w.WriteUInt64(1_000_000_000_000UL));
    }

    [TestMethod]
    public void UnsignedIntegerMaxValueEncodesAsEightBytes()
    {
        AssertEncoded("1bffffffffffffffff", w => w.WriteUInt64(ulong.MaxValue));
    }

    [TestMethod]
    public void NegativeOneEncodesAsImmediate()
    {
        AssertEncoded("20", w => w.WriteInt64(-1));
    }

    [TestMethod]
    public void NegativeTenEncodesAsImmediate()
    {
        AssertEncoded("29", w => w.WriteInt64(-10));
    }

    [TestMethod]
    public void NegativeOneHundredEncodesAsOneByte()
    {
        AssertEncoded("3863", w => w.WriteInt64(-100));
    }

    [TestMethod]
    public void NegativeOneThousandEncodesAsTwoBytes()
    {
        AssertEncoded("3903e7", w => w.WriteInt64(-1000));
    }

    [TestMethod]
    public void DoubleOnePointOneEncodesAsBinary64()
    {
        AssertEncoded("fb3ff199999999999a", w => w.WriteDouble(1.1));
    }

    [TestMethod]
    public void HalfZeroEncodesAsThreeBytes()
    {
        AssertEncoded("f90000", w => w.WriteHalf((Half)0.0f));
    }

    [TestMethod]
    public void HalfOneEncodesAsThreeBytes()
    {
        AssertEncoded("f93c00", w => w.WriteHalf((Half)1.0f));
    }

    [TestMethod]
    public void SingleOnePointOneEncodesAsFiveBytes()
    {
        AssertEncoded("fa3f8ccccd", w => w.WriteSingle(1.1f));
    }

    [TestMethod]
    public void DoublePositiveInfinityEncodesAsBinary64()
    {
        AssertEncoded("fb7ff0000000000000", w => w.WriteDouble(double.PositiveInfinity));
    }

    [TestMethod]
    public void DoubleNegativeInfinityEncodesAsBinary64()
    {
        AssertEncoded("fbfff0000000000000", w => w.WriteDouble(double.NegativeInfinity));
    }

    [TestMethod]
    public void BooleanFalseEncodesAsF4()
    {
        AssertEncoded("f4", w => w.WriteBoolean(false));
    }

    [TestMethod]
    public void BooleanTrueEncodesAsF5()
    {
        AssertEncoded("f5", w => w.WriteBoolean(true));
    }

    [TestMethod]
    public void NullEncodesAsF6()
    {
        AssertEncoded("f6", w => w.WriteNull());
    }

    [TestMethod]
    public void UndefinedEncodesAsF7()
    {
        AssertEncoded("f7", w => w.WriteUndefined());
    }

    [TestMethod]
    public void SimpleValueSixteenEncodesAsImmediate()
    {
        AssertEncoded("f0", w => w.WriteSimpleValue(16));
    }

    [TestMethod]
    public void SimpleValueOneHundredEightyFiveEncodesAsTwoByte()
    {
        AssertEncoded("f8ff", w => w.WriteSimpleValue(255));
    }

    [TestMethod]
    public void EmptyByteStringEncodesAsHeaderOnly()
    {
        AssertEncoded("40", w => w.WriteByteString(ReadOnlySpan<byte>.Empty));
    }

    [TestMethod]
    public void ThreeByteByteStringEncodesAsHeaderPlusPayload()
    {
        byte[] payload = [0x01, 0x02, 0x03];
        AssertEncoded("43010203", w => w.WriteByteString(payload));
    }

    [TestMethod]
    public void EmptyTextStringEncodesAsHeaderOnly()
    {
        AssertEncoded("60", w => w.WriteTextString(string.Empty));
    }

    [TestMethod]
    public void SingleCharTextStringEncodesAsTwoBytes()
    {
        AssertEncoded("6161", w => w.WriteTextString("a"));
    }

    [TestMethod]
    public void IetfTextStringEncodesAsFiveBytes()
    {
        AssertEncoded("6449455446", w => w.WriteTextString("IETF"));
    }

    [TestMethod]
    public void EmptyArrayEncodesAsHeaderOnly()
    {
        AssertEncoded("80", w =>
        {
            w.WriteStartArray(0);
            w.WriteEndArray();
        });
    }

    [TestMethod]
    public void ThreeElementArrayEncodesEachItemInOrder()
    {
        AssertEncoded("83010203", w =>
        {
            w.WriteStartArray(3);
            w.WriteUInt64(1);
            w.WriteUInt64(2);
            w.WriteUInt64(3);
            w.WriteEndArray();
        });
    }

    [TestMethod]
    public void NestedArraysEncodeCorrectly()
    {
        //[1, [2, 3], [4, 5]] is example from RFC 8949 Appendix A.
        AssertEncoded("8301820203820405", w =>
        {
            w.WriteStartArray(3);
            w.WriteUInt64(1);
            w.WriteStartArray(2);
            w.WriteUInt64(2);
            w.WriteUInt64(3);
            w.WriteEndArray();
            w.WriteStartArray(2);
            w.WriteUInt64(4);
            w.WriteUInt64(5);
            w.WriteEndArray();
            w.WriteEndArray();
        });
    }

    [TestMethod]
    public void EmptyMapEncodesAsHeaderOnly()
    {
        AssertEncoded("a0", w =>
        {
            w.WriteStartMap(0);
            w.WriteEndMap();
        });
    }

    [TestMethod]
    public void TwoEntryMapWithIntegerKeysEncodesEntriesInOrder()
    {
        //{1: 2, 3: 4} is example from RFC 8949 Appendix A.
        AssertEncoded("a201020304", w =>
        {
            w.WriteStartMap(2);
            w.WriteUInt64(1);
            w.WriteUInt64(2);
            w.WriteUInt64(3);
            w.WriteUInt64(4);
            w.WriteEndMap();
        });
    }

    [TestMethod]
    public void MapWithTextKeyAndArrayValueEncodesCorrectly()
    {
        //{"a": 1, "b": [2, 3]} is example from RFC 8949 Appendix A.
        AssertEncoded("a26161016162820203", w =>
        {
            w.WriteStartMap(2);
            w.WriteTextString("a");
            w.WriteUInt64(1);
            w.WriteTextString("b");
            w.WriteStartArray(2);
            w.WriteUInt64(2);
            w.WriteUInt64(3);
            w.WriteEndArray();
            w.WriteEndMap();
        });
    }

    [TestMethod]
    public void TagWithTextStringContentEncodesAsTagThenContent()
    {
        //Tag 0 (date/time string) followed by the example RFC 3339 timestamp.
        AssertEncoded(
            "c074323031332d30332d32315432303a30343a30305a",
            w =>
            {
                w.WriteTag(CborTag.DateTimeString);
                w.WriteTextString("2013-03-21T20:04:00Z");
            });
    }

    [TestMethod]
    public void IndefiniteArrayWritesIntroducerItemsAndBreak()
    {
        AssertEncoded("9f018202039f0405ffff", w =>
        {
            w.WriteStartArray(null);
            w.WriteUInt64(1);
            w.WriteStartArray(2);
            w.WriteUInt64(2);
            w.WriteUInt64(3);
            w.WriteEndArray();
            w.WriteStartArray(null);
            w.WriteUInt64(4);
            w.WriteUInt64(5);
            w.WriteEndArray();
            w.WriteEndArray();
        });
    }

    [TestMethod]
    public void IndefiniteLengthRejectedUnderDeterministicMode()
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.Cde));

        Assert.Throws<InvalidOperationException>(() => writer.WriteStartArray(null));
    }

    [TestMethod]
    public void DefiniteArrayShortCountThrowsOnEnd()
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.Lax));
        writer.WriteStartArray(3);
        writer.WriteUInt64(1);
        writer.WriteUInt64(2);

        Assert.Throws<InvalidOperationException>(() => writer.WriteEndArray());
    }

    [TestMethod]
    public void MapClosedMidPairThrows()
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.Lax));
        writer.WriteStartMap(1);
        writer.WriteTextString("key");

        Assert.Throws<InvalidOperationException>(() => writer.WriteEndMap());
    }

    [TestMethod]
    public void ResetClearsContainerStackAndCounter()
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.Lax));
        writer.WriteStartArray(3);
        writer.WriteUInt64(1);
        Assert.IsGreaterThan(0, writer.BytesWritten);

        writer.Reset();

        Assert.AreEqual(0, writer.BytesWritten);
        //After Reset the writer is usable for a fresh top-level item.
        writer.WriteUInt64(7);
        Assert.AreEqual(1, writer.BytesWritten);
    }

    private static void AssertEncoded(string expectedHex, Action<CborWriter> action)
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.Lax));
        action(writer);
        string actualHex = Convert.ToHexString(buffer.WrittenSpan);
        Assert.AreEqual(expectedHex.ToUpperInvariant(), actualHex);
        Assert.AreEqual(buffer.WrittenCount, writer.BytesWritten);
    }
}
