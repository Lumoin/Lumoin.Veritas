using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The disjunctive engine pins: each pin drives
/// multi-literal-head clauses through the lifted engine core DIRECTLY —
/// clausifier to engine, below the module survey and the reasoner's second gate
/// — so a half-migrated selection, index, or readback site fails HERE
/// independently of the gates. Semantic expectations are transcribed from the
/// pre-registered ground-truth sheet (42 of 42 independently confirmed); row
/// ids in the method prefixes name the ground-truth rows.
/// The subsumption READ-OFF rows (COV-1, UNI, CARRY-1, TAX-1) certify the
/// maximal-set selection over the band-relaxed order. The final pin
/// witnesses the production flip: the reasoner decides disjunctive
/// modules whole through the opened gates.
/// </summary>
[TestClass]
internal sealed class ContextDisjunctiveEnginePinTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/tier2stageA#";

    /// <summary>CTRL-1 (the Horn control): the shipped Horn max-1 merge clash still refutes through the lifted core, and Factor stays inert on Horn input — a single-literal head has no second disjunct to factor, so the selection, re-keyed indexes, and migrated readbacks are behavior-preserving where selected == head[0].</summary>
    [TestMethod]
    public void Ctrl1HornMaxOneMergeClashRefutesWithFactorInert()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Max("feeds", 1, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("Birch"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("Cedar"))),
                Disjoint(Class("Birch"), Class("Cedar")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Spruce")), "The Horn max-1 merge clash makes Ash unsatisfiable, read as subsumed-by-everything.");
        Assert.AreEqual(0L, engine.BuildStatistics(contextDecided: true).FactorApplications, "Factor never fires on Horn input: no head carries a second equality disjunct.");
    }

    /// <summary>Covering refutation: both covering branches lower to bottom, so ordered resolution must resolve THROUGH both disjuncts of the EmitDl1 multi-literal head — the Hyper residual carry, the selection order, and the selected-literal premise index all sit on the derivation path.</summary>
    [TestMethod]
    public void CovCoveringWithBothBranchesBottomRefutes()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Union(Class("Birch"), Class("Cedar"))),
                SubClassOf(Class("Birch"), NothingReference),
                SubClassOf(Class("Cedar"), NothingReference),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Spruce")), "With both covering branches bottom, Ash is unsatisfiable through case analysis over the disjunctive head.");
    }

    /// <summary>NEU-1a/NEU-1b + the §3.5 direct witness: a genuine open covering decides NEITHER disjunct — the query context provably holds the disjunctive clause, and the verdict reader answers non-subsumed on both branches (a disjunctive head is not a decided subsumption).</summary>
    [TestMethod]
    public void Neu1OpenCoveringAnswersNonSubsumedOnBothBranches()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Union(Class("Birch"), Class("Cedar"))),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Birch")), "An open covering does not entail the first disjunct.");
        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Cedar")), "An open covering does not entail the second disjunct.");
        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Spruce")), "Ash stays satisfiable under an open covering.");
        Assert.IsFalse(engine.IsInconsistent, "An open covering is consistent.");
    }

    /// <summary>COV-3: the DisjointUnion covering half registers as a multi-literal ontology head while the PAIRWISE-DISJOINTNESS half clashes two asserted memberships in the individual's ground context — the per-literal ontology grammar assert and the ground slice both hold with disjunctive machinery live.</summary>
    [TestMethod]
    public void Cov3DisjointUnionPairwiseHalfClashesOnAssertedMembers()
    {
        ContextSaturationEngine engine = Saturate(
            [
                DisjointUnion("Ash", Class("Birch"), Class("Cedar")),
                ClassAssertion(Class("Birch"), Individual("idb")),
                ClassAssertion(Class("Cedar"), Individual("idb")),
            ],
            out _);

        Assert.IsTrue(engine.IsInconsistent, "One individual asserted into both members of a disjoint union clashes on the pairwise-disjointness half.");
    }

    /// <summary>DL4-1: three forced pairwise-disjoint successors under an unqualified max-2 refute by the pigeonhole — the DL4 disjunctive-equality head, the equality-literal orientation invariant, the Eq residual carry, the published Succ trigger (which fires on a selected function-bearing literal WITH residual disjuncts — the late K2 hypothesis seeding this derivation needs), and the Pred residual carry all sit on the derivation path.</summary>
    [TestMethod]
    public void Dl41ThreeForcedDistinctSuccessorsUnderMaxTwoRefute()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Max("feeds", 2, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("B1"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B2"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B3"))),
                Disjoint(Class("B1"), Class("B2")),
                Disjoint(Class("B1"), Class("B3")),
                Disjoint(Class("B2"), Class("B3")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Spruce")), "Three pairwise-distinct forced successors under max-2 are a pigeonhole: Ash is unsatisfiable.");
    }

    /// <summary>DL4-2 (control): only two forced successors under max-2 stay satisfiable — the same machinery must not over-derive.</summary>
    [TestMethod]
    public void Dl42TwoForcedSuccessorsUnderMaxTwoStaySatisfiable()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Max("feeds", 2, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("B1"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B2"))),
                Disjoint(Class("B1"), Class("B2")),
                Disjoint(Class("B1"), Class("B3")),
                Disjoint(Class("B2"), Class("B3")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Spruce")), "Two forced successors satisfy max-2; Ash stays satisfiable.");
        Assert.IsFalse(engine.IsInconsistent, "The two-successor control module is consistent.");
    }

    /// <summary>DL4-5: the exact-2 upper bound carries the same pigeonhole as max-2 — the exact lowering's max half feeds the DL4 merge head.</summary>
    [TestMethod]
    public void Dl45ExactTwoWithThreeDistinctSuccessorsRefutes()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Exact("feeds", 2, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("B1"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B2"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B3"))),
                Disjoint(Class("B1"), Class("B2")),
                Disjoint(Class("B1"), Class("B3")),
                Disjoint(Class("B2"), Class("B3")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Spruce")), "Exact-2's upper half is the same pigeonhole: Ash is unsatisfiable.");
    }

    /// <summary>FACT-1: with only one disjoint pair, a merge over the B3-compatible pair satisfies max-2 — a false <c>s ≉ s</c> disjunct arising mid-derivation must drop ALONE, never collapse its whole head to bottom, or this satisfiable module reads inconsistent.</summary>
    [TestMethod]
    public void Fact1PartialDistinctnessOnePairStaysSatisfiable()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Max("feeds", 2, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("B1"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B2"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B3"))),
                Disjoint(Class("B1"), Class("B2")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Spruce")), "A B3-compatible merge satisfies max-2: Ash stays satisfiable under partial distinctness.");
        Assert.IsFalse(engine.IsInconsistent, "The partial-distinctness module is consistent.");
    }

    /// <summary>FACT-2: two disjoint pairs still leave the B2/B3 merge open — the second partial-distinctness face; an Eq conclusion that dropped its carried disjuncts would over-derive bottom here.</summary>
    [TestMethod]
    public void Fact2PartialDistinctnessTwoPairsStaysSatisfiable()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Max("feeds", 2, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("B1"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B2"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B3"))),
                Disjoint(Class("B1"), Class("B2")),
                Disjoint(Class("B1"), Class("B3")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Spruce")), "The B2/B3 merge stays open: Ash is satisfiable.");
        Assert.IsFalse(engine.IsInconsistent, "The two-pair partial-distinctness module is consistent.");
    }

    /// <summary>Min-3 under max-2 (the classic counting clash, unqualified): the DL2 witness-distinctness inequalities meet the DL4 merge equalities, so the refutation peels through Eq rewrites whose conclusions carry residual disjuncts, drop false <c>s ≉ s</c> disjuncts alone, and drop <c>s ≈ s</c>-bearing tautology clauses whole — the pure in-context equality face of the lift.</summary>
    [TestMethod]
    public void MinMaxMinThreeUnderMaxTwoRefutes()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Min("feeds", 3, null)),
                SubClassOf(Class("Ash"), Max("feeds", 2, null)),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Spruce")), "Three pairwise-distinct witnesses under max-2 refute: Ash is unsatisfiable.");
    }

    /// <summary>ZERO-1: max-0 against a forced successor is the already-lowerable no-successor Horn bottom clause — the n=0 survey row's engine face.</summary>
    [TestMethod]
    public void Zero1MaxZeroWithForcedSuccessorRefutes()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Max("feeds", 0, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("Birch"))),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Spruce")), "Max-0 against a forced successor is unsatisfiable.");
    }

    /// <summary>ZERO-2: exact-0 alone forces nothing and stays satisfiable.</summary>
    [TestMethod]
    public void Zero2ExactZeroAloneStaysSatisfiable()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Exact("feeds", 0, null)),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Spruce")), "Exact-0 with no forced successor is satisfiable.");
        Assert.IsFalse(engine.IsInconsistent, "The exact-0 module is consistent.");
    }

    /// <summary>ZERO-3: exact-0 against a forced successor refutes through its max half.</summary>
    [TestMethod]
    public void Zero3ExactZeroWithForcedSuccessorRefutes()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Exact("feeds", 0, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("Birch"))),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Spruce")), "Exact-0 against a forced successor is unsatisfiable.");
    }

    /// <summary>CARRY-2: the whole pigeonhole apparatus rides under a carried covering disjunct and must NOT sink the carrier — Ash keeps its Cedar model, so no bottom may reach Ash's query context while every merge clause carries the Cedar residual.</summary>
    [TestMethod]
    public void Carry2PigeonholeUnderCarriedDisjunctStaysSatisfiable()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Union(Class("Cedar"), Class("Rowan"))),
                SubClassOf(Class("Rowan"), Max("feeds", 2, null)),
                SubClassOf(Class("Rowan"), Some("feeds", Class("B1"))),
                SubClassOf(Class("Rowan"), Some("feeds", Class("B2"))),
                SubClassOf(Class("Rowan"), Some("feeds", Class("B3"))),
                Disjoint(Class("B1"), Class("B2")),
                Disjoint(Class("B1"), Class("B3")),
                Disjoint(Class("B2"), Class("B3")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Spruce")), "Ash keeps the Cedar model: the carried disjunct survives the equality reasoning.");
        Assert.IsFalse(engine.IsInconsistent, "The carried-disjunct module is consistent.");
    }

    /// <summary>COV-1 (the maximal-set read-off): case analysis over a covering — both branches lower to the same superclass, so the subsumption reads off the saturated query context as a single-literal clause. The query-concept band makes both disjuncts maximal, so resolution runs through each — the band relaxation's flagship row.</summary>
    [TestMethod]
    public void Cov1CoveringCaseAnalysisReadsOffTheSubsumption()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Union(Class("Birch"), Class("Cedar"))),
                SubClassOf(Class("Birch"), Class("Oak")),
                SubClassOf(Class("Cedar"), Class("Oak")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Oak")), "Case analysis over the covering entails Ash is an Oak.");
        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Birch")), "The covering does not entail the first disjunct.");
        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Spruce")), "Ash stays satisfiable; only the entailed subsumption reads off.");
    }

    /// <summary>UNI-1 (the maximal-set read-off): the two-step case analysis — the covering resolves to a shared superclass which then chains upward.</summary>
    [TestMethod]
    public void Uni1TwoStepCaseAnalysisReadsOffTheChainedSubsumption()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Union(Class("Birch"), Class("Cedar"))),
                SubClassOf(Class("Birch"), Class("Elm")),
                SubClassOf(Class("Cedar"), Class("Elm")),
                SubClassOf(Class("Elm"), Class("Fir")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Elm")), "The covering resolves to the shared superclass.");
        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Fir")), "The shared superclass chains upward.");
    }

    /// <summary>UNI-2 (the maximal-set read-off): disjunct absorption — one branch is subsumed by the other, so the disjunction collapses. Underivable under unique selection when the absorbing disjunct outranks the absorbed one; the band makes both maximal.</summary>
    [TestMethod]
    public void Uni2DisjunctAbsorptionReadsOffTheSubsumption()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Union(Class("Birch"), Class("Cedar"))),
                SubClassOf(Class("Birch"), Class("Cedar")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Cedar")), "The Birch branch is absorbed into Cedar; the disjunction collapses to a decided subsumption.");
        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Birch")), "The absorbed branch itself is not entailed.");
    }

    /// <summary>CARRY-1 (the maximal-set read-off): the whole pigeonhole apparatus runs UNDER a carried covering disjunct — equality literals outrank the band, so the merge machinery is never starved by the carry — and the dead branch peels away, leaving the surviving disjunct as a decided subsumption.</summary>
    [TestMethod]
    public void Carry1PigeonholeKillsTheCarriedBranchAndReadsOffTheSurvivor()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Union(Class("Cedar"), Class("Rowan"))),
                SubClassOf(Class("Rowan"), Max("feeds", 2, null)),
                SubClassOf(Class("Rowan"), Some("feeds", Class("B1"))),
                SubClassOf(Class("Rowan"), Some("feeds", Class("B2"))),
                SubClassOf(Class("Rowan"), Some("feeds", Class("B3"))),
                Disjoint(Class("B1"), Class("B2")),
                Disjoint(Class("B1"), Class("B3")),
                Disjoint(Class("B2"), Class("B3")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Cedar")), "Rowan is unsatisfiable by the pigeonhole, so the covering forces Cedar.");
        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Spruce")), "Ash keeps its Cedar model — the peel decides the subsumption, not an inconsistency.");
        Assert.IsFalse(engine.IsInconsistent, "The module is consistent.");
    }

    /// <summary>TAX-1 (the maximal-set read-off): inherited case analysis — a subclass of the covering's root inherits the resolved superclass through its own query context.</summary>
    [TestMethod]
    public void Tax1InheritedCaseAnalysisReadsOffThroughTheSubclass()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Pine",
            [
                SubClassOf(Class("Ash"), Union(Class("Birch"), Class("Cedar"))),
                SubClassOf(Class("Birch"), Class("Elm")),
                SubClassOf(Class("Cedar"), Class("Elm")),
                SubClassOf(Class("Pine"), Class("Ash")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "Pine"), Atom(clausification, "Elm")), "The subclass inherits the case analysis.");
        Assert.IsFalse(engine.IsSubsumedBy(Atom(clausification, "Elm"), Atom(clausification, "Ash")), "No reverse inclusion is entailed.");
    }

    /// <summary>REAL-FACTOR-COLLAPSE, the OWL-reachability half (the synthetic equality-factoring certificate lives beside the normalization pins): Factor stays INERT on the DL4-1 pigeonhole — the DOCUMENTED-INERT encoding fact the family measures, matching the reference shipping Factor inert on this fragment. The mechanism is derivable: EmitDl4 emits COMPLETE pairwise merge heads, and factoring any two equalities sharing the oriented maximal side replaces <c>s ≈ t</c> with <c>t ≉ t'</c> while the residual still carries the pair equality <c>t ≈ t'</c>, so every factored conclusion holds a complementary pair and drops as a tautology (Definition 4 condition 1). Factor earns its keep only on heads WITHOUT the pair equality — not an emission shape here (the synthetic witness certifies the rule's conclusion path on exactly that head); necessity arrives with nominals.</summary>
    [TestMethod]
    public void FactorStaysInertOnCompletePairwiseMergeHead()
    {
        ContextSaturationEngine engine = SaturateWithQuery(
            "Ash",
            [
                SubClassOf(Class("Ash"), Max("feeds", 2, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("B1"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B2"))),
                SubClassOf(Class("Ash"), Some("feeds", Class("B3"))),
                Disjoint(Class("B1"), Class("B2")),
                Disjoint(Class("B1"), Class("B3")),
                Disjoint(Class("B2"), Class("B3")),
                Bystander(),
            ],
            out ClausificationResult clausification);

        Assert.IsTrue(engine.IsSubsumedBy(Atom(clausification, "Ash"), Atom(clausification, "Spruce")), "The pigeonhole refutes: Ash is unsatisfiable.");
        Assert.AreEqual(0L, engine.BuildStatistics(contextDecided: true).FactorApplications, "Factor is inert on the complete pairwise merge head: every factored conclusion carries a complementary pair and drops as a tautology.");
    }

    /// <summary>The production flip: the production reasoner DECIDES disjunctive modules whole — the survey admits the positive union and the max-2 merge, the per-literal second gate passes their DL1 and DL4 heads, and the same engine core the below-gate pins certify answers through the seam.</summary>
    [TestMethod]
    public void ProductionReasonerDecidesDisjunctiveModules()
    {
        ModuleDecision union = ContextSaturationModuleReasoner.DecideModule(Module(SubClassOf(Class("Ash"), Union(Class("Birch"), Class("Cedar")))), TestContext.CancellationToken);
        Assert.IsTrue(union.Statistics.ContextTotals.ContextDecided, "A positive-union module is context-decided with the gates open.");
        Assert.IsTrue(union.Verdict!.IsConsistent, "The covering module is consistent.");

        ModuleDecision cardinality = ContextSaturationModuleReasoner.DecideModule(
            Module(
                SubClassOf(Class("Ash"), Max("feeds", 2, null)),
                SubClassOf(Class("Ash"), Some("feeds", Class("B1")))),
            TestContext.CancellationToken);
        Assert.IsTrue(cardinality.Statistics.ContextTotals.ContextDecided, "A max-2 module is context-decided with the gates open.");
        Assert.IsTrue(cardinality.Verdict!.IsConsistent, "One successor under max-2 is consistent.");
    }

    /// <summary>Clausifies the axioms, builds the engine BELOW the gates (no survey, no second gate), ensures the named query context, and saturates to the fixpoint.</summary>
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

    /// <summary>Clausifies the axioms and saturates WITHOUT a query context (ground-clash rows read <see cref="ContextSaturationEngine.IsInconsistent"/> only), running the Self-ghost pass as the production path does after a completed saturation.</summary>
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

    /// <summary>The unrelated Horn axiom minting the bystander classes the unsatisfiability reads use: an unsatisfiable query class is subsumed by the bystander, a satisfiable one is not.</summary>
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

    /// <summary>The reserved <c>owl:Nothing</c> reference.</summary>
    private static OwlClassReference NothingReference { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Nothing")));

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

    /// <summary>An existential restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>A union of class expressions.</summary>
    /// <param name="operands">The union operands.</param>
    /// <returns>The union.</returns>
    private static OwlObjectUnionOf Union(params OwlClassExpression[] operands)
    {
        return new OwlObjectUnionOf(operands);
    }

    /// <summary>A qualified or unqualified minimum-cardinality restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound n.</param>
    /// <param name="filler">The filler class, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Min(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Min, cardinality, Property(property), filler);
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

    /// <summary>A qualified or unqualified exact-cardinality restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound n.</param>
    /// <param name="filler">The filler class, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Exact(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Exact, cardinality, Property(property), filler);
    }

    /// <summary>A subclass axiom.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>A pairwise disjointness axiom.</summary>
    /// <param name="operands">The mutually disjoint expressions.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointClassesAxiom Disjoint(params OwlClassExpression[] operands)
    {
        return new OwlDisjointClassesAxiom(operands) { Origin = Origin("disjoint") };
    }

    /// <summary>A disjoint-union axiom defining a class as the disjoint union of its operands.</summary>
    /// <param name="definedClass">The defined class's local name.</param>
    /// <param name="operands">The member expressions.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointUnionAxiom DisjointUnion(string definedClass, params OwlClassExpression[] operands)
    {
        return new OwlDisjointUnionAxiom(new NamedNode(Utf8Strings.From(Example + definedClass)), operands) { Origin = Origin("disjointunion") };
    }

    /// <summary>A class assertion typing an individual.</summary>
    /// <param name="type">The asserted type.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, NamedNode individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }
}
