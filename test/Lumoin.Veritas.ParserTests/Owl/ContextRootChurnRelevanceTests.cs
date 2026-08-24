using System;
using System.Collections.Generic;
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
/// The root-churn decider battery: the
/// r-Pred ground-relevance filter behind the default-off
/// <see cref="RootPropagationRelevance.GroundFiltered"/> mode is a PERFORMANCE
/// knob, never a semantic switch, and every row here holds that line — the
/// mode-equivalence sweep re-runs the certified nominal battery and the wedge
/// family under the filtered mode against the shipped defaults; the face rows
/// pin the filter, compensation, and re-offer counters live on constructed
/// modules while asserting verdict-and-exact-set identity across the modes; the
/// bridge-hole row is the executable witness of the bridge-premise exemption;
/// the attribution rows pin the four-origin partition of
/// <see cref="ContextSaturationStatistics.RootPredApplications"/>; the mark row
/// pins the filter and compensation counters onto the in-saturation progress
/// mark at distinct values; and the
/// dark-ship guard pins the unrestricted default byte-silent. Any measured
/// verdict or subsumption divergence between the modes is a defect, never a
/// tuning input.
/// </summary>
[TestClass]
internal sealed class ContextRootChurnRelevanceTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the face-row classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/rootchurn#";

    /// <summary>
    /// MODE EQUIVALENCE: every certified nominal-battery row decided under
    /// <see cref="RootPropagationRelevance.GroundFiltered"/> matches the shipped
    /// default row-by-row — outcome, verdict, context-decided path, and the EXACT
    /// module-local subsumption set — and in BOTH modes the four origin counters
    /// partition <see cref="ContextSaturationStatistics.RootPredApplications"/>
    /// exactly. Costs of both modes ride the log so a sibling-band movement names
    /// its row without a debugger.
    /// </summary>
    [TestMethod]
    public void GroundFilteredModeMatchesTheShippedDefaultsAcrossTheNominalBattery()
    {
        StringBuilder report = new();
        report.AppendLine("\nrow | outcome | attempts default/filtered | rpred default/filtered | filtered/reoffered/seeded | verdict");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool _, string[] _) in ContextNominalBatteryTests.BatteryRows())
        {
            ModuleDecision shipped = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
            ModuleDecision filtered = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.SingleRoot, RootPropagationRelevance.GroundFiltered, ReasoningConfiguration.Default.Budget, engineProbe: null, TestContext.CancellationToken);
            ContextSaturationStatistics shippedTotals = shipped.Statistics.ContextTotals;
            ContextSaturationStatistics filteredTotals = filtered.Statistics.ContextTotals;
            AssertOriginPartition(name + " shipped", shippedTotals, mismatches);
            AssertOriginPartition(name + " filtered", filteredTotals, mismatches);
            if(shippedTotals.RootPredFilteredOffers != 0 || shippedTotals.RootPredReofferedByGroundHead != 0 || shippedTotals.RelevanceTautologiesSeeded != 0)
            {
                mismatches.Add(name + ": the shipped default run moved a relevance counter — the dark mode leaked.");
            }

            bool identical = shipped.Outcome == filtered.Outcome
                && shippedTotals.ContextDecided == filteredTotals.ContextDecided
                && (shipped.Verdict is null) == (filtered.Verdict is null)
                && (shipped.Verdict is null || shipped.Verdict.IsConsistent == filtered.Verdict!.IsConsistent)
                && (shipped.Verdict is null || KeysEqual(SubsumptionKeys(shipped.Verdict), SubsumptionKeys(filtered.Verdict!)));
            report.AppendLine(name + " | " + shipped.Outcome + "/" + filtered.Outcome + " | " + shippedTotals.InferenceAttempts + "/" + filteredTotals.InferenceAttempts
                + " | " + shippedTotals.RootPredApplications + "/" + filteredTotals.RootPredApplications
                + " | " + filteredTotals.RootPredFilteredOffers + "/" + filteredTotals.RootPredReofferedByGroundHead + "/" + filteredTotals.RelevanceTautologiesSeeded
                + " | " + (identical ? "OK" : "MISMATCH"));
            if(!identical)
            {
                mismatches.Add(name + ": the filtered mode diverged from the shipped default (outcome " + shipped.Outcome + " vs " + filtered.Outcome + ").");
            }
        }

        TestContext.WriteLine(report.ToString());
        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>MODE EQUIVALENCE, wedge fold-in: the budget-honesty wedge under the filtered mode abstains at the same finite ceiling with no verdict, exactly as the shipped default pins it — the wedge exhaustions are not r-Pred-bound, so the filter must not move their face.</summary>
    [TestMethod]
    public void GroundFilteredWedgeCeilingAbstainsLikeTheShippedDefault()
    {
        ReasoningModule module = ContextSaturationModuleReasonerTests.WedgeTowerModule(ContextSaturationModuleReasonerTests.WedgeCeilingSize);
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.SingleRoot, RootPropagationRelevance.GroundFiltered, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: ContextSaturationModuleReasonerTests.WedgeCeiling), engineProbe: null, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "The finite ceiling abstains on the wedge under the filtered mode exactly as under the shipped default.");
        Assert.IsNull(decision.Verdict, "A budget abstention carries no verdict.");
    }

    /// <summary>MODE EQUIVALENCE, nominal-wedge fold-in: the NOM-mint-bound wedge exhaustion holds its face under the filtered mode — its consuming assertion reads the mint channel, not r-Pred, so the filter is argued AND measured a non-mover here.</summary>
    [TestMethod]
    public void GroundFilteredNomWedgeCeilingAbstainsLikeTheShippedDefault()
    {
        ReasoningModule module = ContextSaturationModuleReasonerTests.NomWedgeTowerModule(ContextSaturationModuleReasonerTests.NomWedgeCeilingSize);
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.SingleRoot, RootPropagationRelevance.GroundFiltered, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: ContextSaturationModuleReasonerTests.NomWedgeCeiling), engineProbe: null, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "The finite ceiling abstains on the nominal wedge under the filtered mode exactly as under the shipped default.");
        Assert.IsNull(decision.Verdict, "A budget abstention carries no verdict.");
    }

    /// <summary>
    /// FILTER FACE, swept and broadcast: the two-target module derives the root
    /// clause <c>S(y,o) ∧ B(o) → D(y)</c> (the C-chain types o's abstraction B;
    /// the ontology <c>∃r.B ⊑ D</c> lifts to the root over the seeded trigger)
    /// and the ground tautology broadcast <c>B(o) → B(o)</c>; the E-target holds
    /// the root edge for o but no discharge witness for <c>B(o)</c>, so under the
    /// filtered mode its offers are refused at zero budget while every verdict
    /// and the exact subsumption set match the shipped default — including
    /// <c>C ⊑ D</c>, the subsumption face riding the qualified C-target.
    /// </summary>
    [TestMethod]
    public void FilterFaceBlocksUnqualifiedTargetsAndPreservesTheDecision()
    {
        ReasoningModule module = FilterFaceModule();
        ModuleDecision shipped = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken);
        ModuleDecision filtered = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.SingleRoot, RootPropagationRelevance.GroundFiltered, ReasoningBudget.Unbounded, engineProbe: null, TestContext.CancellationToken);

        AssertDecisionsIdentical(shipped, filtered);
        ContextSaturationStatistics totals = filtered.Statistics.ContextTotals;
        TestContext.WriteLine(RelevanceCounterLine("FilterFace", totals));
        Assert.Contains(Sub("C", "D"), SubsumptionKeys(filtered.Verdict!), "The subsumption face lands: the qualified C-target discharges B(o) and D reads off.");
        Assert.DoesNotContain(Sub("E", "D"), SubsumptionKeys(filtered.Verdict!), "The unqualified E-target must not gain the subsumption — o carries no B-typing on E's face.");
        Assert.IsGreaterThan(0L, totals.RootPredFilteredOffers, "The filter refuses the offers whose target holds no discharge witness for B(o).");
        Assert.AreEqual(0L, shipped.Statistics.ContextTotals.RootPredFilteredOffers, "The shipped default filters nothing — the mode ships dark.");
    }

    /// <summary>FILTER FACE dual: a nominal module whose eligible root clauses carry NO ground body conjunct (the individual-value chain of the certified battery) gives the filter no bite — zero filtered offers, zero compensation, zero re-offers, decision identical.</summary>
    [TestMethod]
    public void FilterFaceDualGroundConjunctFreeModuleFiltersNothing()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), HasValue("r", "o")),
            SubClassOf(OneOf("o"), Class("C")),
            SubClassOf(Class("C"), Class("E")),
            Bystander());
        ModuleDecision shipped = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken);
        ModuleDecision filtered = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.SingleRoot, RootPropagationRelevance.GroundFiltered, ReasoningBudget.Unbounded, engineProbe: null, TestContext.CancellationToken);

        AssertDecisionsIdentical(shipped, filtered);
        ContextSaturationStatistics totals = filtered.Statistics.ContextTotals;
        Assert.AreEqual(0L, totals.RootPredFilteredOffers, "A ground-conjunct-free module gives the filter no bite.");
        Assert.AreEqual(0L, totals.RootPredReofferedByGroundHead, "Nothing was blocked, so nothing re-offers.");
    }

    /// <summary>
    /// BROADCAST FACE: <c>B ⊑ {p}</c> turns the C-chain's ground selection
    /// <c>B(o)</c> into the pure-ground-body root clause <c>B(o) → o ≈ p</c> — an
    /// n-zero broadcast image — which the filtered mode refuses at every context
    /// lacking a <c>B(o)</c> discharge witness and lands at the qualified
    /// C-target, where the merge consequence stays decision-identical to the
    /// shipped default; the broadcast origin counter proves the broadcast path
    /// carried landings in both modes.
    /// </summary>
    [TestMethod]
    public void BroadcastFaceFiltersAndLandsThePureGroundBodyImage()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("C"), Some("r", Class("X"))),
            SubClassOf(Class("X"), OneOf("o")),
            SubClassOf(Class("X"), Class("B")),
            SubClassOf(Class("B"), OneOf("p")),
            SubClassOf(OneOf("p"), Class("G")),
            Bystander());
        ModuleDecision shipped = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken);
        ModuleDecision filtered = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.SingleRoot, RootPropagationRelevance.GroundFiltered, ReasoningBudget.Unbounded, engineProbe: null, TestContext.CancellationToken);

        AssertDecisionsIdentical(shipped, filtered);
        ContextSaturationStatistics totals = filtered.Statistics.ContextTotals;
        TestContext.WriteLine(RelevanceCounterLine("BroadcastFace", totals));
        Assert.IsGreaterThan(0L, totals.RootPredFilteredOffers, "The pure-ground-body broadcast is refused at the contexts holding no B(o) witness.");
        Assert.IsGreaterThan(0L, totals.RootPredFromBroadcast, "The broadcast path landed images at the qualified contexts under the filtered mode.");
    }

    /// <summary>
    /// COMPENSATION and REOFFER FACES: the chain module's consumer context (W's,
    /// which reconstructs ground <c>B(o)</c> from its q-successor's relayed
    /// bridge facts) sits an ordinary edge ABOVE the r-Pred target (the
    /// U-successor holding the root edge for o but no local B-typing), so the
    /// target's swept offer is refused until the downward compensation floods
    /// <c>B(o) → B(o)</c> across the edge and the newly live ground head
    /// re-offers the blocked root clause — both counters must be live and the
    /// decision identical to the shipped default.
    /// </summary>
    [TestMethod]
    public void CompensationChainSeedsAndReoffersAcrossTheAncestorEdge()
    {
        ReasoningModule module = CompensationChainModule();
        ModuleDecision shipped = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken);
        ModuleDecision filtered = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.SingleRoot, RootPropagationRelevance.GroundFiltered, ReasoningBudget.Unbounded, engineProbe: null, TestContext.CancellationToken);

        AssertDecisionsIdentical(shipped, filtered);
        ContextSaturationStatistics totals = filtered.Statistics.ContextTotals;
        TestContext.WriteLine(RelevanceCounterLine("CompensationChain", totals));
        Assert.IsGreaterThan(0L, totals.RelevanceTautologiesSeeded, "The consumer's ground selection floods the tautology down its ordinary successors — the compensation counter's consuming assertion.");
        Assert.IsGreaterThan(0L, totals.RootPredReofferedByGroundHead, "The flooded ground head turns the target's qualification live and the blocked offer replays — the re-offer counter's consuming assertion.");
    }

    /// <summary>
    /// BRIDGE-HOLE ROW: driven BELOW the gates so the B-cored cautious successor
    /// stays a NON-read-off context. That context holds the empty-body maximal
    /// abstraction <c>⊤ → B(x)</c> (its core) and the empty-body maximal bridge
    /// <c>⊤ → x ≈ o</c> (from <c>B ⊑ {o}</c>) but never ground <c>B(o)</c>
    /// itself, and it holds the root edge for o — so the filtered mode may land
    /// the ground-conjunct root clause there ONLY through the bridge-premise
    /// exemption's arm (ii). A form-(a)-only filter would over-block the family
    /// (the refuted round-2 shape) and lose the discharge chain that makes W
    /// unsatisfiable; both modes must read the identical faces.
    /// </summary>
    [TestMethod]
    public void BridgeHoleTargetQualifiesThroughTheBridgePremisePair()
    {
        OwlAxiom[] axioms =
        [
            SubClassOf(Class("W"), Some("q", Class("B"))),
            SubClassOf(Class("B"), OneOf("o")),
            SubClassOf(Class("B"), HasValue("r", "o")),
            SubClassOf(Some("r", Class("B")), Class("D")),
            SubClassOf(Class("D"), NothingReference),
            Bystander(),
        ];

        (bool wUnsatisfiable, bool inconsistent, ContextSaturationStatistics totals) unrestricted = SaturateBelowGates(axioms, RootPropagationRelevance.Unrestricted);
        (bool wUnsatisfiable, bool inconsistent, ContextSaturationStatistics totals) filtered = SaturateBelowGates(axioms, RootPropagationRelevance.GroundFiltered);

        TestContext.WriteLine(RelevanceCounterLine("BridgeHole", filtered.totals));
        Assert.AreEqual(unrestricted.inconsistent, filtered.inconsistent, "The consistency face is mode-identical.");
        Assert.AreEqual(unrestricted.wUnsatisfiable, filtered.wUnsatisfiable, "The W-unsatisfiability face is mode-identical: the bridge-premise pair at the non-read-off B-context must qualify the ground-conjunct landing under the filtered mode.");
        Assert.IsTrue(filtered.wUnsatisfiable, "The discharge chain closes: B's element is o, o is a B, and B's r-edge to o meets the ∃r.B ⊑ ⊥ clash — W's existential is unsatisfiable.");
    }

    /// <summary>
    /// ANCESTOR-BRIDGE ROW, positive-witness face: the abstraction witness
    /// <c>⊤ → B(x)</c> and bridge <c>⊤ → x ≈ o</c> sit at the B-cored context
    /// while the r-Pred landing targets its U-cored ordinary SUCCESSOR, which
    /// lacks both. This is the D-b@ancestor SHAPE, but as constructed it is a
    /// mode-equivalence witness, NOT yet the discriminating flip-gate: W's
    /// query context (the B-context's ordinary predecessor) reconstructs ground
    /// <c>B(o)</c> mode-independently from the relayed <c>f(x) ≈ o</c> equality
    /// and its own DL2 image, so the already-certified D-a@ancestor path fires
    /// one hop up and rescues the landing in both modes. The genuine
    /// D-b@ancestor geometry — a bridge-pair ancestor whose predecessors CANNOT
    /// reconstruct the ground conjunct — remains the named flip-gate
    /// construction, resolved by the reachability proof or the R1b′ rule.
    /// </summary>
    [TestMethod]
    public void AncestorBridgeRowMeasuresBothModes()
    {
        OwlAxiom[] axioms =
        [
            SubClassOf(Class("W"), Some("q", Class("B"))),
            SubClassOf(Class("B"), OneOf("o")),
            SubClassOf(Class("B"), Some("s", Class("U"))),
            SubClassOf(Class("U"), HasValue("r", "o")),
            SubClassOf(Some("r", Class("B")), Class("D")),
            SubClassOf(Class("D"), NothingReference),
            Bystander(),
        ];

        (bool wUnsatisfiable, bool inconsistent, ContextSaturationStatistics totals) unrestricted = SaturateBelowGates(axioms, RootPropagationRelevance.Unrestricted);
        (bool wUnsatisfiable, bool inconsistent, ContextSaturationStatistics totals) filtered = SaturateBelowGates(axioms, RootPropagationRelevance.GroundFiltered);

        TestContext.WriteLine(RelevanceCounterLine("AncestorBridge", filtered.totals));
        Assert.AreEqual(unrestricted.inconsistent, filtered.inconsistent, "The consistency face is mode-identical.");
        Assert.AreEqual(unrestricted.wUnsatisfiable, filtered.wUnsatisfiable, "The ancestor-bridge faces are mode-identical — the flip-gate row's green face; a divergence here is the D-b@ancestor residual made flesh and mandates R1b′ before any default flip.");
    }

    /// <summary>DARK-SHIP GUARD: the module that exercises the filter, compensation, and re-offers under the filtered mode keeps ALL THREE relevance counters at zero under the shipped default — a named leak detector for the dark mode.</summary>
    [TestMethod]
    public void DarkShipGuardKeepsTheDefaultModeSilent()
    {
        ModuleDecision shipped = ContextSaturationModuleReasoner.DecideModule(CompensationChainModule(), EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken);

        ContextSaturationStatistics totals = shipped.Statistics.ContextTotals;
        Assert.AreEqual(0L, totals.RootPredFilteredOffers, "The unrestricted default filters nothing.");
        Assert.AreEqual(0L, totals.RelevanceTautologiesSeeded, "The unrestricted default seeds no compensation tautology.");
        Assert.AreEqual(0L, totals.RootPredReofferedByGroundHead, "The unrestricted default re-offers nothing.");
    }

    /// <summary>DISJUNCTIVE-HEAD GUARD: the target's B-typing rides only a DISJUNCTIVE ground head (<c>⊤ → B(o) ∨ K(o)</c> at the C-context), so the arm (i) probe answers off the all-maximal registration — a first-selected-only index would diverge here (the killed RC-A2 shape); both modes must decide identically.</summary>
    [TestMethod]
    public void DisjunctiveGroundHeadGuardDecidesIdenticallyAcrossModes()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("C"), Some("r", Class("X"))),
            SubClassOf(Class("X"), OneOf("o")),
            SubClassOf(Class("X"), Union(Class("B"), Class("K"))),
            SubClassOf(Some("r", Class("B")), Class("D")),
            SubClassOf(Some("r", Class("K")), Class("D")),
            Bystander());
        ModuleDecision shipped = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken);
        ModuleDecision filtered = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.SingleRoot, RootPropagationRelevance.GroundFiltered, ReasoningBudget.Unbounded, engineProbe: null, TestContext.CancellationToken);

        TestContext.WriteLine(RelevanceCounterLine("DisjunctiveGuard", filtered.Statistics.ContextTotals));
        AssertDecisionsIdentical(shipped, filtered);
    }

    /// <summary>
    /// MARK FACE: the two relevance counters ride the in-saturation progress mark,
    /// not only the per-decision statistics. The compensation chain under the
    /// filtered mode moves both counters to distinct nonzero values, so the mark
    /// columns are read apart from one another rather than by position — a
    /// transposition of the two at the emission site reds here.
    /// </summary>
    [TestMethod]
    public void GroundFilteredMarksCarryDistinctRelevanceCounts()
    {
        SampledEngineProbe probe = new(new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero)), new Guid("41c7b0d6-58e2-4a90-9f13-7cd6820ae54b"));

        ModuleDecision filtered = ContextSaturationModuleReasoner.DecideModule(CompensationChainModule(), EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.SingleRoot, RootPropagationRelevance.GroundFiltered, ReasoningBudget.Unbounded, probe.Attach, TestContext.CancellationToken);

        ContextSaturationStatistics totals = filtered.Statistics.ContextTotals;
        Assert.IsNotEmpty(probe.Marks, "The sampled filtered run crossed power-of-two attempt marks.");
        SaturationProgressTraceEvent last = probe.Marks[^1];
        TestContext.WriteLine("final mark: filtered=" + last.RootPredFilteredOffers + " seeded=" + last.RelevanceTautologiesSeeded + " | decision: filtered=" + totals.RootPredFilteredOffers + " seeded=" + totals.RelevanceTautologiesSeeded);
        Assert.IsGreaterThan(0L, last.RootPredFilteredOffers, "The filtered mode blocked offers by the final mark.");
        Assert.IsGreaterThan(0L, last.RelevanceTautologiesSeeded, "The compensation seeded tautologies by the final mark.");
        Assert.AreNotEqual(last.RootPredFilteredOffers, last.RelevanceTautologiesSeeded, "The two counters hold distinct values, so the mark reads them apart rather than by position.");
        Assert.AreEqual(18L, last.RootPredFilteredOffers, "The blocked offers standing at the final mark.");
        Assert.AreEqual(6L, last.RelevanceTautologiesSeeded, "The seeded tautologies standing at the final mark.");
        Assert.IsGreaterThanOrEqualTo(last.RootPredFilteredOffers, totals.RootPredFilteredOffers, "A mark is a prefix of the run, so the decision's blocked-offer total is at least the final mark's.");
        Assert.IsGreaterThanOrEqualTo(last.RelevanceTautologiesSeeded, totals.RelevanceTautologiesSeeded, "A mark is a prefix of the run, so the decision's seeded-tautology total is at least the final mark's.");
    }

    /// <summary>
    /// THE ODOMETER PIN: the two r-Pred-bearing fixtures decided on the shipped
    /// default, with the whole attempt-and-funnel shape of each run pinned to
    /// literals — the budget-gated attempts, the absorbed conclusions, the inserted
    /// population, and the per-origin offer and duplicate split. The odometer builds
    /// its conclusions in reusable scratch and hands them to the insertion gate as
    /// spans, so three distinct defects all land here: a containment probe moved
    /// AHEAD of the budget gate stops charging attempts for absorbed conclusions and
    /// the attempt total falls; scratch corrupted across odometer combinations
    /// changes what is offered and every funnel column shifts; and an offer charged
    /// to the wrong origin moves counts between the per-origin columns while their
    /// sum stands still.
    /// </summary>
    [TestMethod]
    public void TheOdometerPathPinsItsAttemptAndFunnelTotals()
    {
        ContextSaturationStatistics filterFace = ContextSaturationModuleReasoner.DecideModule(FilterFaceModule(), EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics chain = ContextSaturationModuleReasoner.DecideModule(CompensationChainModule(), EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken).Statistics.ContextTotals;

        TestContext.WriteLine(OfferCounterLine("FilterFace", filterFace));
        TestContext.WriteLine(OfferCounterLine("CompensationChain", chain));

        Assert.IsTrue(filterFace.ContextDecided, "The filter-face module is context-decided, so the totals are the saturation's own.");
        Assert.AreEqual(157L, filterFace.InferenceAttempts, "The filter-face run's budget-gated attempts: absorbed conclusions charge the gate exactly as landed ones do, so a probe moved ahead of it lowers this total.");
        Assert.AreEqual(65L, filterFace.RedundantConclusions, "The filter-face run's absorbed conclusions.");
        Assert.AreEqual(90, filterFace.ClausesDerived, "The filter-face run's inserted population.");
        Assert.AreEqual(3L, filterFace.RootPredRegistrationSweepOffers, "The filter-face run's registration-sweep offers.");
        Assert.AreEqual(0L, filterFace.RootPredNewRootEdgeOffers, "The filter-face run's new-root-edge offers.");
        Assert.AreEqual(3L, filterFace.RootPredPremiseOffers, "The filter-face run's landed-premise offers.");
        Assert.AreEqual(45L, filterFace.RootPredBroadcastOffers, "The filter-face run's broadcast offers.");
        Assert.AreEqual(1L, filterFace.RootPredRegistrationSweepDuplicateHits, "The filter-face run's registration-sweep duplicates.");
        Assert.AreEqual(0L, filterFace.RootPredNewRootEdgeDuplicateHits, "The filter-face run's new-root-edge duplicates.");
        Assert.AreEqual(1L, filterFace.RootPredPremiseDuplicateHits, "The filter-face run's landed-premise duplicates.");
        Assert.AreEqual(9L, filterFace.RootPredBroadcastDuplicateHits, "The filter-face run's broadcast duplicates.");
        Assert.AreEqual(filterFace.RootPredApplications, filterFace.RootPredFromRegistrationSweep + filterFace.RootPredFromNewRootEdge + filterFace.RootPredFromPremise + filterFace.RootPredFromBroadcast, "The four landing counters still partition the r-Pred applications.");

        Assert.IsTrue(chain.ContextDecided, "The compensation-chain module is context-decided.");
        Assert.AreEqual(156L, chain.InferenceAttempts, "The compensation-chain run's budget-gated attempts.");
        Assert.AreEqual(62L, chain.RedundantConclusions, "The compensation-chain run's absorbed conclusions.");
        Assert.AreEqual(90, chain.ClausesDerived, "The compensation-chain run's inserted population.");
        Assert.AreEqual(4L, chain.RootPredRegistrationSweepOffers, "The compensation-chain run's registration-sweep offers — different from the filter-face run's, so a fixture-blind misattribution cannot satisfy both rows.");
        Assert.AreEqual(0L, chain.RootPredNewRootEdgeOffers, "The compensation-chain run's new-root-edge offers.");
        Assert.AreEqual(0L, chain.RootPredPremiseOffers, "The compensation-chain run's landed-premise offers: this fixture drives none, while the filter-face fixture drives three.");
        Assert.AreEqual(45L, chain.RootPredBroadcastOffers, "The compensation-chain run's broadcast offers.");
        Assert.AreEqual(2L, chain.RootPredRegistrationSweepDuplicateHits, "The compensation-chain run's registration-sweep duplicates.");
        Assert.AreEqual(0L, chain.RootPredNewRootEdgeDuplicateHits, "The compensation-chain run's new-root-edge duplicates.");
        Assert.AreEqual(0L, chain.RootPredPremiseDuplicateHits, "The compensation-chain run's landed-premise duplicates.");
        Assert.AreEqual(9L, chain.RootPredBroadcastDuplicateHits, "The compensation-chain run's broadcast duplicates.");
        Assert.AreEqual(chain.RootPredApplications, chain.RootPredFromRegistrationSweep + chain.RootPredFromNewRootEdge + chain.RootPredFromPremise + chain.RootPredFromBroadcast, "The four landing counters still partition the r-Pred applications.");
    }

    /// <summary>
    /// The per-origin OFFER counters read the traffic the accept-keyed landing
    /// counters cannot see: each origin's offers stand at or above its landings on
    /// every r-Pred-bearing fixture, an origin the fixture drives no traffic through
    /// pins zero rather than absorbing another origin's increments, and a
    /// nominal-free module leaves all four columns dark. A counter keyed on ACCEPT
    /// rather than on the offer collapses each column onto its landing counterpart
    /// and the strict gap the r-Pred-bearing fixtures carry disappears.
    /// </summary>
    [TestMethod]
    public void ThePerOriginOfferCountersStandAboveTheirLandings()
    {
        ContextSaturationStatistics chain = ContextSaturationModuleReasoner.DecideModule(CompensationChainModule(), EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics nominalFree = ContextSaturationModuleReasoner.DecideModule(NominalFreeModule(), EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken).Statistics.ContextTotals;

        TestContext.WriteLine(OfferCounterLine("CompensationChain", chain));
        TestContext.WriteLine(OfferCounterLine("NominalFree", nominalFree));

        Assert.IsGreaterThanOrEqualTo(chain.RootPredFromRegistrationSweep, chain.RootPredRegistrationSweepOffers, "The registration-sweep origin offered at least what it landed.");
        Assert.IsGreaterThanOrEqualTo(chain.RootPredFromNewRootEdge, chain.RootPredNewRootEdgeOffers, "The new-root-edge origin offered at least what it landed.");
        Assert.IsGreaterThanOrEqualTo(chain.RootPredFromPremise, chain.RootPredPremiseOffers, "The landed-premise origin offered at least what it landed.");
        Assert.IsGreaterThanOrEqualTo(chain.RootPredFromBroadcast, chain.RootPredBroadcastOffers, "The broadcast origin offered at least what it landed.");

        long offers = chain.RootPredRegistrationSweepOffers + chain.RootPredNewRootEdgeOffers + chain.RootPredPremiseOffers + chain.RootPredBroadcastOffers;
        Assert.IsGreaterThan(chain.RootPredApplications, offers, "The r-Pred-bearing fixture offers strictly more than it lands, so a counter keyed on accept could not produce this total.");

        Assert.AreEqual(0L, nominalFree.RootPredRegistrationSweepOffers, "A nominal-free module offers no registration-sweep completion.");
        Assert.AreEqual(0L, nominalFree.RootPredNewRootEdgeOffers, "A nominal-free module offers no new-root-edge completion.");
        Assert.AreEqual(0L, nominalFree.RootPredPremiseOffers, "A nominal-free module offers no landed-premise completion.");
        Assert.AreEqual(0L, nominalFree.RootPredBroadcastOffers, "A nominal-free module offers no broadcast image.");
    }

    /// <summary>
    /// The per-origin DUPLICATE counters carry the exact-duplicate half of the
    /// funnel keyed by rule-invocation site: their sum is a share of the run's own
    /// exact-duplicate total and never of the subsumed half, each origin's
    /// duplicates are a share of that origin's offers, and a module driving no
    /// duplicate r-Pred offer pins all four columns zero. Attributing an absorption
    /// to the duplicate column when the subsumption walk answered it breaks the
    /// share relation against the duplicate total.
    /// </summary>
    [TestMethod]
    public void ThePerOriginDuplicateCountersAreAShareOfTheDuplicateHalf()
    {
        ContextSaturationStatistics chain = ContextSaturationModuleReasoner.DecideModule(CompensationChainModule(), EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics nominalFree = ContextSaturationModuleReasoner.DecideModule(NominalFreeModule(), EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken).Statistics.ContextTotals;

        TestContext.WriteLine(OfferCounterLine("CompensationChain", chain));

        long duplicates = chain.RootPredRegistrationSweepDuplicateHits + chain.RootPredNewRootEdgeDuplicateHits + chain.RootPredPremiseDuplicateHits + chain.RootPredBroadcastDuplicateHits;
        Assert.IsGreaterThan(0L, duplicates, "The compensation chain re-offers r-Pred conclusions the gate absorbs as exact duplicates, so the columns have something to hold.");
        Assert.AreEqual(2L, chain.RootPredRegistrationSweepDuplicateHits, "The registration-sweep origin's exact-duplicate absorptions, pinned apart from its subsumer absorptions.");
        Assert.AreEqual(0L, chain.RootPredNewRootEdgeDuplicateHits, "The new-root-edge origin absorbs no exact duplicate on this fixture.");
        Assert.AreEqual(0L, chain.RootPredPremiseDuplicateHits, "The landed-premise origin absorbs no exact duplicate on this fixture.");
        Assert.AreEqual(9L, chain.RootPredBroadcastDuplicateHits, "The broadcast origin's exact-duplicate absorptions: charging a subsumer absorption to this column instead moves the value.");
        Assert.IsGreaterThanOrEqualTo(duplicates, chain.DuplicateContainmentHits, "The r-Pred duplicate columns are a share of the run's own exact-duplicate total.");
        Assert.IsGreaterThanOrEqualTo(chain.RootPredRegistrationSweepDuplicateHits, chain.RootPredRegistrationSweepOffers, "An origin's duplicates are a share of its offers.");
        Assert.IsGreaterThanOrEqualTo(chain.RootPredNewRootEdgeDuplicateHits, chain.RootPredNewRootEdgeOffers, "An origin's duplicates are a share of its offers.");
        Assert.IsGreaterThanOrEqualTo(chain.RootPredPremiseDuplicateHits, chain.RootPredPremiseOffers, "An origin's duplicates are a share of its offers.");
        Assert.IsGreaterThanOrEqualTo(chain.RootPredBroadcastDuplicateHits, chain.RootPredBroadcastOffers, "An origin's duplicates are a share of its offers.");

        Assert.AreEqual(0L, nominalFree.RootPredRegistrationSweepDuplicateHits, "A nominal-free module absorbs no registration-sweep duplicate.");
        Assert.AreEqual(0L, nominalFree.RootPredNewRootEdgeDuplicateHits, "A nominal-free module absorbs no new-root-edge duplicate.");
        Assert.AreEqual(0L, nominalFree.RootPredPremiseDuplicateHits, "A nominal-free module absorbs no landed-premise duplicate.");
        Assert.AreEqual(0L, nominalFree.RootPredBroadcastDuplicateHits, "A nominal-free module absorbs no broadcast duplicate.");
    }

    /// <summary>
    /// The join OFFER counter counts join-family conclusions handed to the insertion
    /// gate, not join candidates and not join landings: it stands at or above the
    /// landed applications on a join-bearing fixture, strictly above where the gate
    /// absorbs some, and pins zero on a nominal-free module the join rule cannot
    /// fire in. Incrementing per enumerated candidate instead of per offered
    /// conclusion inflates the column past the offers the funnel actually saw.
    /// </summary>
    [TestMethod]
    public void TheJoinOfferCounterCountsOfferedJoinConclusions()
    {
        ContextSaturationStatistics chain = ContextSaturationModuleReasoner.DecideModule(CompensationChainModule(), EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken).Statistics.ContextTotals;
        ContextSaturationStatistics nominalFree = ContextSaturationModuleReasoner.DecideModule(NominalFreeModule(), EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken).Statistics.ContextTotals;

        TestContext.WriteLine(OfferCounterLine("CompensationChain", chain));

        Assert.AreEqual(34L, chain.JoinOffers, "The compensation chain's offered join conclusions — far above the two landed applications, which is the gap the column exists to read.");
        Assert.IsGreaterThanOrEqualTo(chain.JoinApplications, chain.JoinOffers, "Every landed join conclusion was offered first.");
        Assert.IsGreaterThanOrEqualTo(chain.JoinOffers, chain.RedundantConclusions + chain.JoinApplications, "The offered join conclusions are covered by the run's landings and absorptions together, so a per-candidate increment would overshoot.");
        Assert.AreEqual(0L, nominalFree.JoinOffers, "The join rule cannot fire without nominal jurisdiction, so the column stays dark.");
    }

    /// <summary>
    /// A run stopped on the fixed-work population bound still carries the offer and
    /// duplicate slice on the statistics it abstains with: the decision is
    /// <see cref="ReasoningDecisionOutcome.AbstainedBudget"/> with no verdict, and
    /// several of the new columns read NONZERO on the r-Pred-bearing fixture — the
    /// bounded read the counter slice exists to be consumed through. A statistics
    /// feed that dropped the new columns leaves them all zero and the abstention
    /// carries nothing.
    /// </summary>
    [TestMethod]
    public void TheBoundedRunCarriesTheOfferCountersOnItsAbstention()
    {
        ModuleDecision abstained = ContextSaturationModuleReasoner.DecideModule(CompensationChainModule(), EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.SingleRoot, RootPropagationRelevance.Unrestricted, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 0, MaxDerivedClauses: 60), engineProbe: null, TestContext.CancellationToken);

        ContextSaturationStatistics totals = abstained.Statistics.ContextTotals;
        TestContext.WriteLine(OfferCounterLine("BoundedChain", totals));

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, abstained.Outcome, "The population ceiling stops the r-Pred-bearing fixture short of its fixpoint.");
        Assert.IsNull(abstained.Verdict, "A budget abstention carries no verdict.");
        long offers = totals.RootPredRegistrationSweepOffers + totals.RootPredNewRootEdgeOffers + totals.RootPredPremiseOffers + totals.RootPredBroadcastOffers;
        Assert.IsGreaterThan(0L, offers, "The bounded run offered r-Pred conclusions before it stopped, so the offer slice is readable off the abstention.");
        Assert.IsGreaterThan(0L, totals.JoinOffers, "The bounded run offered join conclusions before it stopped.");
        Assert.IsGreaterThan(0L, totals.InferenceAttempts, "The bounded run spent real work before the ceiling latched.");
    }

    /// <summary>
    /// THE BRIDGE SWEEP IS OFFER-IDENTICAL end to end: the compensation chain —
    /// the join-bearing fixture whose bridge premises and abstract premises meet in
    /// the same contexts — decides with its whole join funnel and clause population
    /// unmoved now that the sweep enumerates a posting of registered bridge
    /// individuals instead of the whole individual census. An individual absent from
    /// the posting has no empty-body maximal bridge clause, so skipping it removes no
    /// offer; a registration silently lost, on the other hand, empties the posting
    /// and the sweep visits NOTHING, which this row's join columns read directly.
    /// </summary>
    [TestMethod]
    public void TheBridgeSweepLeavesTheCompensationChainDecisionUnmoved()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CompensationChainModule(), EnumerationDeciderFaces.None, ReasoningBudget.Unbounded, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        TestContext.WriteLine(OfferCounterLine("CompensationChain", totals));
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The compensation chain decides on the context path.");
        Assert.IsTrue(totals.ContextDecided, "The totals are the saturation's own.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The compensation chain is consistent.");
        Assert.AreEqual(34L, totals.JoinOffers, "The join offers the sweep and its sibling dispatches together hand the insertion gate — a posting that lost a registration visits nothing and this column collapses.");
        Assert.AreEqual(2L, totals.JoinApplications, "The landed join conclusions.");
        Assert.AreEqual(32L, totals.JoinDuplicateHits, "The join offers the gate absorbed as exact duplicates.");
        Assert.AreEqual(156L, totals.InferenceAttempts, "The run's budget-gated attempts.");
        Assert.AreEqual(90, totals.ClausesDerived, "The run's inserted population.");
        Assert.AreEqual(62L, totals.RedundantConclusions, "The run's absorbed conclusions.");
    }

    /// <summary>The nominal-free control: a plain Horn module carrying no individual, so the root tier never exists, the join rule cannot fire, and every counter of the offer slice must read zero.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NominalFreeModule()
    {
        return Module(
            SubClassOf(Class("A"), Some("r", Class("B"))),
            SubClassOf(Class("B"), Class("D")),
            Bystander());
    }

    /// <summary>One log line of the offer, duplicate, and join-offer counters for a named row.</summary>
    /// <param name="name">The row label.</param>
    /// <param name="totals">The decision's context totals.</param>
    /// <returns>The line.</returns>
    private static string OfferCounterLine(string name, ContextSaturationStatistics totals)
    {
        return name + ": attempts=" + totals.InferenceAttempts + " derived=" + totals.ClausesDerived + " redundant=" + totals.RedundantConclusions
            + " dup/sub=" + totals.DuplicateContainmentHits + "/" + totals.SubsumedContainmentHits
            + " offers=" + totals.RootPredRegistrationSweepOffers + "/" + totals.RootPredNewRootEdgeOffers + "/" + totals.RootPredPremiseOffers + "/" + totals.RootPredBroadcastOffers
            + " originDup=" + totals.RootPredRegistrationSweepDuplicateHits + "/" + totals.RootPredNewRootEdgeDuplicateHits + "/" + totals.RootPredPremiseDuplicateHits + "/" + totals.RootPredBroadcastDuplicateHits
            + " landings=" + totals.RootPredFromRegistrationSweep + "/" + totals.RootPredFromNewRootEdge + "/" + totals.RootPredFromPremise + "/" + totals.RootPredFromBroadcast
            + " rpred=" + totals.RootPredApplications + " joinOffers=" + totals.JoinOffers + " joinApplications=" + totals.JoinApplications;
    }

    /// <summary>Attaches one progress sampler to every constructed saturation engine and collects the marks it emits, holding the sampler and the mark list as explicit state so neither the probe nor the handler closes over an enclosing local.</summary>
    private sealed class SampledEngineProbe
    {
        /// <summary>Binds the probe to a sampler built over its own mark collector.</summary>
        /// <param name="clock">The clock stamping each emitted mark.</param>
        /// <param name="correlationId">The correlation id carried on every emitted mark.</param>
        public SampledEngineProbe(TimeProvider clock, Guid correlationId)
        {
            Sampler = new SaturationProgressSampler(Handle, clock, correlationId);
        }

        /// <summary>The marks every sampled engine emitted, in emission order.</summary>
        public List<SaturationProgressTraceEvent> Marks { get; } = [];

        /// <summary>The sampler attached to each constructed engine.</summary>
        private SaturationProgressSampler Sampler { get; }

        /// <summary>Attaches the sampler to a constructed engine, before its seeding runs.</summary>
        /// <param name="engine">The created engine.</param>
        public void Attach(ContextSaturationEngine engine)
        {
            engine.Progress = Sampler;
        }

        /// <summary>Appends one emitted mark.</summary>
        /// <param name="mark">The mark.</param>
        private void Handle(in SaturationProgressTraceEvent mark)
        {
            Marks.Add(mark);
        }
    }

    /// <summary>Asserts the four origin counters partition the r-Pred application total exactly — three alone are unsatisfiable on any broadcasting module, so the assertion also separates the broadcast path from the swept origins.</summary>
    /// <param name="name">The row label for the offender report.</param>
    /// <param name="totals">The decision's context totals.</param>
    /// <param name="mismatchesToAppendTo">The offender list a violation is appended to.</param>
    private static void AssertOriginPartition(string name, ContextSaturationStatistics totals, List<string> mismatchesToAppendTo)
    {
        long sum = totals.RootPredFromRegistrationSweep + totals.RootPredFromNewRootEdge + totals.RootPredFromPremise + totals.RootPredFromBroadcast;
        if(sum != totals.RootPredApplications)
        {
            mismatchesToAppendTo.Add(name + ": the four r-Pred origin counters sum to " + sum + " but RootPredApplications is " + totals.RootPredApplications + " — the partition leaked.");
        }
    }

    /// <summary>The swept-and-broadcast filter-face module: the C-chain types o's abstraction B and opens the root edge; the E-target opens the same edge without the typing; the ontology existential lifts to the ground-conjunct root clause.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule FilterFaceModule()
    {
        return Module(
            SubClassOf(Class("C"), Some("r", Class("X"))),
            SubClassOf(Class("X"), OneOf("o")),
            SubClassOf(Class("X"), Class("B")),
            SubClassOf(Class("E"), HasValue("r", "o")),
            SubClassOf(Some("r", Class("B")), Class("D")),
            Bystander());
    }

    /// <summary>The compensation-chain module: W derives the ground B(o) and owns the ordinary edge down to the U-successor, which holds the root edge for o but no local B-typing.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule CompensationChainModule()
    {
        return Module(
            SubClassOf(Class("W"), Some("s", Class("U"))),
            SubClassOf(Class("U"), HasValue("r", "o")),
            SubClassOf(Class("W"), Some("q", Class("X"))),
            SubClassOf(Class("X"), OneOf("o")),
            SubClassOf(Class("X"), Class("B")),
            SubClassOf(Some("r", Class("B")), Class("D")),
            Bystander());
    }

    /// <summary>Clausifies the axioms, builds the engine BELOW the gates under the given relevance mode with the shipped scope, ensures W's query context only (so the B-cored successor stays non-read-off), saturates unbounded, and reads the W-unsatisfiability and consistency faces with the statistics.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <param name="relevance">The r-Pred ground-relevance mode.</param>
    /// <returns>The W-unsatisfiability face, the inconsistency face, and the run's statistics.</returns>
    private (bool WUnsatisfiable, bool Inconsistent, ContextSaturationStatistics Totals) SaturateBelowGates(OwlAxiom[] axioms, RootPropagationRelevance relevance)
    {
        ClausificationResult clausification = ContextClausifier.Clausify(Module(axioms));
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification, DatatypeRegistry.Empty, NominalParamodulationScope.QueryScoped, relevance);
        int wAtom = clausification.Symbols.AtomOf(Utf8Strings.From(Example + "W"));
        engine.EnsureQueryContext(wAtom);
        Assert.AreEqual(SaturationOutcome.Completed, engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken), "The unbounded saturation reaches its fixpoint.");
        engine.RunGroundGhostPass();

        int willowAtom = clausification.Symbols.AtomOf(Utf8Strings.From(Example + "Willow"));

        return (engine.IsSubsumedBy(wAtom, willowAtom), engine.IsInconsistent, engine.BuildStatistics(contextDecided: true));
    }

    /// <summary>Asserts two module decisions identical on outcome, context-decided path, verdict, and the exact subsumption set.</summary>
    /// <param name="shipped">The shipped-default decision.</param>
    /// <param name="filtered">The filtered-mode decision.</param>
    private static void AssertDecisionsIdentical(ModuleDecision shipped, ModuleDecision filtered)
    {
        Assert.AreEqual(shipped.Outcome, filtered.Outcome, "The decision outcome is mode-identical.");
        Assert.AreEqual(shipped.Statistics.ContextTotals.ContextDecided, filtered.Statistics.ContextTotals.ContextDecided, "The context-decided path is mode-identical.");
        Assert.AreEqual(shipped.Verdict is null, filtered.Verdict is null, "Verdict presence is mode-identical.");
        if(shipped.Verdict is not null)
        {
            Assert.AreEqual(shipped.Verdict.IsConsistent, filtered.Verdict!.IsConsistent, "The consistency verdict is mode-identical.");
            List<string> shippedKeys = SubsumptionKeys(shipped.Verdict);
            List<string> filteredKeys = SubsumptionKeys(filtered.Verdict);
            Assert.IsTrue(KeysEqual(shippedKeys, filteredKeys), "The exact subsumption sets are mode-identical (shipped: " + string.Join(", ", shippedKeys) + " | filtered: " + string.Join(", ", filteredKeys) + ").");
        }
    }

    /// <summary>One log line of the relevance counters for a named row.</summary>
    /// <param name="name">The row label.</param>
    /// <param name="totals">The decision's context totals.</param>
    /// <returns>The line.</returns>
    private static string RelevanceCounterLine(string name, ContextSaturationStatistics totals)
    {
        return name + ": filtered=" + totals.RootPredFilteredOffers + " reoffered=" + totals.RootPredReofferedByGroundHead + " seeded=" + totals.RelevanceTautologiesSeeded
            + " origins=" + totals.RootPredFromRegistrationSweep + "/" + totals.RootPredFromNewRootEdge + "/" + totals.RootPredFromPremise + "/" + totals.RootPredFromBroadcast
            + " rpred=" + totals.RootPredApplications + " attempts=" + totals.InferenceAttempts;
    }

    /// <summary>The sorted subsumption keys of a verdict, for the exact-set comparison.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The sorted keys.</returns>
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

    /// <summary>The <c>owl:Nothing</c> class reference.</summary>
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

    /// <summary>An enumeration of individuals in the example namespace (<c>ObjectOneOf</c>).</summary>
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

    /// <summary>A union of class expressions (<c>ObjectUnionOf</c>).</summary>
    /// <param name="members">The member expressions.</param>
    /// <returns>The union.</returns>
    private static OwlObjectUnionOf Union(params OwlClassExpression[] members)
    {
        return new OwlObjectUnionOf(members);
    }

    /// <summary>A subclass axiom.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }
}
