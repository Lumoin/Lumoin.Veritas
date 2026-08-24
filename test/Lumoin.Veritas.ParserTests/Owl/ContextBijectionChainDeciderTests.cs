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
/// The bijection-chain cardinality habitat decider's battery: the propagating
/// refutations across every told arithmetic route the jurisdiction admits — the
/// corpus chain template's constant against its own disjoint-union sum, the
/// mismatched tower's fan-in against its fiber product, the singleton
/// constants, the negative sum residue, and the bounds clashing with a forced
/// constant and with a told-distinct member count — both certificate routes on
/// their corpus shapes, the two strict-arm entailment-probe shapes decided
/// pre-engine, the nineteen near-miss silences one per named hazard, the
/// class-window silence, the long-arithmetic overflow pin, the window
/// derivation, the census signal's role linkage as a minimal pair, the explicit
/// dark control with its census ride, and the
/// verdict-identity sweep. Every row drives the production seams — the
/// faces-carrying reasoner overload or the decider's own measurement surface —
/// and every counter the battery reads is consumed by an assert.
/// </summary>
[TestClass]
internal sealed class ContextBijectionChainDeciderTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the battery's classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/bijectionchaincsp#";

    /// <summary>Both bijection-chain faces lit — the selection the deciding rows drive.</summary>
    private const EnumerationDeciderFaces BijectionChainFaces = EnumerationDeciderFaces.BijectionChainClash | EnumerationDeciderFaces.BijectionChainCertify;

    /// <summary>Every decider face the recognizer's registry lights, read from the production fold — the selection the verdict-identity sweep runs against the explicit dark control.</summary>
    private static EnumerationDeciderFaces AllFaces { get; } = ContextHabitatRecognizer.EveryFaceLit;

    /// <summary>The clash reason family's arithmetic leading identifier.</summary>
    private const string ForcedConstantConflict = "BijectionChainForcedConstantConflict(";

    /// <summary>The clash reason family's asserted-conjunct leading identifier.</summary>
    private const string UnsatisfiableAssertedConjunct = "BijectionChainUnsatisfiableAssertedConjunct(";

    /// <summary>The bounded budget the silence rows drive: enough for the engine to fire rules on a bijection-chain module, far below what its saturation would need.</summary>
    private static ReasoningBudget ProbeBudget { get; } = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 4096);

    /// <summary>
    /// The corpus chain template told exactly as the premise spells it: eight
    /// roles each told functional AND inverse-functional, four told inverse
    /// pairs, the four paired existential chains that biject all five named
    /// classes onto one another, the anchor class equated to a three-member
    /// enumeration whose members are told pairwise different, the union class
    /// equated to the disjoint union of two chain members, and the seven told
    /// disjointness edges. The one equality class carries the constant three and,
    /// through the sum over its own members, six: the propagation refutes with
    /// zero inference attempts and no engine. The habitat-label assert doubles as
    /// the positive reachability pin — a nominal module every earlier probe
    /// declines must still reach the bijection-chain probe.
    /// </summary>
    [TestMethod]
    public void Bc1CorpusChainTemplateRefutesByForcedConstantConflict()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CorpusChainTemplateModule(), BijectionChainFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Bc1 CorpusChainTemplate: the clash face decides the template at the production ceiling.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Bc1 CorpusChainTemplate: one set cannot have both three and six elements, so no model exists.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Bc1 CorpusChainTemplate: a pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(0, totals.ContextsCreated, "Bc1 CorpusChainTemplate: no engine was constructed — the seat is upstream of every engine axis.");
        Assert.AreEqual(EnumerationHabitatClass.BijectionChainArithmetic, totals.EnumerationHabitat, "Bc1 CorpusChainTemplate: a nominal module every earlier probe declines still reaches the bijection-chain probe and is labelled Shape B.");
        Assert.AreEqual(1, totals.BijectionChainDeciderClashes, "Bc1 CorpusChainTemplate: the clash face's counter reads the decision.");
        Assert.AreEqual(0, totals.BijectionChainDeciderCertifications, "Bc1 CorpusChainTemplate: a refuted module takes no certificate.");
        Assert.AreEqual(5, totals.BijectionChainClassCount, "Bc1 CorpusChainTemplate: the five named classes are the measured size variables.");
        Assert.IsGreaterThan(0, totals.BijectionChainConstraintCount, "Bc1 CorpusChainTemplate: the collected constraint sources ride the measurement.");
        Assert.AreEqual(0, totals.BijectionChainWindowExceededClasses, "Bc1 CorpusChainTemplate: five variables sit well inside the class window.");

        BijectionChainOutcome outcome = ContextBijectionChainDecider.Run(CorpusChainTemplateModule());

        Assert.StartsWith(ForcedConstantConflict, outcome.ClashReason, StringComparison.Ordinal, "Bc1 CorpusChainTemplate: the clash reason names the arithmetic conflict.");
        Assert.IsNull(outcome.CertificateRoute, "Bc1 CorpusChainTemplate: a refutation names no certificate route.");
    }

    /// <summary>
    /// The grounded-tower certificate: the corpus diamond template, whose anchor
    /// is a singleton enumeration counted twice over one told inverse and six
    /// times over another, whose mid level is the anchor's functional
    /// predecessor set counted three times over its own told inverse, and whose
    /// top level fibres over both. Six equals two times three, so the canonical
    /// fiber model witnesses the whole module and the certify face decides it
    /// consistent pre-engine. The anchor's member additionally carries the
    /// <c>owl:Thing</c> typing the corpus premise's <c>&lt;owl:Thing rdf:ID&gt;</c>
    /// element spelling maps to — an assertion that asks only for a domain
    /// element and so rides both certificate routes as passthrough.
    /// </summary>
    [TestMethod]
    public void Bc2GroundedTowerCertifiesByFiberArithmetic()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(GroundedTowerModule(6), BijectionChainFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Bc2 GroundedTower: the certify face decides the diamond template.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Bc2 GroundedTower: the canonical fiber model satisfies every told axiom.");
        Assert.IsEmpty(decision.Verdict.Subsumptions, "Bc2 GroundedTower: the closed-form certificate claims no subsumption set.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Bc2 GroundedTower: a pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(0, totals.ContextsCreated, "Bc2 GroundedTower: no engine was constructed.");
        Assert.AreEqual(EnumerationHabitatClass.BijectionChainArithmetic, totals.EnumerationHabitat, "Bc2 GroundedTower: the tower is labelled Shape B.");
        Assert.AreEqual(1, totals.BijectionChainDeciderCertifications, "Bc2 GroundedTower: the certify face's counter reads the decision.");
        Assert.AreEqual(0, totals.BijectionChainDeciderClashes, "Bc2 GroundedTower: the arithmetic is consistent, so no clash counter moves.");

        BijectionChainOutcome outcome = ContextBijectionChainDecider.Run(GroundedTowerModule(6));

        Assert.AreEqual("GroundedTower", outcome.CertificateRoute, "Bc2 GroundedTower: the grounded-tower route names the certificate.");
        Assert.IsNull(outcome.ClashReason, "Bc2 GroundedTower: a certificate names no clash reason.");
    }

    /// <summary>
    /// The vacuity certificate: the corpus chain premise without any
    /// nonemptiness forcer — eight functional and inverse-functional roles, four
    /// told inverse pairs, the four paired existential chains, seven
    /// disjointness edges, the disjoint-union equivalence, the
    /// subclass-position union of singleton enumerations that only bounds, and
    /// three bare individual declarations. Every named class denotes the empty
    /// set and every role the empty relation in a model of the whole module, so
    /// the certify face decides it consistent pre-engine.
    /// </summary>
    [TestMethod]
    public void Bc3VacuityCertifiesTheEmptyModel()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(VacuityChainModule(), BijectionChainFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Bc3 Vacuity: the certify face decides the chain premise.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Bc3 Vacuity: the all-empty interpretation satisfies every whitelisted axiom.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Bc3 Vacuity: a pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(EnumerationHabitatClass.BijectionChainArithmetic, totals.EnumerationHabitat, "Bc3 Vacuity: the chain premise is labelled Shape B.");
        Assert.AreEqual(1, totals.BijectionChainDeciderCertifications, "Bc3 Vacuity: the certify face's counter reads the decision.");
        Assert.AreEqual(0, totals.BijectionChainDeciderClashes, "Bc3 Vacuity: the self-sum collapses to zero inside its told bound, so nothing clashes.");
        Assert.AreEqual("Vacuity", ContextBijectionChainDecider.Run(VacuityChainModule()).CertificateRoute, "Bc3 Vacuity: the vacuity route names the certificate.");
    }

    /// <summary>
    /// The pure-TBox tower: three functional roles with told inverse partners,
    /// told domains and ranges, and five equivalences whose exact counts never
    /// ground against an anchor, with no individual and no enumeration anywhere.
    /// No size constant exists, so the propagation is silent — and the same
    /// all-empty model certifies the module, since the exact counts are all at
    /// least one and evaluate empty under empty relations. The row additionally
    /// exercises the NOMINAL-FREE recognizer path.
    /// </summary>
    [TestMethod]
    public void Bc4PureTboxTowerVacuityCertifies()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(PureTboxTowerModule(), BijectionChainFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Bc4 PureTboxTower: the certify face decides the pure-TBox tower.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Bc4 PureTboxTower: the all-empty interpretation satisfies every told axiom.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Bc4 PureTboxTower: a pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(EnumerationHabitatClass.BijectionChainArithmetic, totals.EnumerationHabitat, "Bc4 PureTboxTower: a nominal-FREE counting module the gadget and partition probes both decline reaches the bijection-chain probe.");
        Assert.AreEqual(1, totals.BijectionChainDeciderCertifications, "Bc4 PureTboxTower: the certify face's counter reads the decision.");
        Assert.AreEqual(0, totals.BijectionChainDeciderClashes, "Bc4 PureTboxTower: with no anchor there is no constant to conflict.");
        Assert.AreEqual("Vacuity", ContextBijectionChainDecider.Run(PureTboxTowerModule()).CertificateRoute, "Bc4 PureTboxTower: the vacuity route names the certificate.");
    }

    /// <summary>The mismatched tower: the diamond template whose top-level count is seven rather than the product of its two lower levels. The anchored fan-in forces seven while the fiber product forces six — the monotone clash decides it, and the certify pass is never consulted.</summary>
    [TestMethod]
    public void Bc5MismatchedTowerRefutes()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(GroundedTowerModule(7), BijectionChainFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Bc5 MismatchedTower: the clash face decides the mismatched tower.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Bc5 MismatchedTower: the top level cannot be both seven and six.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Bc5 MismatchedTower: the refutation is pre-engine.");
        Assert.AreEqual(1, totals.BijectionChainDeciderClashes, "Bc5 MismatchedTower: the clash face's counter reads the decision.");
        Assert.AreEqual(0, totals.BijectionChainDeciderCertifications, "Bc5 MismatchedTower: the clash face runs first, so no certificate is taken.");
        Assert.StartsWith(ForcedConstantConflict, ContextBijectionChainDecider.Run(GroundedTowerModule(7)).ClashReason, StringComparison.Ordinal, "Bc5 MismatchedTower: the clash reason names the arithmetic conflict.");
    }

    /// <summary>Singleton enumerations pin their constants unconditionally, with no distinctness axiom anywhere: one class of exactly one element is told to be the disjoint union of two classes of exactly one element each, so one must equal two.</summary>
    [TestMethod]
    public void Bc6SingletonOneOfsPinConstantsWithoutAllDifferent()
    {
        BijectionChainOutcome outcome = ContextBijectionChainDecider.Run(SingletonSumModule());

        Assert.IsFalse(outcome.Consistent, "Bc6 SingletonOneOfs: a singleton enumeration pins its size with no told distinctness needed.");
        Assert.StartsWith(ForcedConstantConflict, outcome.ClashReason, StringComparison.Ordinal, "Bc6 SingletonOneOfs: the clash reason names the arithmetic conflict.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(SingletonSumModule(), BijectionChainFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.IsFalse(decision.Verdict!.IsConsistent, "Bc6 SingletonOneOfs: the reasoner carries the same refutation.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.BijectionChainDeciderClashes, "Bc6 SingletonOneOfs: the clash face's counter reads the decision.");
    }

    /// <summary>
    /// The near-miss sub-checks: nineteen perturbations, each of which must
    /// leave BOTH faces silent — a dropped distinctness, inverse linkage,
    /// functionality, inverse-functionality, or same-role characteristic pairing
    /// that each break a bijection premise; a dropped disjointness that breaks a
    /// sum; a qualified and a data-side cardinality that break a fan-in; the six
    /// vacuity leaks and the four tower leaks that each break a certificate's
    /// model construction; the mixed vocabulary that fits neither route's own
    /// list; an anonymous enumeration member; and a self-sum with no finite
    /// bound, which infinite models satisfy. Every row is built so that a leak in
    /// the named guard would VERDICT it, so silence is the whole assertion. The
    /// faces are read directly and through the reasoner: neither counter may move
    /// on any row.
    /// </summary>
    [TestMethod]
    public void Bc7NearMissRowsStaySilent()
    {
        foreach((string name, ReasoningModule module) in NearMissRows())
        {
            BijectionChainOutcome outcome = ContextBijectionChainDecider.Run(module);

            Assert.IsNull(outcome.Consistent, "Bc7 " + name + ": both faces must stay silent on the near miss.");
            Assert.IsNull(outcome.ClashReason, "Bc7 " + name + ": a silent clash face names no reason.");
            Assert.IsNull(outcome.CertificateRoute, "Bc7 " + name + ": a silent certify face names no route.");

            ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, BijectionChainFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

            Assert.AreEqual(0, totals.BijectionChainDeciderClashes, "Bc7 " + name + ": no clash decision on the near miss.");
            Assert.AreEqual(0, totals.BijectionChainDeciderCertifications, "Bc7 " + name + ": no certificate on the near miss.");
        }
    }

    /// <summary>The negative sum residue: a class of exactly one element is the disjoint union of a two-element class and a second class, so the residue is minus one — no cardinal solves it.</summary>
    [TestMethod]
    public void Bc8NegativeSumResidueRefutes()
    {
        BijectionChainOutcome outcome = ContextBijectionChainDecider.Run(NegativeResidueModule());

        Assert.IsFalse(outcome.Consistent, "Bc8 NegativeSumResidue: a disjoint operand larger than the whole union refutes the module.");
        Assert.StartsWith(ForcedConstantConflict, outcome.ClashReason, StringComparison.Ordinal, "Bc8 NegativeSumResidue: the clash reason names the arithmetic conflict.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(NegativeResidueModule(), BijectionChainFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.IsFalse(decision.Verdict!.IsConsistent, "Bc8 NegativeSumResidue: the reasoner carries the same refutation.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.BijectionChainDeciderClashes, "Bc8 NegativeSumResidue: the clash face's counter reads the decision.");
    }

    /// <summary>The told upper bound against a forced constant: four pairwise-different enumerated members force a size of four, while a subclass-position union of three singleton enumerations admits at most three.</summary>
    [TestMethod]
    public void Bc9UpperBoundClashesWithForcedConstant()
    {
        BijectionChainOutcome outcome = ContextBijectionChainDecider.Run(UpperBoundModule());

        Assert.IsFalse(outcome.Consistent, "Bc9 UpperBound: four told-distinct members cannot fit a three-element upper bound.");
        Assert.StartsWith(ForcedConstantConflict, outcome.ClashReason, StringComparison.Ordinal, "Bc9 UpperBound: the clash reason names the arithmetic conflict.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(UpperBoundModule(), BijectionChainFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.IsFalse(decision.Verdict!.IsConsistent, "Bc9 UpperBound: the reasoner carries the same refutation.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.BijectionChainDeciderClashes, "Bc9 UpperBound: the clash face's counter reads the decision.");
    }

    /// <summary>The certify jurisdiction is WHOLE-MODULE: the vacuity premise plus one axiom outside the whitelist — a told transitivity characteristic — takes no certificate, while the clash face stays silent too, so ordinary saturation owns the module.</summary>
    [TestMethod]
    public void Bc10CertifyNeverFiresWithoutWholeModuleAdmission()
    {
        BijectionChainOutcome outcome = ContextBijectionChainDecider.Run(TransitiveExtraModule());

        Assert.IsNull(outcome.Consistent, "Bc10 WholeModuleAdmission: one unwhitelisted axiom leaves the certify face silent.");
        Assert.IsNull(outcome.CertificateRoute, "Bc10 WholeModuleAdmission: a silent certify face names no route.");
        Assert.IsNull(outcome.ClashReason, "Bc10 WholeModuleAdmission: the arithmetic is silent too.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(TransitiveExtraModule(), BijectionChainFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(0, totals.BijectionChainDeciderCertifications, "Bc10 WholeModuleAdmission: no certificate on a module the whitelist rejects.");
        Assert.AreEqual(0, totals.BijectionChainDeciderClashes, "Bc10 WholeModuleAdmission: no clash either.");
    }

    /// <summary>The propagation runs in long arithmetic with a pre-multiply guard: a fan-in chain whose third level would multiply past the long range charges a silence instead of grounding a wrapped value, and the window measurement still rides the outcome.</summary>
    [TestMethod]
    public void Bc11LongArithmeticGuardsTheOverflow()
    {
        BijectionChainOutcome outcome = ContextBijectionChainDecider.Run(OverflowChainModule());

        Assert.IsNull(outcome.Consistent, "Bc11 LongArithmetic: a product past the long range charges a silence, never a verdict.");
        Assert.IsNull(outcome.ClashReason, "Bc11 LongArithmetic: no clash, no reason.");
        Assert.IsNull(outcome.CertificateRoute, "Bc11 LongArithmetic: the enumeration equivalence keeps the vacuity route out, so no certificate either.");
        Assert.AreEqual(4, outcome.Window.ClassCount, "Bc11 LongArithmetic: the four levels are measured despite the silence.");
        Assert.AreEqual(0, outcome.Window.ClassSilences, "Bc11 LongArithmetic: the silence is the overflow guard's, not the class window's.");
        Assert.IsGreaterThan(0, outcome.Window.ConstraintCount, "Bc11 LongArithmetic: the collected sources ride the silent measurement.");
    }

    /// <summary>
    /// The class-window silence charges its named counter, with the measured
    /// numbers landing BEFORE the boundary comparison: one variable past the
    /// bound leaves both faces silent even though the module's own arithmetic
    /// clashes outright, so silence here is the window doing its work, not a
    /// coincidence of the numbers.
    /// </summary>
    [TestMethod]
    public void Bc12SeventeenClassesChargeTheClassWindowSilence()
    {
        int overflow = ContextBijectionChainDecider.BijectionChainClassBound + 1;
        ReasoningModule module = ClassWindowModule(overflow);
        BijectionChainOutcome outcome = ContextBijectionChainDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Bc12 ClassWindow: both faces are silent past the class bound.");
        Assert.AreEqual(overflow, outcome.Window.ClassCount, "Bc12 ClassWindow: the measured variable count is reported past the bound.");
        Assert.AreEqual(1, outcome.Window.ClassSilences, "Bc12 ClassWindow: the silence is charged to the class counter.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, BijectionChainFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(1, totals.BijectionChainWindowExceededClasses, "Bc12 ClassWindow: the window silence rides the statistics record.");
        Assert.AreEqual(overflow, totals.BijectionChainClassCount, "Bc12 ClassWindow: the measured variables ride the statistics record.");
        Assert.AreEqual(0, totals.BijectionChainDeciderClashes, "Bc12 ClassWindow: no clash past the class bound.");
        Assert.AreEqual(0, totals.BijectionChainDeciderCertifications, "Bc12 ClassWindow: no certificate past the class bound.");
    }

    /// <summary>
    /// The verdict-identity sweep: every nominal-battery row and every certified
    /// partition-battery row decided under the explicit dark control and under
    /// every lit face, across both paramodulation scopes and both root-tier
    /// topologies, must be identical in outcome, verdict, subsumption set, and
    /// attempt count. No bijection-chain face may claim a row of either
    /// neighbouring habitat, and — the census guard — no such row may take the
    /// Shape B label either: the new probe answers LAST on both paths, so an
    /// existing classification moving is a probe-placement leak. The deciding
    /// bijection-chain rows ride the same matrix: the lit run decides each one
    /// pre-engine with zero attempts in every cell.
    /// </summary>
    [TestMethod]
    public void Bc13LitFacesMoveNoVerdictAcrossTheMatrix()
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
                if(litTotals.BijectionChainDeciderClashes + litTotals.BijectionChainDeciderCertifications > 0)
                {
                    mismatches.Add(cell + ": a nominal-battery row was claimed by a bijection-chain face.");
                    continue;
                }

                if(litTotals.EnumerationHabitat == EnumerationHabitatClass.BijectionChainArithmetic)
                {
                    mismatches.Add(cell + ": a nominal-battery row's census label moved to Shape B.");
                    continue;
                }

                if(litTotals.EnumerationHabitat != dark.Statistics.ContextTotals.EnumerationHabitat)
                {
                    mismatches.Add(cell + ": the census label moved between the dark and lit runs.");
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
                if(litTotals.BijectionChainDeciderClashes + litTotals.BijectionChainDeciderCertifications > 0)
                {
                    mismatches.Add(cell + ": a partition-battery row was claimed by a bijection-chain face.");
                    continue;
                }

                if(litTotals.EnumerationHabitat == EnumerationHabitatClass.BijectionChainArithmetic)
                {
                    mismatches.Add(cell + ": a partition-battery row's census label moved to Shape B.");
                    continue;
                }

                if(lit.Outcome != ReasoningDecisionOutcome.Decided || lit.Verdict is null || lit.Verdict.IsConsistent != consistent)
                {
                    mismatches.Add(cell + ": the partition row lost its certified verdict under the bijection-chain-lit faces.");
                    continue;
                }

                partitionDecided++;
            }
        }

        int bijectionChainDecided = 0;
        foreach((string name, ReasoningModule module, bool consistent) in BijectionChainRows())
        {
            foreach((NominalParamodulationScope scope, RootContextTopology topology) in cells)
            {
                string cell = name + "@" + scope + "/" + topology;
                ModuleDecision lit = ContextSaturationModuleReasoner.DecideModule(module, AllFaces, scope, topology, ProbeBudget, TestContext.CancellationToken);
                if(lit.Outcome != ReasoningDecisionOutcome.Decided || lit.Verdict is null || lit.Verdict.IsConsistent != consistent)
                {
                    mismatches.Add(cell + ": the lit bijection-chain faces did not decide the row.");
                    continue;
                }

                bijectionChainDecided++;
                if(lit.Statistics.ContextTotals.InferenceAttempts != 0L)
                {
                    mismatches.Add(cell + ": a bijection-chain-decided run spent engine attempts (" + lit.Statistics.ContextTotals.InferenceAttempts + ").");
                }
            }
        }

        TestContext.WriteLine("Bc13 verdict-identity sweep: " + bijectionChainDecided + " bijection-chain cells decided pre-engine, " + partitionDecided + " partition cells unmoved, zero certified movement.");
        Assert.IsGreaterThan(0, bijectionChainDecided, "Bc13: the lit faces decide at least one bijection-chain cell pre-engine — the sweep instruments a lit decider.");
        Assert.IsGreaterThan(0, partitionDecided, "Bc13: the neighbouring partition habitat still decides under the bijection-chain-lit selection.");
        Assert.IsEmpty(mismatches, string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>
    /// The dark control: under the explicit
    /// <see cref="EnumerationDeciderFaces.None"/> selection the corpus chain
    /// template keeps the engine-face budget abstention — the abstained outcome,
    /// no verdict, the inclusive ceiling spent, a genuine saturation behind it —
    /// and the census still ships: the habitat label and both measured numbers
    /// are on the record while neither decision counter moved.
    /// </summary>
    [TestMethod]
    public void Bc14DarkFacesKeepTheAbstentionByteIdenticalAndCensusRides()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CorpusChainTemplateModule(), EnumerationDeciderFaces.None, ProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "Bc14 DarkFaces: the template abstains with both faces dark.");
        Assert.IsNull(decision.Verdict, "Bc14 DarkFaces: the dark abstention carries no verdict.");
        Assert.AreEqual((long)ProbeBudget.MaxInferences, totals.InferenceAttempts, "Bc14 DarkFaces: the dark run spends exactly the inclusive ceiling.");
        Assert.IsGreaterThan(0L, totals.RuleApplications, "Bc14 DarkFaces: the dark exhaust is an admitted saturation, not a non-admission.");
        Assert.AreEqual(EnumerationHabitatClass.BijectionChainArithmetic, totals.EnumerationHabitat, "Bc14 DarkFaces: the habitat label rides the dark abstention record.");
        Assert.AreEqual(5, totals.BijectionChainClassCount, "Bc14 DarkFaces: the size variables are measured dark.");
        Assert.IsGreaterThan(0, totals.BijectionChainConstraintCount, "Bc14 DarkFaces: the constraint sources are measured dark.");
        Assert.AreEqual(0, totals.BijectionChainWindowExceededClasses, "Bc14 DarkFaces: no window silence dark at five variables.");
        Assert.AreEqual(0, totals.BijectionChainDeciderClashes, "Bc14 DarkFaces: no clash decision with the faces dark.");
        Assert.AreEqual(0, totals.BijectionChainDeciderCertifications, "Bc14 DarkFaces: no certificate with the faces dark.");
    }

    /// <summary>
    /// The window-constant derivation pin: the size-variable ceiling sits on the
    /// counting faces' shared sixteen boundary discipline — equal by value to the
    /// counted-population, ground-clique, partition-anchor, gadget-atom,
    /// pair-assignment, and spy-point member ceilings — and a module sitting
    /// exactly AT the bound still decides, so the boundary is inclusive and the
    /// silence begins one variable later.
    /// </summary>
    [TestMethod]
    public void Bc15WindowConstantDerivation()
    {
        BijectionChainOutcome atBound = ContextBijectionChainDecider.Run(ClassWindowModule(ContextBijectionChainDecider.BijectionChainClassBound));

        Assert.IsFalse(atBound.Consistent, "Bc15 WindowConstant: the clash face decides AT the class bound — the bijected pair still carries two different constants.");
        Assert.AreEqual(ContextBijectionChainDecider.BijectionChainClassBound, atBound.Window.ClassCount, "Bc15 WindowConstant: the measured variable ceiling is the face's own bound.");
        Assert.AreEqual(ContextNominalCountingDecider.CountedPopulationBound, atBound.Window.ClassCount, "Bc15 WindowConstant: the measured variable ceiling shares the counted-population bound — one boundary discipline across the pre-engine faces.");
        Assert.AreEqual(ContextClausifier.GroundCountingCliqueBound, atBound.Window.ClassCount, "Bc15 WindowConstant: the measured variable ceiling shares the ground rider's clique bound.");
        Assert.AreEqual(ContextPartitionCountingDecider.PartitionAnchorBound, atBound.Window.ClassCount, "Bc15 WindowConstant: the measured variable ceiling shares the partition faces' anchor bound.");
        Assert.AreEqual(ContextBooleanGadgetDecider.GadgetAtomBound, atBound.Window.ClassCount, "Bc15 WindowConstant: the measured variable ceiling shares the gadget faces' atom bound.");
        Assert.AreEqual(ContextEnumerationAlgebraDecider.PairAssignmentBound, atBound.Window.ClassCount, "Bc15 WindowConstant: the measured variable ceiling shares the pair-composition bound.");
        Assert.AreEqual(ContextSpyPointDecider.SpyPointMemberBound, atBound.Window.ClassCount, "Bc15 WindowConstant: the measured variable ceiling shares the spy-point member bound.");
        Assert.AreEqual(0, atBound.Window.ClassSilences, "Bc15 WindowConstant: no window silence exactly at the bound.");
    }

    /// <summary>
    /// The strict arm's refutation-probe shape decided pre-engine: the vacuity
    /// premise plus the EXACT axiom the shared refutation builders emit for a
    /// <c>SubClassOf(C, owl:Nothing)</c> conclusion — a class assertion of
    /// <c>C ⊓ ¬owl:Nothing</c> on one shared NAMED witness individual. The
    /// wrapper is a no-op, so it lower-bounds the class at one, while the one
    /// equality class collapses to zero under the premise's told three-element
    /// upper bound: the propagation refutes, which is exactly what the
    /// entailment arm needs. The probe shape is the
    /// <c>Refutations()</c> / <c>ContextRefutations()</c> switch's
    /// <c>Counterexample(Overlap(SubClass, ComplementOf(SuperClass)))</c> arm in
    /// W3cOwl2DirectTests.cs, replicated here so a harness change cannot
    /// silently diverge from this battery.
    /// </summary>
    [TestMethod]
    public void Bc16EntailmentProbeShapeRefutesBySelfSumCollapse()
    {
        ReasoningModule module = ProbeModule(ClassAssertion(Intersection(Class("2a"), Complement(Nothing)), Individual("witness")));
        BijectionChainOutcome outcome = ContextBijectionChainDecider.Run(module);

        Assert.IsFalse(outcome.Consistent, "Bc16 EntailmentProbeShape: a member of a class the chains collapse to zero refutes the probe module.");
        Assert.StartsWith(ForcedConstantConflict, outcome.ClashReason, StringComparison.Ordinal, "Bc16 EntailmentProbeShape: the clash reason names the arithmetic conflict.");
        Assert.IsNull(outcome.CertificateRoute, "Bc16 EntailmentProbeShape: the class assertion keeps the vacuity route out.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, BijectionChainFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Bc16 EntailmentProbeShape: the clash face decides the probe module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Bc16 EntailmentProbeShape: the probe module is inconsistent, so the conclusion axiom is entailed.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Bc16 EntailmentProbeShape: the probe decides with zero inference attempts.");
        Assert.AreEqual(1, totals.BijectionChainDeciderClashes, "Bc16 EntailmentProbeShape: the clash face's counter reads the decision.");
        Assert.AreEqual(0, totals.BijectionChainDeciderCertifications, "Bc16 EntailmentProbeShape: no certificate on a refuted probe.");
    }

    /// <summary>Told distinct members above a told upper bound: four pairwise-different named individuals asserted into a class the subclass-position union of three singleton enumerations bounds at three — the lower-bound arithmetic pinned with no equality chain in play.</summary>
    [TestMethod]
    public void Bc17ToldDistinctMembersAboveUpperBoundRefute()
    {
        BijectionChainOutcome outcome = ContextBijectionChainDecider.Run(LowerBoundModule());

        Assert.IsFalse(outcome.Consistent, "Bc17 ToldDistinctMembers: four told-distinct members cannot fit a three-element upper bound.");
        Assert.StartsWith(ForcedConstantConflict, outcome.ClashReason, StringComparison.Ordinal, "Bc17 ToldDistinctMembers: the clash reason names the arithmetic conflict.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(LowerBoundModule(), BijectionChainFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.IsFalse(decision.Verdict!.IsConsistent, "Bc17 ToldDistinctMembers: the reasoner carries the same refutation.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.BijectionChainDeciderClashes, "Bc17 ToldDistinctMembers: the clash face's counter reads the decision.");
    }

    /// <summary>
    /// The strict arm's FIFTH probe shape decided outright: the vacuity premise
    /// plus the axiom the shared refutation builders emit for a
    /// <c>SubClassOf(a, owl:Thing)</c> conclusion — a class assertion of
    /// <c>a ⊓ ¬owl:Thing</c> on the shared named witness, which the harness's
    /// vacuity predicate does NOT skip. The complement of the universal class is
    /// empty in every interpretation while the assertion demands a member, so the
    /// module refutes before any propagation runs. With Bc16 this pins that the
    /// whole five-probe walk of the corpus case decides without the engine.
    /// </summary>
    [TestMethod]
    public void Bc18FifthProbeEmptyConjunctRefutesOutright()
    {
        ReasoningModule module = ProbeModule(ClassAssertion(Intersection(Class("a"), Complement(Thing)), Individual("witness")));
        BijectionChainOutcome outcome = ContextBijectionChainDecider.Run(module);

        Assert.IsFalse(outcome.Consistent, "Bc18 FifthProbe: an asserted empty conjunct refutes the module outright.");
        Assert.StartsWith(UnsatisfiableAssertedConjunct, outcome.ClashReason, StringComparison.Ordinal, "Bc18 FifthProbe: the clash reason names the unsatisfiable asserted conjunct.");
        Assert.IsNull(outcome.CertificateRoute, "Bc18 FifthProbe: the class assertion keeps the vacuity route out.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, BijectionChainFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Bc18 FifthProbe: the clash face decides the fifth probe module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Bc18 FifthProbe: the probe module is inconsistent, so the conclusion axiom is entailed.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Bc18 FifthProbe: the probe decides with zero inference attempts.");
        Assert.AreEqual(1, totals.BijectionChainDeciderClashes, "Bc18 FifthProbe: the clash face's counter reads the decision.");
    }

    /// <summary>
    /// The census signal is bound to ONE role: a counting module carrying a told
    /// functional characteristic, a told inverse pair over that same functional
    /// role, and a told existential over an UNRELATED role — the three Shape B
    /// ingredients present but never meeting — takes no Shape B label, because
    /// neither the equality derivation nor the fiber product can read a premise
    /// out of ingredients that share no role. The minimal pair is the assertion:
    /// moving the existential onto the characteristic's own role, and changing
    /// nothing else, restores the label.
    /// </summary>
    [TestMethod]
    public void Bc19IncidentalIngredientsOnUnrelatedRolesTakeNoShapeBLabel()
    {
        ContextSaturationStatistics scattered = ContextSaturationModuleReasoner.DecideModule(ScatteredIngredientModule(), BijectionChainFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.None, scattered.EnumerationHabitat, "Bc19 ScatteredIngredients: ingredients that never meet on a role are no bijection chain, and no later probe claims the module either.");
        Assert.AreEqual(0, scattered.BijectionChainDeciderClashes, "Bc19 ScatteredIngredients: an unlabelled module reaches no bijection-chain face.");
        Assert.AreEqual(0, scattered.BijectionChainDeciderCertifications, "Bc19 ScatteredIngredients: no certificate either.");

        ContextSaturationStatistics linked = ContextSaturationModuleReasoner.DecideModule(LinkedIngredientModule(), BijectionChainFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.BijectionChainArithmetic, linked.EnumerationHabitat, "Bc19 LinkedIngredients: the same module with the existential on the characteristic's own role is the habitat.");
    }

    /// <summary>The deciding bijection-chain rows with their verdicts — the sweep's lit-face fixtures across both faces and both certificate routes.</summary>
    /// <returns>The rows.</returns>
    private static (string Name, ReasoningModule Module, bool Consistent)[] BijectionChainRows()
    {
        return
        [
            ("Bc1", CorpusChainTemplateModule(), false),
            ("Bc2", GroundedTowerModule(6), true),
            ("Bc3", VacuityChainModule(), true),
            ("Bc4", PureTboxTowerModule(), true),
            ("Bc5", GroundedTowerModule(7), false),
            ("Bc6", SingletonSumModule(), false),
        ];
    }

    /// <summary>
    /// The nineteen near-miss modules, one per named hazard: the six broken
    /// bijection and sum premises, the two broken fan-in cardinalities, the four
    /// vacuity leaks, the four tower leaks, the mixed chain-and-tower vocabulary
    /// no certificate route covers, the anonymous enumeration member, and the
    /// self-sum with no finite bound. Every one is tuned so that a leak in its
    /// guard would move a verdict.
    /// </summary>
    /// <returns>The rows.</returns>
    private static (string Name, ReasoningModule Module)[] NearMissRows()
    {
        return
        [
            ("MissingAllDifferentDropsTheConstant", ChainSumModule(
                Equivalent(Class("U"), OneOf("u1", "u2", "u3")))),

            ("MissingInverseLinkageDropsTheEquality", Module(
                Equivalent(Class("U"), Union(Class("B"), Class("C"))),
                Disjoint(Class("B"), Class("C")),
                SubClassOf(Class("U"), Some("f", Class("B"))),
                SubClassOf(Class("B"), Some("g", Class("U"))),
                Functional("f"),
                InverseFunctional("f"),
                SubClassOf(Class("B"), Some("h", Class("C"))),
                SubClassOf(Class("C"), Some("k", Class("B"))),
                Functional("h"),
                InverseFunctional("h"),
                InverseProperties("h", "k"),
                Equivalent(Class("U"), OneOf("u1", "u2", "u3")),
                Different("u1", "u2", "u3"))),

            ("DroppedFunctionalDropsTheEquality", Module(
                Equivalent(Class("U"), Union(Class("B"), Class("C"))),
                Disjoint(Class("B"), Class("C")),
                SubClassOf(Class("U"), Some("f", Class("B"))),
                SubClassOf(Class("B"), Some("g", Class("U"))),
                InverseFunctional("f"),
                InverseProperties("f", "g"),
                SubClassOf(Class("B"), Some("h", Class("C"))),
                SubClassOf(Class("C"), Some("k", Class("B"))),
                Functional("h"),
                InverseFunctional("h"),
                InverseProperties("h", "k"),
                Equivalent(Class("U"), OneOf("u1", "u2", "u3")),
                Different("u1", "u2", "u3"))),

            ("DroppedInverseFunctionalDropsTheEquality", Module(
                Equivalent(Class("U"), Union(Class("B"), Class("C"))),
                Disjoint(Class("B"), Class("C")),
                SubClassOf(Class("U"), Some("f", Class("B"))),
                SubClassOf(Class("B"), Some("g", Class("U"))),
                Functional("f"),
                InverseProperties("f", "g"),
                SubClassOf(Class("B"), Some("h", Class("C"))),
                SubClassOf(Class("C"), Some("k", Class("B"))),
                Functional("h"),
                InverseFunctional("h"),
                InverseProperties("h", "k"),
                Equivalent(Class("U"), OneOf("u1", "u2", "u3")),
                Different("u1", "u2", "u3"))),

            ("SplitCharacteristicsAcrossRolesDropTheEquality", Module(
                Equivalent(Class("U"), Union(Class("B"), Class("C"))),
                Disjoint(Class("B"), Class("C")),
                SubClassOf(Class("U"), Some("f", Class("B"))),
                SubClassOf(Class("B"), Some("g", Class("U"))),
                Functional("f"),
                InverseFunctional("g"),
                InverseProperties("f", "g"),
                SubClassOf(Class("B"), Some("h", Class("C"))),
                SubClassOf(Class("C"), Some("k", Class("B"))),
                Functional("h"),
                InverseFunctional("h"),
                InverseProperties("h", "k"),
                Equivalent(Class("U"), OneOf("u1", "u2", "u3")),
                Different("u1", "u2", "u3"))),

            ("MissingDisjointnessDropsTheSum", Module(
                Equivalent(Class("U"), Union(Class("B"), Class("C"))),
                SubClassOf(Class("U"), Some("f", Class("B"))),
                SubClassOf(Class("B"), Some("g", Class("U"))),
                Functional("f"),
                InverseFunctional("f"),
                InverseProperties("f", "g"),
                SubClassOf(Class("B"), Some("h", Class("C"))),
                SubClassOf(Class("C"), Some("k", Class("B"))),
                Functional("h"),
                InverseFunctional("h"),
                InverseProperties("h", "k"),
                Equivalent(Class("U"), OneOf("u1", "u2", "u3")),
                Different("u1", "u2", "u3"))),

            ("QualifiedCardinalityDropsTheFanIn", TowerModuleWith(Equivalent(Class("A"), Exact("g3", 7, Class("F"))))),

            ("DataCardinalityLookalike", TowerModuleWith(Equivalent(Class("A"), ExactData("dp", 7)))),

            ("ThingSubjectBlocksVacuity", VacuitySafeBaseModule(
                SubClassOf(Thing, Nothing))),

            ("ThingAsEquivalentOperandStaysSilent", VacuitySafeBaseModule(
                Equivalent(Class("N"), Thing),
                Equivalent(Class("N"), Nothing))),

            ("ClassAssertionBlocksVacuity", VacuitySafeBaseModule(
                Equivalent(Class("N"), Nothing),
                ClassAssertion(Class("N"), Individual("w")))),

            ("ZeroCardinalityEquivalenceBlocksVacuity", VacuitySafeBaseModule(
                Equivalent(Class("N"), Exact("q", 0, null)))),

            ("TowerWithExtraAxiomStaysSilent", TowerModuleWith(
                Equivalent(Class("A"), Exact("g3", 6, null)),
                SubClassOf(Class("T"), Nothing))),

            ("TowerWithForeignRangeStaysSilent", TowerModuleWith(
                Equivalent(Class("A"), Exact("g3", 6, null)),
                Range("f2", Class("A")))),

            ("ThingAsTowerClassStaysSilent", TowerModuleWithAnchor(Thing)),

            ("NothingAsTowerClassStaysSilent", TowerModuleWithAnchor(Nothing)),

            ("MixedChainTowerStaysSilent", Module(
                Equivalent(Class("A"), OneOf("d")),
                Equivalent(Class("A"), Exact("g1", 2, null)),
                Equivalent(Class("M"), Some("f1", Class("A"))),
                Functional("f1"),
                InverseProperties("f1", "g1"))),

            ("AnonymousOneOfMemberDropsTheConstant", ChainSumModule(
                Equivalent(Class("U"), MixedOneOf("u1", "hidden")))),

            ("SelfSumWithoutFiniteBoundStaysSilent", ChainSumModule(
                ClassAssertion(Class("U"), Individual("w")))),
        ];
    }

    /// <summary>
    /// The corpus chain template: eight roles told functional and
    /// inverse-functional, four told inverse pairs, the four paired existential
    /// chains bijecting all five named classes, the anchor's three-member
    /// enumeration with its told distinctness, the disjoint-union equivalence,
    /// and the seven told disjointness edges.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule CorpusChainTemplateModule()
    {
        List<OwlAxiom> axioms = [];
        AppendChainSpine(axioms, "bandc");
        axioms.Add(Equivalent(Class("a"), OneOf("j", "k", "i")));
        axioms.Add(Equivalent(Class("bandc"), Union(Class("b"), Class("c"))));
        axioms.Add(Different("j", "k", "i"));

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>
    /// The vacuity chain premise: the same spine and disjointness edges with the
    /// disjoint-union equivalence, but the anchor bounded from ABOVE by a
    /// subclass-position union of three singleton enumerations rather than
    /// pinned by an equivalence, and three bare individual declarations for an
    /// otherwise empty ABox.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule VacuityChainModule()
    {
        List<OwlAxiom> axioms = [];
        AppendChainSpine(axioms, "bUNIONc");
        axioms.Add(Equivalent(Class("bUNIONc"), Union(Class("b"), Class("c"))));
        axioms.Add(SubClassOf(Class("a"), Union(OneOf("i1"), OneOf("i2"), OneOf("i3"))));
        axioms.Add(Declaration("i1"));
        axioms.Add(Declaration("i2"));
        axioms.Add(Declaration("i3"));

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The vacuity chain premise plus one strict-arm refutation probe axiom — the shape the shared refutation builders emit for one conclusion axiom.</summary>
    /// <param name="probe">The probe axiom.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ProbeModule(OwlAxiom probe)
    {
        List<OwlAxiom> axioms = [];
        AppendChainSpine(axioms, "bUNIONc");
        axioms.Add(Equivalent(Class("bUNIONc"), Union(Class("b"), Class("c"))));
        axioms.Add(SubClassOf(Class("a"), Union(OneOf("i1"), OneOf("i2"), OneOf("i3"))));
        axioms.Add(Declaration("i1"));
        axioms.Add(Declaration("i2"));
        axioms.Add(Declaration("i3"));
        axioms.Add(probe);

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>Appends the shared chain spine: the eight told characteristics, four told inverse pairs, four paired existential chains over the five named classes, and the seven told disjointness edges.</summary>
    /// <param name="axiomsToAppendTo">The axiom list the spine is appended to.</param>
    /// <param name="unionClass">The local name of the class the union equivalence defines — the chain partner of the doubled class.</param>
    private static void AppendChainSpine(List<OwlAxiom> axiomsToAppendTo, string unionClass)
    {
        string[] roles = ["ra", "rg", "rb", "rd", "rc", "re", "rf", "rh"];
        for(int index = 0; index < roles.Length; index++)
        {
            axiomsToAppendTo.Add(Functional(roles[index]));
            axiomsToAppendTo.Add(InverseFunctional(roles[index]));
        }

        axiomsToAppendTo.Add(InverseProperties("ra", "rg"));
        axiomsToAppendTo.Add(InverseProperties("rb", "rd"));
        axiomsToAppendTo.Add(InverseProperties("rc", "re"));
        axiomsToAppendTo.Add(InverseProperties("rf", "rh"));

        axiomsToAppendTo.Add(SubClassOf(Class("2a"), Some("ra", Class(unionClass))));
        axiomsToAppendTo.Add(SubClassOf(Class(unionClass), Some("rg", Class("2a"))));
        axiomsToAppendTo.Add(SubClassOf(Class("2a"), Some("rb", Class("a"))));
        axiomsToAppendTo.Add(SubClassOf(Class("a"), Some("rd", Class("2a"))));
        axiomsToAppendTo.Add(SubClassOf(Class("a"), Some("rc", Class("b"))));
        axiomsToAppendTo.Add(SubClassOf(Class("b"), Some("re", Class("a"))));
        axiomsToAppendTo.Add(SubClassOf(Class("b"), Some("rf", Class("c"))));
        axiomsToAppendTo.Add(SubClassOf(Class("c"), Some("rh", Class("b"))));

        axiomsToAppendTo.Add(Disjoint(Class("2a"), Class("a")));
        axiomsToAppendTo.Add(Disjoint(Class("2a"), Class("b")));
        axiomsToAppendTo.Add(Disjoint(Class("2a"), Class(unionClass)));
        axiomsToAppendTo.Add(Disjoint(Class("2a"), Class("c")));
        axiomsToAppendTo.Add(Disjoint(Class("a"), Class("b")));
        axiomsToAppendTo.Add(Disjoint(Class("a"), Class("c")));
        axiomsToAppendTo.Add(Disjoint(Class("b"), Class("c")));
    }

    /// <summary>The compact chain-and-sum template: two paired bijection chains merging three named classes into one equality class, whose disjoint union decomposes it into two of its own members — the shape a told constant or bound turns into a clash. The row's own perturbation is appended last.</summary>
    /// <param name="perturbation">The axiom the row varies.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ChainSumModule(OwlAxiom perturbation)
    {
        return Module(
            Equivalent(Class("U"), Union(Class("B"), Class("C"))),
            Disjoint(Class("B"), Class("C")),
            SubClassOf(Class("U"), Some("f", Class("B"))),
            SubClassOf(Class("B"), Some("g", Class("U"))),
            Functional("f"),
            InverseFunctional("f"),
            InverseProperties("f", "g"),
            SubClassOf(Class("B"), Some("h", Class("C"))),
            SubClassOf(Class("C"), Some("k", Class("B"))),
            Functional("h"),
            InverseFunctional("h"),
            InverseProperties("h", "k"),
            perturbation);
    }

    /// <summary>The vacuity-safe base: one paired bijection chain whose every axiom the vacuity whitelist admits, so the all-empty model would certify it — each near-miss row appends exactly one axiom the whitelist must reject.</summary>
    /// <param name="leaks">The axioms the row appends.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule VacuitySafeBaseModule(params OwlAxiom[] leaks)
    {
        List<OwlAxiom> axioms =
        [
            SubClassOf(Class("U"), Some("f", Class("B"))),
            SubClassOf(Class("B"), Some("g", Class("U"))),
            Functional("f"),
            InverseFunctional("f"),
            InverseProperties("f", "g"),
        ];
        for(int index = 0; index < leaks.Length; index++)
        {
            axioms.Add(leaks[index]);
        }

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The corpus diamond template with the requested top-level count.</summary>
    /// <param name="topLevel">The anchor's told count over the top level's anchor-step inverse.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule GroundedTowerModule(int topLevel)
    {
        return TowerModuleWith(Equivalent(Class("A"), Exact("g3", topLevel, null)));
    }

    /// <summary>The diamond template with the top-level count axiom supplied and any extra axioms appended — the shape the tower near-miss rows perturb.</summary>
    /// <param name="topCount">The axiom playing the anchor's second told count.</param>
    /// <param name="extra">The axioms the row appends.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule TowerModuleWith(OwlAxiom topCount, params OwlAxiom[] extra)
    {
        List<OwlAxiom> axioms =
        [
            Functional("f1"),
            Functional("f2"),
            Functional("f3"),
            InverseProperties("f1", "g1"),
            InverseProperties("f2", "g2"),
            InverseProperties("f3", "g3"),
            Equivalent(Class("A"), OneOf("d")),
            ClassAssertion(Thing, Individual("d")),
            Equivalent(Class("A"), Exact("g1", 2, null)),
            topCount,
            Equivalent(Class("M"), Some("f1", Class("A"))),
            Equivalent(Class("M"), Exact("g2", 3, null)),
            Equivalent(Class("T"), Some("f2", Class("M"))),
            Equivalent(Class("T"), Some("f3", Class("A"))),
            Domain("f1", Class("M")),
            Range("f1", Class("A")),
            Domain("f2", Class("T")),
            Range("f2", Class("M")),
            Domain("f3", Class("T")),
            Range("f3", Class("A")),
        ];
        for(int index = 0; index < extra.Length; index++)
        {
            axioms.Add(extra[index]);
        }

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The diamond template with the requested expression in every anchor position — the shape the two OWL class constants must not occupy, since their semantics-fixed extensions break the construction's free choice of level sets.</summary>
    /// <param name="anchor">The anchor expression.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule TowerModuleWithAnchor(OwlClassExpression anchor)
    {
        return Module(
            Functional("f1"),
            Functional("f2"),
            Functional("f3"),
            InverseProperties("f1", "g1"),
            InverseProperties("f2", "g2"),
            InverseProperties("f3", "g3"),
            Equivalent(anchor, OneOf("d")),
            Equivalent(anchor, Exact("g1", 2, null)),
            Equivalent(anchor, Exact("g3", 6, null)),
            Equivalent(Class("M"), Some("f1", anchor)),
            Equivalent(Class("M"), Exact("g2", 3, null)),
            Equivalent(Class("T"), Some("f2", Class("M"))),
            Equivalent(Class("T"), Some("f3", anchor)),
            Domain("f1", Class("M")),
            Range("f1", anchor),
            Domain("f2", Class("T")),
            Range("f2", Class("M")),
            Domain("f3", Class("T")),
            Range("f3", anchor));
    }

    /// <summary>The pure-TBox tower: three functional roles with told inverse partners, told domains and ranges, and the five equivalences whose exact counts never meet an anchor — no individual, no enumeration, no ABox of any kind.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule PureTboxTowerModule()
    {
        return Module(
            Functional("p"),
            Functional("q"),
            Functional("r"),
            InverseProperties("p", "invP"),
            InverseProperties("q", "invQ"),
            InverseProperties("r", "invR"),
            Domain("p", Class("cardinalityN")),
            Range("p", Class("unbounded")),
            Domain("q", Class("cardinalityNM")),
            Range("q", Class("cardinalityN")),
            Domain("r", Class("cardinalityNM")),
            Range("r", Class("unbounded")),
            Equivalent(Class("unbounded"), Exact("invP", 2, null)),
            Equivalent(Class("unbounded"), Exact("invR", 5, null)),
            Equivalent(Class("cardinalityN"), Some("p", Class("unbounded"))),
            Equivalent(Class("cardinalityN"), Exact("invQ", 3, null)),
            Equivalent(Class("cardinalityNM"), Some("q", Class("cardinalityN"))),
            Equivalent(Class("cardinalityNM"), Some("r", Class("unbounded"))));
    }

    /// <summary>The singleton-sum module: a one-element class told to be the disjoint union of two one-element classes, with no distinctness axiom anywhere, beside an unrelated bijection pair that carries the habitat signal.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SingletonSumModule()
    {
        return Module(
            Equivalent(Class("A"), OneOf("d")),
            Equivalent(Class("A"), Union(Class("B"), Class("C"))),
            Disjoint(Class("B"), Class("C")),
            Equivalent(Class("B"), OneOf("b1")),
            Equivalent(Class("C"), OneOf("c1")),
            SubClassOf(Class("P"), Some("f", Class("Q"))),
            SubClassOf(Class("Q"), Some("g", Class("P"))),
            Functional("f"),
            InverseFunctional("f"),
            InverseProperties("f", "g"));
    }

    /// <summary>The negative-residue module: a one-element union whose first disjoint operand already holds two told-distinct members, beside an unrelated bijection pair that carries the habitat signal.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NegativeResidueModule()
    {
        return Module(
            Equivalent(Class("U"), Union(Class("B"), Class("C"))),
            Disjoint(Class("B"), Class("C")),
            Equivalent(Class("U"), OneOf("u1")),
            Equivalent(Class("B"), OneOf("b1", "b2")),
            Different("b1", "b2"),
            SubClassOf(Class("P"), Some("f", Class("Q"))),
            SubClassOf(Class("Q"), Some("g", Class("P"))),
            Functional("f"),
            InverseFunctional("f"),
            InverseProperties("f", "g"));
    }

    /// <summary>The upper-bound module: a four-member enumeration with full told distinctness under a subclass-position union of three singleton enumerations, beside an unrelated bijection pair that carries the habitat signal.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule UpperBoundModule()
    {
        return Module(
            Equivalent(Class("X"), OneOf("m1", "m2", "m3", "m4")),
            Different("m1", "m2", "m3", "m4"),
            SubClassOf(Class("X"), Union(OneOf("s1"), OneOf("s2"), OneOf("s3"))),
            SubClassOf(Class("P"), Some("f", Class("Q"))),
            SubClassOf(Class("Q"), Some("g", Class("P"))),
            Functional("f"),
            InverseFunctional("f"),
            InverseProperties("f", "g"));
    }

    /// <summary>The lower-bound module: four told-distinct named individuals asserted into a class a subclass-position union of three singleton enumerations bounds at three, beside an unrelated bijection pair that carries the habitat signal.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule LowerBoundModule()
    {
        return Module(
            ClassAssertion(Class("X"), Individual("w1")),
            ClassAssertion(Class("X"), Individual("w2")),
            ClassAssertion(Class("X"), Individual("w3")),
            ClassAssertion(Class("X"), Individual("w4")),
            Different("w1", "w2", "w3", "w4"),
            SubClassOf(Class("X"), Union(OneOf("s1"), OneOf("s2"), OneOf("s3"))),
            SubClassOf(Class("P"), Some("f", Class("Q"))),
            SubClassOf(Class("Q"), Some("g", Class("P"))),
            Functional("f"),
            InverseFunctional("f"),
            InverseProperties("f", "g"));
    }

    /// <summary>The vacuity chain premise plus one told transitivity characteristic — the single axiom outside the whitelist that leaves the whole-module admission unmet.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule TransitiveExtraModule()
    {
        List<OwlAxiom> axioms = [];
        AppendChainSpine(axioms, "bUNIONc");
        axioms.Add(Equivalent(Class("bUNIONc"), Union(Class("b"), Class("c"))));
        axioms.Add(SubClassOf(Class("a"), Union(OneOf("i1"), OneOf("i2"), OneOf("i3"))));
        axioms.Add(Transitive("rt"));

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The overflow chain: four anchored levels each multiplying by the maximal int, so the third product leaves the long range and the propagation charges a silence rather than grounding a wrapped value.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule OverflowChainModule()
    {
        return Module(
            Equivalent(Class("A"), OneOf("d")),
            Equivalent(Class("A"), Exact("g1", int.MaxValue, null)),
            Equivalent(Class("M"), Some("f1", Class("A"))),
            Equivalent(Class("M"), Exact("g2", int.MaxValue, null)),
            Equivalent(Class("T"), Some("f2", Class("M"))),
            Equivalent(Class("T"), Exact("g3", int.MaxValue, null)),
            Equivalent(Class("U"), Some("f3", Class("T"))),
            Functional("f1"),
            Functional("f2"),
            Functional("f3"),
            InverseProperties("f1", "g1"),
            InverseProperties("f2", "g2"),
            InverseProperties("f3", "g3"));
    }

    /// <summary>The class-window template: the requested number of size variables, the first two bijected onto one another while carrying two different told constants, so the module's own arithmetic clashes wherever the window admits it.</summary>
    /// <param name="classes">The size variables the module carries.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ClassWindowModule(int classes)
    {
        List<OwlAxiom> axioms =
        [
            Equivalent(Class("x0"), OneOf("m0a", "m0b")),
            Different("m0a", "m0b"),
            Equivalent(Class("x1"), OneOf("m1")),
            SubClassOf(Class("x0"), Some("f", Class("x1"))),
            SubClassOf(Class("x1"), Some("g", Class("x0"))),
            Functional("f"),
            InverseFunctional("f"),
            InverseProperties("f", "g"),
        ];
        for(int index = 2; index < classes; index++)
        {
            axioms.Add(Equivalent(Class("x" + index), OneOf("m" + index)));
        }

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>
    /// The scattered-ingredient module: a told enumeration and a told exact
    /// cardinality supply the nominal and counting mentions, one role is told
    /// functional AND stands in a told inverse pair, and the module's only
    /// existential sits on a third role that carries neither — the ingredient
    /// spread a ground ontology reaches by accident. No inverse-functional
    /// characteristic occurs anywhere.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ScatteredIngredientModule()
    {
        return Module(
            Equivalent(Class("Colour"), OneOf("red", "white")),
            Different("red", "white"),
            Equivalent(Class("Bottling"), Exact("hasColour", 2, null)),
            Functional("hasMaker"),
            InverseProperties("producesBottling", "hasMaker"),
            SubClassOf(Class("Bottling"), Some("locatedIn", Class("Region"))));
    }

    /// <summary>The linked-ingredient module: the scattered module with its existential moved onto the functional role the told inverse pair already names, so all three ingredients meet on one role.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule LinkedIngredientModule()
    {
        return Module(
            Equivalent(Class("Colour"), OneOf("red", "white")),
            Different("red", "white"),
            Equivalent(Class("Bottling"), Exact("hasColour", 2, null)),
            Functional("hasMaker"),
            InverseProperties("producesBottling", "hasMaker"),
            SubClassOf(Class("Bottling"), Some("hasMaker", Class("Region"))));
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

    /// <summary>The <c>owl:Thing</c> reference — the universal class the recognized positions bar.</summary>
    private static OwlClassReference Thing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing")));

    /// <summary>The <c>owl:Nothing</c> reference — the empty class the strict arm's probes complement.</summary>
    private static OwlClassReference Nothing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Nothing")));

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

    /// <summary>An anonymous individual — the enumeration member that drops a told constant.</summary>
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

    /// <summary>An enumeration of one named and one ANONYMOUS individual — the shape that drops a told constant whole, since an anonymous member carries no told distinctness.</summary>
    /// <param name="named">The named member's local name.</param>
    /// <param name="label">The anonymous member's blank-node label.</param>
    /// <returns>The enumeration.</returns>
    private static OwlObjectOneOf MixedOneOf(string named, string label)
    {
        return new OwlObjectOneOf([Individual(named), Anonymous(label)]);
    }

    /// <summary>A union of class expressions.</summary>
    /// <param name="operands">The union operands.</param>
    /// <returns>The union.</returns>
    private static OwlObjectUnionOf Union(params OwlClassExpression[] operands)
    {
        return new OwlObjectUnionOf([.. operands]);
    }

    /// <summary>An intersection of class expressions — the strict arm's refutation-probe wrapper.</summary>
    /// <param name="operands">The intersection operands.</param>
    /// <returns>The intersection.</returns>
    private static OwlObjectIntersectionOf Intersection(params OwlClassExpression[] operands)
    {
        return new OwlObjectIntersectionOf([.. operands]);
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

    /// <summary>A qualified or unqualified exact-cardinality restriction over a named forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound <c>k</c>.</param>
    /// <param name="filler">The qualifying filler, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Exact(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Exact, cardinality, Property(property), filler);
    }

    /// <summary>An unqualified exact-cardinality restriction over a named DATA property — the fan-in lookalike that bounds literals rather than domain elements.</summary>
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

    /// <summary>A disjoint-classes axiom over the operands.</summary>
    /// <param name="operands">The pairwise-disjoint class expressions.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointClassesAxiom Disjoint(params OwlClassExpression[] operands)
    {
        return new OwlDisjointClassesAxiom([.. operands]) { Origin = Origin("disjoint") };
    }

    /// <summary>A class assertion typing an individual.</summary>
    /// <param name="type">The asserted class expression.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, RdfTerm individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
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

    /// <summary>An inverse-functionality characteristic over a named role in the example namespace.</summary>
    /// <param name="role">The role's local name.</param>
    /// <returns>The characteristic axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom InverseFunctional(string role)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.InverseFunctional, Property(role)) { Origin = Origin("inversefunctional") };
    }

    /// <summary>A transitivity characteristic over a named role — the characteristic the vacuity whitelist does not admit.</summary>
    /// <param name="role">The role's local name.</param>
    /// <returns>The characteristic axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Transitive(string role)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Transitive, Property(role)) { Origin = Origin("transitive") };
    }

    /// <summary>A told inverse between two named object properties.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlInverseObjectPropertiesAxiom InverseProperties(string first, string second)
    {
        return new OwlInverseObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("inverse") };
    }

    /// <summary>A told object-property domain axiom.</summary>
    /// <param name="role">The role's local name.</param>
    /// <param name="domain">The domain class expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyDomainAxiom Domain(string role, OwlClassExpression domain)
    {
        return new OwlObjectPropertyDomainAxiom(Property(role), domain) { Origin = Origin("domain") };
    }

    /// <summary>A told object-property range axiom.</summary>
    /// <param name="role">The role's local name.</param>
    /// <param name="range">The range class expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyRangeAxiom Range(string role, OwlClassExpression range)
    {
        return new OwlObjectPropertyRangeAxiom(Property(role), range) { Origin = Origin("range") };
    }

    /// <summary>A bare named-individual declaration — the only ABox content the vacuity premise carries.</summary>
    /// <param name="local">The individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlDeclarationAxiom Declaration(string local)
    {
        return new OwlDeclarationAxiom(OwlEntityKind.NamedIndividual, Individual(local)) { Origin = Origin("declare") };
    }
}
