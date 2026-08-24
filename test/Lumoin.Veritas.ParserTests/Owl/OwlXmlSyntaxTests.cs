using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Functional;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Owl.Xml;
using Lumoin.Veritas.ParserTests.Conformance;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The OWL/XML front-end reads and writes the OWL 2 XML serialization against
/// the same structural model as the other OWL syntaxes. The corpus sweep is the
/// breadth gate: every functional-syntax ontology of the conformance corpus,
/// rendered to OWL/XML and read back, reproduces its structural content — checked
/// through the functional writer's canonical rendering, which is independent of
/// the synthetic origin each front-end stamps. The focused tests cover the
/// OWL/XML surface features the writer does not itself emit: abbreviated IRIs,
/// internal-subset entities, comments, and the child-element IRI form.
/// </summary>
[TestClass]
internal sealed class OwlXmlSyntaxTests
{
    [TestMethod]
    public void OwlXmlRoundTripsTheStructuralContentOfTheCorpus()
    {
        int documents = 0;
        foreach(string status in (string[])["approved", "proposed"])
        {
            foreach(Owl2TestCase testCase in Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", status, "all.rdf")))
            {
                foreach(string? text in (string?[])[testCase.FunctionalPremise, testCase.FunctionalConclusion, testCase.FunctionalNonConclusion])
                {
                    if(text is null)
                    {
                        continue;
                    }

                    OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(text);
                    if(document.Diagnostics.HasErrors)
                    {
                        continue;
                    }

                    string canonical = OwlFunctionalSyntaxWriter.Write(document);
                    string xml = OwlXmlSyntaxWriter.Write(document);
                    OwlOntologyDocument reread = OwlXmlSyntaxReader.Read(xml);

                    Assert.IsFalse(reread.Diagnostics.HasErrors, $"{testCase.Identifier}: the OWL/XML rendering did not read back cleanly.\n{xml}");
                    Assert.AreEqual(canonical, OwlFunctionalSyntaxWriter.Write(reread), $"{testCase.Identifier}: the OWL/XML round-trip changed the structural content.\n{xml}");
                    documents++;
                }
            }
        }

        Assert.IsGreaterThan(50, documents, "The corpus sweep should cover the functional-syntax documents.");
    }

    [TestMethod]
    public void AbbreviatedIrisResolveThroughPrefixDeclarations()
    {
        const string Document = """
            <?xml version="1.0"?>
            <Ontology xmlns="http://www.w3.org/2002/07/owl#" ontologyIRI="http://example.org/o">
              <Prefix name="" IRI="http://example.org/"/>
              <Declaration><Class abbreviatedIRI=":A"/></Declaration>
              <Declaration><Class abbreviatedIRI=":B"/></Declaration>
              <SubClassOf><Class abbreviatedIRI=":A"/><Class abbreviatedIRI=":B"/></SubClassOf>
            </Ontology>
            """;

        OwlOntologyDocument document = OwlXmlSyntaxReader.Read(Document);

        Assert.IsFalse(document.Diagnostics.HasErrors);
        Assert.HasCount(3, document.Axioms);
        OwlSubClassOfAxiom subClass = Find<OwlSubClassOfAxiom>(document)!;
        Assert.AreEqual("http://example.org/A", ClassIri(subClass.SubClass));
        Assert.AreEqual("http://example.org/B", ClassIri(subClass.SuperClass));
    }

    [TestMethod]
    public void InternalSubsetEntitiesExpandInAttributeValues()
    {
        const string Document = """
            <?xml version="1.0"?>
            <!DOCTYPE Ontology [ <!ENTITY ex "http://example.org/"> ]>
            <Ontology xmlns="http://www.w3.org/2002/07/owl#">
              <Declaration><Class IRI="&ex;A"/></Declaration>
            </Ontology>
            """;

        OwlOntologyDocument document = OwlXmlSyntaxReader.Read(Document);

        Assert.IsFalse(document.Diagnostics.HasErrors);
        OwlDeclarationAxiom declaration = Find<OwlDeclarationAxiom>(document)!;
        Assert.AreEqual("http://example.org/A", declaration.Entity.Iri.ToString());
    }

    /// <summary>A billion-laughs entity-amplification document is bounded by the expansion budget — it reads to completion rather than exhausting memory.</summary>
    [TestMethod]
    public void EntityAmplificationIsBoundedByTheExpansionBudget()
    {
        StringBuilder builder = new("<?xml version=\"1.0\"?><!DOCTYPE Ontology [<!ENTITY a \"");
        builder.Append('x', 64).Append("\">");
        for(char level = 'b'; level <= 'i'; level++)
        {
            builder.Append("<!ENTITY ").Append(level).Append(" \"");
            for(int reference = 0; reference < 10; reference++)
            {
                builder.Append('&').Append((char)(level - 1)).Append(';');
            }

            builder.Append("\">");
        }

        //Unbounded, &i; would expand to 64 * 10^8 = 6.4 GB; the budget caps the total expansion far below that, so the
        //read completes instead of exhausting memory (without the budget this document hangs / throws OutOfMemory).
        builder.Append("]><Ontology xmlns=\"http://www.w3.org/2002/07/owl#\"><x>&i;</x></Ontology>");

        OwlOntologyDocument document = OwlXmlSyntaxReader.Read(builder.ToString());

        Assert.IsNotNull(document);
    }

    [TestMethod]
    public void CommentsAndChildElementIrisReadInAnnotationAssertions()
    {
        const string Document = """
            <?xml version="1.0"?>
            <!-- the document comment -->
            <Ontology xmlns="http://www.w3.org/2002/07/owl#">
              <AnnotationAssertion>
                <AnnotationProperty IRI="http://www.w3.org/2000/01/rdf-schema#label"/>
                <IRI>http://example.org/A</IRI>
                <Literal datatypeIRI="http://www.w3.org/2001/XMLSchema#string">hello</Literal>
              </AnnotationAssertion>
            </Ontology>
            """;

        OwlOntologyDocument document = OwlXmlSyntaxReader.Read(Document);

        Assert.IsFalse(document.Diagnostics.HasErrors);
        OwlAnnotationAssertionAxiom assertion = Find<OwlAnnotationAssertionAxiom>(document)!;
        Assert.AreEqual("http://example.org/A", (assertion.Subject as NamedNode)?.Iri.ToString());
        Assert.AreEqual("hello", (assertion.Value as Literal)?.Value.ToString());
    }

    [TestMethod]
    public void QualifiedCardinalityAndFacetsAndNestedAnnotationsRoundTrip()
    {
        const string Document = """
            Prefix( : = <http://example.org/> )
            Prefix( xsd: = <http://www.w3.org/2001/XMLSchema#> )
            Prefix( rdfs: = <http://www.w3.org/2000/01/rdf-schema#> )
            Ontology( <http://example.org/o>
              Declaration( Class( :A ) )
              Declaration( ObjectProperty( :p ) )
              Declaration( DataProperty( :dp ) )
              SubClassOf( :A ObjectMinCardinality( 2 :p :A ) )
              SubClassOf( Annotation( Annotation( rdfs:label "meta" ) rdfs:comment "stated" ) :A DataAllValuesFrom( :dp DatatypeRestriction( xsd:integer xsd:minInclusive "0"^^xsd:integer ) ) )
            )
            """;

        OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(Document);
        Assert.IsFalse(document.Diagnostics.HasErrors);

        string xml = OwlXmlSyntaxWriter.Write(document);
        OwlOntologyDocument reread = OwlXmlSyntaxReader.Read(xml);

        Assert.IsFalse(reread.Diagnostics.HasErrors, xml);
        Assert.AreEqual(OwlFunctionalSyntaxWriter.Write(document), OwlFunctionalSyntaxWriter.Write(reread), xml);
    }

    [TestMethod]
    public void TheReaderFeedsIncrementallyAcrossChunkBoundaries()
    {
        const string Document = """
            <Ontology xmlns="http://www.w3.org/2002/07/owl#"><Declaration><Class IRI="http://example.org/A"/></Declaration></Ontology>
            """;
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(Document);

        OwlXmlSyntaxIncrementalReader reader = new();
        foreach(byte b in bytes)
        {
            reader.Feed([b]);
        }

        OwlOntologyDocument document = reader.Complete();

        Assert.IsFalse(document.Diagnostics.HasErrors);
        Assert.HasCount(1, document.Axioms);
        Assert.AreEqual("http://example.org/A", Find<OwlDeclarationAxiom>(document)!.Entity.Iri.ToString());
    }

    [TestMethod]
    public void HasKeyObjectAndDataKeysRoundTrip()
    {
        NamedNode keyedClass = new(Utf8Strings.From("http://example.org/A"));
        NamedNode objectKey = new(Utf8Strings.From("http://example.org/p"));
        NamedNode dataKey = new(Utf8Strings.From("http://example.org/dp"));
        Quad origin = new(keyedClass, keyedClass, keyedClass, Graph: null);
        OwlHasKeyAxiom key = new(new OwlClassReference(keyedClass), [new OwlObjectPropertyReference(objectKey)], [dataKey]) { Origin = origin };
        OwlOntologyDocument document = new(
            [key],
            null,
            new DiagnosticBag(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>(),
            new HashSet<Utf8String>());

        string xml = OwlXmlSyntaxWriter.Write(document);
        OwlOntologyDocument reread = OwlXmlSyntaxReader.Read(xml);

        Assert.IsFalse(reread.Diagnostics.HasErrors, xml);
        OwlHasKeyAxiom rekeyed = Find<OwlHasKeyAxiom>(reread)!;
        Assert.HasCount(1, rekeyed.ObjectProperties);
        Assert.HasCount(1, rekeyed.DataProperties);
        Assert.AreEqual("http://example.org/p", rekeyed.ObjectProperties[0].Property.Iri.ToString());
        Assert.AreEqual("http://example.org/dp", rekeyed.DataProperties[0].Iri.ToString());
    }

    /// <summary>The IRI of a named-class expression, for assertions.</summary>
    /// <param name="expression">The class expression.</param>
    /// <returns>The class IRI, or an empty string when the expression is not a named class.</returns>
    private static string ClassIri(OwlClassExpression expression)
    {
        return expression is OwlClassReference reference ? reference.Class.Iri.ToString() : string.Empty;
    }

    /// <summary>The first axiom of a kind in a document, or <see langword="null"/> when none.</summary>
    /// <typeparam name="T">The axiom kind.</typeparam>
    /// <param name="document">The document to search.</param>
    /// <returns>The first matching axiom, or <see langword="null"/>.</returns>
    private static T? Find<T>(OwlOntologyDocument document)
        where T : OwlAxiom
    {
        foreach(OwlAxiom axiom in document.Axioms)
        {
            if(axiom is T match)
            {
                return match;
            }
        }

        return null;
    }
}
