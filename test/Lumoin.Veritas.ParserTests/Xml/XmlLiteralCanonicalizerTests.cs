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

    /// <summary>The default (document-order) strategy hoists the namespaces in source declaration order.</summary>
    [TestMethod]
    public void DocumentOrderHoistsNamespacesInSourceOrder()
    {
        Assert.AreEqual(
            "<br xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:eg=\"http://example.org/\"></br>",
            ReadXmlLiteral(canonicalizer: null));
    }

    /// <summary>Selecting the <see cref="XmlLiteralCanonicalizers.Canonical"/> strategy sorts the namespaces lexicographically by prefix.</summary>
    [TestMethod]
    public void CanonicalSortsNamespacesByPrefix()
    {
        Assert.AreEqual(
            "<br xmlns:eg=\"http://example.org/\" xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"></br>",
            ReadXmlLiteral(XmlLiteralCanonicalizers.Canonical));
    }

    /// <summary>Reads the sole <c>rdf:XMLLiteral</c> object from <see cref="Document"/> using the given canonicalizer.</summary>
    /// <param name="canonicalizer">The canonicalizer to use, or <see langword="null"/> for the reader default.</param>
    /// <returns>The XML literal's lexical value.</returns>
    private static string ReadXmlLiteral(XmlLiteralCanonicalizer? canonicalizer)
    {
        DiagnosticBag diagnostics = new();
        IReadOnlyList<Quad> quads = RdfXmlReader.Read(System.Text.Encoding.UTF8.GetBytes(Document), diagnostics, baseIri: null, canonicalizer);

        Assert.IsFalse(diagnostics.HasErrors, "The document should parse without diagnostics.");
        Literal literal = quads.Select(quad => quad.Object).OfType<Literal>().Single();

        return literal.Value.ToString();
    }
}
