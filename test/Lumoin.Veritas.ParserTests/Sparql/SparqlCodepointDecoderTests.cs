using System.Text;
using Lumoin.Veritas.Sparql.Lexer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests for <see cref="SparqlCodepointDecoder"/>: the SPARQL 1.2 §19.2 numeric codepoint-escape
/// rewrite (<c>\uXXXX</c> / <c>\UXXXXXXXX</c>), its precedence over string escapes, the malformed-escape
/// diagnostics, and the decoded-to-source offset map that keeps spans in source coordinates.
/// </summary>
[TestClass]
internal sealed class SparqlCodepointDecoderTests
{
    /// <summary>A four-hex escape decodes to its codepoint's UTF-8 byte, mapped to the escape's source start.</summary>
    [TestMethod]
    public void DecodesFourHexEscapeToUtf8Byte()
    {
        SparqlCodepointDecoder decoder = Decode("\\u0041", out int consumed);

        Assert.AreEqual(6, consumed);
        Assert.AreSequenceEqual(new byte[] { (byte)'A' }, decoder.Decoded.ToArray());
        Assert.AreEqual(0, decoder.SourceOffsetAt(0));
        Assert.AreEqual(6, decoder.SourceOffsetAt(1));
    }

    /// <summary>
    /// A backslash before a numeric escape is emitted literally and the scan resumes at the escape, so
    /// <c>\\u0074</c> yields the string escape <c>\t</c> — codepoint decoding precedes ECHAR interpretation.
    /// </summary>
    [TestMethod]
    public void BackslashBeforeNumericEscapeIsLiteralThenDecodes()
    {
        SparqlCodepointDecoder decoder = Decode("\\\\u0074", out _);

        //Literal backslash (U+5C) followed by the decode of U+74 ('t').
        Assert.AreSequenceEqual(new byte[] { (byte)'\\', (byte)'t' }, decoder.Decoded.ToArray());
        Assert.AreEqual(0, decoder.SourceOffsetAt(0));
        Assert.AreEqual(1, decoder.SourceOffsetAt(1));
    }

    /// <summary>A codepoint-escaped quotation mark decodes to a structural quote byte.</summary>
    [TestMethod]
    public void CodepointEscapedQuoteDecodesToQuoteByte()
    {
        SparqlCodepointDecoder decoder = Decode("\\u0022", out _);

        Assert.AreSequenceEqual(new byte[] { (byte)'"' }, decoder.Decoded.ToArray());
    }

    /// <summary>An eight-hex escape decodes a supplementary-plane codepoint to its four UTF-8 bytes, all mapped to the escape start.</summary>
    [TestMethod]
    public void DecodesEightHexEscapeToFourUtf8Bytes()
    {
        SparqlCodepointDecoder decoder = Decode("\\U0001F600", out int consumed);

        Assert.AreEqual(10, consumed);
        Assert.AreSequenceEqual(new byte[] { 0xF0, 0x9F, 0x98, 0x80 }, decoder.Decoded.ToArray());
        Assert.AreEqual(0, decoder.SourceOffsetAt(0));
        Assert.AreEqual(0, decoder.SourceOffsetAt(3));
        Assert.AreEqual(10, decoder.SourceOffsetAt(4));
    }

    /// <summary>A non-hex digit in a numeric escape is recorded as an invalid-hex diagnostic and the escape is dropped.</summary>
    [TestMethod]
    public void InvalidHexDigitRecordsDiagnostic()
    {
        SparqlCodepointDecoder decoder = Decode("\\u00ZZ", out _);

        Assert.AreEqual(0, decoder.DecodedLength);
        Assert.HasCount(1, decoder.Diagnostics);
        Assert.AreEqual(SparqlLexErrorCode.InvalidHexDigit, decoder.Diagnostics[0].Code);
    }

    /// <summary>A surrogate codepoint is rejected as a diagnostic and dropped.</summary>
    [TestMethod]
    public void SurrogateCodepointRecordsDiagnostic()
    {
        SparqlCodepointDecoder decoder = Decode("\\uD800", out _);

        Assert.AreEqual(0, decoder.DecodedLength);
        Assert.HasCount(1, decoder.Diagnostics);
        Assert.AreEqual(SparqlLexErrorCode.SurrogateCodePoint, decoder.Diagnostics[0].Code);
    }

    /// <summary>A codepoint beyond U+10FFFF is rejected as a diagnostic and dropped.</summary>
    [TestMethod]
    public void OutOfRangeCodepointRecordsDiagnostic()
    {
        SparqlCodepointDecoder decoder = Decode("\\U00110000", out _);

        Assert.AreEqual(0, decoder.DecodedLength);
        Assert.HasCount(1, decoder.Diagnostics);
        Assert.AreEqual(SparqlLexErrorCode.CodePointOutOfRange, decoder.Diagnostics[0].Code);
    }

    /// <summary>The offset map reports source line and column, including for an escape that follows a newline.</summary>
    [TestMethod]
    public void OffsetMapTracksSourceLineAndColumnAcrossEscapes()
    {
        SparqlCodepointDecoder decoder = Decode("A\n\\u0042", out _);

        //Decoded bytes are 'A', '\n', 'B'. 'B' originates from the escape that starts at source line 1, column 0.
        Assert.AreSequenceEqual(new byte[] { (byte)'A', (byte)'\n', (byte)'B' }, decoder.Decoded.ToArray());
        Assert.AreEqual(2, decoder.SourceOffsetAt(2));
        Assert.AreEqual(1, decoder.SourceLineAt(2));
        Assert.AreEqual(0, decoder.SourceColumnAt(2));
    }

    /// <summary>A partial escape at a non-final span boundary is left unconsumed for the next feed.</summary>
    [TestMethod]
    public void PartialEscapeAtNonFinalBoundaryIsHeld()
    {
        SparqlCodepointDecoder decoder = new();
        byte[] source = Encoding.UTF8.GetBytes("\\u00");

        int consumed = decoder.Feed(source, isFinal: false);

        Assert.AreEqual(0, consumed);
        Assert.AreEqual(0, decoder.DecodedLength);
    }

    /// <summary>A numeric escape split across two feeds decodes once the remaining bytes arrive.</summary>
    [TestMethod]
    public void EscapeSplitAcrossFeedsDecodes()
    {
        SparqlCodepointDecoder decoder = new();

        int firstConsumed = decoder.Feed(Encoding.UTF8.GetBytes("\\u00"), isFinal: false);
        Assert.AreEqual(0, firstConsumed);

        int secondConsumed = decoder.Feed(Encoding.UTF8.GetBytes("\\u0041"), isFinal: true);

        Assert.AreEqual(6, secondConsumed);
        Assert.AreSequenceEqual(new byte[] { (byte)'A' }, decoder.Decoded.ToArray());
    }

    /// <summary>Decodes the supplied text in a single final feed.</summary>
    /// <param name="text">The source text to decode.</param>
    /// <param name="consumed">The number of source bytes the decoder consumed.</param>
    /// <returns>The decoder holding the decoded bytes, map, and diagnostics.</returns>
    private static SparqlCodepointDecoder Decode(string text, out int consumed)
    {
        SparqlCodepointDecoder decoder = new();
        consumed = decoder.Feed(Encoding.UTF8.GetBytes(text), isFinal: true);

        return decoder;
    }
}
