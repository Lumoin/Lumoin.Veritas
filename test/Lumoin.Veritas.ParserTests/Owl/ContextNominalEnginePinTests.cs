using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Functional;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The nominal below-gate pin battery: each pin drives the ALCHOIQ
/// nominal machinery of the consequence-based context calculus
/// (<see href="https://arxiv.org/abs/1805.01396"/>) through the engine core
/// DIRECTLY — clausifier to engine, below the module survey and below the
/// reasoner's second gate — an independence instrument, so a half-migrated
/// nominal rule (Join, r-Succ, r-Pred, Nom), a broken partial term order, a
/// mis-lowered <c>ObjectOneOf</c> / <c>ObjectHasValue</c> clause, or a corrupt
/// generated-nominal channel fails HERE even where the production gates would
/// mask it. The pins
/// exercise the enumeration read-off, the enumeration exhaustion
/// clash, the fresh-singleton <c>N_o</c> normal form, the root edge exchange,
/// the general-path merge counting, the generated-nominal habitat, Factor at
/// enumeration heads, answer-neutrality on mixed heads, the root data demand
/// latch (including its GALEN-Heart raw-engine census probe), the root
/// ground-loop bridge, the packed-width delegation constant, the packed total
/// order, and the disjunctive data lane (refutation, survivor certification,
/// honest abstention, the covering-configuration latch, and the
/// marker-position belt).
/// Statistics are read through <see cref="ContextSaturationEngine.BuildStatistics"/>.
/// </summary>
[TestClass]
internal sealed class ContextNominalEnginePinTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/tier3stageA#";

    /// <summary>The inference ceiling the GALEN-Heart cell pin rides — a deterministic wall-free budget deep inside the measured churn regime (Release probes exhaust 20M in 46s and 100M in 8m10s with 97+ percent redundant conclusions and superlinear r-Pred growth, so no practical ceiling closes the fixpoint): large enough that the abstention attribution is stable, small enough for the suite.</summary>
    private const int GalenHeartInferenceCeiling = 2_000_000;

    /// <summary>The enumeration read-off: <c>A ⊑ {o}</c> lowers to <c>A(x) → x ≈ o</c> and <c>{o} ⊑ B</c> to the DL7 fact <c>⊤ → B(o)</c>; because <c>x</c> is INCOMPARABLE to the constant <c>o</c>, the Eq rule reads <c>o</c> as the sole rewrite source and rewrites <c>B(o)</c> toward <c>B(x)</c> inside A's query context, so <c>A ⊑ B</c> reads off. The DL7 fact is an empty-body ontology clause that fires everywhere, so the distinguished root context exists and holds clauses.</summary>
    [TestMethod]
    public void Dl81ReadOffSubsumesThroughUnorientedConstantEquality()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "A",
            [
                SubClassOf(Class("A"), OneOf("o")),
                SubClassOf(OneOf("o"), Class("B")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "A"), Atom(clausification, "B")), "The enumeration read-off entails A is a B: A's element equals the constant o and the DL7 fact makes o a B, so o rewrites toward x precisely because x is incomparable to o.");
        Assert.IsFalse(engine.IsInconsistent, "The enumeration read-off module is consistent.");
        Assert.IsGreaterThan(0, engine.BuildStatistics(contextDecided: true).RootContextClauses, "The distinguished root context exists and holds clauses once the module interns the constant o and seeds the DL7 fact.");
    }

    /// <summary>Enum-1 exhaustion clash: <c>B ⊑ {o1, o2}</c> forces every B-instance onto o1 or o2, but the ABox individual i is asserted a B and declared different from both, so the enumeration has nowhere to land. This drives ABox root seeding, the disjunctive <c>x ≈ o</c> heads, and the Eq/Ineq reasoning over constants; the clash lands as the root-context empty clause.</summary>
    [TestMethod]
    public void Enum1ExhaustionClashesOnDistinctMembers()
    {
        ContextSaturationEngine engine = Saturate(
            [
                SubClassOf(Class("B"), OneOf("o1", "o2")),
                ClassAssertion(Class("B"), Individual("i")),
                Different("i", "o1"),
                Different("i", "o2"),
            ],
            out _);

        ContextSaturationStatistics statistics = engine.BuildStatistics(contextDecided: true);
        Assert.IsTrue(engine.IsInconsistent, "The enumeration collapse is unsatisfiable: i must equal o1 or o2 yet differs from both.");
        Assert.IsGreaterThan(0L, statistics.JoinApplications + statistics.EqApplications, $"The enumeration exhaustion drives the ground equality machinery (observed Join={statistics.JoinApplications}, Eq={statistics.EqApplications}).");
    }

    /// <summary>Enum-2 control: the same enumeration WITHOUT the distinctness declarations stays satisfiable — with no unique-name assumption i simply collapses onto a member of the enumeration.</summary>
    [TestMethod]
    public void Enum2CollapseIsConsistentWithoutDistinctness()
    {
        ContextSaturationEngine engine = Saturate(
            [
                SubClassOf(Class("B"), OneOf("o1", "o2")),
                ClassAssertion(Class("B"), Individual("i")),
            ],
            out _);

        Assert.IsFalse(engine.IsInconsistent, "Absent distinctness, the enumerated individual collapses onto a member and the module is consistent.");
    }

    /// <summary>Hval-1 fresh-singleton chain: <c>A ⊑ ∃r.{o}</c> lowers through the fresh singleton <c>N_o</c> (<c>A(x) → r(x, f(x))</c>, <c>A(x) → N_o(f(x))</c>, and the DL7/DL8 pair defining <c>N_o</c>), and <c>{o} ⊑ C</c> types o a C. A's r-successor is o and o is a C, but A itself is not a C — an answer-neutrality face — while the <c>∃r.N_o</c> witness expands through Succ.</summary>
    [TestMethod]
    public void Hval1HasValueChainsThroughSingleton()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "A",
            [
                SubClassOf(Class("A"), HasValue("r", "o")),
                SubClassOf(OneOf("o"), Class("C")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        ContextSaturationStatistics statistics = engine.BuildStatistics(contextDecided: true);
        Assert.IsFalse(engine.IsInconsistent, "The individual-value chain is consistent.");
        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "A"), Atom(clausification, "C")), "A is not a C: only A's r-successor is the C-typed constant o, which is answer-neutral for A itself.");
        Assert.IsGreaterThan(0L, statistics.SuccApplications, $"The existential over the fresh singleton expands its witness through Succ (observed Succ={statistics.SuccApplications}).");
    }

    /// <summary>Root-1 root edge exchange: the Hval-1 module extended with <c>C ⊑ E</c>. A's query context derives the role literal <c>r(x, f(x))</c> and the singleton equality <c>f(x) ≈ o</c>; the Eq rewrite rewrites the function witness toward the constant, yielding <c>S(x, o)</c> — a root successor trigger — so r-Succ opens the root edge <c>⟨A-context, vr, o⟩</c> and seeds its tautology. The pin asserts the root exchange fired and a root edge opened.</summary>
    [TestMethod]
    public void Root1SuccOpensEdgeFromDerivedGroundRole()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "A",
            [
                SubClassOf(Class("A"), HasValue("r", "o")),
                SubClassOf(OneOf("o"), Class("C")),
                SubClassOf(Class("C"), Class("E")),
                Bystander(),
            ],
            out _);

        ContextSaturationStatistics statistics = engine.BuildStatistics(contextDecided: true);
        Assert.IsGreaterThan(0L, statistics.RootSuccApplications, $"The derived ground role literal S(x, o) opens the root exchange through r-Succ (observed RootSucc={statistics.RootSuccApplications}).");
        Assert.IsGreaterThan(0, statistics.RootEdges, $"A nominal-labelled root edge opens for the constant o (observed RootEdges={statistics.RootEdges}).");
    }

    /// <summary>Pign-1 merge counting: an inert nominal mention gives the module nominal jurisdiction, so B(b) and b's two r-edges route through the root context as constants; the told <c>≤1 r</c> merges o1 and o2, so the module is consistent. The nominal jurisdiction bypasses the ground-context slice whole (no ground contexts created) while the root context holds clauses.</summary>
    [TestMethod]
    public void Pign1MergeChoiceCountingDecides()
    {
        ContextSaturationEngine engine = Saturate(
            [
                SubClassOf(Class("B"), Max("r", 1, null)),
                ClassAssertion(Class("B"), Individual("b")),
                Edge("b", "r", "o1"),
                Edge("b", "r", "o2"),
                SubClassOf(Class("Z"), OneOf("zed")),
            ],
            out _);

        ContextSaturationStatistics statistics = engine.BuildStatistics(contextDecided: true);
        Assert.IsFalse(engine.IsInconsistent, "The told at-most-one merges b's two successors, so the module is consistent.");
        Assert.AreEqual(0, statistics.GroundContextsCreated, "The nominal jurisdiction routes the entire ABox through the root context, so the ground-context slice is bypassed whole.");
        Assert.IsGreaterThan(0, statistics.RootContextClauses, "The module took the root path: the distinguished root context holds clauses.");
    }

    /// <summary>Pign-2 pigeonhole clash: the Pign-1 module plus a distinctness declaration between the two successors — the merge is now forbidden, so at-most-one against two distinct successors is a pigeonhole that refutes through the general root path (not the clausifier rider). The inert nominal mention keeps the module in nominal jurisdiction.</summary>
    [TestMethod]
    public void Pign2DistinctSuccessorsRefuteThroughGeneralPath()
    {
        ContextSaturationEngine engine = Saturate(
            [
                SubClassOf(Class("B"), Max("r", 1, null)),
                ClassAssertion(Class("B"), Individual("b")),
                Edge("b", "r", "o1"),
                Edge("b", "r", "o2"),
                Different("o1", "o2"),
                SubClassOf(Class("Z"), OneOf("zed")),
            ],
            out _);

        Assert.IsTrue(engine.IsInconsistent, "At-most-one against two forced distinct successors is a pigeonhole that refutes through the general vr path.");
    }

    /// <summary>
    /// Nom-1 the generated-nominal habitat: an ANONYMOUS element (the existential
    /// witness of <c>B ⊑ ∃s.A</c> in B's query context) reaches the counted nominal —
    /// <c>A ⊑ ∃r.{o}</c> puts an r-edge from the anonymous A-element to o, and
    /// <c>{o} ⊑ ≤1 r⁻</c> counts o's r-predecessors. The anonymous predecessor is
    /// represented in the root context by the context variable <c>y</c> (the r-Succ
    /// seed), so the counting clause's head instantiates to the <c>y ≈ y</c> /
    /// <c>y ≈ f(o)</c> tail forms that trigger the Nom rule: the anonymous element
    /// must be one of the at most K named successors, and the rule MINTS the
    /// generated-nominal siblings to name them. Named-predecessor variants (told
    /// ABox sources) never trigger Nom — their merges are ground and discharge
    /// through Eq and r-Pred instead — which is exactly the co-occurrence trigger
    /// fact: Nom needs inverse roles, nominals, counting, AND an anonymous element.
    /// </summary>
    [TestMethod]
    public void Nom1AnonymousPredecessorHabitatMintsGeneratedNominals()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "B",
            [
                Inverse("r", "rInv"),
                SubClassOf(Class("B"), Some("s", Class("A"))),
                SubClassOf(Class("A"), HasValue("r", "o")),
                SubClassOf(OneOf("o"), MaxInverse("r", 1, null)),
                Bystander(),
            ],
            out _);

        ContextSaturationStatistics statistics = engine.BuildStatistics(contextDecided: true);
        Assert.IsFalse(engine.IsInconsistent, "The anonymous predecessor folds onto a generated successor of o, so the module is consistent.");
        Assert.IsGreaterThan(0L, statistics.NomApplications, $"The anonymous-predecessor habitat fires the Nom rule (observed Nom={statistics.NomApplications}, Generated={statistics.GeneratedNominals}, RootPred={statistics.RootPredApplications}, RootSucc={statistics.RootSuccApplications}).");
        Assert.IsGreaterThan(0, statistics.GeneratedNominals, $"The Nom rule minted the K sibling nominals through the bounded channel (observed Generated={statistics.GeneratedNominals}, MaxLabelDepth={statistics.MaxNominalLabelDepth}).");
    }

    /// <summary>
    /// Fct-1 the Factor-live witness: in the QUERY context of B, Hyper instantiates
    /// <c>B ⊑ {o1, o2}</c> to the head <c>x ≈ o1 ∨ x ≈ o2</c>, whose disjuncts share
    /// the VARIABLE side <c>x</c> — a legal Factor shared side, because the other side
    /// is INCOMPARABLE to it (the published <c>t′ ⊁ s</c> holds through
    /// incomparability), so Factor derives <c>o1 ≉ o2 ∨ x ≈ o2</c>. Factor earns its
    /// keep at nominals exactly here; in the root context the instantiated
    /// <c>i ≈ o1 ∨ i ≈ o2</c> shares only the MINIMAL constant side, where
    /// <c>t′ ⊁ s</c> correctly bars factoring — both faces of the side condition.
    /// </summary>
    [TestMethod]
    public void Fct1FactorFiresOnQueryEnumerationHead()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "B",
            [
                SubClassOf(Class("B"), OneOf("o1", "o2")),
                ClassAssertion(Class("B"), Individual("i")),
                Bystander(),
            ],
            out _);

        ContextSaturationStatistics statistics = engine.BuildStatistics(contextDecided: true);
        Assert.IsFalse(engine.IsInconsistent, "The enumeration with one asserted member is consistent.");
        Assert.IsGreaterThan(0L, statistics.FactorApplications, $"Factor fires on the query enumeration head whose disjuncts share the variable side (observed Factor={statistics.FactorApplications}).");
    }

    /// <summary>
    /// Join-1 the ground-resolution witness: a nominal-jurisdiction module's
    /// negative property assertion seeds the ground-body clash form
    /// <c>r(a, b) → ⊥</c> into the root context, and the positive assertion seeds
    /// the fact <c>⊤ → r(a, b)</c>; the Join rule resolves the ground body literal
    /// against the ground maximal head — folding the conditional clash into the
    /// unconditional empty clause the simple inconsistency probe reads (the reader
    /// never evaluates bodies; eager Join does the folding).
    /// </summary>
    [TestMethod]
    public void Join1NegativeAssertionFoldsGroundClash()
    {
        ContextSaturationEngine engine = Saturate(
            [
                Edge("a", "r", "b"),
                NegativeEdge("a", "r", "b"),
                SubClassOf(Class("Z"), OneOf("zed")),
            ],
            out _);

        ContextSaturationStatistics statistics = engine.BuildStatistics(contextDecided: true);
        Assert.IsTrue(engine.IsInconsistent, "An asserted edge against its own negative assertion clashes, folded to the unconditional empty clause by Join.");
        Assert.IsGreaterThan(0L, statistics.JoinApplications, $"The Join rule resolved the ground body literal against the ground fact (observed Join={statistics.JoinApplications}).");
    }

    /// <summary>
    /// Join-1's consistent twin (the strengthen-first row for the
    /// reader-shortcut mutation, which every derived-clash fixture masked): a
    /// negative property assertion whose edge is NEVER asserted seeds only the
    /// CONDITIONAL clash form <c>r(a, b) → ⊥</c> into the root context — a
    /// body-bearing clause the simple probe must not consume. The module is
    /// consistent; a reader that condemned on an empty head alone would flip this
    /// face, so the probe provably reads only the genuinely empty clause eager
    /// Join folds when the premises actually meet.
    /// </summary>
    [TestMethod]
    public void Join2NegativeAssertionWithoutEdgeStaysConsistent()
    {
        ContextSaturationEngine engine = Saturate(
            [
                NegativeEdge("a", "r", "b"),
                SubClassOf(Class("Z"), OneOf("zed")),
            ],
            out _);

        Assert.IsFalse(engine.IsInconsistent, "A negative assertion with no matching asserted edge leaves the conditional clash unfired - the module is consistent, and the probe never reads a body-bearing clause as the empty clause.");
    }

    /// <summary>A negative object-property assertion in the example namespace.</summary>
    /// <param name="source">The source individual's local name.</param>
    /// <param name="role">The property's local name.</param>
    /// <param name="target">The target individual's local name.</param>
    /// <returns>The negative assertion.</returns>
    private static OwlNegativeObjectPropertyAssertionAxiom NegativeEdge(string source, string role, string target)
    {
        return new OwlNegativeObjectPropertyAssertionAxiom(Individual(source), Property(role), Individual(target)) { Origin = Origin($"negative-edge-{source}-{target}") };
    }

    /// <summary>
    /// Loop-1 the root ground-loop bridge, irreflexive face: <c>i ∈ {a}</c> folds
    /// <c>i ≈ a</c>, so the asserted <c>r(i, a)</c> Eq-rewrites into the constant
    /// self-edge <c>r(c, c)</c> of the merged representative; the bridge derives
    /// the loop concept <c>Self_r(c)</c>, and the irreflexivity clash
    /// <c>Self_r(x) → ⊥</c> condemns the module through the root Hyper. Without
    /// the bridge the derived constant loop reaches no loop consumer and the
    /// inconsistency is silently missed — the ELH-differential discovery this
    /// pin freezes.
    /// </summary>
    [TestMethod]
    public void Loop1NominalFoldSelfEdgeClashesOnIrreflexive()
    {
        ContextSaturationEngine engine = Saturate(
            [
                ClassAssertion(OneOf("a"), Individual("i")),
                Edge("i", "r", "a"),
                Irreflexive("r"),
            ],
            out _);

        Assert.IsTrue(engine.IsInconsistent, "The nominal fold makes the asserted edge a self-edge on an irreflexive role, so the module is inconsistent.");
    }

    /// <summary>Loop-2 control: the Loop-1 module WITHOUT the irreflexivity constraint stays satisfiable — the fold produces a harmless self-edge.</summary>
    [TestMethod]
    public void Loop2NominalFoldSelfEdgeConsistentWithoutConstraint()
    {
        ContextSaturationEngine engine = Saturate(
            [
                ClassAssertion(OneOf("a"), Individual("i")),
                Edge("i", "r", "a"),
            ],
            out _);

        Assert.IsFalse(engine.IsInconsistent, "Absent a loop constraint, the folded self-edge is consistent.");
    }

    /// <summary>Loop-3 the asymmetric face: <c>SameIndividual(i, a)</c> merges the asserted <c>r(i, a)</c> into the constant self-edge, which is its own reverse — the asymmetry clash's self-collapsed variant <c>Self_r(x) → ⊥</c> condemns it through the same bridge.</summary>
    [TestMethod]
    public void Loop3MergedSelfEdgeClashesOnAsymmetric()
    {
        ContextSaturationEngine engine = Saturate(
            [
                Same("i", "a"),
                Edge("i", "r", "a"),
                Asymmetric("r"),
                SubClassOf(Class("Z"), OneOf("zed")),
            ],
            out _);

        Assert.IsTrue(engine.IsInconsistent, "The merged self-edge violates asymmetry (a self-edge is its own reverse), so the module is inconsistent.");
    }

    /// <summary>An irreflexivity characteristic over a named role in the example namespace.</summary>
    /// <param name="role">The property's local name.</param>
    /// <returns>The characteristic axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Irreflexive(string role)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Irreflexive, Property(role)) { Origin = Origin($"irreflexive-{role}") };
    }

    /// <summary>An asymmetry characteristic over a named role in the example namespace.</summary>
    /// <param name="role">The property's local name.</param>
    /// <returns>The characteristic axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Asymmetric(string role)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Asymmetric, Property(role)) { Origin = Origin($"asymmetric-{role}") };
    }

    /// <summary>Neut-1 answer-neutrality: <c>A ⊑ {o1, o2}</c> derives the disjunctive head <c>A(x) → x ≈ o1 ∨ x ≈ o2</c>, which is not a subsumption to any named class — a derived mixed equality head decides no membership. The module is consistent.</summary>
    [TestMethod]
    public void Neut1MixedHeadIsNeverASubsumption()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "A",
            [
                SubClassOf(Class("A"), OneOf("o1", "o2")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "A"), Atom(clausification, "Spruce")), "A disjunctive enumeration head is not a subsumption to the first bystander class.");
        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "A"), Atom(clausification, "Willow")), "A disjunctive enumeration head is not a subsumption to the second bystander class.");
        Assert.IsFalse(engine.IsInconsistent, "The open enumeration is consistent.");
    }

    /// <summary>Grd-1 root data demand statistic: <c>{o} ⊑ ∃dp.integer</c> instantiates the data demand marker at the constant o, and a constant-instantiated demand landing on the root context is recorded in the arm's activity statistic <see cref="ContextSaturationEngine.RootDataDemandObserved"/> when the per-constant arm is off — this raw engine leaves it off, so the demand lands on the census statistic without the arm running.</summary>
    [TestMethod]
    public void Grd1RootDataDemandObserved()
    {
        ContextSaturationEngine engine = Saturate(
            [
                SubClassOf(OneOf("o"), DataSome("dp", Integer)),
                SubClassOf(Class("Z"), OneOf("zed")),
            ],
            out _);

        Assert.IsTrue(engine.RootDataDemandObserved, "A data demand instantiated at the constant o lands on the root data demand activity statistic while the per-constant arm is off.");
    }

    /// <summary>Wid-1 packed-width delegation constant: the clausifier's whole-module rejection <c>PackedTermWidthExceeded</c> guards on <see cref="DlTerm.FitsFunctionOfIndividual"/>, which packs exactly up to the field limits and refuses one beyond the function-symbol limit — the boundary the clausifier's <c>WholeModuleRejection</c> path reads.</summary>
    [TestMethod]
    public void Wid1PackedWidthDelegatesNamed()
    {
        Assert.IsTrue(DlTerm.FitsFunctionOfIndividual(DlTerm.FunctionSymbolLimit, DlTerm.IndividualLimit), "The function-of-individual payload packs exactly up to both field limits.");
        Assert.IsFalse(DlTerm.FitsFunctionOfIndividual(DlTerm.FunctionSymbolLimit + 1, 1), "One function symbol beyond the packed field limit fails the fit check, which delegates the whole module named.");
    }

    /// <summary>
    /// Wid-2 the corpus width measurement (the lock of the packed-field
    /// split): GALEN-Heart is the one vendored nominal-jurisdiction corpus module
    /// that passes every guard (92 punned singleton enumerations, no HasKey), so
    /// its frozen signature is the binding measurement for the
    /// <c>FunctionOfIndividual</c> field widths — the module must clausify
    /// without the <c>PackedTermWidthExceeded</c> whole-module rejection and its
    /// function-symbol and individual counts must fit the packed split. The
    /// OWL2Bench TBoxes are key-guarded whole (<c>KeyOnNominalModule</c>), so
    /// their widths never reach the packing.
    /// </summary>
    [TestMethod]
    public async Task Wid2GalenHeartSignatureFitsThePackedSplit()
    {
        string path = Path.Combine(W3cCorpusPath.LibraryDirectory("Benchmark"), "ORE2014", "galen-heart-alchoi-d.ofn");
        byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
        OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(bytes);
        Assert.IsFalse(document.Diagnostics.HasErrors, "The vendored GALEN-Heart module parses cleanly.");

        ClausificationResult clausification = ContextClausifier.Clausify(new ReasoningModule([.. document.Axioms], Violations: []));
        Assert.DoesNotContain(ContextRemainderNames.PackedTermWidthExceeded, clausification.Remainder, "GALEN-Heart's frozen signature fits the packed f(o) split, so the width rejection never fires.");
        Assert.IsTrue(
            DlTerm.FitsFunctionOfIndividual(clausification.Symbols.FunctionSymbolCount, clausification.Symbols.IndividualCount),
            $"GALEN-Heart's frozen signature fits the packed split (measured functions={clausification.Symbols.FunctionSymbolCount} of {DlTerm.FunctionSymbolLimit}, individuals={clausification.Symbols.IndividualCount} of {DlTerm.IndividualLimit}).");
        Assert.IsGreaterThan(0, clausification.Symbols.IndividualCount, $"GALEN-Heart takes the nominal jurisdiction and interns its punned individuals (measured individuals={clausification.Symbols.IndividualCount}, functions={clausification.Symbols.FunctionSymbolCount}).");
    }

    /// <summary>
    /// Cor-1 the corpus survey attribution after the data-polarity widening:
    /// GALEN-Heart is survey-admitted WHOLE with the nominal census bit set —
    /// the negative-position single-property data existentials and has-values
    /// its definitional equivalences carry now admit and lower to their
    /// NNF-dual universal markers. The widened admission is attributable:
    /// exactly the same 43 data-bearing axioms the pre-widening survey rejected
    /// on carry the data census keys, and the polarity-qualified census counts
    /// each construct at BOTH polarities (the equivalence double-walk: 24
    /// existentials and 19 has-values, once per side).
    /// </summary>
    [TestMethod]
    public async Task Cor1GalenHeartSurveyAdmitsAfterDataPolarityWidening()
    {
        string path = Path.Combine(W3cCorpusPath.LibraryDirectory("Benchmark"), "ORE2014", "galen-heart-alchoi-d.ofn");
        byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
        OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(bytes);
        Assert.IsFalse(document.Diagnostics.HasErrors, "The vendored GALEN-Heart module parses cleanly.");

        ContextModuleSurveyResult survey = ContextModuleSurvey.Survey(new ReasoningModule([.. document.Axioms], Violations: []));
        Assert.IsTrue(survey.Admitted, "The production survey admits GALEN-Heart whole after the data-polarity widening.");
        Assert.IsTrue(survey.MentionsNominals, "The singleton enumerations keep the nominal census bit set.");

        int dataBearing = 0;
        foreach(OwlAxiom axiom in document.Axioms)
        {
            if(MentionsDataConstruct(axiom))
            {
                dataBearing++;
            }
        }

        Assert.AreEqual(43, dataBearing, "The frozen GALEN-Heart signature hosts its data shapes on exactly 43 axioms — the same axioms the pre-widening survey rejected on, now admitted.");
        IReadOnlyList<(string Key, int Count)> census = OwlConstructCensus.Count(new ReasoningModule([.. document.Axioms], Violations: []));
        Assert.AreEqual(24, CensusCountOf(census, "DataSomeValuesFrom(sub)"), "The 24 data existentials census once each at sub polarity through their equivalences.");
        Assert.AreEqual(24, CensusCountOf(census, "DataSomeValuesFrom(super)"), "The 24 data existentials census once each at super polarity through their equivalences.");
        Assert.AreEqual(19, CensusCountOf(census, "DataHasValue(sub)"), "The 19 data has-values census once each at sub polarity through their equivalences.");
        Assert.AreEqual(19, CensusCountOf(census, "DataHasValue(super)"), "The 19 data has-values census once each at super polarity through their equivalences.");
    }

    /// <summary>
    /// Cor-1 the measured corpus cell: the admitted GALEN-Heart module
    /// clausifies whole — every backward equivalence direction emits its NNF
    /// dual — and the gates are OPEN (the second gate admits the Universal-kind
    /// markers), so the module SATURATES for the first time. The measured cell
    /// is the honest budget abstention: the saturation exhausts any practical
    /// ceiling inside the ROOT context's churn regime (97+ percent of attempts
    /// are redundant re-offers with superlinear r-Pred growth over the 92
    /// punned enumerations — Release probes: 20M attempts in 46s, 100M in
    /// 8m10s, neither near a fixpoint), UPSTREAM of the disjunctive data lane,
    /// which never engages (zero probes). The pin holds the attribution: the
    /// Cor-1 admission surface and decision lane are no longer the blocker —
    /// the named backlog is the root-churn habitat (the NOMR-2
    /// simplify-reflect/fragmentation lane), and this cell moves to
    /// DECIDED-Consistent when that lane lands. The tier is composed over an
    /// abstaining fallback so nothing resolves the delegation.
    /// </summary>
    [TestMethod]
    public async Task Cor1GalenHeartAdmitsWholeAndAbstainsInRootChurnAtTheStageBudget()
    {
        string path = Path.Combine(W3cCorpusPath.LibraryDirectory("Benchmark"), "ORE2014", "galen-heart-alchoi-d.ofn");
        byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
        OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(bytes);
        Assert.IsFalse(document.Diagnostics.HasErrors, "The vendored GALEN-Heart module parses cleanly.");
        ReasoningModule module = new([.. document.Axioms], Violations: []);

        ClausificationResult clausification = ContextClausifier.Clausify(module);
        Assert.AreEqual(43, clausification.NegativePolarityDataMarkers, "Each of the 43 data-bearing equivalences lowers its backward direction to exactly one NNF dual.");

        DescriptionLogicDelegate contextOverAbstain = ReasoningEngines.ContextSaturation(new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: GalenHeartInferenceCeiling), AbstainOnDelegation);
        ModuleDecision decision = await contextOverAbstain(module, TestContext.CancellationToken).ConfigureAwait(false);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "The admitted module saturates and abstains on the ceiling — the honest measured cell; a decide here means the churn habitat closed and this pin must move to the DECIDED-Consistent cell.");
        Assert.AreEqual((long)GalenHeartInferenceCeiling, totals.InferenceAttempts, "The abstention carries exactly the spent ceiling — the beyond-practical-reach signal, distinct from a not-admitted empty-totals abstention.");
        Assert.AreEqual(43L, totals.NegativePolarityDataMarkersMinted, "The statistics attribute the widened admission through the abstention: all 43 duals minted.");
        Assert.AreEqual(0L, totals.DisjunctiveDataProbes, "The disjunctive data lane never engages before the ceiling — the burn is upstream of Cor-1's machinery, in the root churn.");
        Assert.AreEqual(0L, totals.DisjunctiveDataCertifications, "The fixpoint certification never runs on a budget-stopped saturation — the whole lane funnel stays zero through the abstention.");
        Assert.IsGreaterThan(totals.InferenceAttempts / 2, totals.RedundantConclusions, "Redundant re-offers dominate the spent budget — the churn-regime attribution.");
        Assert.IsGreaterThan(0L, totals.RootPredApplications, "The r-Pred root machinery is the active rule family in the churn.");
    }

    /// <summary>
    /// Cor-3 the raw-engine <c>RootDataDemandObserved</c> census probe: the
    /// statistic's set sites are
    /// budget-unguarded, so it is readable straight off the engine core
    /// at the Cor-1 ceiling even though the saturation itself abstains before
    /// its fixpoint — this closes the census hole on whether a root-landed data
    /// demand is reachable on GALEN-Heart at all, driven DIRECTLY through
    /// <see cref="ContextSaturationEngine"/> the way the other nominal pins in
    /// this class do, below the module survey and below the reasoner's second
    /// gate. This raw engine leaves the per-constant arm off, so the statistic
    /// records the landing without the arm running. MEASURED:
    /// <see langword="true"/> — a constant-instantiated data demand reaches a
    /// root context inside the spent ceiling; the churn habitat that blocks
    /// Cor-1's own decided verdict does not block this statistic, because the
    /// set site runs unguarded on the way to the ceiling, not at the fixpoint.
    /// The per-constant arm's activity attribution is a separate step.
    /// </summary>
    [TestMethod]
    public async Task Cor3GalenHeartRawEngineRootDataDemandObservedMeasuresAtTheStageBudget()
    {
        string path = Path.Combine(W3cCorpusPath.LibraryDirectory("Benchmark"), "ORE2014", "galen-heart-alchoi-d.ofn");
        byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
        OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(bytes);
        Assert.IsFalse(document.Diagnostics.HasErrors, "The vendored GALEN-Heart module parses cleanly.");
        ReasoningModule module = new([.. document.Axioms], Violations: []);

        ClausificationResult clausification = ContextClausifier.Clausify(module);
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification);
        SaturationOutcome outcome = engine.Saturate(new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: GalenHeartInferenceCeiling), TestContext.CancellationToken);

        Assert.AreEqual(SaturationOutcome.BudgetExhausted, outcome, "GALEN-Heart's root churn regime exhausts the ceiling before its fixpoint, the same measured cell Cor-1 reads.");
        Assert.IsTrue(engine.RootDataDemandObserved, "MEASURED: the budget-unguarded statistic records inside the spent ceiling — a constant-instantiated data demand does reach a root context on GALEN-Heart, closing the census hole with a positive reading independent of the churn habitat that blocks Cor-1's own decided verdict.");
    }

    /// <summary>The abstaining fallback for the second-gate pin: it decides no module, so a context-tier delegation surfaces as the on-budget abstention rather than an oracle's verdict or cost.</summary>
    /// <param name="module">The module the context tier did not decide.</param>
    /// <param name="cancellationToken">The budget token, unused because the fallback does no work.</param>
    /// <returns>An abstaining decision carrying only the module's axiom count.</returns>
    private static ValueTask<ModuleDecision> AbstainOnDelegation(ReasoningModule module, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        return new ValueTask<ModuleDecision>(ModuleDecision.AbstainedOnBudget(ReasoningDecisionStatistics.Empty with { ModuleAxiomCount = module.Axioms.Count }));
    }

    /// <summary>The census count of a key, zero when absent.</summary>
    /// <param name="census">The censused key counts.</param>
    /// <param name="key">The key to look up.</param>
    /// <returns>The count.</returns>
    private static int CensusCountOf(IReadOnlyList<(string Key, int Count)> census, string key)
    {
        foreach((string candidate, int count) in census)
        {
            if(candidate == key)
            {
                return count;
            }
        }

        return 0;
    }

    /// <summary>Detects a data-family construct anywhere on the axiom's own layer or its nested class-expression and data-range surfaces through the polarity-qualified construct census — every data-family census key carries the <c>Data</c> prefix.</summary>
    /// <param name="axiom">The axiom to probe.</param>
    /// <returns>Whether the axiom's census carries a data-family key.</returns>
    private static bool MentionsDataConstruct(OwlAxiom axiom)
    {
        IReadOnlyList<(string Key, int Count)> census = OwlConstructCensus.Count(new ReasoningModule([axiom], Violations: []));
        foreach((string key, _) in census)
        {
            if(key.StartsWith("Data", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cor-2 the OWL2Bench DL/EL survey attribution under the lit key join (the
    /// stand's DL/EL cells, the lift flip): EL's sole blocker is the
    /// key-on-nominal co-occurrence guard, so the lit key-join switch lifts it and
    /// the full EL TBox is ADMITTED and DECIDES through the context engine directly
    /// (not the public <c>DecideConsistency</c> surface, which times out unbounded) —
    /// the admitted+decided flip. DL carries the same guard plus exactly two axiom-level
    /// rejections, both <c>SubDataPropertyOf</c> under the reserved
    /// <c>owl:topDataProperty</c> (a separate backlog item — the surveyed
    /// data-property RBox admits named data properties only), so the full DL TBox
    /// stays not-admitted under the lit switch and its residual re-pins to exactly
    /// those two axioms; dropping the key axiom and the two admits DL with the
    /// nominal census bit set.
    /// </summary>
    [TestMethod]
    public async Task Cor2Owl2BenchElAdmitsAndDecidesWhileDlResidualPinsToTheTwoSubDataPropertyAxioms()
    {
        OwlOntologyDocument elDocument = await LoadOwl2BenchProfileAsync("UNIV-BENCH-OWL2EL.owl").ConfigureAwait(false);
        ReasoningModule elModule = new([.. elDocument.Axioms], Violations: []);
        Assert.IsFalse(ContextModuleSurvey.Survey(elModule, rootKeyJoinEnabled: false).Admitted, "The dark face (key join off) whole-rejects EL on the key-on-nominal co-occurrence guard — the pre-lift baseline the flip lifts.");
        Assert.IsTrue(ContextModuleSurvey.Survey(elModule, rootKeyJoinEnabled: true).Admitted, "The lit key-join switch lifts EL's sole blocker — the key-on-nominal guard — so the full EL TBox is admitted.");
        (int elKeyAxioms, int elAxiomRejections, _) = SurveyAxiomBreakdown(elDocument);
        Assert.AreEqual(1, elKeyAxioms, "EL carries exactly one HasKey axiom in its frozen census.");
        Assert.AreEqual(0, elAxiomRejections, "EL carries no axiom-level survey rejection beside the key-on-nominal co-occurrence guard.");
        ClausificationResult elClausification = ContextClausifier.Clausify(elModule, EqualityLowering.GeneralClause, DatatypeRegistry.Empty, [], riderEnabled: false, nominalDeciderEnabled: false, rootKeyJoinEnabled: true);
        ContextSaturationEngine elEngine = ContextSaturationEngine.Create(elClausification);
        SaturationOutcome elOutcome = elEngine.Saturate(new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 2_000_000), TestContext.CancellationToken);
        Assert.AreEqual(SaturationOutcome.Completed, elOutcome, "The admitted EL TBox saturates to its fixpoint through the context engine under the lit key join — the admitted+decided flip.");
        Assert.IsFalse(elEngine.IsInconsistent, "The OWL2Bench EL TBox is consistent through the context engine.");

        OwlOntologyDocument dlDocument = await LoadOwl2BenchProfileAsync("UNIV-BENCH-OWL2DL.owl").ConfigureAwait(false);
        ReasoningModule dlModule = new([.. dlDocument.Axioms], Violations: []);
        Assert.IsFalse(ContextModuleSurvey.Survey(dlModule, rootKeyJoinEnabled: true).Admitted, "DL stays not-admitted under the lit key join — two SubDataPropertyOf axioms remain outside the surveyed data-property RBox.");
        (int dlKeyAxioms, int dlAxiomRejections, List<OwlAxiom> dlAdmissible) = SurveyAxiomBreakdown(dlDocument);
        Assert.AreEqual(1, dlKeyAxioms, "DL carries exactly one HasKey axiom in its frozen census.");
        Assert.AreEqual(2, dlAxiomRejections, "DL carries exactly two axiom-level survey rejections beside the key-on-nominal co-occurrence guard.");
        ContextModuleSurveyResult dlTrimmed = ContextModuleSurvey.Survey(new ReasoningModule([.. dlAdmissible], Violations: []), rootKeyJoinEnabled: true);
        Assert.IsTrue(dlTrimmed.Admitted, "Dropping the HasKey axiom and the two SubDataPropertyOf rejections admits DL — the named blockers are exhaustive.");
        Assert.IsTrue(dlTrimmed.MentionsNominals, "The admitted remainder of DL keeps its nominal constructs, so the nominal census bit is set.");
    }

    /// <summary>
    /// The bounded consistency decision on the vendored OWL2Bench EL module
    /// returns rather than searching without end: the module is admitted by the
    /// context survey, yet the production context surface declines the
    /// whole-module verdict and delegates to the snapshot tableau, whose rule
    /// applications the same budget bounds — so a starved inference ceiling
    /// yields an <see cref="ReasoningDecisionOutcome.AbstainedBudget"/> that
    /// carries no verdict and the delegated leg's spent tableau totals. The
    /// context totals stay empty on the delegated answer, the tableau leg's own
    /// totals standing as the abstention's evidence.
    /// </summary>
    [TestMethod]
    public async Task Owl2BenchElBoundedConsistencyDecisionAbstainsWithReasonThroughTheDelegatedTableauLeg()
    {
        OwlOntologyDocument elDocument = await LoadOwl2BenchProfileAsync("UNIV-BENCH-OWL2EL.owl").ConfigureAwait(false);
        ReasoningModule elModule = new([.. elDocument.Axioms], Violations: []);
        Assert.IsTrue(ContextModuleSurvey.Survey(elModule, rootKeyJoinEnabled: true).Admitted, "The vendored EL TBox is admitted by the context survey under the lit key join.");
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideConsistencyModule(elModule, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 100), TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "The bounded consistency decision on the admitted-yet-delegated EL module abstains with a reason rather than searching without end.");
        Assert.IsNull(decision.Verdict, "An abstention carries no verdict.");
        Assert.AreEqual(100L, decision.Statistics.TableauTotals.RuleApplications, "The delegated snapshot tableau spent exactly the inference ceiling before abstaining.");
        Assert.AreEqual(1, decision.Statistics.TableauTotals.TableauRuns, "One tableau run carried the delegated leg.");
        Assert.IsFalse(decision.Statistics.ContextTotals.ContextDecided, "The context engine did not produce the verdict; the delegated tableau leg carried the answer.");
        Assert.AreEqual(0L, decision.Statistics.ContextTotals.InferenceAttempts, "The delegated answer leaves the context totals empty, the tableau leg's totals standing as the abstention's evidence.");
    }

    /// <summary>Loads and maps a vendored OWL2Bench profile TBox, asserting it parses and maps cleanly.</summary>
    /// <param name="profile">The profile file name under the OWL2Bench corpus.</param>
    /// <returns>The mapped ontology document.</returns>
    private async Task<OwlOntologyDocument> LoadOwl2BenchProfileAsync(string profile)
    {
        string path = Path.Combine(W3cCorpusPath.LibraryDirectory("Benchmark"), "OWL2Bench", profile);
        byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
        DiagnosticBag diagnostics = new();
        IReadOnlyList<Quad> quads = RdfXmlReader.Read(bytes, diagnostics, Utf8Strings.From(new Uri(Path.GetFullPath(path)).AbsoluteUri));
        Assert.IsFalse(diagnostics.HasErrors, $"The vendored {profile} TBox parses cleanly.");
        OwlOntologyDocument document = OwlRdfMapper.Map(quads);
        Assert.IsFalse(document.Diagnostics.HasErrors, $"The vendored {profile} TBox maps cleanly.");

        return document;
    }

    /// <summary>Breaks an OWL2Bench TBox into its HasKey axiom count, its axiom-level survey rejections (each pinned a <see cref="OwlSubDataPropertyOfAxiom"/> under the reserved <c>owl:topDataProperty</c>), and the axioms the survey admits individually.</summary>
    /// <param name="document">The mapped OWL2Bench TBox.</param>
    /// <returns>The HasKey axiom count, the axiom-level rejection count, and the individually-admitted axioms.</returns>
    private static (int KeyAxioms, int AxiomRejections, List<OwlAxiom> Admissible) SurveyAxiomBreakdown(OwlOntologyDocument document)
    {
        List<OwlAxiom> admissible = new(document.Axioms.Length);
        int keyAxioms = 0;
        int axiomRejections = 0;
        foreach(OwlAxiom axiom in document.Axioms)
        {
            if(axiom is OwlHasKeyAxiom)
            {
                keyAxioms++;
            }
            else if(ContextModuleSurvey.Survey(new ReasoningModule([axiom], Violations: [])).Admitted)
            {
                admissible.Add(axiom);
            }
            else
            {
                axiomRejections++;
                Assert.IsInstanceOfType<OwlSubDataPropertyOfAxiom>(axiom, "Every axiom-level rejection is a SubDataPropertyOf under the reserved owl:topDataProperty — the surveyed data-property RBox admits named data properties only.");
            }
        }

        return (keyAxioms, axiomRejections, admissible);
    }

    /// <summary>Ddata-1 the disjunctive refutation rung: a node forcing <c>∃d.[&lt;4]</c> meets the dual <c>⊤ → {∀d.¬[&lt;5](x), B(x)}</c> of <c>∃d.[&lt;5] ⊑ B</c>; the probe clashes (<c>[&lt;4] ⊆ [&lt;5]</c>), the universal is refuted against the pool, and the body-conditioned narrowing forces the residual — the containment subsumption <c>A ⊑ B</c> reads off the query context.</summary>
    [TestMethod]
    public void SubclassExistentialContainmentRefutesDisjunctAndForcesResidual()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "A",
            [
                SubClassOf(Class("A"), DataSome("d", IntegerBelow(4))),
                SubClassOf(DataSome("d", IntegerBelow(5)), Class("B")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        ContextSaturationStatistics statistics = engine.BuildStatistics(contextDecided: true);
        Assert.IsFalse(engine.IsInconsistent, "The containment module is consistent.");
        Assert.AreEqual(1L, statistics.DisjunctiveDataRefutations, "Exactly the containment universal refutes against the forced existential.");
        Assert.AreEqual(1L, statistics.DisjunctiveDataProbes, "Exactly one sidecar probe decides the pool-plus-marker set — the per-clause signature memo and the vacuous-satisfaction guard keep every other visit off the oracle.");
        Assert.IsGreaterThan(0L, statistics.DisjunctiveDataNarrowings, "The refutation emitted its body-conditioned residual narrowing.");
        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "A"), Atom(clausification, "B")), "The narrowing forces the residual: A is a B — the containment subsumption the dual encodes.");
        Assert.IsFalse(engine.HasUndecidedDataObligation, "Nothing stays undecided: the refuted universal leaves the survivor set and the pool decides clean.");
        Assert.AreEqual(0L, statistics.UndecidedDataObligationCount, "The clean decide never touches the undecided funnel counter.");
    }

    /// <summary>Ddata-2 the survivor certification rung: the reverse pairing — a node forcing <c>∃d.[&lt;5]</c> against the dual of <c>∃d.[&lt;4] ⊑ B</c> — stays openable (<c>[&lt;5] ⊄ [&lt;4]</c>, a value in <c>[4,5)</c> realizes both), so the universal survives, the fixpoint certifies the joint set, and the residual is never forced — the answer-neutrality face.</summary>
    [TestMethod]
    public void OpenDataDisjunctCertifiesConsistent()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "A",
            [
                SubClassOf(Class("A"), DataSome("d", IntegerBelow(5))),
                SubClassOf(DataSome("d", IntegerBelow(4)), Class("B")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        ContextSaturationStatistics statistics = engine.BuildStatistics(contextDecided: true);
        Assert.IsFalse(engine.IsInconsistent, "The non-containment module is consistent.");
        Assert.AreEqual(0L, statistics.DisjunctiveDataRefutations, "A non-containment never refutes — the survivor face.");
        Assert.IsGreaterThan(0L, statistics.DisjunctiveDataCertifications, "The fixpoint certification realizes the surviving universal jointly with the forced existential.");
        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "A"), Atom(clausification, "B")), "No narrowing fires: A is not a B — the open disjunct stays answer-neutral.");
        Assert.IsFalse(engine.HasUndecidedDataObligation, "The certified module carries no undecided obligation.");
        Assert.AreEqual(0L, statistics.UndecidedDataObligationCount, "The certified module never touches the undecided funnel counter.");
        Assert.AreEqual(0L, statistics.UncertifiedDisjunctiveDataLatches, "Every survivor set certifies — no uncertified latch.");
    }

    /// <summary>Ddata-3 the honest-abstention face: a dual whose complemented range rides a pattern facet the value-space checker cannot decide leaves the probe and the joint certification undecided — the module latches the undecided-obligation delegation instead of claiming a verdict over an unmodelled base.</summary>
    [TestMethod]
    public void UnmodelledDisjunctDatatypeAbstains()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "A",
            [
                SubClassOf(Class("A"), DataSome("d", StringPattern("[0-9]+"))),
                SubClassOf(DataSome("d", StringPattern("[a-z]+")), Class("B")),
                Bystander(),
            ],
            out _);

        ContextSaturationStatistics statistics = engine.BuildStatistics(contextDecided: true);
        Assert.IsFalse(engine.IsInconsistent, "An undecided obligation is never read as a clash.");
        Assert.IsTrue(engine.HasUndecidedDataObligation, "The pattern facet is outside the checker's decided fragment, so the obligation latches undecided.");
        Assert.IsGreaterThan(0L, statistics.UndecidedDataObligationCount, "The numeric funnel counter records the latch.");
        Assert.IsGreaterThan(0L, statistics.UncertifiedDisjunctiveDataLatches, "The joint certification over the unmodelled survivor declines rather than certify.");
        Assert.AreEqual(0L, statistics.DisjunctiveDataRefutations, "Nothing refutes over an undecidable base.");
    }

    /// <summary>Ddata-4 the covering-configuration latch: a node forcing <c>∃d.[4,6]</c> meets two survivor universals <c>∀d.¬[4,5]</c> and <c>∀d.¬[5,6]</c>; each is individually openable (a value remains outside either window), so neither refutes — but their complements jointly cover the forced range, the joint certification clashes, and the module DELEGATES on the sound latch rather than claim a per-disjunction choice it cannot make.</summary>
    [TestMethod]
    public void CoveringUniversalsLatchUncertified()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "A",
            [
                SubClassOf(Class("A"), DataSome("d", IntegerBetween(4, 6))),
                SubClassOf(DataSome("d", IntegerBetween(4, 5)), Class("B1")),
                SubClassOf(DataSome("d", IntegerBetween(5, 6)), Class("B2")),
                Bystander(),
            ],
            out _);

        ContextSaturationStatistics statistics = engine.BuildStatistics(contextDecided: true);
        Assert.IsFalse(engine.IsInconsistent, "The covering configuration is not a derived inconsistency — one disjunct per clause remains openable.");
        Assert.AreEqual(0L, statistics.DisjunctiveDataRefutations, "Neither universal refutes alone: a value outside each single window exists.");
        Assert.IsGreaterThan(0L, statistics.UncertifiedDisjunctiveDataLatches, "The survivor joint set clashes — the complements cover the forced range — so the context latches uncertified.");
        Assert.IsTrue(engine.HasUndecidedDataObligation, "The uncertified latch delegates the module rather than claim the all-survivors-open model that does not exist.");
    }

    /// <summary>Ddata-5 the marker-position belt (adjudication C-S2-3): the superclass B is interned BEFORE the dual's marker is minted, so the canonicalised two-literal head sorts B ahead of the marker — the marker sits at a non-zero index, and the refutation still fires because every consumer locates markers by predicate scan, never by positional index.</summary>
    [TestMethod]
    public void TwoLiteralDisjunctiveHeadLocatesMarkerNotByIndexZero()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "A",
            [
                SubClassOf(Class("B"), Class("W")),
                SubClassOf(Class("A"), DataSome("d", IntegerBelow(4))),
                SubClassOf(DataSome("d", IntegerBelow(5)), Class("B")),
            ],
            out ClausificationResult clausification);

        bool premiseHeld = false;
        foreach(DlClause clause in clausification.Clauses)
        {
            if(clause.BodyLength == 0 && clause.Head.Length == 2 && clausification.DataDemandDescriptors.ContainsKey(clause.Head[1].Symbol))
            {
                Assert.IsFalse(clausification.DataDemandDescriptors.ContainsKey(clause.Head[0].Symbol), "The fixture premise: index 0 carries the superclass atom, not the marker.");
                premiseHeld = true;
            }
        }

        Assert.IsTrue(premiseHeld, "The fixture premise held: the dual clause's canonicalised head sorts its marker to index 1.");
        ContextSaturationStatistics statistics = engine.BuildStatistics(contextDecided: true);
        Assert.AreEqual(1L, statistics.DisjunctiveDataRefutations, "The refutation fires on the marker at index 1 — located by predicate scan.");
        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "A"), Atom(clausification, "B")), "The narrowing forces the residual through the non-zero-index marker head.");
    }

    /// <summary>Ord-1 packed total order: named individuals compare by their interned id, an individual equals itself, and every individual packs strictly ABOVE every function term in the canonicalisation order — the 3.1 packed-representation total order the clause canonicaliser leans on.</summary>
    [TestMethod]
    public void Ord1PackedOrderComparesIndividualsByInternedId()
    {
        Assert.IsLessThan(0, DlTerm.Individual(3).CompareTo(DlTerm.Individual(7)), "A lower-id individual packs below a higher-id one.");
        Assert.AreEqual(0, DlTerm.Individual(5).CompareTo(DlTerm.Individual(5)), "An individual packs equal to itself.");
        Assert.IsLessThan(0, DlTerm.Function(0).CompareTo(DlTerm.Individual(0)), "The first function term packs below the first individual.");
        Assert.IsLessThan(0, DlTerm.Function(1000).CompareTo(DlTerm.Individual(0)), "Every function term packs below every individual, even a high-id function symbol against the first individual.");
    }

    // Sig-1 (Pred-sigma root-predecessor branch): ApplyPredSigma / InvertPredSigma are private
    // helpers of the engine; their branch on a root predecessor is driven indirectly by the r-Succ /
    // r-Pred exchange that Root1SuccOpensEdgeFromDerivedGroundRole pins, so no direct unit pin exists
    // here (the behaviour is covered through the root edge exchange).

    /// <summary>Clausifies the axioms, builds the engine BELOW the gates, ensures the named query context, and saturates to the fixpoint.</summary>
    /// <param name="queryClass">The local name of the class whose query context the pins read.</param>
    /// <param name="axioms">The module axioms.</param>
    /// <param name="clausification">The clausification, for atom lookups.</param>
    /// <returns>The saturated engine.</returns>
    private ContextSaturationEngine SaturateWithQuery(string queryClass, OwlAxiom[] axioms, out ClausificationResult clausification)
    {
        clausification = ContextClausifier.Clausify(Module(axioms));
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification);
        engine.EnsureQueryContext(Atom(clausification, queryClass));
        Assert.AreEqual(SaturationOutcome.Completed, engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken), "The unbounded saturation reaches its fixpoint.");

        return engine;
    }

    /// <summary>Clausifies the axioms and saturates WITHOUT a query context (ground-clash rows read <see cref="ContextSaturationEngine.IsInconsistent"/> only), running the ground ghost pass as the production path does after a completed saturation.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <param name="clausification">The clausification, for atom lookups.</param>
    /// <returns>The saturated engine.</returns>
    private ContextSaturationEngine Saturate(OwlAxiom[] axioms, out ClausificationResult clausification)
    {
        clausification = ContextClausifier.Clausify(Module(axioms));
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification);
        Assert.AreEqual(SaturationOutcome.Completed, engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken), "The unbounded saturation reaches its fixpoint.");
        engine.RunGroundGhostPass();

        return engine;
    }

    /// <summary>The concept atom id of a named class in the example namespace.</summary>
    /// <param name="clausification">The clausification whose symbol table is consulted.</param>
    /// <param name="local">The class's local name.</param>
    /// <returns>The atom id.</returns>
    private static int Atom(ClausificationResult clausification, string local)
    {
        return clausification.Symbols.AtomOf(Utf8Strings.From(Example + local));
    }

    /// <summary>The unrelated Horn axiom minting the bystander classes the answer-neutrality reads use.</summary>
    /// <returns>The bystander axiom.</returns>
    private static OwlSubClassOfAxiom Bystander()
    {
        return SubClassOf(Class("Spruce"), Class("Willow"));
    }

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>A provenance quad naming the axiom's origin.</summary>
    /// <param name="marker">The origin marker's local name.</param>
    /// <returns>The quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(new NamedNode(Utf8Strings.From(Example + marker)), new NamedNode(Utf8Strings.From(Example + "p")), new NamedNode(Utf8Strings.From(Example + "o")), Graph: null);
    }

    /// <summary>A named class reference in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The reference.</returns>
    private static OwlClassReference Class(string local)
    {
        return new OwlClassReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named object property expression in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property expression.</returns>
    private static OwlObjectPropertyReference Property(string local)
    {
        return new OwlObjectPropertyReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named individual in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The individual node.</returns>
    private static NamedNode Individual(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>An existential restriction over a forward role and a class filler.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class expression.</param>
    /// <returns>The existential restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>An individual-value restriction over a forward role — <c>∃r.{a}</c> in its <c>ObjectHasValue</c> spelling.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="individual">The required value individual's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasValue HasValue(string property, string individual)
    {
        return new OwlObjectHasValue(Property(property), Individual(individual));
    }

    /// <summary>An enumeration of individuals in the example namespace (<c>ObjectOneOf</c>); a single individual is the nominal <c>{a}</c>.</summary>
    /// <param name="individuals">The enumerated individuals' local names.</param>
    /// <returns>The enumeration.</returns>
    private static OwlObjectOneOf OneOf(params string[] individuals)
    {
        RdfTerm[] terms = new RdfTerm[individuals.Length];
        for(int index = 0; index < individuals.Length; index++)
        {
            terms[index] = Individual(individuals[index]);
        }

        return new OwlObjectOneOf(terms);
    }

    /// <summary>A qualified or unqualified maximum-cardinality restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound n.</param>
    /// <param name="filler">The filler class, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Max(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), filler);
    }

    /// <summary>A qualified or unqualified maximum-cardinality restriction over the inverse of a forward role.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="cardinality">The bound n.</param>
    /// <param name="filler">The filler class, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The inverse maximum-cardinality restriction.</returns>
    private static OwlObjectCardinality MaxInverse(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property))), filler);
    }

    /// <summary>A subclass axiom.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>An equivalence of two class expressions.</summary>
    /// <param name="first">The first expression.</param>
    /// <param name="second">The second expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentClassesAxiom Equivalent(OwlClassExpression first, OwlClassExpression second)
    {
        return new OwlEquivalentClassesAxiom(first, second) { Origin = Origin("equivalent") };
    }

    /// <summary>A class assertion typing an individual.</summary>
    /// <param name="type">The asserted type.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, NamedNode individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

    /// <summary>An asserted role edge between two individuals.</summary>
    /// <param name="from">The source individual's local name.</param>
    /// <param name="role">The role's local name.</param>
    /// <param name="to">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom Edge(string from, string role, string to)
    {
        return new OwlObjectPropertyAssertionAxiom(Individual(from), Individual(role), Individual(to)) { Origin = Origin($"edge-{from}-{to}") };
    }

    /// <summary>A same-individual axiom pairing two named individuals.</summary>
    /// <param name="first">The first individual's local name.</param>
    /// <param name="second">The second individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSameIndividualAxiom Same(string first, string second)
    {
        return new OwlSameIndividualAxiom(Individual(first), Individual(second)) { Origin = Origin("same") };
    }

    /// <summary>A different-individuals axiom over the named individuals.</summary>
    /// <param name="individuals">The mutually distinct individuals' local names.</param>
    /// <returns>The axiom.</returns>
    private static OwlDifferentIndividualsAxiom Different(params string[] individuals)
    {
        RdfTerm[] terms = new RdfTerm[individuals.Length];
        for(int index = 0; index < individuals.Length; index++)
        {
            terms[index] = Individual(individuals[index]);
        }

        return new OwlDifferentIndividualsAxiom(terms) { Origin = Origin("different") };
    }

    /// <summary>An <c>InverseObjectProperties</c> axiom pairing two roles as each other's reverse.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlInverseObjectPropertiesAxiom Inverse(string first, string second)
    {
        return new OwlInverseObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("inverse") };
    }

    /// <summary>A single-property data existential over a data property in the example namespace.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="range">The filler range.</param>
    /// <returns>The data existential.</returns>
    private static OwlDataSomeValuesFrom DataSome(string property, OwlDataRange range)
    {
        return new OwlDataSomeValuesFrom([new NamedNode(Utf8Strings.From(Example + property))], range);
    }

    /// <summary>An <c>xsd:integer</c> typed literal.</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The literal.</returns>
    private static Literal IntegerLiteral(int value)
    {
        return new Literal(Utf8Strings.From(value.ToString(System.Globalization.CultureInfo.InvariantCulture)), new NamedNode(Vocabulary.Xsd.Integer));
    }

    /// <summary>An integer datatype restriction bounded strictly below the given value.</summary>
    /// <param name="bound">The exclusive upper bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerBelow(int bound)
    {
        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.Integer), [new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MaxExclusive), IntegerLiteral(bound))]);
    }

    /// <summary>A closed integer interval <c>[low, high]</c> as a datatype restriction.</summary>
    /// <param name="low">The inclusive lower bound.</param>
    /// <param name="high">The inclusive upper bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerBetween(int low, int high)
    {
        return new OwlDatatypeRestriction(
            new NamedNode(Vocabulary.Xsd.Integer),
            [
                new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MinInclusive), IntegerLiteral(low)),
                new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MaxInclusive), IntegerLiteral(high)),
            ]);
    }

    /// <summary>A pattern-restricted string range the value-space checker cannot decide — the honest-abstention base.</summary>
    /// <param name="pattern">The regular-expression facet value.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction StringPattern(string pattern)
    {
        return new OwlDatatypeRestriction(
            new NamedNode(Vocabulary.Xsd.NormalizedString),
            [new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.Pattern), new Literal(Utf8Strings.From(pattern), new NamedNode(Vocabulary.Xsd.String)))]);
    }

    /// <summary>The <c>xsd:integer</c> datatype as a data range.</summary>
    private static OwlDatatypeReference Integer { get; } = new(new NamedNode(Vocabulary.Xsd.Integer));
}
