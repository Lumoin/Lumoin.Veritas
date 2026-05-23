using System;
using System.Security.Cryptography;
using Lumoin.Veritas.Canonicalization;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Tests.Core;

/// <summary>
/// Verifies the canonical N-Quads escaping rules through the public
/// <see cref="RdfCanonicalizer.Canonicalize(System.Collections.Generic.IEnumerable{Quad}, HashDelegate)"/>
/// surface. The escaping is implemented by the canonicaliser's
/// internal serializer; these tests pin its observable output to the
/// canonical N-Triples / N-Quads form the W3C C14N suite expects.
/// </summary>
[TestClass]
internal sealed class RdfCanonicalEscapingTests
{
    public TestContext TestContext { get; set; } = null!;

    private static HashDelegate Sha256 { get; } = SHA256.HashData;

    [TestMethod]
    public void EscapesDoubleQuoteAsBackslashQuote()
    {
        string line = CanonicalizeLiteral("a\"b", XsdString);

        Assert.Contains("\"a\\\"b\"", line, StringComparison.Ordinal);
    }

    [TestMethod]
    public void EscapesBackslashAsDoubleBackslash()
    {
        string line = CanonicalizeLiteral("a\\b", XsdString);

        Assert.Contains("\"a\\\\b\"", line, StringComparison.Ordinal);
    }

    [TestMethod]
    public void EscapesBackspaceAsBackslashB()
    {
        string line = CanonicalizeLiteral("\b", XsdString);

        Assert.Contains("\"\\b\"", line, StringComparison.Ordinal);
    }

    [TestMethod]
    public void EscapesTabAsBackslashT()
    {
        string line = CanonicalizeLiteral("\t", XsdString);

        Assert.Contains("\"\\t\"", line, StringComparison.Ordinal);
    }

    [TestMethod]
    public void EscapesLineFeedAsBackslashN()
    {
        string line = CanonicalizeLiteral("\n", XsdString);

        Assert.Contains("\"\\n\"", line, StringComparison.Ordinal);
    }

    [TestMethod]
    public void EscapesFormFeedAsBackslashF()
    {
        string line = CanonicalizeLiteral("\f", XsdString);

        Assert.Contains("\"\\f\"", line, StringComparison.Ordinal);
    }

    [TestMethod]
    public void EscapesCarriageReturnAsBackslashR()
    {
        string line = CanonicalizeLiteral("\r", XsdString);

        Assert.Contains("\"\\r\"", line, StringComparison.Ordinal);
    }

    [TestMethod]
    public void EscapesControlCharactersBelowSpaceAsUppercaseUnicodeForm()
    {
        //U+000E is one of the controls that have no named escape.
        string line = CanonicalizeLiteral(((char)0x0E).ToString(), XsdString);

        Assert.Contains("\"\\u000E\"", line, StringComparison.Ordinal);
    }

    [TestMethod]
    public void EscapesDeleteCharacterAsUnicodeForm()
    {
        string line = CanonicalizeLiteral(((char)0x7F).ToString(), XsdString);

        Assert.Contains("\"\\u007F\"", line, StringComparison.Ordinal);
    }

    [TestMethod]
    public void PreservesPrintableAscii()
    {
        string line = CanonicalizeLiteral("plain text", XsdString);

        Assert.Contains("\"plain text\"", line, StringComparison.Ordinal);
    }

    [TestMethod]
    public void PreservesNonAsciiCharacters()
    {
        string line = CanonicalizeLiteral("naïve ß 中", XsdString);

        Assert.Contains("\"naïve ß 中\"", line, StringComparison.Ordinal);
    }

    [TestMethod]
    public void OmitsImplicitXsdStringDatatype()
    {
        string line = CanonicalizeLiteral("value", XsdString);

        Assert.DoesNotContain("^^", line, StringComparison.Ordinal);
    }

    [TestMethod]
    public void KeepsExplicitNonStringDatatype()
    {
        string line = CanonicalizeLiteral("42", "http://www.w3.org/2001/XMLSchema#integer");

        Assert.Contains("^^<http://www.w3.org/2001/XMLSchema#integer>", line, StringComparison.Ordinal);
    }

    [TestMethod]
    public void LowercasesLanguageTag()
    {
        using Utf8StringPool pool = new();
        Quad quad = new(
            new NamedNode(pool.Intern("http://example.org/s")),
            new NamedNode(pool.Intern("http://example.org/p")),
            new Literal(
                pool.Intern("chat"),
                new NamedNode(pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString")),
                pool.Intern("EN-GB")));

        string line = RdfCanonicalizer.Canonicalize([quad], Sha256);

        Assert.Contains("@en-gb", line, StringComparison.Ordinal);
    }

    [TestMethod]
    public void AppendsBaseDirectionAfterLanguageTag()
    {
        using Utf8StringPool pool = new();
        Quad quad = new(
            new NamedNode(pool.Intern("http://example.org/s")),
            new NamedNode(pool.Intern("http://example.org/p")),
            new Literal(
                pool.Intern("chat"),
                new NamedNode(pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#dirLangString")),
                pool.Intern("en"),
                TextDirection.Ltr));

        string line = RdfCanonicalizer.Canonicalize([quad], Sha256);

        Assert.Contains("@en--ltr", line, StringComparison.Ordinal);
    }

    [TestMethod]
    public void SerializesTripleTermInCanonicalForm()
    {
        using Utf8StringPool pool = new();
        TripleTerm inner = new(
            new NamedNode(pool.Intern("http://example.org/s1")),
            new NamedNode(pool.Intern("http://example.org/p1")),
            new NamedNode(pool.Intern("http://example.org/o1")));
        Quad quad = new(
            new NamedNode(pool.Intern("http://example.org/s")),
            new NamedNode(pool.Intern("http://example.org/p")),
            inner);

        string line = RdfCanonicalizer.Canonicalize([quad], Sha256);

        Assert.Contains(
            "<<( <http://example.org/s1> <http://example.org/p1> <http://example.org/o1> )>>",
            line,
            StringComparison.Ordinal);
    }

    [TestMethod]
    public void EmitsLineFeedTerminatorWithoutCarriageReturn()
    {
        string line = CanonicalizeLiteral("value", XsdString);

        Assert.DoesNotContain('\r', line);
        Assert.IsTrue(line.EndsWith(" .\n", StringComparison.Ordinal), line);
    }

    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    private static string CanonicalizeLiteral(string value, string datatypeIri)
    {
        using Utf8StringPool pool = new();
        Quad quad = new(
            new NamedNode(pool.Intern("http://example.org/s")),
            new NamedNode(pool.Intern("http://example.org/p")),
            new Literal(pool.Intern(value), new NamedNode(pool.Intern(datatypeIri))));

        return RdfCanonicalizer.Canonicalize([quad], Sha256);
    }
}
