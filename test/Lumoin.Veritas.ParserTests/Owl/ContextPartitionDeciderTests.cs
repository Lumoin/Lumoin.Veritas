using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The partition-counting habitat decider's battery: the closed-form decided
/// rows on both verdict directions, the explicit dark control and its census
/// row, the eleven near-miss silences one per lock-round attack (two of them
/// replicas of real corpus lookalike shapes), the window boundary and
/// derivation pins, and the verdict-identity sweep. Every row drives the
/// production seams — the faces-carrying reasoner overload or the decider's own
/// measurement surface — and every counter the battery reads is consumed by an
/// assert.
/// </summary>
[TestClass]
internal sealed class ContextPartitionDeciderTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the battery's classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/partitioncsp#";

    /// <summary>Both partition faces lit — the selection the decided rows drive.</summary>
    private const EnumerationDeciderFaces PartitionFaces = EnumerationDeciderFaces.PartitionClash | EnumerationDeciderFaces.PartitionCertify;

    /// <summary>Every decider face the recognizer's registry lights, read from the production fold — the selection the verdict-identity sweep runs against the explicit dark control.</summary>
    private static EnumerationDeciderFaces AllFaces { get; } = ContextHabitatRecognizer.EveryFaceLit;

    /// <summary>The bounded budget the silence rows drive: enough for the engine to fire rules on a partition module, far below what its saturation would need.</summary>
    private static ReasoningBudget ProbeBudget { get; } = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 4096);

    /// <summary>
    /// The t3.x certifying template at the bound — three pairwise-disjoint
    /// anchors under a told cap of three, six existential conjuncts, one
    /// anonymous individual typed by the defined class: the closed form decides
    /// CONSISTENT with zero inference attempts and no engine. The habitat-label
    /// assert doubles as the positive reachability pin — a nominal-free module
    /// must reach the partition probe at all — and every one of the seven
    /// partition statistics fields is read.
    /// </summary>
    [TestMethod]
    public void Pd1TemplateCertifiesAtTheBound()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CertifyingTemplateModule(), PartitionFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Pd1: the certify face decides the template at the production ceiling.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Pd1: three anchors inside a cap of three are witnessed consistent.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Pd1: a pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(0, totals.ContextsCreated, "Pd1: no engine was constructed — the seat is upstream of every engine axis.");
        Assert.AreEqual(EnumerationHabitatClass.PartitionCounting, totals.EnumerationHabitat, "Pd1: a nominal-free counting module reaches the partition probe and is labelled Shape P.");
        Assert.AreEqual(3, totals.PartitionAnchorCount, "Pd1: the distinct anchors are measured.");
        Assert.AreEqual(6, totals.PartitionRestrictionCount, "Pd1: the existential conjuncts are measured.");
        Assert.AreEqual(3, totals.PartitionCapBound, "Pd1: the told cap is measured.");
        Assert.AreEqual(1, totals.PartitionDeciderCertifications, "Pd1: the certify face's counter reads the decision.");
        Assert.AreEqual(0, totals.PartitionDeciderClashes, "Pd1: the clash face did not decide this module.");
        Assert.AreEqual(0, totals.PartitionWindowExceededAnchors, "Pd1: no anchor-window silence at three anchors.");
        Assert.AreEqual(0, totals.PartitionWindowExceededRestrictions, "Pd1: no restriction-window silence at six conjuncts.");
    }

    /// <summary>The strictly-under-the-bound face: three anchors against a told cap of four are witnessed consistent pre-engine.</summary>
    [TestMethod]
    public void Pd2TemplateCertifiesUnderTheBound()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(TemplateModule(anchorCount: 3, compoundCount: 3, chainLength: 5, cap: 4), PartitionFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Pd2: the certify face decides the under-bound template.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Pd2: three anchors under a cap of four are witnessed consistent.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Pd2: the decision is pre-engine.");
        Assert.AreEqual(1, totals.PartitionDeciderCertifications, "Pd2: the certify face's counter reads the decision.");
        Assert.AreEqual(4, totals.PartitionCapBound, "Pd2: the told cap rides the decided record.");
    }

    /// <summary>
    /// The t3.x refutation template — five pairwise-disjoint anchors against a
    /// told cap of four, with the counting conjunct sitting MID-LIST so the
    /// position-independent match is pinned: the pigeonhole refutation decides
    /// INCONSISTENT pre-engine.
    /// </summary>
    [TestMethod]
    public void Pd3PigeonholeRefutesAboveTheBound()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(RefutationTemplateModule(), PartitionFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Pd3: the clash face decides the refutation template.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Pd3: five disjoint anchor obligations above a cap of four refute every model.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Pd3: the refutation is pre-engine.");
        Assert.AreEqual(1, totals.PartitionDeciderClashes, "Pd3: the clash face's counter reads the decision.");
        Assert.AreEqual(0, totals.PartitionDeciderCertifications, "Pd3: no certification on a refuted module.");
        Assert.AreEqual(5, totals.PartitionAnchorCount, "Pd3: the five distinct anchors are measured.");
        Assert.AreEqual(9, totals.PartitionRestrictionCount, "Pd3: the nine existential conjuncts are measured across the mid-list cap.");
        Assert.AreEqual(4, totals.PartitionCapBound, "Pd3: the mid-list cap is measured.");
    }

    /// <summary>The degenerate cap: a told cap of zero against a single existential requirement refutes — one anchor obligation cannot fit under no successors at all.</summary>
    [TestMethod]
    public void Pd4CapZeroRefutesWithAnyRequirement()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(TemplateModule(anchorCount: 1, compoundCount: 0, chainLength: 2, cap: 0), PartitionFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Pd4: the clash face decides the zero-cap module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Pd4: one anchor obligation under a cap of zero refutes every model.");
        Assert.AreEqual(1, totals.PartitionDeciderClashes, "Pd4: the clash face's counter reads the decision.");
        Assert.AreEqual(1, totals.PartitionAnchorCount, "Pd4: the single anchor is measured.");
        Assert.AreEqual(0, totals.PartitionCapBound, "Pd4: the zero cap is measured.");
    }

    /// <summary>
    /// The dark control: under the explicit
    /// <see cref="EnumerationDeciderFaces.None"/> selection the certifying
    /// template keeps the honest engine-face budget abstention — the abstained
    /// outcome, no verdict, the inclusive ceiling spent, and the exhaust's
    /// measured funnel profile intact.
    /// </summary>
    [TestMethod]
    public void Pd5DarkFacesKeepTheHonestAbstentionByteIdentical()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CertifyingTemplateModule(), EnumerationDeciderFaces.None, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "Pd5: the template abstains honestly with the faces dark.");
        Assert.IsNull(decision.Verdict, "Pd5: the dark abstention carries no verdict.");
        Assert.AreEqual((long)ReasoningConfiguration.Default.Budget.MaxInferences, totals.InferenceAttempts, "Pd5: the dark run spends exactly the inclusive ceiling.");
        Assert.IsGreaterThan(0L, totals.RuleApplications, "Pd5: the dark exhaust is an admitted saturation, not a non-admission.");
        Assert.IsGreaterThan(0L, totals.WorklistEnqueues, "Pd5: the dark exhaust lands genuine insertions at the funnel's head.");
    }

    /// <summary>The census ships unconditionally: on a dark abstention over the same template the habitat label and the three measured numbers are already on the record, with both decision counters still zero.</summary>
    [TestMethod]
    public void Pd6CensusRidesTheDarkAbstentionRecordsAlways()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CertifyingTemplateModule(), EnumerationDeciderFaces.None, ProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "Pd6: the template stays abstained dark — the census never moves a decision.");
        Assert.AreEqual(EnumerationHabitatClass.PartitionCounting, totals.EnumerationHabitat, "Pd6: the habitat label rides the dark abstention record.");
        Assert.AreEqual(3, totals.PartitionAnchorCount, "Pd6: the anchors are measured dark.");
        Assert.AreEqual(6, totals.PartitionRestrictionCount, "Pd6: the existential conjuncts are measured dark.");
        Assert.AreEqual(3, totals.PartitionCapBound, "Pd6: the told cap is measured dark.");
        Assert.AreEqual(0, totals.PartitionDeciderClashes, "Pd6: no clash decision with the faces dark.");
        Assert.AreEqual(0, totals.PartitionDeciderCertifications, "Pd6: no certification with the faces dark.");
    }

    /// <summary>
    /// The near-miss silences: eleven perturbations of the template, each of
    /// which must leave the faces silent — nine synthetic attacks one per named
    /// hazard, plus two replicas of real corpus lookalike shapes (the
    /// global-cap family, whose counting restriction is a separate whole-domain
    /// axiom rather than a sibling conjunct, and the multi-property family,
    /// whose existential conjuncts run over two distinct roles). The faces are
    /// read directly and through the reasoner: neither decision counter may
    /// move on any row.
    /// </summary>
    [TestMethod]
    public void Pd7NearMissSilences()
    {
        foreach((string name, ReasoningModule module) in NearMissRows())
        {
            Assert.IsNull(ContextPartitionCountingDecider.Run(module).Consistent, "Pd7 " + name + ": the face must stay silent on the near miss.");

            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, PartitionFaces, ProbeBudget, TestContext.CancellationToken);
            ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
            Assert.AreEqual(0, totals.PartitionDeciderClashes, "Pd7 " + name + ": no clash decision on the near miss.");
            Assert.AreEqual(0, totals.PartitionDeciderCertifications, "Pd7 " + name + ": no certification on the near miss.");
        }
    }

    /// <summary>
    /// The window silences charge their named counters, with the measured
    /// numbers landing BEFORE the boundary comparison: seventeen anchors charge
    /// the anchor counter, and seventeen existential conjuncts over three
    /// anchors charge the restriction counter alone. Both rows are read on the
    /// decider's own measurement surface and on the reasoner's statistics
    /// record.
    /// </summary>
    [TestMethod]
    public void Pd8WindowSilencesChargeTheirNamedCounters()
    {
        int anchorOverflow = ContextPartitionCountingDecider.PartitionAnchorBound + 1;
        ReasoningModule anchorModule = TemplateModule(anchorCount: anchorOverflow, compoundCount: 0, chainLength: anchorOverflow, cap: anchorOverflow + 1);
        PartitionCountingOutcome anchorOutcome = ContextPartitionCountingDecider.Run(anchorModule);

        Assert.IsNull(anchorOutcome.Consistent, "Pd8: the face is silent past the anchor bound.");
        Assert.AreEqual(anchorOverflow, anchorOutcome.Window.AnchorCount, "Pd8: the measured anchors are reported past the bound.");
        Assert.AreEqual(anchorOverflow + 1, anchorOutcome.Window.CapBound, "Pd8: the told cap is reported past the bound.");
        Assert.AreEqual(1, anchorOutcome.Window.AnchorSilences, "Pd8: the silence is charged to the anchor counter.");

        ModuleDecision anchorDecision = ContextSaturationModuleReasoner.DecideModule(anchorModule, PartitionFaces, ProbeBudget, TestContext.CancellationToken);
        Assert.AreEqual(1, anchorDecision.Statistics.ContextTotals.PartitionWindowExceededAnchors, "Pd8: the anchor-window silence rides the statistics record.");
        Assert.AreEqual(anchorOverflow, anchorDecision.Statistics.ContextTotals.PartitionAnchorCount, "Pd8: the measured anchors ride the statistics record.");
        Assert.AreEqual(0, anchorDecision.Statistics.ContextTotals.PartitionDeciderCertifications, "Pd8: no certification past the anchor bound.");
        Assert.AreEqual(0, anchorDecision.Statistics.ContextTotals.PartitionDeciderClashes, "Pd8: no clash past the anchor bound.");

        int restrictionOverflow = ContextPartitionCountingDecider.PartitionRestrictionBound + 1;
        ReasoningModule restrictionModule = TemplateModule(anchorCount: 3, compoundCount: restrictionOverflow - 3, chainLength: 3, cap: 3);
        PartitionCountingOutcome restrictionOutcome = ContextPartitionCountingDecider.Run(restrictionModule);

        Assert.IsNull(restrictionOutcome.Consistent, "Pd8: the face is silent past the restriction bound.");
        Assert.AreEqual(restrictionOverflow, restrictionOutcome.Window.RestrictionCount, "Pd8: the measured conjuncts are reported past the bound.");
        Assert.AreEqual(3, restrictionOutcome.Window.AnchorCount, "Pd8: the anchors are measured inside their own bound.");
        Assert.AreEqual(1, restrictionOutcome.Window.RestrictionSilences, "Pd8: the silence is charged to the restriction counter.");
        Assert.AreEqual(0, restrictionOutcome.Window.AnchorSilences, "Pd8: the anchor counter stays uncharged.");

        ModuleDecision restrictionDecision = ContextSaturationModuleReasoner.DecideModule(restrictionModule, PartitionFaces, ProbeBudget, TestContext.CancellationToken);
        Assert.AreEqual(1, restrictionDecision.Statistics.ContextTotals.PartitionWindowExceededRestrictions, "Pd8: the restriction-window silence rides the statistics record.");
        Assert.AreEqual(restrictionOverflow, restrictionDecision.Statistics.ContextTotals.PartitionRestrictionCount, "Pd8: the measured conjuncts ride the statistics record.");
        Assert.AreEqual(0, restrictionDecision.Statistics.ContextTotals.PartitionDeciderCertifications, "Pd8: no certification past the restriction bound.");
    }

    /// <summary>
    /// The window-constant derivation pins, expressed through measured values:
    /// a template sitting exactly at the anchor bound still certifies, its
    /// measured window numbers land on the counting faces' shared sixteen
    /// boundary discipline (the counted-population and ground-clique ceilings
    /// on the anchor side, the funnel-chain hop ceiling on the restriction
    /// side), and the anchor bound's stated cost formula, C(16,2) = 120 pair
    /// comparisons, is enumerated exactly.
    /// </summary>
    [TestMethod]
    public void Pd9WindowConstantDerivations()
    {
        int atBound = ContextPartitionCountingDecider.PartitionAnchorBound;
        ReasoningModule module = TemplateModule(anchorCount: atBound, compoundCount: 0, chainLength: atBound, cap: atBound);
        PartitionCountingOutcome outcome = ContextPartitionCountingDecider.Run(module);

        Assert.IsTrue(outcome.Consistent, "Pd9: the certify face decides AT the anchor bound — sixteen anchors inside a told cap of sixteen.");
        Assert.AreEqual(ContextNominalCountingDecider.CountedPopulationBound, outcome.Window.AnchorCount, "Pd9: the measured anchor ceiling shares the counted-population bound — one boundary discipline across the counting faces.");
        Assert.AreEqual(ContextClausifier.GroundCountingCliqueBound, outcome.Window.AnchorCount, "Pd9: the measured anchor ceiling shares the ground rider's clique bound.");
        Assert.AreEqual(ContextNominalCountingDecider.FunnelChainHopBound, outcome.Window.RestrictionCount, "Pd9: the measured conjunct ceiling shares the funnel-chain hop bound — the empirical corpus-maximum-with-margin discipline.");
        Assert.AreEqual(0, outcome.Window.AnchorSilences, "Pd9: no anchor-window silence exactly at the bound.");
        Assert.AreEqual(0, outcome.Window.RestrictionSilences, "Pd9: no restriction-window silence exactly at the bound.");

        using VeritasMemoryPool<int> pool = new();
        long pairs = 0;
        using CombinationIndexEnumerator sweep = CombinationIndexEnumerator.Create(pool, ContextPartitionCountingDecider.PartitionAnchorBound, 2);
        while(sweep.MoveNext())
        {
            pairs++;
        }

        Assert.AreEqual(120L, pairs, "Pd9: C(16,2) = 120 anchor-pair comparisons at the bound — the documented cost formula, enumerated.");
    }

    /// <summary>
    /// The verdict-identity sweep: every certified nominal-battery row decided
    /// under the explicit dark control and under every lit face, across both
    /// paramodulation scopes and both root-tier topologies, must be identical
    /// in outcome, verdict, subsumption set, and attempt count — the new probe
    /// moves no existing classification and no existing verdict. The four
    /// partition rows ride the same matrix: the lit run decides pre-engine with
    /// zero attempts in every cell, and where the dark run reached a verdict of
    /// its own the two agree.
    /// </summary>
    [TestMethod]
    public void Pd10LitFacesMoveNoCertifiedVerdictAcrossTheMatrix()
    {
        (NominalParamodulationScope Scope, RootContextTopology Topology)[] cells =
        [
            (NominalParamodulationScope.QueryScoped, RootContextTopology.SingleRoot),
            (NominalParamodulationScope.QueryScoped, RootContextTopology.PerIndividualRoots),
            (NominalParamodulationScope.Unrestricted, RootContextTopology.SingleRoot),
            (NominalParamodulationScope.Unrestricted, RootContextTopology.PerIndividualRoots),
        ];
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool _, string[] _) in ContextNominalBatteryTests.BatteryRows())
        {
            foreach((NominalParamodulationScope scope, RootContextTopology topology) in cells)
            {
                string cell = name + "@" + scope + "/" + topology;
                ModuleDecision dark = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, scope, topology, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
                ModuleDecision lit = ContextSaturationModuleReasoner.DecideModule(module, AllFaces, scope, topology, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
                ContextSaturationStatistics litTotals = lit.Statistics.ContextTotals;
                if(litTotals.PartitionDeciderClashes + litTotals.PartitionDeciderCertifications > 0)
                {
                    mismatches.Add(cell + ": a nominal-battery row was claimed by a partition face.");
                    continue;
                }

                if(lit.Outcome != dark.Outcome)
                {
                    mismatches.Add(cell + ": outcome moved " + dark.Outcome + " -> " + lit.Outcome + ".");
                    continue;
                }

                if(lit.Verdict is null != dark.Verdict is null || (lit.Verdict is not null && lit.Verdict.IsConsistent != dark.Verdict!.IsConsistent))
                {
                    mismatches.Add(cell + ": the verdict moved under the lit faces.");
                    continue;
                }

                if(!KeySetsEqual(SubsumptionKeySet(dark.Verdict), SubsumptionKeySet(lit.Verdict)))
                {
                    mismatches.Add(cell + ": the exact subsumption set moved under the lit faces.");
                    continue;
                }

                bool preEngine = litTotals.EnumerationDeciderClashes + litTotals.EnumerationDeciderCertifications + litTotals.EnumerationDeciderRefutations > 0;
                if(!preEngine && litTotals.InferenceAttempts != dark.Statistics.ContextTotals.InferenceAttempts)
                {
                    mismatches.Add(cell + ": a silent-face run moved the attempt count (" + dark.Statistics.ContextTotals.InferenceAttempts + " -> " + litTotals.InferenceAttempts + ").");
                }
            }
        }

        int partitionDecided = 0;
        foreach((string name, ReasoningModule module, bool consistent) in PartitionRows())
        {
            foreach((NominalParamodulationScope scope, RootContextTopology topology) in cells)
            {
                string cell = name + "@" + scope + "/" + topology;
                ModuleDecision dark = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, scope, topology, ProbeBudget, TestContext.CancellationToken);
                ModuleDecision lit = ContextSaturationModuleReasoner.DecideModule(module, AllFaces, scope, topology, ProbeBudget, TestContext.CancellationToken);
                if(lit.Outcome != ReasoningDecisionOutcome.Decided || lit.Verdict is null || lit.Verdict.IsConsistent != consistent)
                {
                    mismatches.Add(cell + ": the lit partition faces did not decide the row's certified verdict.");
                    continue;
                }

                partitionDecided++;
                if(lit.Statistics.ContextTotals.InferenceAttempts != 0L)
                {
                    mismatches.Add(cell + ": a partition-decided run spent engine attempts (" + lit.Statistics.ContextTotals.InferenceAttempts + ").");
                }

                if(dark.Verdict is ModuleVerdict darkVerdict && darkVerdict.IsConsistent != consistent)
                {
                    mismatches.Add(cell + ": the dark run's own verdict disagrees with the closed form.");
                }
            }
        }

        TestContext.WriteLine("Pd10 verdict-identity sweep: " + partitionDecided + " partition cells decided pre-engine, zero certified movement.");
        Assert.IsGreaterThan(0, partitionDecided, "Pd10: the lit faces decide at least one partition cell pre-engine — the sweep instruments a lit decider.");
        Assert.IsEmpty(mismatches, string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>The four decided partition rows with their certified verdicts — the sweep's lit-face fixtures, shared with the sibling decider batteries whose verdict-identity sweeps must prove their own faces claim none of them.</summary>
    /// <returns>The rows.</returns>
    internal static (string Name, ReasoningModule Module, bool Consistent)[] PartitionRows()
    {
        return
        [
            ("Pd1", CertifyingTemplateModule(), true),
            ("Pd2", TemplateModule(anchorCount: 3, compoundCount: 3, chainLength: 5, cap: 4), true),
            ("Pd3", RefutationTemplateModule(), false),
            ("Pd4", TemplateModule(anchorCount: 1, compoundCount: 0, chainLength: 2, cap: 0), false),
        ];
    }

    /// <summary>
    /// The eleven near-miss modules, one per named hazard: a dual-anchor and an
    /// anchor-free filler, a second and a qualified counting conjunct, a broken
    /// disjointness chain, a role characteristic, an extra ABox assertion, the
    /// defined class reused in a second axiom, a free class carrying an axiom,
    /// and the two corpus lookalike replicas.
    /// </summary>
    /// <returns>The rows.</returns>
    private static (string Name, ReasoningModule Module)[] NearMissRows()
    {
        return
        [
            ("DualAnchorFiller", Module(
                Equivalent(Class("Defined"), Intersection(Some("r", Class("p1")), Some("r", Class("p2")), Some("r", Intersection(Class("p1"), Class("p2"))), Max("r", 3, null))),
                SubClassOf(Class("p1"), Complement(Class("p2"))),
                ClassAssertion(Class("Defined"), Anonymous("w")))),

            ("AnchorFreeFiller", Module(
                Equivalent(Class("Defined"), Intersection(Some("r", Class("p1")), Some("r", Class("p2")), Some("r", Class("free")), Max("r", 3, null))),
                SubClassOf(Class("p1"), Complement(Class("p2"))),
                ClassAssertion(Class("Defined"), Anonymous("w")))),

            ("SecondCountingConjunct", Module(
                Equivalent(Class("Defined"), Intersection(Some("r", Class("p1")), Some("r", Class("p2")), Max("r", 2, null), Max("r", 3, null))),
                SubClassOf(Class("p1"), Complement(Class("p2"))),
                ClassAssertion(Class("Defined"), Anonymous("w")))),

            ("QualifiedCountingConjunct", Module(
                Equivalent(Class("Defined"), Intersection(Some("r", Class("p1")), Some("r", Class("p2")), Max("r", 2, Class("q")))),
                SubClassOf(Class("p1"), Complement(Class("p2"))),
                ClassAssertion(Class("Defined"), Anonymous("w")))),

            ("BrokenDisjointnessChain", Module(
                Equivalent(Class("Defined"), Intersection(Some("r", Class("p1")), Some("r", Class("p2")), Max("r", 1, null))),
                SubClassOf(Class("p1"), Complement(Class("p3"))),
                SubClassOf(Class("p2"), Complement(Class("p3"))),
                ClassAssertion(Class("Defined"), Anonymous("w")))),

            ("RoleCharacteristic", Module(
                Equivalent(Class("Defined"), Intersection(Some("r", Class("p1")), Some("r", Class("p2")), Max("r", 2, null))),
                SubClassOf(Class("p1"), Complement(Class("p2"))),
                Functional("r"),
                ClassAssertion(Class("Defined"), Anonymous("w")))),

            ("ExtraAboxAssertion", Module(
                Equivalent(Class("Defined"), Intersection(Some("r", Class("p1")), Some("r", Class("p2")), Max("r", 2, null))),
                SubClassOf(Class("p1"), Complement(Class("p2"))),
                ClassAssertion(Class("Defined"), Individual("x")),
                Edge("r", "x", "y"))),

            ("DefinedClassReused", Module(
                Equivalent(Class("Defined"), Intersection(Some("r", Class("p1")), Some("r", Class("p2")), Max("r", 2, null))),
                SubClassOf(Class("p1"), Complement(Class("p2"))),
                SubClassOf(Class("Defined"), Complement(Class("p2"))),
                ClassAssertion(Class("Defined"), Anonymous("w")))),

            ("FreeClassCarriesAnAxiom", Module(
                Equivalent(Class("Defined"), Intersection(Some("r", Class("p1")), Some("r", Intersection(Class("p2"), Class("p"))), Max("r", 2, null))),
                SubClassOf(Class("p1"), Complement(Class("p2"))),
                SubClassOf(Class("p"), Class("q")),
                ClassAssertion(Class("Defined"), Anonymous("w")))),

            ("GlobalCapLookalike", Module(
                Equivalent(Class("Defined"), Intersection(Some("r", Class("p1")), Some("r", Class("p2")))),
                SubClassOf(Thing, Max("r", 2, null)),
                SubClassOf(Class("p1"), Complement(Class("p2"))),
                ClassAssertion(Class("Defined"), Anonymous("w")))),

            ("MultiPropertyLookalike", Module(
                Equivalent(Class("Defined"), Intersection(Some("r", Class("p1")), Some("s", Class("p2")), Max("r", 2, null))),
                SubClassOf(Class("p1"), Complement(Class("p2"))),
                ClassAssertion(Class("Defined"), Anonymous("w")))),
        ];
    }

    /// <summary>The t3.x certifying template replica: three anchors drawn from a five-class disjointness chain, three compound fillers pairing each anchor with the free class, and a told cap of three.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule CertifyingTemplateModule()
    {
        return TemplateModule(anchorCount: 3, compoundCount: 3, chainLength: 5, cap: 3);
    }

    /// <summary>The t3.x refutation template replica: five anchors drawn from a five-class disjointness chain, four compound fillers, and a told cap of four with the counting conjunct MID-LIST — a trailing existential follows it, so the position-independent match is exercised.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule RefutationTemplateModule()
    {
        return Module(
            Equivalent(Class("Defined"), Intersection(
                Some("r", Class("p1")),
                Some("r", Class("p2")),
                Some("r", Class("p3")),
                Some("r", Class("p4")),
                Some("r", Intersection(Class("p1"), Class("p"))),
                Some("r", Intersection(Class("p2"), Class("p"))),
                Some("r", Intersection(Class("p3"), Class("p"))),
                Some("r", Intersection(Class("p4"), Class("p"))),
                Max("r", 4, null),
                Some("r", Class("p5")))),
            SubClassOf(Class("p1"), Complement(Union(Class("p2"), Class("p3"), Class("p4"), Class("p5")))),
            SubClassOf(Class("p2"), Complement(Union(Class("p3"), Class("p4"), Class("p5")))),
            SubClassOf(Class("p3"), Complement(Union(Class("p4"), Class("p5")))),
            SubClassOf(Class("p4"), Complement(Class("p5"))),
            ClassAssertion(Class("Defined"), Anonymous("w")));
    }

    /// <summary>
    /// Builds a partition-counting template module: a defined named class
    /// equivalent to the intersection of one existential restriction per anchor,
    /// <paramref name="compoundCount"/> compound existential restrictions
    /// pairing an anchor with the free class, and one trailing unqualified
    /// max-cardinality restriction, every conjunct over the same named role;
    /// beside it the descending disjointness chain over
    /// <paramref name="chainLength"/> classes and one anonymous individual typed
    /// by the defined class.
    /// </summary>
    /// <param name="anchorCount">The plain existential restrictions, one per anchor class.</param>
    /// <param name="compoundCount">The compound existential restrictions, cycling over the anchors.</param>
    /// <param name="chainLength">The disjointness chain's class count; at least <paramref name="anchorCount"/>.</param>
    /// <param name="cap">The told max-cardinality bound.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule TemplateModule(int anchorCount, int compoundCount, int chainLength, int cap)
    {
        List<OwlClassExpression> conjuncts = [];
        for(int i = 1; i <= anchorCount; i++)
        {
            conjuncts.Add(Some("r", Class("p" + i)));
        }

        for(int i = 0; i < compoundCount; i++)
        {
            conjuncts.Add(Some("r", Intersection(Class("p" + ((i % anchorCount) + 1)), Class("p"))));
        }

        conjuncts.Add(Max("r", cap, null));

        List<OwlAxiom> axioms = [Equivalent(Class("Defined"), new OwlObjectIntersectionOf([.. conjuncts]))];
        for(int link = 1; link < chainLength; link++)
        {
            axioms.Add(SubClassOf(Class("p" + link), Complement(ChainTail(link + 1, chainLength))));
        }

        axioms.Add(ClassAssertion(Class("Defined"), Anonymous("w")));

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The descending chain link's complemented operand: the single remaining class, or the union of the classes from <paramref name="first"/> to <paramref name="last"/>.</summary>
    /// <param name="first">The first class index in the tail.</param>
    /// <param name="last">The last class index in the chain.</param>
    /// <returns>The complemented operand.</returns>
    private static OwlClassExpression ChainTail(int first, int last)
    {
        if(first >= last)
        {
            return Class("p" + last);
        }

        List<OwlClassExpression> members = [];
        for(int i = first; i <= last; i++)
        {
            members.Add(Class("p" + i));
        }

        return new OwlObjectUnionOf([.. members]);
    }

    /// <summary>The verdict's sorted subsumption key set, empty for an absent verdict.</summary>
    /// <param name="verdict">The verdict, or <see langword="null"/>.</param>
    /// <returns>The sorted keys.</returns>
    private static List<string> SubsumptionKeySet(ModuleVerdict? verdict)
    {
        List<string> keys = [];
        if(verdict is null)
        {
            return keys;
        }

        foreach((NamedNode subClass, NamedNode superClass) in verdict.Subsumptions)
        {
            keys.Add(subClass.Iri.ToString() + "->" + superClass.Iri.ToString());
        }

        keys.Sort(StringComparer.Ordinal);

        return keys;
    }

    /// <summary>Whether two sorted key lists are element-wise equal.</summary>
    /// <param name="first">The first sorted list.</param>
    /// <param name="second">The second sorted list.</param>
    /// <returns><see langword="true"/> on equality.</returns>
    private static bool KeySetsEqual(List<string> first, List<string> second)
    {
        if(first.Count != second.Count)
        {
            return false;
        }

        for(int index = 0; index < first.Count; index++)
        {
            if(!string.Equals(first[index], second[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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

    /// <summary>The <c>owl:Thing</c> reference — the global-cap lookalike's subclass position.</summary>
    private static OwlClassReference Thing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing")));

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

    /// <summary>An anonymous individual — the template's ABox subject shape.</summary>
    /// <param name="label">The blank node's label.</param>
    /// <returns>The blank node.</returns>
    private static BlankNode Anonymous(string label)
    {
        return new BlankNode(Utf8Strings.From(label));
    }

    /// <summary>An intersection of class expressions.</summary>
    /// <param name="operands">The intersection operands.</param>
    /// <returns>The intersection.</returns>
    private static OwlObjectIntersectionOf Intersection(params OwlClassExpression[] operands)
    {
        return new OwlObjectIntersectionOf([.. operands]);
    }

    /// <summary>A union of class expressions.</summary>
    /// <param name="operands">The union operands.</param>
    /// <returns>The union.</returns>
    private static OwlObjectUnionOf Union(params OwlClassExpression[] operands)
    {
        return new OwlObjectUnionOf([.. operands]);
    }

    /// <summary>A complement of a class expression.</summary>
    /// <param name="operand">The complemented operand.</param>
    /// <returns>The complement.</returns>
    private static OwlObjectComplementOf Complement(OwlClassExpression operand)
    {
        return new OwlObjectComplementOf(operand);
    }

    /// <summary>An existential restriction over a named forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>A qualified or unqualified maximum-cardinality restriction over a named forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound k.</param>
    /// <param name="filler">The filler class, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Max(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), filler);
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

    /// <summary>A functionality characteristic over a named role in the example namespace.</summary>
    /// <param name="role">The role's local name.</param>
    /// <returns>The characteristic axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Functional(string role)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, Property(role)) { Origin = Origin("functional") };
    }
}
