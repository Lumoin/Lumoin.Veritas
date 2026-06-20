using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Runs the Direct-Semantics, DL-species arms of the W3C OWL 2 conformance
/// corpus through the description-logic tableau: consistency and
/// inconsistency verdicts directly, and positive/negative entailment by
/// refutation — a conclusion axiom holds exactly when its counterexample
/// concept, asserted on a fresh witness individual alongside the premise,
/// is unsatisfiable.
/// </summary>
/// <remarks>
/// <para>
/// Applicability is enforced at the data source
/// (<see cref="Owl2TestRemit.DirectSemanticsDl"/>): only entailment and
/// consistency tests stated for the Direct Semantics over DL-species
/// documents, and not the RL rules runner's, ever materialise here — the
/// out-of-remit cases are filtered before the row exists, so this arm makes no
/// claim on them and reports no skip for them.
/// </para>
/// <para>
/// Within remit every case decides, passes a pinned capability boundary, or
/// fails. The tableau's reach is bounded — it interprets the ALC fragment, and
/// the corpus reaches well beyond it — so the cases it cannot decide are an
/// asserted census, not an inconclusive verdict: <see cref="FragmentGaps"/>
/// (premises naming beyond-fragment constructs, which abstain at the survey
/// gate before any search), <see cref="SnapshotPracticalReachGaps"/> (in
/// fragment but beyond the snapshot engine's practical reach), and
/// <see cref="RefutationGaps"/> (conclusion axioms with no refutation encoding
/// in the fragment, refutations that fall outside it, a non-conclusion no
/// axiom settles, or a premise that does not map). A case entering or leaving
/// a census is a visible test change: a beyond-fragment premise that is not
/// pinned (or a pinned one now inside the fragment) fails the run, as does a
/// conclusion gap that is not pinned.
/// </para>
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

    /// <summary>
    /// The in-remit premises whose axioms name constructs beyond the tableau's
    /// ALC fragment, so the arm abstains at the survey gate before any
    /// satisfiability search: its verdict could only ever be fragment-relative
    /// while the search cost is unbounded and the outcome foreclosed. This is
    /// the measured fragment boundary of the corpus, engine-independent (both
    /// engines share the fragment); a premise that enters it without a pin, or
    /// a pinned premise now inside the fragment, fails the run.
    /// </summary>
    private static HashSet<string> FragmentGaps { get; } =
    [
        "Consistent Datatype restrictions with Different Types",
        "Consistent owl:real range with DataOneOf",
        "Consistent-but-all-unsat",
        "Consistent-dateTime",
        "Contradicting datatype Restrictions",
        "Contradicting-dateTime-restrictions",
        "Datatype-DataComplementOf-001",
        "Datatype-Float-Discrete-001",
        "Different types in Datatype Restrictions and Complement",
        "Inconsistent Byte Filler",
        "Inconsistent Data Complement with the Restrictions",
        "Inconsistent Disjoint Dataproperties",
        "Inconsistent String Pattern with Disjoint Dataproperties",
        "Minus Infinity is not in owl:real",
        "New-Feature-BottomDataProperty-001",
        "New-Feature-BottomObjectProperty-001",
        "New-Feature-DataQCR-001",
        "New-Feature-DisjointUnion-001",
        "New-Feature-Keys-001",
        "New-Feature-Keys-002",
        "New-Feature-Keys-007",
        "New-Feature-Rational-001",
        "New-Feature-SelfRestriction-001",
        "New-Feature-TopObjectProperty-001",
        "Plus and Minus Zero Integer",
        "Qualified-cardinality-boolean",
        "Qualified-cardinality-restricted-int",
        "WebOnt-I4.5-001",
        "WebOnt-I4.5-002",
        "WebOnt-I5.2-001",
        "WebOnt-I5.2-002",
        "WebOnt-I5.2-003",
        "WebOnt-I5.2-004",
        "WebOnt-I5.2-005",
        "WebOnt-I5.2-006",
        "WebOnt-I5.21-002",
        "WebOnt-I5.3-008",
        "WebOnt-I5.8-010",
        "WebOnt-Restriction-003",
        "WebOnt-Restriction-004",
        "WebOnt-Restriction-005-direct",
        "WebOnt-SymmetricProperty-002",
        "WebOnt-Thing-004",
        "WebOnt-TransitiveProperty-002",
        "WebOnt-cardinality-001",
        "WebOnt-cardinality-002",
        "WebOnt-cardinality-003",
        "WebOnt-cardinality-004",
        "WebOnt-description-logic-003",
        "WebOnt-description-logic-004",
        "WebOnt-description-logic-005",
        "WebOnt-description-logic-006",
        "WebOnt-description-logic-007",
        "WebOnt-description-logic-008",
        "WebOnt-description-logic-009",
        "WebOnt-description-logic-010",
        "WebOnt-description-logic-011",
        "WebOnt-description-logic-012",
        "WebOnt-description-logic-013",
        "WebOnt-description-logic-014",
        "WebOnt-description-logic-015",
        "WebOnt-description-logic-016",
        "WebOnt-description-logic-017",
        "WebOnt-description-logic-018",
        "WebOnt-description-logic-019",
        "WebOnt-description-logic-020",
        "WebOnt-description-logic-021",
        "WebOnt-description-logic-022",
        "WebOnt-description-logic-023",
        "WebOnt-description-logic-024",
        "WebOnt-description-logic-025",
        "WebOnt-description-logic-026",
        "WebOnt-description-logic-027",
        "WebOnt-description-logic-028",
        "WebOnt-description-logic-029",
        "WebOnt-description-logic-030",
        "WebOnt-description-logic-031",
        "WebOnt-description-logic-032",
        "WebOnt-description-logic-033",
        "WebOnt-description-logic-034",
        "WebOnt-description-logic-035",
        "WebOnt-description-logic-105",
        "WebOnt-description-logic-106",
        "WebOnt-description-logic-107",
        "WebOnt-description-logic-108",
        "WebOnt-description-logic-109",
        "WebOnt-description-logic-111",
        "WebOnt-description-logic-501",
        "WebOnt-description-logic-502",
        "WebOnt-description-logic-601",
        "WebOnt-description-logic-602",
        "WebOnt-description-logic-603",
        "WebOnt-description-logic-604",
        "WebOnt-description-logic-605",
        "WebOnt-description-logic-606",
        "WebOnt-description-logic-608",
        "WebOnt-description-logic-609",
        "WebOnt-description-logic-610",
        "WebOnt-description-logic-611",
        "WebOnt-description-logic-612",
        "WebOnt-description-logic-613",
        "WebOnt-description-logic-614",
        "WebOnt-description-logic-615",
        "WebOnt-description-logic-616",
        "WebOnt-description-logic-617",
        "WebOnt-description-logic-623",
        "WebOnt-description-logic-624",
        "WebOnt-description-logic-625",
        "WebOnt-description-logic-626",
        "WebOnt-description-logic-627",
        "WebOnt-description-logic-628",
        "WebOnt-description-logic-629",
        "WebOnt-description-logic-630",
        "WebOnt-description-logic-631",
        "WebOnt-description-logic-632",
        "WebOnt-description-logic-633",
        "WebOnt-description-logic-634",
        "WebOnt-description-logic-641",
        "WebOnt-description-logic-642",
        "WebOnt-description-logic-643",
        "WebOnt-description-logic-644",
        "WebOnt-description-logic-646",
        "WebOnt-description-logic-650",
        "WebOnt-description-logic-661",
        "WebOnt-description-logic-665",
        "WebOnt-description-logic-667",
        "WebOnt-description-logic-905",
        "WebOnt-description-logic-908",
        "WebOnt-equivalentClass-004",
        "WebOnt-equivalentClass-005",
        "WebOnt-equivalentClass-009",
        "WebOnt-equivalentProperty-001",
        "WebOnt-equivalentProperty-004",
        "WebOnt-maxCardinality-001",
        "WebOnt-miscellaneous-001",
        "WebOnt-miscellaneous-002",
        "WebOnt-miscellaneous-202",
        "WebOnt-miscellaneous-203",
        "WebOnt-miscellaneous-204",
        "WebOnt-oneOf-001",
        "WebOnt-oneOf-004",
        "WebOnt-unionOf-003",
        "WebOnt-unionOf-004",
        "consistent-dataproperty-disjointness",
        "consistent-integer-filler",
        "datatype-restriction-min-max-inconsistency",
        "inconsistent-integer-filler",
        "inconsistent_datatypes",
        "one=two",
        "owl2-rl-invalid-leftside-maxcard",
        "owl2-rl-invalid-oneof",
        "owl2-rl-invalid-owlreal",
        "owl2-rl-valid-mincard",
    ];

    /// <summary>
    /// In-fragment cases whose conclusion the tableau cannot refute within the
    /// concept language, paired with the reason: a conclusion axiom kind with
    /// no refutation encoding (role/data axioms, property assertions,
    /// individual (in)equality, keys), a synthesized refutation that itself
    /// names beyond-fragment constructs, a non-conclusion no axiom settles
    /// either way, or a premise that does not map to structural form. A case
    /// hitting one of these without a pin fails the run so the entry is added.
    /// </summary>
    private static Dictionary<string, string> RefutationGaps { get; } = new(StringComparer.Ordinal)
    {
        ["New-Feature-ObjectQCR-001"] = "a conclusion refutation lies beyond the tableau's fragment",
        ["New-Feature-SelfRestriction-002"] = "a conclusion refutation lies beyond the tableau's fragment",
        ["WebOnt-I5.26-009"] = "a conclusion refutation lies beyond the tableau's fragment",
        ["WebOnt-description-logic-901"] = "a conclusion refutation lies beyond the tableau's fragment",
        ["WebOnt-AnnotationProperty-002"] = "a conclusion axiom has no refutation encoding in the tableau's fragment",
        ["WebOnt-disjointWith-001"] = "a conclusion axiom has no refutation encoding in the tableau's fragment",
        ["WebOnt-someValuesFrom-003"] = "a conclusion axiom has no refutation encoding in the tableau's fragment",
        ["somevaluesfrom2bnode"] = "a conclusion axiom has no refutation encoding in the tableau's fragment",
        ["WebOnt-allValuesFrom-002"] = "no non-conclusion axiom settles either way within the fragment",
        ["WebOnt-description-logic-902"] = "no non-conclusion axiom settles either way within the fragment",
        ["New-Feature-Rational-002"] = "the premise does not map to structural form",
        ["New-Feature-Rational-003"] = "the premise does not map to structural form",
    };

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
    [Owl2ManifestData("approved", "all.rdf", Owl2TestRemit.DirectSemanticsDl)]
    public void RunApproved(Owl2TestCase testCase)
    {
        RunAndAssert(DirectEngine.Snapshot, testCase);
    }

    /// <summary>Runs one proposed-status test case through the snapshot tableau — the reference baseline.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("proposed", "all.rdf", Owl2TestRemit.DirectSemanticsDl)]
    public void RunProposed(Owl2TestCase testCase)
    {
        RunAndAssert(DirectEngine.Snapshot, testCase);
    }

    /// <summary>Runs one approved-status test case through the SAT-backed sibling, the parametrized engine arm.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("approved", "all.rdf", Owl2TestRemit.DirectSemanticsDl)]
    public void RunApprovedSatBacked(Owl2TestCase testCase)
    {
        RunAndAssert(DirectEngine.SatBacked, testCase);
    }

    /// <summary>Runs one proposed-status test case through the SAT-backed sibling, the parametrized engine arm.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("proposed", "all.rdf", Owl2TestRemit.DirectSemanticsDl)]
    public void RunProposedSatBacked(Owl2TestCase testCase)
    {
        RunAndAssert(DirectEngine.SatBacked, testCase);
    }

    /// <summary>Loads and maps the test documents and dispatches the test's kinds to the tableau arms through the selected engine; capability boundaries pass through the pinned census, everything else decides.</summary>
    /// <param name="engine">The engine to decide through.</param>
    /// <param name="testCase">The test case.</param>
    private void RunAndAssert(DirectEngine engine, Owl2TestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        //Applicability — entailment/consistency kind, Direct Semantics, DL
        //species, not RL-marked — is enforced at the data source
        //(Owl2TestRemit.DirectSemanticsDl), so every case reaching here is the
        //tableau's to decide.
        bool isPositive = testCase.Kinds.Contains("PositiveEntailmentTest");
        bool isNegative = testCase.Kinds.Contains("NegativeEntailmentTest");
        bool isInconsistency = testCase.Kinds.Contains("InconsistencyTest");
        bool isConsistency = testCase.Kinds.Contains("ConsistencyTest");

        if(IsPracticalReachGap(engine, testCase.Identifier))
        {
            //Pinned, engine-relative gap: in fragment but beyond the snapshot
            //engine's practical reach. The SAT-backed sibling decides it.
            return;
        }

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
        OwlOntologyDocument premise = OwlRdfMapper.Map(premiseQuads);
        if(premise.Diagnostics.HasErrors)
        {
            AssertKnownGap(testCase, "the premise does not map to structural form");

            return;
        }

        //The scope gate: a beyond-fragment premise forecloses every arm's
        //decidable outcome, so it abstains before any tableau search runs.
        IReadOnlyList<string> remainder = AlcModuleReasoner.Survey(new ReasoningModule([.. premise.Axioms], Violations: []));
        if(AssertFragmentBoundary(testCase, beyondFragment: remainder.Count > 0))
        {
            return;
        }

        if(isInconsistency)
        {
            ModuleVerdict verdict = Decide(engine, premise, refutation: null);
            if(verdict.IsConsistent)
            {
                if(AbstainWhenBeyondFragment(testCase, verdict))
                {
                    return;
                }

                Assert.Fail($"{testCase.Identifier}: the tableau should find the premise inconsistent.");
            }

            return;
        }

        if(isConsistency)
        {
            ModuleVerdict verdict = Decide(engine, premise, refutation: null);

            Assert.IsTrue(verdict.IsConsistent, $"{testCase.Identifier}: the premise is consistent, but the tableau found a clash.");
            if(AbstainWhenBeyondFragment(testCase, verdict))
            {
                return;
            }
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
    /// non-entailment and fails; one resting on beyond-fragment axioms is a
    /// pinned refutation gap.
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
                AssertKnownGap(testCase, $"a conclusion {axiom.GetType().Name} has no refutation encoding in the tableau's fragment");

                return;
            }

            foreach(OwlClassAssertionAxiom check in checks)
            {
                if(IsBeyondFragment(check))
                {
                    AssertKnownGap(testCase, $"a conclusion {axiom.GetType().Name} refutation lies beyond the tableau's fragment");

                    return;
                }

                ModuleVerdict verdict = Decide(engine, premise, check);
                if(verdict.IsConsistent)
                {
                    if(AbstainWhenBeyondFragment(testCase, verdict))
                    {
                        return;
                    }

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
    /// any axiom either way is a pinned refutation gap.
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
            AssertKnownGap(testCase, "no non-conclusion axiom settles either way within the tableau's fragment");

            return;
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

    /// <summary>
    /// Resolves a beyond-fragment premise against the pinned
    /// <see cref="FragmentGaps"/> census: a pinned premise that is beyond the
    /// fragment passes (returns <c>true</c> so the caller abstains); an
    /// unpinned beyond-fragment premise, or a pinned premise now inside the
    /// fragment, fails so the census stays exact.
    /// </summary>
    /// <param name="testCase">The test case.</param>
    /// <param name="beyondFragment">Whether the survey found beyond-fragment constructs in the premise.</param>
    /// <returns><c>true</c> when the premise is a pinned fragment gap and the caller should abstain.</returns>
    private static bool AssertFragmentBoundary(Owl2TestCase testCase, bool beyondFragment)
    {
        bool pinned = FragmentGaps.Contains(testCase.Identifier);
        if(beyondFragment)
        {
            if(!pinned)
            {
                Assert.Fail($"{testCase.Identifier}: the premise lies beyond the tableau's fragment but is not pinned; add it to FragmentGaps.");
            }

            return true;
        }

        if(pinned)
        {
            Assert.Fail($"{testCase.Identifier}: pinned as a fragment gap but the premise now lies inside the tableau's fragment; remove it from FragmentGaps.");
        }

        return false;
    }

    /// <summary>Resolves a fragment-relative verdict against the pinned <see cref="RefutationGaps"/> census: a pinned case abstains (returns <c>true</c>), an unpinned one fails.</summary>
    /// <param name="testCase">The test case.</param>
    /// <param name="verdict">The verdict to inspect.</param>
    /// <returns><c>true</c> when the verdict is fragment-relative and the caller should abstain.</returns>
    private static bool AbstainWhenBeyondFragment(Owl2TestCase testCase, ModuleVerdict verdict)
    {
        if(verdict.UnsupportedConstructs.Count > 0)
        {
            AssertKnownGap(testCase, $"fragment-relative verdict; beyond the tableau: {string.Join(", ", verdict.UnsupportedConstructs.Distinct())}");

            return true;
        }

        return false;
    }

    /// <summary>Resolves a conclusion-level or setup-level capability boundary against the pinned <see cref="RefutationGaps"/> census: a pinned case passes, an unpinned one fails so the entry is added.</summary>
    /// <param name="testCase">The test case.</param>
    /// <param name="reason">The boundary the case hit, for the failure message when it is not pinned.</param>
    private static void AssertKnownGap(Owl2TestCase testCase, string reason)
    {
        if(RefutationGaps.ContainsKey(testCase.Identifier))
        {
            return;
        }

        Assert.Fail($"{testCase.Identifier}: unpinned capability gap ({reason}); extend the tableau or pin it in RefutationGaps.");
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
            Assert.IsFalse(document.Diagnostics.HasErrors, $"{testCase.Identifier}: a test document did not parse as functional syntax; the test cannot be set up.");

            return OwlStructuralToRdf.ToQuads(document);
        }

        return null;
    }

    /// <summary>Parses an inline RDF/XML document against the test's base IRI, failing when it does not parse — an unparseable corpus document is a setup failure, not a skip.</summary>
    /// <param name="testCase">The test case supplying the base IRI.</param>
    /// <param name="document">The inline RDF/XML.</param>
    /// <returns>The parsed triples.</returns>
    private static List<Quad> ParseDocument(Owl2TestCase testCase, string document)
    {
        DiagnosticBag diagnostics = new();
        List<Quad> quads = [.. RdfXmlReader.Read(Encoding.UTF8.GetBytes(document), diagnostics, baseIri: testCase.Uri.AbsoluteUri)];
        Assert.IsFalse(diagnostics.HasErrors, $"{testCase.Identifier}: a test document did not parse as RDF/XML; the test cannot be set up.");

        return quads;
    }
}
