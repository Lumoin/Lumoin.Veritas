using System.Collections.Immutable;
using Lumoin.Veritas.Owl.Functional;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.ParserTests.Conformance;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The functional-syntax writer completes the text round-trip: reading a
/// rendering reproduces the document, and rendering that is byte-identical
/// to the first rendering — the writer's output is its own fixed point,
/// checked over every functional-syntax document of the conformance corpus.
/// </summary>
[TestClass]
internal sealed class OwlFunctionalSyntaxWriterTests
{
    [TestMethod]
    public void WriterOutputIsItsOwnFixedPointOverTheCorpus()
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

                    string first = OwlFunctionalSyntaxWriter.Write(document);
                    OwlOntologyDocument reread = OwlFunctionalSyntaxReader.Read(first);

                    Assert.IsFalse(reread.Diagnostics.HasErrors, $"{testCase.Identifier}: the rendering did not read back cleanly.\n{first}");
                    Assert.HasCount(document.Axioms.Length, reread.Axioms, $"{testCase.Identifier}: the rendering read back with a different axiom count.\n{first}");
                    Assert.AreEqual(first, OwlFunctionalSyntaxWriter.Write(reread), $"{testCase.Identifier}: the rendering is not a fixed point.");
                    documents++;
                }
            }
        }

        Assert.IsGreaterThan(50, documents, "The corpus sweep should cover the functional-syntax documents.");
    }

    [TestMethod]
    public void NestedAnnotationsSurviveTheTextRoundTrip()
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
        OwlOntologyDocument reread = OwlFunctionalSyntaxReader.Read(OwlFunctionalSyntaxWriter.Write(document));

        Assert.IsFalse(reread.Diagnostics.HasErrors);
        ImmutableArray<OwlAnnotation> annotations = FindSubClassOf(reread)!.Annotations;
        Assert.HasCount(1, annotations);
        Assert.HasCount(1, annotations[0].Annotations);
    }

    [TestMethod]
    public void QualifiedCardinalityAndRestrictionsSurviveTheTextRoundTrip()
    {
        const string Document = """
            Prefix( : = <http://example.org/> )
            Prefix( xsd: = <http://www.w3.org/2001/XMLSchema#> )
            Ontology( <http://example.org/o>
              Declaration( Class( :A ) )
              Declaration( ObjectProperty( :p ) )
              Declaration( DataProperty( :dp ) )
              SubClassOf( :A ObjectMinCardinality( 2 :p :A ) )
              SubClassOf( :A DataAllValuesFrom( :dp DatatypeRestriction( xsd:integer xsd:minInclusive "0"^^xsd:integer ) ) )
            )
            """;

        OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(Document);
        Assert.IsFalse(document.Diagnostics.HasErrors);

        string first = OwlFunctionalSyntaxWriter.Write(document);
        OwlOntologyDocument reread = OwlFunctionalSyntaxReader.Read(first);

        Assert.IsFalse(reread.Diagnostics.HasErrors);
        Assert.HasCount(document.Axioms.Length, reread.Axioms);
        Assert.AreEqual(first, OwlFunctionalSyntaxWriter.Write(reread));
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
}
