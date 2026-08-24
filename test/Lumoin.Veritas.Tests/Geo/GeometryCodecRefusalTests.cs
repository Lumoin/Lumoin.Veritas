using System.Text;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.SimpleFeatures;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The refusal value type of the serialization codec family. Two facts carry
/// the whole contract: the success sentinel is the None property and never
/// the default value — a zero byte offset is a real offset, so a consumer
/// testing against default would read the first byte of a document as
/// success — and the refusal compares by value so test rows can assert whole
/// refusals, not field by field. The well-known-text reader answers in this
/// currency, and the DGGS recognizer and the house body scan locate the byte
/// the projection carries into it, so every family of the substrate names its
/// reason and its first offending byte in one shape. Offsets are computed from
/// the offending text a row states, never hand-counted.
/// </summary>
[TestClass]
internal sealed class GeometryCodecRefusalTests
{
    /// <summary>The success sentinel carries the None kind and names no offending byte.</summary>
    [TestMethod]
    public void SuccessSentinelCarriesNoKindAndNoOffendingByte()
    {
        Assert.AreEqual(GeometryCodecRefusalKind.None, GeometryCodecRefusal.None.Kind, "the success sentinel carries the None kind");
        Assert.AreEqual(-1, GeometryCodecRefusal.None.ByteOffset, "the success sentinel names no offending byte");
    }

    /// <summary>The zero-initialized default value carries a real byte offset and is never mistaken for the success sentinel.</summary>
    [TestMethod]
    public void DefaultValueIsNotTheSuccessSentinel()
    {
        //Zero-initialization yields byte offset zero, a real offset into every non-empty
        //document, so the default value must never be mistaken for success.
        Assert.AreNotEqual(GeometryCodecRefusal.None, default(GeometryCodecRefusal), "the default value is not the success sentinel");
        Assert.AreEqual(0, default(GeometryCodecRefusal).ByteOffset, "the default value's offset is a real index");
    }

