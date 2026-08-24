using System;
using System.Buffers;
using System.Collections.Generic;
using Lumoin.Veritas.Cbor;

namespace Lumoin.Veritas.ParserTests.Cbor;

/// <summary>
/// Tests for <see cref="CborCanonicalizer"/>: read arbitrary CBOR under
/// <see cref="CborConformanceMode.Lax"/> and re-emit in CDE form per RFC
/// 8949 §4.2.
/// </summary>
[TestClass]
internal sealed class CborCanonicalizerTests
{
    public required TestContext TestContext { get; set; }

    private static byte[] Canonicalize(byte[] source)
    {
        ArrayBufferWriter<byte> sink = new();
        CborCanonicalizer.Canonicalize(source, sink);
        return sink.WrittenSpan.ToArray();
    }

    private static byte[] Hex(string h) => Convert.FromHexString(h.Replace(" ", string.Empty, StringComparison.Ordinal));
    private static string HexOf(byte[] b) => Convert.ToHexString(b);

    [TestMethod]
    public void CanonicalisesPrimitiveScalars()
    {
        //null, true, false, small int, neg int, simple-form floats — all
        //pass through cleanly.
        Assert.AreEqual("F6", HexOf(Canonicalize(Hex("F6"))));        //null
        Assert.AreEqual("F5", HexOf(Canonicalize(Hex("F5"))));        //true
        Assert.AreEqual("F4", HexOf(Canonicalize(Hex("F4"))));        //false
        Assert.AreEqual("01", HexOf(Canonicalize(Hex("01"))));        //unsigned 1
        Assert.AreEqual("20", HexOf(Canonicalize(Hex("20"))));        //negative -1
    }

    [TestMethod]
    public void CanonicalisesTextString()
    {
        //"hello" — 5-byte text string.
        byte[] result = Canonicalize(Hex("65 68 65 6C 6C 6F"));
        Assert.AreEqual("6568656C6C6F", HexOf(result));
    }

    [TestMethod]
    public void CanonicalisesByteString()
    {
        //byte string of three bytes 0x01 0x02 0x03
        byte[] result = Canonicalize(Hex("43 01 02 03"));
        Assert.AreEqual("43010203", HexOf(result));
    }

    [TestMethod]
    public void ShortensIntegerToSmallestWidth()
    {
        //Non-canonical: 25-byte-encoded form of the value 1 (1B 00 00 00 00 00 00 00 01).
        //Canonical: 01 (single byte). The canonicaliser must shorten.
        byte[] nonCanonical = Hex("1B 00 00 00 00 00 00 00 01");
        byte[] canonical = Canonicalize(nonCanonical);
        Assert.AreEqual("01", HexOf(canonical));
    }

    [TestMethod]
    public void ShortensTwoByteIntegerToOneByte()
    {
        //Value 23 encoded as a 2-byte form (18 17). Canonical: just 17.
        byte[] nonCanonical = Hex("18 17");
        byte[] canonical = Canonicalize(nonCanonical);
        Assert.AreEqual("17", HexOf(canonical));
    }

    [TestMethod]
    public void SortsMapKeysLengthFirstLexically()
    {
        //Input map: {"b": 2, "a": 1} — out of canonical order. CDE sorts
        //by encoded key bytes; both keys are 1-byte text, so ordinal
        //order applies and "a" sorts before "b".
        byte[] nonCanonical = Hex("A2 61 62 02 61 61 01");
        byte[] canonical = Canonicalize(nonCanonical);
        //Expected: A2 61 61 01 61 62 02
        Assert.AreEqual("A2616101616202", HexOf(canonical));
    }

    [TestMethod]
    public void SortsMapKeysShorterFirst()
    {
        //Length-first lexical: shorter keys precede longer keys regardless
        //of byte value. Here "bb" (2 bytes) should come AFTER "a" (1 byte).
        byte[] nonCanonical = Hex("A2 62 62 62 02 61 61 01");
        byte[] canonical = Canonicalize(nonCanonical);
        Assert.AreEqual("A26161016262620 2".Replace(" ", string.Empty, StringComparison.Ordinal), HexOf(canonical));
    }

    [TestMethod]
    public void CollapsesIndefiniteArrayToDefinite()
    {
        //Indefinite array [1, 2, 3] in non-canonical form: 9F 01 02 03 FF
        //Canonical equivalent: definite array 83 01 02 03
        byte[] nonCanonical = Hex("9F 01 02 03 FF");
        byte[] canonical = Canonicalize(nonCanonical);
        Assert.AreEqual("8301 0203".Replace(" ", string.Empty, StringComparison.Ordinal), HexOf(canonical));
    }

    [TestMethod]
    public void CollapsesIndefiniteMapToDefinite()
    {
        //Indefinite map {"a": 1, "b": 2}: BF 61 61 01 61 62 02 FF
        //Canonical (definite + sorted): A2 61 61 01 61 62 02
        byte[] nonCanonical = Hex("BF 61 61 01 61 62 02 FF");
        byte[] canonical = Canonicalize(nonCanonical);
        Assert.AreEqual("A2616101616202", HexOf(canonical));
    }

    [TestMethod]
    public void PreservesTagsAndCanonicalisesContent()
    {
        //tag 42 followed by a non-canonical integer 1B...01. Tag survives;
        //content is canonicalised.
        byte[] nonCanonical = Hex("D8 2A 1B 00 00 00 00 00 00 00 01");
        byte[] canonical = Canonicalize(nonCanonical);
        //Expected: D8 2A 01
        Assert.AreEqual("D82A01", HexOf(canonical));
    }

