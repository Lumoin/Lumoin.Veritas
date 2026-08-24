using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.Dggs;
using Lumoin.Veritas.Geo.Dggs.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The DGGS span recognizer's battery: the standard's example literal, the empty form, the whitespace-only
/// forms, the malformed prefix families, the separator alphabet, the opaque geometry-data abstentions, the
/// long-form bounds, and the house A5 flavour's certified body grammar. The certified region is the
/// angle-bracket IRI prefix and its whitespace separator for every grid, plus the whole cell-set body when
/// the IRI is the house grid; every non-empty FOREIGN-grid body abstains, because its formulation belongs
/// to the identified DGGS.
/// </summary>
[TestClass]
internal sealed class DggsLexicalTests
{
    /// <summary>The example literal of the standard: a DGGS IRI, one space, and a cell expression.</summary>
    private const string ExampleLiteral = "<https://w3id.org/dggs/auspix> CELL (R3234)";

    /// <summary>The house grid's angle-bracket prefix, shared by the flavour rows.</summary>
    private const string HousePrefix = "<https://lumoin.com/veritas/dggs/a5>";

    /// <summary>The standard's own example literal carries a valid prefix and an opaque body, so recognition abstains rather than certifying or rejecting.</summary>
    [TestMethod]
    public void ExampleLiteralAbstains()
    {
        Assert.AreEqual(GeometryLexicalRecognition.Unrecognized, DggsLexical.Recognize(Utf8Strings.From(ExampleLiteral).Span, out _));
    }

