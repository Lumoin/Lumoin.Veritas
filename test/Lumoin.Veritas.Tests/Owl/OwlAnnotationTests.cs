using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Functional;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// Axiom annotations survive both front-ends: the functional-syntax
/// axiom-frame annotations and the RDF <c>owl:Axiom</c> reification blocks
/// attach to the axiom records, nesting included.
/// </summary>
[TestClass]
internal sealed class OwlAnnotationTests
{
    [TestMethod]
    public void FunctionalAxiomFrameAnnotationAttachesToTheAxiom()
    {
        const string Document = """
            Prefix( : = <http://example.org/> )
            Prefix( rdfs: = <http://www.w3.org/2000/01/rdf-schema#> )
            Ontology( <http://example.org/o>
              Declaration( Class( :A ) )
              Declaration( Class( :B ) )
              SubClassOf( Annotation( rdfs:comment "stated in v2" ) :A :B )
            )
            """;

        OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(Document);

        Assert.IsFalse(document.Diagnostics.HasErrors);
        OwlSubClassOfAxiom? subClass = FindSubClassOf(document);
        Assert.IsNotNull(subClass);
        Assert.HasCount(1, subClass.Annotations);
        Assert.AreEqual("http://www.w3.org/2000/01/rdf-schema#comment", subClass.Annotations[0].Property.Iri.ToString());
        Assert.AreEqual("stated in v2", ((Literal)subClass.Annotations[0].Value).Value.ToString());
    }

    [TestMethod]
    public void FunctionalNestedAnnotationAttachesToTheAnnotation()
    {
        const string Document = """
            Prefix( : = <http://example.org/> )
            Prefix( rdfs: = <http://www.w3.org/2000/01/rdf-schema#> )
            Ontology( <http://example.org/o>
              Declaration( Class( :A ) )
              Declaration( Class( :B ) )
              SubClassOf( Annotation( Annotation( rdfs:label "meta" ) rdfs:comment "stated" ) :A :B )
            )
            """;

        OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(Document);

        Assert.IsFalse(document.Diagnostics.HasErrors);
        OwlSubClassOfAxiom? subClass = FindSubClassOf(document);
        Assert.IsNotNull(subClass);
        Assert.HasCount(1, subClass.Annotations);
        Assert.HasCount(1, subClass.Annotations[0].Annotations);
        Assert.AreEqual("meta", ((Literal)subClass.Annotations[0].Annotations[0].Value).Value.ToString());
    }

    [TestMethod]
    public void FunctionalOntologyAnnotationSurfacesAsAnnotationAssertion()
    {
        const string Document = """
            Prefix( rdfs: = <http://www.w3.org/2000/01/rdf-schema#> )
            Ontology( <http://example.org/o>
              Annotation( rdfs:comment "the ontology itself" )
            )
            """;

        OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(Document);

        Assert.IsFalse(document.Diagnostics.HasErrors);
        bool found = false;
        foreach(OwlAxiom axiom in document.Axioms)
        {
            if(axiom is OwlAnnotationAssertionAxiom { Subject: NamedNode subject } assertion
                && subject.Iri.ToString() == "http://example.org/o")
            {
                found = true;
                Assert.AreEqual("the ontology itself", ((Literal)assertion.Value).Value.ToString());
            }
        }

        Assert.IsTrue(found, "The ontology annotation should surface as an annotation assertion on the ontology IRI.");
    }

    [TestMethod]
    public void RdfAxiomReificationAttachesToTheAxiom()
    {
        const string Document = """
            <rdf:RDF
                xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"
                xmlns:rdfs="http://www.w3.org/2000/01/rdf-schema#"
                xmlns:owl="http://www.w3.org/2002/07/owl#"
                xml:base="http://example.org/" >
              <owl:Ontology rdf:about=""/>
              <owl:Class rdf:about="A">
                <rdfs:subClassOf rdf:resource="B"/>
              </owl:Class>
              <owl:Class rdf:about="B"/>
              <owl:Axiom>
                <owl:annotatedSource rdf:resource="A"/>
                <owl:annotatedProperty rdf:resource="http://www.w3.org/2000/01/rdf-schema#subClassOf"/>
                <owl:annotatedTarget rdf:resource="B"/>
                <rdfs:comment>stated in v2</rdfs:comment>
              </owl:Axiom>
            </rdf:RDF>
            """;

        OwlOntologyDocument document = MapRdfXml(Document);

        Assert.IsFalse(document.Diagnostics.HasErrors);
        OwlSubClassOfAxiom? subClass = FindSubClassOf(document);
        Assert.IsNotNull(subClass);
        Assert.HasCount(1, subClass.Annotations);
        Assert.AreEqual("stated in v2", ((Literal)subClass.Annotations[0].Value).Value.ToString());
    }

    [TestMethod]
    public void RdfNestedAnnotationReificationFoldsIntoTheParentAnnotation()
    {
        const string Document = """
            <rdf:RDF
                xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"
                xmlns:rdfs="http://www.w3.org/2000/01/rdf-schema#"
                xmlns:owl="http://www.w3.org/2002/07/owl#"
                xml:base="http://example.org/" >
              <owl:Ontology rdf:about=""/>
              <owl:Class rdf:about="A">
                <rdfs:subClassOf rdf:resource="B"/>
              </owl:Class>
              <owl:Class rdf:about="B"/>
              <owl:Axiom rdf:nodeID="ax">
                <owl:annotatedSource rdf:resource="A"/>
                <owl:annotatedProperty rdf:resource="http://www.w3.org/2000/01/rdf-schema#subClassOf"/>
                <owl:annotatedTarget rdf:resource="B"/>
                <rdfs:comment>stated</rdfs:comment>
              </owl:Axiom>
              <owl:Annotation>
                <owl:annotatedSource rdf:nodeID="ax"/>
                <owl:annotatedProperty rdf:resource="http://www.w3.org/2000/01/rdf-schema#comment"/>
                <owl:annotatedTarget>stated</owl:annotatedTarget>
                <rdfs:label>meta</rdfs:label>
              </owl:Annotation>
            </rdf:RDF>
            """;

        OwlOntologyDocument document = MapRdfXml(Document);

        Assert.IsFalse(document.Diagnostics.HasErrors);
        OwlSubClassOfAxiom? subClass = FindSubClassOf(document);
        Assert.IsNotNull(subClass);
        Assert.HasCount(1, subClass.Annotations);
        Assert.HasCount(1, subClass.Annotations[0].Annotations);
        Assert.AreEqual("meta", ((Literal)subClass.Annotations[0].Annotations[0].Value).Value.ToString());
    }

    private static OwlSubClassOfAxiom? FindSubClassOf(OwlOntologyDocument document)
    {
        foreach(OwlAxiom axiom in document.Axioms)
        {
            if(axiom is OwlSubClassOfAxiom subClass)
            {
                return subClass;
            }
        }

        return null;
    }

    private static OwlOntologyDocument MapRdfXml(string document)
    {
        DiagnosticBag diagnostics = new();
        List<Quad> quads = [.. RdfXmlReader.Read(Encoding.UTF8.GetBytes(document), diagnostics, baseIri: "http://example.org/")];
        Assert.IsFalse(diagnostics.HasErrors);

        return OwlRdfMapper.Map(quads);
    }
}
