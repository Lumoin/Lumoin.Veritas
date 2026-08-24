using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Xml;

/// <summary>
/// Verifies that <see cref="RdfXmlReader"/> bounds RDF 1.2 quoted-triple (<c>rdf:parseType="Triple"</c>)
/// nesting at <see cref="QuotedTripleLimits.MaxNestingDepth"/>: a term nested to the limit reads cleanly,
/// and a deeper one records a recoverable diagnostic — the value-based reader reports rather than throwing —
/// instead of growing the pending chain without bound.
/// </summary>
[TestClass]
internal sealed class RdfXmlReaderTripleTermCapTests
{
    /// <summary>A quoted triple nested exactly to the cap reads without a diagnostic and yields its triple.</summary>
    [TestMethod]
    public void TripleTermNestedToTheLimitReadsClean()
    {
        DiagnosticBag diagnostics = new();
        List<Quad> quads = [.. RdfXmlReader.Read(Encoding.UTF8.GetBytes(NestedParseTypeTripleXml(QuotedTripleLimits.MaxNestingDepth)), diagnostics, baseIri: default)];

        Assert.IsFalse(diagnostics.HasErrors, "a quoted triple nested to the limit must read without a diagnostic");
        Assert.IsNotEmpty(quads);
    }

    /// <summary>A quoted triple nested beyond the cap records a recoverable diagnostic instead of overflowing.</summary>
    [TestMethod]
    public void TripleTermNestedBeyondTheLimitRecordsADiagnostic()
    {
        DiagnosticBag diagnostics = new();
        _ = new List<Quad>(RdfXmlReader.Read(Encoding.UTF8.GetBytes(NestedParseTypeTripleXml(QuotedTripleLimits.MaxNestingDepth + 1)), diagnostics, baseIri: default));

        Assert.IsTrue(diagnostics.HasErrors, "a quoted triple nested beyond the limit must record a diagnostic and recover");
    }

    /// <summary>Builds an RDF/XML document with one quoted triple (<c>rdf:parseType="Triple"</c>) nested <paramref name="depth"/> levels through the object position.</summary>
    /// <param name="depth">The quoted-triple nesting depth.</param>
    /// <returns>The RDF/XML text.</returns>
    private static string NestedParseTypeTripleXml(int depth)
    {
        StringBuilder builder = new();
        builder.Append("<?xml version=\"1.0\"?><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:ex=\"http://e/\" rdf:version=\"1.2\"><rdf:Description rdf:about=\"http://e/top\">");
        for(int i = 0; i < depth; i++)
        {
            builder.Append("<ex:p rdf:parseType=\"Triple\"><rdf:Description rdf:about=\"http://e/s\">");
        }

        builder.Append("<ex:p rdf:resource=\"http://e/o\"/>");
        for(int i = 0; i < depth; i++)
        {
            builder.Append("</rdf:Description></ex:p>");
        }

        builder.Append("</rdf:Description></rdf:RDF>");

        return builder.ToString();
    }
}