    /// <summary>The empty lexical form is well-formed — the empty geometry, the one form the grammar defines completely.</summary>
    [TestMethod]
    public void EmptyFormWellFormed()
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, DggsLexical.Recognize(Utf8Strings.From("").Span, out _));
    }

    /// <summary>A whitespace-only form is not the empty form and carries no angle-bracket prefix, so it is malformed.</summary>
    /// <param name="body">The lexical form under test.</param>
    [TestMethod]
    [DataRow(" ")]
    [DataRow("\t")]
    [DataRow("\r\n")]
    [DataRow("  \t  ")]
    public void WhitespaceOnlyFormMalformed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, DggsLexical.Recognize(Utf8Strings.From(body).Span, out _));
    }

    /// <summary>
    /// A malformed prefix: a form not opening with the bracket at offset zero, an unterminated bracket, an
    /// empty IRI region, whitespace or a second opening bracket inside the brackets, a missing separator, and
    /// a prefix without geometry data.
    /// </summary>
    /// <param name="body">The lexical form under test.</param>
    [TestMethod]
    [DataRow("CELL (R3234)")]
    [DataRow(" <https://w3id.org/dggs/auspix> CELL (R3234)")]
    [DataRow("<")]
    [DataRow("<https://w3id.org/dggs/auspix")]
    [DataRow("<https://w3id.org/dggs/auspix CELL (R3234)")]
    [DataRow("<>")]
    [DataRow("<> CELL (R3234)")]
    [DataRow("<https://w3id.org/dggs/aus pix> CELL (R3234)")]
    [DataRow("<https://w3id.org/dggs/aus\tpix> CELL (R3234)")]
    [DataRow("<https://w3id.org/dggs/aus<pix> CELL (R3234)")]
    [DataRow("<https://w3id.org/dggs/auspix>CELL (R3234)")]
    [DataRow("<https://w3id.org/dggs/auspix>")]
    [DataRow("<https://w3id.org/dggs/auspix> ")]
    [DataRow("<https://w3id.org/dggs/auspix> \t \r\n ")]
    public void MalformedPrefixFamilies(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, DggsLexical.Recognize(Utf8Strings.From(body).Span, out _));
    }

    /// <summary>A raw control character or the delete character inside the IRI region is provably outside the IRI grammar, so the form is malformed.</summary>
    /// <param name="insertion">The character inserted into the IRI region.</param>
    [TestMethod]
    [DataRow('\u0001')]
    [DataRow('\u001f')]
    [DataRow('\u007f')]
    public void ControlCharacterInsideBracketsMalformed(char insertion)
    {
        string body = $"<https://w3id.org/dggs/aus{insertion}pix> CELL (R3234)";

        Assert.AreEqual(GeometryLexicalRecognition.Malformed, DggsLexical.Recognize(Utf8Strings.From(body).Span, out _));
    }

    /// <summary>Every separator whitespace character — a space, a horizontal tab, a carriage return, a line feed, and any run of them — separates the prefix from the geometry data.</summary>
    /// <param name="separator">The separator run under test.</param>
    [TestMethod]
    [DataRow(" ")]
    [DataRow("\t")]
    [DataRow("\r")]
    [DataRow("\n")]
    [DataRow(" \t\r\n ")]
    public void SeparatorAlphabetAbstains(string separator)
    {
        string body = $"<https://w3id.org/dggs/auspix>{separator}CELL (R3234)";

        Assert.AreEqual(GeometryLexicalRecognition.Unrecognized, DggsLexical.Recognize(Utf8Strings.From(body).Span, out _));
    }

    /// <summary>The geometry data is opaque — cell expressions, internal whitespace, brackets, punctuation, and non-ASCII text all abstain without any claim in either direction.</summary>
    /// <param name="data">The geometry data under test.</param>
    [TestMethod]
    [DataRow("CELL (R3234)")]
    [DataRow("R3234 R3235 R3236")]
    [DataRow("{\"cells\":[\"R3234\"]}")]
    [DataRow("<cells>R3234</cells>")]
    [DataRow("solu R3234 järjestelmässä")]
    public void OpaqueGeometryDataAbstains(string data)
    {
        string body = $"<https://w3id.org/dggs/auspix> {data}";

        Assert.AreEqual(GeometryLexicalRecognition.Unrecognized, DggsLexical.Recognize(Utf8Strings.From(body).Span, out _));
    }

    /// <summary>A non-ASCII IRI passes the prefix scan — an IRI admits such characters raw, and rejecting them would over-reject.</summary>
    [TestMethod]
    public void NonAsciiIriAbstains()
    {
        Assert.AreEqual(GeometryLexicalRecognition.Unrecognized, DggsLexical.Recognize(Utf8Strings.From("<https://example.org/dggs/järjestelmä> CELL (R3234)").Span, out _));
    }

    /// <summary>A long IRI and a long body scan in one pass without any length wall.</summary>
    [TestMethod]
    public void LongFormsScanWithoutBounds()
    {
        string longIri = "<https://example.org/dggs/" + new string('a', 1000) + "> CELL (R3234)";
        string longBody = "<https://w3id.org/dggs/auspix> " + new string('R', 10000);

        Assert.AreEqual(GeometryLexicalRecognition.Unrecognized, DggsLexical.Recognize(Utf8Strings.From(longIri).Span, out _));
        Assert.AreEqual(GeometryLexicalRecognition.Unrecognized, DggsLexical.Recognize(Utf8Strings.From(longBody).Span, out _));
    }

    /// <summary>A conformant house-flavour cell-set body is certified well-formed: any keyword case, token case, leading zeros, separator runs, whitespace around the roster, and trailing whitespace.</summary>
    /// <param name="data">The geometry data under test.</param>
    [TestMethod]
    [DataRow("CELLS (4f05dccc726e0000)")]
    [DataRow("cells (4f05dccc726e0000)")]
    [DataRow("CeLLs(4f05dccc726e0000)")]
    [DataRow("CELLS ( 4f05dccc726e0000 )")]
    [DataRow("CELLS (4F05DCCC726E0000)")]
    [DataRow("CELLS (01)")]
    [DataRow("CELLS (0)")]
    [DataRow("CELLS (600000000000000 a00000000000000)")]
    [DataRow("CELLS\t(600000000000000\r\na00000000000000)  ")]
    public void HouseFlavourConformantBodyWellFormed(string data)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, DggsLexical.Recognize(Utf8Strings.From($"{HousePrefix} {data}").Span, out _));
    }

    /// <summary>A house-flavour body violation is malformed: a wrong keyword, an empty roster, a non-hexadecimal or overlong token, an unterminated roster, trailing content, a comma separator, or a missing roster.</summary>
    /// <param name="data">The geometry data under test.</param>
    [TestMethod]
    [DataRow("CELL (4f05dccc726e0000)")]
    [DataRow("CELLS ()")]
    [DataRow("CELLS ( )")]
    [DataRow("CELLS (xyz)")]
    [DataRow("CELLS (11111111111111111)")]
    [DataRow("CELLS (4f05dccc726e0000")]
    [DataRow("CELLS (4f05dccc726e0000) extra")]
    [DataRow("CELLS (4f05dccc726e0000,600000000000000)")]
    [DataRow("CELLS 4f05dccc726e0000")]
    public void HouseFlavourViolatingBodyMalformed(string data)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, DggsLexical.Recognize(Utf8Strings.From($"{HousePrefix} {data}").Span, out _));
    }

    /// <summary>A token that parses as hexadecimal but names no decodable cell — an origin index outside the twelve — is malformed under the house flavour.</summary>
    [TestMethod]
    public void HouseFlavourUndecodableCellMalformed()
    {
        ulong undecodable = A5.GetResolutionZeroCells()[11].Value + (1UL << Serialization.HilbertStartBit);

        Assert.AreEqual(GeometryLexicalRecognition.Malformed, DggsLexical.Recognize(Utf8Strings.From($"{HousePrefix} CELLS ({undecodable:x})").Span, out _));
    }

    /// <summary>A foreign grid keeps the abstention even over a body spelled in the house cell-set shape — the house certifies only its own flavour.</summary>
    [TestMethod]
    public void ForeignGridWithHouseShapedBodyAbstains()
    {
        Assert.AreEqual(GeometryLexicalRecognition.Unrecognized, DggsLexical.Recognize(Utf8Strings.From("<https://w3id.org/dggs/auspix> CELLS (4f05dccc726e0000)").Span, out _));
    }
}