    [TestMethod]
    public void IsIdempotent()
    {
        //Canonicalising already-canonical bytes must produce the same bytes.
        byte[] mixedInput = Hex("A2 62 62 62 02 61 61 01");
        byte[] pass1 = Canonicalize(mixedInput);
        byte[] pass2 = Canonicalize(pass1);
        Assert.AreSequenceEqual(pass1, pass2);
    }

    [TestMethod]
    public void IdempotentOverNestedDocument()
    {
        //A nested map+array with mixed-order keys. Two passes produce
        //identical bytes; the second pass must be a pure transcription.
        byte[] input = Hex("A2 63 7A 7A 7A 9F 01 02 FF 63 61 61 61 A1 61 62 02");
        byte[] pass1 = Canonicalize(input);
        byte[] pass2 = Canonicalize(pass1);
        Assert.AreSequenceEqual(pass1, pass2);
    }

    [TestMethod]
    public void DeterministicOverEquivalentInputs()
    {
        //Same data with reordered map keys must canonicalise to the same
        //bytes. This is the property that makes canonicalisation suitable
        //for content-addressing.
        byte[] orderedA = Hex("A2 61 61 01 61 62 02");           //{a:1, b:2}
        byte[] orderedB = Hex("A2 61 62 02 61 61 01");           //{b:2, a:1}

        byte[] canonicalA = Canonicalize(orderedA);
        byte[] canonicalB = Canonicalize(orderedB);

        Assert.AreSequenceEqual(canonicalA, canonicalB);
    }

    [TestMethod]
    public void PreservesUnsignedIntegerAtUlongMaxValue()
    {
        //ulong.MaxValue is 2^64 - 1. Wire form: 1B FF FF FF FF FF FF FF FF.
        //Already canonical at width 8.
        byte[] canonical = Canonicalize(Hex("1B FF FF FF FF FF FF FF FF"));
        Assert.AreEqual("1BFFFFFFFFFFFFFFFF", HexOf(canonical));
    }

    [TestMethod]
    public void PreservesNegativeIntegerAtLongMinValue()
    {
        //long.MinValue = -2^63. CBOR encodes as -(1 + arg) where arg = 2^63 - 1.
        //arg in 8-byte form: 7F FF FF FF FF FF FF FF, major-type-1 header 3B.
        byte[] canonical = Canonicalize(Hex("3B 7F FF FF FF FF FF FF FF"));
        Assert.AreEqual("3B7FFFFFFFFFFFFFFF", HexOf(canonical));
    }

    [TestMethod]
    public void PreservesNegativeIntegerBelowLongMinValue()
    {
        //Just below long.MinValue: -(2^63 + 1). CBOR arg = 2^63.
        //Header 3B, argument 80 00 00 00 00 00 00 00.
        //This value cannot be expressed as a .NET long; the canonicaliser
        //must round-trip it via the raw representation.
        byte[] canonical = Canonicalize(Hex("3B 80 00 00 00 00 00 00 00"));
        Assert.AreEqual("3B8000000000000000", HexOf(canonical));
    }

    [TestMethod]
    public void PreservesNegativeIntegerAtSmallestRepresentable()
    {
        //Smallest CBOR negative: -(2^64). Arg = 2^64 - 1 = FF FF FF FF FF FF FF FF.
        //Header 3B.
        byte[] canonical = Canonicalize(Hex("3B FF FF FF FF FF FF FF FF"));
        Assert.AreEqual("3BFFFFFFFFFFFFFFFF", HexOf(canonical));
    }

    [TestMethod]
    public void IdempotentOverLargeNegative()
    {
        //Canonicalising a large negative twice produces the same bytes.
        byte[] input = Hex("3B 80 00 00 00 00 00 00 00");
        byte[] pass1 = Canonicalize(input);
        byte[] pass2 = Canonicalize(pass1);
        Assert.AreSequenceEqual(pass1, pass2);
    }

    [TestMethod]
    public void CanonicalisesLargeNegativeInsideMap()
    {
        //Map containing a large-negative value to verify the read/write
        //path composes with container traversal.
        byte[] input = Hex(
            "A1 " +                          //map(1)
            "61 78 " +                       //"x"
            "3B 80 00 00 00 00 00 00 00"     //negative -(2^63 + 1)
        );
        byte[] canonical = Canonicalize(input);
        byte[] expected = Hex(
            "A1 " +
            "61 78 " +
            "3B 80 00 00 00 00 00 00 00"
        );
        Assert.AreSequenceEqual(expected, canonical);
    }

    [TestMethod]
    public void CanonicalisesNestedMapsAndArrays()
    {
        //Outer map with unsorted keys and an inner map that also has
        //unsorted keys.
        byte[] nonCanonical = Hex(
            "A2 " +
            "63 62 62 62 " +           //key "bbb"
            "A2 61 79 01 61 78 02 " +  //inner map {"y":1, "x":2}  → canonicalises to {"x":2, "y":1}
            "63 61 61 61 " +           //key "aaa"
            "82 01 02"                 //array [1, 2]
        );
        byte[] canonical = Canonicalize(nonCanonical);

        //Expected canonical bytes:
        //  outer map sorted by length-first lexical:
        //    "aaa" (3 bytes, ordinal less than "bbb") comes first
        //  inner map's keys "x", "y" sort as "x", "y"
        byte[] expected = Hex(
            "A2 " +
            "63 61 61 61 " +           //key "aaa"
            "82 01 02 " +              //array [1, 2]
            "63 62 62 62 " +           //key "bbb"
            "A2 61 78 02 61 79 01"     //inner map {"x":2, "y":1}
        );
        Assert.AreSequenceEqual(expected, canonical);
    }
}
