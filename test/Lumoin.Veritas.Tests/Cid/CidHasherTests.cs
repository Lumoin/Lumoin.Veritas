using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Lumoin.Veritas.Cid;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Tests.Cid;

[TestClass]
internal sealed class CidHasherTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public void ComputeFromBytesProducesValidShape()
    {
        HashDelegate sha256 = SHA256.HashData;

        Veritas.Cid.Cid cid = CidHasher.ComputeFromBytes("hello"u8, CidCodec.Raw, sha256);

        Assert.AreEqual(CidCodec.Raw, cid.Codec);
        Assert.AreEqual(32, cid.Digest.AsSpan().Length);
    }

    [TestMethod]
    public void ComputeFromBytesProducesDigestMatchingSha256OfInput()
    {
        //SHA-256("") fixture vector from FIPS 180-4: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855.
        HashDelegate sha256 = SHA256.HashData;

        Veritas.Cid.Cid cid = CidHasher.ComputeFromBytes(ReadOnlySpan<byte>.Empty, CidCodec.Raw, sha256);

        Assert.AreEqual(
            "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
            Convert.ToHexString(cid.Digest.ToArray()));
    }

    [TestMethod]
    public void ComputeFromBytesUsesProvidedHashDelegate()
    {
        bool delegateInvoked = false;
        HashDelegate spy = bytes =>
        {
            delegateInvoked = true;
            return SHA256.HashData(bytes);
        };

        CidHasher.ComputeFromBytes("hello"u8, CidCodec.Raw, spy);

        Assert.IsTrue(delegateInvoked);
    }

    [TestMethod]
    public void ComputeFromBytesRejectsNullHashDelegate()
    {
        Assert.Throws<ArgumentNullException>(
            () => CidHasher.ComputeFromBytes("hello"u8, CidCodec.Raw, null!));
    }

    [TestMethod]
    public void ComputeFromBytesRejectsUnknownCodec()
    {
        HashDelegate sha256 = SHA256.HashData;

        Assert.Throws<ArgumentException>(
            () => CidHasher.ComputeFromBytes("hello"u8, (CidCodec)0x00, sha256));
    }

    [TestMethod]
    public void ComputeFromBytesRejectsHashOfWrongLength()
    {
        HashDelegate broken = _ => new byte[31];

        Assert.Throws<InvalidOperationException>(
            () => CidHasher.ComputeFromBytes("hello"u8, CidCodec.Raw, broken));
    }

    [TestMethod]
    public void ComputeFromBytesProducesCidThatRoundTripsThroughFormatterAndParser()
    {
        HashDelegate sha256 = SHA256.HashData;
        Veritas.Cid.Cid original = CidHasher.ComputeFromBytes("round trip"u8, CidCodec.Drisl, sha256);
        string text = CidFormatter.ToCanonicalString(original);

        Veritas.Cid.Cid roundTripped = CidParser.Parse(text);

        Assert.AreEqual(original.Codec, roundTripped.Codec);
        Assert.AreEqual(original.Digest, roundTripped.Digest);
    }
}
