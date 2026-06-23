using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Xml;

/// <summary>
/// Verifies behaviours of the byte-native <see cref="RdfXmlReader"/> that the W3C RDF/XML conformance suite does not
/// exercise: DOCTYPE internal-subset entity abbreviation of IRIs (the idiom the OWL corpus relies on), and the
/// containment of an XML literal whose content references a DOCTYPE entity (which the detached canonicalization wrapper
/// cannot expand) so that one such literal does not discard the whole document.
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
        List<Quad> quads = [.. RdfXmlReader.Read(Encoding.UTF8.GetBytes(xml), diagnostics, baseIri: null)];

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
        List<Quad> quads = [.. RdfXmlReader.Read(Encoding.UTF8.GetBytes(xml), diagnostics, baseIri: null)];

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
}
