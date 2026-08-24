using System;
using System.Buffers;
using System.Globalization;
using System.Numerics;
using Lumoin.Veritas.Cbor;
using Lumoin.Veritas.Cbor.Converters;

namespace Lumoin.Veritas.ParserTests.Cbor;

/// <summary>
/// Round-trip tests for each built-in tag converter. Each test asserts
/// that writing a typed value through the converter produces a tagged
/// byte sequence whose initial bytes match the expected tag header, and
/// that reading those bytes back yields a value equal to the original.
/// </summary>
[TestClass]
internal sealed class CborTagConverterTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void DateTimeStringConverterRoundTripsUtcInstant()
    {
        DateTimeStringCborConverter converter = new();
        DateTimeOffset value = new(2024, 3, 14, 15, 9, 26, TimeSpan.Zero);

        byte[] bytes = WriteToBytes(w => converter.Write(w, value));
        Assert.AreEqual((byte)0xC0, bytes[0]);

        DateTimeOffset roundTripped = ReadFromBytes(bytes, converter);
        Assert.AreEqual(value, roundTripped);
    }

    [TestMethod]
    public void EpochTimeConverterRoundTripsFractionalInstant()
    {
        EpochTimeCborConverter converter = new();
        DateTimeOffset baseInstant = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset value = baseInstant + TimeSpan.FromMilliseconds(250);

        byte[] bytes = WriteToBytes(w => converter.Write(w, value));
        Assert.AreEqual((byte)0xC1, bytes[0]);

        DateTimeOffset roundTripped = ReadFromBytes(bytes, converter);
        Assert.AreEqual(value.UtcDateTime, roundTripped.UtcDateTime);
    }

    [TestMethod]
    public void UnsignedBigIntegerConverterRoundTripsLargeValue()
    {
        UnsignedBigIntegerCborConverter converter = new();
        BigInteger value = BigInteger.Parse("18446744073709551616", CultureInfo.InvariantCulture);

        byte[] bytes = WriteToBytes(w => converter.Write(w, value));
        Assert.AreEqual((byte)0xC2, bytes[0]);

        BigInteger roundTripped = ReadFromBytes(bytes, converter);
        Assert.AreEqual(value, roundTripped);
    }

    [TestMethod]
    public void UnsignedBigIntegerConverterRejectsNegative()
    {
        UnsignedBigIntegerCborConverter converter = new();
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.Lax));

        Assert.Throws<ArgumentException>(() => converter.Write(writer, BigInteger.MinusOne));
    }

    [TestMethod]
    public void NegativeBigIntegerConverterRoundTripsLargeNegativeValue()
    {
        NegativeBigIntegerCborConverter converter = new();
        BigInteger value = -BigInteger.Parse("18446744073709551617", CultureInfo.InvariantCulture);

        byte[] bytes = WriteToBytes(w => converter.Write(w, value));
        Assert.AreEqual((byte)0xC3, bytes[0]);

        BigInteger roundTripped = ReadFromBytes(bytes, converter);
        Assert.AreEqual(value, roundTripped);
    }

    [TestMethod]
    public void DecimalFractionConverterRoundTripsValue()
    {
        DecimalFractionCborConverter converter = new();
        CborDecimalFraction value = new(Exponent: -3, Mantissa: new BigInteger(12345));

        byte[] bytes = WriteToBytes(w => converter.Write(w, value));
        Assert.AreEqual((byte)0xC4, bytes[0]);

        CborDecimalFraction roundTripped = ReadFromBytes(bytes, converter);
        Assert.AreEqual(value, roundTripped);
    }

    [TestMethod]
    public void BigfloatConverterRoundTripsValue()
    {
        BigfloatCborConverter converter = new();
        CborBigfloat value = new(Exponent: 5, Mantissa: new BigInteger(-7));

        byte[] bytes = WriteToBytes(w => converter.Write(w, value));
        Assert.AreEqual((byte)0xC5, bytes[0]);

        CborBigfloat roundTripped = ReadFromBytes(bytes, converter);
        Assert.AreEqual(value, roundTripped);
    }

    [TestMethod]
    public void UriConverterRoundTripsAbsoluteUri()
    {
        UriCborConverter converter = new();
        Uri value = new("https://example.org/some/resource?x=1");

        byte[] bytes = WriteToBytes(w => converter.Write(w, value));
        //Tag 32 with one-byte argument (24, 32) packs as 0xD8 0x20.
        Assert.AreEqual((byte)0xD8, bytes[0]);
        Assert.AreEqual((byte)0x20, bytes[1]);

        Uri roundTripped = ReadFromBytes(bytes, converter);
        Assert.AreEqual(value, roundTripped);
    }

    [TestMethod]
    public void Base64UrlConverterRoundTripsBytes()
    {
        Base64UrlCborConverter converter = new();
        byte[] value = [0x01, 0x02, 0x03, 0xFE, 0xFF];

        byte[] bytes = WriteToBytes(w => converter.Write(w, value));
        Assert.AreEqual((byte)0xD8, bytes[0]);
        Assert.AreEqual((byte)0x21, bytes[1]);

        byte[] roundTripped = ReadFromBytes(bytes, converter);
        Assert.AreSequenceEqual(value, roundTripped);
    }

    [TestMethod]
    public void Base64ConverterRoundTripsBytes()
    {
        Base64CborConverter converter = new();
        byte[] value = [0x10, 0x20, 0x30, 0x40, 0x50];

        byte[] bytes = WriteToBytes(w => converter.Write(w, value));
        Assert.AreEqual((byte)0xD8, bytes[0]);
        Assert.AreEqual((byte)0x22, bytes[1]);

        byte[] roundTripped = ReadFromBytes(bytes, converter);
        Assert.AreSequenceEqual(value, roundTripped);
    }

    [TestMethod]
    public void SelfDescribePrefixWriteAndConsumeRoundTrips()
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.Lax));
        SelfDescribeCborConverter.WritePrefix(writer);
        writer.WriteUInt64(42);

        //Tag 55799 packs as 0xD9 0xD9 0xF7.
        Assert.AreEqual((byte)0xD9, buffer.WrittenSpan[0]);
        Assert.AreEqual((byte)0xD9, buffer.WrittenSpan[1]);
        Assert.AreEqual((byte)0xF7, buffer.WrittenSpan[2]);

        CborReader reader = new((ReadOnlyMemory<byte>)buffer.WrittenMemory, CborSerializerOptions.Default(CborConformanceMode.Lax));
        Assert.IsTrue(SelfDescribeCborConverter.ConsumePrefix(reader));
        Assert.AreEqual(42UL, reader.ReadUInt64());
    }

    [TestMethod]
    public void SelfDescribeConsumeReturnsFalseWhenNoTagPresent()
    {
        byte[] bytes = WriteToBytes(w => w.WriteUInt64(7));
        CborReader reader = new(bytes, CborSerializerOptions.Default(CborConformanceMode.Lax));

        Assert.IsFalse(SelfDescribeCborConverter.ConsumePrefix(reader));
        Assert.AreEqual(7UL, reader.ReadUInt64());
    }

    private static byte[] WriteToBytes(Action<CborWriter> action)
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.Lax));
        action(writer);
        return buffer.WrittenSpan.ToArray();
    }

    private static T ReadFromBytes<T>(byte[] bytes, CborConverter<T> converter)
    {
        CborReader reader = new(bytes, CborSerializerOptions.Default(CborConformanceMode.Lax));
        return converter.Read(reader);
    }
}
