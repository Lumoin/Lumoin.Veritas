using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Lumoin.Veritas.Cid;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Tests.Cid;

[TestClass]
internal sealed class CidParsingTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void ParseValidRawCidRoundTrips()
    {
        Veritas.Cid.Cid original = BuildKnownCid(CidCodec.Raw);
        string text = CidFormatter.ToCanonicalString(original);

        Veritas.Cid.Cid parsed = CidParser.Parse(text);

        Assert.AreEqual(original.Codec, parsed.Codec);
        Assert.AreEqual(original.Digest, parsed.Digest);
    }

    [TestMethod]
    public void ParseValidDrislCidRoundTrips()
    {
        Veritas.Cid.Cid original = BuildKnownCid(CidCodec.Drisl);
        string text = CidFormatter.ToCanonicalString(original);

        Veritas.Cid.Cid parsed = CidParser.Parse(text);

        Assert.AreEqual(CidCodec.Drisl, parsed.Codec);
        Assert.AreEqual(original.Digest, parsed.Digest);
    }

    [TestMethod]
    public void RejectStringWithoutBPrefix()
    {
        string body = CidFormatter.ToCanonicalString(BuildKnownCid(CidCodec.Raw))[1..];
        string missingPrefix = "x" + body;

        CidParseException ex = Assert.Throws<CidParseException>(() => CidParser.Parse(missingPrefix));
        Assert.Contains("prefix", ex.Message);
    }

    [TestMethod]
    public void RejectStringWithUppercaseBase32()
    {
        string text = CidFormatter.ToCanonicalString(BuildKnownCid(CidCodec.Raw)).ToUpperInvariant();
        //The 'B' prefix mismatches the lowercase 'b'; reset it so the uppercase body is the rejection cause.
        string upperBodyLowerPrefix = "b" + text[1..];

        Assert.Throws<CidParseException>(() => CidParser.Parse(upperBodyLowerPrefix));
    }

    [TestMethod]
    public void RejectStringWithInvalidBase32Character()
    {
        string text = CidFormatter.ToCanonicalString(BuildKnownCid(CidCodec.Raw));
        //Replace a body character with '1', which is outside the RFC 4648 alphabet.
        char[] chars = text.ToCharArray();
        chars[5] = '1';
        string corrupted = new(chars);

        Assert.Throws<CidParseException>(() => CidParser.Parse(corrupted));
    }

    [TestMethod]
    public void RejectStringWithWrongLength()
    {
        string text = CidFormatter.ToCanonicalString(BuildKnownCid(CidCodec.Raw));
        string truncated = text[..^1];

        Assert.Throws<CidParseException>(() => CidParser.Parse(truncated));
    }

    [TestMethod]
    public void RejectStringWithNonCanonicalTrailingBits()
    {
        //The last character of a 36-byte CID's base32 form encodes 3 bits of payload
        //and 2 trailing bits that must be zero. Replace the final character with one
        //whose low two bits are non-zero to force the canonical-form check to fail.
        string text = CidFormatter.ToCanonicalString(BuildKnownCid(CidCodec.Raw));
        char[] chars = text.ToCharArray();
        //Find a final-character substitute that keeps the top three bits the same
        //but sets at least one of the bottom two bits.
        char original = chars[^1];
        int originalIndex = LookupAlphabet(original);
        int withBitsSet = (originalIndex & 0b11100) | 0b01;
        if(withBitsSet == originalIndex)
        {
            withBitsSet = (originalIndex & 0b11100) | 0b10;
        }
        chars[^1] = AlphabetChar(withBitsSet);
        string corrupted = new(chars);

        Assert.Throws<CidParseException>(() => CidParser.Parse(corrupted));
    }

    [TestMethod]
    public void RejectBinaryFormShorterThan36Bytes()
    {
        byte[] bytes = CidFormatter.ToBytes(BuildKnownCid(CidCodec.Raw));
        byte[] shortened = bytes[..35];

        Assert.Throws<CidParseException>(() => CidParser.Parse(shortened));
    }

    [TestMethod]
    public void RejectBinaryFormLongerThan36Bytes()
    {
        byte[] bytes = CidFormatter.ToBytes(BuildKnownCid(CidCodec.Raw));
        byte[] padded = new byte[bytes.Length + 1];
        bytes.CopyTo(padded, 0);

        Assert.Throws<CidParseException>(() => CidParser.Parse(padded));
    }

    [TestMethod]
    public void RejectBinaryFormWithUnknownVersionByte()
    {
        byte[] bytes = CidFormatter.ToBytes(BuildKnownCid(CidCodec.Raw));
        bytes[0] = 0x02;

        Assert.Throws<CidParseException>(() => CidParser.Parse(bytes));
    }

    [TestMethod]
    public void RejectBinaryFormWithUnknownCodecByte()
    {
        byte[] bytes = CidFormatter.ToBytes(BuildKnownCid(CidCodec.Raw));
        bytes[1] = 0x00;

        Assert.Throws<CidParseException>(() => CidParser.Parse(bytes));
    }

    [TestMethod]
    public void RejectBinaryFormWithUnknownHashTypeByte()
    {
        byte[] bytes = CidFormatter.ToBytes(BuildKnownCid(CidCodec.Raw));
        bytes[2] = 0x13;

        Assert.Throws<CidParseException>(() => CidParser.Parse(bytes));
    }

    [TestMethod]
    public void RejectBinaryFormWithUnknownHashLengthByte()
    {
        byte[] bytes = CidFormatter.ToBytes(BuildKnownCid(CidCodec.Raw));
        bytes[3] = 0x40;

        Assert.Throws<CidParseException>(() => CidParser.Parse(bytes));
    }

    [TestMethod]
    [SuppressMessage("Usage", "MSTEST0037:Use 'Assert.HasCount' instead of 'Assert.AreEqual'", Justification = "The asserted length belongs to a span or memory view, which has no enumerable counting assert; the scalar comparison is the assertion.")]
    public void ParseBinaryFormProducesCorrectCidShape()
    {
        Veritas.Cid.Cid original = BuildKnownCid(CidCodec.Drisl);
        byte[] bytes = CidFormatter.ToBytes(original);

        Veritas.Cid.Cid parsed = CidParser.Parse(bytes);

        Assert.AreEqual(CidCodec.Drisl, parsed.Codec);
        Assert.AreEqual(32, parsed.Digest.AsSpan().Length);
        Assert.AreEqual(original.Digest, parsed.Digest);
    }

    [TestMethod]
    public void ParseRejectsNullString()
    {
        Assert.Throws<ArgumentNullException>(() => CidParser.Parse((string)null!));
    }

    [TestMethod]
    public void TryParseDigestParsesValidBinaryToCodecAndDigest()
    {
        foreach(CidCodec codec in new[] { CidCodec.Raw, CidCodec.Drisl })
        {
            Veritas.Cid.Cid original = BuildKnownCid(codec);
            byte[] bytes = CidFormatter.ToBytes(original);

            bool ok = CidParser.TryParseDigest(bytes, out CidCodec parsedCodec, out Digest32 parsedDigest);

            Assert.IsTrue(ok);
            Assert.AreEqual(codec, parsedCodec);
            Assert.AreEqual(original.Digest, parsedDigest);

            //Agrees with the materialising Parse, just without the per-CID Cid allocation.
            Veritas.Cid.Cid viaParse = CidParser.Parse(bytes);
            Assert.AreEqual(viaParse.Codec, parsedCodec);
            Assert.AreEqual(viaParse.Digest, parsedDigest);
        }
    }

    [TestMethod]
    public void TryParseDigestRejectsInvalidInputWithDefaultOut()
    {
        byte[] valid = CidFormatter.ToBytes(BuildKnownCid(CidCodec.Raw));

        //Wrong length, both shorter and longer than the canonical 36 bytes.
        Assert.IsFalse(CidParser.TryParseDigest(valid.AsSpan(0, 35), out _, out _));
        Assert.IsFalse(CidParser.TryParseDigest(new byte[37], out _, out _));

        //Each header byte invalid in turn: version, codec, hash type, hash length.
        foreach((int index, byte bad) in new[] { (0, (byte)0x02), (1, (byte)0x00), (2, (byte)0x13), (3, (byte)0x40) })
        {
            byte[] corrupted = (byte[])valid.Clone();
            corrupted[index] = bad;

            bool ok = CidParser.TryParseDigest(corrupted, out CidCodec codec, out Digest32 digest);

            Assert.IsFalse(ok);
            Assert.AreEqual(default(CidCodec), codec);
            Assert.AreEqual(default(Digest32), digest);
        }
    }

    private static Veritas.Cid.Cid BuildKnownCid(CidCodec codec)
    {
        HashDelegate sha256 = SHA256.HashData;
        return CidHasher.ComputeFromBytes("hello"u8, codec, sha256);
    }

    private static int LookupAlphabet(char c)
    {
        if(c is >= 'a' and <= 'z')
        {
            return c - 'a';
        }
        if(c is >= '2' and <= '7')
        {
            return 26 + (c - '2');
        }
        throw new ArgumentOutOfRangeException(nameof(c));
    }

    private static char AlphabetChar(int index)
    {
        return "abcdefghijklmnopqrstuvwxyz234567"[index];
    }
}
