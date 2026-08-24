using System;
using System.Buffers;
using Lumoin.Veritas.Cbor;

namespace Lumoin.Veritas.ParserTests.Cbor;

[TestClass]
internal sealed class CborIndefiniteStringTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void IndefiniteByteStringWithTwoChunksEncodesAsIntroducerChunksAndBreak()
    {
        //RFC 8949 Appendix A vector: bytes(_ h'0102', h'030405')
        //Wire: 5F 42 01 02 43 03 04 05 FF
        AssertEncoded("5F420102430304 05FF", w =>
        {
            w.WriteStartIndefiniteByteString();
            w.WriteByteString(new byte[] { 0x01, 0x02 });
            w.WriteByteString(new byte[] { 0x03, 0x04, 0x05 });
            w.WriteEndIndefiniteByteString();
        });
    }

    [TestMethod]
    public void IndefiniteByteStringWithNoChunksEncodesAsIntroducerThenBreak()
    {
        AssertEncoded("5FFF", w =>
        {
            w.WriteStartIndefiniteByteString();
            w.WriteEndIndefiniteByteString();
        });
    }

    [TestMethod]
    public void IndefiniteTextStringWithTwoChunksEncodesAsIntroducerChunksAndBreak()
    {
        //RFC 8949 Appendix A vector: (_ "strea", "ming")
        //Wire: 7F 65 'strea' 64 'ming' FF
        AssertEncoded("7F657374726561646D696E67FF", w =>
        {
            w.WriteStartIndefiniteTextString();
            w.WriteTextString("strea");
            w.WriteTextString("ming");
            w.WriteEndIndefiniteTextString();
        });
    }

    [TestMethod]
    public void IndefiniteByteStringRejectedUnderDeterministicMode()
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.Cde));

        Assert.Throws<InvalidOperationException>(() => writer.WriteStartIndefiniteByteString());
    }

    [TestMethod]
    public void IndefiniteTextStringRejectedUnderDeterministicMode()
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.Cde));

        Assert.Throws<InvalidOperationException>(() => writer.WriteStartIndefiniteTextString());
    }

    [TestMethod]
    public void ReadByteStringConcatenatesIndefiniteChunks()
    {
        //5f 42 01 02 43 03 04 05 ff
        ReadOnlyMemory<byte> bytes = new byte[] { 0x5F, 0x42, 0x01, 0x02, 0x43, 0x03, 0x04, 0x05, 0xFF };
        CborReader reader = new(bytes, CborSerializerOptions.Default(CborConformanceMode.Lax));

        byte[] decoded = reader.ReadByteString();

        Assert.AreSequenceEqual(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 }, decoded);
        Assert.AreEqual(CborReaderState.Finished, reader.PeekState());
    }

    [TestMethod]
    public void ReadByteStringHandlesEmptyIndefinite()
    {
        ReadOnlyMemory<byte> bytes = new byte[] { 0x5F, 0xFF };
        CborReader reader = new(bytes, CborSerializerOptions.Default(CborConformanceMode.Lax));

        byte[] decoded = reader.ReadByteString();

        Assert.IsEmpty(decoded);
    }

    [TestMethod]
    public void ReadTextStringConcatenatesIndefiniteChunks()
    {
        //7f 65 strea 64 ming ff
        ReadOnlyMemory<byte> bytes = new byte[]
        {
            0x7F, 0x65, 0x73, 0x74, 0x72, 0x65, 0x61, 0x64, 0x6D, 0x69, 0x6E, 0x67, 0xFF
        };
        CborReader reader = new(bytes, CborSerializerOptions.Default(CborConformanceMode.Lax));

        string decoded = reader.ReadTextString();

        Assert.AreEqual("streaming", decoded);
    }

    [TestMethod]
    public void ReadByteStringRejectsNonByteStringChunk()
    {
        //5f 42 01 02 64 6D 69 6E 67 ff — second chunk has major type 3 (text string), invalid.
        ReadOnlyMemory<byte> bytes = new byte[]
        {
            0x5F, 0x42, 0x01, 0x02, 0x64, 0x6D, 0x69, 0x6E, 0x67, 0xFF
        };
        CborReader reader = new(bytes, CborSerializerOptions.Default(CborConformanceMode.Lax));

        Assert.Throws<FormatException>(() => reader.ReadByteString());
    }

    [TestMethod]
    public void ReadByteStringRejectsNestedIndefinite()
    {
        //5f 5f ... — second introducer is itself indefinite, forbidden.
        ReadOnlyMemory<byte> bytes = new byte[] { 0x5F, 0x5F, 0xFF, 0xFF };
        CborReader reader = new(bytes, CborSerializerOptions.Default(CborConformanceMode.Lax));

        Assert.Throws<FormatException>(() => reader.ReadByteString());
    }

    [TestMethod]
    public void IndefiniteByteStringRoundTripsThroughReader()
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.Lax));
        writer.WriteStartIndefiniteByteString();
        writer.WriteByteString(new byte[] { 0xAA, 0xBB });
        writer.WriteByteString(new byte[] { 0xCC });
        writer.WriteByteString(new byte[] { 0xDD, 0xEE, 0xFF });
        writer.WriteEndIndefiniteByteString();

        CborReader reader = new((ReadOnlyMemory<byte>)buffer.WrittenMemory, CborSerializerOptions.Default(CborConformanceMode.Lax));
        byte[] roundTripped = reader.ReadByteString();

        Assert.AreSequenceEqual(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }, roundTripped);
    }

    private static void AssertEncoded(string expectedHex, Action<CborWriter> action)
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborSerializerOptions.Default(CborConformanceMode.Lax));
        action(writer);
        string actualHex = Convert.ToHexString(buffer.WrittenSpan);
        //Allow whitespace in expected for readability.
        string normalised = expectedHex.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        Assert.AreEqual(normalised, actualHex);
    }
}
