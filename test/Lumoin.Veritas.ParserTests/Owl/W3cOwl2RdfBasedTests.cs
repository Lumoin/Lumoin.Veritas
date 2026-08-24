using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Runs the RDF-Based-semantics residue arm of the W3C OWL 2 conformance
/// corpus — the entailment and consistency cases neither the RL rules runner
/// nor the Direct-Semantics DL tableau owns — through the OWL 2 RL/RDF rules
/// closure.
/// </summary>
/// <remarks>
/// <para>
/// Applicability is enforced at the data source
/// (<see cref="Owl2TestRemit.RdfBasedBeyondRl"/>): only the cases outside both
/// other arms' remits materialise here, so this arm makes no claim on the rest
/// and reports no skip for them. The premise's <c>owl:imports</c> resolve
/// against the test's supplied ontologies, so the reasoned-over unit is the
/// imports closure. Annotation triples are ordinary triples under the RDF-Based
/// semantics, so no annotation projection applies.
/// </para>
/// <para>
/// The RL/RDF closure is sound for the RDF-Based semantics on any document and
/// complete only for the RL profile, so the arm's claims split by direction. A
/// consistency verdict and a non-entailment are decided soundness claims: a
/// spurious clash on a consistent premise, or a spurious embedding of a
/// non-conclusion, is a soundness defect and fails. A declared inconsistency the
/// closure cannot derive, and a positive conclusion the embedding cannot settle,
/// lie at the closure's completeness boundary, so they are named census entries
/// (<see cref="InconsistencyGaps"/>, <see cref="EntailmentGaps"/>) rather than
/// silent passes: an unpinned boundary fails demanding the closure be extended
/// or the id pinned, and a pinned id the closure now settles fails demanding the
/// pin be removed.
/// </para>
/// <para>
/// A case whose manifest-declared positive conclusion the RDF-Based semantics
/// itself refutes is a recorded corpus defect
/// (<see cref="RefutedEntailmentClaims"/>): the premise admits models in which
/// the declared conclusion is false, so non-entailment is the decided soundness
/// claim and an embedding that settles the conclusion fails.
/// </para>
/// </remarks>
[TestClass]
internal sealed class W3cOwl2RdfBasedTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// The identifiers whose declared inconsistency the RL/RDF closure does not
    /// derive: the RDF-Based semantics finds the premise inconsistent, but the RL
    /// rules, complete only for the RL profile, do not reach the contradiction.
    /// An unpinned declared inconsistency the closure fails to derive fails the
    /// run; a pinned id the closure now clashes on fails demanding the pin be
    /// removed. Seeded by measurement over the corpus; the set is measured empty
    /// — the closure derives every declared inconsistency in the arm's remit.
    /// </summary>
    private static HashSet<string> InconsistencyGaps { get; } = [];

    /// <summary>
    /// The identifiers whose positive conclusion the RL/RDF embedding does not
    /// settle: the RDF-Based semantics entails the conclusion, but the RL rules,
    /// complete only for the RL profile, do not embed it into the closure. An
    /// unpinned positive conclusion the embedding fails to settle fails the run; a
    /// pinned id the embedding now settles fails demanding the pin be removed.
    /// Seeded by measurement over the corpus; the set is measured empty — every
    /// declared positive conclusion in the arm's remit either embeds or carries a
    /// refuted declaration recorded in <see cref="RefutedEntailmentClaims"/>.
    /// </summary>
    private static HashSet<string> EntailmentGaps { get; } = [];

    /// <summary>
    /// The identifiers whose manifest-declared positive entailment the RDF-Based
    /// semantics refutes: the premise admits models in which the declared
    /// conclusion is false, so the declaration is a corpus defect and
    /// non-entailment is the decided soundness claim, asserted in every mode.
    /// Both rows declare the two factor individuals of a pinned integer product
    /// recoverable as a specific two-element enumeration, but the premise
    /// constrains only the product of the two fibre cardinalities, never the
    /// factors: the trivial factorisation — one factor 1, the other the whole
    /// product — satisfies every premise triple and falsifies the declared
    /// enumeration.
    /// An embedding that settles such a conclusion derives a falsehood and
    /// fails.
    /// </summary>
    private static HashSet<string> RefutedEntailmentClaims { get; } =
    [
        "WebOnt-extra-credit-003",
        "WebOnt-extra-credit-004"
    ];

    /// <summary>
    /// The absolute path a census seeding run appends its measured boundary exits
    /// to, or <c>null</c> for the strict census gate. Setting
    /// <c>VERITAS_SEED_RDFBASED_CENSUS</c> to a path re-derives the sets after a
    /// closure widening: every underived inconsistency and every unsettled
    /// positive conclusion is recorded rather than checked. Unset, the census is
    /// exact — an unpinned boundary, or a pin the closure now settles, fails the
    /// run.
    /// </summary>
    private static string? RdfBasedCensusSeedSink { get; } = Environment.GetEnvironmentVariable("VERITAS_SEED_RDFBASED_CENSUS");

    /// <summary>Serialises appends to the seeding sink across the data-driven rows.</summary>
    private static Lock RdfBasedCensusSeedGate { get; } = new();

    /// <summary>Runs one approved-status RDF-Based-residue OWL 2 test case through the RL/RDF closure.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("approved", "all.rdf", Owl2TestRemit.RdfBasedBeyondRl)]
    public void RunApproved(Owl2TestCase testCase)
    {
        RunAndAssert(testCase);
    }

    /// <summary>Runs one proposed-status RDF-Based-residue OWL 2 test case through the RL/RDF closure.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("proposed", "all.rdf", Owl2TestRemit.RdfBasedBeyondRl)]
    public void RunProposed(Owl2TestCase testCase)
    {
        RunAndAssert(testCase);
    }

    private void RunAndAssert(Owl2TestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

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

        //The arm claims the RDF-Based semantics wholesale, so every closure
        //it computes seeds the metaclass-merged axiomatic vocabulary —
        //consistency verdicts and entailment checks alike.
        OwlRlResult result = OwlRlClosure.Compute(encoded, terms, OwlRlDatatypeOracles.FromDictionary(dictionary), axiomaticVocabulary: OwlAxiomaticVocabulary.MetaclassMerged, cancellationToken: TestContext.CancellationToken);

        if(testCase.Kinds.Contains("InconsistencyTest"))
        {
            //The closure should clash: the RDF-Based semantics declares the
            //premise inconsistent, and a premise it cannot refute is a
            //completeness-boundary census entry, never a silent pass.
            AssertInconsistencyBoundary(testCase, derived: !result.IsConsistent);

            return;
        }

        if(testCase.Kinds.Contains("ConsistencyTest"))
        {
            //Soundness: a spurious clash on a consistent premise is a defect.
            Assert.IsTrue(result.IsConsistent, $"{testCase.Identifier}: the premise is consistent under the RDF-Based semantics, but rule {result.InconsistencyRule} fired.");
        }

        if(testCase.Kinds.Contains("PositiveEntailmentTest")
            && LoadQuads(testCase, testCase.RdfXmlConclusion, testCase.FunctionalConclusion) is List<Quad> conclusionQuads)
        {
            bool entails = OwlRlEntailment.TryEntail(
                premiseQuads, conclusionQuads, dictionary, terms, out IReadOnlyList<Quad> unsettled,
                OwlRlDatatypeOracles.FromDictionary(dictionary), OwlComprehension.InformativeConditions, OwlAxiomaticVocabulary.MetaclassMerged, TestContext.CancellationToken);

            //The embedding should hold; a conclusion it cannot settle is a
            //completeness-boundary census entry, never a silent pass.
            AssertEntailmentBoundary(testCase, entailed: entails, unsettled);
        }

        if(testCase.Kinds.Contains("NegativeEntailmentTest")
            && LoadQuads(testCase, testCase.RdfXmlNonConclusion, testCase.FunctionalNonConclusion) is List<Quad> nonConclusionQuads)
        {
            //Soundness: a spurious embedding of a non-conclusion is a defect.
            Assert.IsFalse(
                OwlRlEntailment.Entails(
                    premiseQuads, nonConclusionQuads, dictionary, terms,
                    OwlRlDatatypeOracles.FromDictionary(dictionary), OwlComprehension.InformativeConditions, OwlAxiomaticVocabulary.MetaclassMerged, TestContext.CancellationToken),
                $"{testCase.Identifier}: the non-conclusion follows from the RL/RDF closure but must not.");
        }
    }

    /// <summary>
    /// Resolves an inconsistency case against the pinned
    /// <see cref="InconsistencyGaps"/> census: the RL/RDF closure should clash on
    /// the premise; when it does not the case is a named completeness boundary —
    /// pinned it passes, unpinned it fails demanding the closure be extended or
    /// the id pinned. A pinned id the closure now clashes on fails demanding the
    /// pin be removed. Under a seeding run the boundary is recorded instead of
    /// asserted.
    /// </summary>
    /// <param name="testCase">The test case.</param>
    /// <param name="derived">Whether the closure derived the declared inconsistency.</param>
    private static void AssertInconsistencyBoundary(Owl2TestCase testCase, bool derived)
    {
        if(derived)
        {
            if(RdfBasedCensusSeedSink is null && InconsistencyGaps.Contains(testCase.Identifier))
            {
                Assert.Fail($"{testCase.Identifier}: pinned as an inconsistency gap but the RL/RDF closure now derives the inconsistency; remove it from InconsistencyGaps.");
            }

            return;
        }

        if(RdfBasedCensusSeedSink is string sink)
        {
            RecordRdfBasedSeed(sink, "INCONSISTENCY", testCase.Identifier, reason: null);

            return;
        }

        if(!InconsistencyGaps.Contains(testCase.Identifier))
        {
            Assert.Fail($"{testCase.Identifier}: the RL/RDF closure does not derive the declared inconsistency; extend the closure or pin it in InconsistencyGaps.");
        }
    }

    /// <summary>
    /// Resolves a positive-entailment case against the census sets. An id in
    /// <see cref="RefutedEntailmentClaims"/> carries a refuted declaration, so
    /// non-entailment is asserted as a soundness claim in every mode. Otherwise
    /// the RL/RDF embedding should settle the conclusion against the pinned
    /// <see cref="EntailmentGaps"/> census; when it does not the case is a named
    /// completeness boundary — pinned it passes, unpinned it fails demanding the
    /// closure be extended or the id pinned. A pinned id the embedding now
    /// settles fails demanding the pin be removed. Under a seeding run the
    /// boundary is recorded instead of asserted.
    /// </summary>
    /// <param name="testCase">The test case.</param>
    /// <param name="entailed">Whether the embedding settled the conclusion.</param>
    /// <param name="unsettled">The conclusion triples the check could not settle; non-empty exactly when <paramref name="entailed"/> is <see langword="false"/>.</param>
    private static void AssertEntailmentBoundary(Owl2TestCase testCase, bool entailed, IReadOnlyList<Quad> unsettled)
    {
        if(RefutedEntailmentClaims.Contains(testCase.Identifier))
        {
            Assert.IsFalse(entailed, $"{testCase.Identifier}: the declared conclusion is refuted under the RDF-Based semantics, but the embedding settles it.");

            return;
        }

        if(entailed)
        {
            if(RdfBasedCensusSeedSink is null && EntailmentGaps.Contains(testCase.Identifier))
            {
                Assert.Fail($"{testCase.Identifier}: pinned as an entailment gap but the RL/RDF embedding now settles the conclusion; remove it from EntailmentGaps.");
            }

            return;
        }

        if(RdfBasedCensusSeedSink is string sink)
        {
            RecordRdfBasedSeed(sink, "ENTAILMENT", testCase.Identifier, DescribeUnsettled(unsettled));

            return;
        }

        if(!EntailmentGaps.Contains(testCase.Identifier))
        {
            Assert.Fail($"{testCase.Identifier}: the conclusion does not follow from the RL/RDF closure; extend the closure or pin it in EntailmentGaps.");
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

    /// <summary>
    /// Renders the unsettled remainder into the one-line census reason: up
    /// to eight triples in subject-predicate-object form, then a count of
    /// the rest. Whitespace inside rendered terms folds to single spaces so
    /// the sink line stays one tab-separated record.
    /// </summary>
    /// <param name="unsettled">The conclusion triples the entailment check could not settle.</param>
    /// <returns>The reason text naming the remainder.</returns>
    internal static string DescribeUnsettled(IReadOnlyList<Quad> unsettled)
    {
        ArgumentNullException.ThrowIfNull(unsettled);

        StringBuilder reason = new();
        reason.Append(CultureInfo.InvariantCulture, $"unsettled {unsettled.Count}:");
        int rendered = Math.Min(unsettled.Count, 8);
        for(int i = 0; i < rendered; i++)
        {
            reason.Append(i == 0 ? " " : "; ");
            reason.Append(CultureInfo.InvariantCulture, $"{unsettled[i].Subject} {unsettled[i].Predicate} {unsettled[i].Object}");
        }

        if(unsettled.Count > rendered)
        {
            reason.Append(CultureInfo.InvariantCulture, $"; +{unsettled.Count - rendered} more");
        }

        for(int i = 0; i < reason.Length; i++)
        {
            if(reason[i] is '\r' or '\n' or '\t')
            {
                reason[i] = ' ';
            }
        }

        return reason.ToString();
    }

    /// <summary>Appends one measured boundary exit to the seeding sink under the append gate.</summary>
    /// <param name="sink">The absolute sink path.</param>
    /// <param name="category">The census the exit belongs to.</param>
    /// <param name="identifier">The test identifier.</param>
    /// <param name="reason">The entailment-gap reason, or <c>null</c> for an inconsistency gap.</param>
    private static void RecordRdfBasedSeed(string sink, string category, string identifier, string? reason)
    {
        string line = reason is null ? $"{category}\t{identifier}" : $"{category}\t{identifier}\t{reason}";
        lock(RdfBasedCensusSeedGate)
        {
            File.AppendAllText(sink, line + Environment.NewLine);
        }
    }
}
