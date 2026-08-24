using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Runs the RL-marked arms of the W3C OWL 2 conformance corpus through the
/// OWL 2 RL/RDF rules closure: consistency and inconsistency verdicts, and
/// positive/negative entailment by embedding the conclusion graph into the
/// closure (blank nodes existential, named terms and literals exact).
/// </summary>
/// <remarks>
/// Applicability is enforced at the data source
/// (<see cref="Owl2TestRemit.RlMarked"/>): only tests the corpus marks as RL
/// materialise here — for those, the RL rules are the complete calculus the
/// conformance document prescribes, and a non-RL case is another arm's job, so
/// it never becomes a row and no skip is reported for it. The premise's
/// <c>owl:imports</c> resolve against the test's supplied ontologies, so the
/// reasoned-over unit is the imports closure. An entailment test stated for
/// the Direct Semantics only differs from its RDF-Based form solely in the
/// conclusion's annotations, which the Direct Semantics treats as non-logical;
/// those are projected away and the same closure decides the remainder.
/// </remarks>
[TestClass]
internal sealed class W3cOwl2RlTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Tests whose conclusions lie beyond the entailment surface, recorded
    /// as self-correcting known gaps — currently none. The history of the
    /// list: the trans⇄chain pair closed via the
    /// <c>chain-trans</c>/<c>trans-chain</c> extension rules; the
    /// comprehension pair (<c>I5.26-010</c>, <c>I5.5-005</c>) via the
    /// entailment check's
    /// <see cref="OwlComprehension.InformativeConditions"/> mode (the
    /// closure never materialises comprehension; the checker grants
    /// pure-existence expression scaffolds exactly); and the
    /// contrapositive cluster (the DisjointClasses/DisjointProperties/QCR
    /// families, <c>fp/ifp-differentFrom</c>) via
    /// <see cref="OwlRlEntailment"/> refutation — the rules are complete
    /// for assertional consequences only (Profiles §4.3, Theorem PR1), so
    /// the contrapositive forms refute through the closure instead. A run
    /// fails if a listed test starts passing.
    /// </summary>
    private static HashSet<string> KnownGaps { get; } = [];

    /// <summary>Runs one approved-status, RL-marked OWL 2 test case through the RL closure.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("approved", "all.rdf", Owl2TestRemit.RlMarked)]
    public void RunApproved(Owl2TestCase testCase)
    {
        RunAndAssert(testCase);
    }

    /// <summary>Runs one proposed-status, RL-marked OWL 2 test case through the RL closure.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("proposed", "all.rdf", Owl2TestRemit.RlMarked)]
    public void RunProposed(Owl2TestCase testCase)
    {
        RunAndAssert(testCase);
    }

    private void RunAndAssert(Owl2TestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        //An entailment test stated for the Direct Semantics only differs from
        //its RDF-Based form solely in the conclusion's annotations, which the
        //Direct Semantics treats as non-logical (Syntax §5.5). For the RL
        //profile the two semantics agree on logical consequences, so those
        //annotation assertions are projected away below and the same closure
        //decides the remainder.
        bool isEntailment = testCase.Kinds.Contains("PositiveEntailmentTest") || testCase.Kinds.Contains("NegativeEntailmentTest");
        bool directSemanticsOnly = isEntailment && !testCase.Semantics.Contains("RDF-BASED");

        List<Quad>? maybePremise = LoadQuads(testCase, testCase.RdfXmlPremise, testCase.FunctionalPremise);
        if(maybePremise is not List<Quad> premiseQuads)
        {
            Assert.Fail($"{testCase.Identifier}: the test declares no premise document in a syntax the harness reads.");

            return;
        }

        //The reasoned-over unit is the premise's imports closure: its
        //owl:imports resolve against the test's supplied ontologies, and a
        //supplied ontology the premise never imports contributes nothing.
        premiseQuads = Owl2ImportResolver.Expand(testCase, premiseQuads);

        TermDictionary dictionary = new();
        List<EncodedTriple> encoded = Encode(premiseQuads, dictionary);
        OwlRlTerms terms = new(dictionary);

        OwlRlResult result = OwlRlClosure.Compute(encoded, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), cancellationToken: TestContext.CancellationToken);

        bool isKnownGap = KnownGaps.Contains(testCase.Identifier);

        if(testCase.Kinds.Contains("InconsistencyTest"))
        {
            if(isKnownGap)
            {
                AssertKnownGap(testCase, holds: !result.IsConsistent);

                return;
            }

            Assert.IsFalse(result.IsConsistent, $"{testCase.Identifier}: the RL rules should derive an inconsistency.");

            return;
        }

        if(testCase.Kinds.Contains("ConsistencyTest"))
        {
            Assert.IsTrue(result.IsConsistent, $"{testCase.Identifier}: the premise is consistent, but rule {result.InconsistencyRule} fired.");
        }

        //Entailment runs through the full surface — embedding over the
        //closure, refutation for the contrapositive forms — in both
        //directions, one semantics. The comprehension mode matches the
        //corpus's WebOnt heritage: pure-existence expression scaffolds are
        //granted by the checker, never materialised by the closure.
        if(testCase.Kinds.Contains("PositiveEntailmentTest")
            && LoadQuads(testCase, testCase.RdfXmlConclusion, testCase.FunctionalConclusion) is List<Quad> conclusionQuads)
        {
            if(directSemanticsOnly)
            {
                conclusionQuads = WithoutAnnotations(conclusionQuads);
            }

            bool entails = OwlRlEntailment.Entails(
                premiseQuads, conclusionQuads, dictionary, terms,
                OwlRlDatatypeOracles.FromDictionary(dictionary), OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken);

            if(isKnownGap)
            {
                AssertKnownGap(testCase, holds: entails);

                return;
            }

            Assert.IsTrue(entails, $"{testCase.Identifier}: the conclusion does not follow from the RL closure.");
        }

        if(testCase.Kinds.Contains("NegativeEntailmentTest")
            && LoadQuads(testCase, testCase.RdfXmlNonConclusion, testCase.FunctionalNonConclusion) is List<Quad> nonConclusionQuads)
        {
            if(directSemanticsOnly)
            {
                nonConclusionQuads = WithoutAnnotations(nonConclusionQuads);
            }

            Assert.IsFalse(
                OwlRlEntailment.Entails(
                    premiseQuads, nonConclusionQuads, dictionary, terms,
                    OwlRlDatatypeOracles.FromDictionary(dictionary), OwlComprehension.InformativeConditions, cancellationToken: TestContext.CancellationToken),
                $"{testCase.Identifier}: the non-conclusion follows from the RL closure but must not.");
        }
    }

    //Loads a document role as triples: RDF/XML parses directly; functional
    //syntax reads into structural form and serialises through the forward
    //RDF mapping. Null when the role has no readable document.
    private static List<Quad>? LoadQuads(Owl2TestCase testCase, Utf8String? rdfXml, string? functional)
    {
        if(rdfXml is { } xml)
        {
            return ParseDocument(testCase, xml);
        }

        if(functional is string text)
        {
            Lumoin.Veritas.Owl.Structural.OwlOntologyDocument document = Lumoin.Veritas.Owl.Functional.OwlFunctionalSyntaxReader.Read(text);
            Assert.IsFalse(document.Diagnostics.HasErrors, $"{testCase.Identifier}: a test document did not parse as functional syntax; the test cannot be set up.");

            return Lumoin.Veritas.Owl.Structural.OwlStructuralToRdf.ToQuads(document);
        }

        return null;
    }

    //A known-gap test passes while the gap holds (a pinned, expected
    //capability boundary) and fails loudly the moment it starts passing, so
    //the list self-corrects.
    private static void AssertKnownGap(Owl2TestCase testCase, bool holds)
    {
        if(holds)
        {
            Assert.Fail($"{testCase.Identifier} is a recorded known gap but now passes; remove it from KnownGaps.");
        }
    }

    private static List<Quad> ParseDocument(Owl2TestCase testCase, Utf8String document)
    {
        DiagnosticBag diagnostics = new();
        List<Quad> quads = [.. RdfXmlReader.Read(document.Memory, diagnostics, baseIri: Utf8Strings.From(testCase.Uri.AbsoluteUri))];
        Assert.IsFalse(diagnostics.HasErrors, $"{testCase.Identifier}: a test document did not parse as RDF/XML; the test cannot be set up.");

        return quads;
    }

    private static List<EncodedTriple> Encode(List<Quad> quads, TermDictionary dictionary)
    {
        List<EncodedTriple> encoded = new(quads.Count);
        foreach(Quad quad in quads)
        {
            encoded.Add(EncodedTriple.FromEncoded(
                dictionary.GetOrAdd(quad.Subject).Encoded,
                dictionary.GetOrAdd(quad.Predicate).Encoded,
                dictionary.GetOrAdd(quad.Object).Encoded));
        }

        return encoded;
    }

    //The Direct Semantics' logical projection of a conclusion graph: its
    //annotation assertions carry no logical meaning (Syntax §5.5), so a
    //conclusion that adds only annotations over the premise is entailed once
    //they are set aside. The annotation-property set is the built-ins plus the
    //conclusion's own owl:AnnotationProperty declarations, both supplied by the
    //RDF mapping.
    private static List<Quad> WithoutAnnotations(List<Quad> conclusion)
    {
        IReadOnlySet<Utf8String> annotationProperties =
            Lumoin.Veritas.Owl.Structural.OwlRdfMapper.Map(conclusion).DeclaredAnnotationProperties;

        List<Quad> logical = new(conclusion.Count);
        foreach(Quad quad in conclusion)
        {
            if(!annotationProperties.Contains(quad.Predicate.Iri))
            {
                logical.Add(quad);
            }
        }

        return logical;
    }
}
