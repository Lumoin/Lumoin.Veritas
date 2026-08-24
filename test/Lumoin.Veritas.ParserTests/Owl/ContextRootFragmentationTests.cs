using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The per-individual root fragmentation battery (root-fragmentation spec
/// section 6): the fragmented topology behind the default-off
/// <see cref="RootContextTopology.PerIndividualRoots"/> mode is a performance
/// LAYOUT, never a semantic switch, and every row holds that line — the
/// topology-equivalence sweep re-runs the certified nominal battery under the
/// fragmented topology against the shipped defaults; the meeting, equality,
/// equality-chain, negative-meeting, and propagated-bottom rows are the
/// inter-nominal carrier's executable completeness witnesses (cross-individual
/// evidence meets on imaged premises, the read-off covers every nominal root);
/// the lazy-mint and Nom-tail rows pin the per-sibling mint resolver and the
/// stay-<c>y</c> tail; the latch row pins delegation-not-decision for
/// root-class data demands in all three faces; the anonymous-successor,
/// reflexive-fact, and f-image faces pin the nominal-root grammar's frozen pair
/// list live; the two-successor counting and fragmented-pigeonhole rows execute
/// the whole-head carrier assembly; the composition guard pins the
/// <c>Create</c>-time rejection; and the dark-ship guard pins the single-root
/// default byte-identical run to run. Any measured verdict or subsumption
/// divergence between the topologies is a defect, never a tuning input.
/// </summary>
[TestClass]
internal sealed class ContextRootFragmentationTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the row classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/rootfragmentation#";

    /// <summary>
    /// The one nominal-battery row whose fragmented run legitimately lands on
    /// the other side of the default inference ceiling — the domain-collapse
    /// habitat NOMR-1, where the central respelling regenerates the Nom rule's
    /// counting premises in every minted sibling's context (the calculus's Eq
    /// rule cannot rewrite variable occurrences, so the merge onto the central
    /// variable only inflates), the mint ladder deepens with the budget, and
    /// the redundant-offer fraction dominates the funnel. The single root
    /// terminates the same module cheaply because its ground orientation
    /// collapses generated-nominal spellings onto told constants and starves
    /// the re-fire. This is the Eq-restriction habitat measured as
    /// TERMINATION-BEARING on collapse modules, not an accelerator
    /// — the trigger's face, pinned exactly by
    /// <see cref="FragmentedNomrOneDivergesAtTheDefaultCeiling"/>.
    /// </summary>
    private const string BudgetDivergentRow = "NOMR-1";

    /// <summary>
    /// TOPOLOGY-EQUIVALENCE SWEEP: every certified nominal-battery row decided
    /// under <see cref="RootContextTopology.PerIndividualRoots"/> matches the
    /// shipped default row-by-row — outcome, verdict, context-decided path, and
    /// the EXACT module-local subsumption set — and the shipped run keeps the
    /// carrier counters dark. The topology-equivalence claim is MODULO BUDGET:
    /// the one named budget-divergent row is excluded here and pinned
    /// exactly by its own face. Costs of both topologies ride the log so a
    /// sibling-band movement names its row without a debugger.
    /// </summary>
    [TestMethod]
    public void FragmentedTopologyMatchesTheShippedDefaultAcrossTheNominalBattery()
    {
        StringBuilder report = new();
        report.AppendLine("\nrow | outcome | attempts default/fragmented | roots | carrier landed/absorbed | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool _, string[] _) in ContextNominalBatteryTests.BatteryRows())
        {
            if(string.Equals(name, BudgetDivergentRow, StringComparison.Ordinal))
            {
                continue;
            }

            ModuleDecision shipped = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
            ModuleDecision fragmented = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.PerIndividualRoots, RootPropagationRelevance.Unrestricted, ReasoningConfiguration.Default.Budget, engineProbe: null, TestContext.CancellationToken);
            ContextSaturationStatistics shippedTotals = shipped.Statistics.ContextTotals;
            ContextSaturationStatistics fragmentedTotals = fragmented.Statistics.ContextTotals;
            if(shippedTotals.InterNominalPropagations != 0 || shippedTotals.InterNominalRedundant != 0 || shippedTotals.NominalRootContexts > 1)
            {
                mismatches.Add(name + ": the shipped default run moved a fragmentation counter — the dark mode leaked.");
            }

            bool identical = shipped.Outcome == fragmented.Outcome
                && shippedTotals.ContextDecided == fragmentedTotals.ContextDecided
                && (shipped.Verdict is null) == (fragmented.Verdict is null)
                && (shipped.Verdict is null || shipped.Verdict.IsConsistent == fragmented.Verdict!.IsConsistent)
                && (shipped.Verdict is null || KeysEqual(SubsumptionKeys(shipped.Verdict), SubsumptionKeys(fragmented.Verdict!)));
            report.AppendLine(name + " | " + shipped.Outcome + "/" + fragmented.Outcome + " | " + shippedTotals.InferenceAttempts + "/" + fragmentedTotals.InferenceAttempts
                + " | " + fragmentedTotals.NominalRootContexts
                + " | " + fragmentedTotals.InterNominalPropagations + "/" + fragmentedTotals.InterNominalRedundant
                + " | consistent=" + Describe(shipped) + "/" + Describe(fragmented)
                + " | decidedByContext=" + shippedTotals.ContextDecided + "/" + fragmentedTotals.ContextDecided
                + " | " + (identical ? "OK" : "MISMATCH"));
            if(!identical)
            {
                mismatches.Add(name + ": the fragmented topology diverged from the shipped default (outcome " + shipped.Outcome + " vs " + fragmented.Outcome + ").");
            }
        }

        TestContext.WriteLine(report.ToString());
        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>
    /// THE NAMED BUDGET-DIVERGENT ROW, pinned exactly (partial success is named,
    /// never hidden): the shipped default DECIDES the NOMR-1 domain collapse
    /// consistent within the default ceiling; the fragmented topology spends the
    /// same ceiling in the minted siblings' regenerated counting premises and
    /// honestly ABSTAINS, its mint ladder deepening with the budget — the
    /// measured Eq-restriction trigger (see
    /// <see cref="BudgetDivergentRow"/>). Movement on EITHER side of this pin is
    /// a signal: the fragmented side deciding means the trigger's behavior
    /// changed; the shipped side abstaining means the default regressed.
    /// </summary>
    [TestMethod]
    public void FragmentedNomrOneDivergesAtTheDefaultCeiling()
    {
        ReasoningModule module = Module(
            SubClassOf(Thing, SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("r", 1, null)),
            ClassAssertion(Class("P"), Individual("o")),
            ClassAssertion(Class("C"), Individual("i1")));
        ModuleDecision shipped = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ModuleDecision fragmented = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.PerIndividualRoots, RootPropagationRelevance.Unrestricted, ReasoningConfiguration.Default.Budget, engineProbe: null, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, shipped.Outcome, "The shipped default decides the collapse within the ceiling.");
        Assert.IsTrue(shipped.Verdict!.IsConsistent, "The collapse module is consistent.");
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, fragmented.Outcome, "The fragmented topology abstains at the same ceiling — the pinned Eq-restriction-trigger divergence; a decide here means the trigger's behavior changed.");
        Assert.IsNull(fragmented.Verdict, "A budget abstention carries no verdict.");
        TestContext.WriteLine(CarrierCounterLine("Nomr1Divergence", fragmented.Statistics.ContextTotals));
    }

    /// <summary>
    /// MEETING ROW (the spec's 1.2 schema): a two-individual role module whose
    /// verdict REQUIRES the inter-nominal carrier — the told edge seeds a's
    /// context, b's typing lives in b's context, and the existential clash fires
    /// only when the carried image lands the edge where the typing lives. Both
    /// topologies decide INCONSISTENT; the fragmented run consumes both carrier
    /// counters — the second derivation route re-offers the same image, which
    /// the redundancy discipline must absorb.
    /// </summary>
    [TestMethod]
    public void MeetingRowCrossIndividualEvidenceMeetsThroughTheCarrier()
    {
        ReasoningModule module = Module(
            Edge("r", "a", "b"),
            SubClassOf(OneOf("a"), HasValue("r", "b")),
            ClassAssertion(Class("C"), Individual("b")),
            SubClassOf(Some("r", Class("C")), Class("D")),
            SubClassOf(Class("D"), NothingReference),
            Bystander());
        ModuleDecision shipped = DecideShipped(module);
        ModuleDecision fragmented = DecideFragmented(module);

        AssertTopologiesIdentical(shipped, fragmented);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, shipped.Outcome, "The meeting module is decided.");
        Assert.IsFalse(shipped.Verdict!.IsConsistent, "The cross-individual existential clash decides the module inconsistent.");
        ContextSaturationStatistics totals = fragmented.Statistics.ContextTotals;
        TestContext.WriteLine(CarrierCounterLine("Meeting", totals));
        Assert.IsGreaterThan(0L, totals.InterNominalPropagations, "The carrier landed cross-individual images — the meeting cannot close without them.");
        Assert.IsGreaterThan(0L, totals.InterNominalRedundant, "The duplicate derivation route re-offers the same image and the redundancy discipline absorbs it — the cascade-convergence counter's consuming assertion.");
    }

    /// <summary>EQUALITY ROW: a <c>SameIndividual</c> module where the merge is verdict-bearing through the two-step carrier derivation — the equality seeds one context, the contradictory typings sit split, and the fold's images meet them. Both topologies decide INCONSISTENT; the fragmented run lands carrier images.</summary>
    [TestMethod]
    public void EqualityRowSameIndividualMergeIsVerdictBearing()
    {
        ReasoningModule module = Module(
            InertNominal(),
            Same("o1", "o2"),
            ClassAssertion(Class("P"), Individual("o1")),
            ClassAssertion(Complement(Class("P")), Individual("o2")));
        ModuleDecision shipped = DecideShipped(module);
        ModuleDecision fragmented = DecideFragmented(module);

        AssertTopologiesIdentical(shipped, fragmented);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, shipped.Outcome, "The equality module is decided.");
        Assert.IsFalse(shipped.Verdict!.IsConsistent, "The merged individual carries both the class and its complement.");
        ContextSaturationStatistics totals = fragmented.Statistics.ContextTotals;
        TestContext.WriteLine(CarrierCounterLine("Equality", totals));
        Assert.IsGreaterThan(0L, totals.InterNominalPropagations, "The two-step derivation images the merge across the nominal roots.");
    }

    /// <summary>
    /// EQUALITY-CHAIN ROW: the round-1 counterexample executable —
    /// <c>SameIndividual(o1,o2) + SameIndividual(o2,o3) +
    /// DifferentIndividuals(o1,o3)</c>, jointly inconsistent over three
    /// individuals. The middle context folds the zero-<c>x</c> equality
    /// <c>o1 ≈ o3</c>, which only the per-distinct-foreign whole-head firing
    /// images into BOTH endpoint contexts where the seeded inequality clashes; a
    /// single-foreign or strand-multi-foreign carrier strands the fold and
    /// wrongly reports CONSISTENT.
    /// </summary>
    [TestMethod]
    public void EqualityChainRowZeroXFoldImagesIntoBothEndpoints()
    {
        ReasoningModule module = Module(
            InertNominal(),
            Same("o1", "o2"),
            Same("o2", "o3"),
            Different("o1", "o3"));
        ModuleDecision shipped = DecideShipped(module);
        ModuleDecision fragmented = DecideFragmented(module);

        AssertTopologiesIdentical(shipped, fragmented);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, shipped.Outcome, "The equality-chain module is decided.");
        Assert.IsFalse(shipped.Verdict!.IsConsistent, "The chained merges contradict the pairwise distinctness.");
        TestContext.WriteLine(CarrierCounterLine("EqualityChain", fragmented.Statistics.ContextTotals));
    }

    /// <summary>
    /// NEGATIVE-MEETING ROW: a negative told fact whose clash partner is
    /// derivable only in the FOREIGN context — the inverse existential derives
    /// the denied edge at its target's context, and the denial's home receives
    /// the clash-completing image via the carrier while the negative fact
    /// itself, being empty-head, never images. Both topologies decide
    /// INCONSISTENT. (An alias-merged variant of this row — the positive edge
    /// under a <c>SameIndividual</c> respelling of the denied target — exposes a
    /// SINGLE-ROOT completeness gap: the merged spelling reaches the negative
    /// body only under one equality orientation, so the shipped engine wrongly
    /// reports CONSISTENT there. This row deliberately witnesses the carrier
    /// without riding the broken shape.)
    /// </summary>
    [TestMethod]
    public void NegativeMeetingRowPositiveSideCarriersIntoTheDenialsHome()
    {
        ReasoningModule module = Module(
            SubClassOf(OneOf("p"), SomeInverse("s", OneOf("o"))),
            NegativeEdge("s", "o", "p"),
            Bystander());
        ModuleDecision shipped = DecideShipped(module);
        ModuleDecision fragmented = DecideFragmented(module);

        AssertTopologiesIdentical(shipped, fragmented);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, shipped.Outcome, "The negative-meeting module is decided.");
        Assert.IsFalse(shipped.Verdict!.IsConsistent, "The inverse existential forces the denied edge.");
        TestContext.WriteLine(CarrierCounterLine("NegativeMeeting", fragmented.Statistics.ContextTotals));
    }

    /// <summary>
    /// PROPAGATED-BOTTOM ROW: the clash pair sits split across THREE
    /// individuals, so the <c>⊥</c>-completing image reaches its read-off
    /// context only through a multi-hop carrier relay — the coverage witness for
    /// the read-off loop over EVERY nominal root: a scan that dropped one
    /// root-class context would silently lose this verdict. The scattered
    /// bound-two pigeonhole variant is the second face and rides
    /// <see cref="PignFragmentedRowBoundTwoPigeonholeDecidesInconsistent"/>.
    /// </summary>
    [TestMethod]
    public void PropagatedBottomRowMultiHopRelayReachesTheForeignReadOff()
    {
        ReasoningModule module = Module(
            InertNominal(),
            Same("o1", "o2"),
            Same("o2", "o3"),
            ClassAssertion(Class("P"), Individual("o1")),
            ClassAssertion(Complement(Class("P")), Individual("o3")));
        ModuleDecision shipped = DecideShipped(module);
        ModuleDecision fragmented = DecideFragmented(module);

        AssertTopologiesIdentical(shipped, fragmented);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, shipped.Outcome, "The relay module is decided.");
        Assert.IsFalse(shipped.Verdict!.IsConsistent, "The typing meets its complement across the two-hop merge chain.");
        TestContext.WriteLine(CarrierCounterLine("PropagatedBottom", fragmented.Statistics.ContextTotals));
    }

    /// <summary>
    /// LAZY-MINT ROW: the anonymous-predecessor Nom habitat (inverse role +
    /// nominal + counting) mints generated-nominal siblings mid-saturation;
    /// under the fragmented topology each minted sibling resolves its OWN
    /// nominal-root context inside the mint loop — an anchor-bound seed mutation
    /// fails this face — so the root-class population counts the told
    /// individuals plus every minted sibling, and the population statistic is
    /// consumed here.
    /// </summary>
    [TestMethod]
    public void LazyMintRowMintedSiblingsResolveDistinctNominalRoots()
    {
        ReasoningModule module = NomMintModule();
        ModuleDecision shipped = DecideShipped(module);
        ModuleDecision fragmented = DecideFragmented(module);

        AssertTopologiesIdentical(shipped, fragmented);
        ContextSaturationStatistics totals = fragmented.Statistics.ContextTotals;
        TestContext.WriteLine(CarrierCounterLine("LazyMint", totals));
        Assert.IsGreaterThan(0, totals.GeneratedNominals, "The Nom rule minted generated nominals under the fragmented topology.");
        Assert.AreEqual(1, shipped.Statistics.ContextTotals.NominalRootContexts, "The shipped default holds the one distinguished root context.");
        Assert.IsGreaterThan(totals.GeneratedNominals, totals.NominalRootContexts, "Every told individual has its nominal root beside the minted siblings' contexts.");
        Assert.AreEqual(totals.GeneratedNominals + ToldIndividualCount(module), totals.NominalRootContexts, "Each minted sibling resolved a DISTINCT nominal root beside the told individuals' contexts — an anchor-bound mint seed would collapse them.");
    }

    /// <summary>
    /// LATCH ROW, two faces (the lift flip): the home face carries a GENUINELY
    /// UNSATISFIABLE unconditional
    /// UNIT demand at one individual (an empty facet interval), so the per-constant
    /// root arm decides its ≈-class and the module decides a context INCONSISTENCY —
    /// the decide the arm's flip enables. The foreign face's disjunctive marker head
    /// does not narrow to a unit, so it is an undecided per-constant obligation the
    /// arm delegates named through <c>DataObligationUndecidedOnRoot</c>; the carrier
    /// ships the marker-bearing image into the foreign individual's context BESIDE
    /// the home-spelled landing, and the carrier landing is asserted so the imaged
    /// route is a witnessed, not assumed, trigger surface. Home decides and foreign
    /// delegates identically across both topologies.
    /// </summary>
    [TestMethod]
    public void RootDataDemandHomeUnitDecidesInconsistentForeignDisjunctiveDelegatesInBothTopologies()
    {
        ReasoningModule homeFace = Module(
            InertNominal(),
            SubClassOf(OneOf("d"), DataSome("dp", EmptyIntegerRange())));
        ReasoningModule foreignFace = Module(
            InertNominal(),
            ClassAssertion(Class("A"), Individual("d")),
            SubClassOf(Class("A"), Union(DataSome("dp", EmptyIntegerRange()), OneOf("e"))));

        ModuleDecision homeShipped = DecideShipped(homeFace);
        ModuleDecision homeFragmented = DecideFragmented(homeFace);
        ModuleDecision foreignShipped = DecideShipped(foreignFace);
        ModuleDecision foreignFragmented = DecideFragmented(foreignFace);

        Assert.IsTrue(homeShipped.Statistics.ContextTotals.ContextDecided, "The home-face unit demand is decided by the per-constant root arm under the shipped default.");
        Assert.IsFalse(homeShipped.Verdict!.IsConsistent, "The empty-facet unit demand at d is unsatisfiable, so the per-constant arm closes the class and the module decides INCONSISTENT.");
        Assert.IsTrue(homeFragmented.Statistics.ContextTotals.ContextDecided, "The home-face unit demand is decided under the fragmented topology too.");
        Assert.IsFalse(homeFragmented.Verdict!.IsConsistent, "The fragmented topology closes the same unsatisfiable unit-demand class — INCONSISTENT.");
        Assert.IsFalse(foreignShipped.Statistics.ContextTotals.ContextDecided, "The foreign-face disjunctive marker does not narrow, so it is an undecided per-constant obligation the arm delegates named.");
        Assert.IsFalse(foreignFragmented.Statistics.ContextTotals.ContextDecided, "The foreign-face disjunctive marker delegates under the fragmented topology — the imaged and home-spelled markers route the same undecided delegation.");
        Assert.AreEqual(homeShipped.Outcome, homeFragmented.Outcome, "The home face's outcome is topology-identical.");
        Assert.AreEqual(foreignShipped.Outcome, foreignFragmented.Outcome, "The foreign face's outcome is topology-identical.");

        //A delegated decision drops the context engine's totals by contract, so the
        //imaged trigger surface is witnessed below the reasoner gates: the fragmented
        //saturation of the foreign face lands carrier images AND records the
        //root-data-demand activity statistic on the nominal-root marker landings.
        ClausificationResult clausification = ContextClausifier.Clausify(foreignFace);
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification, DatatypeRegistry.Empty, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.Unrestricted, RootContextTopology.PerIndividualRoots);
        Assert.AreEqual(SaturationOutcome.Completed, engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken), "The foreign face saturates to its fixpoint below the gates.");
        Assert.IsTrue(engine.RootDataDemandObserved, "The activity statistic records the nominal-root marker landings.");
        Assert.IsGreaterThan(0L, engine.BuildStatistics(contextDecided: false).InterNominalPropagations, "The foreign face's marker-bearing disjunction images across the carrier — the imaged trigger surface is witnessed by a landing, not assumed.");
    }

    /// <summary>
    /// ANONYMOUS-SUCCESSOR ROW: the value restriction drives r-Succ's
    /// context-variable seeds into the nominal root and r-Pred carries the
    /// consequence back over <c>y</c> — the subsumption face reads it off — and
    /// the symmetric successor exercise lands the Pred-direction reverse pair
    /// inside the nominal root. Both topologies agree on the whole face set with
    /// a clean out-of-grammar record; a grammar that omitted the context
    /// variable or the reverse function pair would latch here.
    /// </summary>
    [TestMethod]
    public void AnonymousSuccessorRowContextVariableSeedsRoundTrip()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("W"), HasValue("r", "o")),
            SubClassOf(OneOf("o"), Class("B")),
            SubClassOf(Some("r", Class("B")), Class("D")),
            SubClassOf(OneOf("o"), Some("t", Class("E"))),
            Symmetric("t"),
            Bystander());
        ModuleDecision shipped = DecideShipped(module);
        ModuleDecision fragmented = DecideFragmented(module);

        AssertTopologiesIdentical(shipped, fragmented);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, shipped.Outcome, "The anonymous-successor module is decided.");
        Assert.Contains(Sub("W", "D"), SubsumptionKeys(shipped.Verdict!), "The context-variable round trip lands the consequence: W's r-successor o is a B, so W is a D.");
        Assert.AreEqual(0L, shipped.Statistics.ContextTotals.OutOfGrammarConclusions, "The shipped default derives within its grammar.");
        Assert.AreEqual(0L, fragmented.Statistics.ContextTotals.OutOfGrammarConclusions, "The fragmented run derives within the nominal-root grammar — the stay-y seeds and the reverse Pred pair are admitted shapes.");
    }

    /// <summary>REFLEXIVE-FACT FACE: a reflexive told fact on a nominal individual maps BOTH slots to the central variable under the entry translation — the <c>(x,x)</c> pair of the frozen list — with no out-of-grammar latch and verdict identity.</summary>
    [TestMethod]
    public void ReflexiveFactFaceMapsBothSlotsCentral()
    {
        ReasoningModule module = Module(
            InertNominal(),
            Edge("r", "o", "o"),
            SubClassOf(Some("r", OneOf("o")), Class("G")),
            Bystander());
        ModuleDecision shipped = DecideShipped(module);
        ModuleDecision fragmented = DecideFragmented(module);

        AssertTopologiesIdentical(shipped, fragmented);
        Assert.AreEqual(0L, fragmented.Statistics.ContextTotals.OutOfGrammarConclusions, "The self-edge respells as the (x,x) pair, which the nominal-root grammar admits.");
    }

    /// <summary>
    /// F-IMAGE FACE: an own-Skolem-bearing head with a foreign mention —
    /// <c>⊤ → f(x) ≈ b</c> from the nominal-filler existential — drives the
    /// carrier's kind conversion executably: the grounding step promotes
    /// <c>f(x)</c> to <c>f(a)</c> into b's context, and the reciprocal image
    /// demotes it back, which the redundancy discipline absorbs. The frozen pair
    /// list's function-of-individual entries are covered by a run, and the
    /// verdict face stays topology-identical with a clean grammar record.
    /// </summary>
    [TestMethod]
    public void FImageFaceKindConversionCrossesTheCarrier()
    {
        ReasoningModule module = Module(
            SubClassOf(OneOf("a"), Some("t", OneOf("b"))),
            SubClassOf(OneOf("b"), Class("Q")),
            Bystander());
        ModuleDecision shipped = DecideShipped(module);
        ModuleDecision fragmented = DecideFragmented(module);

        AssertTopologiesIdentical(shipped, fragmented);
        ContextSaturationStatistics totals = fragmented.Statistics.ContextTotals;
        TestContext.WriteLine(CarrierCounterLine("FImage", totals));
        Assert.IsGreaterThan(0L, totals.InterNominalPropagations, "The f-bearing equality head images across the carrier with its kind conversion.");
        Assert.AreEqual(0L, totals.OutOfGrammarConclusions, "The promoted and demoted function images are admitted nominal-root shapes.");
    }

    /// <summary>TWO-SUCCESSOR COUNTING ROW: one predecessor holds told edges to two named individuals through a functional role — the <c>R(y,o1) ∧ R(y,o2) → o1 ≈ o2</c> shape — and the told distinctness contradicts the forced merge. Both topologies decide INCONSISTENT; the carrier assembly relocates the merge evidence under fragmentation.</summary>
    [TestMethod]
    public void TwoSuccessorCountingRowMergesNamedSuccessorsAcrossContexts()
    {
        ReasoningModule module = Module(
            InertNominal(),
            Edge("r", "a", "b1"),
            Edge("r", "a", "b2"),
            Functional("r"),
            Different("b1", "b2"));
        ModuleDecision shipped = DecideShipped(module);
        ModuleDecision fragmented = DecideFragmented(module);

        AssertTopologiesIdentical(shipped, fragmented);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, shipped.Outcome, "The two-successor counting module is decided.");
        Assert.IsFalse(shipped.Verdict!.IsConsistent, "The functional merge contradicts the told distinctness.");
        TestContext.WriteLine(CarrierCounterLine("TwoSuccessor", fragmented.Statistics.ContextTotals));
    }

    /// <summary>
    /// PIGN-FRAGMENTED ROW: a genuinely inconsistent bound-two three-successor
    /// pigeonhole (the PIGN-2 family shape) decided under BOTH topologies with
    /// verdict identity — landing identity alone does not entail resolution, so
    /// the row executes the whole-head carrier chain end to end: the counting
    /// bridge assembles subject-side, the pairwise-equality disjunction
    /// relocates whole, each successor resolves its disjunct against the seeded
    /// distinctness, and the shrinking residual re-images until the clash
    /// closes. A single-literal carrier flips this row. The counting-hygiene
    /// face rides along: the ground rider never decides under nominal
    /// jurisdiction, in either topology.
    /// </summary>
    [TestMethod]
    public void PignFragmentedRowBoundTwoPigeonholeDecidesInconsistent()
    {
        ReasoningModule module = Module(
            InertNominal(),
            SubClassOf(Class("A"), Max("r", 2, null)),
            ClassAssertion(Class("A"), Individual("a")),
            Edge("r", "a", "b1"),
            Edge("r", "a", "b2"),
            Edge("r", "a", "b3"),
            Different("b1", "b2", "b3"));
        ModuleDecision shipped = DecideShipped(module);
        ModuleDecision fragmented = DecideFragmented(module);

        AssertTopologiesIdentical(shipped, fragmented);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, shipped.Outcome, "The pigeonhole module is decided.");
        Assert.IsFalse(shipped.Verdict!.IsConsistent, "Three pairwise-distinct successors under a told bound of two clash.");
        Assert.AreEqual(0, shipped.Statistics.ContextTotals.GroundCountingClashes, "The ground counting rider never decides under nominal jurisdiction — the general path owns the clash.");
        Assert.AreEqual(0, fragmented.Statistics.ContextTotals.GroundCountingClashes, "The rider stays jurisdiction-disjoint under the fragmented topology too.");
        TestContext.WriteLine(CarrierCounterLine("PignFragmented", fragmented.Statistics.ContextTotals));
    }

    /// <summary>NOM-TAIL ROW: the mint module's conclusion keeps the stay-<c>y</c> tail — a <c>y</c>-centralizing mutation would assert the anchor equal to its own minted successor, flipping the verdict or latching the grammar — so the face is verdict identity plus a clean grammar record on the minting run.</summary>
    [TestMethod]
    public void NomTailRowMintKeepsTheStayYTail()
    {
        ReasoningModule module = NomMintModule();
        ModuleDecision shipped = DecideShipped(module);
        ModuleDecision fragmented = DecideFragmented(module);

        AssertTopologiesIdentical(shipped, fragmented);
        ContextSaturationStatistics totals = fragmented.Statistics.ContextTotals;
        Assert.IsGreaterThan(0, totals.GeneratedNominals, "The Nom rule fired and minted under the fragmented topology.");
        Assert.AreEqual(0L, totals.OutOfGrammarConclusions, "The post-mint tail keeps the context variable — the stay-y shapes are in-grammar.");
    }

    /// <summary>COMPOSITION GUARD: <see cref="RootContextTopology.PerIndividualRoots"/> combined with <see cref="RootPropagationRelevance.GroundFiltered"/> is rejected at <c>Create</c> — the filter's ground-conjunct indexes are defined over the single shared root table.</summary>
    [TestMethod]
    public void CompositionGuardRejectsFragmentedGroundFiltered()
    {
        ClausificationResult clausification = ContextClausifier.Clausify(Module(InertNominal(), Bystander()));

        Assert.Throws<ArgumentException>(() => ContextSaturationEngine.Create(clausification, DatatypeRegistry.Empty, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.GroundFiltered, RootContextTopology.PerIndividualRoots));
    }

    /// <summary>
    /// DARK-SHIP GUARD: a ROOT-BEARING module (the root-exchange shape the
    /// resolver surgery touches — the single root now mints on first need) run
    /// twice under the shipped default with a run-to-run identity assertion over
    /// allocation bytes, the verdict, and the four root telemetry fields; the
    /// carrier counters stay at zero and the root-class population stays one.
    /// The nominal-free soak cannot see this path, so the guard is the
    /// battery's own instrument. The windows read a thread-local allocation
    /// counter and the steadiness check tolerates shared-cache growth landing
    /// inside a window, so the test runs in the parallel suite.
    /// </summary>
    [TestMethod]
    public void DarkShipGuardKeepsTheSingleRootDefaultByteIdentical()
    {
        ReasoningModule module = Module(
            Equivalent(Class("C1"), OneOf("o")),
            SubClassOf(Class("A"), Some("r", Class("C1"))),
            SubClassOf(Class("A"), All("r", Class("D"))),
            SubClassOf(Class("D"), Class("E")),
            ClassAssertion(Class("A"), Individual("a")),
            SubClassOf(Some("r", Class("E")), Class("F")));
        DecideShipped(module);

        //The byte face demands a STEADY STATE, not a lucky pair: inside the parallel
        //suite a shared cache's growth threshold or a collection boundary can land
        //inside measured windows on this thread — the benign shape is a recurring
        //toggle between two byte counts — so five windows are measured and SOME
        //window byte count must RECUR. A genuine resolver leak grows every window
        //and leaves all five distinct, and it still shows in the cross-record
        //soak's reconciled deltas, while the counters and the verdict must be
        //identical across EVERY window.
        int threadBefore = Environment.CurrentManagedThreadId;
        const int windowCount = 5;
        long[] windowBytes = new long[windowCount];
        ModuleDecision[] decisions = new ModuleDecision[windowCount];
        for(int i = 0; i < windowCount; i++)
        {
            long start = GC.GetAllocatedBytesForCurrentThread();
            decisions[i] = DecideShipped(module);
            windowBytes[i] = GC.GetAllocatedBytesForCurrentThread() - start;
        }

        Assert.AreEqual(threadBefore, Environment.CurrentManagedThreadId, "The identity instrument is valid only on one thread.");
        bool steady = false;
        for(int i = 1; i < windowCount; i++)
        {
            for(int j = 0; j < i; j++)
            {
                steady |= windowBytes[i] == windowBytes[j];
            }
        }

        Assert.IsTrue(steady, "Some measured window byte count recurs — the shipped default reaches its allocation steady state, where a resolver leak leaves every window distinct (observed: " + string.Join("/", windowBytes) + ").");
        ContextSaturationStatistics firstTotals = decisions[0].Statistics.ContextTotals;
        Assert.IsGreaterThan(0L, firstTotals.RootSuccApplications, "The guard module is genuinely root-bearing — the root exchange fired.");
        Assert.AreEqual(0L, firstTotals.InterNominalPropagations, "The carrier never fires under the shipped default.");
        Assert.AreEqual(0L, firstTotals.InterNominalRedundant, "The carrier absorbs nothing under the shipped default.");
        Assert.AreEqual(1, firstTotals.NominalRootContexts, "The shipped default's root class is the one distinguished root.");
        for(int i = 1; i < windowCount; i++)
        {
            ContextSaturationStatistics totals = decisions[i].Statistics.ContextTotals;
            Assert.AreEqual(decisions[0].Verdict!.IsConsistent, decisions[i].Verdict!.IsConsistent, "The verdict is run-to-run identical.");
            Assert.AreEqual(firstTotals.RootContextClauses, totals.RootContextClauses, "The root clause watermark is run-to-run identical.");
            Assert.AreEqual(firstTotals.RootEdges, totals.RootEdges, "The root edge total is run-to-run identical.");
            Assert.AreEqual(firstTotals.RootSuccApplications, totals.RootSuccApplications, "The r-Succ total is run-to-run identical.");
            Assert.AreEqual(firstTotals.RootPredApplications, totals.RootPredApplications, "The r-Pred total is run-to-run identical.");
        }
    }

    /// <summary>WEDGE + NOM-WEDGE FOLDS: the budget-honesty wedge exhaustions hold their faces under the fragmented topology — both abstain at the same finite ceilings with no verdict, exactly as the shipped defaults pin them, so the topology is measured a non-mover on the wedge family.</summary>
    [TestMethod]
    public void WedgeAndNomWedgeFoldsHoldUnderBothTopologies()
    {
        ReasoningModule wedge = ContextSaturationModuleReasonerTests.WedgeTowerModule(ContextSaturationModuleReasonerTests.WedgeCeilingSize);
        ModuleDecision wedgeDecision = ContextSaturationModuleReasoner.DecideModule(wedge, EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.PerIndividualRoots, RootPropagationRelevance.Unrestricted, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: ContextSaturationModuleReasonerTests.WedgeCeiling), engineProbe: null, TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, wedgeDecision.Outcome, "The finite ceiling abstains on the wedge under the fragmented topology exactly as under the shipped default.");
        Assert.IsNull(wedgeDecision.Verdict, "A budget abstention carries no verdict.");

        ReasoningModule nomWedge = ContextSaturationModuleReasonerTests.NomWedgeTowerModule(ContextSaturationModuleReasonerTests.NomWedgeCeilingSize);
        ModuleDecision nomWedgeDecision = ContextSaturationModuleReasoner.DecideModule(nomWedge, EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.PerIndividualRoots, RootPropagationRelevance.Unrestricted, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: ContextSaturationModuleReasonerTests.NomWedgeCeiling), engineProbe: null, TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, nomWedgeDecision.Outcome, "The finite ceiling abstains on the nominal wedge under the fragmented topology exactly as under the shipped default.");
        Assert.IsNull(nomWedgeDecision.Verdict, "A budget abstention carries no verdict.");
    }

    /// <summary>Decides a module through the production reasoner under the shipped defaults and an unbounded budget.</summary>
    /// <param name="module">The module.</param>
    /// <returns>The decision.</returns>
    private ModuleDecision DecideShipped(ReasoningModule module)
    {
        return ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken);
    }

    /// <summary>Decides a module through the production reasoner under the fragmented topology and an unbounded budget.</summary>
    /// <param name="module">The module.</param>
    /// <returns>The decision.</returns>
    private ModuleDecision DecideFragmented(ReasoningModule module)
    {
        return ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.PerIndividualRoots, RootPropagationRelevance.Unrestricted, ReasoningBudget.Unbounded, engineProbe: null, TestContext.CancellationToken);
    }

    /// <summary>Asserts two module decisions identical on outcome, context-decided path, verdict, and the exact subsumption set, with the shipped run's fragmentation counters dark.</summary>
    /// <param name="shipped">The shipped-default decision.</param>
    /// <param name="fragmented">The fragmented-topology decision.</param>
    private static void AssertTopologiesIdentical(ModuleDecision shipped, ModuleDecision fragmented)
    {
        Assert.AreEqual(shipped.Outcome, fragmented.Outcome, "The decision outcome is topology-identical.");
        Assert.AreEqual(shipped.Statistics.ContextTotals.ContextDecided, fragmented.Statistics.ContextTotals.ContextDecided, "The context-decided path is topology-identical.");
        Assert.AreEqual(shipped.Verdict is null, fragmented.Verdict is null, "Verdict presence is topology-identical.");
        Assert.AreEqual(0L, shipped.Statistics.ContextTotals.InterNominalPropagations, "The carrier never fires under the shipped default.");
        if(shipped.Verdict is not null)
        {
            Assert.AreEqual(shipped.Verdict.IsConsistent, fragmented.Verdict!.IsConsistent, "The consistency verdict is topology-identical.");
            List<string> shippedKeys = SubsumptionKeys(shipped.Verdict);
            List<string> fragmentedKeys = SubsumptionKeys(fragmented.Verdict);
            Assert.IsTrue(KeysEqual(shippedKeys, fragmentedKeys), "The exact subsumption sets are topology-identical (shipped: " + string.Join(", ", shippedKeys) + " | fragmented: " + string.Join(", ", fragmentedKeys) + ").");
        }
    }

    /// <summary>A decision's consistency face as a log token: the verdict's consistency, or the outcome name when no verdict rides it.</summary>
    /// <param name="decision">The decision.</param>
    /// <returns>The token.</returns>
    private static string Describe(ModuleDecision decision)
    {
        return decision.Verdict is null ? decision.Outcome.ToString() : decision.Verdict.IsConsistent.ToString();
    }

    /// <summary>One log line of the fragmentation counters for a named row.</summary>
    /// <param name="name">The row label.</param>
    /// <param name="totals">The decision's context totals.</param>
    /// <returns>The line.</returns>
    private static string CarrierCounterLine(string name, ContextSaturationStatistics totals)
    {
        return name + ": roots=" + totals.NominalRootContexts + " landed=" + totals.InterNominalPropagations + " absorbed=" + totals.InterNominalRedundant
            + " rsucc=" + totals.RootSuccApplications + " rpred=" + totals.RootPredApplications + " nom=" + totals.NomApplications
            + " generated=" + totals.GeneratedNominals + " attempts=" + totals.InferenceAttempts;
    }

    /// <summary>The anonymous-predecessor Nom mint habitat: an inverse role, a nominal, and an inverse counting bound force the anonymous element onto a minted sibling of the constant's successors — the module that fires the Nom rule and mints under both topologies.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NomMintModule()
    {
        return Module(
            Inverse("r", "rInv"),
            SubClassOf(Class("B"), Some("s", Class("A"))),
            SubClassOf(Class("A"), HasValue("r", "o")),
            SubClassOf(OneOf("o"), MaxInverse("r", 1, null)),
            ClassAssertion(Class("B"), Individual("w")),
            Bystander());
    }

    /// <summary>The told-individual count of a module — the named individuals its assertions intern — read through a fresh clausification's symbol table.</summary>
    /// <param name="module">The module.</param>
    /// <returns>The told individual count.</returns>
    private static int ToldIndividualCount(ReasoningModule module)
    {
        return ContextClausifier.Clausify(module).Symbols.IndividualCount;
    }

    /// <summary>The verdict's subsumption pairs as sorted comparison keys, one <c>subIri-&gt;superIri</c> string per pair.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The keys, sorted ordinally.</returns>
    private static List<string> SubsumptionKeys(ModuleVerdict verdict)
    {
        List<string> keys = new(verdict.Subsumptions.Count);
        foreach((NamedNode subClass, NamedNode superClass) in verdict.Subsumptions)
        {
            keys.Add($"{subClass.Iri}->{superClass.Iri}");
        }

        keys.Sort(StringComparer.Ordinal);

        return keys;
    }

    /// <summary>Whether two sorted key lists are equal element-wise.</summary>
    /// <param name="expected">The first sorted list.</param>
    /// <param name="actual">The second sorted list.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    private static bool KeysEqual(List<string> expected, List<string> actual)
    {
        if(expected.Count != actual.Count)
        {
            return false;
        }

        for(int i = 0; i < expected.Count; i++)
        {
            if(!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A sorted subsumption key over two example-namespace local names.</summary>
    /// <param name="sub">The subclass local name.</param>
    /// <param name="super">The superclass local name.</param>
    /// <returns>The <c>subIri-&gt;superIri</c> key.</returns>
    private static string Sub(string sub, string super)
    {
        return Example + sub + "->" + Example + super;
    }

    /// <summary>The unrelated Horn axiom minting the bystander classes the guard reads use.</summary>
    /// <returns>The bystander axiom.</returns>
    private static OwlSubClassOfAxiom Bystander()
    {
        return SubClassOf(Class("Spruce"), Class("Willow"));
    }

    /// <summary>An inert nominal mention — a fresh disconnected class subsumed by a singleton enumeration — that gives an otherwise nominal-free module its nominal jurisdiction, routing the ABox through the root tier. Semantically inert: it adds no named subsumption and changes no verdict.</summary>
    /// <returns>The inert nominal axiom.</returns>
    private static OwlSubClassOfAxiom InertNominal()
    {
        return SubClassOf(Class("NominalAnchor"), OneOf("nominalAnchorPoint"));
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

    /// <summary>The <c>owl:Nothing</c> reference — the empty class the clash rows condemn a consequence into.</summary>
    private static OwlClassReference NothingReference { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Nothing")));

    /// <summary>The <c>owl:Thing</c> reference — the universal class the domain-collapse diagnostics constrain.</summary>
    private static OwlClassReference Thing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing")));

    /// <summary>An existential restriction over the inverse of a forward role.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="filler">The filler class.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom SomeInverse(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(InverseProperty(property), filler);
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

    /// <summary>The inverse of a named object property in the example namespace.</summary>
    /// <param name="local">The forward role's local name.</param>
    /// <returns>The inverse property expression.</returns>
    private static OwlInverseObjectProperty InverseProperty(string local)
    {
        return new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named data property node in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property node.</returns>
    private static NamedNode DataProperty(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>A named individual in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The individual node.</returns>
    private static NamedNode Individual(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>An enumeration of individuals in the example namespace; a single individual is the nominal <c>{a}</c>.</summary>
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

    /// <summary>An individual-value restriction over a forward role and a named individual.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="individual">The required value individual's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasValue HasValue(string property, string individual)
    {
        return new OwlObjectHasValue(Property(property), Individual(individual));
    }

    /// <summary>An existential restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>A universal restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectAllValuesFrom All(string property, OwlClassExpression filler)
    {
        return new OwlObjectAllValuesFrom(Property(property), filler);
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
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, InverseProperty(property), filler);
    }

    /// <summary>A union of class expressions.</summary>
    /// <param name="operands">The union operands.</param>
    /// <returns>The union.</returns>
    private static OwlObjectUnionOf Union(params OwlClassExpression[] operands)
    {
        return new OwlObjectUnionOf(operands);
    }

    /// <summary>A complement of a class expression.</summary>
    /// <param name="operand">The complemented operand.</param>
    /// <returns>The complement.</returns>
    private static OwlObjectComplementOf Complement(OwlClassExpression operand)
    {
        return new OwlObjectComplementOf(operand);
    }

    /// <summary>A subclass axiom.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>An equivalence between two class expressions.</summary>
    /// <param name="first">The first expression.</param>
    /// <param name="second">The second expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentClassesAxiom Equivalent(OwlClassExpression first, OwlClassExpression second)
    {
        return new OwlEquivalentClassesAxiom(first, second) { Origin = Origin("equiv") };
    }

    /// <summary>A class assertion typing an individual.</summary>
    /// <param name="type">The asserted class expression.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, RdfTerm individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

    /// <summary>An asserted object-property edge between two named individuals.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="source">The source individual's local name.</param>
    /// <param name="target">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom Edge(string property, string source, string target)
    {
        return new OwlObjectPropertyAssertionAxiom(Individual(source), new NamedNode(Utf8Strings.From(Example + property)), Individual(target)) { Origin = Origin("edge") };
    }

    /// <summary>A negative object-property assertion between two named individuals — the told clash form.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="source">The source individual's local name.</param>
    /// <param name="target">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlNegativeObjectPropertyAssertionAxiom NegativeEdge(string property, string source, string target)
    {
        return new OwlNegativeObjectPropertyAssertionAxiom(Individual(source), Property(property), Individual(target)) { Origin = Origin("negativeedge") };
    }

    /// <summary>A same-individual axiom pairing two named individuals.</summary>
    /// <param name="first">The first individual's local name.</param>
    /// <param name="second">The second individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSameIndividualAxiom Same(string first, string second)
    {
        return new OwlSameIndividualAxiom(Individual(first), Individual(second)) { Origin = Origin("same") };
    }

    /// <summary>A different-individuals axiom over the named individuals — pairwise distinct.</summary>
    /// <param name="individuals">The pairwise-distinct individuals' local names.</param>
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

    /// <summary>A functionality characteristic over a named role in the example namespace.</summary>
    /// <param name="role">The role's local name.</param>
    /// <returns>The characteristic axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Functional(string role)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, Property(role)) { Origin = Origin("functional") };
    }

    /// <summary>A symmetry characteristic over a named role in the example namespace.</summary>
    /// <param name="role">The role's local name.</param>
    /// <returns>The characteristic axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Symmetric(string role)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Symmetric, Property(role)) { Origin = Origin("symmetric") };
    }

    /// <summary>An <c>InverseObjectProperties</c> axiom pairing two roles as each other's reverse.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlInverseObjectPropertiesAxiom Inverse(string first, string second)
    {
        return new OwlInverseObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("inverse") };
    }

    /// <summary>A single-property data existential over a named data property in the example namespace.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="range">The filler range.</param>
    /// <returns>The data existential.</returns>
    private static OwlDataSomeValuesFrom DataSome(string property, OwlDataRange range)
    {
        return new OwlDataSomeValuesFrom([DataProperty(property)], range);
    }

    /// <summary>An <c>xsd:integer</c> typed literal.</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The literal.</returns>
    private static Literal IntegerLiteral(int value)
    {
        return new Literal(Utf8Strings.From(value.ToString(CultureInfo.InvariantCulture)), new NamedNode(Vocabulary.Xsd.Integer));
    }

    /// <summary>An EMPTY integer facet interval — minimum five, maximum three — the genuinely unsatisfiable unconditional demand the latch row's home face carries.</summary>
    /// <returns>The empty data range.</returns>
    private static OwlDatatypeRestriction EmptyIntegerRange()
    {
        return new OwlDatatypeRestriction(
            new NamedNode(Vocabulary.Xsd.Integer),
            [
                new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MinInclusive), IntegerLiteral(5)),
                new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MaxInclusive), IntegerLiteral(3)),
            ]);
    }
}
