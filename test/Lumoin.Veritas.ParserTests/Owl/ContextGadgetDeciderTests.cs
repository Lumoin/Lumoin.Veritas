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
/// The boolean-cardinality-gadget habitat decider's battery: the decided rows
/// on both verdict directions and both target shapes — the pure propositional
/// module and the module carrying the modal prelude — the explicit dark control
/// and its census row, the fifteen near-miss sub-checks one per lock-round
/// attack (three of them replicas of real corpus lookalike shapes), the window
/// boundary and derivation pins, the verdict-identity sweep, and the
/// defined-atom-elimination rows — the construction head-to-head, the widened
/// atom window, and the degradation guards. Every row
/// drives the production seams — the faces-carrying reasoner overload or the
/// decider's own measurement surface — and every counter the battery reads is
/// consumed by an assert.
/// </summary>
[TestClass]
internal sealed class ContextGadgetDeciderTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the battery's classes, properties, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/gadgetcsp#";

    /// <summary>Both gadget faces lit — the selection the decided rows drive.</summary>
    private const EnumerationDeciderFaces GadgetFaces = EnumerationDeciderFaces.GadgetClash | EnumerationDeciderFaces.GadgetCertify;

    /// <summary>Every decider face the recognizer's registry lights, read from the production fold — the selection the verdict-identity sweep runs against the explicit dark control.</summary>
    private static EnumerationDeciderFaces AllFaces { get; } = ContextHabitatRecognizer.EveryFaceLit;

    /// <summary>The bounded budget the silence rows drive: enough for the engine to fire rules on a gadget module, far below what its saturation would need.</summary>
    private static ReasoningBudget ProbeBudget { get; } = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 4096);

    /// <summary>
    /// The pure propositional template made satisfiable — the gadget cluster
    /// without the disjointness link that closes it: seven gadget properties
    /// across the data and object sides, one free class, fourteen determined
    /// classes of which five are defined twice, one anonymous individual typed
    /// by the target class. The bounded walk finds a passing assignment and
    /// decides CONSISTENT with zero inference attempts and no engine. The
    /// habitat-label assert doubles as the positive reachability pin — a
    /// nominal-free gadget module must reach the gadget probe at all — and every
    /// one of the six gadget statistics fields is read.
    /// </summary>
    [TestMethod]
    public void Gd1GadgetTemplateCertifiesPure()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CertifyingTemplateModule(), GadgetFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Gd1: the certify face decides the template at the production ceiling.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Gd1: a passing assignment witnesses the module consistent.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Gd1: a pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(0, totals.ContextsCreated, "Gd1: no engine was constructed — the seat is upstream of every engine axis.");
        Assert.AreEqual(EnumerationHabitatClass.BooleanCardinalityGadget, totals.EnumerationHabitat, "Gd1: a nominal-free counting module carrying gadget and named-intersection equivalences reaches the gadget probe and is labelled Shape G.");
        Assert.AreEqual(7, totals.GadgetPropertyAtomCount, "Gd1: the gadget-property atoms are measured.");
        Assert.AreEqual(1, totals.GadgetFreeClassAtomCount, "Gd1: the free-class atom is measured.");
        Assert.IsGreaterThan(0, totals.GadgetEvaluatedVectorCount, "Gd1: the certifying walk visited at least the passing assignment.");
        Assert.IsLessThanOrEqualTo(256, totals.GadgetEvaluatedVectorCount, "Gd1: the certifying walk stopped inside the eight-atom assignment space.");
        Assert.AreEqual(1, totals.GadgetDeciderCertifications, "Gd1: the certify face's counter reads the decision.");
        Assert.AreEqual(0, totals.GadgetDeciderClashes, "Gd1: the clash face did not decide this module.");
        Assert.AreEqual(0, totals.GadgetWindowExceededAtoms, "Gd1: no window silence at eight atoms.");
    }

    /// <summary>
    /// The pure propositional template with the disjointness link in place —
    /// the corpus refutation shape, whose target obligation compiles to a
    /// three-way disjunction every branch of which the told implications close:
    /// the exhaustion refutation decides INCONSISTENT pre-engine, having walked
    /// the whole free assignment space — three atoms survive defined-atom
    /// elimination out of the eight compiled, the five dual-defined classes
    /// pinning their gadget atoms.
    /// </summary>
    [TestMethod]
    public void Gd2GadgetTemplateRefutesPure()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(RefutingTemplateModule(), GadgetFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Gd2: the clash face decides the refutation template.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Gd2: every assignment fails, so no model exists.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Gd2: the refutation is pre-engine.");
        Assert.AreEqual(1, totals.GadgetDeciderClashes, "Gd2: the clash face's counter reads the decision.");
        Assert.AreEqual(0, totals.GadgetDeciderCertifications, "Gd2: no certification on a refuted module.");
        Assert.AreEqual(7, totals.GadgetPropertyAtomCount, "Gd2: the gadget-property atoms are measured raw.");
        Assert.AreEqual(1, totals.GadgetFreeClassAtomCount, "Gd2: the free-class atom is measured raw.");
        Assert.AreEqual(8, totals.GadgetEvaluatedVectorCount, "Gd2: the refutation exhausted the whole 2^3 free space — five of the eight compiled atoms are defined and computed, never enumerated.");
    }

    /// <summary>The prelude shape on the certify face: the target class defined as the BARE existential into the anchor, whose at-most-one merge forces the merge class onto the typed individual — the propositional core beside it stays satisfiable, so the module decides CONSISTENT pre-engine.</summary>
    [TestMethod]
    public void Gd3PreludeTemplateCertifies()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CertifyingPreludeModule(), GadgetFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Gd3: the certify face decides the bare-existential prelude module.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Gd3: the forced merge leaves the propositional core satisfiable.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Gd3: the decision is pre-engine.");
        Assert.AreEqual(EnumerationHabitatClass.BooleanCardinalityGadget, totals.EnumerationHabitat, "Gd3: the prelude module reaches the gadget probe and is labelled Shape G.");
        Assert.AreEqual(1, totals.GadgetDeciderCertifications, "Gd3: the certify face's counter reads the decision.");
        Assert.AreEqual(0, totals.GadgetDeciderClashes, "Gd3: no clash on a certified module.");
        Assert.AreEqual(4, totals.GadgetPropertyAtomCount, "Gd3: the gadget-property atoms are measured across the prelude.");
        Assert.AreEqual(1, totals.GadgetFreeClassAtomCount, "Gd3: the free-class atom is measured.");
    }

    /// <summary>The prelude shape on the clash face: the target class defined as a named conjunct BESIDE the existential, and that conjunct denies the very property atom the forced merge asserts — the exhaustion refutation decides INCONSISTENT pre-engine over the two-atom free space the dual-defined twin leaves.</summary>
    [TestMethod]
    public void Gd4PreludeTemplateRefutes()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(RefutingPreludeModule(), GadgetFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Gd4: the clash face decides the conjunct-carrying prelude module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Gd4: the named conjunct and the forced merge contradict on one property atom.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Gd4: the refutation is pre-engine.");
        Assert.AreEqual(1, totals.GadgetDeciderClashes, "Gd4: the clash face's counter reads the decision.");
        Assert.AreEqual(0, totals.GadgetDeciderCertifications, "Gd4: no certification on a refuted module.");
        Assert.AreEqual(4, totals.GadgetEvaluatedVectorCount, "Gd4: the refutation exhausted the whole 2^2 free space — the dual-defined twin pins its gadget atom, leaving two of the three compiled atoms enumerated.");
    }

    /// <summary>
    /// The dark control: under the explicit
    /// <see cref="EnumerationDeciderFaces.None"/> selection the certifying
    /// template keeps the honest engine-face budget abstention — the abstained
    /// outcome, no verdict, the inclusive ceiling spent, and the exhaust's
    /// measured funnel profile intact.
    /// </summary>
    [TestMethod]
    public void Gd5DarkFacesKeepTheHonestAbstentionByteIdentical()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CertifyingTemplateModule(), EnumerationDeciderFaces.None, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "Gd5: the template abstains honestly with the faces dark.");
        Assert.IsNull(decision.Verdict, "Gd5: the dark abstention carries no verdict.");
        Assert.AreEqual((long)ReasoningConfiguration.Default.Budget.MaxInferences, totals.InferenceAttempts, "Gd5: the dark run spends exactly the inclusive ceiling.");
        Assert.IsGreaterThan(0L, totals.RuleApplications, "Gd5: the dark exhaust is an admitted saturation, not a non-admission.");
        Assert.IsGreaterThan(0L, totals.WorklistEnqueues, "Gd5: the dark exhaust lands genuine insertions at the funnel's head.");
    }

    /// <summary>The census ships unconditionally: on a dark abstention over the same template the habitat label and both measured atom counts are already on the record, the walk never ran, and both decision counters are still zero.</summary>
    [TestMethod]
    public void Gd6CensusRidesTheDarkAbstentionRecordsAlways()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CertifyingTemplateModule(), EnumerationDeciderFaces.None, ProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "Gd6: the template stays abstained dark — the census never moves a decision.");
        Assert.AreEqual(EnumerationHabitatClass.BooleanCardinalityGadget, totals.EnumerationHabitat, "Gd6: the habitat label rides the dark abstention record.");
        Assert.AreEqual(7, totals.GadgetPropertyAtomCount, "Gd6: the gadget-property atoms are measured dark.");
        Assert.AreEqual(1, totals.GadgetFreeClassAtomCount, "Gd6: the free-class atom is measured dark.");
        Assert.AreEqual(0, totals.GadgetEvaluatedVectorCount, "Gd6: the dark path never walks an assignment.");
        Assert.AreEqual(0, totals.GadgetWindowExceededAtoms, "Gd6: no window silence dark at eight atoms.");
        Assert.AreEqual(0, totals.GadgetDeciderClashes, "Gd6: no clash decision with the faces dark.");
        Assert.AreEqual(0, totals.GadgetDeciderCertifications, "Gd6: no certification with the faces dark.");
    }

    /// <summary>
    /// The near-miss sub-checks: fourteen perturbations of the two templates,
    /// each of which must leave the faces SILENT — a definition cycle, the three
    /// non-boolean or qualified cardinality attacks, a gadget property carrying
    /// a characteristic, a gadget property doubling as a prelude role, the four
    /// prelude mis-shapes including the merge class aliased to the anchor, the
    /// two ABox extras, and two replicas of real corpus lookalike shapes (the
    /// dynamic-blocking modal module and the gadget-pair-with-universal module).
    /// The faces are read directly and through the reasoner: neither decision
    /// counter may move on any row. The fifteenth sub-check is the reachability
    /// gap the recognizer holds open: a FACE-ADMISSIBLE module carrying no
    /// intersection equivalence at all is probe-invisible, so its census label
    /// stays none and it rides the engine — pinned here so no unadjudicated
    /// probe widening passes silently.
    /// </summary>
    [TestMethod]
    public void Gd7NearMissSilences()
    {
        foreach((string name, ReasoningModule module) in NearMissRows())
        {
            Assert.IsNull(ContextBooleanGadgetDecider.Run(module).Consistent, "Gd7 " + name + ": the face must stay silent on the near miss.");

            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, GadgetFaces, ProbeBudget, TestContext.CancellationToken);
            ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
            Assert.AreEqual(0, totals.GadgetDeciderClashes, "Gd7 " + name + ": no clash decision on the near miss.");
            Assert.AreEqual(0, totals.GadgetDeciderCertifications, "Gd7 " + name + ": no certification on the near miss.");
        }

        ReasoningModule intersectionFree = IntersectionFreeGadgetModule();
        ModuleDecision probeInvisible = ContextSaturationModuleReasoner.DecideModule(intersectionFree, GadgetFaces, ProbeBudget, TestContext.CancellationToken);

        Assert.IsNotNull(ContextBooleanGadgetDecider.Run(intersectionFree).Consistent, "Gd7 IntersectionFreeGadget: the FACE admits the module — the gap is the probe's, not the jurisdiction's.");
        Assert.AreEqual(EnumerationHabitatClass.None, probeInvisible.Statistics.ContextTotals.EnumerationHabitat, "Gd7 IntersectionFreeGadget: no intersection equivalence, so the probe never fires and the census label stays none.");
        Assert.AreEqual(0, probeInvisible.Statistics.ContextTotals.GadgetDeciderClashes, "Gd7 IntersectionFreeGadget: an unlabelled module never reaches the gadget faces.");
        Assert.AreEqual(0, probeInvisible.Statistics.ContextTotals.GadgetDeciderCertifications, "Gd7 IntersectionFreeGadget: an unlabelled module never reaches the gadget faces.");
    }

    /// <summary>
    /// The window silence charges its named counter, with the measured numbers
    /// landing BEFORE the boundary comparison: a module one atom past the bound
    /// charges the atom counter, reports both measured atom counts, and walks no
    /// assignment at all. The row is read on the decider's own measurement
    /// surface and on the reasoner's statistics record.
    /// </summary>
    [TestMethod]
    public void Gd8WindowSilencesChargeTheirNamedCounter()
    {
        int overflow = ContextBooleanGadgetDecider.GadgetAtomBound + 1;
        ReasoningModule module = ConjunctionModule(overflow, negated: false);
        BooleanGadgetOutcome outcome = ContextBooleanGadgetDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Gd8: the face is silent past the atom bound.");
        Assert.AreEqual(overflow, outcome.Window.PropertyAtomCount, "Gd8: the measured gadget-property atoms are reported past the bound.");
        Assert.AreEqual(0, outcome.Window.FreeClassAtomCount, "Gd8: the measured free-class atoms are reported past the bound.");
        Assert.AreEqual(1, outcome.Window.AtomSilences, "Gd8: the silence is charged to the atom counter.");
        Assert.AreEqual(0, outcome.Window.EvaluatedVectorCount, "Gd8: a window silence walks no assignment.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, GadgetFaces, ProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(1, totals.GadgetWindowExceededAtoms, "Gd8: the window silence rides the statistics record.");
        Assert.AreEqual(overflow, totals.GadgetPropertyAtomCount, "Gd8: the measured atoms ride the statistics record.");
        Assert.AreEqual(0, totals.GadgetDeciderCertifications, "Gd8: no certification past the atom bound.");
        Assert.AreEqual(0, totals.GadgetDeciderClashes, "Gd8: no clash past the atom bound.");
    }

    /// <summary>
    /// The window-constant derivation pins, expressed through measured values: a
    /// module sitting exactly AT the atom bound still decides, its measured atom
    /// total lands on the counting faces' shared sixteen boundary discipline
    /// (the counted-population, ground-clique, and partition-anchor ceilings),
    /// and the bound's stated cost formula — 2^16 assignments walked by a
    /// refutation at the bound — is exercised exactly.
    /// </summary>
    [TestMethod]
    public void Gd9WindowConstantDerivations()
    {
        BooleanGadgetOutcome certified = ContextBooleanGadgetDecider.Run(ConjunctionModule(ContextBooleanGadgetDecider.GadgetAtomBound, negated: true));

        Assert.IsTrue(certified.Consistent, "Gd9: the certify face decides AT the atom bound — sixteen denied gadgets satisfied by the all-false assignment.");
        Assert.AreEqual(ContextNominalCountingDecider.CountedPopulationBound, certified.Window.AtomCount, "Gd9: the measured atom ceiling shares the counted-population bound — one boundary discipline across the pre-engine faces.");
        Assert.AreEqual(ContextClausifier.GroundCountingCliqueBound, certified.Window.AtomCount, "Gd9: the measured atom ceiling shares the ground rider's clique bound.");
        Assert.AreEqual(ContextPartitionCountingDecider.PartitionAnchorBound, certified.Window.PropertyAtomCount, "Gd9: the measured gadget-property ceiling shares the partition faces' anchor bound.");
        Assert.AreEqual(0, certified.Window.FreeClassAtomCount, "Gd9: the at-bound module spends its whole atom budget on gadget properties.");
        Assert.AreEqual(0, certified.Window.AtomSilences, "Gd9: no window silence exactly at the bound.");
        Assert.AreEqual(1, certified.Window.EvaluatedVectorCount, "Gd9: the all-false assignment passes first, so the walk stops at one.");

        BooleanGadgetOutcome refuted = ContextBooleanGadgetDecider.Run(ContradictionModule(ContextBooleanGadgetDecider.GadgetAtomBound));

        Assert.IsFalse(refuted.Consistent, "Gd9: the contradicting module at the bound refutes.");
        Assert.AreEqual(ContextBooleanGadgetDecider.GadgetAtomBound, refuted.Window.AtomCount, "Gd9: the refuting module sits exactly at the bound.");
        Assert.AreEqual(1 << ContextBooleanGadgetDecider.GadgetAtomBound, refuted.Window.EvaluatedVectorCount, "Gd9: 2^16 = 65,536 assignment evaluations at the bound — the documented cost formula, walked.");
    }

    /// <summary>
    /// The verdict-identity sweep: every certified nominal-battery row and every
    /// certified partition-battery row decided under the explicit dark control
    /// and under every lit face, across both paramodulation scopes and both
    /// root-tier topologies, must be identical in outcome, verdict, subsumption
    /// set, and attempt count — the new probe moves no existing classification
    /// and no existing verdict, and no gadget face may claim a row of either
    /// neighbouring habitat. The four gadget rows ride the same matrix: the lit
    /// run decides pre-engine with zero attempts in every cell, and where the
    /// dark run reached a verdict of its own the two agree.
    /// </summary>
    [TestMethod]
    public void Gd10LitFacesMoveNoCertifiedVerdictAcrossTheMatrix()
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
                if(litTotals.GadgetDeciderClashes + litTotals.GadgetDeciderCertifications > 0)
                {
                    mismatches.Add(cell + ": a nominal-battery row was claimed by a gadget face.");
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
        foreach((string name, ReasoningModule module, bool consistent) in ContextPartitionDeciderTests.PartitionRows())
        {
            foreach((NominalParamodulationScope scope, RootContextTopology topology) in cells)
            {
                string cell = name + "@" + scope + "/" + topology;
                ModuleDecision lit = ContextSaturationModuleReasoner.DecideModule(module, AllFaces, scope, topology, ProbeBudget, TestContext.CancellationToken);
                ContextSaturationStatistics litTotals = lit.Statistics.ContextTotals;
                if(litTotals.GadgetDeciderClashes + litTotals.GadgetDeciderCertifications > 0)
                {
                    mismatches.Add(cell + ": a partition-battery row was claimed by a gadget face.");
                    continue;
                }

                if(lit.Outcome != ReasoningDecisionOutcome.Decided || lit.Verdict is null || lit.Verdict.IsConsistent != consistent)
                {
                    mismatches.Add(cell + ": the partition row lost its certified verdict under the gadget-lit faces.");
                    continue;
                }

                partitionDecided++;
            }
        }

        int gadgetDecided = 0;
        foreach((string name, ReasoningModule module, bool consistent) in GadgetRows())
        {
            foreach((NominalParamodulationScope scope, RootContextTopology topology) in cells)
            {
                string cell = name + "@" + scope + "/" + topology;
                ModuleDecision dark = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, scope, topology, ProbeBudget, TestContext.CancellationToken);
                ModuleDecision lit = ContextSaturationModuleReasoner.DecideModule(module, AllFaces, scope, topology, ProbeBudget, TestContext.CancellationToken);
                if(lit.Outcome != ReasoningDecisionOutcome.Decided || lit.Verdict is null || lit.Verdict.IsConsistent != consistent)
                {
                    mismatches.Add(cell + ": the lit gadget faces did not decide the row's certified verdict.");
                    continue;
                }

                gadgetDecided++;
                if(lit.Statistics.ContextTotals.InferenceAttempts != 0L)
                {
                    mismatches.Add(cell + ": a gadget-decided run spent engine attempts (" + lit.Statistics.ContextTotals.InferenceAttempts + ").");
                }

                if(dark.Verdict is ModuleVerdict darkVerdict && darkVerdict.IsConsistent != consistent)
                {
                    mismatches.Add(cell + ": the dark run's own verdict disagrees with the bounded walk.");
                }
            }
        }

        TestContext.WriteLine("Gd10 verdict-identity sweep: " + gadgetDecided + " gadget cells decided pre-engine, " + partitionDecided + " partition cells unmoved, zero certified movement.");
        Assert.IsGreaterThan(0, gadgetDecided, "Gd10: the lit faces decide at least one gadget cell pre-engine — the sweep instruments a lit decider.");
        Assert.IsGreaterThan(0, partitionDecided, "Gd10: the neighbouring partition habitat still decides under the gadget-lit selection.");
        Assert.IsEmpty(mismatches, string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>
    /// The defined-atom-elimination head-to-head: every decided row runs under
    /// the production construction and under the suppressing one, and the two
    /// must agree in verdict on every fixture while the production walk never
    /// evaluates more assignments — the elimination is a construction heuristic
    /// with no soundness weight, and the whole-theory re-check on every induced
    /// assignment is what keeps the two constructions verdict-identical. The
    /// measured shrinks are pinned exactly: the refuting template's five defined
    /// atoms take its exhaustion from the raw 2^8 to the 2^3 free space, and
    /// the refuting prelude's dual-defined twin takes its exhaustion from 2^3
    /// to 2^2. The raw atom counts are construction-invariant by design.
    /// </summary>
    [TestMethod]
    public void Gd11DefinedAtomEliminationShrinksTheFreeVector()
    {
        GadgetConstruction suppressed = new(SuppressDefinedAtomElimination: true);
        foreach((string name, ReasoningModule module, bool consistent) in GadgetRows())
        {
            BooleanGadgetOutcome production = ContextBooleanGadgetDecider.Run(module);
            BooleanGadgetOutcome raw = ContextBooleanGadgetDecider.Run(module, suppressed);

            Assert.AreEqual(consistent, production.Consistent, "Gd11 " + name + ": the production construction decides the certified verdict.");
            Assert.AreEqual(consistent, raw.Consistent, "Gd11 " + name + ": the suppressing construction decides the same verdict — the elimination carries no soundness weight.");
            Assert.AreEqual(raw.Window.PropertyAtomCount, production.Window.PropertyAtomCount, "Gd11 " + name + ": the measured gadget-property atoms are raw counts, construction-invariant.");
            Assert.AreEqual(raw.Window.FreeClassAtomCount, production.Window.FreeClassAtomCount, "Gd11 " + name + ": the measured free-class atoms are raw counts, construction-invariant.");
            Assert.IsLessThanOrEqualTo(raw.Window.EvaluatedVectorCount, production.Window.EvaluatedVectorCount, "Gd11 " + name + ": the production walk never evaluates more assignments than the raw walk on a decided row.");
        }

        BooleanGadgetOutcome refutedFree = ContextBooleanGadgetDecider.Run(RefutingTemplateModule());
        BooleanGadgetOutcome refutedRaw = ContextBooleanGadgetDecider.Run(RefutingTemplateModule(), suppressed);

        Assert.AreEqual(8, refutedFree.Window.EvaluatedVectorCount, "Gd11: the refuting template's exhaustion walks the 2^3 free space under elimination.");
        Assert.AreEqual(256, refutedRaw.Window.EvaluatedVectorCount, "Gd11: the same exhaustion walks the raw 2^8 space with the elimination suppressed.");

        BooleanGadgetOutcome preludeFree = ContextBooleanGadgetDecider.Run(RefutingPreludeModule());
        BooleanGadgetOutcome preludeRaw = ContextBooleanGadgetDecider.Run(RefutingPreludeModule(), suppressed);

        Assert.AreEqual(4, preludeFree.Window.EvaluatedVectorCount, "Gd11: the refuting prelude's exhaustion walks the 2^2 free space under elimination.");
        Assert.AreEqual(8, preludeRaw.Window.EvaluatedVectorCount, "Gd11: the same exhaustion walks the raw 2^3 space with the elimination suppressed.");
    }

    /// <summary>
    /// The elimination's window headroom: a seventeen-property conjunction whose
    /// last gadget class carries a second, intersection definition compiles to
    /// seventeen raw atoms — past the bound — but sixteen surviving free atoms,
    /// so the production faces DECIDE where the suppressed construction is
    /// window-silent, and the boundary comparison provably runs over the
    /// surviving count on both sides of the seam. The raw counts and the
    /// decision ride the reasoner's statistics record.
    /// </summary>
    [TestMethod]
    public void Gd12EliminationWidensTheAtomWindow()
    {
        ReasoningModule module = WidenedConjunctionModule();
        BooleanGadgetOutcome production = ContextBooleanGadgetDecider.Run(module);

        Assert.IsTrue(production.Consistent, "Gd12: the certify face decides the seventeen-atom module — sixteen atoms survive elimination, inside the bound.");
        Assert.AreEqual(17, production.Window.PropertyAtomCount, "Gd12: the measured gadget-property atoms are the raw seventeen.");
        Assert.AreEqual(0, production.Window.FreeClassAtomCount, "Gd12: every named class is determined.");
        Assert.AreEqual(0, production.Window.AtomSilences, "Gd12: no window silence at sixteen surviving atoms.");
        Assert.AreEqual(1 << ContextBooleanGadgetDecider.GadgetAtomBound, production.Window.EvaluatedVectorCount, "Gd12: the all-true certificate sits last in the 2^16 free enumeration.");

        BooleanGadgetOutcome raw = ContextBooleanGadgetDecider.Run(module, new GadgetConstruction(SuppressDefinedAtomElimination: true));

        Assert.IsNull(raw.Consistent, "Gd12: the suppressing construction is window-silent at seventeen enumerated atoms.");
        Assert.AreEqual(1, raw.Window.AtomSilences, "Gd12: the suppressed silence is charged to the atom counter.");
        Assert.AreEqual(0, raw.Window.EvaluatedVectorCount, "Gd12: a window silence walks no assignment.");
        Assert.AreEqual(17, raw.Window.PropertyAtomCount, "Gd12: the raw measurement still lands before the suppressed boundary comparison.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, GadgetFaces, ProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Gd12: the widened window decides end-to-end through the reasoner.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Gd12: the certified verdict rides the production path.");
        Assert.AreEqual(0, totals.GadgetWindowExceededAtoms, "Gd12: no window silence on the statistics record.");
        Assert.AreEqual(1, totals.GadgetDeciderCertifications, "Gd12: the certify face's counter reads the decision.");
        Assert.AreEqual(17, totals.GadgetPropertyAtomCount, "Gd12: the raw atom measurement rides the statistics record.");
    }

    /// <summary>
    /// The elimination's degradation guards, both landing on FREE and never on
    /// silence or a wrong verdict. First, a class defined by two gadget
    /// restrictions over two properties: the pair is mutually defining, the
    /// plan demotes one atom to free and computes the other from it, and the
    /// verdict and the raw counts agree with the suppressed walk while the free
    /// space halves. Second, a class defined by two OPPOSITE gadgets over ONE
    /// property: the self-referential candidate is never evaluable, nothing is
    /// eliminated, and both constructions exhaust the identical raw space to
    /// the identical refutation — the agreement no assignment satisfies.
    /// </summary>
    [TestMethod]
    public void Gd13EliminationDegradesToFreeNeverToSilence()
    {
        GadgetConstruction suppressed = new(SuppressDefinedAtomElimination: true);
        ReasoningModule mutual = MutualGadgetPairModule();
        BooleanGadgetOutcome mutualFree = ContextBooleanGadgetDecider.Run(mutual);
        BooleanGadgetOutcome mutualRaw = ContextBooleanGadgetDecider.Run(mutual, suppressed);

        Assert.IsTrue(mutualFree.Consistent, "Gd13 MutualGadgetPair: the production construction certifies the mutually defining pair.");
        Assert.IsTrue(mutualRaw.Consistent, "Gd13 MutualGadgetPair: the suppressing construction certifies identically.");
        Assert.AreEqual(4, mutualFree.Window.EvaluatedVectorCount, "Gd13 MutualGadgetPair: one atom of the pair is demoted to free, one computed — the certificate closes the 2^2 free space.");
        Assert.AreEqual(8, mutualRaw.Window.EvaluatedVectorCount, "Gd13 MutualGadgetPair: the raw certificate closes the 2^3 space.");

        ReasoningModule contradiction = OppositeGadgetPairModule();
        BooleanGadgetOutcome contradictionFree = ContextBooleanGadgetDecider.Run(contradiction);
        BooleanGadgetOutcome contradictionRaw = ContextBooleanGadgetDecider.Run(contradiction, suppressed);

        Assert.IsFalse(contradictionFree.Consistent, "Gd13 OppositeGadgetPair: the never-satisfiable agreement refutes under production.");
        Assert.IsFalse(contradictionRaw.Consistent, "Gd13 OppositeGadgetPair: the refutation is construction-invariant.");
        Assert.AreEqual(contradictionRaw.Window.EvaluatedVectorCount, contradictionFree.Window.EvaluatedVectorCount, "Gd13 OppositeGadgetPair: the self-referential candidate eliminates nothing, so both walks exhaust the identical space.");
        Assert.AreEqual(0, contradictionFree.Window.AtomSilences, "Gd13 OppositeGadgetPair: the degradation is to enumeration, never to silence.");
    }

    /// <summary>The four decided gadget rows with their certified verdicts — the sweep's lit-face fixtures, and the battery half of the corpus head-to-head instrument's module set.</summary>
    /// <returns>The rows.</returns>
    internal static (string Name, ReasoningModule Module, bool Consistent)[] GadgetRows()
    {
        return
        [
            ("Gd1", CertifyingTemplateModule(), true),
            ("Gd2", RefutingTemplateModule(), false),
            ("Gd3", CertifyingPreludeModule(), true),
            ("Gd4", RefutingPreludeModule(), false),
        ];
    }

    /// <summary>
    /// The fourteen near-miss modules, one per named hazard: a definition cycle,
    /// a minimum bound of two, an exact bound of one, a qualified gadget filler,
    /// a gadget property carrying a characteristic, a gadget property doubling
    /// as the prelude's inner role, a prelude cap of two, a prelude anchor with
    /// a third conjunct, a prelude without its told inverse, the merge class
    /// aliased to the anchor, an ABox property assertion, a second class
    /// assertion, and the two corpus lookalike replicas.
    /// </summary>
    /// <returns>The rows.</returns>
    private static (string Name, ReasoningModule Module)[] NearMissRows()
    {
        return
        [
            ("DefinitionCycle", Module(
                Equivalent(Class("X"), Intersection(Class("Y"), Class("Z"))),
                Equivalent(Class("Y"), Intersection(Class("X"), Class("Z"))),
                Equivalent(Class("Z"), MinObject("G1", 1)),
                ClassAssertion(Class("X"), Anonymous("w")))),

            ("MinimumBoundOfTwo", Module(
                Equivalent(Class("Z"), MinObject("G1", 2)),
                Equivalent(Class("X"), Intersection(Class("Z"), Class("Free"))),
                ClassAssertion(Class("X"), Anonymous("w")))),

            ("ExactBoundOfOne", Module(
                Equivalent(Class("Z"), ExactObject("G1", 1)),
                Equivalent(Class("X"), Intersection(Class("Z"), Class("Free"))),
                ClassAssertion(Class("X"), Anonymous("w")))),

            ("QualifiedGadgetFiller", Module(
                Equivalent(Class("Z"), QualifiedMinObject("G1", 1, Class("Filler"))),
                Equivalent(Class("X"), Intersection(Class("Z"), Class("Free"))),
                ClassAssertion(Class("X"), Anonymous("w")))),

            ("GadgetPropertyCharacteristic", Module(
                Equivalent(Class("Z"), MinObject("G1", 1)),
                Equivalent(Class("X"), Intersection(Class("Z"), Class("Free"))),
                Functional("G1"),
                ClassAssertion(Class("X"), Anonymous("w")))),

            ("GadgetPropertyAsPreludeRole", Module(
                Equivalent(Class("Target"), Some("outer", Class("Anchor"))),
                Equivalent(Class("Anchor"), Intersection(Some("inner", Class("Merge")), MaxObject("inner", 1))),
                Equivalent(Class("Merge"), MinObject("inner", 1)),
                Equivalent(Class("Other"), Intersection(Class("Merge"), Class("Free"))),
                InverseProperties("outer", "inner"),
                ClassAssertion(Class("Target"), Anonymous("w")))),

            ("PreludeCapOfTwo", Module(
                Equivalent(Class("Target"), Some("outer", Class("Anchor"))),
                Equivalent(Class("Anchor"), Intersection(Some("inner", Class("Merge")), MaxObject("inner", 2))),
                Equivalent(Class("Merge"), MinObject("G1", 1)),
                Equivalent(Class("Other"), Intersection(Class("Merge"), Class("Free"))),
                InverseProperties("outer", "inner"),
                ClassAssertion(Class("Target"), Anonymous("w")))),

            ("PreludeAnchorThirdConjunct", Module(
                Equivalent(Class("Target"), Some("outer", Class("Anchor"))),
                Equivalent(Class("Anchor"), Intersection(Some("inner", Class("Merge")), MaxObject("inner", 1), Class("Extra"))),
                Equivalent(Class("Merge"), MinObject("G1", 1)),
                Equivalent(Class("Other"), Intersection(Class("Merge"), Class("Free"))),
                InverseProperties("outer", "inner"),
                ClassAssertion(Class("Target"), Anonymous("w")))),

            ("PreludeWithoutToldInverse", Module(
                Equivalent(Class("Target"), Some("outer", Class("Anchor"))),
                Equivalent(Class("Anchor"), Intersection(Some("inner", Class("Merge")), MaxObject("inner", 1))),
                Equivalent(Class("Merge"), MinObject("G1", 1)),
                Equivalent(Class("Other"), Intersection(Class("Merge"), Class("Free"))),
                ClassAssertion(Class("Target"), Anonymous("w")))),

            ("MergeClassAliasedToAnchor", Module(
                Equivalent(Class("Target"), Some("outer", Class("Anchor"))),
                Equivalent(Class("Anchor"), Intersection(Some("inner", Class("Anchor")), MaxObject("inner", 1))),
                Equivalent(Class("Merge"), MinObject("G1", 1)),
                Equivalent(Class("Other"), Intersection(Class("Merge"), Class("Free"))),
                InverseProperties("outer", "inner"),
                ClassAssertion(Class("Target"), Anonymous("w")))),

            ("AboxPropertyAssertion", Module(
                Equivalent(Class("Z"), MinObject("G1", 1)),
                Equivalent(Class("X"), Intersection(Class("Z"), Class("Free"))),
                ClassAssertion(Class("X"), Individual("x")),
                Edge("G2", "x", "y"))),

            ("SecondClassAssertion", Module(
                Equivalent(Class("Z"), MinObject("G1", 1)),
                Equivalent(Class("X"), Intersection(Class("Z"), Class("Free"))),
                ClassAssertion(Class("X"), Individual("x")),
                ClassAssertion(Class("Z"), Individual("y")))),

            ("DynamicBlockingLookalike", Module(
                Equivalent(Class("a"), MinObject("P1", 1)),
                Equivalent(Class("acomp"), MaxObject("P1", 0)),
                Equivalent(Class("A2"), Only("r", Class("V3"))),
                Equivalent(Class("V3"), Intersection(Class("a"), Class("acomp"))),
                SubClassOf(Class("Unsatisfiable"), Some("s", Class("A2"))),
                Transitive("p"),
                InverseProperties("invP", "p"),
                ClassAssertion(Class("Unsatisfiable"), Anonymous("w")))),

            ("UniversalBesideGadgetPairLookalike", Module(
                Equivalent(Class("c"), MinData("P1", 1)),
                Equivalent(Class("ccomp"), ExactData("P1", 0)),
                Equivalent(Class("D"), Intersection(Class("c"), Class("e"))),
                Equivalent(Class("E"), MinObject("P2", 1)),
                SubClassOf(Class("e"), Only("r", Class("c"))),
                ClassAssertion(Class("D"), Anonymous("w")))),
        ];
    }

    /// <summary>
    /// The reachability-gap replica: a module the gadget FACE admits whole — two
    /// gadget pairs, a free typed class held between a gadget class and its
    /// complement, one anonymous individual — but which carries NO intersection
    /// equivalence, so the census probe never fires on it.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule IntersectionFreeGadgetModule()
    {
        return Module(
            Equivalent(Class("d1"), MinObject("P2", 1)),
            Equivalent(Class("d1comp"), MaxObject("P2", 0)),
            Equivalent(Class("d"), MinData("P1", 1)),
            Equivalent(Class("dcomp"), ExactData("P1", 0)),
            SubClassOf(Class("Unsatisfiable"), Class("d1")),
            SubClassOf(Class("Unsatisfiable"), Class("d1comp")),
            ClassAssertion(Class("Unsatisfiable"), Anonymous("w")));
    }

    /// <summary>The satisfiable pure template: the gadget cluster WITHOUT the disjointness link, so the target obligation's disjunction has a surviving branch.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule CertifyingTemplateModule()
    {
        return PureTemplateModule(includeDisjointnessLink: false);
    }

    /// <summary>The refuting pure template: the same cluster WITH the disjointness link, which closes every branch of the target obligation's disjunction.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule RefutingTemplateModule()
    {
        return PureTemplateModule(includeDisjointnessLink: true);
    }

    /// <summary>
    /// The pure propositional template replica: seven gadget properties — four
    /// on the data side, three on the object side — defining seven complementary
    /// class pairs, five of whose members carry a SECOND definition as an
    /// intersection of named classes, one free class held under a defined class
    /// by a told subclass axiom, and one anonymous individual typed by the target
    /// class. With <paramref name="includeDisjointnessLink"/> the told
    /// implication between two cluster members closes the last open branch of
    /// the target obligation.
    /// </summary>
    /// <param name="includeDisjointnessLink">Whether the closing told implication is present.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule PureTemplateModule(bool includeDisjointnessLink)
    {
        List<OwlAxiom> axioms =
        [
            Equivalent(Class("C1comp"), MaxData("P1", 0)),
            Equivalent(Class("bcomp"), ExactData("P3", 0)),
            Equivalent(Class("ccomp"), ExactObject("P2", 0)),
            Equivalent(Class("Ucomp"), MinData("P5", 1)),
            Equivalent(Class("Ucomp"), Intersection(Class("C6"), Class("C7"), Class("C8"))),
            Equivalent(Class("C6comp"), MinObject("P6", 1)),
            Equivalent(Class("C6comp"), Intersection(Class("a"), Class("b"))),
            Equivalent(Class("C8"), ExactObject("P8", 0)),
            Equivalent(Class("C7comp"), ExactData("P7", 0)),
            Equivalent(Class("C7comp"), Intersection(Class("a"), Class("c"))),
            Equivalent(Class("C7"), MinData("P7", 1)),
            Equivalent(Class("C8comp"), MinObject("P8", 1)),
            Equivalent(Class("C8comp"), Intersection(Class("b"), Class("c"))),
            Equivalent(Class("C6"), MaxObject("P6", 0)),
            Equivalent(Class("U"), ExactData("P5", 0)),
            Equivalent(Class("c"), MinObject("P2", 1)),
            Equivalent(Class("b"), MinData("P3", 1)),
            Equivalent(Class("C1"), MinData("P1", 1)),
            Equivalent(Class("C1"), Intersection(Class("bcomp"), Class("ccomp"))),
            SubClassOf(Class("a"), Class("C1")),
        ];

        if(includeDisjointnessLink)
        {
            axioms.Add(SubClassOf(Class("b"), Class("ccomp")));
        }

        axioms.Add(ClassAssertion(Class("U"), Anonymous("w")));

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>
    /// The satisfiable prelude replica: the target class defined as the BARE
    /// existential into the anchor, the anchor as the merge existential beside
    /// its at-most-one cap, a told inverse linking the two roles, and a
    /// propositional core of four gadget properties, one dual-defined class, one
    /// free class, and two told implications the forced merge leaves satisfiable.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule CertifyingPreludeModule()
    {
        return Module(
            Equivalent(Class("Target"), Some("outer", Class("Anchor"))),
            Equivalent(Class("Anchor"), Intersection(Some("inner", Class("Merge")), MaxObject("inner", 1))),
            InverseProperties("outer", "inner"),
            Equivalent(Class("Merge"), MinObject("G1", 1)),
            Equivalent(Class("Mergecomp"), MaxObject("G1", 0)),
            Equivalent(Class("Pair"), MinObject("G2", 1)),
            Equivalent(Class("Paircomp"), ExactObject("G2", 0)),
            Equivalent(Class("Flag"), MinData("D1", 1)),
            Equivalent(Class("Flagcomp"), ExactData("D1", 0)),
            Equivalent(Class("Joint"), MinObject("G3", 1)),
            Equivalent(Class("Joint"), Intersection(Class("Merge"), Class("Flag"))),
            SubClassOf(Class("Free"), Class("Pair")),
            SubClassOf(Class("Merge"), Class("Flag")),
            ClassAssertion(Class("Target"), Anonymous("w")));
    }

    /// <summary>
    /// The refuting prelude replica: the target class defined as a named
    /// conjunct BESIDE the existential, where the conjunct denies the very
    /// property atom the merge class asserts — so the obligation is
    /// unsatisfiable and the walk exhausts its three-atom space.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule RefutingPreludeModule()
    {
        return Module(
            Equivalent(Class("Target"), Intersection(Class("Blocker"), Some("outer", Class("Anchor")))),
            Equivalent(Class("Anchor"), Intersection(Some("inner", Class("Merge")), MaxObject("inner", 1))),
            InverseProperties("outer", "inner"),
            Equivalent(Class("Merge"), MinObject("G1", 1)),
            Equivalent(Class("Blocker"), MaxObject("G1", 0)),
            Equivalent(Class("Twin"), ExactObject("G2", 0)),
            Equivalent(Class("Twin"), Intersection(Class("Merge"), Class("Blocker"))),
            SubClassOf(Class("Free"), Class("Merge")),
            ClassAssertion(Class("Target"), Anonymous("w")));
    }

    /// <summary>
    /// Builds a boundary module of <paramref name="atomCount"/> gadget
    /// properties: one class per property, each defined by the minimum-of-one
    /// gadget or — with <paramref name="negated"/> — by the maximum-of-zero
    /// gadget, all conjoined by one named intersection the typed individual
    /// carries. The negated form is satisfied by the all-false assignment, so it
    /// decides at the first step; the plain form forces every atom true.
    /// </summary>
    /// <param name="atomCount">The gadget properties, and so the compiled atoms.</param>
    /// <param name="negated">Whether each class is defined by the denying gadget.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ConjunctionModule(int atomCount, bool negated)
    {
        List<OwlAxiom> axioms = [];
        List<OwlClassExpression> conjuncts = [];
        for(int i = 1; i <= atomCount; i++)
        {
            axioms.Add(Equivalent(Class("C" + i), negated ? MaxObject("P" + i, 0) : MinObject("P" + i, 1)));
            conjuncts.Add(Class("C" + i));
        }

        axioms.Add(Equivalent(Class("Target"), new OwlObjectIntersectionOf([.. conjuncts])));
        axioms.Add(ClassAssertion(Class("Target"), Anonymous("w")));

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>Builds a contradicting boundary module of <paramref name="atomCount"/> gadget properties: the conjunction of every asserting gadget together with the denying gadget over the first property, so no assignment passes and the walk exhausts the whole space.</summary>
    /// <param name="atomCount">The gadget properties, and so the compiled atoms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ContradictionModule(int atomCount)
    {
        List<OwlAxiom> axioms = [];
        List<OwlClassExpression> conjuncts = [];
        for(int i = 1; i <= atomCount; i++)
        {
            axioms.Add(Equivalent(Class("C" + i), MinObject("P" + i, 1)));
            conjuncts.Add(Class("C" + i));
        }

        axioms.Add(Equivalent(Class("Zero"), MaxObject("P1", 0)));
        conjuncts.Add(Class("Zero"));
        axioms.Add(Equivalent(Class("Target"), new OwlObjectIntersectionOf([.. conjuncts])));
        axioms.Add(ClassAssertion(Class("Target"), Anonymous("w")));

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>Builds the widened-window module: seventeen asserting gadget properties conjoined by the typed target class, with the seventeenth gadget class carrying a SECOND definition as the intersection of the first two — seventeen raw atoms, sixteen surviving free ones, satisfied only by the all-true assignment.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule WidenedConjunctionModule()
    {
        List<OwlAxiom> axioms = [];
        List<OwlClassExpression> conjuncts = [];
        for(int i = 1; i <= ContextBooleanGadgetDecider.GadgetAtomBound + 1; i++)
        {
            axioms.Add(Equivalent(Class("C" + i), MinObject("P" + i, 1)));
            conjuncts.Add(Class("C" + i));
        }

        axioms.Add(Equivalent(Class("C" + (ContextBooleanGadgetDecider.GadgetAtomBound + 1)), Intersection(Class("C1"), Class("C2"))));
        axioms.Add(Equivalent(Class("Target"), new OwlObjectIntersectionOf([.. conjuncts])));
        axioms.Add(ClassAssertion(Class("Target"), Anonymous("w")));

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>Builds the mutually defining pair: one class equivalent to the asserting gadget over each of two properties — the agreement pins the two atoms equal, so the plan demotes one to free and computes the other — held under the typed intersection beside a free class.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule MutualGadgetPairModule()
    {
        return Module(
            Equivalent(Class("A"), MinObject("P", 1)),
            Equivalent(Class("A"), MinObject("Q", 1)),
            Equivalent(Class("X"), Intersection(Class("A"), Class("Free"))),
            ClassAssertion(Class("X"), Anonymous("w")));
    }

    /// <summary>Builds the opposite pair: one class equivalent to the asserting AND the denying gadget over ONE property — an agreement no assignment satisfies, whose self-referential definer candidate is never evaluable, so nothing is eliminated and both constructions refute over the identical space.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule OppositeGadgetPairModule()
    {
        return Module(
            Equivalent(Class("A"), MinObject("P", 1)),
            Equivalent(Class("A"), MaxObject("P", 0)),
            Equivalent(Class("X"), Intersection(Class("A"))),
            ClassAssertion(Class("X"), Anonymous("w")));
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

    /// <summary>A named data property in the example namespace.</summary>
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

    /// <summary>An existential restriction over a named forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>A universal restriction over a named forward role — the modal shape the jurisdiction refuses outside the prelude.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectAllValuesFrom Only(string property, OwlClassExpression filler)
    {
        return new OwlObjectAllValuesFrom(Property(property), filler);
    }

    /// <summary>An unqualified minimum-cardinality restriction over a named object property.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality MinObject(string property, int cardinality)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Min, cardinality, Property(property), Filler: null);
    }

    /// <summary>An unqualified maximum-cardinality restriction over a named object property.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality MaxObject(string property, int cardinality)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), Filler: null);
    }

    /// <summary>An unqualified exact-cardinality restriction over a named object property.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality ExactObject(string property, int cardinality)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Exact, cardinality, Property(property), Filler: null);
    }

    /// <summary>A QUALIFIED minimum-cardinality restriction over a named object property — the counting shape the gadget jurisdiction refuses.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <param name="filler">The qualifying filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality QualifiedMinObject(string property, int cardinality, OwlClassExpression filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Min, cardinality, Property(property), filler);
    }

    /// <summary>An unqualified minimum-cardinality restriction over a named data property.</summary>
    /// <param name="property">The property's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataCardinality MinData(string property, int cardinality)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Min, cardinality, DataProperty(property), Range: null);
    }

    /// <summary>An unqualified maximum-cardinality restriction over a named data property.</summary>
    /// <param name="property">The property's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataCardinality MaxData(string property, int cardinality)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Max, cardinality, DataProperty(property), Range: null);
    }

    /// <summary>An unqualified exact-cardinality restriction over a named data property.</summary>
    /// <param name="property">The property's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataCardinality ExactData(string property, int cardinality)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Exact, cardinality, DataProperty(property), Range: null);
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

    /// <summary>A told inverse between two named object properties — the prelude's linking axiom.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlInverseObjectPropertiesAxiom InverseProperties(string first, string second)
    {
        return new OwlInverseObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("inverse") };
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

    /// <summary>A transitivity characteristic over a named role in the example namespace.</summary>
    /// <param name="role">The role's local name.</param>
    /// <returns>The characteristic axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Transitive(string role)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Transitive, Property(role)) { Origin = Origin("transitive") };
    }
}
