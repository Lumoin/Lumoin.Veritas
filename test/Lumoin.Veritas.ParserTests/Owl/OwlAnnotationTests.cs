using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Functional;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Axiom annotations survive both front-ends: the functional-syntax
/// axiom-frame annotations and the RDF <c>owl:Axiom</c> reification blocks
/// attach to the axiom records, nesting included. The forward mapping is
/// pinned here too, both for the annotation round trip and for its
/// mint-collision resolution — a writer-minted expression node never takes
/// the label of an input anonymous individual.
/// </summary>
[TestClass]
internal sealed class OwlAnnotationTests
{
    /// <summary>The <c>rdf:type</c> predicate IRI.</summary>
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>The <c>rdfs:subClassOf</c> predicate IRI.</summary>
    private const string RdfsSubClassOf = "http://www.w3.org/2000/01/rdf-schema#subClassOf";

    /// <summary>The <c>owl:onProperty</c> predicate IRI — the marker of a serialised restriction node.</summary>
    private const string OwlOnProperty = "http://www.w3.org/2002/07/owl#onProperty";

    /// <summary>The ontology header node the mint-collision document declares; a named header keeps the writer's blank counter for the expression structure alone.</summary>
    private static NamedNode Ontology { get; } = new(Utf8Strings.From("http://example.org/o"));

    /// <summary>The class the mint-collision document asserts of its individual and subsumes under the restriction.</summary>
    private static NamedNode AssertedClass { get; } = new(Utf8Strings.From("http://example.org/A"));

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

    [TestMethod]
    public void AnnotationsRoundTripThroughTheForwardMapping()
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

        OwlOntologyDocument read = OwlFunctionalSyntaxReader.Read(Document);
        Assert.IsFalse(read.Diagnostics.HasErrors);

        //Forward to triples and back: the owl:Axiom block the emitter wrote
        //reattaches through the reverse mapping, nesting included.
        List<Quad> quads = OwlStructuralToRdf.ToQuads(read);
        OwlOntologyDocument mapped = OwlRdfMapper.Map(quads);

        Assert.IsFalse(mapped.Diagnostics.HasErrors);
        OwlSubClassOfAxiom? subClass = FindSubClassOf(mapped);
        Assert.IsNotNull(subClass);
        Assert.HasCount(1, subClass.Annotations);
        Assert.AreEqual("stated", ((Literal)subClass.Annotations[0].Value).Value.ToString());
        Assert.HasCount(1, subClass.Annotations[0].Annotations);
        Assert.AreEqual("meta", ((Literal)subClass.Annotations[0].Annotations[0].Value).Value.ToString());
    }

    /// <summary>
    /// The forward mapping keeps a writer-minted expression node apart from an input anonymous individual that
    /// already occupies the mint counter's first label: the individual's label survives verbatim on its class
    /// assertion, while the restriction node the <c>SubClassOf</c> serialisation mints is relabelled, so the
    /// mapped graph never conflates the two.
    /// </summary>
    [TestMethod]
    public void ForwardMappingKeepsAMintedNodeApartFromACollidingAnonymousIndividual()
    {
        BlankNode individual = new(Utf8Strings.From("owlmap0"));

        //The control fixes that the label really is the one the writer would mint: with a named individual in
        //the same document the restriction node lands on exactly that label, so the anonymous row below is a
        //genuine collision rather than an accidental miss.
        List<Quad> control = OwlStructuralToRdf.ToQuads(MintCollisionDocument(new NamedNode(Utf8Strings.From("http://example.org/i"))));
        BlankNode? controlRestriction = FindRestrictionNode(control);
        Assert.IsNotNull(controlRestriction, "The control document's SubClassOf carries a writer-minted restriction node.");
        Assert.AreEqual(individual.Label.ToString(), controlRestriction.Label.ToString(), "Absent an input anonymous individual the restriction node mints at exactly the contested label.");

        List<Quad> quads = OwlStructuralToRdf.ToQuads(MintCollisionDocument(individual));

        BlankNode? restriction = FindRestrictionNode(quads);
        Assert.IsNotNull(restriction, "The mapped SubClassOf carries a writer-minted restriction node.");
        Assert.AreNotEqual(individual.Label.ToString(), restriction.Label.ToString(), "The writer-minted restriction node never takes the input anonymous individual's label.");
        Assert.IsTrue(ContainsTriple(quads, individual, RdfType, AssertedClass), "The input anonymous individual keeps its label verbatim on its class assertion.");
        Assert.IsTrue(ContainsTriple(quads, AssertedClass, RdfsSubClassOf, restriction), "The subsumption points at the relabelled minted restriction node, not at the input individual.");
    }

    /// <summary>
    /// The mint-collision document: a class assertion over the given individual, plus a <c>SubClassOf</c> whose
    /// superclass is an <c>ObjectSomeValuesFrom</c> — a class expression the forward mapping serialises onto a
    /// freshly minted blank node.
    /// </summary>
    /// <param name="individual">The asserted individual: an anonymous node contesting the mint label, or a named control.</param>
    /// <returns>The structural document.</returns>
    private static OwlOntologyDocument MintCollisionDocument(RdfTerm individual)
    {
        Quad origin = new(Ontology, new NamedNode(Utf8Strings.From(RdfType)), Ontology, Graph: null);
        NamedNode filler = new(Utf8Strings.From("http://example.org/B"));
        NamedNode property = new(Utf8Strings.From("http://example.org/p"));

        return new OwlOntologyDocument(
            [
                new OwlClassAssertionAxiom(new OwlClassReference(AssertedClass), individual) { Origin = origin },
                new OwlSubClassOfAxiom(
                    new OwlClassReference(AssertedClass),
                    new OwlObjectSomeValuesFrom(new OwlObjectPropertyReference(property), new OwlClassReference(filler)))
                {
                    Origin = origin,
                },
            ],
            Ontology,
            new DiagnosticBag(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>());
    }

    /// <summary>The blank node a serialised restriction sits on — the subject of the mapped <c>owl:onProperty</c> triple.</summary>
    /// <param name="quads">The mapped quads.</param>
    /// <returns>The restriction node, or <see langword="null"/> when the mapping emitted none.</returns>
    private static BlankNode? FindRestrictionNode(List<Quad> quads)
    {
        foreach(Quad quad in quads)
        {
            if(quad.Predicate.Iri.ToString() == OwlOnProperty && quad.Subject is BlankNode node)
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>Whether the mapped quads carry a triple with the given subject term, predicate IRI, and object term.</summary>
    /// <param name="quads">The mapped quads.</param>
    /// <param name="subject">The subject term.</param>
    /// <param name="predicate">The predicate IRI.</param>
    /// <param name="object">The object term.</param>
    /// <returns><see langword="true"/> when a matching triple is present.</returns>
    private static bool ContainsTriple(List<Quad> quads, RdfTerm subject, string predicate, RdfTerm @object)
    {
        foreach(Quad quad in quads)
        {
            if(quad.Subject.Equals(subject) && quad.Predicate.Iri.ToString() == predicate && quad.Object.Equals(@object))
            {
                return true;
            }
        }

        return false;
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
        List<Quad> quads = [.. RdfXmlReader.Read(Encoding.UTF8.GetBytes(document), diagnostics, baseIri: Utf8Strings.From("http://example.org/"))];
        Assert.IsFalse(diagnostics.HasErrors);

        return OwlRdfMapper.Map(quads);
    }
}
