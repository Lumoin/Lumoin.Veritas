using System;
using System.Linq;
using System.Security.Cryptography;
using Lumoin.Veritas.Cid;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Tests.Cid;

[TestClass]
internal sealed class CidFormattingTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void FormatBytesProducesThirtySixByteSequence()
    {
        Veritas.Cid.Cid cid = BuildKnownCid(CidCodec.Raw);
        byte[] bytes = CidFormatter.ToBytes(cid);

        Assert.HasCount(36, bytes);
    }

    [TestMethod]
    public void FormatBytesEmitsHeaderInOrder()
    {
        Veritas.Cid.Cid cid = BuildKnownCid(CidCodec.Drisl);
        byte[] bytes = CidFormatter.ToBytes(cid);

        Assert.AreEqual(0x01, bytes[0]);
        Assert.AreEqual(0x71, bytes[1]);
        Assert.AreEqual(0x12, bytes[2]);
        Assert.AreEqual(0x20, bytes[3]);
    }

    [TestMethod]
    public void FormatBytesContainsDigestStartingAtOffsetFour()
    {
        Veritas.Cid.Cid cid = BuildKnownCid(CidCodec.Raw);
        byte[] bytes = CidFormatter.ToBytes(cid);

        Assert.AreSequenceEqual(cid.Digest.ToArray(), bytes[4..]);
    }

    [TestMethod]
    public void FormatStringStartsWithLowercaseB()
    {
        Veritas.Cid.Cid cid = BuildKnownCid(CidCodec.Raw);
        string text = CidFormatter.ToCanonicalString(cid);

        Assert.IsTrue(text.StartsWith('b'));
    }

    [TestMethod]
    public void FormatStringHasFiftyNineCharacters()
    {
        Veritas.Cid.Cid cid = BuildKnownCid(CidCodec.Raw);
        string text = CidFormatter.ToCanonicalString(cid);

        Assert.HasCount(59, text);
    }

    [TestMethod]
    public void FormatStringUsesOnlyLowercaseAlphabet()
    {
        Veritas.Cid.Cid cid = BuildKnownCid(CidCodec.Drisl);
        string text = CidFormatter.ToCanonicalString(cid);
        string body = text[1..];

        Assert.IsTrue(body.All(c => (c is >= 'a' and <= 'z') || (c is >= '2' and <= '7')));
    }

    [TestMethod]
    public void RoundTripStringFormPreservesCid()
    {
        Veritas.Cid.Cid original = BuildKnownCid(CidCodec.Raw);
        string text = CidFormatter.ToCanonicalString(original);
        Veritas.Cid.Cid roundTripped = CidParser.Parse(text);

        Assert.AreEqual(original.Codec, roundTripped.Codec);
        Assert.AreEqual(original.Digest, roundTripped.Digest);
    }

    [TestMethod]
    public void RoundTripBinaryFormPreservesCid()
    {
        Veritas.Cid.Cid original = BuildKnownCid(CidCodec.Drisl);
        byte[] bytes = CidFormatter.ToBytes(original);
        Veritas.Cid.Cid roundTripped = CidParser.Parse(bytes);

        Assert.AreEqual(original.Codec, roundTripped.Codec);
        Assert.AreEqual(original.Digest, roundTripped.Digest);
    }

    [TestMethod]
    public void FormatRejectsNullCid()
    {
        Assert.Throws<ArgumentNullException>(() => CidFormatter.ToBytes(null!));
        Assert.Throws<ArgumentNullException>(() => CidFormatter.ToCanonicalString(null!));
    }

    [TestMethod]
    public void Digest32RejectsWrongLengthBytes()
    {
        //Digest length is now a wire-form invariant carried by the
        //Digest32 struct itself; the constructor enforces 32 bytes.
        Assert.Throws<ArgumentException>(() => Digest32.FromSpan(new byte[31]));
        Assert.Throws<ArgumentException>(() => Digest32.FromSpan(new byte[33]));
    }

    [TestMethod]
    public void FormatRejectsUnknownCodecValue()
    {
        Veritas.Cid.Cid cid = new() { Codec = (CidCodec)0x00, Digest = Digest32.FromSpan(new byte[32]) };

        Assert.Throws<ArgumentException>(() => CidFormatter.ToBytes(cid));
        Assert.Throws<ArgumentException>(() => CidFormatter.ToCanonicalString(cid));
    }

    private static Veritas.Cid.Cid BuildKnownCid(CidCodec codec)
    {
        HashDelegate sha256 = SHA256.HashData;
        return CidHasher.ComputeFromBytes("hello"u8, codec, sha256);
    }
}
