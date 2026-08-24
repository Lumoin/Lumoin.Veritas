using System;
using System.Buffers;
using Lumoin.Veritas.Cbor.CborLd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.CborLd;

[TestClass]
internal sealed class CborLdReferenceCodecTests
{
    [TestMethod]
    public void UrlEncoderProducesBigEndianBytes()
    {
        CborLdInputInt id = new(200L);
        using IMemoryOwner<byte> result = CborLdTestSetup.UrlEncoder(id, MemoryPool<byte>.Shared);

        Assert.HasCount(2, result.Memory);
        Assert.AreEqual(0x00, result.Memory.Span[0]);
        Assert.AreEqual(0xC8, result.Memory.Span[1]);
    }

    [TestMethod]
    public void UrlDecoderReadsBigEndianBytes()
    {
        byte[] bytes = [0x00, 0xC8];
        CborLdInputNode decoded = CborLdTestSetup.UrlDecoder(bytes);

        Assert.IsInstanceOfType<CborLdInputInt>(decoded);
        Assert.AreEqual(200L, ((CborLdInputInt)decoded).Value);
    }

    [TestMethod]
    public void DateRoundTrip()
    {
        CborLdInputString input = new("2026-05-12");
        using IMemoryOwner<byte> encoded = CborLdTestSetup.DateEncoder(input, MemoryPool<byte>.Shared);

        Assert.HasCount(4, encoded.Memory);

        CborLdInputNode decoded = CborLdTestSetup.DateDecoder(encoded.Memory);
        Assert.AreEqual("2026-05-12", ((CborLdInputString)decoded).Value);
    }

    [TestMethod]
    public void DateTimeRoundTrip()
    {
        CborLdInputString input = new("2026-05-12T12:00:00Z");
        using IMemoryOwner<byte> encoded = CborLdTestSetup.DateTimeEncoder(input, MemoryPool<byte>.Shared);

        Assert.HasCount(8, encoded.Memory);

        CborLdInputNode decoded = CborLdTestSetup.DateTimeDecoder(encoded.Memory);
        Assert.AreEqual("2026-05-12T12:00:00Z", ((CborLdInputString)decoded).Value);
    }

    [TestMethod]
    public void Base64UrlRoundTrip()
    {
        //"foobar" base64url = "Zm9vYmFy"
        CborLdInputString input = new("Zm9vYmFy");
        using IMemoryOwner<byte> encoded = CborLdTestSetup.Base64UrlEncoder(input, MemoryPool<byte>.Shared);

        Assert.HasCount(6, encoded.Memory);
        Assert.AreEqual((byte)'f', encoded.Memory.Span[0]);
        Assert.AreEqual((byte)'r', encoded.Memory.Span[5]);

        CborLdInputNode decoded = CborLdTestSetup.Base64UrlDecoder(encoded.Memory);
        Assert.AreEqual("Zm9vYmFy", ((CborLdInputString)decoded).Value);
    }

    [TestMethod]
    public void RegistryIsInitializedByModuleInitializer()
    {
        Assert.IsTrue(CborLdTypedValueCodecs.IsInitialized);
    }
}
