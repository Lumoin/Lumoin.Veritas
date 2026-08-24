using System;
using Lumoin.Veritas.Cbor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Cbor;

[TestClass]
internal sealed class CborReaderAdversarialTests
{
    [TestMethod]
    public void OversizedByteStringLengthFailsBeforeAllocating()
    {
        //Wire form: byte-string header declaring 2 GB (0x5A + 4-byte length),
        //then a 5-byte truncated payload. The reader must reject the declared
        //size before attempting to materialise it.
        byte[] payload = [0x5A, 0x7F, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00];
        CborReader reader = new(payload, CborSerializerOptions.Default(CborConformanceMode.Lax));

        Exception ex = Assert.ThrowsExactly<CborSizeLimitExceededException>(() => _ = reader.ReadByteStringSpan());
        Assert.Contains(nameof(CborSerializerOptions.MaxByteStringLength), ex.Message);
    }

    [TestMethod]
    public void OversizedDeclaredArrayCountHitsMaxArrayLength()
    {
        //Wire form: array header declaring 10 million entries.
        byte[] payload = [0x9A, 0x00, 0x98, 0x96, 0x80];
        CborSerializerOptions options = CborSerializerOptions.Default(CborConformanceMode.Lax);
        options.MaxArrayLength = 1_000_000;
        CborReader reader = new(payload, options);

        Exception ex = Assert.ThrowsExactly<CborSizeLimitExceededException>(() => _ = reader.ReadStartArray());
        Assert.Contains(nameof(CborSerializerOptions.MaxArrayLength), ex.Message);
    }

    [TestMethod]
    public void OversizedDeclaredMapCountHitsMaxMapEntryCount()
    {
        //Wire form: map header declaring 10 million entries.
        byte[] payload = [0xBA, 0x00, 0x98, 0x96, 0x80];
        CborSerializerOptions options = CborSerializerOptions.Default(CborConformanceMode.Lax);
        options.MaxMapEntryCount = 1_000_000;
        CborReader reader = new(payload, options);

        Exception ex = Assert.ThrowsExactly<CborSizeLimitExceededException>(() => _ = reader.ReadStartMap());
        Assert.Contains(nameof(CborSerializerOptions.MaxMapEntryCount), ex.Message);
    }

    [TestMethod]
    public void DeeplyNestedArrayHitsMaxDepth()
    {
        //Build 100 nested 1-element arrays: each 0x81 is "array of 1 item".
        byte[] payload = new byte[101];
        for(int i = 0; i < 100; i++)
        {
            payload[i] = 0x81;
        }
        payload[100] = 0x00;   //terminal integer 0

        CborSerializerOptions options = CborSerializerOptions.Default(CborConformanceMode.Lax);
        options.MaxDepth = 64;
        CborReader reader = new(payload, options);

        Assert.ThrowsExactly<CborSizeLimitExceededException>(() =>
        {
            while(reader.PeekState() == CborReaderState.StartArray)
            {
                reader.ReadStartArray();
            }
        });
    }

    [TestMethod]
    public void LongTagChainHitsMaxTagDepth()
    {
        //32 consecutive 1-byte tags (tag 6 fits in additional info).
        byte[] payload = new byte[33];
        for(int i = 0; i < 32; i++)
        {
            payload[i] = 0xC6;   //major type 6, additional info 6 -> tag 6
        }
        payload[32] = 0x00;

        CborSerializerOptions options = CborSerializerOptions.Default(CborConformanceMode.Lax);
        options.MaxTagDepth = 16;
        CborReader reader = new(payload, options);

        Assert.ThrowsExactly<CborSizeLimitExceededException>(() =>
        {
            for(int i = 0; i < 32; i++)
            {
                reader.ReadTag();
            }
        });
    }
}
