using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Tests.Conformance;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// Runs the Direct-Semantics, DL-species arms of the W3C OWL 2 conformance
/// corpus through the description-logic tableau: consistency and
/// inconsistency verdicts directly, and positive/negative entailment by
/// refutation — a conclusion axiom holds exactly when its counterexample
/// concept, asserted on a fresh witness individual alongside the premise,
/// is unsatisfiable.
/// </summary>
/// <remarks>
/// Only tests stated for the Direct Semantics over DL-species documents run
/// here, and only those the RL runner does not already decide — for
/// RL-marked tests the RL rules are the prescribed complete calculus. The
/// premise's <c>owl:imports</c> resolve against the test's supplied
/// ontologies, so the reasoned-over unit is the imports closure. The
/// runner's scope is the tests whose premise module lies wholly inside the
/// tableau's fragment: a premise naming beyond-fragment constructs abstains
/// up front, named, before any satisfiability search — its verdict could
/// only ever be fragment-relative, and the search cost is unbounded while
/// the outcome is foreclosed. Within scope, verdicts are whole-module;
/// refutation checks whose synthesized counterexample falls outside the
/// fragment abstain the same way. Honest skips: non-entailment/consistency
/// kinds, RL-marked tests, RDF-Based-only tests, Full-only tests,
/// beyond-fragment premises, conclusion axioms with no refutation encoding
/// in the tableau's fragment.
/// </remarks>
[TestClass]
internal sealed class W3cOwl2DirectTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The engines the corpus runs against: the snapshot tableau as the reference baseline and the SAT-backed sibling as the parametrized arm.</summary>
    internal enum DirectEngine
    {
        /// <summary>The snapshot tableau, <see cref="AlcModuleReasoner.DecideConsistency"/> — the reference baseline.</summary>
        Snapshot,

        /// <summary>The SAT-backed sibling, <see cref="SatTableauModuleReasoner.DecideConsistency"/>.</summary>
        SatBacked,
    }

    /// <summary>
    /// Tests whose in-fragment tableau search measurably exceeds the
    /// <em>snapshot</em> engine's practical reach (over twenty seconds per
    /// decision when triaged): hard propositional instances whose
    /// disjunction branching a copy-on-branch tableau cannot bound without
    /// conflict learning. The gap is engine-relative — the SAT-backed
    /// sibling decides every one of them, so it abstains on none; only the
    /// snapshot arm reads this set.
    /// </summary>
    private static HashSet<string> SnapshotPracticalReachGaps { get; } =
    [
        "WebOnt-description-logic-040",
        "WebOnt-description-logic-201",
        "WebOnt-description-logic-208",
        "WebOnt-description-logic-209",
    ];

    /// <summary>Whether the engine abstains on the test as a measured practical-reach gap: the snapshot engine does on the recorded set, the SAT-backed engine on none.</summary>
    /// <param name="engine">The deciding engine.</param>
    /// <param name="identifier">The test identifier.</param>
    /// <returns><c>true</c> when the engine abstains on the test as a practical-reach gap.</returns>
    private static bool IsPracticalReachGap(DirectEngine engine, string identifier)
    {
        return engine == DirectEngine.Snapshot && SnapshotPracticalReachGaps.Contains(identifier);
    }

    /// <summary>The fresh witness individual counterexample concepts are asserted on; its reserved IRI cannot collide with corpus document terms.</summary>
    private static NamedNode Witness { get; } = new(Utf8String.FromString("urn:lumoin:veritas:conformance:direct#witness"));

    /// <summary>The <c>owl:Thing</c> reference used to close a property over all successors in a domain counterexample.</summary>
    private static OwlClassReference ThingReference { get; } = new(new NamedNode(Utf8String.FromString("http://www.w3.org/2002/07/owl#Thing")));

    /// <summary>Runs one approved-status test case through the snapshot tableau — the reference baseline.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("approved", "all.rdf")]
    public void RunApproved(Owl2TestCase testCase)
    {
        RunAndAssert(DirectEngine.Snapshot, testCase);
    }

    /// <summary>Runs one proposed-status test case through the snapshot tableau — the reference baseline.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("proposed", "all.rdf")]
    public void RunProposed(Owl2TestCase testCase)
    {
        RunAndAssert(DirectEngine.Snapshot, testCase);
    }

    /// <summary>Runs one approved-status test case through the SAT-backed sibling, the parametrized engine arm.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("approved", "all.rdf")]
    public void RunApprovedSatBacked(Owl2TestCase testCase)
    {
        RunAndAssert(DirectEngine.SatBacked, testCase);
    }

    /// <summary>Runs one proposed-status test case through the SAT-backed sibling, the parametrized engine arm.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("proposed", "all.rdf")]
    public void RunProposedSatBacked(Owl2TestCase testCase)
    {
        RunAndAssert(DirectEngine.SatBacked, testCase);
    }

    /// <summary>Applies the remit guard, loads and maps the test documents, and dispatches the test's kinds to the tableau arms through the selected engine.</summary>
    /// <param name="engine">The engine to decide through.</param>
    /// <param name="testCase">The test case.</param>
    private void RunAndAssert(DirectEngine engine, Owl2TestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        bool isPositive = testCase.Kinds.Contains("PositiveEntailmentTest");
        bool isNegative = testCase.Kinds.Contains("NegativeEntailmentTest");
        bool isInconsistency = testCase.Kinds.Contains("InconsistencyTest");
        bool isConsistency = testCase.Kinds.Contains("ConsistencyTest");

        if(!isPositive && !isNegative && !isInconsistency && !isConsistency)
        {
            Assert.Inconclusive("Not an entailment or consistency test; the tableau decides nothing here.");
        }

        if(testCase.Profiles.Contains("RL"))
        {
            Assert.Inconclusive("RL-marked; the RL rules runner decides it.");
        }

        if(!testCase.Semantics.Contains("DIRECT"))
        {
            Assert.Inconclusive("Not stated for the Direct Semantics.");
        }

        if(!testCase.Species.Contains("DL"))
        {
            Assert.Inconclusive("Not a DL-species test; a description-logic tableau makes no claim.");
        }

        if(IsPracticalReachGap(engine, testCase.Identifier))
        {
            Assert.Inconclusive("Beyond the snapshot tableau's practical reach; the SAT-backed sibling's workload.");
        }

        List<Quad>? maybePremise = LoadQuads(testCase, testCase.RdfXmlPremise, testCase.FunctionalPremise);
        if(maybePremise is not List<Quad> premiseQuads)
        {
            Assert.Inconclusive("The test declares no premise document in a syntax the harness reads.");

            return;
        }

        //The reasoned-over unit is the premise's imports closure: its
        //owl:imports resolve against the test's supplied ontologies, and a
        //supplied ontology the premise never imports contributes nothing.
        premiseQuads = Owl2ImportResolver.Expand(testCase, premiseQuads);
        OwlOntologyDocument premise = OwlRdfMapper.Map(premiseQuads);
        if(premise.Diagnostics.HasErrors)
        {
            Assert.Inconclusive("The premise did not map to structural form; the test cannot be set up.");
        }

        //The scope gate: a beyond-fragment premise forecloses every arm's
        //decidable outcome, so it abstains before any tableau search runs.
        IReadOnlyList<string> remainder = AlcModuleReasoner.Survey(new ReasoningModule([.. premise.Axioms], Violations: []));
        if(remainder.Count > 0)
        {
            Assert.Inconclusive($"The premise lies beyond the tableau's fragment: {string.Join(", ", remainder.Distinct())}.");
        }

        if(isInconsistency)
        {
            ModuleVerdict verdict = Decide(engine, premise, refutation: null);
            if(verdict.IsConsistent)
            {
                AbstainWhenBeyondFragment(verdict);
                Assert.Fail($"{testCase.Identifier}: the tableau should find the premise inconsistent.");
            }

            return;
        }

        if(isConsistency)
        {
            ModuleVerdict verdict = Decide(engine, premise, refutation: null);

            Assert.IsTrue(verdict.IsConsistent, $"{testCase.Identifier}: the premise is consistent, but the tableau found a clash.");
            AbstainWhenBeyondFragment(verdict);
        }

        if(isPositive
            && LoadQuads(testCase, testCase.RdfXmlConclusion, testCase.FunctionalConclusion) is List<Quad> conclusionQuads)
        {
            AssertEntailed(engine, testCase, premise, conclusionQuads);
        }

        if(isNegative
            && LoadQuads(testCase, testCase.RdfXmlNonConclusion, testCase.FunctionalNonConclusion) is List<Quad> nonConclusionQuads)
        {
            AssertNotEntailed(engine, testCase, premise, nonConclusionQuads);
        }
    }

    /// <summary>
    /// The positive arm: every logical conclusion axiom must be entailed,
    /// each by all of its refutation checks coming back unsatisfiable. A
    /// satisfiable check over a fully supported module is a genuine
    /// non-entailment and fails; one resting on beyond-fragment axioms
    /// abstains.
    /// </summary>
    /// <param name="engine">The engine to decide through.</param>
    /// <param name="testCase">The test case.</param>
    /// <param name="premise">The mapped premise.</param>
    /// <param name="conclusionQuads">The conclusion document's triples.</param>
    private void AssertEntailed(DirectEngine engine, Owl2TestCase testCase, OwlOntologyDocument premise, List<Quad> conclusionQuads)
    {
        OwlOntologyDocument conclusion = OwlRdfMapper.Map(conclusionQuads);

        foreach(OwlAxiom axiom in conclusion.Axioms)
        {
            if(IsVacuous(axiom))
            {
                continue;
            }

            if(Refutations(axiom) is not List<OwlClassAssertionAxiom> checks)
            {
                Assert.Inconclusive($"A conclusion {axiom.GetType().Name} has no refutation encoding in the tableau's fragment.");

                return;
            }

            foreach(OwlClassAssertionAxiom check in checks)
            {
                if(IsBeyondFragment(check))
                {
                    Assert.Inconclusive($"A conclusion {axiom.GetType().Name} refutation lies beyond the tableau's fragment.");
                }

                ModuleVerdict verdict = Decide(engine, premise, check);
                if(verdict.IsConsistent)
                {
                    AbstainWhenBeyondFragment(verdict);
                    Assert.Fail($"{testCase.Identifier}: a conclusion {axiom.GetType().Name} does not follow from the premise.");
                }
            }
        }
    }

    /// <summary>
    /// The negative arm: the non-conclusion must not be entailed, which one
    /// logical axiom with a cleanly satisfiable refutation check witnesses.
    /// An axiom whose checks are all unsatisfiable is entailed; if every
    /// axiom is entailed the test fails, and a walk that could not settle
    /// any axiom either way abstains.
    /// </summary>
    /// <param name="engine">The engine to decide through.</param>
    /// <param name="testCase">The test case.</param>
    /// <param name="premise">The mapped premise.</param>
    /// <param name="nonConclusionQuads">The non-conclusion document's triples.</param>
    private void AssertNotEntailed(DirectEngine engine, Owl2TestCase testCase, OwlOntologyDocument premise, List<Quad> nonConclusionQuads)
    {
        OwlOntologyDocument nonConclusion = OwlRdfMapper.Map(nonConclusionQuads);
        bool isUndecided = false;

        foreach(OwlAxiom axiom in nonConclusion.Axioms)
        {
            if(IsVacuous(axiom))
            {
                continue;
            }

            if(Refutations(axiom) is not List<OwlClassAssertionAxiom> checks)
            {
                isUndecided = true;

                continue;
            }

            foreach(OwlClassAssertionAxiom check in checks)
            {
                if(IsBeyondFragment(check))
                {
                    isUndecided = true;

                    break;
                }

                ModuleVerdict verdict = Decide(engine, premise, check);
                if(!verdict.IsConsistent)
                {
                    continue;
                }

                if(verdict.UnsupportedConstructs.Count > 0)
                {
                    isUndecided = true;

                    break;
                }

                //A cleanly satisfiable counterexample: this axiom is not
                //entailed, so the non-conclusion does not follow.
                return;
            }
        }

        if(isUndecided)
        {
            Assert.Inconclusive("No non-conclusion axiom settled either way within the tableau's fragment.");
        }

        Assert.Fail($"{testCase.Identifier}: the non-conclusion follows from the premise but must not.");
    }

    /// <summary>Decides the premise's axioms, optionally extended by one refutation assertion, as one module through the selected engine's consistency-only entry.</summary>
    /// <param name="engine">The engine to decide through.</param>
    /// <param name="premise">The mapped premise.</param>
    /// <param name="refutation">The counterexample assertion to add, or <c>null</c> for the premise alone.</param>
    /// <returns>The tableau verdict.</returns>
    private ModuleVerdict Decide(DirectEngine engine, OwlOntologyDocument premise, OwlClassAssertionAxiom? refutation)
    {
        List<OwlAxiom> axioms = new(premise.Axioms.Length + 1);
        axioms.AddRange(premise.Axioms);
        if(refutation is not null)
        {
            axioms.Add(refutation);
        }

        ReasoningModule module = new(axioms, Violations: []);

        return engine switch
        {
            DirectEngine.SatBacked => SatTableauModuleReasoner.DecideConsistency(module, cancellationToken: TestContext.CancellationToken),
            _ => AlcModuleReasoner.DecideConsistency(module, TestContext.CancellationToken),
        };
    }

    /// <summary>Whether a synthesized refutation assertion falls outside the tableau's fragment — its corpus-drawn expression carries a construct the calculus does not interpret.</summary>
    /// <param name="check">The refutation assertion.</param>
    /// <returns><c>true</c> when the check cannot be decided whole.</returns>
    private static bool IsBeyondFragment(OwlClassAssertionAxiom check)
    {
        return AlcModuleReasoner.Survey(new ReasoningModule([check], Violations: [])).Count > 0;
    }

    /// <summary>Abstains, naming the beyond-fragment constructs, when the verdict is fragment-relative rather than whole-module.</summary>
    /// <param name="verdict">The verdict to inspect.</param>
    private static void AbstainWhenBeyondFragment(ModuleVerdict verdict)
    {
        if(verdict.UnsupportedConstructs.Count > 0)
        {
            Assert.Inconclusive($"Fragment-relative verdict; beyond the tableau: {string.Join(", ", verdict.UnsupportedConstructs.Distinct())}.");
        }
    }

    /// <summary>An axiom with no logical content under the Direct Semantics: declarations and the annotation family.</summary>
    /// <param name="axiom">The axiom to classify.</param>
    /// <returns><c>true</c> when the axiom asserts nothing logical.</returns>
    private static bool IsVacuous(OwlAxiom axiom)
    {
        return axiom is OwlDeclarationAxiom
            or OwlAnnotationAssertionAxiom
            or OwlSubAnnotationPropertyOfAxiom
            or OwlAnnotationPropertyDomainAxiom
            or OwlAnnotationPropertyRangeAxiom;
    }

    /// <summary>
    /// The refutation checks of a conclusion axiom: counterexample concepts
    /// asserted on the witness (or, for a class assertion, the complement on
    /// the asserted individual itself), all of which must be unsatisfiable
    /// alongside the premise for the axiom to be entailed. <c>null</c> when
    /// the axiom kind has no refutation encoding in the tableau's fragment —
    /// role axioms, property assertions, individual (in)equality, data
    /// axioms, keys, and anonymous-individual assertions, whose refutation
    /// needs constructs the concept language lacks.
    /// </summary>
    /// <param name="axiom">The conclusion axiom.</param>
    /// <returns>The checks, or <c>null</c> when not encodable.</returns>
    private static List<OwlClassAssertionAxiom>? Refutations(OwlAxiom axiom)
    {
        return axiom switch
        {
            OwlSubClassOfAxiom sub =>
                [Counterexample(Overlap(sub.SubClass, new OwlObjectComplementOf(sub.SuperClass)))],
            OwlEquivalentClassesAxiom eq =>
            [
                Counterexample(Overlap(eq.First, new OwlObjectComplementOf(eq.Second))),
                Counterexample(Overlap(eq.Second, new OwlObjectComplementOf(eq.First))),
            ],
            OwlDisjointClassesAxiom disjoint => PairwiseOverlaps(disjoint.Operands),
            OwlClassAssertionAxiom { Individual: NamedNode individual } assertion =>
                [Assertion(new OwlObjectComplementOf(assertion.Class), individual)],
            OwlObjectPropertyDomainAxiom domain =>
                [Counterexample(Overlap(new OwlObjectSomeValuesFrom(domain.Property, ThingReference), new OwlObjectComplementOf(domain.Domain)))],
            OwlObjectPropertyRangeAxiom range =>
                [Counterexample(new OwlObjectComplementOf(new OwlObjectAllValuesFrom(range.Property, range.Range)))],
            _ => null,
        };
    }

    /// <summary>The pairwise counterexamples of a disjointness: for each operand pair, an individual in both.</summary>
    /// <param name="operands">The mutually disjoint expressions.</param>
    /// <returns>One check per unordered pair.</returns>
    private static List<OwlClassAssertionAxiom> PairwiseOverlaps(IReadOnlyList<OwlClassExpression> operands)
    {
        List<OwlClassAssertionAxiom> checks = [];
        for(int first = 0; first < operands.Count; first++)
        {
            for(int second = first + 1; second < operands.Count; second++)
            {
                checks.Add(Counterexample(Overlap(operands[first], operands[second])));
            }
        }

        return checks;
    }

    /// <summary>The intersection of two class expressions.</summary>
    /// <param name="first">The first operand.</param>
    /// <param name="second">The second operand.</param>
    /// <returns>The intersection expression.</returns>
    private static OwlObjectIntersectionOf Overlap(OwlClassExpression first, OwlClassExpression second)
    {
        return new OwlObjectIntersectionOf([first, second]);
    }

    /// <summary>Asserts a counterexample concept on the fresh witness individual.</summary>
    /// <param name="expression">The counterexample concept.</param>
    /// <returns>The synthesized assertion.</returns>
    private static OwlClassAssertionAxiom Counterexample(OwlClassExpression expression)
    {
        return Assertion(expression, Witness);
    }

    /// <summary>Asserts a class expression on an individual with a synthesized origin.</summary>
    /// <param name="expression">The class expression.</param>
    /// <param name="individual">The individual node.</param>
    /// <returns>The synthesized assertion.</returns>
    private static OwlClassAssertionAxiom Assertion(OwlClassExpression expression, NamedNode individual)
    {
        return new OwlClassAssertionAxiom(expression, individual) { Origin = new Quad(Witness, Witness, Witness, Graph: null) };
    }

    /// <summary>Loads a document role as triples: RDF/XML parses directly; functional syntax reads into structural form and serialises through the forward RDF mapping. <c>null</c> when the role has no readable document.</summary>
    /// <param name="testCase">The test case the documents belong to.</param>
    /// <param name="rdfXml">The role's inline RDF/XML, or <c>null</c>.</param>
    /// <param name="functional">The role's inline functional syntax, or <c>null</c>.</param>
    /// <returns>The triples, or <c>null</c>.</returns>
    private static List<Quad>? LoadQuads(Owl2TestCase testCase, string? rdfXml, string? functional)
    {
        if(rdfXml is string xml)
        {
            return ParseDocument(testCase, xml);
        }

        if(functional is string text)
        {
            OwlOntologyDocument document = Lumoin.Veritas.Owl.Functional.OwlFunctionalSyntaxReader.Read(text);
            if(document.Diagnostics.HasErrors)
            {
                Assert.Inconclusive("A test document did not parse as functional syntax; the test cannot be set up.");
            }

            return OwlStructuralToRdf.ToQuads(document);
        }

        return null;
    }

    /// <summary>Parses an inline RDF/XML document against the test's base IRI, abstaining when it does not parse.</summary>
    /// <param name="testCase">The test case supplying the base IRI.</param>
    /// <param name="document">The inline RDF/XML.</param>
    /// <returns>The parsed triples.</returns>
    private static List<Quad> ParseDocument(Owl2TestCase testCase, string document)
    {
        DiagnosticBag diagnostics = new();
        List<Quad> quads = [.. RdfXmlReader.Read(Encoding.UTF8.GetBytes(document), diagnostics, baseIri: testCase.Uri.AbsoluteUri)];
        if(diagnostics.HasErrors)
        {
            Assert.Inconclusive("A test document did not parse as RDF/XML; the test cannot be set up.");
        }

        return quads;
    }
}