    /// <summary>Two refusals with equal kind and offset compare equal, and a differing offset compares unequal.</summary>
    [TestMethod]
    public void RefusalsCompareByValue()
    {
        var first = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, 12);
        var second = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, 12);
        var different = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, 13);

        Assert.AreEqual(first, second, "equal kind and offset compare equal");
        Assert.AreNotEqual(first, different, "a differing offset compares unequal");
    }

    /// <summary>
    /// The well-known-text reader answers the shared currency: a grammar break, a structural
    /// shortfall, an uncertified tag, an arity disagreement, an unrepresentable empty member,
    /// and trailing content each name their reason and the first byte that carries it.
    /// </summary>
    /// <param name="text">The well-known-text body under test.</param>
    /// <param name="kind">The expected reason.</param>
    /// <param name="offendingText">The text whose first byte the offset must name.</param>
    [TestMethod]
    [DataRow("POINT(1 X)", GeometryCodecRefusalKind.MalformedDocument, "X", DisplayName = "a broken position refuses as malformed at the offending byte")]
    [DataRow("LINESTRING(1 2)", GeometryCodecRefusalKind.StructuralViolation, ")", DisplayName = "a one-position line refuses as structural at the byte closing the run")]
    [DataRow("POLYGON((0 0, 1 0, 1 1, 2 2))", GeometryCodecRefusalKind.StructuralViolation, "))", DisplayName = "an unclosed ring refuses as structural at the byte closing the run")]
    [DataRow("CIRCULARSTRING(0 0, 1 1, 2 0)", GeometryCodecRefusalKind.UnsupportedGeometry, "CIRCULARSTRING", DisplayName = "a curve tag refuses as unsupported at the tag")]
    [DataRow("LINESTRING(1 2 3, 4 5)", GeometryCodecRefusalKind.DimensionMismatch, ")", DisplayName = "a disagreeing arity refuses as a dimension mismatch where the position stopped")]
    [DataRow("MULTIPOINT(EMPTY)", GeometryCodecRefusalKind.EmptyUnrepresentable, "EMPTY", DisplayName = "an empty member refuses as an unrepresentable empty at the member")]
    [DataRow("POINT(1 2) POINT(3 4)", GeometryCodecRefusalKind.TrailingContent, "POINT(3 4)", DisplayName = "content after the geometry refuses as trailing at its first byte")]
    public void TheWellKnownTextReaderRefusesInTheSharedCurrency(string text, GeometryCodecRefusalKind kind, string offendingText)
    {
        byte[] utf8Text = Encoding.UTF8.GetBytes(text);
        bool accepted = WktGeometryReader.TryRead(utf8Text, out FlatGeometry geometry, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, $"'{text}' must refuse");
        Assert.AreEqual(new GeometryCodecRefusal(kind, XmlScannerAssert.ByteOffsetOf(text, offendingText)), refusal, $"the whole refusal for '{text}'");
        Assert.AreEqual(default, geometry, "a refused read yields the default carrier");
    }

    /// <summary>A member past the reader's nesting bound refuses at that member's first byte, with nothing rented.</summary>
    [TestMethod]
    public void TheWellKnownTextReaderRefusesPastItsNestingBound()
    {
        const int WrappersPastTheBound = 32;
        string text = string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION(", WrappersPastTheBound))
            + "POINT(1 2)"
            + string.Concat(Enumerable.Repeat(")", WrappersPastTheBound));
        byte[] utf8Text = Encoding.UTF8.GetBytes(text);
        bool accepted = WktGeometryReader.TryRead(utf8Text, out FlatGeometry geometry, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, "the member past the bound must refuse");
        Assert.AreEqual(
            new GeometryCodecRefusal(GeometryCodecRefusalKind.NestingTooDeep, XmlScannerAssert.ByteOffsetOf(text, "POINT(1 2)")),
            refusal,
            "the refusal names the member the bound refused");
        Assert.AreEqual(default, geometry, "a refused read yields the default carrier");
    }

    /// <summary>
    /// The DGGS recognizer locates its own offending byte over the whole lexical form: a missing
    /// prefix, an empty IRI region, a missing separator, and every house-body violation alike.
    /// </summary>
    /// <param name="body">The DGGS lexical form under test.</param>
    /// <param name="offendingText">The text whose first byte the offset must name.</param>
    [TestMethod]
    [DataRow("CELL (R3234)", "CELL", DisplayName = "a form without the angle-bracket prefix offends at its first byte")]
    [DataRow("<> CELL (R3234)", ">", DisplayName = "an empty IRI region offends at its terminator")]
    [DataRow("<https://w3id.org/dggs/auspix>CELL (R3234)", "CELL", DisplayName = "a missing separator offends where it had to appear")]
    [DataRow("<https://lumoin.com/veritas/dggs/a5> CELLS (xyz)", "xyz", DisplayName = "a non-hexadecimal cell token offends at the token")]
    [DataRow("<https://lumoin.com/veritas/dggs/a5> CELLS ()", ")", DisplayName = "a memberless roster offends where its member had to appear")]
    [DataRow("<https://lumoin.com/veritas/dggs/a5> CELLS (4f05dccc726e0000) extra", "extra", DisplayName = "trailing content offends at its first byte")]
    public void TheDggsRecognizerLocatesItsOffendingByte(string body, string offendingText)
    {
        GeometryLexicalRecognition recognition = DggsLexical.Recognize(Utf8Strings.From(body).Span, out int offendingOffset);

        Assert.AreEqual(GeometryLexicalRecognition.Malformed, recognition, $"'{body}' must be malformed");
        Assert.AreEqual(XmlScannerAssert.ByteOffsetOf(body, offendingText), offendingOffset, $"the offending byte of '{body}'");
    }

    /// <summary>
    /// The house body scan locates its own offending byte inside the geometry-data region: an absent
    /// keyword or opener names the byte where it had to appear, a token violation names the token, a
    /// memberless roster names where its member had to appear, and trailing content names its first byte.
    /// </summary>
    /// <param name="data">The geometry-data region under test.</param>
    /// <param name="offendingText">The text whose first byte the offset must name.</param>
    [TestMethod]
    [DataRow("CELL (4f05dccc726e0000)", "CELL", DisplayName = "a wrong keyword offends at the byte where the keyword had to appear")]
    [DataRow("CELLS 4f05dccc726e0000", "4f05dccc726e0000", DisplayName = "a missing opener offends at the byte where it had to appear")]
    [DataRow("CELLS (xyz)", "xyz", DisplayName = "a non-hexadecimal token offends at the token")]
    [DataRow("CELLS ()", ")", DisplayName = "a memberless roster offends where its member had to appear")]
    [DataRow("CELLS (4f05dccc726e0000,600000000000000)", ",", DisplayName = "a comma separator offends at the comma")]
    [DataRow("CELLS (4f05dccc726e0000) extra", "extra", DisplayName = "trailing content offends at its first byte")]
    public void TheHouseBodyScanLocatesItsOffendingByte(string data, string offendingText)
    {
        bool certified = A5DggsBody.Certify(Utf8Strings.From(data).Span, out int offendingOffset);

        Assert.IsFalse(certified, $"'{data}' must be outside the body grammar");
        Assert.AreEqual(XmlScannerAssert.ByteOffsetOf(data, offendingText), offendingOffset, $"the offending byte of '{data}'");
    }

    /// <summary>An over-long cell token names the byte past its longest admissible extent, the sixteenth hexadecimal digit's successor.</summary>
    [TestMethod]
    public void AnOverlongCellTokenOffendsPastItsLongestAdmissibleExtent()
    {
        const int LongestTokenLength = 16;
        const string Data = "CELLS (11111111111111111)";
        bool certified = A5DggsBody.Certify(Utf8Strings.From(Data).Span, out int offendingOffset);

        Assert.IsFalse(certified, "a seventeen-digit token is outside the body grammar");
        Assert.AreEqual(XmlScannerAssert.ByteOffsetOf(Data, "1") + LongestTokenLength, offendingOffset, "the offense names the byte past the longest admissible extent");
    }
}
