using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The nominal-pinned-role habitat decider's battery: the closed-form refuting
/// rows across every told route the jurisdiction admits — the corpus shape's
/// one-hop equivalence, the inline zero-hop range, the subclass hop, the
/// unique-name collision, and the single-member pin — the premise silence that
/// hands the module back to saturation, the pinned-loop denial the engine
/// owns, the near-miss silences one per named hazard with the four
/// per-position inverse-spelling rows among them, the member-window silence,
/// the ground-form denial's standing regression, the explicit dark control and
/// its census ride, the cross-sibling verdict sweep, the window derivation,
/// and the statistics assembly. Every row drives the production seams — the
/// faces-carrying reasoner overload or the decider's own measurement surface —
/// and every counter the battery reads is consumed by an assert.
/// </summary>
[TestClass]
internal sealed class ContextNominalPinnedRoleDeciderTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the battery's classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/nominalpinnedrole#";

    /// <summary>The nominal-pinned-role clash face lit — the selection the refuting rows drive. The face has no certify counterpart.</summary>
    private const EnumerationDeciderFaces NominalPinnedRoleFaces = EnumerationDeciderFaces.NominalPinnedRoleClash;

    /// <summary>The bounded budget the silence rows drive: enough for the engine to fire rules on a diagonal-pinned module, far below what the probe-bearing equivalence machinery's saturation would need.</summary>
    private static ReasoningBudget ProbeBudget { get; } = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 4096);

    /// <summary>
    /// The corpus shape told exactly as the target case spells it: the
    /// inverse-functional characteristic and the range over one plain role, the
    /// range's named class resolved to the two-member one-of through the ONE
    /// told equivalence hop, the two told self-loops, the three Thing
    /// assertions, and the skolemized arm's told edge beside the concept denial
    /// of its exact reverse. The diagonal is pinned and the denied reverse is
    /// present in every model: the closed form refutes with zero inference
    /// attempts and no engine, exactly where the engine's own route through the
    /// equivalence's clause pair is a measured paramodulation-cycle wall. Every
    /// one of the five nominal-pinned-role statistics fields is read.
    /// </summary>
    [TestMethod]
    public void Dp1CorpusShapeOneHopEquivalenceRefutes()
    {
        ReasoningModule module = CorpusShapeModule();
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, NominalPinnedRoleFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Dp1 CorpusShape: the clash face decides the corpus shape at the production ceiling.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Dp1 CorpusShape: the pinned diagonal holds the denied reverse edge in every model, so no model exists.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Dp1 CorpusShape: a pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(0, totals.ContextsCreated, "Dp1 CorpusShape: no engine was constructed — the seat is upstream of every engine axis.");
        Assert.AreEqual(EnumerationHabitatClass.NominalPinnedRole, totals.EnumerationHabitat, "Dp1 CorpusShape: the corpus shape reaches the tail probe and is labelled Shape D.");
        Assert.AreEqual(1, totals.NominalPinnedRoleDeciderClashes, "Dp1 CorpusShape: the clash face's counter reads the decision.");
        Assert.AreEqual(2, totals.NominalPinnedRoleMemberCount, "Dp1 CorpusShape: the two-member range resolution is measured.");
        Assert.AreEqual(2, totals.NominalPinnedRolePinnedEdgeCount, "Dp1 CorpusShape: both told self-loops were consumed by the pinning.");
        Assert.AreEqual(1, totals.NominalPinnedRoleDeniedEdgeCount, "Dp1 CorpusShape: the one concept-form denial is measured.");
        Assert.AreEqual(0, totals.NominalPinnedRoleWindowExceededMembers, "Dp1 CorpusShape: no member-window silence at two members.");
        Assert.AreEqual("NominalPinnedRoleDeniedDiagonalEdge(" + Example + "r)", ContextNominalPinnedRoleDecider.Run(module).ClashReason, "Dp1 CorpusShape: the clash reason names the pinned role.");
    }

    /// <summary>The inline zero-hop route: the range carries its one-of directly, no named class and no hop anywhere, so the resolution is immediate and the same denied diagonal edge refutes pre-engine.</summary>
    [TestMethod]
    public void Dp2InlineRangeZeroHopRefutes()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(InlineRangeModule(), NominalPinnedRoleFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Dp2 InlineRange: the clash face decides the zero-hop route.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Dp2 InlineRange: the inline one-of pins the same diagonal, so no model exists.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Dp2 InlineRange: the refutation is pre-engine.");
        Assert.AreEqual(1, totals.NominalPinnedRoleDeciderClashes, "Dp2 InlineRange: the clash face's counter reads the decision.");
        Assert.AreEqual(2, totals.NominalPinnedRoleMemberCount, "Dp2 InlineRange: the inline two-member resolution is measured.");
    }

    /// <summary>The subclass hop route: the range's named class is bounded by the one-of through a told subclass axiom with the class in SUBCLASS position — the class-to-one-of direction a subclass axiom legitimately supplies — and the same denied diagonal edge refutes pre-engine.</summary>
    [TestMethod]
    public void Dp3SubClassHopRefutes()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(SubClassHopModule(), NominalPinnedRoleFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Dp3 SubClassHop: the clash face decides the subclass-hop route.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Dp3 SubClassHop: the subclass hop bounds the range from above, so the diagonal is pinned and no model exists.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Dp3 SubClassHop: the refutation is pre-engine.");
        Assert.AreEqual(1, totals.NominalPinnedRoleDeciderClashes, "Dp3 SubClassHop: the clash face's counter reads the decision.");
    }

    /// <summary>
    /// The premise alone: the pinned diagonal without any probe. The face is
    /// SILENT — its only told edges ARE the pinned self-loops, so no clash edge
    /// exists — and ordinary saturation owns the module and decides it
    /// consistent. The census still ships: the habitat label and the measured
    /// window ride the engine's decision record while the clash counter stays
    /// at zero.
    /// </summary>
    [TestMethod]
    public void Dp4PremiseAloneStaysSilent()
    {
        ReasoningModule module = PremiseModule();
        NominalPinnedRoleOutcome ran = ContextNominalPinnedRoleDecider.Run(module);

        Assert.IsNull(ran.Consistent, "Dp4 PremiseAlone: the premise's only told edges are the pinned loops, so the face is silent.");
        Assert.IsNull(ran.ClashReason, "Dp4 PremiseAlone: a silent face names no clash reason.");
        Assert.AreEqual(2, ran.Window.MemberCount, "Dp4 PremiseAlone: the two-member resolution is measured on the silence.");
        Assert.AreEqual(2, ran.Window.PinnedEdgeCount, "Dp4 PremiseAlone: the consumed self-loops are measured on the silence.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, NominalPinnedRoleFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Dp4 PremiseAlone: saturation owns and decides the premise the face declined.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Dp4 PremiseAlone: the pinned premise is satisfiable — the diagonal itself is its model.");
        Assert.IsGreaterThan(0L, totals.InferenceAttempts, "Dp4 PremiseAlone: the engine's decision spent attempts — the face decided nothing.");
        Assert.AreEqual(0, totals.NominalPinnedRoleDeciderClashes, "Dp4 PremiseAlone: no clash decision on the premise.");
        Assert.AreEqual(EnumerationHabitatClass.NominalPinnedRole, totals.EnumerationHabitat, "Dp4 PremiseAlone: the census label rides the silent face.");
    }

    /// <summary>The unique-name collision rows: a told <c>SameIndividual</c> over the two members still clashes — the pinned set only shrinks under collision, the lemma using no unique-name assumption — and a duplicate member spelling dedupes to two seats and still clashes.</summary>
    [TestMethod]
    public void Dp5SameIndividualCollisionStillRefutes()
    {
        List<OwlAxiom> collided = [.. CorpusShapeModule().Axioms];
        collided.Add(new OwlSameIndividualAxiom(Individual("a"), Individual("b")) { Origin = Origin("same") });
        ModuleDecision collidedDecision = ContextSaturationModuleReasoner.DecideModule(new ReasoningModule([.. collided], Violations: []), NominalPinnedRoleFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, collidedDecision.Outcome, "Dp5 SameIndividualCollision: the clash face decides the collided module.");
        Assert.IsFalse(collidedDecision.Verdict!.IsConsistent, "Dp5 SameIndividualCollision: collapsing the members only shrinks the pinned set — the refutation stands without any unique-name assumption.");
        Assert.AreEqual(0L, collidedDecision.Statistics.ContextTotals.InferenceAttempts, "Dp5 SameIndividualCollision: the refutation is pre-engine.");

        ModuleDecision dedupedDecision = ContextSaturationModuleReasoner.DecideModule(DuplicateMemberModule(), NominalPinnedRoleFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics dedupedTotals = dedupedDecision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, dedupedDecision.Outcome, "Dp5 DuplicateMember: the clash face decides the duplicate-spelling module.");
        Assert.IsFalse(dedupedDecision.Verdict!.IsConsistent, "Dp5 DuplicateMember: a repeated name is one seat, so the diagonal is pinned and no model exists.");
        Assert.AreEqual(2, dedupedTotals.NominalPinnedRoleMemberCount, "Dp5 DuplicateMember: the duplicate spelling dedupes to two seats.");
    }

    /// <summary>
    /// The near-miss sub-checks: fourteen perturbations of the refuting shapes,
    /// each of which must leave the face SILENT — one told self-loop dropped and
    /// all of them dropped (the totality guard between right and wrong), the
    /// reversed subclass hop and the two-hop chain (the resolution direction
    /// and depth guards), the missing and the symmetric characteristic, the
    /// domain axiom read as no range, the characteristic and the range on
    /// different roles, the four inverse-spelling rows one per role position —
    /// the characteristic, the range, the self-loops told on a told inverse
    /// partner, and the denial's has-value — an anonymous one-of member, and
    /// the data-side characteristic lookalike. Every row is built so that a
    /// leak in the named guard would REFUTE it, so silence is the whole
    /// assertion. The face is read directly and through the reasoner: the
    /// clash counter may not move on any row.
    /// </summary>
    [TestMethod]
    public void Dp6NearMissRowsStaySilent()
    {
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module) in NearMissRows())
        {
            NominalPinnedRoleOutcome outcome = ContextNominalPinnedRoleDecider.Run(module);
            if(outcome.Consistent is not null)
            {
                mismatches.Add("Dp6 " + name + ": the face must stay silent on the near miss.");
                continue;
            }

            if(outcome.ClashReason is not null)
            {
                mismatches.Add("Dp6 " + name + ": a silent face names no clash reason.");
                continue;
            }

            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, NominalPinnedRoleFaces, ProbeBudget, TestContext.CancellationToken);
            if(decision.Statistics.ContextTotals.NominalPinnedRoleDeciderClashes != 0)
            {
                mismatches.Add("Dp6 " + name + ": no clash decision may land on the near miss.");
            }
        }

        Assert.IsEmpty(mismatches, string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>
    /// The denial standing on a pinned self-loop: the probe denies the reverse
    /// of the told loop itself, so the module is an ordinary told contradiction.
    /// The face is SILENT — the only told edges are the pinned loops, which the
    /// clash scan excludes — and the ENGINE decides the module inconsistent, so
    /// the split between the face's five-step habitat and the engine's
    /// told-contradiction reach stays exact.
    /// </summary>
    [TestMethod]
    public void Dp7DenialOnPinnedLoopStaysSilent()
    {
        ReasoningModule module = PinnedLoopDenialModule();
        NominalPinnedRoleOutcome ran = ContextNominalPinnedRoleDecider.Run(module);

        Assert.IsNull(ran.Consistent, "Dp7 PinnedLoopDenial: a denial standing on a pinned self-loop is not this face's clash.");
        Assert.IsNull(ran.ClashReason, "Dp7 PinnedLoopDenial: a silent face names no clash reason.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, NominalPinnedRoleFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Dp7 PinnedLoopDenial: the engine decides the told contradiction.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Dp7 PinnedLoopDenial: the told loop stands beside its own reverse's denial — inconsistent.");
        Assert.IsGreaterThan(0L, totals.InferenceAttempts, "Dp7 PinnedLoopDenial: the engine's decision spent attempts — the face decided nothing.");
        Assert.AreEqual(0, totals.NominalPinnedRoleDeciderClashes, "Dp7 PinnedLoopDenial: the face's counter stays at zero while the arm still decides.");
    }

    /// <summary>The member-window silence charges its named counter, with the measured count landing BEFORE the boundary comparison: a seventeen-member resolution is SKIPPED, so its totality is never scanned and no clash survives to compare — everything else about the module is recognizable.</summary>
    [TestMethod]
    public void Dp8SeventeenMembersChargeTheMemberWindowSilence()
    {
        int overflow = ContextNominalPinnedRoleDecider.NominalPinnedRoleMemberBound + 1;
        ReasoningModule module = MemberWindowModule(overflow);
        NominalPinnedRoleOutcome outcome = ContextNominalPinnedRoleDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Dp8 MemberWindow: the face is silent past the member bound.");
        Assert.AreEqual(overflow, outcome.Window.MemberCount, "Dp8 MemberWindow: the measured member count is reported past the bound.");
        Assert.AreEqual(1, outcome.Window.MemberSilences, "Dp8 MemberWindow: the silence is charged to the member counter.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, NominalPinnedRoleFaces, ProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(1, totals.NominalPinnedRoleWindowExceededMembers, "Dp8 MemberWindow: the window silence rides the statistics record.");
        Assert.AreEqual(overflow, totals.NominalPinnedRoleMemberCount, "Dp8 MemberWindow: the measured members ride the statistics record.");
        Assert.AreEqual(0, totals.NominalPinnedRoleDeciderClashes, "Dp8 MemberWindow: no clash past the member bound.");
    }

    /// <summary>The lemma's smallest instance: a single-member range, its one self-loop, and a reverse-denied fresh edge. Every edge collapses onto the one member, so the denied reverse is the loop itself and the closed form refutes.</summary>
    [TestMethod]
    public void Dp9SingleMemberPinRefutes()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(SingleMemberModule(), NominalPinnedRoleFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Dp9 SingleMemberPin: the clash face decides the one-member instance.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Dp9 SingleMemberPin: the singleton diagonal holds the denied reverse — inconsistent.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Dp9 SingleMemberPin: the refutation is pre-engine.");
        Assert.AreEqual(1, totals.NominalPinnedRoleMemberCount, "Dp9 SingleMemberPin: the single member is measured.");
        Assert.AreEqual(1, totals.NominalPinnedRolePinnedEdgeCount, "Dp9 SingleMemberPin: the one self-loop was consumed.");
    }

    /// <summary>The ground denial form: the corpus shape with the probe's denial told as a <c>NegativeObjectPropertyAssertion</c> instead of the concept complement. The face admits the concept form only — the ground form is a named widening it does not admit — so it stays SILENT, and this row is the standing regression that flips to a clash row when the widening is admitted.</summary>
    [TestMethod]
    public void Dp10GroundDenialFormStaysSilent()
    {
        ReasoningModule module = GroundDenialModule();
        NominalPinnedRoleOutcome ran = ContextNominalPinnedRoleDecider.Run(module);

        Assert.IsNull(ran.Consistent, "Dp10 GroundDenialForm: the ground-form denial is outside this rung's admitted shapes.");
        Assert.IsNull(ran.ClashReason, "Dp10 GroundDenialForm: a silent face names no clash reason.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, NominalPinnedRoleFaces, ProbeBudget, TestContext.CancellationToken);

        Assert.AreEqual(0, decision.Statistics.ContextTotals.NominalPinnedRoleDeciderClashes, "Dp10 GroundDenialForm: no clash decision on the ground form.");
    }

    /// <summary>
    /// The dark control: under the explicit
    /// <see cref="EnumerationDeciderFaces.None"/> selection the corpus shape
    /// keeps the honest engine-face budget abstention — the abstained outcome,
    /// no verdict, the inclusive ceiling spent, a genuine saturation behind it —
    /// and the census still ships: the habitat label and all four measured
    /// window fields are on the record while the clash counter stayed at zero.
    /// </summary>
    [TestMethod]
    public void Dp11DarkFaceKeepsTheAbstentionByteIdenticalAndCensusRides()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CorpusShapeModule(), EnumerationDeciderFaces.None, ProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "Dp11 DarkFace: the corpus shape abstains honestly with the face dark — the equivalence machinery outruns the bounded ceiling.");
        Assert.IsNull(decision.Verdict, "Dp11 DarkFace: the dark abstention carries no verdict.");
        Assert.AreEqual((long)ProbeBudget.MaxInferences, totals.InferenceAttempts, "Dp11 DarkFace: the dark run spends exactly the inclusive ceiling.");
        Assert.IsGreaterThan(0L, totals.RuleApplications, "Dp11 DarkFace: the dark exhaust is an admitted saturation, not a non-admission.");
        Assert.AreEqual(EnumerationHabitatClass.NominalPinnedRole, totals.EnumerationHabitat, "Dp11 DarkFace: the habitat label rides the dark abstention record.");
        Assert.AreEqual(2, totals.NominalPinnedRoleMemberCount, "Dp11 DarkFace: the member count is measured dark.");
        Assert.AreEqual(2, totals.NominalPinnedRolePinnedEdgeCount, "Dp11 DarkFace: the consumed self-loops are measured dark.");
        Assert.AreEqual(1, totals.NominalPinnedRoleDeniedEdgeCount, "Dp11 DarkFace: the denial is measured dark.");
        Assert.AreEqual(0, totals.NominalPinnedRoleWindowExceededMembers, "Dp11 DarkFace: no window silence dark at two members.");
        Assert.AreEqual(0, totals.NominalPinnedRoleDeciderClashes, "Dp11 DarkFace: no clash decision with the face dark.");
    }

    /// <summary>
    /// The cross-sibling verdict sweep: every nominal-battery row and every
    /// spy-point-battery row decided under the explicit dark control and under
    /// the nominal-pinned-role face lit alone must be identical in outcome,
    /// verdict, and habitat label, and the new face's clash counter may not
    /// move on any of them — no sibling habitat's module is claimed and no
    /// census label moves under the lit face.
    /// </summary>
    [TestMethod]
    public void Dp12LitFaceMovesNoVerdictAcrossTheSiblingMatrix()
    {
        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool _, string[] _) in ContextNominalBatteryTests.BatteryRows())
        {
            SweepSiblingRow("nominal:" + name, module, ReasoningConfiguration.Default.Budget, mismatches);
        }

        foreach((string name, ReasoningModule module) in ContextSpyPointDeciderTests.SpyPointRows())
        {
            SweepSiblingRow("spypoint:" + name, module, ProbeBudget, mismatches);
        }

        Assert.IsEmpty(mismatches, string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>Runs one sibling row dark and with the nominal-pinned-role face lit alone, collecting any outcome, verdict, habitat, or counter movement.</summary>
    /// <param name="name">The row's sweep label.</param>
    /// <param name="module">The row's module.</param>
    /// <param name="budget">The budget both runs share.</param>
    /// <param name="mismatchesToAppendTo">The mismatch collection.</param>
    private void SweepSiblingRow(string name, ReasoningModule module, ReasoningBudget budget, List<string> mismatchesToAppendTo)
    {
        ModuleDecision dark = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, budget, TestContext.CancellationToken);
        ModuleDecision lit = ContextSaturationModuleReasoner.DecideModule(module, NominalPinnedRoleFaces, budget, TestContext.CancellationToken);
        ContextSaturationStatistics litTotals = lit.Statistics.ContextTotals;
        if(litTotals.NominalPinnedRoleDeciderClashes != 0)
        {
            mismatchesToAppendTo.Add(name + ": a sibling row was claimed by the nominal-pinned-role face.");

            return;
        }

        if(litTotals.EnumerationHabitat != dark.Statistics.ContextTotals.EnumerationHabitat)
        {
            mismatchesToAppendTo.Add(name + ": the census label moved between the dark and lit runs.");

            return;
        }

        if(lit.Outcome != dark.Outcome)
        {
            mismatchesToAppendTo.Add(name + ": outcome moved " + dark.Outcome + " -> " + lit.Outcome + ".");

            return;
        }

        if(lit.Verdict is null != dark.Verdict is null || (lit.Verdict is not null && lit.Verdict.IsConsistent != dark.Verdict!.IsConsistent))
        {
            mismatchesToAppendTo.Add(name + ": the verdict moved under the lit face.");
        }
    }

    /// <summary>The window-constant derivation pin: the member ceiling sits on the counting faces' shared sixteen boundary discipline, and a resolution sitting exactly AT the bound still decides, so the boundary is inclusive and the silence begins one member later.</summary>
    [TestMethod]
    public void Dp13WindowConstantDerivation()
    {
        NominalPinnedRoleOutcome atBound = ContextNominalPinnedRoleDecider.Run(MemberWindowModule(ContextNominalPinnedRoleDecider.NominalPinnedRoleMemberBound));

        Assert.IsFalse(atBound.Consistent, "Dp13 WindowConstant: the clash face decides AT the member bound — sixteen pinned seats still hold the denied reverse.");
        Assert.AreEqual(ContextNominalCountingDecider.CountedPopulationBound, atBound.Window.MemberCount, "Dp13 WindowConstant: the measured member ceiling shares the counted-population bound — one boundary discipline across the pre-engine faces.");
        Assert.AreEqual(ContextClausifier.GroundCountingCliqueBound, atBound.Window.MemberCount, "Dp13 WindowConstant: the measured member ceiling shares the ground rider's clique bound.");
        Assert.AreEqual(ContextPartitionCountingDecider.PartitionAnchorBound, atBound.Window.MemberCount, "Dp13 WindowConstant: the measured member ceiling shares the partition faces' anchor bound.");
        Assert.AreEqual(ContextBooleanGadgetDecider.GadgetAtomBound, atBound.Window.MemberCount, "Dp13 WindowConstant: the measured member ceiling shares the gadget faces' atom bound.");
        Assert.AreEqual(ContextEnumerationAlgebraDecider.PairAssignmentBound, atBound.Window.MemberCount, "Dp13 WindowConstant: the measured member ceiling shares the pair-composition bound.");
        Assert.AreEqual(ContextSpyPointDecider.SpyPointMemberBound, atBound.Window.MemberCount, "Dp13 WindowConstant: the measured member ceiling shares the spy-point member bound.");
        Assert.AreEqual(0, atBound.Window.MemberSilences, "Dp13 WindowConstant: no window silence exactly at the bound.");
    }

    /// <summary>The statistics assembly: the five cluster fields read the clash decision's record exactly, the clash reason surfaces on the decider's own outcome, and the silent premise's record carries the measured window with the clash counter at zero.</summary>
    [TestMethod]
    public void Dp14StatisticsAssembleOffTheClashReason()
    {
        ModuleDecision clash = ContextSaturationModuleReasoner.DecideModule(CorpusShapeModule(), NominalPinnedRoleFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics clashTotals = clash.Statistics.ContextTotals;

        Assert.AreEqual(2, clashTotals.NominalPinnedRoleMemberCount, "Dp14 Statistics: the member count assembles off the clash record.");
        Assert.AreEqual(2, clashTotals.NominalPinnedRolePinnedEdgeCount, "Dp14 Statistics: the pinned-edge count assembles off the clash record.");
        Assert.AreEqual(1, clashTotals.NominalPinnedRoleDeniedEdgeCount, "Dp14 Statistics: the denied-edge count assembles off the clash record.");
        Assert.AreEqual(1, clashTotals.NominalPinnedRoleDeciderClashes, "Dp14 Statistics: the clash counter assembles off the clash record.");
        Assert.AreEqual(0, clashTotals.NominalPinnedRoleWindowExceededMembers, "Dp14 Statistics: no window silence on the clash record.");
        Assert.AreEqual("NominalPinnedRoleDeniedDiagonalEdge(" + Example + "r)", ContextNominalPinnedRoleDecider.Run(CorpusShapeModule()).ClashReason, "Dp14 Statistics: the reason string surfaces on the decider's own outcome.");

        ModuleDecision silent = ContextSaturationModuleReasoner.DecideModule(PremiseModule(), NominalPinnedRoleFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics silentTotals = silent.Statistics.ContextTotals;

        Assert.AreEqual(0, silentTotals.NominalPinnedRoleDeciderClashes, "Dp14 Statistics: the silent record's clash counter stays at zero.");
        Assert.AreEqual(2, silentTotals.NominalPinnedRoleMemberCount, "Dp14 Statistics: the silent record still carries the measured member count.");
        Assert.AreEqual(2, silentTotals.NominalPinnedRolePinnedEdgeCount, "Dp14 Statistics: the silent record still carries the consumed self-loops.");
        Assert.AreEqual(0, silentTotals.NominalPinnedRoleDeniedEdgeCount, "Dp14 Statistics: the premise tells no denial to measure.");
    }

    /// <summary>
    /// The fourteen near-miss modules, one per named hazard. Every one is tuned
    /// so that a leak in its guard would refute the module: the pinning stays
    /// otherwise complete, the probe pair stays present, and only the one
    /// guarded ingredient is perturbed.
    /// </summary>
    /// <returns>The rows.</returns>
    private static (string Name, ReasoningModule Module)[] NearMissRows()
    {
        return
        [
            ("DropOneLoop", ProbedModule(
                InverseFunctional("r"),
                Range("r", Class("A")),
                EquivalentClasses(Class("A"), OneOf("a", "b")),
                Edge("r", "a", "a"))),

            ("DropAllLoops", ProbedModule(
                InverseFunctional("r"),
                Range("r", Class("A")),
                EquivalentClasses(Class("A"), OneOf("a", "b")))),

            ("ReversedHopSubClassOf", ProbedModule(
                InverseFunctional("r"),
                Range("r", Class("A")),
                SubClassOf(OneOf("a", "b"), Class("A")),
                Edge("r", "a", "a"),
                Edge("r", "b", "b"))),

            ("TwoHopChain", ProbedModule(
                InverseFunctional("r"),
                Range("r", Class("A")),
                EquivalentClasses(Class("A"), Class("B")),
                EquivalentClasses(Class("B"), OneOf("a", "b")),
                Edge("r", "a", "a"),
                Edge("r", "b", "b"))),

            ("NoInverseFunctional", ProbedModule(
                Range("r", OneOf("a", "b")),
                Edge("r", "a", "a"),
                Edge("r", "b", "b"))),

            ("SymmetricInsteadOfInverseFunctional", ProbedModule(
                Characteristic(OwlPropertyCharacteristic.Symmetric, "r"),
                Range("r", OneOf("a", "b")),
                Edge("r", "a", "a"),
                Edge("r", "b", "b"))),

            ("DomainInsteadOfRange", ProbedModule(
                InverseFunctional("r"),
                Domain("r", OneOf("a", "b")),
                Edge("r", "a", "a"),
                Edge("r", "b", "b"))),

            ("CapRoleMismatch", ProbedModule(
                InverseFunctional("p"),
                Range("r", OneOf("a", "b")),
                Edge("r", "a", "a"),
                Edge("r", "b", "b"))),

            ("InverseExpressionAtCharacteristic", ProbedModule(
                InverseFunctionalInverse("r"),
                Range("r", OneOf("a", "b")),
                Edge("r", "a", "a"),
                Edge("r", "b", "b"))),

            ("InverseExpressionAtRange", ProbedModule(
                InverseFunctional("r"),
                RangeInverse("r", OneOf("a", "b")),
                Edge("r", "a", "a"),
                Edge("r", "b", "b"))),

            ("InverseExpressionAtSelfLoop", ProbedModule(
                InverseFunctional("r"),
                Range("r", OneOf("a", "b")),
                InverseProperties("r", "q"),
                Edge("q", "a", "a"),
                Edge("q", "b", "b"))),

            ("InverseExpressionAtClashEdge", Module(
                InverseFunctional("r"),
                Range("r", OneOf("a", "b")),
                Edge("r", "a", "a"),
                Edge("r", "b", "b"),
                Edge("r", "u", "v"),
                DenialInverse("r", "v", "u"))),

            ("AnonymousMember", ProbedModule(
                InverseFunctional("r"),
                Range("r", MixedOneOf("a", "hidden")),
                Edge("r", "a", "a"))),

            ("DataPropertyLookalike", ProbedModule(
                new OwlFunctionalDataPropertyAxiom(Individual("r")) { Origin = Origin("datafunc") },
                Range("r", OneOf("a", "b")),
                Edge("r", "a", "a"),
                Edge("r", "b", "b"))),
        ];
    }

    /// <summary>The corpus shape: the one-hop equivalence route, the Thing assertions, the two self-loops, and the probe pair.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule CorpusShapeModule()
    {
        return ProbedModule(
            InverseFunctional("r"),
            Range("r", Class("A")),
            EquivalentClasses(Class("A"), OneOf("a", "b")),
            ClassAssertion(Thing, Individual("a")),
            ClassAssertion(Thing, Individual("b")),
            ClassAssertion(Thing, Individual("c")),
            Edge("r", "a", "a"),
            Edge("r", "b", "b"));
    }

    /// <summary>The corpus shape's premise: the pinned diagonal without any probe.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule PremiseModule()
    {
        return Module(
            InverseFunctional("r"),
            Range("r", Class("A")),
            EquivalentClasses(Class("A"), OneOf("a", "b")),
            ClassAssertion(Thing, Individual("a")),
            ClassAssertion(Thing, Individual("b")),
            ClassAssertion(Thing, Individual("c")),
            Edge("r", "a", "a"),
            Edge("r", "b", "b"));
    }

    /// <summary>The zero-hop route: the range carries its one-of inline.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule InlineRangeModule()
    {
        return ProbedModule(
            InverseFunctional("r"),
            Range("r", OneOf("a", "b")),
            Edge("r", "a", "a"),
            Edge("r", "b", "b"));
    }

    /// <summary>The subclass-hop route: the range's named class bounded by the one-of with the class in subclass position.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SubClassHopModule()
    {
        return ProbedModule(
            InverseFunctional("r"),
            Range("r", Class("A")),
            SubClassOf(Class("A"), OneOf("a", "b")),
            Edge("r", "a", "a"),
            Edge("r", "b", "b"));
    }

    /// <summary>The duplicate-spelling route: the one-of repeats a member, deduping to two seats.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule DuplicateMemberModule()
    {
        return ProbedModule(
            InverseFunctional("r"),
            Range("r", OneOf("a", "a", "b")),
            Edge("r", "a", "a"),
            Edge("r", "b", "b"));
    }

    /// <summary>The pinned-loop denial: the corpus shape's premise with the probe replaced by a denial standing on the told loop itself.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule PinnedLoopDenialModule()
    {
        return Module(
            InverseFunctional("r"),
            Range("r", Class("A")),
            EquivalentClasses(Class("A"), OneOf("a", "b")),
            Edge("r", "a", "a"),
            Edge("r", "b", "b"),
            Denial("r", "a", "a"));
    }

    /// <summary>The ground-form route: the corpus shape with the denial told as a negative object-property assertion.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule GroundDenialModule()
    {
        return Module(
            InverseFunctional("r"),
            Range("r", Class("A")),
            EquivalentClasses(Class("A"), OneOf("a", "b")),
            Edge("r", "a", "a"),
            Edge("r", "b", "b"),
            Edge("r", "u", "v"),
            new OwlNegativeObjectPropertyAssertionAxiom(Individual("v"), Property("r"), Individual("u")) { Origin = Origin("grounddenial") });
    }

    /// <summary>The single-member instance: a singleton range, its one self-loop, and the probe pair.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SingleMemberModule()
    {
        return ProbedModule(
            InverseFunctional("r"),
            Range("r", OneOf("a")),
            Edge("r", "a", "a"));
    }

    /// <summary>The member-window template: an inline range over the requested number of distinct named members, every one of them looped, beside the probe pair.</summary>
    /// <param name="members">The distinct range members.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule MemberWindowModule(int members)
    {
        string[] names = new string[members];
        for(int index = 0; index < members; index++)
        {
            names[index] = "m" + index;
        }

        List<OwlAxiom> axioms =
        [
            InverseFunctional("r"),
            Range("r", OneOf(names)),
        ];
        for(int index = 0; index < members; index++)
        {
            axioms.Add(Edge("r", names[index], names[index]));
        }

        axioms.Add(Edge("r", "u", "v"));
        axioms.Add(Denial("r", "v", "u"));

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>Builds a module over the axioms with the standard skolemized probe pair appended: the told fresh edge beside the concept denial of its exact reverse — the arm-S shape the corpus case poses.</summary>
    /// <param name="axioms">The module axioms ahead of the probe pair.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ProbedModule(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms, Edge("r", "u", "v"), Denial("r", "v", "u")], Violations: []);
    }

    /// <summary>A provenance quad naming the axiom's origin.</summary>
    /// <param name="marker">The origin marker's local name.</param>
    /// <returns>The quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(new NamedNode(Utf8Strings.From(Example + marker)), new NamedNode(Utf8Strings.From(Example + "r")), new NamedNode(Utf8Strings.From(Example + "o")), Graph: null);
    }

    /// <summary>The <c>owl:Thing</c> reference — the corpus shape's Thing-assertion class.</summary>
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

    /// <summary>The inverse of a named object property in the example namespace.</summary>
    /// <param name="local">The forward role's local name.</param>
    /// <returns>The inverse property expression.</returns>
    private static OwlInverseObjectProperty InverseProperty(string local)
    {
        return new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named individual in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The individual node.</returns>
    private static NamedNode Individual(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>An anonymous individual — the member spelling that drops a range resolution whole.</summary>
    /// <param name="label">The blank node's label.</param>
    /// <returns>The blank node.</returns>
    private static BlankNode Anonymous(string label)
    {
        return new BlankNode(Utf8Strings.From(label));
    }

    /// <summary>An enumeration of named individuals in the example namespace.</summary>
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

    /// <summary>An enumeration of one named and one ANONYMOUS individual — the one-of shape that drops a range resolution whole, since an anonymous member carries no told-edge index entry.</summary>
    /// <param name="named">The named member's local name.</param>
    /// <param name="label">The anonymous member's blank-node label.</param>
    /// <returns>The enumeration.</returns>
    private static OwlObjectOneOf MixedOneOf(string named, string label)
    {
        return new OwlObjectOneOf([Individual(named), Anonymous(label)]);
    }

    /// <summary>A told inverse-functional characteristic over a plain named role — the S1 candidate shape.</summary>
    /// <param name="local">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom InverseFunctional(string local)
    {
        return Characteristic(OwlPropertyCharacteristic.InverseFunctional, local);
    }

    /// <summary>A told inverse-functional characteristic over an INLINE INVERSE property expression — the characteristic-position near-miss spelling.</summary>
    /// <param name="local">The inverted role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom InverseFunctionalInverse(string local)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.InverseFunctional, InverseProperty(local)) { Origin = Origin("inversecharacteristic") };
    }

    /// <summary>A told characteristic over a plain named role.</summary>
    /// <param name="characteristic">The asserted characteristic.</param>
    /// <param name="local">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Characteristic(OwlPropertyCharacteristic characteristic, string local)
    {
        return new OwlObjectPropertyCharacteristicAxiom(characteristic, Property(local)) { Origin = Origin("characteristic") };
    }

    /// <summary>A told range over a plain named role.</summary>
    /// <param name="local">The role's local name.</param>
    /// <param name="range">The range expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyRangeAxiom Range(string local, OwlClassExpression range)
    {
        return new OwlObjectPropertyRangeAxiom(Property(local), range) { Origin = Origin("range") };
    }

    /// <summary>A told range over an INLINE INVERSE property expression — the range-position near-miss spelling.</summary>
    /// <param name="local">The inverted role's local name.</param>
    /// <param name="range">The range expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyRangeAxiom RangeInverse(string local, OwlClassExpression range)
    {
        return new OwlObjectPropertyRangeAxiom(InverseProperty(local), range) { Origin = Origin("inverserange") };
    }

    /// <summary>A told domain over a plain named role — the axiom kind the face never reads as a range.</summary>
    /// <param name="local">The role's local name.</param>
    /// <param name="domain">The domain expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyDomainAxiom Domain(string local, OwlClassExpression domain)
    {
        return new OwlObjectPropertyDomainAxiom(Property(local), domain) { Origin = Origin("domain") };
    }

    /// <summary>A told inverse between two named object properties — the partner spelling the self-loop-position near-miss tells its loops on.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlInverseObjectPropertiesAxiom InverseProperties(string first, string second)
    {
        return new OwlInverseObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("inversepair") };
    }

    /// <summary>A told equivalence between two class expressions — the one-hop route's axiom.</summary>
    /// <param name="first">The first expression.</param>
    /// <param name="second">The second expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentClassesAxiom EquivalentClasses(OwlClassExpression first, OwlClassExpression second)
    {
        return new OwlEquivalentClassesAxiom(first, second) { Origin = Origin("equivalence") };
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

    /// <summary>A told edge between two named individuals over a plain named role.</summary>
    /// <param name="role">The role's local name.</param>
    /// <param name="source">The source individual's local name.</param>
    /// <param name="target">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom Edge(string role, string source, string target)
    {
        return new OwlObjectPropertyAssertionAxiom(Individual(source), Property(role).Named, Individual(target)) { Origin = Origin("edge") };
    }

    /// <summary>The concept-form edge denial: the named carrier typed with the top-level complement of the has-value over the plain role and the named excluded value.</summary>
    /// <param name="role">The denied role's local name.</param>
    /// <param name="carrier">The carrier individual's local name — the denied edge's source.</param>
    /// <param name="denied">The excluded value's local name — the denied edge's target.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom Denial(string role, string carrier, string denied)
    {
        return new OwlClassAssertionAxiom(new OwlObjectComplementOf(new OwlObjectHasValue(Property(role), Individual(denied))), Individual(carrier)) { Origin = Origin("denial") };
    }

    /// <summary>The concept-form edge denial spelled over an INLINE INVERSE property expression — the clash-edge-position near-miss spelling.</summary>
    /// <param name="role">The inverted role's local name.</param>
    /// <param name="carrier">The carrier individual's local name.</param>
    /// <param name="denied">The excluded value's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom DenialInverse(string role, string carrier, string denied)
    {
        return new OwlClassAssertionAxiom(new OwlObjectComplementOf(new OwlObjectHasValue(InverseProperty(role), Individual(denied))), Individual(carrier)) { Origin = Origin("inversedenial") };
    }
}
