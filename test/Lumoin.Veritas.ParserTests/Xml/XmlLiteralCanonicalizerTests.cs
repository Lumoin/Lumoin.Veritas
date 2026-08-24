using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Xml;

/// <summary>
/// Verifies the <see cref="XmlLiteralCanonicalizer"/> seam on <see cref="RdfXmlReader"/>: an
/// <c>rdf:parseType="Literal"</c> object is canonicalized by the selected strategy, the two well-known strategies
/// differing only in the order of the hoisted namespace declarations.
/// </summary>
[TestClass]
internal sealed class XmlLiteralCanonicalizerTests
{
    //An empty <br/> whose only in-scope namespaces (rdf, then eg) are declared on the document element, so both
    //strategies hoist both namespaces onto the apex and expand the empty element — they differ only in ordering.
    private const string Document =
        "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:eg=\"http://example.org/\">" +
        "<rdf:Description rdf:about=\"http://www.example.org/a\">" +
        "<eg:prop rdf:parseType=\"Literal\"><br /></eg:prop>" +
        "</rdf:Description></rdf:RDF>";

    //A literal whose content interleaves text and a child element, to prove the byte-native canonicalizer preserves
    //mixed-content order (it does not coalesce the text runs around the child).
    private const string MixedContentDocument =
        "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:eg=\"http://example.org/\">" +
        "<rdf:Description rdf:about=\"http://www.example.org/a\">" +
        "<eg:prop rdf:parseType=\"Literal\"><eg:p>before<eg:b>x</eg:b>after</eg:p></eg:prop>" +
        "</rdf:Description></rdf:RDF>";

    /// <summary>The default (document-order) strategy hoists the namespaces in source declaration order.</summary>
    [TestMethod]
    public void DocumentOrderHoistsNamespacesInSourceOrder()
    {
        Assert.AreEqual(
            "<br xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:eg=\"http://example.org/\"></br>",
            ReadXmlLiteral(Document, canonicalizer: null));
    }

    /// <summary>Selecting the <see cref="XmlLiteralCanonicalizers.Canonical"/> strategy sorts the namespaces lexicographically by prefix.</summary>
    [TestMethod]
    public void CanonicalSortsNamespacesByPrefix()
    {
        Assert.AreEqual(
            "<br xmlns:eg=\"http://example.org/\" xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"></br>",
            ReadXmlLiteral(Document, XmlLiteralCanonicalizers.Canonical));
    }

    /// <summary>Mixed content (text interleaved with a child element) is serialized in document order, with the apex element hoisting the in-scope namespaces — the text runs around the child are not coalesced.</summary>
    [TestMethod]
    public void DocumentOrderPreservesMixedContentInterleaving()
    {
        Assert.AreEqual(
            "<eg:p xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:eg=\"http://example.org/\">before<eg:b>x</eg:b>after</eg:p>",
            ReadXmlLiteral(MixedContentDocument, canonicalizer: null));
    }

    /// <summary>A descendant that undeclares the default namespace (<c>xmlns=""</c>) emits no declaration when no ancestor declared a non-empty default — the empty undeclaration carries no information (Canonical XML 1.0 §2.3).</summary>
    [TestMethod]
    public void DocumentOrderOmitsRedundantDefaultUndeclarationOnDescendant()
    {
        string document =
            "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:eg=\"http://example.org/\">" +
            "<rdf:Description rdf:about=\"http://x/a\"><eg:prop rdf:parseType=\"Literal\"><eg:a><b xmlns=\"\">text</b></eg:a></eg:prop></rdf:Description></rdf:RDF>";

        Assert.AreEqual(
            "<eg:a xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:eg=\"http://example.org/\"><b>text</b></eg:a>",
            ReadXmlLiteral(document, canonicalizer: null));
    }

    /// <summary>A descendant that undeclares the default namespace keeps its <c>xmlns=""</c> when it cancels an inherited non-empty default.</summary>
    [TestMethod]
    public void DocumentOrderKeepsDefaultUndeclarationThatCancelsAnInheritedDefault()
    {
        string document =
            "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
            "<rdf:Description rdf:about=\"http://x/s\"><rdf:value rdf:parseType=\"Literal\"><a xmlns=\"http://ex/\"><b xmlns=\"\">x</b></a></rdf:value></rdf:Description></rdf:RDF>";

        Assert.AreEqual(
            "<a xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns=\"http://ex/\"><b xmlns=\"\">x</b></a>",
            ReadXmlLiteral(document, canonicalizer: null));
    }

    /// <summary>An apex element that inherits an empty default (an ancestor <c>xmlns=""</c>) does not hoist it — there is no output ancestor with a non-empty default to undeclare.</summary>
    [TestMethod]
    public void DocumentOrderOmitsInheritedEmptyDefaultOnApex()
    {
        string document =
            "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns=\"http://outer/\">" +
            "<rdf:Description rdf:about=\"http://x/s\" xmlns=\"\"><rdf:value rdf:parseType=\"Literal\"><a>x</a></rdf:value></rdf:Description></rdf:RDF>";

        Assert.AreEqual(
            "<a xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">x</a>",
            ReadXmlLiteral(document, canonicalizer: null));
    }

    /// <summary>The strict Canonical strategy serializes the full content subtree (text and descendant elements), not just the apex element.</summary>
    [TestMethod]
    public void CanonicalPreservesDescendantContent()
    {
        string document =
            "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:eg=\"http://example.org/\">" +
            "<rdf:Description rdf:about=\"http://x/a\"><eg:prop rdf:parseType=\"Literal\"><eg:p>x</eg:p></eg:prop></rdf:Description></rdf:RDF>";

        Assert.AreEqual(
            "<eg:p xmlns:eg=\"http://example.org/\" xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">x</eg:p>",
            ReadXmlLiteral(document, XmlLiteralCanonicalizers.Canonical));
    }

    /// <summary>The strict Canonical strategy preserves mixed-content interleaving (text around a child element), with namespaces sorted by prefix.</summary>
    [TestMethod]
    public void CanonicalPreservesMixedContentInterleaving()
    {
        Assert.AreEqual(
            "<eg:p xmlns:eg=\"http://example.org/\" xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">before<eg:b>x</eg:b>after</eg:p>",
            ReadXmlLiteral(MixedContentDocument, XmlLiteralCanonicalizers.Canonical));
    }

    /// <summary>Reads the sole <c>rdf:XMLLiteral</c> object from a document using the given canonicalizer.</summary>
    /// <param name="document">The RDF/XML document carrying exactly one XML literal.</param>
    /// <param name="canonicalizer">The canonicalizer to use, or <see langword="null"/> for the reader default.</param>
    /// <returns>The XML literal's lexical value.</returns>
    private static string ReadXmlLiteral(string document, XmlLiteralCanonicalizer? canonicalizer)
    {
        DiagnosticBag diagnostics = new();
        IReadOnlyList<Quad> quads = RdfXmlReader.Read(System.Text.Encoding.UTF8.GetBytes(document), diagnostics, baseIri: default, canonicalizer);

        Assert.IsFalse(diagnostics.HasErrors, "The document should parse without diagnostics.");
        Literal literal = quads.Select(quad => quad.Object).OfType<Literal>().Single();

        return literal.Value.ToString();
    }
}
