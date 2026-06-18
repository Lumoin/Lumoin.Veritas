using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// One imported ontology a test case supplies inline: the manifests carry the
/// document under a sibling node keyed by the ontology IRI that an
/// <c>owl:imports</c> triple in a test document resolves against.
/// </summary>
/// <param name="Iri">The ontology IRI (<c>test:importedOntologyIRI</c>) the import resolves by.</param>
/// <param name="RdfXml">The ontology document as inline RDF/XML, or <c>null</c>.</param>
/// <param name="Functional">The ontology document in OWL functional syntax, or <c>null</c>.</param>
internal sealed record Owl2ImportedOntology(string Iri, string? RdfXml, string? Functional);

/// <summary>
/// One W3C OWL 2 conformance test case as declared in the test-ontology
/// manifests (<c>http://www.w3.org/2007/OWL/testOntology#</c>). Unlike the
/// RDF/SPARQL suites, the ontology documents under test are carried
/// <b>inline</b> as escaped strings rather than as sibling files.
/// </summary>
/// <remarks>
/// <para>
/// Every test in the corpus is (also) a <c>ProfileIdentificationTest</c>:
/// <see cref="Profiles"/> lists the OWL 2 profiles every ontology document of
/// the test belongs to, and per the conformance document the annotation is
/// complete — a profile <b>absent</b> from the set means at least one of the
/// test's documents is <b>not</b> in that profile. <see cref="Species"/>
/// carries the analogous DL/Full markers.
/// </para>
/// </remarks>
/// <param name="Uri">The full test IRI (an <c>owl.semanticweb.org</c> identifier).</param>
/// <param name="Identifier">The test's short <c>test:identifier</c>.</param>
/// <param name="Description">The test's <c>test:description</c>, when present.</param>
/// <param name="Kinds">The declared test kinds (the <c>rdf:type</c> markers ending in <c>…Test</c>).</param>
/// <param name="Profiles">The OWL 2 profiles (<c>EL</c>/<c>QL</c>/<c>RL</c>) the test's documents belong to; absence is a negative verdict.</param>
/// <param name="Species">The species markers (<c>DL</c>/<c>FULL</c>) the test's documents belong to.</param>
/// <param name="Semantics">The semantics the test applies under (<c>DIRECT</c>/<c>RDF-BASED</c>).</param>
/// <param name="RdfXmlPremise">The premise ontology document as inline RDF/XML, or <c>null</c>.</param>
/// <param name="RdfXmlConclusion">The conclusion ontology document as inline RDF/XML, or <c>null</c>.</param>
/// <param name="RdfXmlNonConclusion">The non-conclusion ontology document (negative entailment) as inline RDF/XML, or <c>null</c>.</param>
/// <param name="RdfXmlInput">The input ontology document (pure profile/syntax tests) as inline RDF/XML, or <c>null</c>.</param>
/// <param name="FunctionalPremise">The premise ontology document in OWL functional syntax, or <c>null</c>.</param>
/// <param name="FunctionalConclusion">The conclusion ontology document in OWL functional syntax, or <c>null</c>.</param>
/// <param name="FunctionalNonConclusion">The non-conclusion ontology document in OWL functional syntax, or <c>null</c>.</param>
/// <param name="Imports">The imported ontologies the test supplies (<c>test:importedOntology</c>), resolvable by IRI when a document's <c>owl:imports</c> references them.</param>
[DebuggerDisplay("Owl2TestCase {Identifier,nq}")]
internal sealed record Owl2TestCase(
    Uri Uri,
    string Identifier,
    string Description,
    IReadOnlySet<string> Kinds,
    IReadOnlySet<string> Profiles,
    IReadOnlySet<string> Species,
    IReadOnlySet<string> Semantics,
    string? RdfXmlPremise,
    string? RdfXmlConclusion,
    string? RdfXmlNonConclusion,
    string? RdfXmlInput,
    string? FunctionalPremise,
    string? FunctionalConclusion,
    string? FunctionalNonConclusion,
    IReadOnlyList<Owl2ImportedOntology> Imports)
{
    /// <summary>The RDF/XML documents the test asserts profile membership over: premise, conclusion, non-conclusion, and input, in that order, skipping absent ones.</summary>
    public IEnumerable<string> RdfXmlDocuments
    {
        get
        {
            if(RdfXmlPremise is not null)
            {
                yield return RdfXmlPremise;
            }

            if(RdfXmlConclusion is not null)
            {
                yield return RdfXmlConclusion;
            }

            if(RdfXmlNonConclusion is not null)
            {
                yield return RdfXmlNonConclusion;
            }

            if(RdfXmlInput is not null)
            {
                yield return RdfXmlInput;
            }
        }
    }

    /// <summary>Whether the test carries at least one RDF/XML document (a functional-syntax-only test needs the functional-syntax reader).</summary>
    public bool IsRdfXmlReadable
    {
        get
        {
            return RdfXmlPremise is not null || RdfXmlInput is not null || RdfXmlConclusion is not null || RdfXmlNonConclusion is not null;
        }
    }
}
