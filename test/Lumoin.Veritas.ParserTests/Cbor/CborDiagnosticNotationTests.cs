using System;
using Lumoin.Veritas.Cbor;

namespace Lumoin.Veritas.ParserTests.Cbor;

/// <summary>
/// Tests <see cref="CborDiagnosticNotation"/> against hand-encoded CBOR whose Extended Diagnostic
/// Notation is known (the encodings are drawn from RFC 8949 Appendix A), plus the embedded-CBOR
/// (<c>&lt;&lt;…&gt;&gt;</c>) rendering and its fallback to hex.
/// </summary>
[TestClass]
internal sealed class CborDiagnosticNotationTests
{
    /// <summary>Renders the hex-encoded CBOR to diagnostic notation with the default options.</summary>
    /// <param name="hex">The CBOR bytes as hex.</param>
    /// <returns>The EDN.</returns>
    private static string Edn(string hex)
    {
        return CborDiagnosticNotation.ToDiagnosticNotation(Convert.FromHexString(hex));
    }

    /// <summary>Unsigned and negative integers render in decimal, including values below <see cref="long.MinValue"/>.</summary>
    [TestMethod]
    public void IntegersRenderInDecimal()
    {
        Assert.AreEqual("0", Edn("00"));
        Assert.AreEqual("10", Edn("0a"));
        Assert.AreEqual("-1", Edn("20"));
        Assert.AreEqual("-500", Edn("3901f3"));
        Assert.AreEqual("-18446744073709551616", Edn("3bffffffffffffffff"));
    }

    /// <summary>The simple values render as their EDN keywords.</summary>
    [TestMethod]
    public void SimpleValuesRenderAsKeywords()
    {
        Assert.AreEqual("false", Edn("f4"));
        Assert.AreEqual("true", Edn("f5"));
        Assert.AreEqual("null", Edn("f6"));
        Assert.AreEqual("undefined", Edn("f7"));
        Assert.AreEqual("simple(16)", Edn("f0"));
    }

    /// <summary>Floats render with a forced decimal point, and the non-finite values render as keywords.</summary>
    [TestMethod]
    public void FloatsRenderWithDecimalPointOrKeyword()
    {
        Assert.AreEqual("1.5", Edn("f93e00"));
        Assert.AreEqual("1.0", Edn("fb3ff0000000000000"));
        Assert.AreEqual("Infinity", Edn("f97c00"));
        Assert.AreEqual("NaN", Edn("f97e00"));
        Assert.AreEqual("-Infinity", Edn("f9fc00"));
    }

    /// <summary>Text strings are quoted and escaped; byte strings render as lowercase hex.</summary>
    [TestMethod]
    public void StringsRenderQuotedOrHex()
    {
        Assert.AreEqual("\"a\"", Edn("6161"));
        Assert.AreEqual("\"a\\nb\"", Edn("63610a62"));
        Assert.AreEqual("h'010203'", Edn("43010203"));
        Assert.AreEqual("h''", Edn("40"));
    }

    /// <summary>Arrays and maps render with the spaced separators, nesting structurally.</summary>
    [TestMethod]
    public void ArraysAndMapsRenderStructurally()
    {
        Assert.AreEqual("[1, 2, 3]", Edn("83010203"));
        Assert.AreEqual("[]", Edn("80"));
        Assert.AreEqual("{\"a\": 1, \"b\": [2, 3]}", Edn("a26161016162820203"));
        Assert.AreEqual("[1, 2]", Edn("9f0102ff"));
    }

    /// <summary>Tags render as <c>n(item)</c>.</summary>
    [TestMethod]
    public void TagsRenderWithNumberAndParentheses()
    {
        Assert.AreEqual("0(\"2013-03-21T20:04:00Z\")", Edn("c074323031332d30332d32315432303a30343a30305a"));
        Assert.AreEqual("32(\"x\")", Edn("d8206178"));
    }

    /// <summary>With embedded decoding enabled, a byte string holding a complete CBOR item renders as <c>&lt;&lt;…&gt;&gt;</c>; off, it stays hex.</summary>
    [TestMethod]
    public void EmbeddedByteStringRendersAsNestedCborWhenEnabled()
    {
        byte[] cbor = Convert.FromHexString("43820102");

        Assert.AreEqual("h'820102'", CborDiagnosticNotation.ToDiagnosticNotation(cbor));
        Assert.AreEqual("<<[1, 2]>>", CborDiagnosticNotation.ToDiagnosticNotation(cbor, new CborDiagnosticOptions(DecodeEmbeddedByteStrings: true)));
    }

    /// <summary>A byte string whose content does not decode to exactly one CBOR item falls back to hex even with embedded decoding enabled.</summary>
    [TestMethod]
    public void EmbeddedDecodingFallsBackToHexOnTrailingBytes()
    {
        byte[] cbor = Convert.FromHexString("420101");

        Assert.AreEqual("h'0101'", CborDiagnosticNotation.ToDiagnosticNotation(cbor, new CborDiagnosticOptions(DecodeEmbeddedByteStrings: true)));
    }
}
