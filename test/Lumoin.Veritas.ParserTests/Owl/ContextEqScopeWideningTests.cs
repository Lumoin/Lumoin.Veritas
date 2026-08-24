using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The license-scoped Eq-restriction widening battery: the
/// <see cref="NominalParamodulationScope.LicenseScoped"/> member behind the
/// shipped <see cref="NominalParamodulationScope.QueryScoped"/> default is a
/// dark measured knob whose two axes — the query-atom target restriction in
/// every query-initialized context and the transitive push-provenance gate on
/// root-class contexts under the fragmented topology — may only REMOVE
/// inferences; every row holds the sound-or-silent line. The
/// verdict-identity sweep re-runs the certified nominal battery under the
/// widened scope against the shipped defaults; the PIGN row pins the
/// acting-literal and scope-gate composition; the NOMR-2 row pins the
/// fragmented demonstration face inside its numeric non-regression band; the
/// home-seeded merge row is the P4a case-(d) witness over a decide-able merge;
/// the query-local clash row exercises the blocked-live latch's query-surface
/// arm (the P3a delta guard); the certificate row exercises its
/// consistency-surface arm; the counter row consumes the three scalar
/// counters where they provably fire and pins them zero on the dark default
/// paths; the NOMR-1 row RECORDS the jurisdiction face without moving its
/// standing divergence pin; the env-gated scope-cost probe extends the
/// ENUM-1 topology instrument by the scope column with the per-landing
/// rewrite-shape attribution, passing dark; and the env-gated jurisdiction
/// probe writes the NOMR-1 fragmented reads under both scopes, passing dark.
/// A wrong verdict on any cell is a defect, never a tuning input. Every row
/// drives the explicit enumeration-decider dark control: these instruments
/// measure the ENGINE faces beneath the lit production decider, whose
/// pre-engine decisions would otherwise short-circuit the role-free
/// fixtures.
/// </summary>
[TestClass]
internal sealed class ContextEqScopeWideningTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the row classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/eqscopewidening#";

    /// <summary>The NOMR-1 row name — excluded from the verdict-identity sweep by name (its fragmented run legitimately sits budget-divergent), instrumented solely by <see cref="FragmentedNomrOneUnderTheWidenedScope"/>.</summary>
    private const string BudgetDivergentRow = "NOMR-1";

    /// <summary>The NOMR-2 fragmented non-regression band: ten times the measured 732-attempt completion baseline — the numeric conjunct the demonstration face must stay inside under the widened scope.</summary>
    private const int Nomr2FragmentedAttemptBand = 7_320;

    /// <summary>
    /// VERDICT SWEEP, SOUND-OR-SILENT: every certified
    /// nominal-battery row decided under
    /// <see cref="NominalParamodulationScope.LicenseScoped"/> — on BOTH
    /// topologies — either matches the shipped default exactly (outcome,
    /// context-decided path, verdict, and the EXACT subsumption set) or is
    /// WITHHELD by the blocked-live latch with counter attribution; a widened
    /// run that CERTIFIES through the context engine must be verdict-identical,
    /// and the shipped run keeps all three scope counters dark. The measured
    /// finding this row records: the atom axis blocks live
    /// query-context traffic on a large share of the certified rows — the P3a
    /// Q-local delta is real on this battery, the latch carries those reads,
    /// and the everywhere-identical expectation holds only for the
    /// certifying subset. The one named budget-divergent row
    /// is excluded by name and pinned by its own face.
    /// </summary>
    [TestMethod]
    public void WidenedScopeKeepsEveryNominalBatteryVerdict()
    {
        StringBuilder report = new();
        report.AppendLine("\nrow | outcome shipped/licSingle/licFragmented | identity single/fragmented | blockedQ/blockedR/joins (fragmented engine)");
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool _, string[] _) in ContextNominalBatteryTests.BatteryRows())
        {
            if(string.Equals(name, BudgetDivergentRow, StringComparison.Ordinal))
            {
                continue;
            }

            ModuleDecision shipped = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
            EngineCapture singleCapture = new();
            ModuleDecision widenedSingle = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.LicenseScoped, RootContextTopology.SingleRoot, RootPropagationRelevance.Unrestricted, ReasoningConfiguration.Default.Budget, singleCapture.Handle, TestContext.CancellationToken);
            EngineCapture fragmentedCapture = new();
            ModuleDecision widenedFragmented = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.LicenseScoped, RootContextTopology.PerIndividualRoots, RootPropagationRelevance.Unrestricted, ReasoningConfiguration.Default.Budget, fragmentedCapture.Handle, TestContext.CancellationToken);
            ContextSaturationStatistics shippedTotals = shipped.Statistics.ContextTotals;
            if(shippedTotals.EqScopeBlockedQueryAtom != 0 || shippedTotals.EqScopeBlockedRootClass != 0 || shippedTotals.EqScopeTagJoins != 0)
            {
                mismatches.Add(name + ": the shipped default run moved a scope counter — the dark mode leaked.");
            }

            string singleFace = ClassifyWidenedRun(name, "single-root", shipped, widenedSingle, singleCapture, mismatches);
            string fragmentedFace = ClassifyWidenedRun(name, "fragmented", shipped, widenedFragmented, fragmentedCapture, mismatches);
            ContextSaturationStatistics fragmentedEngineTotals = fragmentedCapture.Engine is null ? widenedFragmented.Statistics.ContextTotals : fragmentedCapture.Engine.BuildStatistics(contextDecided: widenedFragmented.Statistics.ContextTotals.ContextDecided);
            report.AppendLine(name + " | " + shipped.Outcome + "/" + widenedSingle.Outcome + "/" + widenedFragmented.Outcome
                + " | " + singleFace + "/" + fragmentedFace
                + " | " + fragmentedEngineTotals.EqScopeBlockedQueryAtom + "/" + fragmentedEngineTotals.EqScopeBlockedRootClass + "/" + fragmentedEngineTotals.EqScopeTagJoins);
        }

        TestContext.WriteLine(report.ToString());
        Assert.IsEmpty(mismatches, report.ToString());
    }

    /// <summary>Classifies one widened cell of the sweep: IDENTICAL when the decision matches the shipped default exactly; WITHHELD when the context engine did not certify and the captured engine carries the blocked-live attribution on some surface; anything else — a certified divergence, or a withheld run without attribution — is a mismatch.</summary>
    /// <param name="name">The row name.</param>
    /// <param name="cell">The widened cell label.</param>
    /// <param name="shipped">The shipped-default decision.</param>
    /// <param name="widened">The widened decision.</param>
    /// <param name="capture">The widened run's engine capture.</param>
    /// <param name="mismatchesToAppendTo">The sweep's mismatch accumulator.</param>
    /// <returns>The face token for the report line.</returns>
    private static string ClassifyWidenedRun(string name, string cell, ModuleDecision shipped, ModuleDecision widened, EngineCapture capture, List<string> mismatchesToAppendTo)
    {
        if(DecisionsIdentical(shipped, widened))
        {
            return "IDENTICAL";
        }

        if(!widened.Statistics.ContextTotals.ContextDecided
            && capture.Engine is not null
            && (capture.Engine.HasEqScopeBlockedConsistencyReadOff || capture.Engine.HasEqScopeBlockedQueryReadOff))
        {
            return "WITHHELD";
        }

        mismatchesToAppendTo.Add(name + ": the widened scope diverged from the shipped default on the " + cell + " topology without latch attribution (outcome " + shipped.Outcome + " vs " + widened.Outcome + ").");

        return "MISMATCH";
    }

    /// <summary>
    /// PIGN COMPOSITION PIN: the bound-two three-successor pigeonhole (the
    /// fragmented battery's multi-maximal equality-head row) re-run under the
    /// widened scope on the fragmented topology — the scope gate sits inside
    /// the Eq application after the constant guard, so it composes with the
    /// acting-literal dispatch by construction and the certified INCONSISTENT
    /// verdict must stand with the shipped run verdict-identical.
    /// </summary>
    [TestMethod]
    public void PignFragmentedRowHoldsUnderTheWidenedScope()
    {
        ReasoningModule module = Module(
            InertNominal(),
            SubClassOf(Class("A"), Max("r", 2, null)),
            ClassAssertion(Class("A"), Individual("a")),
            Edge("r", "a", "b1"),
            Edge("r", "a", "b2"),
            Edge("r", "a", "b3"),
            Different("b1", "b2", "b3"));
        ModuleDecision fragmented = DecideFragmented(module);
        ModuleDecision widened = DecideWidenedFragmented(module, engineProbe: null);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, fragmented.Outcome, "The pigeonhole module is decided under the shipped fragmented run.");
        Assert.IsFalse(fragmented.Verdict!.IsConsistent, "Three pairwise-distinct successors under a told bound of two clash.");
        Assert.IsTrue(DecisionsIdentical(fragmented, widened), "The widened scope keeps the pigeonhole verdict on the fragmented topology (outcome " + fragmented.Outcome + " vs " + widened.Outcome + ").");
        TestContext.WriteLine(ScopeCounterLine("PignWidened", widened.Statistics.ContextTotals));
    }

    /// <summary>
    /// NOMR-2 NON-REGRESSION: the fragmented ENGINE demonstration face — the
    /// certified INCONSISTENT enumeration-CSP cell that fragmentation
    /// dissolves to a 732-attempt completion — must complete under the widened
    /// scope inside the ten-fold numeric band with the same certified verdict.
    /// Both runs drive the explicit dark control behind the lit production
    /// default: the production surface decides NOMR-2 pre-engine, and this row
    /// instruments the engine face beneath it. A derived inconsistency is
    /// decisive regardless of any blocked rewrite (the widened scope only
    /// removes inferences), so the blocked-live latch never withholds this
    /// face.
    /// </summary>
    [TestMethod]
    public void Nomr2FragmentedCompletionStandsUnderTheWidenedScope()
    {
        ReasoningBudget band = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: Nomr2FragmentedAttemptBand);
        ModuleDecision baseline = ContextSaturationModuleReasoner.DecideModule(ContextNominalBatteryTests.Nomr2Module(), EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.PerIndividualRoots, band, TestContext.CancellationToken);
        ModuleDecision widened = ContextSaturationModuleReasoner.DecideModule(ContextNominalBatteryTests.Nomr2Module(), EnumerationDeciderFaces.None, NominalParamodulationScope.LicenseScoped, RootContextTopology.PerIndividualRoots, band, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, baseline.Outcome, "The shipped fragmented run completes NOMR-2 inside the band — the 732-attempt baseline anchor.");
        Assert.IsFalse(baseline.Verdict!.IsConsistent, "The certified NOMR-2 verdict is INCONSISTENT.");
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, widened.Outcome, "The widened scope keeps the fragmented completion inside the ten-fold band.");
        Assert.IsFalse(widened.Verdict!.IsConsistent, "The certified INCONSISTENT verdict stands under the widened scope.");
        Assert.IsLessThanOrEqualTo((long)Nomr2FragmentedAttemptBand, widened.Statistics.ContextTotals.InferenceAttempts, "The widened completion spends at most " + Nomr2FragmentedAttemptBand + " attempts (observed " + widened.Statistics.ContextTotals.InferenceAttempts + ").");
        TestContext.WriteLine(ScopeCounterLine("Nomr2Widened", widened.Statistics.ContextTotals) + " baselineAttempts=" + baseline.Statistics.ContextTotals.InferenceAttempts);
    }

    /// <summary>
    /// HOME-SEEDED MERGE (the P4a case-(d) witness, re-based on a decide-able
    /// shape): the concept-complement merge decides INCONSISTENT under the
    /// shipped fragmented run — verified here first, the build-verification the
    /// round-3 retraction demanded — and under the widened scope the verdict is
    /// either KEPT through the ungated clash paths or withheld by the
    /// blocked-live latch with counter attribution; a wrong whole-module
    /// CONSISTENT is the one impossible face. Never a self-loop shape.
    /// </summary>
    [TestMethod]
    public void FragmentedHomeSeededMergeKeepsTheVerdictUnderTheWidenedScope()
    {
        ReasoningModule module = Module(
            InertNominal(),
            Same("a", "b"),
            ClassAssertion(Class("P"), Individual("a")),
            ClassAssertion(Complement(Class("P")), Individual("b")));
        ModuleDecision baseline = DecideFragmented(module);
        EngineCapture capture = new();
        ModuleDecision widened = DecideWidenedFragmented(module, capture.Handle);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, baseline.Outcome, "The home-seeded merge DECIDES under shipped fragmentation — the case-(d) witness is built on a decide-able shape.");
        Assert.IsTrue(baseline.Statistics.ContextTotals.ContextDecided, "The shipped fragmented run decides IN-ENGINE — the round-3 build verification requires the context engine, never the fallback.");
        Assert.IsFalse(baseline.Verdict!.IsConsistent, "The merged individual carries both the class and its complement.");
        ContextSaturationStatistics widenedTotals = widened.Statistics.ContextTotals;
        TestContext.WriteLine(ScopeCounterLine("HomeSeededMerge", widenedTotals) + " outcome=" + widened.Outcome + EngineLatchSuffix(capture));
        bool keptVerdict = widenedTotals.ContextDecided && widened.Outcome == ReasoningDecisionOutcome.Decided && widened.Verdict is { IsConsistent: false };
        if(!keptVerdict)
        {
            Assert.IsFalse(widened.Outcome == ReasoningDecisionOutcome.Decided && widened.Verdict is { IsConsistent: true } && widenedTotals.ContextDecided, "The latch never lets a blocked run assert a whole CONSISTENT — sound-or-silent.");
            Assert.IsNotNull(capture.Engine, "The probe captured the widened engine for the attribution read.");
            Assert.IsTrue(capture.Engine.HasEqScopeBlockedConsistencyReadOff || capture.Engine.HasEqScopeBlockedQueryReadOff, "A withheld verdict carries the blocked-live attribution on at least one surface.");
        }
    }

    /// <summary>
    /// CONNECTOR CLASH (the P4a case-(c) construction): a
    /// root-class clash linking two pushed facts through scopable equality
    /// material that is an INTRA-CONTEXT residual of pushed clauses. Two
    /// carrier images land in the b-root bearing the same <c>x ≈ c</c> tail
    /// over complementary <c>c ≈ e</c> / <c>c ≉ e</c> disjuncts — their
    /// Ineq-and-factor residual is the unit merge <c>x ≈ c</c>, tagged
    /// transitively through the conclusion sink — and a third image carries the
    /// ground role fact <c>r(c,d)</c> behind an <c>x ≈ d</c> tail the home
    /// inequality resolves away; the scopable rewrite <c>c ↦ x</c> links the
    /// residual role fact to the home negative-assertion denial
    /// <c>r(x,d) → ⊥</c>, and no self-loop shape participates. The
    /// row asserts the shipped fragmented run decides INCONSISTENT in-engine
    /// (build verification) and the widened scope keeps the certified verdict
    /// with the tag machinery exercised on the deciding run (untagged
    /// home-seeded rewrites blocked, pushed material admitted, joins charged).
    /// The told-material merge ALSO derives in the exempt ground lane through
    /// never-scoped ground rewrites — the control measured the clash
    /// deciding under the blunt narrowing through that sibling path — so the
    /// case-(c) CLASH face alone does not separate the tag from the blunt
    /// narrowing; the separation lives on the consistency-certification face.
    /// </summary>
    [TestMethod]
    public void FragmentedConnectorClashDecidesUnderTheWidenedScope()
    {
        ReasoningModule module = ConnectorModule();
        ModuleDecision baseline = DecideFragmented(module);
        EngineCapture capture = new();
        ModuleDecision widened = DecideWidenedFragmented(module, capture.Handle);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, baseline.Outcome, "The connector module is decided under the shipped fragmented run.");
        Assert.IsTrue(baseline.Statistics.ContextTotals.ContextDecided, "The shipped fragmented run decides IN-ENGINE — the witness is built on the context engine, never the fallback.");
        Assert.IsFalse(baseline.Verdict!.IsConsistent, "The merged individual's pushed role fact contradicts the home negative assertion.");
        ContextSaturationStatistics widenedTotals = capture.Engine is null ? widened.Statistics.ContextTotals : capture.Engine.BuildStatistics(contextDecided: widened.Statistics.ContextTotals.ContextDecided);
        TestContext.WriteLine(ScopeCounterLine("ConnectorClash", widenedTotals) + " outcome=" + widened.Outcome + EngineLatchSuffix(capture));
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, widened.Outcome, "The widened scope keeps the connector clash — the tag-admitted rewrite decides.");
        Assert.IsTrue(widened.Statistics.ContextTotals.ContextDecided, "The widened run decides IN-ENGINE through the context engine.");
        Assert.IsFalse(widened.Verdict!.IsConsistent, "The certified INCONSISTENT verdict stands under the widened scope.");
    }

    /// <summary>The case-(c) connector module: <c>c = e ∨ c = b</c> and <c>c ≠ e ∨ c = b</c> force the merge <c>c = b</c> as a pushed-material residual in the b-root; <c>r(c,d) ∨ d = b</c> with <c>b ≠ d</c> forces the pushed role fact <c>r(c,d)</c>; the merge carries it onto <c>b</c>, contradicting the told negative assertion <c>¬r(b,d)</c>.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ConnectorModule()
    {
        return Module(
            InertNominal(),
            ClassAssertion(Union(OneOf("e"), OneOf("b")), Individual("c")),
            ClassAssertion(Union(Complement(OneOf("e")), OneOf("b")), Individual("c")),
            ClassAssertion(Union(HasInverseValue("r", "c"), OneOf("b")), Individual("d")),
            Different("b", "d"),
            NegativeEdge("r", "b", "d"));
    }

    /// <summary>
    /// QUERY-LOCAL CLASH (the P3a delta witness): a class condemned only inside
    /// its own query context through a scopable rewrite on a NON-query-atom
    /// target — the derived <c>x ≈ o</c> acting on the derived <c>x ≉ o</c>
    /// inequality, whose rewrite collapses to the empty clause. The shipped
    /// default finds the <c>A ⊑ ⊥</c> subsumption face; the widened scope
    /// blocks the rewrite (an inequality is not a query atom), so the latch's
    /// query-surface arm must withhold the satisfiable/non-subsumption read
    /// with the query-atom counter carrying the attribution — the
    /// wrong-SATISFIABLE guard exercised, not assumed.
    /// </summary>
    [TestMethod]
    public void QueryLocalClashAbstainsThroughTheBlockedLiveLatch()
    {
        ReasoningModule module = Module(
            SubClassOf(Class("A"), OneOf("o")),
            SubClassOf(Class("A"), Complement(OneOf("o"))),
            Bystander());
        ModuleDecision shipped = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        EngineCapture capture = new();
        ModuleDecision widened = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.LicenseScoped, RootContextTopology.SingleRoot, RootPropagationRelevance.Unrestricted, ReasoningConfiguration.Default.Budget, capture.Handle, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, shipped.Outcome, "The shipped default decides the module whole.");
        Assert.IsTrue(shipped.Verdict!.IsConsistent, "An unsatisfiable class leaves the module consistent.");
        Assert.Contains(Sub("A", "Spruce"), SubsumptionKeys(shipped.Verdict), "The shipped default reads the condemned class subsumed by everything — the query-context empty clause landed.");
        Assert.IsNotNull(capture.Engine, "The probe captured the widened engine for the attribution read.");
        ContextSaturationStatistics widenedEngineTotals = capture.Engine.BuildStatistics(contextDecided: false);
        TestContext.WriteLine(ScopeCounterLine("QueryLocalClash", widenedEngineTotals) + " outcome=" + widened.Outcome + EngineLatchSuffix(capture));
        Assert.IsGreaterThan(0L, widenedEngineTotals.EqScopeBlockedQueryAtom, "The atom axis blocked the non-query-atom rewrite and charged its counter.");
        Assert.IsTrue(capture.Engine.HasEqScopeBlockedQueryReadOff, "The blocked query context latched the certificate's query-surface arm.");
        Assert.IsFalse(widened.Statistics.ContextTotals.ContextDecided, "The affected read is withheld — the context engine never certifies the blocked query surface.");
    }

    /// <summary>
    /// BLOCKED-LIVE CERTIFICATE, CONSISTENCY SURFACE: a consistent module whose
    /// scopable root-class rewrite rides a home-seeded (push-ancestor-free)
    /// equality — admitted under the shipped fragmented run, blocked by the
    /// push-provenance gate under the widened scope. The fixpoint would assert
    /// CONSISTENT, so the latch's consistency-surface arm must withhold the
    /// positive verdict with the root-class counter carrying the attribution.
    /// The single-root cell charges NO root-class block — the context axis's
    /// exemption face — while the topology-independent atom axis may still
    /// withhold that cell on its own arm; a certified divergence is
    /// the one impossible face.
    /// </summary>
    [TestMethod]
    public void WidenedScopeBlockedLiveCertificateGatesThePositiveVerdict()
    {
        ReasoningModule module = Module(
            InertNominal(),
            Same("a", "b"),
            Edge("r", "a", "b"),
            Bystander());
        ModuleDecision baseline = DecideFragmented(module);
        EngineCapture capture = new();
        ModuleDecision widened = DecideWidenedFragmented(module, capture.Handle);
        EngineCapture singleCapture = new();
        ModuleDecision widenedSingle = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.LicenseScoped, RootContextTopology.SingleRoot, RootPropagationRelevance.Unrestricted, ReasoningConfiguration.Default.Budget, singleCapture.Handle, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, baseline.Outcome, "The merged-edge module is decided under the shipped fragmented run.");
        Assert.IsTrue(baseline.Verdict!.IsConsistent, "The merged edge is satisfiable.");
        Assert.IsNotNull(capture.Engine, "The probe captured the widened engine for the attribution read.");
        ContextSaturationStatistics widenedEngineTotals = capture.Engine.BuildStatistics(contextDecided: false);
        TestContext.WriteLine(ScopeCounterLine("BlockedLiveCertificate", widenedEngineTotals) + " outcome=" + widened.Outcome + EngineLatchSuffix(capture));
        Assert.IsGreaterThan(0L, widenedEngineTotals.EqScopeBlockedRootClass, "The context axis blocked the push-ancestor-free rewrite and charged its counter.");
        Assert.IsTrue(capture.Engine.HasEqScopeBlockedConsistencyReadOff, "The blocked root-class rewrite latched the certificate's consistency-surface arm.");
        Assert.IsFalse(widened.Statistics.ContextTotals.ContextDecided, "A run with a blocked consistency read-off never asserts CONSISTENT through the context engine — sound-or-silent.");
        Assert.IsNotNull(singleCapture.Engine, "The probe captured the single-root engine for the exemption read.");
        Assert.AreEqual(0L, singleCapture.Engine.BuildStatistics(contextDecided: false).EqScopeBlockedRootClass, "The single root charges no root-class block — the context-axis exemption's counter face; the atom axis stays topology-independent and may withhold on its own arm.");
        Assert.IsTrue(DecisionsIdentical(baseline, widenedSingle) || (!widenedSingle.Statistics.ContextTotals.ContextDecided && singleCapture.Engine.HasEqScopeBlockedQueryReadOff), "The single-root cell is verdict-identical or query-withheld with attribution — never a certified divergence (outcome " + baseline.Outcome + " vs " + widenedSingle.Outcome + ").");
    }

    /// <summary>
    /// COUNTER ATTRIBUTION: the tag-join counter fires where a pushed carrier
    /// image is absorbed by a content-identical home-seeded (untagged) survivor
    /// — the reciprocal same-individual pair seeds each orientation home and
    /// the carrier re-derives it pushed — and the join changes bookkeeping,
    /// never a certified verdict (the run may still be atom-axis-withheld with
    /// attribution); all three counters stay ZERO on the shipped
    /// default, and the tag machinery stays dark on the widened single-root
    /// cell — the dark-ship face at counter granularity. The blocked-counter
    /// faces are consumed by the two latch rows.
    /// </summary>
    [TestMethod]
    public void WidenedScopeBlockCountersAttributeTheCut()
    {
        ReasoningModule module = Module(
            InertNominal(),
            Same("a", "b"),
            Same("b", "a"),
            Bystander());
        ModuleDecision shipped = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ModuleDecision baseline = DecideFragmented(module);
        EngineCapture capture = new();
        ModuleDecision widened = DecideWidenedFragmented(module, capture.Handle);
        ModuleDecision widenedSingle = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.LicenseScoped, RootContextTopology.SingleRoot, RootPropagationRelevance.Unrestricted, ReasoningConfiguration.Default.Budget, engineProbe: null, TestContext.CancellationToken);

        ContextSaturationStatistics shippedTotals = shipped.Statistics.ContextTotals;
        Assert.AreEqual(0L, shippedTotals.EqScopeBlockedQueryAtom, "The shipped default never charges the query-atom counter.");
        Assert.AreEqual(0L, shippedTotals.EqScopeBlockedRootClass, "The shipped default never charges the root-class counter.");
        Assert.AreEqual(0L, shippedTotals.EqScopeTagJoins, "The shipped default never joins a tag.");
        ContextSaturationStatistics singleTotals = widenedSingle.Statistics.ContextTotals;
        Assert.AreEqual(0L, singleTotals.EqScopeTagJoins, "The tag machinery is dark under the single-root topology even with the widened scope selected.");
        Assert.IsNotNull(capture.Engine, "The probe captured the widened fragmented engine for the counter read.");
        ContextSaturationStatistics widenedTotals = capture.Engine.BuildStatistics(contextDecided: widened.Statistics.ContextTotals.ContextDecided);
        TestContext.WriteLine(ScopeCounterLine("CounterAttribution", widenedTotals) + " outcome=" + widened.Outcome + EngineLatchSuffix(capture));
        Assert.IsGreaterThan(0L, widenedTotals.EqScopeTagJoins, "The reciprocal pushed image absorbed by its untagged home twin joins the tag and charges the counter.");
        Assert.IsTrue(DecisionsIdentical(baseline, widened) || (!widened.Statistics.ContextTotals.ContextDecided && (capture.Engine.HasEqScopeBlockedConsistencyReadOff || capture.Engine.HasEqScopeBlockedQueryReadOff)), "The tag join changes bookkeeping, never a certified verdict — identical or latch-withheld with attribution (outcome " + baseline.Outcome + " vs " + widened.Outcome + ").");
    }

    /// <summary>
    /// NOMR-1 UNDER THE WIDENED SCOPE (the jurisdiction RECORD row): the
    /// domain-collapse habitat whose fragmented run diverges at the default
    /// ceiling — excluded from every sweep by name — run once under the widened
    /// fragmented cell to RECORD the measured outcome: a decide at the ceiling,
    /// a budget abstention, or a latch-withheld delegation are all sound
    /// outcomes; a wrong whole-module INCONSISTENT is the one impossible face.
    /// The standing divergence pin
    /// (<c>FragmentedNomrOneDivergesAtTheDefaultCeiling</c>) moves only with
    /// the decision rules, never through this row.
    /// </summary>
    [TestMethod]
    public void FragmentedNomrOneUnderTheWidenedScope()
    {
        EngineCapture capture = new();
        ModuleDecision widened = DecideWidenedFragmented(ContextNominalBatteryTests.Nomr1Module(), capture.Handle);

        Assert.IsNotNull(capture.Engine, "The probe captured the widened engine for the attribution read.");
        ContextSaturationStatistics totals = capture.Engine.BuildStatistics(contextDecided: widened.Statistics.ContextTotals.ContextDecided);
        TestContext.WriteLine(ScopeCounterLine("Nomr1Widened", totals)
            + " outcome=" + widened.Outcome
            + " attempts=" + totals.InferenceAttempts
            + " generated=" + totals.GeneratedNominals
            + " depth=" + totals.MaxNominalLabelDepth
            + EngineLatchSuffix(capture));
        Assert.IsFalse(widened.Outcome == ReasoningDecisionOutcome.Decided && widened.Statistics.ContextTotals.ContextDecided && widened.Verdict is { IsConsistent: false }, "The consistent collapse module never reads INCONSISTENT — the widened scope only removes inferences.");
    }

    /// <summary>The environment variable naming the ENUM-1 scope-cost probe's absolute output path; unset means the probe passes without measuring — measurement scaffolding, never a correctness gate.</summary>
    private const string Enum1ScopeCostVariable = "VERITAS_ENUM1_SCOPE_COST";

    /// <summary>The escalation ceiling the scope-cost probe re-runs a backstop-abstained cell at, aligned with the deep probe's power-of-two ceiling.</summary>
    private const int Enum1ScopeEscalationAttempts = 4_194_304;

    /// <summary>
    /// The ENUM-1 both-topologies-both-scopes completion-cost probe (the
    /// measurement program's scope column over the topology instrument):
    /// measurement scaffolding that runs only when
    /// <see cref="Enum1ScopeCostVariable"/> names an absolute output file, and
    /// otherwise passes without measuring. When it runs, the certified ENUM-1
    /// cell decides under every topology-scope combination at the measured 600k
    /// backstop, a backstop abstention escalating once to the deep-probe
    /// ceiling, and each run's outcome, attempt count, carrier counters, and
    /// scope counters are written to the named file — the default-face and
    /// jurisdiction reads the decision rules consume.
    /// </summary>
    [TestMethod]
    public void Enum1WidenedScopeCompletionCostWritesTheRead()
    {
        string? outputPath = Environment.GetEnvironmentVariable(Enum1ScopeCostVariable);
        if(string.IsNullOrWhiteSpace(outputPath))
        {
            TestContext.WriteLine("Skipping the ENUM-1 scope-cost probe: set " + Enum1ScopeCostVariable + " to an absolute output path to run it.");

            return;
        }

        StringBuilder report = new();
        report.AppendLine("ENUM-1 both-topologies-both-scopes completion cost (600k backstop; a backstop abstention escalates once to the deep-probe ceiling)");
        report.AppendLine(CultureInfo.InvariantCulture, $"host: machine={Environment.MachineName} os={Environment.OSVersion.VersionString} runtime={Environment.Version} cores={Environment.ProcessorCount}");
        foreach(RootContextTopology topology in (RootContextTopology[])[RootContextTopology.SingleRoot, RootContextTopology.PerIndividualRoots])
        {
            foreach(NominalParamodulationScope scope in (NominalParamodulationScope[])[NominalParamodulationScope.QueryScoped, NominalParamodulationScope.LicenseScoped])
            {
                ReasoningDecisionOutcome outcome = AppendEnum1ScopeRun(report, topology, scope, 600_000, "backstop");
                if(outcome == ReasoningDecisionOutcome.AbstainedBudget)
                {
                    AppendEnum1ScopeRun(report, topology, scope, Enum1ScopeEscalationAttempts, "escalated");
                }

            }

        }

        File.WriteAllText(outputPath, report.ToString());
        TestContext.WriteLine("ENUM-1 scope-cost probe written to " + outputPath + ".");
    }

    /// <summary>Decides the ENUM-1 module once under a topology-scope cell and inference ceiling and appends the run's read line.</summary>
    /// <param name="report">The report appended to.</param>
    /// <param name="topology">The root-tier topology.</param>
    /// <param name="scope">The paramodulation scope.</param>
    /// <param name="ceiling">The inference ceiling.</param>
    /// <param name="label">The run label distinguishing the backstop read from an escalation.</param>
    /// <returns>The decision outcome.</returns>
    private ReasoningDecisionOutcome AppendEnum1ScopeRun(StringBuilder report, RootContextTopology topology, NominalParamodulationScope scope, int ceiling, string label)
    {
        EngineCapture capture = new();
        Stopwatch clock = Stopwatch.StartNew();
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(ContextNominalBatteryTests.Enum1Module(), EnumerationDeciderFaces.None, scope, topology, RootPropagationRelevance.Unrestricted, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: ceiling), capture.Handle, TestContext.CancellationToken);
        clock.Stop();

        ContextSaturationStatistics totals = capture.Engine is null ? decision.Statistics.ContextTotals : capture.Engine.BuildStatistics(contextDecided: decision.Statistics.ContextTotals.ContextDecided);
        report.AppendLine(CultureInfo.InvariantCulture, $"{label}: topology={topology} | scope={scope} | ceiling={ceiling} | outcome={decision.Outcome} | attempts={totals.InferenceAttempts} | wall={clock.Elapsed.TotalSeconds:F2}s | nominalRoots={totals.NominalRootContexts} interNominal={totals.InterNominalPropagations} interNominalRedundant={totals.InterNominalRedundant} | blockedQueryAtom={totals.EqScopeBlockedQueryAtom} blockedRootClass={totals.EqScopeBlockedRootClass} tagJoins={totals.EqScopeTagJoins} | rules hyper={totals.HyperApplications} eq={totals.EqApplications} factor={totals.FactorApplications} join={totals.JoinApplications} rootSucc={totals.RootSuccApplications} rootPred={totals.RootPredApplications} nom={totals.NomApplications}");
        if(capture.Landings is not null)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"{label}: topology={topology} | scope={scope} | eqLanded rootClass={capture.Landings.RootClassLanded} ordinary={capture.Landings.OrdinaryLanded} | eqShapes {capture.Landings.ShapeLine()}");
        }

        return decision.Outcome;
    }

    /// <summary>The environment variable naming the NOMR-1 jurisdiction probe's absolute output path; unset means the probe passes without measuring — measurement scaffolding, never a correctness gate.</summary>
    private const string Nomr1JurisdictionVariable = "VERITAS_NOMR1_JURISDICTION";

    /// <summary>
    /// The NOMR-1 jurisdiction probe (the measurement program's P5/P4b
    /// driver attribution): measurement scaffolding that runs only when
    /// <see cref="Nomr1JurisdictionVariable"/> names an absolute output file,
    /// and otherwise passes without measuring. When it runs, the
    /// domain-collapse habitat is decided under the fragmented topology at the
    /// default ceiling under both scopes, a budget abstention escalating once
    /// to the deep-probe ceiling, and each run's outcome, mint-ladder reads
    /// (generated nominals and label depth), scope counters, latch flags, and
    /// per-landing rewrite-shape attribution are written to the named file —
    /// the jurisdiction conjunct (a license-scoped decide at the default
    /// ceiling) and the P5 driver attribution (the shipped run's root-class
    /// scopable share against the widened run's blocked counters) are read
    /// here, and the standing divergence pin moves only through the decision
    /// rules, never through this probe.
    /// </summary>
    [TestMethod]
    public void Nomr1JurisdictionProbeWritesTheRead()
    {
        string? outputPath = Environment.GetEnvironmentVariable(Nomr1JurisdictionVariable);
        if(string.IsNullOrWhiteSpace(outputPath))
        {
            TestContext.WriteLine("Skipping the NOMR-1 jurisdiction probe: set " + Nomr1JurisdictionVariable + " to an absolute output path to run it.");

            return;
        }

        StringBuilder report = new();
        report.AppendLine("NOMR-1 jurisdiction probe (fragmented topology, both scopes; default ceiling with one escalation to the deep-probe ceiling)");
        report.AppendLine(CultureInfo.InvariantCulture, $"host: machine={Environment.MachineName} os={Environment.OSVersion.VersionString} runtime={Environment.Version} cores={Environment.ProcessorCount}");
        foreach(NominalParamodulationScope scope in (NominalParamodulationScope[])[NominalParamodulationScope.QueryScoped, NominalParamodulationScope.LicenseScoped])
        {
            ReasoningDecisionOutcome outcome = AppendNomr1Run(report, scope, ReasoningConfiguration.Default.Budget, "default-ceiling");
            if(outcome == ReasoningDecisionOutcome.AbstainedBudget)
            {
                AppendNomr1Run(report, scope, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: Enum1ScopeEscalationAttempts), "escalated");
            }

        }

        File.WriteAllText(outputPath, report.ToString());
        TestContext.WriteLine("NOMR-1 jurisdiction probe written to " + outputPath + ".");
    }

    /// <summary>Decides the NOMR-1 module once under the fragmented topology, a scope, and a budget, and appends the run's read lines.</summary>
    /// <param name="report">The report appended to.</param>
    /// <param name="scope">The paramodulation scope.</param>
    /// <param name="budget">The reasoning budget.</param>
    /// <param name="label">The run label distinguishing the default-ceiling read from an escalation.</param>
    /// <returns>The decision outcome.</returns>
    private ReasoningDecisionOutcome AppendNomr1Run(StringBuilder report, NominalParamodulationScope scope, ReasoningBudget budget, string label)
    {
        EngineCapture capture = new();
        Stopwatch clock = Stopwatch.StartNew();
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(ContextNominalBatteryTests.Nomr1Module(), EnumerationDeciderFaces.None, scope, RootContextTopology.PerIndividualRoots, RootPropagationRelevance.Unrestricted, budget, capture.Handle, TestContext.CancellationToken);
        clock.Stop();

        ContextSaturationStatistics totals = capture.Engine is null ? decision.Statistics.ContextTotals : capture.Engine.BuildStatistics(contextDecided: decision.Statistics.ContextTotals.ContextDecided);
        report.AppendLine(CultureInfo.InvariantCulture, $"{label}: scope={scope} | ceiling={budget.MaxInferences} | outcome={decision.Outcome} | attempts={totals.InferenceAttempts} | wall={clock.Elapsed.TotalSeconds:F2}s | generatedNominals={totals.GeneratedNominals} labelDepth={totals.MaxNominalLabelDepth} | nominalRoots={totals.NominalRootContexts} interNominal={totals.InterNominalPropagations} interNominalRedundant={totals.InterNominalRedundant} | blockedQueryAtom={totals.EqScopeBlockedQueryAtom} blockedRootClass={totals.EqScopeBlockedRootClass} tagJoins={totals.EqScopeTagJoins}{EngineLatchSuffix(capture)}");
        report.AppendLine(CultureInfo.InvariantCulture, $"{label}: scope={scope} | rules hyper={totals.HyperApplications} eq={totals.EqApplications} ineq={totals.IneqApplications} factor={totals.FactorApplications} pred={totals.PredApplications} join={totals.JoinApplications} rootSucc={totals.RootSuccApplications} rootPred={totals.RootPredApplications} nom={totals.NomApplications}");
        if(capture.Landings is not null)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"{label}: scope={scope} | eqLanded rootClass={capture.Landings.RootClassLanded} ordinary={capture.Landings.OrdinaryLanded} | eqShapes {capture.Landings.ShapeLine()}");
        }

        return decision.Outcome;
    }

    /// <summary>Captures the saturation engine a module decision constructs, so a row can read the scope counters and latch flags off the engine after a delegated decision whose module-level statistics carry the fallback's totals; a key-merge fixpoint invokes the probe per round and the last engine wins. Each captured engine gets a fresh per-landing Eq accumulation attached — the rewrite-shape attribution read.</summary>
    private sealed class EngineCapture
    {
        /// <summary>The last engine the decision constructed, or <see langword="null"/> before the first round.</summary>
        public ContextSaturationEngine? Engine { get; private set; }

        /// <summary>The per-landing Eq accumulation attached to the last captured engine, or <see langword="null"/> before the first round.</summary>
        public ContextNominalBatteryTests.EqLandingAccumulator? Landings { get; private set; }

        /// <summary>Receives one constructed engine, keeping it for the post-run reads and attaching a fresh landed-Eq accumulation.</summary>
        /// <param name="engine">The created engine.</param>
        public void Handle(ContextSaturationEngine engine)
        {
            Engine = engine;
            Landings = new ContextNominalBatteryTests.EqLandingAccumulator();
            engine.EqLandingProbe = Landings.Handle;
        }
    }

    /// <summary>Decides a module through the production reasoner under the shipped fragmented cell — the query-scoped baseline the widened rows compare against.</summary>
    /// <param name="module">The module.</param>
    /// <returns>The decision.</returns>
    private ModuleDecision DecideFragmented(ReasoningModule module)
    {
        return ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.QueryScoped, RootContextTopology.PerIndividualRoots, RootPropagationRelevance.Unrestricted, ReasoningConfiguration.Default.Budget, engineProbe: null, TestContext.CancellationToken);
    }

    /// <summary>Decides a module through the production reasoner under the widened fragmented cell — the license-scoped jurisdiction cell these rows exercise.</summary>
    /// <param name="module">The module.</param>
    /// <param name="engineProbe">The optional engine capture.</param>
    /// <returns>The decision.</returns>
    private ModuleDecision DecideWidenedFragmented(ReasoningModule module, SaturationEngineProbeDelegate? engineProbe)
    {
        return ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, NominalParamodulationScope.LicenseScoped, RootContextTopology.PerIndividualRoots, RootPropagationRelevance.Unrestricted, ReasoningConfiguration.Default.Budget, engineProbe, TestContext.CancellationToken);
    }

    /// <summary>Whether two module decisions are identical on outcome, context-decided path, verdict presence, consistency, and the exact subsumption set.</summary>
    /// <param name="expected">The baseline decision.</param>
    /// <param name="actual">The compared decision.</param>
    /// <returns><see langword="true"/> when identical.</returns>
    private static bool DecisionsIdentical(ModuleDecision expected, ModuleDecision actual)
    {
        return expected.Outcome == actual.Outcome
            && expected.Statistics.ContextTotals.ContextDecided == actual.Statistics.ContextTotals.ContextDecided
            && (expected.Verdict is null) == (actual.Verdict is null)
            && (expected.Verdict is null || expected.Verdict.IsConsistent == actual.Verdict!.IsConsistent)
            && (expected.Verdict is null || KeysEqual(SubsumptionKeys(expected.Verdict), SubsumptionKeys(actual.Verdict!)));
    }

    /// <summary>One log line of the scope counters for a named row.</summary>
    /// <param name="name">The row label.</param>
    /// <param name="totals">The run's context totals.</param>
    /// <returns>The line.</returns>
    private static string ScopeCounterLine(string name, ContextSaturationStatistics totals)
    {
        return name + ": blockedQueryAtom=" + totals.EqScopeBlockedQueryAtom + " blockedRootClass=" + totals.EqScopeBlockedRootClass + " tagJoins=" + totals.EqScopeTagJoins
            + " roots=" + totals.NominalRootContexts + " landed=" + totals.InterNominalPropagations + " absorbed=" + totals.InterNominalRedundant + " attempts=" + totals.InferenceAttempts;
    }

    /// <summary>The captured engine's latch flags as a log suffix, empty when no engine was captured.</summary>
    /// <param name="capture">The engine capture.</param>
    /// <returns>The suffix.</returns>
    private static string EngineLatchSuffix(EngineCapture capture)
    {
        return capture.Engine is null
            ? string.Empty
            : " latchConsistency=" + capture.Engine.HasEqScopeBlockedConsistencyReadOff + " latchQuery=" + capture.Engine.HasEqScopeBlockedQueryReadOff;
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

    /// <summary>The unrelated Horn axiom minting the bystander classes the signature reads use.</summary>
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

    /// <summary>A qualified or unqualified maximum-cardinality restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound n.</param>
    /// <param name="filler">The filler class, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Max(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), filler);
    }

    /// <summary>A complement of a class expression.</summary>
    /// <param name="operand">The complemented operand.</param>
    /// <returns>The complement.</returns>
    private static OwlObjectComplementOf Complement(OwlClassExpression operand)
    {
        return new OwlObjectComplementOf(operand);
    }

    /// <summary>A union of class expressions.</summary>
    /// <param name="operands">The union operands.</param>
    /// <returns>The union.</returns>
    private static OwlObjectUnionOf Union(params OwlClassExpression[] operands)
    {
        return new OwlObjectUnionOf([.. operands]);
    }

    /// <summary>A has-value restriction over the INVERSE of a named role — membership asserts the incoming edge from the named individual.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="individual">The edge source's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasValue HasInverseValue(string property, string individual)
    {
        return new OwlObjectHasValue(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property))), Individual(individual));
    }

    /// <summary>A told negative object-property assertion between two named individuals.</summary>
    /// <param name="property">The denied role's local name.</param>
    /// <param name="source">The source individual's local name.</param>
    /// <param name="target">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlNegativeObjectPropertyAssertionAxiom NegativeEdge(string property, string source, string target)
    {
        return new OwlNegativeObjectPropertyAssertionAxiom(Individual(source), Property(property), Individual(target)) { Origin = Origin("negativeEdge") };
    }

    /// <summary>A subclass axiom.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
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
}
