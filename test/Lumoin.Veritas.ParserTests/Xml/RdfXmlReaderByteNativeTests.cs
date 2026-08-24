using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Xml;

/// <summary>
/// Verifies behaviours of the byte-native <see cref="RdfXmlReader"/> that the W3C RDF/XML conformance suite does not
/// exercise: DOCTYPE internal-subset entity abbreviation of IRIs (the idiom the OWL corpus relies on), the
/// containment of an XML literal whose content references a DOCTYPE entity (which the detached canonicalization wrapper
/// cannot expand) so that one such literal does not discard the whole document, and the <c>xml:base</c> scoping edges —
/// verbatim adoption of an inner base and the present-but-empty override — that the corpus never touches.
/// </summary>
[TestClass]
internal sealed class RdfXmlReaderByteNativeTests
{
    private const string RdfXmlns = "xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"";

    /// <summary>A DOCTYPE internal-subset general entity expands inside namespace declarations and attribute values.</summary>
    [TestMethod]
    public void DoctypeEntityAbbreviatesIris()
    {
        string xml = "<?xml version=\"1.0\"?><!DOCTYPE rdf:RDF [ <!ENTITY ex \"http://e/\"> ]>" +
            "<rdf:RDF " + RdfXmlns + " xmlns:p=\"&ex;p#\">" +
            "<rdf:Description rdf:about=\"&ex;thing\"><p:name>x</p:name></rdf:Description></rdf:RDF>";

        DiagnosticBag diagnostics = new();
        List<Quad> quads = [.. RdfXmlReader.Read(Encoding.UTF8.GetBytes(xml), diagnostics, baseIri: default)];

        Assert.IsFalse(diagnostics.HasErrors, "a DOCTYPE-entity-abbreviated document must read cleanly");
        Assert.HasCount(1, quads);
        Assert.IsTrue(quads[0].Subject is NamedNode { Iri: var subject } && subject.ToString() == "http://e/thing", "rdf:about=\"&ex;thing\" must expand to http://e/thing");
        Assert.IsTrue(quads[0].Predicate is NamedNode { Iri: var predicate } && predicate.ToString() == "http://e/p#name", "the &ex;-abbreviated namespace must expand in the predicate");
    }

    /// <summary>An XML literal whose content references a DOCTYPE entity records a diagnostic and falls back, without discarding the document's other statements.</summary>
    [TestMethod]
    public void ParseTypeLiteralWithDoctypeEntityDoesNotAbortDocument()
    {
        string xml = "<?xml version=\"1.0\"?><!DOCTYPE rdf:RDF [ <!ENTITY foo \"bar\"> ]>" +
            "<rdf:RDF " + RdfXmlns + " xmlns:ex=\"http://e/\">" +
            "<rdf:Description rdf:about=\"http://e/s\">" +
            "<ex:plain>ok</ex:plain>" +
            "<ex:lit rdf:parseType=\"Literal\"><x>&foo;</x></ex:lit>" +
            "</rdf:Description></rdf:RDF>";

        DiagnosticBag diagnostics = new();
        List<Quad> quads = [.. RdfXmlReader.Read(Encoding.UTF8.GetBytes(xml), diagnostics, baseIri: default)];

        Assert.IsTrue(diagnostics.HasErrors, "the unexpandable literal entity must record a diagnostic");
        Assert.HasCount(2, quads);

        bool plainSurvives = false;
        foreach(Quad quad in quads)
        {
            if(quad.Predicate is NamedNode { Iri: var predicate } && predicate.ToString() == "http://e/plain")
            {
                plainSurvives = true;
            }
        }

        Assert.IsTrue(plainSurvives, "the ex:plain statement must survive the unprocessable literal rather than the whole document being discarded");
    }

    /// <summary>An element's <c>xml:base</c> is adopted verbatim: an absolute inner base replaces the inherited one whole, and a relative inner base is never resolved against it, so references beneath a relative base stay unresolved.</summary>
    [TestMethod]
    public void NestedXmlBaseAdoptsVerbatim()
    {
        string xml = "<rdf:RDF " + RdfXmlns + " xmlns:ex=\"http://e/\">" +
            "<rdf:Description rdf:about=\"x\" xml:base=\"http://inner.example/o/\"><ex:p>v</ex:p></rdf:Description>" +
            "<rdf:Description rdf:about=\"y\" xml:base=\"rel/\"><ex:p>v</ex:p></rdf:Description>" +
            "</rdf:RDF>";

        DiagnosticBag diagnostics = new();
        List<Quad> quads = [.. RdfXmlReader.Read(Encoding.UTF8.GetBytes(xml), diagnostics, Utf8Strings.From("http://h/d/"))];

        Assert.IsFalse(diagnostics.HasErrors, "the base-scoped document must read cleanly");
        Assert.HasCount(2, quads);
        Assert.IsTrue(quads[0].Subject is NamedNode { Iri: var absolute } && absolute.ToString() == "http://inner.example/o/x", "an absolute inner xml:base replaces the inherited base whole");
        Assert.IsTrue(quads[1].Subject is NamedNode { Iri: var relative } && relative.ToString() == "y", "a relative inner xml:base is adopted verbatim (never resolved against the outer base), so the reference beneath it stays unresolved");
    }

    /// <summary>A present-but-empty <c>xml:base</c> overrides the inherited base — distinct from an absent one, which inherits.</summary>
    [TestMethod]
    public void EmptyXmlBaseOverridesTheInheritedBase()
    {
        string xml = "<rdf:RDF " + RdfXmlns + " xmlns:ex=\"http://e/\">" +
            "<rdf:Description rdf:about=\"a\"><ex:p>v</ex:p></rdf:Description>" +
            "<rdf:Description rdf:about=\"z\" xml:base=\"\"><ex:p>v</ex:p></rdf:Description>" +
            "</rdf:RDF>";

        DiagnosticBag diagnostics = new();
        List<Quad> quads = [.. RdfXmlReader.Read(Encoding.UTF8.GetBytes(xml), diagnostics, Utf8Strings.From("http://h/d/"))];

        Assert.IsFalse(diagnostics.HasErrors, "the base-scoped document must read cleanly");
        Assert.HasCount(2, quads);
        Assert.IsTrue(quads[0].Subject is NamedNode { Iri: var inherited } && inherited.ToString() == "http://h/d/a", "an element with no xml:base inherits the document base");
        Assert.IsTrue(quads[1].Subject is NamedNode { Iri: var overridden } && overridden.ToString() == "z", "an empty xml:base overrides the inherited base rather than inheriting it");
    }

    /// <summary>A located grammar violation records its diagnostic at the span of the offending element, not the empty default span.</summary>
    [TestMethod]
    public void LocatedDiagnosticCarriesTheOffendingElementSpan()
    {
        string xml = "<rdf:RDF " + RdfXmlns + "><rdf:Description rdf:about=\"http://e/s\" rdf:nodeID=\"n\"/></rdf:RDF>";

        DiagnosticBag diagnostics = new();
        _ = RdfXmlReader.Read(Encoding.UTF8.GetBytes(xml), diagnostics, baseIri: default);

        Assert.IsTrue(diagnostics.HasErrors, "a node element carrying both rdf:about and rdf:nodeID must record a diagnostic");

        Diagnostic diagnostic = diagnostics.Diagnostics[0];
        Assert.IsGreaterThan(0, diagnostic.Span.StartByte, "the diagnostic locates the offending element, not the document start.");
        Assert.IsGreaterThan(diagnostic.Span.StartByte, diagnostic.Span.EndByte, "the diagnostic span covers the offending element.");
    }
}
