using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The enumeration-CSP habitat decider's battery: the decided-at-ceiling
/// rows for both faces,
/// the dark byte-identity control, the census rows, one near-miss row per
/// lock-round attack, one killer row per repaired forced-merge kind, the
/// window-constant boundary and derivation pins, the shared
/// struct-enumerator surface pins, and the rider clique sweep's bit-for-bit
/// A/B harness. Every row drives the production seams — the faces-carrying
/// reasoner overload, the flag-carrying clausifier overload, or the survey —
/// and every counter the battery reads is thereby consumed by an assert.
/// </summary>
[TestClass]
internal sealed class ContextEnumerationDeciderTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the battery's classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/enumcsp#";

    /// <summary>The nominal battery's namespace — the reused ENUM-1 and NOMR fixtures live there.</summary>
    private const string BatteryExample = "http://example.org/tier3nominal#";

    /// <summary>Both decider faces lit — the fully-lit selection the decided rows drive.</summary>
    private const EnumerationDeciderFaces BothFaces = EnumerationDeciderFaces.ClashOnly | EnumerationDeciderFaces.Certifying;

    /// <summary>Both pair-composition faces lit — the selection the pair rows drive; the older sibling selections stay exactly as they were, so no existing row changes face.</summary>
    private const EnumerationDeciderFaces PairFaces = EnumerationDeciderFaces.EnumerationPairClash | EnumerationDeciderFaces.EnumerationPairCertify;

    /// <summary>Both partition faces lit — the neighbouring habitat's selection the pair rows' face-selection sweep drives.</summary>
    private const EnumerationDeciderFaces PartitionFaces = EnumerationDeciderFaces.PartitionClash | EnumerationDeciderFaces.PartitionCertify;

    /// <summary>The bounded budget the pair-face silence and dark rows drive: enough for the engine to fire rules on a past-window enumeration module, far below what its saturation would need.</summary>
    private static ReasoningBudget PairProbeBudget { get; } = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 4096);

    /// <summary>
    /// ENUM-1 decided at the UNCHANGED production ceiling: the certifying face
    /// certifies the certified-consistent verdict with the certified exact set
    /// — the told equivalence pair, both directions — spending ZERO inference
    /// attempts and constructing no engine (every engine counter zero, the
    /// upstream-of-topology-and-scope seat by construction).
    /// </summary>
    [TestMethod]
    public void Enum1CertifyingFaceDecidesAtProductionCeilingWithCertifiedSet()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(ContextNominalBatteryTests.Enum1Module(), EnumerationDeciderFaces.Certifying, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The certifying face decides ENUM-1 at the production ceiling.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "ENUM-1 is certified consistent.");
        AssertSubsumptionKeys(decision.Verdict, [BatterySub("C", "D"), BatterySub("D", "C")]);
        Assert.AreEqual(0L, totals.InferenceAttempts, "A pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(0, totals.ContextsCreated, "No engine was constructed — the seat is upstream of every engine axis.");
        Assert.AreEqual(1, totals.EnumerationDeciderCertifications, "The certifying face's counter reads the decision.");
        Assert.AreEqual(0, totals.EnumerationDeciderClashes, "The clash face did not decide this module.");
        Assert.AreEqual(EnumerationHabitatClass.EnumerationAlgebra, totals.EnumerationHabitat, "The recognizer classifies ENUM-1 as the enumeration algebra.");
        Assert.AreEqual(5, totals.EnumerationMemberUniverse, "The deduplicated member universe is the five enumerated individuals.");
    }

    /// <summary>
    /// NOMR-2 decided at the UNCHANGED production ceiling: the clash-only face
    /// decides the certified-inconsistent verdict — three pairwise
    /// told-distinct individuals against the told cap of two — with zero
    /// inference attempts, no engine, and the ground rider staying
    /// jurisdiction-disjoint (its counter zero on the nominal-arm decision).
    /// </summary>
    [TestMethod]
    public void Nomr2ClashFaceDecidesAtProductionCeilingInconsistent()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(ContextNominalBatteryTests.Nomr2Module(), EnumerationDeciderFaces.ClashOnly, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The clash face decides NOMR-2 at the production ceiling.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "NOMR-2 is certified inconsistent.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "A pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(0, totals.ContextsCreated, "No engine was constructed.");
        Assert.AreEqual(1, totals.EnumerationDeciderClashes, "The clash face's counter reads the decision.");
        Assert.AreEqual(0, totals.GroundCountingClashes, "The ground rider stays jurisdiction-disjoint on the nominal arm.");
        Assert.AreEqual(EnumerationHabitatClass.NominalCounting, totals.EnumerationHabitat, "The recognizer classifies NOMR-2 as nominal counting.");
    }

    /// <summary>
    /// The dark control: under the explicit
    /// <see cref="EnumerationDeciderFaces.None"/> selection — the measured
    /// dark knob behind the lit production default — ENUM-1 and NOMR-2 keep
    /// the honest engine-face budget abstention: the abstained outcome, no
    /// verdict, the inclusive ceiling spent, and the exhaust's measured
    /// funnel profile intact — tautology- and redundancy-dominated churn
    /// over genuine insertions.
    /// </summary>
    [TestMethod]
    public void DarkFacesKeepTheHonestAbstentionByteIdentical()
    {
        (string Name, ReasoningModule Module)[] rows = [("ENUM-1", ContextNominalBatteryTests.Enum1Module()), ("NOMR-2", ContextNominalBatteryTests.Nomr2Module())];
        foreach((string name, ReasoningModule module) in rows)
        {
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
            ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

            Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, name + " abstains honestly with the faces dark.");
            Assert.IsNull(decision.Verdict, name + "'s dark abstention carries no verdict.");
            Assert.AreEqual((long)ReasoningConfiguration.Default.Budget.MaxInferences, totals.InferenceAttempts, name + " spends exactly the inclusive ceiling, dark.");
            Assert.IsGreaterThan(0L, totals.TautologyDrops, name + "'s dark exhaust drops tautologies at the funnel's first stage.");
            Assert.IsGreaterThan(0L, totals.RedundantConclusions, name + "'s dark exhaust rejects redundant conclusions at the containment stage.");
            Assert.IsGreaterThan(0L, totals.WorklistEnqueues, name + "'s dark exhaust still lands genuine insertions at the funnel's head.");
        }
    }

    /// <summary>
    /// The census ships unconditionally (CEN-1): on the SAME explicit-dark
    /// abstentions the habitat class and the window measurements are already
    /// on the record — ENUM-1's member universe and NOMR-2's counted
    /// population, told-distinct clique, and cap bound, all measured with
    /// both faces dark.
    /// </summary>
    [TestMethod]
    public void CensusRidesTheDarkAbstentionRecordsAlways()
    {
        ModuleDecision enumDecision = ContextSaturationModuleReasoner.DecideModule(ContextNominalBatteryTests.Enum1Module(), EnumerationDeciderFaces.None, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics enumTotals = enumDecision.Statistics.ContextTotals;
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, enumDecision.Outcome, "ENUM-1 stays abstained dark — the census never moves a decision.");
        Assert.AreEqual(EnumerationHabitatClass.EnumerationAlgebra, enumTotals.EnumerationHabitat, "The habitat class rides the dark abstention record.");
        Assert.AreEqual(5, enumTotals.EnumerationMemberUniverse, "The member universe is measured dark.");

        ModuleDecision nomrDecision = ContextSaturationModuleReasoner.DecideModule(ContextNominalBatteryTests.Nomr2Module(), EnumerationDeciderFaces.None, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics nomrTotals = nomrDecision.Statistics.ContextTotals;
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, nomrDecision.Outcome, "NOMR-2 stays abstained dark.");
        Assert.AreEqual(EnumerationHabitatClass.NominalCounting, nomrTotals.EnumerationHabitat, "The habitat class rides the dark abstention record.");
        Assert.AreEqual(4, nomrTotals.EnumerationCountedPopulation, "The counted population — the anchor plus the three told-distinct individuals — is measured dark.");
        Assert.AreEqual(3, nomrTotals.EnumerationDistinctCliqueSize, "The told-distinct clique is measured dark.");
        Assert.AreEqual(2, nomrTotals.EnumerationCapBound, "The cap bound is measured dark.");
    }

    /// <summary>
    /// The generic-element row: an unconstrained bystander class beside
    /// ENUM-1's equated enumerations. Every block of every candidate model
    /// satisfies both enumerated classes, so a block-only read-off would
    /// wrongly entail the bystander's subsumption under them — the GenericSat
    /// element, where every one-of atom is pinned false, is what refutes it,
    /// and the exact set stays the told pair alone.
    /// </summary>
    [TestMethod]
    public void GenericElementBlocksBystanderEntailments()
    {
        ReasoningModule module = Extend(ContextNominalBatteryTests.Enum1Module(), SubClassOf(Class("E"), Thing));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.Certifying, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The certifying face decides the extended module.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The bystander changes nothing about consistency.");
        AssertSubsumptionKeys(decision.Verdict, [BatterySub("C", "D"), BatterySub("D", "C")]);
    }

    /// <summary>
    /// The overlapping-lists near-miss: <c>C = {a, b}</c>, <c>D = {b, c}</c>,
    /// <c>C = D</c> is CONSISTENT — the member lists overlap without being
    /// equal, the models merge across the lists (the endpoints' sameness is
    /// entailed while neither adjacent pair's is), and the exact set is the
    /// told equivalence pair.
    /// </summary>
    [TestMethod]
    public void OverlappingOneOfListsCertifyConsistentWithTheToldPair()
    {
        ReasoningModule module = Module(
            Equivalent(Class("C"), OneOf("a", "b")),
            Equivalent(Class("D"), OneOf("b", "c")),
            Equivalent(Class("C"), Class("D")));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.Certifying, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The certifying face decides the overlapping-lists module.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Overlapping lists under equivalence are consistent.");
        AssertSubsumptionKeys(decision.Verdict, [Sub("C", "D"), Sub("D", "C")]);
    }

    /// <summary>
    /// The overlapping-lists identity observable (the optional D8 flip-gate
    /// row): the endpoint sameness the overlapping row's models entail —
    /// <c>a = c</c> whenever <c>{a, b} = {b, c}</c> — is not on the verdict
    /// surface directly, so the row observes it indirectly: telling the
    /// endpoints different contradicts the entailment in every candidate
    /// partition and the certifying face decides INCONSISTENT.
    /// </summary>
    [TestMethod]
    public void OverlappingListEndpointIdentitySurfacesAsInconsistency()
    {
        ReasoningModule module = Module(
            Equivalent(Class("C"), OneOf("a", "b")),
            Equivalent(Class("D"), OneOf("b", "c")),
            Equivalent(Class("C"), Class("D")),
            Different("a", "c"));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.Certifying, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The certifying face decides the contradicted-endpoint module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The entailed endpoint sameness against the told distinctness refutes every partition.");
        Assert.AreEqual(0L, decision.Statistics.ContextTotals.InferenceAttempts, "The decision is pre-engine.");
    }

    /// <summary>
    /// The complement-wrapped-members near-miss: the collector descends into
    /// complement subtrees, so the members of a complemented one-of are
    /// first-class in the universe — three members counted — and the module
    /// certifies consistent (the asserted individual separates from both
    /// enumerated members in every model).
    /// </summary>
    [TestMethod]
    public void ComplementWrappedMembersAreFirstClassInTheUniverse()
    {
        ReasoningModule module = Module(
            Equivalent(Class("W"), Complement(OneOf("a", "b"))),
            ClassAssertion(Class("W"), Individual("w")));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.Certifying, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The certifying face decides the complement-wrapped module.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The complement-wrapped enumeration is consistent.");
        Assert.AreEqual(3, decision.Statistics.ContextTotals.EnumerationMemberUniverse, "The complement-wrapped members and the asserted individual are all in the universe.");
        Assert.IsEmpty(decision.Verdict.Subsumptions, "One named class yields no candidate pairs.");
    }

    /// <summary>
    /// The complement-member separation observable (the optional D8 flip-gate
    /// row's second face): the separations the complement row's models entail
    /// — <c>w != a</c> and <c>w != b</c> for a told member of the complement —
    /// are not on the verdict surface directly, so the row observes one
    /// indirectly: telling <c>w</c> same as an enumerated member contradicts
    /// the complement in every candidate partition and the certifying face
    /// decides INCONSISTENT.
    /// </summary>
    [TestMethod]
    public void ComplementMemberSeparationSurfacesAsInconsistency()
    {
        ReasoningModule module = Module(
            Equivalent(Class("W"), Complement(OneOf("a", "b"))),
            ClassAssertion(Class("W"), Individual("w")),
            Same("w", "a"));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.Certifying, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The certifying face decides the contradicted-complement module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The entailed separation against the told sameness refutes every partition.");
        Assert.AreEqual(0L, decision.Statistics.ContextTotals.InferenceAttempts, "The decision is pre-engine.");
    }

    /// <summary>
    /// The whole-axiom-set gate's killer row: a genuinely INCONSISTENT module
    /// whose class axioms alone look enumeration-algebra shaped — the
    /// inconsistency rides a has-value pair under a functional role — must be
    /// REJECTED from the certifying face wholesale and decided by ordinary
    /// saturation, never wrongly certified consistent off the class axioms.
    /// </summary>
    [TestMethod]
    public void HasValueFunctionalSmuggleIsRejectedFromTheCertifyingFace()
    {
        ReasoningModule module = Module(
            Equivalent(Class("C"), OneOf("a", "b")),
            SubClassOf(Thing, HasValue("r", "o1")),
            SubClassOf(Thing, HasValue("r", "o2")),
            Functional("r"),
            Different("o1", "o2"));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.Certifying, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(0, totals.EnumerationDeciderCertifications, "The certifying face never touches the smuggle module.");
        Assert.AreNotEqual(EnumerationHabitatClass.EnumerationAlgebra, totals.EnumerationHabitat, "The recognizer's whole-axiom-set view rejects the smuggle from Shape E.");
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Ordinary saturation decides the smuggle module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The functional has-value pair over told-distinct fillers is inconsistent.");
    }

    /// <summary>
    /// The DisjointUnion near-miss, pinning the behavior the design left
    /// open: <c>DisjointUnion</c> is NOT an admitted Σ_E kind — the module
    /// routes to sound certifying-face silence and ordinary saturation decides
    /// it, correctly consistent here.
    /// </summary>
    [TestMethod]
    public void DisjointUnionRejectsFromTheCertifyingFaceAndStaysWithSaturation()
    {
        ReasoningModule module = Module(
            DisjointUnion("C", Class("A"), Class("B")),
            Equivalent(Class("D"), OneOf("a")),
            ClassAssertion(Class("A"), Individual("x")));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.Certifying, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(0, totals.EnumerationDeciderCertifications, "The certifying face stays silent on a DisjointUnion-bearing module.");
        Assert.AreNotEqual(EnumerationHabitatClass.EnumerationAlgebra, totals.EnumerationHabitat, "DisjointUnion is not an admitted Shape E kind.");
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Ordinary saturation decides the module.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The module is consistent.");
    }

    /// <summary>
    /// The mixed-module face-ordering row [R2B-N4]: one module carrying the
    /// NOMR-2 cluster beside an enumeration-algebra cluster. The recognizer
    /// emits MIXED; the certifying face is silent over the ENTIRE module —
    /// never a sub-cluster certificate — and the clash face decides the
    /// genuine whole-module clash.
    /// </summary>
    [TestMethod]
    public void MixedModuleFaceOrderingClashDecidesAndCertifyingStaysSilent()
    {
        ReasoningModule module = Extend(
            ContextNominalBatteryTests.Nomr2Module(),
            Equivalent(Class("C"), OneOf("a", "b")),
            Equivalent(Class("D"), OneOf("b")),
            Equivalent(Class("C"), Class("D")));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, BothFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.Mixed, totals.EnumerationHabitat, "The recognizer emits the mixed class.");
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The clash face decides the whole module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The NOMR-2 cluster's clash condemns the module whole.");
        Assert.AreEqual(1, totals.EnumerationDeciderClashes, "The clash face decided.");
        Assert.AreEqual(0, totals.EnumerationDeciderCertifications, "The certifying face never certifies a sub-cluster of a mixed module.");
    }

    /// <summary>The member-universe window: nine enumerated individuals exceed the bound of eight, so the certifying face is silent with the named counter charged, and no decider counter moves.</summary>
    [TestMethod]
    public void MemberUniverseBoundSilencesTheCertifyingFace()
    {
        ReasoningModule module = Module(
            Equivalent(Class("C"), OneOf("a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8", "a9")));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.Certifying, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(0, totals.EnumerationDeciderCertifications, "The face is silent past the member-universe bound.");
        Assert.AreEqual(1, totals.EnumerationWindowExceededMembers, "The silence is charged to its named window counter.");
        Assert.AreEqual(9, totals.EnumerationMemberUniverse, "The measured universe is reported past the bound.");
        Assert.AreEqual(EnumerationHabitatClass.EnumerationAlgebra, totals.EnumerationHabitat, "The recognizer still names the habitat.");
    }

    /// <summary>The signature-class window: nine named classes exceed the bound of eight, so the certifying face is silent with the named counter charged.</summary>
    [TestMethod]
    public void SignatureClassBoundSilencesTheCertifyingFace()
    {
        ReasoningModule module = Module(
            Equivalent(Class("C1"), OneOf("a")),
            SubClassOf(Class("C2"), Thing),
            SubClassOf(Class("C3"), Thing),
            SubClassOf(Class("C4"), Thing),
            SubClassOf(Class("C5"), Thing),
            SubClassOf(Class("C6"), Thing),
            SubClassOf(Class("C7"), Thing),
            SubClassOf(Class("C8"), Thing),
            SubClassOf(Class("C9"), Thing));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.Certifying, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(0, totals.EnumerationDeciderCertifications, "The face is silent past the signature-class bound.");
        Assert.AreEqual(1, totals.EnumerationWindowExceededClasses, "The silence is charged to its named window counter.");
    }

    /// <summary>
    /// The clash face never certifies: NOMR-1 — the total-collapse funnel
    /// without any told distinctness — is CONSISTENT, the face stays silent,
    /// and ordinary saturation decides it with real inference work.
    /// </summary>
    [TestMethod]
    public void Nomr1ConsistencyStaysWithSaturationTheClashFaceNeverCertifies()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(ContextNominalBatteryTests.Nomr1Module(), EnumerationDeciderFaces.ClashOnly, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Saturation decides NOMR-1.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "NOMR-1 is consistent — the clash face never claims it.");
        Assert.AreEqual(0, totals.EnumerationDeciderClashes, "The clash face stayed silent.");
        Assert.IsGreaterThan(0L, totals.InferenceAttempts, "The engine, not the decider, did the work.");
    }

    /// <summary>Forced-merge kind (a), told sameness: a told-Same pair that is also told-Different collapses under the congruence closure and the clash face decides, inside an active funnel-and-cap habitat whose counting alone would stay silent.</summary>
    [TestMethod]
    public void ToldSameCollapseOfToldDistinctPairClashes()
    {
        ReasoningModule module = Module(
            SubClassOf(Thing, SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("r", 9, null)),
            Same("a", "b"),
            Different("a", "b"));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.ClashOnly, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The clash face decides the told-Same collapse.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "A told-Same pair told different refutes every model.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.EnumerationDeciderClashes, "The clash face's counter reads the decision.");
        Assert.AreEqual(0L, decision.Statistics.ContextTotals.InferenceAttempts, "The decision is pre-engine.");
    }

    /// <summary>Forced-merge kind (b), singleton-one-of membership: a told member of a class bounded by a singleton one-of merges with the singleton, and a told distinctness against it clashes.</summary>
    [TestMethod]
    public void SingletonOneOfMembershipForcesTheMergeAndClashes()
    {
        ReasoningModule module = Module(
            SubClassOf(Thing, SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("r", 9, null)),
            SubClassOf(Class("B"), OneOf("m")),
            ClassAssertion(Class("B"), Individual("x")),
            Different("x", "m"));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.ClashOnly, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The clash face decides the singleton-membership collapse.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The forced merge collapses a told-distinct pair.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.EnumerationDeciderClashes, "The clash face's counter reads the decision.");
    }

    /// <summary>Forced-merge kind (c), the total-collapse funnel: a single-member funnel under an unqualified at-most-one cap of the same role collapses the whole domain, so any told distinctness clashes — the NOMR-1r shape decided pre-engine, with the window measurement still landing on the clash-decided record (the census ships unconditionally).</summary>
    [TestMethod]
    public void TotalCollapseFunnelForcesTheMergeAndClashes()
    {
        ReasoningModule module = Module(
            SubClassOf(Thing, SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("r", 1, null)),
            ClassAssertion(Class("P"), Individual("o")),
            ClassAssertion(Class("C"), Individual("i1")),
            Different("i1", "o"));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.ClashOnly, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The clash face decides the total collapse.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The collapse merges a told-distinct pair.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.EnumerationDeciderClashes, "The clash face's counter reads the decision.");
        Assert.AreEqual(0L, decision.Statistics.ContextTotals.InferenceAttempts, "The decision is pre-engine.");
        Assert.AreEqual(2, decision.Statistics.ContextTotals.EnumerationCountedPopulation, "The window is measured even on a collapse-decided module.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.EnumerationCapBound, "The measured cap bound rides the clash-decided record.");
    }

    /// <summary>Kind (c) with the explicit <c>owl:Thing</c> filler: a unit cap qualified by <c>owl:Thing</c> is the same unrestricted count as the unqualified spelling, so the total collapse fires identically.</summary>
    [TestMethod]
    public void ThingQualifiedUnitCapCollapsesLikeUnqualified()
    {
        ReasoningModule module = Module(
            SubClassOf(Thing, SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("r", 1, Thing)),
            ClassAssertion(Class("P"), Individual("o")),
            ClassAssertion(Class("C"), Individual("i1")),
            Different("i1", "o"));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.ClashOnly, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The Thing-qualified unit cap decides the total collapse.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The collapse merges the told-distinct pair.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.EnumerationDeciderClashes, "The clash face's counter reads the decision.");
    }

    /// <summary>Kind (c)'s first killer: a MULTI-member funnel under the unit cap never forces the collapse — the disjunctive funnel pins nobody, the judge-verified countermodel stands, and the face is silent.</summary>
    [TestMethod]
    public void MultiMemberFunnelNeverForcesTheCollapse()
    {
        ClausificationResult clausification = ClausifyDecider(
            SubClassOf(Thing, SomeInverse("r", OneOf("o1", "o2"))),
            SubClassOf(OneOf("o1", "o2"), Max("r", 1, null)),
            Different("i1", "o1"));

        Assert.IsFalse(clausification.NominalClash, "A multi-member funnel never licenses the total collapse.");
    }

    /// <summary>Kind (c)'s second killer: a QUALIFIED at-most-one cap never forces the collapse — the qualified form has a judge-verified consistent countermodel and the face is silent.</summary>
    [TestMethod]
    public void QualifiedUnitCapNeverForcesTheCollapse()
    {
        ClausificationResult clausification = ClausifyDecider(
            SubClassOf(Thing, SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("r", 1, Class("F"))),
            Different("i1", "o"));

        Assert.IsFalse(clausification.NominalClash, "A qualified unit cap never licenses the total collapse.");
    }

    /// <summary>Forced-merge kind (d), enumeration membership under a collapsed member set: told sameness collapses the two-member bound, the told member merges with the set's representative, and a told distinctness against it clashes.</summary>
    [TestMethod]
    public void CollapsedEnumerationMembershipForcesTheMergeAndClashes()
    {
        ReasoningModule module = Module(
            SubClassOf(Thing, SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("r", 9, null)),
            SubClassOf(Class("B"), OneOf("m1", "m2")),
            Same("m1", "m2"),
            ClassAssertion(Class("B"), Individual("x")),
            Different("x", "m1"));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.ClashOnly, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The clash face decides the collapsed-membership merge.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The forced merge collapses a told-distinct pair.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.EnumerationDeciderClashes, "The clash face's counter reads the decision.");
    }

    /// <summary>Kind (d)'s killer: an UNCOLLAPSED two-member set never merges — the member could be either element, the module is consistent, and the face is silent.</summary>
    [TestMethod]
    public void UncollapsedEnumerationMembershipNeverMerges()
    {
        ClausificationResult clausification = ClausifyDecider(
            SubClassOf(Thing, SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("r", 9, null)),
            SubClassOf(Class("B"), OneOf("m1", "m2")),
            ClassAssertion(Class("B"), Individual("x")),
            Different("x", "m1"));

        Assert.IsFalse(clausification.NominalClash, "An uncollapsed member set never licenses the merge — the member may be the other element.");
    }

    /// <summary>
    /// The qualified-cap near-miss: only members PROVABLY inside the
    /// filler count toward the bound — of three told-distinct candidates only
    /// one carries a told filler membership, the filtered population is
    /// measured BEFORE the boundary comparison, the residual is silent, and
    /// no clash fires on the under-filled filler.
    /// </summary>
    [TestMethod]
    public void QualifiedCapCountsOnlyProvableFillerMembers()
    {
        ClausificationResult clausification = ClausifyDecider(
            SubClassOf(Thing, SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("r", 1, Class("F"))),
            ClassAssertion(Class("F"), Individual("x1")),
            Different("x1", "x2", "o"));

        Assert.IsFalse(clausification.NominalClash, "The under-filled qualified cap never clashes.");
        Assert.AreEqual(1, clausification.NominalWindow.CountedPopulation, "The filtered population — told filler members only — is the measured quantity.");
    }

    /// <summary>
    /// The forced-alias filler flip-gate row, owed before the clash face
    /// lights: a kind-(b) FORCED alias — never a told one — is the sole
    /// mechanism transferring a told filler membership onto the counted
    /// members. Without the singleton-membership axioms the filter keeps only
    /// the two filler-asserted bystanders, no told-distinct pair survives,
    /// and the face is silent; with them each told-distinct member merges
    /// with a filler-asserted alias, the filter keeps the merged classes, and
    /// the qualified pigeonhole — not the collapse monitor — decides.
    /// </summary>
    [TestMethod]
    public void ForcedAliasFillerMembershipFlipsSilenceToClash()
    {
        ClausificationResult silent = ClausifyDecider(
            SubClassOf(Thing, SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("r", 1, Class("F"))),
            ClassAssertion(Class("F"), Individual("a1")),
            ClassAssertion(Class("F"), Individual("a2")),
            Different("x1", "x2", "o"));

        Assert.IsFalse(silent.NominalClash, "Without the forced aliases no told-distinct member is provably in the filler — the face is silent.");
        Assert.AreEqual(2, silent.NominalWindow.CountedPopulation, "The filter keeps only the two told filler members.");

        ClausificationResult flipped = ClausifyDecider(
            SubClassOf(Thing, SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("r", 1, Class("F"))),
            SubClassOf(Class("A1"), OneOf("x1")),
            SubClassOf(Class("A2"), OneOf("x2")),
            ClassAssertion(Class("A1"), Individual("a1")),
            ClassAssertion(Class("A2"), Individual("a2")),
            ClassAssertion(Class("F"), Individual("a1")),
            ClassAssertion(Class("F"), Individual("a2")),
            Different("x1", "x2", "o"));

        Assert.IsTrue(flipped.NominalClash, "The forced aliases transfer the filler memberships and the qualified cap clashes.");
        Assert.Contains("NominalCountingPigeonhole", flipped.NominalClashReason!, "The counting comparison, not the collapse monitor, decides the flip.");
        Assert.AreEqual(4, flipped.NominalWindow.CountedPopulation, "The filter keeps both merged classes — each member with its filler-asserted alias.");
        Assert.AreEqual(2, flipped.NominalWindow.DistinctCliqueSize, "The told-distinct pair inside the filtered population exceeds the unit bound.");
    }

    /// <summary>The empty-filler near-miss: a cap qualified by <c>owl:Nothing</c> is an unconditional non-constraint — never a clash, any clique size — and the cap is skipped entirely.</summary>
    [TestMethod]
    public void NothingFillerCapIsNeverAConstraint()
    {
        ClausificationResult clausification = ClausifyDecider(
            SubClassOf(Thing, SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("r", 2, Nothing)),
            Different("i1", "i2", "i3", "i4"));

        Assert.IsFalse(clausification.NominalClash, "A provably-empty filler makes the cap an unconditional non-constraint.");
    }

    /// <summary>The empty-one-of near-miss: an empty funnel filler is out of jurisdiction — the inconsistency there rides domain non-emptiness, an unrelated mechanism — so the face is silent and ordinary machinery owns the module without ever answering a wrong CONSISTENT.</summary>
    [TestMethod]
    public void EmptyOneOfFunnelStaysWithSaturation()
    {
        OwlObjectOneOf empty = new([]);
        ReasoningModule module = Module(
            SubClassOf(Thing, new OwlObjectSomeValuesFrom(InverseProperty("r"), empty)),
            SubClassOf(empty, Max("r", 2, null)),
            Different("i1", "i2", "i3"));
        ClausificationResult clausification = ContextClausifier.Clausify(module, EqualityLowering.GeneralClause, DatatypeRegistry.Empty, [], riderEnabled: false, nominalDeciderEnabled: true);
        Assert.IsFalse(clausification.NominalClash, "The empty-one-of funnel is out of the face's jurisdiction.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.ClashOnly, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        Assert.AreEqual(0, decision.Statistics.ContextTotals.EnumerationDeciderClashes, "The face stays silent end to end.");
        Assert.IsFalse(decision.Verdict is { IsConsistent: true }, "The empty-funnel module is never wrongly certified consistent.");
    }

    /// <summary>The union-guarded near-miss: a funnel under a union pins nobody — the recognizer never matches subterms, the habitat is not claimed, and the face is silent on the consistent module.</summary>
    [TestMethod]
    public void UnionGuardedFunnelPinsNobodyAndStaysSilent()
    {
        ReasoningModule module = Module(
            SubClassOf(Thing, Union(Class("A"), SomeInverse("r", OneOf("o")))),
            SubClassOf(OneOf("o"), Max("r", 2, null)),
            Different("i1", "i2", "i3"));
        ClausificationResult clausification = ContextClausifier.Clausify(module, EqualityLowering.GeneralClause, DatatypeRegistry.Empty, [], riderEnabled: false, nominalDeciderEnabled: true);

        Assert.IsFalse(clausification.NominalClash, "A disjunctive funnel pins nobody — the face is silent.");
        Assert.AreEqual(EnumerationHabitatClass.None, ContextModuleSurvey.Survey(module).EnumerationHabitat, "The recognizer never matches a funnel subterm.");
    }

    /// <summary>The chain-funnel row: a told subclass chain from <c>owl:Thing</c> through named hops reaches the funnel and the clash face decides — the positive chain face beside the disjunctive-hop killer.</summary>
    [TestMethod]
    public void SubclassChainFunnelDecidesThroughNamedHops()
    {
        ReasoningModule module = Module(
            SubClassOf(Thing, Class("B1")),
            SubClassOf(Class("B1"), Class("B2")),
            SubClassOf(Class("B2"), SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("r", 2, null)),
            Different("i1", "i2", "i3"));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.ClashOnly, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The chain funnel decides.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The chain-reached funnel carries the same pigeonhole.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.EnumerationDeciderClashes, "The clash face decided through the chain.");
        Assert.AreEqual(EnumerationHabitatClass.NominalCounting, decision.Statistics.ContextTotals.EnumerationHabitat, "The recognizer sees the chained shape.");
    }

    /// <summary>The disjunctive-terminal-hop killer [R2B-N2]: a chain whose hop passes through a boolean combinator is never followed — every hop must be exactly the next named class or the funnel directly — so the face is silent on the consistent module.</summary>
    [TestMethod]
    public void DisjunctiveTerminalHopSilencesTheChain()
    {
        ClausificationResult clausification = ClausifyDecider(
            SubClassOf(Thing, Class("B1")),
            SubClassOf(Class("B1"), Union(Class("B2"), SomeInverse("r", OneOf("o")))),
            SubClassOf(OneOf("o"), Max("r", 2, null)),
            Different("i1", "i2", "i3"));

        Assert.IsFalse(clausification.NominalClash, "A disjunctive hop breaks the chain — the face is silent.");
    }

    /// <summary>The super-role near-miss: the cap role must be TEXTUALLY the funnel role — a cap on a told super-role stays silent, parked in backlog behind its own role-hierarchy-composition lemma.</summary>
    [TestMethod]
    public void SuperRoleCapStaysSilentPendingItsOwnLemma()
    {
        ClausificationResult clausification = ClausifyDecider(
            SubClassOf(Thing, SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("s", 2, null)),
            SubRole("r", "s"),
            Different("i1", "i2", "i3"));

        Assert.IsFalse(clausification.NominalClash, "The super-role cap is out of this lock's jurisdiction — silence, backlog pointer.");
    }

    /// <summary>The told-edge source row: with a multi-member funnel attributing nobody, the counted population comes exclusively from told edges of the funnel role — three told-distinct told-edge targets against a cap of two decide.</summary>
    [TestMethod]
    public void ToldEdgesSupplementTheCountedPopulation()
    {
        ReasoningModule module = Module(
            SubClassOf(Thing, SomeInverse("r", OneOf("o1", "o2"))),
            SubClassOf(OneOf("o1", "o2"), Max("r", 2, null)),
            Edge("r", "o1", "x1"),
            Edge("r", "o1", "x2"),
            Edge("r", "o1", "x3"),
            Different("x1", "x2", "x3"));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.ClashOnly, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The told-edge population decides.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Three told-distinct told-edge successors under a cap of two clash.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.EnumerationDeciderClashes, "The clash face decided off the told edges alone.");
    }

    /// <summary>The derived-distinctness near-miss: distinctness via disjoint class assertions is DERIVED, not told — the clash monitor reads told <c>DifferentIndividuals</c> only, the face is silent, and the future forced-distinctness kind stays a named backlog pointer.</summary>
    [TestMethod]
    public void DerivedDistinctnessStaysSilentPendingItsOwnKind()
    {
        ClausificationResult clausification = ClausifyDecider(
            SubClassOf(Thing, SomeInverse("r", OneOf("o"))),
            SubClassOf(OneOf("o"), Max("r", 2, null)),
            Disjoint(Class("A"), Class("B"), Class("C")),
            ClassAssertion(Class("A"), Individual("i1")),
            ClassAssertion(Class("B"), Individual("i2")),
            ClassAssertion(Class("C"), Individual("i3")));

        Assert.IsFalse(clausification.NominalClash, "Derived distinctness never feeds the told clash monitor — silence, backlog pointer.");
    }

    /// <summary>
    /// The funnel-chain hop window: a funnel at exactly the sixteenth named
    /// hop decides, and a funnel one hop past the bound is silent with the
    /// chain-hop counter charged — the boundary pinned on both sides.
    /// </summary>
    [TestMethod]
    public void FunnelChainHopBoundDecidesAtAndSilencesPastTheBound()
    {
        ClausificationResult atBound = ContextClausifier.Clausify(ChainFunnelModule(ContextNominalCountingDecider.FunnelChainHopBound), EqualityLowering.GeneralClause, DatatypeRegistry.Empty, [], riderEnabled: false, nominalDeciderEnabled: true);
        Assert.IsTrue(atBound.NominalClash, "The funnel at exactly the hop bound decides.");
        Assert.AreEqual(0, atBound.NominalWindow.ChainHopSilences, "No walk was abandoned at the bound.");

        ClausificationResult pastBound = ContextClausifier.Clausify(ChainFunnelModule(ContextNominalCountingDecider.FunnelChainHopBound + 1), EqualityLowering.GeneralClause, DatatypeRegistry.Empty, [], riderEnabled: false, nominalDeciderEnabled: true);
        Assert.IsFalse(pastBound.NominalClash, "The funnel past the hop bound is silent — never a verdict over an unwalked chain.");
        Assert.IsGreaterThanOrEqualTo(1, pastBound.NominalWindow.ChainHopSilences, "The silence is charged to its named window counter.");
    }

    /// <summary>
    /// The counted-population window: a told-edge population of exactly the
    /// bound decides against a cap one below it, and a population one past the
    /// bound is silent with the population counter charged — completeness AT
    /// the bound, silence past it.
    /// </summary>
    [TestMethod]
    public void CountedPopulationBoundaryDecidesAtAndSilencesPastTheBound()
    {
        ClausificationResult atBound = ContextClausifier.Clausify(EdgePopulationModule(ContextNominalCountingDecider.CountedPopulationBound, ContextNominalCountingDecider.CountedPopulationBound - 1), EqualityLowering.GeneralClause, DatatypeRegistry.Empty, [], riderEnabled: false, nominalDeciderEnabled: true);
        Assert.IsTrue(atBound.NominalClash, "The population at exactly the bound is searched and decides.");
        Assert.AreEqual(ContextNominalCountingDecider.CountedPopulationBound, atBound.NominalWindow.CountedPopulation, "The measured population sits exactly at the bound.");

        ClausificationResult pastBound = ContextClausifier.Clausify(EdgePopulationModule(ContextNominalCountingDecider.CountedPopulationBound + 1, ContextNominalCountingDecider.CountedPopulationBound), EqualityLowering.GeneralClause, DatatypeRegistry.Empty, [], riderEnabled: false, nominalDeciderEnabled: true);
        Assert.IsFalse(pastBound.NominalClash, "The population past the bound is never searched — silence, never a verdict.");
        Assert.AreEqual(1, pastBound.NominalWindow.PopulationSilences, "The silence is charged to its named window counter.");
    }

    /// <summary>
    /// The window-constant derivation pins: the counted-population
    /// bound equals the ground rider's clique ceiling (one boundary
    /// discipline across both counting faces) and its 2^16 combination
    /// ceiling is enumerated exactly; the member-universe bound's Bell(8)
    /// partition count is enumerated exactly; the signature-class bound's
    /// ordered-pair set fills exactly one 64-bit refutation mask.
    /// </summary>
    [TestMethod]
    public void WindowConstantDerivationsArePinned()
    {
        //The two counting faces share one boundary discipline: the sweep runs to
        //the decider's population bound and the expectation is stated over the
        //ground rider's clique ceiling, so any drift between the two constants
        //fails this enumeration.
        using VeritasMemoryPool<int> pool = new();
        long combinations = 0;
        for(int size = 0; size <= ContextNominalCountingDecider.CountedPopulationBound; size++)
        {
            using CombinationIndexEnumerator sweep = CombinationIndexEnumerator.Create(pool, ContextNominalCountingDecider.CountedPopulationBound, size);
            while(sweep.MoveNext())
            {
                combinations++;
            }
        }

        Assert.AreEqual(1L << ContextClausifier.GroundCountingCliqueBound, combinations, "The clique sweep's total combination ceiling is 2^16 — the documented cost formula, enumerated over the shared bound.");

        long partitions = 0;
        using PartitionGrowthEnumerator bell = PartitionGrowthEnumerator.Create(pool, ContextEnumerationAlgebraDecider.MemberUniverseBound);
        while(bell.MoveNext())
        {
            partitions++;
        }

        Assert.AreEqual(4140L, partitions, "Bell(8) = 4,140 — the partition-sweep term of the documented cost formula, enumerated.");
    }

    /// <summary>
    /// The signature-class bound's completeness AT the bound: a module with
    /// exactly eight named classes exercises the full 64-bit refutation mask
    /// and the assignment sweep at its documented width, and the certifying
    /// face still decides — every candidate pair refuted by the unconstrained
    /// assignments, the exact set empty.
    /// </summary>
    [TestMethod]
    public void SignatureClassBoundDecidesAtFullMaskWidth()
    {
        ReasoningModule module = Module(
            Equivalent(Class("C1"), OneOf("a")),
            SubClassOf(Class("C2"), Thing),
            SubClassOf(Class("C3"), Thing),
            SubClassOf(Class("C4"), Thing),
            SubClassOf(Class("C5"), Thing),
            SubClassOf(Class("C6"), Thing),
            SubClassOf(Class("C7"), Thing),
            SubClassOf(Class("C8"), Thing));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.Certifying, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The face decides at exactly the class bound.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The module is consistent.");
        Assert.AreEqual(1, decision.Statistics.ContextTotals.EnumerationDeciderCertifications, "The certifying face decided at full mask width.");
        Assert.IsEmpty(decision.Verdict.Subsumptions, "Every candidate pair is refuted by the unconstrained assignments.");
    }

    /// <summary>The combination kernel's pins: C(5,3) enumerates its ten combinations in exact lexicographic order, a zero-size subset enumerates one empty combination, and an oversized subset enumerates nothing.</summary>
    [TestMethod]
    public void CombinationEnumeratorSweepsLexicographically()
    {
        using VeritasMemoryPool<int> pool = new();
        List<string> sequence = [];
        using CombinationIndexEnumerator sweep = CombinationIndexEnumerator.Create(pool, 5, 3);
        while(sweep.MoveNext())
        {
            sequence.Add(string.Join("", sweep.Current.ToArray()));
        }

        Assert.AreSequenceEqual(new List<string> { "012", "013", "014", "023", "024", "034", "123", "124", "134", "234" }, sequence, "C(5,3) in lexicographic order.");

        using CombinationIndexEnumerator emptySubset = CombinationIndexEnumerator.Create(pool, 4, 0);
        Assert.IsTrue(emptySubset.MoveNext(), "The zero-size subset enumerates one empty combination.");
        Assert.IsTrue(emptySubset.Current.IsEmpty, "The empty combination has no indices.");
        Assert.IsFalse(emptySubset.MoveNext(), "Exactly one empty combination.");

        using CombinationIndexEnumerator oversized = CombinationIndexEnumerator.Create(pool, 2, 3);
        Assert.IsFalse(oversized.MoveNext(), "An oversized subset enumerates nothing.");
    }

    /// <summary>The partition kernel's pins: three elements enumerate exactly the five restricted growth strings in order with correct block counts, zero elements enumerate one empty partition, and Bell(4) counts fifteen.</summary>
    [TestMethod]
    public void PartitionEnumeratorSweepsRestrictedGrowthStrings()
    {
        using VeritasMemoryPool<int> pool = new();
        List<string> sequence = [];
        List<int> blockCounts = [];
        using PartitionGrowthEnumerator sweep = PartitionGrowthEnumerator.Create(pool, 3);
        while(sweep.MoveNext())
        {
            sequence.Add(string.Join("", sweep.Current.ToArray()));
            blockCounts.Add(sweep.BlockCount);
        }

        Assert.AreSequenceEqual(new List<string> { "000", "001", "010", "011", "012" }, sequence, "Bell(3) = 5 restricted growth strings in order.");
        Assert.AreSequenceEqual(new List<int> { 1, 2, 2, 2, 3 }, blockCounts, "The block counts read off the prefix-maximum shadow.");

        using PartitionGrowthEnumerator empty = PartitionGrowthEnumerator.Create(pool, 0);
        Assert.IsTrue(empty.MoveNext(), "Zero elements enumerate one empty partition.");
        Assert.IsFalse(empty.MoveNext(), "Exactly one empty partition.");

        long bellFour = 0;
        using PartitionGrowthEnumerator four = PartitionGrowthEnumerator.Create(pool, 4);
        while(four.MoveNext())
        {
            bellFour++;
        }

        Assert.AreEqual(15L, bellFour, "Bell(4) = 15.");
    }

    /// <summary>
    /// The rider refactor's A/B harness: the original in-place
    /// odometer and its shared-surface twin answer bit-for-bit identically
    /// over an exhaustive deterministic sweep — every node count to the PIG
    /// boundary, every subset size to one past the count, and three
    /// distinctness patterns including the under-filled ones.
    /// </summary>
    [TestMethod]
    public void RiderCliqueSurfaceArmIsBitForBitIdentical()
    {
        for(int nodeCount = 0; nodeCount <= 8; nodeCount++)
        {
            List<Utf8String> nodes = [];
            for(int i = 0; i < nodeCount; i++)
            {
                nodes.Add(Utf8Strings.From("n" + i));
            }

            for(int pattern = 0; pattern < 3; pattern++)
            {
                HashSet<(Utf8String First, Utf8String Second)> distinct = BuildDistinctPattern(nodes, pattern);
                for(int size = 0; size <= nodeCount + 1; size++)
                {
                    bool inPlace = ContextClausifier.HasDistinctClique(nodes, size, distinct, onEnumeratorSurface: false);
                    bool onSurface = ContextClausifier.HasDistinctClique(nodes, size, distinct, onEnumeratorSurface: true);

                    Assert.AreEqual(inPlace, onSurface, $"The two arms diverge at nodes={nodeCount}, size={size}, pattern={pattern}.");
                }
            }
        }
    }

    /// <summary>
    /// The certifying face's refutation arm: a singleton enumeration whose
    /// told member is told-distinct from a told instance leaves no admissible
    /// partition — every candidate either merges the told-distinct pair or
    /// violates the assertion — so the face decides INCONSISTENT pre-engine
    /// with its refutation counter read.
    /// </summary>
    [TestMethod]
    public void CertifyingFaceRefutesTheUnsatisfiableEnumeration()
    {
        ReasoningModule module = Module(
            Equivalent(Class("C"), OneOf("a")),
            ClassAssertion(Class("C"), Individual("b")),
            Different("a", "b"));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.Certifying, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The certifying face refutes the module.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The told instance is forced into the singleton it is told distinct from.");
        Assert.AreEqual(1, totals.EnumerationDeciderRefutations, "The refutation counter reads the decision.");
        Assert.AreEqual(0, totals.EnumerationDeciderCertifications, "No certification on a refuted module.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "The refutation is pre-engine.");
    }

    /// <summary>
    /// The window silences ride the STATISTICS records end to end (CEN-1):
    /// the past-bound chain module and the past-bound population module,
    /// driven through the reasoner under a small budget, carry their named
    /// window-exceeded counters on the abstention records.
    /// </summary>
    [TestMethod]
    public void WindowSilenceCountersRideTheStatisticsRecords()
    {
        ReasoningBudget small = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1000);
        ModuleDecision chainDecision = ContextSaturationModuleReasoner.DecideModule(ChainFunnelModule(ContextNominalCountingDecider.FunnelChainHopBound + 1), EnumerationDeciderFaces.ClashOnly, small, TestContext.CancellationToken);
        Assert.IsGreaterThanOrEqualTo(1, chainDecision.Statistics.ContextTotals.EnumerationWindowExceededChainHops, "The chain-hop silence rides the statistics record.");
        Assert.AreEqual(0, chainDecision.Statistics.ContextTotals.EnumerationDeciderClashes, "The face stayed silent past the hop bound.");

        ModuleDecision populationDecision = ContextSaturationModuleReasoner.DecideModule(EdgePopulationModule(ContextNominalCountingDecider.CountedPopulationBound + 1, ContextNominalCountingDecider.CountedPopulationBound), EnumerationDeciderFaces.ClashOnly, small, TestContext.CancellationToken);
        Assert.AreEqual(1, populationDecision.Statistics.ContextTotals.EnumerationWindowExceededPopulation, "The population silence rides the statistics record.");
        Assert.AreEqual(0, populationDecision.Statistics.ContextTotals.EnumerationDeciderClashes, "The face stayed silent past the population bound.");
    }

    /// <summary>
    /// Per-cap-axiom evaluation is INDEPENDENT [R2B-N3]: two caps on the
    /// same anchor and role, each with its own filler and bound. Neither
    /// clashes alone — one has a large clique under a large bound, the other
    /// a small filtered clique under a small bound — and cross-pairing the
    /// first cap's clique with the second cap's bound would wrongly clash;
    /// the silence pins the per-axiom F-to-k binding. The companion positive
    /// face: tightening the second cap's own filler population makes exactly
    /// that axiom clash on its own numbers.
    /// </summary>
    [TestMethod]
    public void PerCapAxiomEvaluationIsIndependent()
    {
        ClausificationResult silent = ClausifyDecider(
            SubClassOf(Thing, SomeInverse("r", OneOf("o1", "o2"))),
            SubClassOf(OneOf("o1", "o2"), Max("r", 3, Class("F"))),
            SubClassOf(OneOf("o1", "o2"), Max("r", 1, Class("G"))),
            Edge("r", "o1", "x1"),
            Edge("r", "o1", "x2"),
            Edge("r", "o1", "x3"),
            Edge("r", "o1", "y1"),
            ClassAssertion(Class("F"), Individual("x1")),
            ClassAssertion(Class("F"), Individual("x2")),
            ClassAssertion(Class("F"), Individual("x3")),
            ClassAssertion(Class("G"), Individual("y1")),
            Different("x1", "x2", "x3"));
        Assert.IsFalse(silent.NominalClash, "Neither cap clashes on its own numbers — the F-clique never meets the G-bound.");

        ClausificationResult decided = ClausifyDecider(
            SubClassOf(Thing, SomeInverse("r", OneOf("o1", "o2"))),
            SubClassOf(OneOf("o1", "o2"), Max("r", 3, Class("F"))),
            SubClassOf(OneOf("o1", "o2"), Max("r", 1, Class("G"))),
            Edge("r", "o1", "y1"),
            Edge("r", "o1", "y2"),
            ClassAssertion(Class("G"), Individual("y1")),
            ClassAssertion(Class("G"), Individual("y2")),
            Different("y1", "y2"));
        Assert.IsTrue(decided.NominalClash, "The G-cap clashes on its own filtered clique against its own bound.");
    }

    /// <summary>The member-universe bound's completeness AT the bound: exactly eight enumerated individuals are searched — Bell(8) partitions — and the certifying face decides.</summary>
    [TestMethod]
    public void MemberUniverseAtBoundDecides()
    {
        ReasoningModule module = Module(
            Equivalent(Class("C"), OneOf("a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8")));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.Certifying, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "The face decides at exactly the member bound.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The plain enumeration is consistent.");
        Assert.AreEqual(1, totals.EnumerationDeciderCertifications, "The certifying face decided at the bound.");
        Assert.AreEqual(8, totals.EnumerationMemberUniverse, "The measured universe sits exactly at the bound.");
        Assert.AreEqual(0, totals.EnumerationWindowExceededMembers, "No silence at the bound.");
    }

    /// <summary>
    /// The P-S1 seat pin: a lit face decides IDENTICALLY under both
    /// paramodulation scopes and both root-tier topologies — a pre-engine
    /// decision constructs no engine, so no engine axis can move it. NOMR-2's
    /// clash and ENUM-1's certificate are pinned across the matrix with zero
    /// attempts everywhere.
    /// </summary>
    [TestMethod]
    public void SeatIsUpstreamOfTopologyAndScope()
    {
        (NominalParamodulationScope Scope, RootContextTopology Topology)[] cells =
        [
            (NominalParamodulationScope.QueryScoped, RootContextTopology.SingleRoot),
            (NominalParamodulationScope.QueryScoped, RootContextTopology.PerIndividualRoots),
            (NominalParamodulationScope.Unrestricted, RootContextTopology.SingleRoot),
            (NominalParamodulationScope.Unrestricted, RootContextTopology.PerIndividualRoots),
        ];
        foreach((NominalParamodulationScope scope, RootContextTopology topology) in cells)
        {
            ModuleDecision clash = ContextSaturationModuleReasoner.DecideModule(ContextNominalBatteryTests.Nomr2Module(), EnumerationDeciderFaces.ClashOnly, scope, topology, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
            Assert.AreEqual(ReasoningDecisionOutcome.Decided, clash.Outcome, $"NOMR-2 decides under {scope}/{topology}.");
            Assert.IsFalse(clash.Verdict!.IsConsistent, $"NOMR-2's verdict is inconsistent under {scope}/{topology}.");
            Assert.AreEqual(0L, clash.Statistics.ContextTotals.InferenceAttempts, $"Zero attempts under {scope}/{topology} — no engine existed.");

            ModuleDecision certificate = ContextSaturationModuleReasoner.DecideModule(ContextNominalBatteryTests.Enum1Module(), EnumerationDeciderFaces.Certifying, scope, topology, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
            Assert.AreEqual(ReasoningDecisionOutcome.Decided, certificate.Outcome, $"ENUM-1 decides under {scope}/{topology}.");
            Assert.IsTrue(certificate.Verdict!.IsConsistent, $"ENUM-1's verdict is consistent under {scope}/{topology}.");
            AssertSubsumptionKeys(certificate.Verdict, [BatterySub("C", "D"), BatterySub("D", "C")]);
            Assert.AreEqual(0L, certificate.Statistics.ContextTotals.InferenceAttempts, $"Zero attempts under {scope}/{topology} — no engine existed.");
        }
    }

    /// <summary>
    /// The verdict-identity sweep: every certified
    /// nominal-battery row decided under the explicit dark control and under
    /// both lit faces, across both paramodulation scopes and both root-tier
    /// topologies. A row the lit decider decides pre-engine must carry the
    /// dark run's exact verdict and subsumption set with zero attempts;
    /// every other row must be identical in outcome, verdict, subsumption
    /// set, and attempt count — the decider's silence leaves the engine path
    /// untouched, so the lit production default moves no certified verdict
    /// anywhere on the matrix.
    /// </summary>
    [TestMethod]
    public void LitFacesMoveNoCertifiedVerdictAcrossTheMatrix()
    {
        (NominalParamodulationScope Scope, RootContextTopology Topology)[] cells =
        [
            (NominalParamodulationScope.QueryScoped, RootContextTopology.SingleRoot),
            (NominalParamodulationScope.QueryScoped, RootContextTopology.PerIndividualRoots),
            (NominalParamodulationScope.Unrestricted, RootContextTopology.SingleRoot),
            (NominalParamodulationScope.Unrestricted, RootContextTopology.PerIndividualRoots),
        ];
        List<string> mismatches = [];
        int deciderDecided = 0;
        foreach((string name, ReasoningModule module, bool _, string[] _) in ContextNominalBatteryTests.BatteryRows())
        {
            foreach((NominalParamodulationScope scope, RootContextTopology topology) in cells)
            {
                string cell = name + "@" + scope + "/" + topology;
                ModuleDecision dark = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, scope, topology, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
                ModuleDecision lit = ContextSaturationModuleReasoner.DecideModule(module, BothFaces, scope, topology, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
                ContextSaturationStatistics litTotals = lit.Statistics.ContextTotals;
                bool preEngine = litTotals.EnumerationDeciderClashes + litTotals.EnumerationDeciderCertifications + litTotals.EnumerationDeciderRefutations > 0;
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

                if(preEngine)
                {
                    deciderDecided++;
                    if(litTotals.InferenceAttempts != 0L)
                    {
                        mismatches.Add(cell + ": a decider-decided run spent engine attempts (" + litTotals.InferenceAttempts + ").");
                    }

                    continue;
                }

                if(litTotals.InferenceAttempts != dark.Statistics.ContextTotals.InferenceAttempts)
                {
                    mismatches.Add(cell + ": a silent-face run moved the attempt count (" + dark.Statistics.ContextTotals.InferenceAttempts + " -> " + litTotals.InferenceAttempts + ").");
                }
            }
        }

        TestContext.WriteLine("Verdict-identity sweep: " + deciderDecided + " matrix cells decided pre-engine, zero certified movement.");
        Assert.IsGreaterThan(0, deciderDecided, "The lit faces decide at least one certified habitat cell pre-engine — the sweep instruments a lit decider.");
        Assert.IsEmpty(mismatches, string.Join(Environment.NewLine, mismatches));
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

    /// <summary>The recognizer's none path: a nominal-free ground module and a nominal module without funnel, cap, or algebra shape both classify as none — the habitat class never over-claims.</summary>
    [TestMethod]
    public void HabitatClassIsNoneOffTheHabitat()
    {
        ReasoningModule ground = Module(
            SubClassOf(Class("B"), Max("r", 2, null)),
            ClassAssertion(Class("B"), Individual("b")),
            Edge("r", "b", "t1"),
            Edge("r", "b", "t2"));
        Assert.AreEqual(EnumerationHabitatClass.None, ContextModuleSurvey.Survey(ground).EnumerationHabitat, "A nominal-free module is none on the zero-allocation path.");

        ReasoningModule hasValueOnly = Module(
            SubClassOf(Class("A"), HasValue("r", "o")),
            ClassAssertion(Class("A"), Individual("x")));
        Assert.AreEqual(EnumerationHabitatClass.None, ContextModuleSurvey.Survey(hasValueOnly).EnumerationHabitat, "A nominal module without the habitat shapes is none.");
    }

    /// <summary>
    /// The funnel-and-cap answers taken on the recognizer's own entry point with
    /// the census bits supplied directly: NOMR-2's funnel beside its
    /// one-of-anchored cap reads nominal counting, the same cluster beside an
    /// enumeration-algebra cluster reads mixed, and a funnel told WITHOUT its cap
    /// reads none — the cap guard trips, the whole nominal fall-through chain
    /// runs, and every probe on it declines in turn. These are gate-0b freeze
    /// instruments for census states the corpus sweep cannot see: no swept module
    /// stands in the nominal-counting or the mixed state, so the direct call is
    /// the only place those two answers are held still.
    /// </summary>
    [TestMethod]
    public void FunnelCapAndFallThroughRowsAnswerOnTheDirectClassifyCall()
    {
        (string Name, ReasoningModule Module, bool MentionsNominals, bool MentionsCounting, EnumerationHabitatClass Expected)[] rows =
        [
            ("FunnelAndCap", ContextNominalBatteryTests.Nomr2Module(), true, true, EnumerationHabitatClass.NominalCounting),
            ("FunnelCapAndAlgebra", Extend(ContextNominalBatteryTests.Nomr2Module(), Equivalent(Class("C"), OneOf("a", "b")), Equivalent(Class("D"), OneOf("b")), Equivalent(Class("C"), Class("D"))), true, true, EnumerationHabitatClass.Mixed),
            ("FunnelWithoutCap", Module(SubClassOf(Thing, SomeInverse("r", OneOf("o"))), Different("i1", "i2", "i3")), true, false, EnumerationHabitatClass.None),
        ];
        foreach((string name, ReasoningModule module, bool mentionsNominals, bool mentionsCounting, EnumerationHabitatClass expected) in rows)
        {
            Assert.AreEqual(expected, ContextHabitatRecognizer.Classify(module: module, mentionsNominals: mentionsNominals, mentionsCounting: mentionsCounting), name + ": the recognizer answers the row's habitat class at the told census bits.");
        }
    }

    /// <summary>
    /// The asymmetric-admission pins: at the census state where nominals are
    /// mentioned and counting is not — the exact pair of bits each module below
    /// induces, since a one-of and a value pin each set the nominal mention while
    /// neither module tells a cardinality restriction or a counting
    /// characteristic — the nominal path admits Shape W and Shape R
    /// UNCONDITIONALLY, no counting gate standing over either probe there though
    /// both sit behind one on the nominal-free path. The told-ground witness
    /// module carries that probe's three told ingredients — an object-property
    /// assertion, an inverse pair over plain roles, and a top-level plain-role
    /// existential in subclass position — and the restriction-rich ground module
    /// carries a universal and a value pin in obligation position over seventeen
    /// distinct told individuals. Every earlier probe declines on both: no
    /// whole-module enumeration admission, no one-of-anchored cap, no composition
    /// layer, no spy-point cap signal, and no functional or inverse-functional
    /// characteristic. These are gate-0b freeze instruments for a census state
    /// the corpus sweep cannot see: no shipped row exercises it, so an inverted
    /// admission column on either probe is visible here alone.
    /// </summary>
    [TestMethod]
    public void NominalPathAdmitsShapeWAndShapeRWithoutTheCountingMention()
    {
        ReasoningModule toldGroundWitness = Module(
            SubClassOf(Class("Witness"), new OwlObjectSomeValuesFrom(Property("r"), Thing)),
            new OwlInverseObjectPropertiesAxiom(Property("r"), Property("s")) { Origin = Origin("inverse") },
            Edge("r", "a", "b"),
            Equivalent(Class("Anchor"), OneOf("a")));
        ReasoningModule restrictionRichGround = Module(
            SubClassOf(Class("Ground"), new OwlObjectAllValuesFrom(Property("q"), Class("GroundFiller"))),
            SubClassOf(Class("Ground"), HasValue("q", "pin")),
            Different("g1", "g2", "g3", "g4", "g5", "g6", "g7", "g8", "g9", "g10", "g11", "g12", "g13", "g14", "g15", "g16", "g17"));
        (string Name, ReasoningModule Module, EnumerationHabitatClass Expected)[] rows =
        [
            ("ToldGroundWitness", toldGroundWitness, EnumerationHabitatClass.ToldGroundWitness),
            ("RestrictionRichGround", restrictionRichGround, EnumerationHabitatClass.RestrictionRichGround),
        ];
        foreach((string name, ReasoningModule module, EnumerationHabitatClass expected) in rows)
        {
            Assert.AreEqual(expected, ContextHabitatRecognizer.Classify(module: module, mentionsNominals: true, mentionsCounting: false), name + ": the nominal path admits the probe with the counting mention clear.");
        }
    }

    /// <summary>
    /// The nominal-free answers taken on the recognizer's own entry point at
    /// both counting states: with the counting mention clear a plain told
    /// subclass module reads none — the opening modal-gadget probe and the
    /// closing modal role-expansion tail both decline and the five counting
    /// probes are skipped — and with the counting mention set a module carrying
    /// the bijection-chain signal — one role told functional that also stands in
    /// a told inverse pair and heads a told existential — reads the
    /// bijection-chain arithmetic through the counting-admitted probes. These are
    /// gate-0b freeze instruments completing real-module coverage of the census
    /// domain's nominal-free half on the recognizer's direct entry point.
    /// </summary>
    [TestMethod]
    public void NominalFreePathAnswersAreHeldStillAtBothCountingStates()
    {
        ReasoningModule plainSubclass = Module(SubClassOf(Class("A"), Class("B")));
        ReasoningModule bijectionChain = Module(
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, Property("r")) { Origin = Origin("functional") },
            new OwlInverseObjectPropertiesAxiom(Property("r"), Property("s")) { Origin = Origin("inverse") },
            SubClassOf(Class("A"), new OwlObjectSomeValuesFrom(Property("r"), Class("B"))));
        (string Name, ReasoningModule Module, bool MentionsCounting, EnumerationHabitatClass Expected)[] rows =
        [
            ("NominalFreeWithoutCounting", plainSubclass, false, EnumerationHabitatClass.None),
            ("NominalFreeWithCounting", bijectionChain, true, EnumerationHabitatClass.BijectionChainArithmetic),
        ];
        foreach((string name, ReasoningModule module, bool mentionsCounting, EnumerationHabitatClass expected) in rows)
        {
            Assert.AreEqual(expected, ContextHabitatRecognizer.Classify(module: module, mentionsNominals: false, mentionsCounting: mentionsCounting), name + ": the nominal-free path answers the row's habitat class at the told census bits.");
        }
    }

    /// <summary>
    /// The registry order pin: the probe table holds exactly eleven rows, its
    /// label sequence read top to bottom IS the answer order the walk takes, and
    /// the nominal-funnel row is the only row declaring a second label. A row's
    /// array POSITION is the whole ordering surface, so a re-ordered, dropped,
    /// added or mis-labelled registration is visible here.
    /// </summary>
    [TestMethod]
    public void ProbeOrderIsTheElevenDeclaredRowsInAnswerOrder()
    {
        EnumerationHabitatClass[] expectedLabels =
        [
            EnumerationHabitatClass.EnumerationAlgebra,
            EnumerationHabitatClass.NominalCounting,
            EnumerationHabitatClass.ModalGadgetTree,
            EnumerationHabitatClass.BooleanCardinalityGadget,
            EnumerationHabitatClass.PartitionCounting,
            EnumerationHabitatClass.SpyPointDomainBound,
            EnumerationHabitatClass.BijectionChainArithmetic,
            EnumerationHabitatClass.RestrictionRichGround,
            EnumerationHabitatClass.ToldGroundWitness,
            EnumerationHabitatClass.ModalRoleExpansion,
            EnumerationHabitatClass.NominalPinnedRole,
        ];
        EnumerationHabitatClass[] expectedAlternates =
        [
            EnumerationHabitatClass.None,
            EnumerationHabitatClass.Mixed,
            EnumerationHabitatClass.None,
            EnumerationHabitatClass.None,
            EnumerationHabitatClass.None,
            EnumerationHabitatClass.None,
            EnumerationHabitatClass.None,
            EnumerationHabitatClass.None,
            EnumerationHabitatClass.None,
            EnumerationHabitatClass.None,
            EnumerationHabitatClass.None,
        ];
        ReadOnlySpan<HabitatProbeEntry> rows = ContextHabitatRecognizer.ProbeOrder;
        int rowCount = rows.Length;

        Assert.AreEqual(11, rowCount, "The registry holds exactly eleven habitat probe rows.");
        Assert.AreEqual(expectedLabels.Length, rowCount, "The declared label sequence covers every registered row.");
        for(int index = 0; index < expectedLabels.Length; index++)
        {
            Assert.AreEqual(expectedLabels[index], rows[index].Label, $"Registry position {index}: the row's label is the declared one, and its position is the answer order.");
            Assert.AreEqual(expectedAlternates[index], rows[index].AlternateLabel, $"Registry position {index}: the row's alternate label is the declared one — only the nominal-funnel row admits a second answer.");
        }
    }

    /// <summary>
    /// The reachable-sequence pin: at each of the four census states, the rows the
    /// registry admits — walked in array order — are the two shipped
    /// classification chains filtered by path. This row is the EXECUTABLE form of
    /// those chains: the nominal-free chain is the (nominals clear) pair of
    /// sequences and the nominal chain is the (nominals set) pair, and the nominal
    /// chain carries no counting gate at all, so its two counting states read
    /// identically. Reading the table with a row's admission columns reproduces
    /// each chain row for row and in order.
    /// </summary>
    [TestMethod]
    public void ReachableRowSequencesAreTheShippedChainsAtEveryCensusState()
    {
        (string Name, bool MentionsNominals, bool MentionsCounting, EnumerationHabitatClass[] Expected)[] states =
        [
            ("NominalFreeWithoutCounting", false, false,
                [EnumerationHabitatClass.ModalGadgetTree, EnumerationHabitatClass.ModalRoleExpansion]),
            ("NominalFreeWithCounting", false, true,
                [EnumerationHabitatClass.ModalGadgetTree, EnumerationHabitatClass.BooleanCardinalityGadget, EnumerationHabitatClass.PartitionCounting, EnumerationHabitatClass.BijectionChainArithmetic, EnumerationHabitatClass.RestrictionRichGround, EnumerationHabitatClass.ToldGroundWitness, EnumerationHabitatClass.ModalRoleExpansion]),
            ("NominalWithoutCounting", true, false,
                [EnumerationHabitatClass.EnumerationAlgebra, EnumerationHabitatClass.NominalCounting, EnumerationHabitatClass.ModalGadgetTree, EnumerationHabitatClass.SpyPointDomainBound, EnumerationHabitatClass.BijectionChainArithmetic, EnumerationHabitatClass.RestrictionRichGround, EnumerationHabitatClass.ToldGroundWitness, EnumerationHabitatClass.ModalRoleExpansion, EnumerationHabitatClass.NominalPinnedRole]),
            ("NominalWithCounting", true, true,
                [EnumerationHabitatClass.EnumerationAlgebra, EnumerationHabitatClass.NominalCounting, EnumerationHabitatClass.ModalGadgetTree, EnumerationHabitatClass.SpyPointDomainBound, EnumerationHabitatClass.BijectionChainArithmetic, EnumerationHabitatClass.RestrictionRichGround, EnumerationHabitatClass.ToldGroundWitness, EnumerationHabitatClass.ModalRoleExpansion, EnumerationHabitatClass.NominalPinnedRole]),
        ];
        ReadOnlySpan<HabitatProbeEntry> rows = ContextHabitatRecognizer.ProbeOrder;
        List<EnumerationHabitatClass> admitted = [];
        foreach((string name, bool mentionsNominals, bool mentionsCounting, EnumerationHabitatClass[] expected) in states)
        {
            admitted.Clear();
            for(int index = 0; index < rows.Length; index++)
            {
                if(rows[index].Admits(mentionsNominals, mentionsCounting))
                {
                    admitted.Add(rows[index].Label);
                }
            }

            Assert.HasCount(expected.Length, admitted, name + ": the census state admits the declared number of rows.");
            for(int position = 0; position < expected.Length; position++)
            {
                Assert.AreEqual(expected[position], admitted[position], name + $": the admitted row at chain position {position} is the declared one.");
            }
        }
    }

    /// <summary>
    /// The admission matrix: every registry row's admission answer at each of the
    /// four census states, against declared expectations and with no module in
    /// play. A row's reachability is a total function of its two declared columns
    /// and the two census bits, so the whole four-by-ten surface is asserted text
    /// rather than a brace structure a reader must trace.
    /// </summary>
    [TestMethod]
    public void AdmissionMatrixMatchesTheDeclaredExpectationAtEveryCensusState()
    {
        (EnumerationHabitatClass Label, bool AtNeitherBit, bool AtCountingOnly, bool AtNominalOnly, bool AtBothBits)[] expectations =
        [
            (EnumerationHabitatClass.EnumerationAlgebra, false, false, true, true),
            (EnumerationHabitatClass.NominalCounting, false, false, true, true),
            (EnumerationHabitatClass.ModalGadgetTree, true, true, true, true),
            (EnumerationHabitatClass.BooleanCardinalityGadget, false, true, false, false),
            (EnumerationHabitatClass.PartitionCounting, false, true, false, false),
            (EnumerationHabitatClass.SpyPointDomainBound, false, false, true, true),
            (EnumerationHabitatClass.BijectionChainArithmetic, false, true, true, true),
            (EnumerationHabitatClass.RestrictionRichGround, false, true, true, true),
            (EnumerationHabitatClass.ToldGroundWitness, false, true, true, true),
            (EnumerationHabitatClass.ModalRoleExpansion, true, true, true, true),
            (EnumerationHabitatClass.NominalPinnedRole, false, false, true, true),
        ];
        ReadOnlySpan<HabitatProbeEntry> rows = ContextHabitatRecognizer.ProbeOrder;
        int rowCount = rows.Length;

        Assert.AreEqual(expectations.Length, rowCount, "The expectation table covers every registered row.");
        for(int index = 0; index < rows.Length; index++)
        {
            ref readonly HabitatProbeEntry row = ref rows[index];
            bool declared = false;
            foreach((EnumerationHabitatClass label, bool atNeitherBit, bool atCountingOnly, bool atNominalOnly, bool atBothBits) in expectations)
            {
                if(label != row.Label)
                {
                    continue;
                }

                declared = true;
                Assert.AreEqual(atNeitherBit, row.Admits(mentionsNominals: false, mentionsCounting: false), $"{label}: the admission answer with neither census bit set is the declared one.");
                Assert.AreEqual(atCountingOnly, row.Admits(mentionsNominals: false, mentionsCounting: true), $"{label}: the admission answer with the counting mention alone is the declared one.");
                Assert.AreEqual(atNominalOnly, row.Admits(mentionsNominals: true, mentionsCounting: false), $"{label}: the admission answer with the nominal mention alone is the declared one.");
                Assert.AreEqual(atBothBits, row.Admits(mentionsNominals: true, mentionsCounting: true), $"{label}: the admission answer with both census bits set is the declared one.");
            }

            Assert.IsTrue(declared, $"Registry position {index}: the row's label {row.Label} carries an expectation in the admission matrix.");
        }
    }

    /// <summary>
    /// The label-discipline pin: every registry row's match step, driven directly
    /// over a spread of synthetic modules drawn from this battery's own
    /// constructions, answers only that row's own label, that row's declared
    /// alternate, or none. An eleven-row table admits copy-paste mis-wiring, and a row
    /// bound to another family's match step answers outside its own vocabulary
    /// here.
    /// </summary>
    [TestMethod]
    public void EveryRowMatchStepAnswersOnlyItsOwnLabelsOrNone()
    {
        ReasoningModule toldGroundWitness = Module(
            SubClassOf(Class("Witness"), new OwlObjectSomeValuesFrom(Property("r"), Thing)),
            new OwlInverseObjectPropertiesAxiom(Property("r"), Property("s")) { Origin = Origin("inverse") },
            Edge("r", "a", "b"),
            Equivalent(Class("Anchor"), OneOf("a")));
        ReasoningModule restrictionRichGround = Module(
            SubClassOf(Class("Ground"), new OwlObjectAllValuesFrom(Property("q"), Class("GroundFiller"))),
            SubClassOf(Class("Ground"), HasValue("q", "pin")),
            Different("g1", "g2", "g3", "g4", "g5", "g6", "g7", "g8", "g9", "g10", "g11", "g12", "g13", "g14", "g15", "g16", "g17"));
        ReasoningModule plainSubclass = Module(SubClassOf(Class("A"), Class("B")));
        ReasoningModule bijectionChain = Module(
            new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, Property("r")) { Origin = Origin("functional") },
            new OwlInverseObjectPropertiesAxiom(Property("r"), Property("s")) { Origin = Origin("inverse") },
            SubClassOf(Class("A"), new OwlObjectSomeValuesFrom(Property("r"), Class("B"))));
        (string Name, ReasoningModule Module)[] modules =
        [
            ("Enum1", ContextNominalBatteryTests.Enum1Module()),
            ("Nomr2", ContextNominalBatteryTests.Nomr2Module()),
            ("FunnelCapAndAlgebra", Extend(ContextNominalBatteryTests.Nomr2Module(), Equivalent(Class("C"), OneOf("a", "b")), Equivalent(Class("D"), OneOf("b")), Equivalent(Class("C"), Class("D")))),
            ("ToldGroundWitness", toldGroundWitness),
            ("RestrictionRichGround", restrictionRichGround),
            ("PlainSubclass", plainSubclass),
            ("BijectionChain", bijectionChain),
        ];
        ReadOnlySpan<HabitatProbeEntry> rows = ContextHabitatRecognizer.ProbeOrder;
        for(int index = 0; index < rows.Length; index++)
        {
            ref readonly HabitatProbeEntry row = ref rows[index];
            foreach((string name, ReasoningModule module) in modules)
            {
                EnumerationHabitatClass answer = row.Match(module);
                bool insideRowVocabulary = answer == row.Label || answer == row.AlternateLabel || answer == EnumerationHabitatClass.None;

                Assert.IsTrue(insideRowVocabulary, $"The {row.Label} row answered {answer} on {name}: a row's match step answers its own label, its declared alternate, or none.");
            }
        }
    }

    /// <summary>
    /// CI-1, the census-silent carrier rule: a row whose signal may ride a
    /// construct kind the survey census reports on NEITHER passed bit may not gate
    /// its evaluation on the counting mention, on either path — such a gate is
    /// latent unreachability rather than a narrower jurisdiction, because the bit
    /// it waits on is never set by the construct that carries the signal. The rule
    /// is exercised rather than vacuous: at least one registered row declares a
    /// census-silent carrier.
    /// </summary>
    [TestMethod]
    public void CensusSilentCarrierRowsDoNotGateOnTheCountingMention()
    {
        ReadOnlySpan<HabitatProbeEntry> rows = ContextHabitatRecognizer.ProbeOrder;
        int censusSilentRows = 0;
        for(int index = 0; index < rows.Length; index++)
        {
            ref readonly HabitatProbeEntry row = ref rows[index];
            if((row.Carriers & HabitatSignalCarriers.CensusSilent) == HabitatSignalCarriers.None)
            {
                continue;
            }

            censusSilentRows++;
            Assert.AreNotEqual(HabitatPathAdmission.WhenCounting, row.OnNominalFree, $"CI-1, {row.Label}, nominal-free column: a row whose signal may ride a census-silent construct kind may not gate on the counting mention.");
            Assert.AreNotEqual(HabitatPathAdmission.WhenCounting, row.OnNominal, $"CI-1, {row.Label}, nominal column: a row whose signal may ride a census-silent construct kind may not gate on the counting mention.");
        }

        Assert.IsGreaterThan(0, censusSilentRows, "CI-1 is exercised: at least one registered row declares a census-silent carrier.");
    }

    /// <summary>
    /// The census contract battery: one minimal admissible module per construct
    /// kind, driven through the SHIPPING survey and scanner — the admission
    /// verdict first, then the passed census bits through the behind-the-gate
    /// seam. Every row asserts admission before its bits, so a data-side row
    /// proves census SILENCE on an admitted module rather than inadmissibility,
    /// with each module's polarity and bound drawn from the survey's own
    /// admissible cases, and the five census-silent construct kinds assert both
    /// passed bits clear. The declared carrier column is the mapping the
    /// census-silent lock holds against this measurement.
    /// </summary>
    [TestMethod]
    public void CensusContractRowsMeasureTheDeclaredBitsAdmissibilityFirst()
    {
        foreach((string kind, ReasoningModule module, HabitatSignalCarriers carrier, bool expectedNominals, bool expectedCounting) in CensusContractRows())
        {
            Assert.IsTrue(ContextModuleSurvey.Survey(module).Admitted, $"{kind}: the census contract module is admitted — the row measures silence, never inadmissibility.");
            Assert.IsTrue(ContextModuleSurvey.TryCensusFor(module, out bool mentionsNominals, out bool mentionsCounting), $"{kind}: every axiom is individually admissible, so the census scan runs.");
            Assert.AreEqual(expectedNominals, mentionsNominals, $"{kind}: the passed nominal-mention bit is the declared one.");
            Assert.AreEqual(expectedCounting, mentionsCounting, $"{kind}: the passed counting-mention bit is the declared one.");
        }
    }

    /// <summary>
    /// CI-1b, the census-silent mapping lock: over the census contract table a
    /// construct kind measures both passed bits clear precisely where its
    /// declared carrier is census-silent or the declaration-only inverse — the
    /// scanned-but-never-passed dark kind. The declaration and the measurement
    /// cannot drift apart: a construct kind moving into the census breaks its
    /// row's equivalence, a kind declared silent that the census in fact
    /// reports breaks it the other way, and a composite redefined away from the
    /// three data-side flags breaks the row of whichever kind it dropped.
    /// </summary>
    [TestMethod]
    public void CensusSilentDeclarationEqualsTheMeasuredSilence()
    {
        int silentRows = 0;
        int darkRows = 0;
        foreach((string kind, ReasoningModule module, HabitatSignalCarriers carrier, _, _) in CensusContractRows())
        {
            Assert.IsTrue(ContextModuleSurvey.TryCensusFor(module, out bool mentionsNominals, out bool mentionsCounting), $"{kind}: the census scan runs on the contract module.");
            bool measuredSilent = !mentionsNominals && !mentionsCounting;
            bool declaredSilentOrDark = (carrier & (HabitatSignalCarriers.CensusSilent | HabitatSignalCarriers.Inverse)) != HabitatSignalCarriers.None;

            Assert.AreEqual(declaredSilentOrDark, measuredSilent, $"{kind}: both passed bits are clear exactly where the declared carrier is census-silent or the declaration-only inverse.");
            if((carrier & HabitatSignalCarriers.CensusSilent) != HabitatSignalCarriers.None)
            {
                silentRows++;
            }

            if((carrier & HabitatSignalCarriers.Inverse) != HabitatSignalCarriers.None)
            {
                darkRows++;
            }
        }

        Assert.AreEqual(5, silentRows, "The mapping covers all five census-silent construct kinds.");
        Assert.AreEqual(1, darkRows, "The mapping carries the one declaration-only inverse construct kind.");
    }

    /// <summary>
    /// The data-cardinality shadow control, in vitro: a module whose gadget
    /// signal rides a range-less boolean-bound data cardinality — a
    /// census-silent construct — beside a named-only intersection. The min-one
    /// form carries the signal because it is the data gadget the survey admits
    /// at BOTH polarities and an equivalence side is surveyed at both, which
    /// narrows the admitted shadow class to the two-polarity data gadgets and
    /// is itself a measured boundary of the shadow. The passed census reads
    /// neither bit, the boolean-cardinality gadget row MATCHES while the
    /// nominal-free path declines to admit it without a counting mention, and
    /// the production label is none: the match-without-admission shadow the
    /// corpus masks record, exhibited on a synthetic module the corpus does not
    /// contain, beside the told-ground witness and restriction-rich shadows the
    /// corpus columns are read for. The rows admitted at this census state —
    /// the two data-counting carriers — both decline, so the walk ends at its
    /// terminal.
    /// </summary>
    [TestMethod]
    public void DataCardinalityShadowControlLightsTheBooleanGadgetShadow()
    {
        ReasoningModule control = Module(
            Equivalent(Class("ShadowGadget"), DataMin("shadowData", 1)),
            Equivalent(Class("ShadowGadget"), Intersection(Class("ShadowLeft"), Class("ShadowRight"))));
        AssertShadowControl("DataCardinality", control, expectedMatches: [EnumerationHabitatClass.BooleanCardinalityGadget]);
    }

    /// <summary>
    /// The data-value-restriction shadow control, in vitro: a single-axiom
    /// module whose only construct is a data existential — a census-silent
    /// construct kind no registered row's signal rides. The passed census reads
    /// neither bit and NO row matches, so the shadow is empty: the control pins
    /// the current narrowness, and a match step widened to ride the kind lights
    /// here before any corpus read can see it.
    /// </summary>
    [TestMethod]
    public void DataValueRestrictionShadowControlPinsTheEmptyShadow()
    {
        AssertShadowControl("DataValueRestriction", Module(SubClassOf(Class("ShadowHost"), DataSome("shadowData"))), expectedMatches: []);
    }

    /// <summary>
    /// The data-characteristic shadow control, in vitro: a single-axiom module
    /// whose only construct is a told functional data-property characteristic —
    /// a census-silent construct kind no registered row's signal rides. The
    /// passed census reads neither bit and NO row matches, so the shadow is
    /// empty: the control pins the current narrowness, and a match step widened
    /// to ride the kind lights here before any corpus read can see it.
    /// </summary>
    [TestMethod]
    public void DataCharacteristicShadowControlPinsTheEmptyShadow()
    {
        AssertShadowControl("DataCharacteristic", Module(FunctionalData("shadowData")), expectedMatches: []);
    }

    /// <summary>
    /// Asserts one shadow control's full read: the module is admitted, both
    /// passed census bits read through the behind-the-gate seam are clear, the
    /// registry rows matching the module are exactly the expected ones, and
    /// every matching row stands in the neither-bit shadow — matched yet
    /// unadmitted at the census state the module induces. The production label
    /// is asserted none: the shadow is a reachability loss made loud in vitro,
    /// recorded corpus-side and never asserted empty there.
    /// </summary>
    /// <param name="kind">The control's census-silent construct kind.</param>
    /// <param name="control">The control module.</param>
    /// <param name="expectedMatches">The labels of the rows expected to match the module, in registry order; empty where no registered row's signal rides the kind.</param>
    private static void AssertShadowControl(string kind, ReasoningModule control, EnumerationHabitatClass[] expectedMatches)
    {
        Assert.IsTrue(ContextModuleSurvey.Survey(control).Admitted, $"{kind} shadow control: the module is admitted.");
        Assert.IsTrue(ContextModuleSurvey.TryCensusFor(control, out bool mentionsNominals, out bool mentionsCounting), $"{kind} shadow control: the census scan runs.");
        Assert.IsFalse(mentionsNominals, $"{kind} shadow control: the construct sets no nominal mention.");
        Assert.IsFalse(mentionsCounting, $"{kind} shadow control: the construct sets no counting mention.");
        ReadOnlySpan<HabitatProbeEntry> rows = ContextHabitatRecognizer.ProbeOrder;
        List<EnumerationHabitatClass> matches = [];
        List<EnumerationHabitatClass> shadow = [];
        for(int index = 0; index < rows.Length; index++)
        {
            if(rows[index].Match(control) == EnumerationHabitatClass.None)
            {
                continue;
            }

            matches.Add(rows[index].Label);
            if(!rows[index].Admits(mentionsNominals: false, mentionsCounting: false))
            {
                shadow.Add(rows[index].Label);
            }
        }

        Assert.AreSequenceEqual(expectedMatches, matches, $"{kind} shadow control: the matching rows are the declared ones.");
        Assert.AreSequenceEqual(expectedMatches, shadow, $"{kind} shadow control: every matching row stands in the neither-bit shadow, none admitted there.");
        Assert.AreEqual(EnumerationHabitatClass.None, ContextModuleSurvey.Survey(control).EnumerationHabitat, $"{kind} shadow control: the production label is none, the module unreachable through the admission gate.");
    }

    /// <summary>
    /// The census contract table: one minimal admissible module per construct
    /// kind, its declared signal carrier, and the two passed census bits the
    /// shipping scan is contracted to report for it. The five census-silent
    /// construct kinds — the data cardinality, the three data value
    /// restrictions, and the functional data-property characteristic — declare
    /// both bits clear, and the told inverse pairing declares them clear as the
    /// scanned-but-never-passed dark kind. Every module sits in a
    /// known-admissible survey position: the data universal and the data
    /// counting bounds ride the positive superclass position, so each row
    /// measures the scan and never an admissibility rejection.
    /// </summary>
    /// <returns>The contract rows.</returns>
    private static (string Kind, ReasoningModule Module, HabitatSignalCarriers Carrier, bool ExpectedNominals, bool ExpectedCounting)[] CensusContractRows()
    {
        return
        [
            ("ObjectCardinality", Module(SubClassOf(Class("CensusHost"), Max("censusRole", 2, null))), HabitatSignalCarriers.ObjectCounting, false, true),
            ("FunctionalObjectProperty", Module(Functional("censusRole")), HabitatSignalCarriers.ObjectCounting, false, true),
            ("InverseFunctionalObjectProperty", Module(InverseFunctional("censusRole")), HabitatSignalCarriers.ObjectCounting, false, true),
            ("ObjectOneOf", Module(SubClassOf(Class("CensusHost"), OneOf("censusMember"))), HabitatSignalCarriers.Nominal, true, false),
            ("ObjectHasValue", Module(SubClassOf(Class("CensusHost"), HasValue("censusRole", "censusMember"))), HabitatSignalCarriers.Nominal, true, false),
            ("InverseObjectProperties", Module(InversePair("censusRole", "censusConverse")), HabitatSignalCarriers.Inverse, false, false),
            ("DataCardinality", Module(SubClassOf(Class("CensusHost"), DataMax("censusData", 1))), HabitatSignalCarriers.DataCounting, false, false),
            ("DataSomeValuesFrom", Module(SubClassOf(Class("CensusHost"), DataSome("censusData"))), HabitatSignalCarriers.DataValueRestriction, false, false),
            ("DataAllValuesFrom", Module(SubClassOf(Class("CensusHost"), DataAll("censusData"))), HabitatSignalCarriers.DataValueRestriction, false, false),
            ("DataHasValue", Module(SubClassOf(Class("CensusHost"), DataHasValueOf("censusData", "42"))), HabitatSignalCarriers.DataValueRestriction, false, false),
            ("FunctionalDataProperty", Module(FunctionalData("censusData")), HabitatSignalCarriers.DataCharacteristic, false, false),
        ];
    }

    /// <summary>
    /// The face-fold witness: the production every-face-lit selection is folded
    /// from the registry's own faces column, and this row is the ONE place the
    /// nineteen-term literal survives. A family that registers a row lights its
    /// faces by construction and a face no row owns is never lit, so a dropped,
    /// duplicated or mis-assigned faces declaration reads as a fold that no longer
    /// equals the literal.
    /// </summary>
    [TestMethod]
    public void EveryFaceLitFoldsToTheNineteenTermFaceLiteral()
    {
        const EnumerationDeciderFaces NineteenTermLiteral =
            EnumerationDeciderFaces.ClashOnly
            | EnumerationDeciderFaces.Certifying
            | EnumerationDeciderFaces.PartitionClash
            | EnumerationDeciderFaces.PartitionCertify
            | EnumerationDeciderFaces.GadgetClash
            | EnumerationDeciderFaces.GadgetCertify
            | EnumerationDeciderFaces.EnumerationPairClash
            | EnumerationDeciderFaces.EnumerationPairCertify
            | EnumerationDeciderFaces.SpyPointClash
            | EnumerationDeciderFaces.BijectionChainClash
            | EnumerationDeciderFaces.BijectionChainCertify
            | EnumerationDeciderFaces.ToldGroundWitnessClash
            | EnumerationDeciderFaces.ToldGroundWitnessCertify
            | EnumerationDeciderFaces.RepairingGroundClash
            | EnumerationDeciderFaces.RepairingCertify
            | EnumerationDeciderFaces.ModalExpansionClash
            | EnumerationDeciderFaces.ModalGadgetClash
            | EnumerationDeciderFaces.ModalGadgetCertify
            | EnumerationDeciderFaces.NominalPinnedRoleClash;

        Assert.AreEqual(NineteenTermLiteral, ContextHabitatRecognizer.EveryFaceLit, "The folded every-face-lit selection is the nineteen faces the eleven registered rows own between them.");
    }

    /// <summary>
    /// The trace mark: on an engine-processed run the progress marks
    /// carry the habitat class beside the churn columns — attached through
    /// the engine probe on a dark bounded NOMR-2 run — while a pre-engine
    /// decision constructs no engine and therefore no mark (pinned by the
    /// zero engine counters on the decided rows above).
    /// </summary>
    [TestMethod]
    public void TraceMarksCarryTheHabitatClassOnEngineProcessedRuns()
    {
        List<SaturationProgressTraceEvent> marks = [];
        Microsoft.Extensions.Time.Testing.FakeTimeProvider clock = new(new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero));
        SaturationProgressSampler sampler = new(new ProgressMarkCollector(marks).Handle, clock, new Guid("3d1c9f4a-0b6e-4a4f-8c2f-5e7a90d21b44"));
        EngineSamplerAttacher attacher = new(sampler);
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(
            ContextNominalBatteryTests.Nomr2Module(),
            EnumerationDeciderFaces.None,
            NominalParamodulationScope.QueryScoped,
            RootContextTopology.SingleRoot,
            RootPropagationRelevance.Unrestricted,
            new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 4096),
            attacher.Attach,
            TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "The bounded dark run abstains — the engine processed the module.");
        Assert.IsGreaterThan(0, marks.Count, "The sampler emitted marks on the engine-processed run.");
        foreach(SaturationProgressTraceEvent mark in marks)
        {
            Assert.AreEqual(EnumerationHabitatClass.NominalCounting, mark.EnumerationHabitat, "Every mark carries the habitat class beside its churn profile.");
        }
    }

    /// <summary>
    /// Ep1: the satisfiable pair-composition replica — one named class equated
    /// to the told-distinct anchor pair and to four further two-member one-ofs,
    /// with three clause blocks typing the anchor's first member. The composition
    /// resolves past the member-universe window, the vector sweep finds a passing
    /// assignment, and the pair-certify face decides CONSISTENT pre-engine with
    /// its measured pair count on the record. The vector count is asserted as a
    /// bounded range: the walk stops at whichever witness the told axiom order
    /// reaches first.
    /// </summary>
    [TestMethod]
    public void Ep1PairFaceCertifiesTheSatReplica()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(SatisfiableReplicaModule(), PairFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Ep1: the pair-certify face decides the satisfiable replica.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Ep1: a passing vector witnesses the module consistent.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Ep1: a pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(0, totals.ContextsCreated, "Ep1: no engine was constructed.");
        Assert.AreEqual(EnumerationHabitatClass.EnumerationAlgebra, totals.EnumerationHabitat, "Ep1: the recognizer labels the replica Shape E.");
        Assert.AreEqual(10, totals.EnumerationMemberUniverse, "Ep1: the anchor pair and the four variable pairs are the measured universe.");
        Assert.AreEqual(0, totals.EnumerationWindowExceededMembers, "Ep1: a firing pair face silenced nothing — the member window charges no silence when the tier past it decides.");
        Assert.AreEqual(4, totals.EnumerationPairCount, "Ep1: the measured pair count is exact.");
        Assert.IsGreaterThan(0, totals.EnumerationPairVectorCount, "Ep1: the certifying walk visited at least the passing vector.");
        Assert.IsLessThanOrEqualTo(16, totals.EnumerationPairVectorCount, "Ep1: the certifying walk stopped inside the four-pair vector space.");
        Assert.AreEqual(0, totals.EnumerationWindowExceededPairs, "Ep1: no pair-window silence at four pairs.");
        Assert.AreEqual(1, totals.EnumerationDeciderCertifications, "Ep1: the certification counter reads the decision.");
        Assert.AreEqual(0, totals.EnumerationDeciderRefutations, "Ep1: no refutation on a certified module.");
        Assert.IsEmpty(decision.Verdict.Subsumptions, "Ep1: one named class yields no candidate pairs.");
    }

    /// <summary>
    /// Ep2: the unsatisfiable pair-composition replica — the canonical minimal
    /// unsatisfiable three-variable formula, all eight sign combinations of a
    /// three-literal clause, beside one unconstrained fourth pair. Every vector
    /// leaves the anchor's own block unsatisfiable, so the pair-clash face
    /// decides INCONSISTENT pre-engine having walked the whole vector space —
    /// a structurally determined count, asserted exactly.
    /// </summary>
    [TestMethod]
    public void Ep2PairFaceRefutesTheUnsatReplica()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(UnsatisfiableReplicaModule(), PairFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Ep2: the pair-clash face decides the unsatisfiable replica.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Ep2: every vector fails, so no model exists.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Ep2: the refutation is pre-engine.");
        Assert.AreEqual(1, totals.EnumerationDeciderRefutations, "Ep2: the refutation counter reads the decision.");
        Assert.AreEqual(0, totals.EnumerationDeciderCertifications, "Ep2: no certification on a refuted module.");
        Assert.AreEqual(4, totals.EnumerationPairCount, "Ep2: the measured pair count is exact.");
        Assert.AreEqual(16, totals.EnumerationPairVectorCount, "Ep2: the refutation exhausted the whole 2^4 vector space.");
    }

    /// <summary>
    /// Ep3: told distinctness among pair members bites. The base replica pins
    /// two variable members into the anchor's own block through singleton clause
    /// blocks and is CONSISTENT; telling those two members distinct forces them
    /// apart, no vector survives both, and the verdict flips to INCONSISTENT.
    /// </summary>
    [TestMethod]
    public void Ep3PairFaceRespectsToldDifferentAmongPairMembers()
    {
        ModuleDecision baseDecision = ContextSaturationModuleReasoner.DecideModule(PairReplicaModule(4, ["p1"], ["p2"]), PairFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, baseDecision.Outcome, "Ep3: the pair faces decide the base replica.");
        Assert.IsTrue(baseDecision.Verdict!.IsConsistent, "Ep3: the base replica's co-anchored members are consistent.");

        ModuleDecision flipped = ContextSaturationModuleReasoner.DecideModule(Extend(PairReplicaModule(4, ["p1"], ["p2"]), Different("p1", "p2")), PairFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, flipped.Outcome, "Ep3: the pair faces decide the told-distinct replica.");
        Assert.IsFalse(flipped.Verdict!.IsConsistent, "Ep3: the told distinctness contradicts the forced co-anchoring in every vector.");
        Assert.AreEqual(1, flipped.Statistics.ContextTotals.EnumerationDeciderRefutations, "Ep3: the refutation counter reads the flip.");
        Assert.AreEqual(0L, flipped.Statistics.ContextTotals.InferenceAttempts, "Ep3: the flipped verdict is pre-engine.");
    }

    /// <summary>
    /// Ep4: told sameness among pair members bites. The base replica pins one
    /// variable's positive member and another variable's negative member into the
    /// anchor's own block and is CONSISTENT; telling the two positive members
    /// the same forces a block they cannot share, and the verdict flips to
    /// INCONSISTENT.
    /// </summary>
    [TestMethod]
    public void Ep4PairFaceRespectsToldSameAmongPairMembers()
    {
        ModuleDecision baseDecision = ContextSaturationModuleReasoner.DecideModule(PairReplicaModule(4, ["p1"], ["m2"]), PairFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, baseDecision.Outcome, "Ep4: the pair faces decide the base replica.");
        Assert.IsTrue(baseDecision.Verdict!.IsConsistent, "Ep4: the base replica's opposed pinnings are consistent.");

        ModuleDecision flipped = ContextSaturationModuleReasoner.DecideModule(Extend(PairReplicaModule(4, ["p1"], ["m2"]), Same("p1", "p2")), PairFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, flipped.Outcome, "Ep4: the pair faces decide the told-same replica.");
        Assert.IsFalse(flipped.Verdict!.IsConsistent, "Ep4: the told sameness contradicts the opposed pinnings in every vector.");
        Assert.AreEqual(1, flipped.Statistics.ContextTotals.EnumerationDeciderRefutations, "Ep4: the refutation counter reads the flip.");
    }

    /// <summary>Ep5: without a told-distinct anchor no composition resolves — the same four pairs with the anchor's distinctness removed leave the tier silent, the member-universe silence standing and the pair fields on the record at zero.</summary>
    [TestMethod]
    public void Ep5AnchorMissingKeepsTheFaceSilent()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(AnchorlessReplicaModule(), PairFaces, PairProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(0, totals.EnumerationDeciderCertifications, "Ep5: no certification without an anchor.");
        Assert.AreEqual(0, totals.EnumerationDeciderRefutations, "Ep5: no refutation without an anchor.");
        Assert.AreEqual(1, totals.EnumerationWindowExceededMembers, "Ep5: the member-universe silence stands when the pair tier declines.");
        Assert.AreEqual(0, totals.EnumerationPairCount, "Ep5: an unresolved composition reports no pairs.");
        Assert.AreEqual(0, totals.EnumerationPairVectorCount, "Ep5: no vector is walked on a jurisdiction silence.");
        Assert.AreEqual(0, totals.EnumerationWindowExceededPairs, "Ep5: an unresolved composition charges no pair-window silence.");
    }

    /// <summary>
    /// Ep6: both stray-member shapes leave the tier silent. A literal stray — an
    /// individual the anchor-and-pair partition never covers — and a mis-sized
    /// three-member one-of on the anchor's own class, whose members are thereby
    /// left uncovered because the collector never selects two of the three or
    /// truncates the told list, each report an unresolved composition.
    /// </summary>
    [TestMethod]
    public void Ep6StrayMemberKeepsTheFaceSilent()
    {
        (string Name, ReasoningModule Module)[] rows =
        [
            ("StrayIndividual", Extend(PairReplicaModule(4), ClassAssertion(Class("C"), Individual("z")))),
            ("MisSizedOneOf", Extend(PairReplicaModule(3), Equivalent(Class("C"), OneOf("u", "v", "w")))),
        ];
        foreach((string name, ReasoningModule module) in rows)
        {
            ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, PairFaces, PairProbeBudget, TestContext.CancellationToken);
            ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

            Assert.AreEqual(0, totals.EnumerationDeciderCertifications, "Ep6 " + name + ": no certification over an uncovered universe.");
            Assert.AreEqual(0, totals.EnumerationDeciderRefutations, "Ep6 " + name + ": no refutation over an uncovered universe.");
            Assert.AreEqual(0, totals.EnumerationPairCount, "Ep6 " + name + ": the composition does not resolve.");
            Assert.AreEqual(0, totals.EnumerationPairVectorCount, "Ep6 " + name + ": no vector is walked.");
            Assert.AreEqual(1, totals.EnumerationWindowExceededMembers, "Ep6 " + name + ": the member-universe silence stands.");
        }
    }

    /// <summary>Ep7: a second named class no told equivalence defines leaves the signature unpinned, so the tier is silent with the resolved pair count already on the record — the silence is the pin's, not the composition's.</summary>
    [TestMethod]
    public void Ep7UndefinedNamedClassKeepsTheFaceSilent()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Extend(PairReplicaModule(4), SubClassOf(Class("D"), Thing)), PairFaces, PairProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(0, totals.EnumerationDeciderCertifications, "Ep7: no certification with an undefined class in the signature.");
        Assert.AreEqual(0, totals.EnumerationDeciderRefutations, "Ep7: no refutation with an undefined class in the signature.");
        Assert.AreEqual(4, totals.EnumerationPairCount, "Ep7: the composition resolved — the silence is charged to the pin, not the partition.");
        Assert.AreEqual(0, totals.EnumerationPairVectorCount, "Ep7: no vector is walked past an unpinned class.");
    }

    /// <summary>
    /// Ep8: a composition sitting exactly AT the pair bound still decides, and
    /// its measured pair count lands on the pre-engine faces' shared sixteen
    /// boundary discipline — the counted-population, ground-clique,
    /// partition-anchor, and gadget-atom ceilings all read off the same measured
    /// quantity, so any drift between the five constants fails this row.
    /// </summary>
    [TestMethod]
    public void Ep8PairCountAtTheBoundDecides()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(PairReplicaModule(ContextEnumerationAlgebraDecider.PairAssignmentBound), PairFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Ep8: the pair faces decide at exactly the pair bound.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Ep8: the unconstrained composition at the bound is consistent.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Ep8: the at-bound decision is pre-engine.");
        Assert.AreEqual(0, totals.EnumerationWindowExceededPairs, "Ep8: no window silence exactly at the bound.");
        Assert.IsGreaterThan(0, totals.EnumerationPairVectorCount, "Ep8: the at-bound walk visited its witness.");
        Assert.AreEqual(ContextEnumerationAlgebraDecider.PairAssignmentBound, totals.EnumerationPairCount, "Ep8: the measured composition sits exactly at the pair bound.");
        Assert.AreEqual(ContextBooleanGadgetDecider.GadgetAtomBound, totals.EnumerationPairCount, "Ep8: the measured pair ceiling shares the gadget faces' atom bound.");
        Assert.AreEqual(ContextPartitionCountingDecider.PartitionAnchorBound, totals.EnumerationPairCount, "Ep8: the measured pair ceiling shares the partition faces' anchor bound.");
        Assert.AreEqual(ContextNominalCountingDecider.CountedPopulationBound, totals.EnumerationPairCount, "Ep8: the measured pair ceiling shares the counted-population bound.");
        Assert.AreEqual(ContextClausifier.GroundCountingCliqueBound, totals.EnumerationPairCount, "Ep8: the measured pair ceiling shares the ground rider's clique bound.");
    }

    /// <summary>Ep9: a composition one pair past the bound is never walked — the tier is silent with the measured pair count landed BEFORE the comparison and the silence charged to its own named counter.</summary>
    [TestMethod]
    public void Ep9PairCountPastTheBoundSilencesWithTheCountRecorded()
    {
        int overflow = ContextEnumerationAlgebraDecider.PairAssignmentBound + 1;
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(PairReplicaModule(overflow), PairFaces, PairProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(0, totals.EnumerationDeciderCertifications, "Ep9: no certification past the pair bound.");
        Assert.AreEqual(0, totals.EnumerationDeciderRefutations, "Ep9: no refutation past the pair bound.");
        Assert.AreEqual(1, totals.EnumerationWindowExceededPairs, "Ep9: the silence is charged to its named window counter.");
        Assert.AreEqual(overflow, totals.EnumerationPairCount, "Ep9: the measured pair count is reported past the bound.");
        Assert.AreEqual(0, totals.EnumerationPairVectorCount, "Ep9: a window silence walks no vector.");
    }

    /// <summary>
    /// Ep10: nothing inside the member-universe window ever reaches the pair
    /// tier — a module at the member bound is decided by the block sweep with
    /// every pair counter at zero — and the two faces stay out of each other's
    /// habitat: every certified partition row keeps its verdict under the
    /// pair-lit selection with no composition read, and every pair module of
    /// this file is claimed by no partition face. The gadget battery's fixtures
    /// are private to it, so ITS non-interaction rides the implementation phase's
    /// whole-battery de-risk run instead.
    /// </summary>
    [TestMethod]
    public void Ep10BelowWindowModulesNeverReachThePairFace()
    {
        ModuleDecision belowWindow = ContextSaturationModuleReasoner.DecideModule(
            Module(Equivalent(Class("C"), OneOf("a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8"))),
            PairFaces | EnumerationDeciderFaces.Certifying,
            ReasoningConfiguration.Default.Budget,
            TestContext.CancellationToken);
        ContextSaturationStatistics belowTotals = belowWindow.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, belowWindow.Outcome, "Ep10: the block sweep still decides at the member bound.");
        Assert.IsTrue(belowWindow.Verdict!.IsConsistent, "Ep10: the at-bound enumeration is consistent.");
        Assert.AreEqual(1, belowTotals.EnumerationDeciderCertifications, "Ep10: the certifying face decided it.");
        Assert.AreEqual(0, belowTotals.EnumerationPairCount, "Ep10: no composition is read inside the member window.");
        Assert.AreEqual(0, belowTotals.EnumerationPairVectorCount, "Ep10: no vector is walked inside the member window.");
        Assert.AreEqual(0, belowTotals.EnumerationWindowExceededPairs, "Ep10: no pair-window silence inside the member window.");

        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool consistent) in ContextPartitionDeciderTests.PartitionRows())
        {
            ModuleDecision lit = ContextSaturationModuleReasoner.DecideModule(module, PairFaces | PartitionFaces, PairProbeBudget, TestContext.CancellationToken);
            ContextSaturationStatistics litTotals = lit.Statistics.ContextTotals;
            if(lit.Outcome != ReasoningDecisionOutcome.Decided || lit.Verdict is null || lit.Verdict.IsConsistent != consistent)
            {
                mismatches.Add(name + ": the partition row lost its certified verdict under the pair-lit selection.");
                continue;
            }

            if(litTotals.EnumerationPairCount + litTotals.EnumerationPairVectorCount > 0)
            {
                mismatches.Add(name + ": a partition row was read as a pair composition.");
            }
        }

        foreach((string name, ReasoningModule module) in PairRows())
        {
            ContextSaturationStatistics partitionLit = ContextSaturationModuleReasoner.DecideModule(module, PartitionFaces, PairProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;
            if(partitionLit.PartitionDeciderCertifications + partitionLit.PartitionDeciderClashes > 0)
            {
                mismatches.Add(name + ": a pair module was claimed by a partition face.");
            }
        }

        Assert.IsEmpty(mismatches, string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>Ep11: under the explicit dark control a pair-shaped module keeps the honest engine-face budget abstention — the abstained outcome, no verdict, the whole bounded ceiling spent, and the exhaust's measured funnel profile intact, with neither decision counter moved.</summary>
    [TestMethod]
    public void Ep11DarkFacesKeepTheHonestAbstentionByteIdentical()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(CorpusScaleReplicaModule(), EnumerationDeciderFaces.None, PairProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "Ep11: the corpus-scale replica abstains honestly with the faces dark.");
        Assert.IsNull(decision.Verdict, "Ep11: the dark abstention carries no verdict.");
        Assert.AreEqual((long)PairProbeBudget.MaxInferences, totals.InferenceAttempts, "Ep11: the dark run spends exactly the inclusive ceiling.");
        Assert.IsGreaterThan(0L, totals.RuleApplications, "Ep11: the dark exhaust is an admitted saturation, not a non-admission.");
        Assert.IsGreaterThan(0L, totals.WorklistEnqueues, "Ep11: the dark exhaust lands genuine insertions at the funnel's head.");
        Assert.AreEqual(0, totals.EnumerationDeciderCertifications, "Ep11: no certification with the faces dark.");
        Assert.AreEqual(0, totals.EnumerationDeciderRefutations, "Ep11: no refutation with the faces dark.");
    }

    /// <summary>Ep12: the census ships unconditionally — the same dark abstention already carries the habitat label, the member universe, the structural pair count and its window silence, with the sweep-ran marker at zero because no vector was ever walked.</summary>
    [TestMethod]
    public void Ep12CensusRidesTheDarkAbstentionRecordsAlways()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(SatisfiableReplicaModule(), EnumerationDeciderFaces.None, PairProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.EnumerationAlgebra, totals.EnumerationHabitat, "Ep12: the habitat label rides the dark abstention record.");
        Assert.AreEqual(10, totals.EnumerationMemberUniverse, "Ep12: the member universe is measured dark.");
        Assert.AreEqual(1, totals.EnumerationWindowExceededMembers, "Ep12: the member-universe silence is measured dark.");
        Assert.AreEqual(4, totals.EnumerationPairCount, "Ep12: the composition's pair count is measured dark.");
        Assert.AreEqual(0, totals.EnumerationWindowExceededPairs, "Ep12: no pair-window silence dark at four pairs.");
        Assert.AreEqual(0, totals.EnumerationPairVectorCount, "Ep12: the dark path never walks a vector.");
        Assert.AreEqual(0, totals.EnumerationDeciderCertifications, "Ep12: no certification with the faces dark.");
        Assert.AreEqual(0, totals.EnumerationDeciderRefutations, "Ep12: no refutation with the faces dark.");
    }

    /// <summary>
    /// Ep13: a second named class the told axioms DO pin — a singleton one-of
    /// over one pair member — resolves through the non-trivial pin sweep, and the
    /// tier still decides, reading off the exact set the whole vector space and
    /// the generic element leave standing: the singleton class is subsumed by the
    /// anchor's class and not conversely.
    /// </summary>
    [TestMethod]
    public void Ep13PinnedSecondClassResolvesAndDecides()
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Extend(PairReplicaModule(4), Equivalent(Class("D"), OneOf("p1"))), PairFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Ep13: the pair faces decide past a validly pinned second class.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "Ep13: the pinned singleton class leaves the composition consistent.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Ep13: the decision is pre-engine.");
        Assert.AreEqual(4, totals.EnumerationPairCount, "Ep13: the composition resolved beside the pinned class.");
        Assert.IsGreaterThan(0, totals.EnumerationPairVectorCount, "Ep13: the read-off walk visited the vector space.");
        Assert.IsLessThanOrEqualTo(16, totals.EnumerationPairVectorCount, "Ep13: the read-off walk stayed inside the four-pair vector space.");
        AssertSubsumptionKeys(decision.Verdict, [Sub("D", "C")]);
    }

    /// <summary>Ep14: two named classes each defined through the other never resolve, so the pin sweep stalls on the cycle and the tier is silent with the composition's pair count already on the record.</summary>
    [TestMethod]
    public void Ep14CyclicClassPinKeepsTheFaceSilent()
    {
        ReasoningModule module = Extend(
            PairReplicaModule(4),
            Equivalent(Class("D"), Intersection(Class("E"), Class("C"))),
            Equivalent(Class("E"), Union(Class("D"), Class("C"))));
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, PairFaces, PairProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(0, totals.EnumerationDeciderCertifications, "Ep14: no certification over a definition cycle.");
        Assert.AreEqual(0, totals.EnumerationDeciderRefutations, "Ep14: no refutation over a definition cycle.");
        Assert.AreEqual(4, totals.EnumerationPairCount, "Ep14: the composition resolved — the silence is the cycle's.");
        Assert.AreEqual(0, totals.EnumerationPairVectorCount, "Ep14: no vector is walked over a definition cycle.");
    }

    /// <summary>
    /// Ep15: nine VALIDLY pinned named classes past the member window silence the
    /// tier on the class cap alone. Every class carries a told definition, so the
    /// pin sweep would place them all; the cap is what declines, and it declines
    /// before the refutation mask — whose single word holds only the bound's
    /// ordered pairs — is ever touched.
    /// </summary>
    [TestMethod]
    public void Ep15ClassCountPastTheBoundKeepsTheFaceSilent()
    {
        List<OwlAxiom> axioms = [.. PairReplicaModule(4).Axioms];
        for(int index = 1; index <= ContextEnumerationAlgebraDecider.SignatureClassBound; index++)
        {
            axioms.Add(Equivalent(Class("D" + index), OneOf("t")));
        }

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(new ReasoningModule([.. axioms], Violations: []), PairFaces, PairProbeBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(0, totals.EnumerationDeciderCertifications, "Ep15: no certification past the signature-class cap.");
        Assert.AreEqual(0, totals.EnumerationDeciderRefutations, "Ep15: no refutation past the signature-class cap.");
        Assert.AreEqual(4, totals.EnumerationPairCount, "Ep15: the composition resolved and every class is pinned — the silence is the cap's.");
        Assert.AreEqual(0, totals.EnumerationPairVectorCount, "Ep15: no vector is walked past the cap.");
        Assert.AreEqual(1, totals.EnumerationWindowExceededMembers, "Ep15: the member-universe silence stands.");
    }

    /// <summary>The pair-composition modules of this file, named for the face-selection sweep's mismatch reports.</summary>
    /// <returns>The rows.</returns>
    private static (string Name, ReasoningModule Module)[] PairRows()
    {
        return
        [
            ("Ep1", SatisfiableReplicaModule()),
            ("Ep2", UnsatisfiableReplicaModule()),
            ("Ep8", PairReplicaModule(ContextEnumerationAlgebraDecider.PairAssignmentBound)),
        ];
    }

    /// <summary>The satisfiable replica: four variable pairs beside the anchor, with three clause blocks no single vector trivially satisfies.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SatisfiableReplicaModule()
    {
        return PairReplicaModule(4, ["p1", "p2", "p3"], ["m1", "m2", "m4"], ["p3", "m2", "p4"]);
    }

    /// <summary>The unsatisfiable replica: the canonical minimal unsatisfiable three-variable formula — all eight sign combinations of a three-literal clause — beside one unconstrained fourth pair, so the whole vector space is walked and fails.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule UnsatisfiableReplicaModule()
    {
        return PairReplicaModule(
            4,
            ["p1", "p2", "p3"],
            ["p1", "p2", "m3"],
            ["p1", "m2", "p3"],
            ["p1", "m2", "m3"],
            ["m1", "p2", "p3"],
            ["m1", "p2", "m3"],
            ["m1", "m2", "p3"],
            ["m1", "m2", "m3"]);
    }

    /// <summary>The corpus-scale replica: nine variable pairs beside the anchor — the measured corpus universe of twenty members — carrying the unsatisfiable three-variable core, the shape whose saturation exhausts any practical inference ceiling.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule CorpusScaleReplicaModule()
    {
        return PairReplicaModule(
            9,
            ["p1", "p2", "p3"],
            ["p1", "p2", "m3"],
            ["p1", "m2", "p3"],
            ["p1", "m2", "m3"],
            ["m1", "p2", "p3"],
            ["m1", "p2", "m3"],
            ["m1", "m2", "p3"],
            ["m1", "m2", "m3"]);
    }

    /// <summary>The anchorless replica: the same four pairs on the same class with the anchor pair's told distinctness removed, so no candidate anchor exists at all.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule AnchorlessReplicaModule()
    {
        return Module(
            Equivalent(Class("C"), OneOf("t", "f")),
            Equivalent(Class("C"), OneOf("p1", "m1")),
            Equivalent(Class("C"), OneOf("p2", "m2")),
            Equivalent(Class("C"), OneOf("p3", "m3")),
            Equivalent(Class("C"), OneOf("p4", "m4")));
    }

    /// <summary>
    /// Builds a pair-composition replica: one named class equated to the
    /// told-distinct anchor pair and to one two-member one-of per variable pair,
    /// plus one class assertion per clause typing the anchor's first member by
    /// the clause's one-of. The clause blocks read as a boolean formula over the
    /// pairs, exactly the corpus shape.
    /// </summary>
    /// <param name="pairCount">The variable pairs beside the anchor.</param>
    /// <param name="clauses">The clause blocks, each a list of pair members' local names.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule PairReplicaModule(int pairCount, params string[][] clauses)
    {
        List<OwlAxiom> axioms =
        [
            Equivalent(Class("C"), OneOf("t", "f")),
            Different("t", "f"),
        ];
        for(int pair = 1; pair <= pairCount; pair++)
        {
            axioms.Add(Equivalent(Class("C"), OneOf("p" + pair, "m" + pair)));
        }

        for(int clause = 0; clause < clauses.Length; clause++)
        {
            axioms.Add(ClassAssertion(OneOf(clauses[clause]), Individual("t")));
        }

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>Clausifies the module with the clash-only face lit and everything else at defaults — the boundary rows' direct drive.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The clausification.</returns>
    private static ClausificationResult ClausifyDecider(params OwlAxiom[] axioms)
    {
        return ContextClausifier.Clausify(Module(axioms), EqualityLowering.GeneralClause, DatatypeRegistry.Empty, [], riderEnabled: false, nominalDeciderEnabled: true);
    }

    /// <summary>Builds the chain-funnel module: <c>Thing ⊑ B1 ⊑ … ⊑ B(depth) ⊑ ∃r⁻.{o}</c> with the cap and the clashing distinctness — the funnel sits at exactly <paramref name="depth"/> named hops.</summary>
    /// <param name="depth">The named-hop count to the funnel step.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ChainFunnelModule(int depth)
    {
        List<OwlAxiom> axioms =
        [
            SubClassOf(Thing, Class("B1")),
        ];
        for(int hop = 1; hop < depth; hop++)
        {
            axioms.Add(SubClassOf(Class("B" + hop), Class("B" + (hop + 1))));
        }

        axioms.Add(SubClassOf(Class("B" + depth), SomeInverse("r", OneOf("o"))));
        axioms.Add(SubClassOf(OneOf("o"), Max("r", 2, null)));
        axioms.Add(Different("i1", "i2", "i3"));

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>Builds the told-edge population module: a two-member funnel (attributing nobody), told edges from one anchor to <paramref name="targets"/> pairwise-distinct targets, and a cap of <paramref name="bound"/> on the funnel role.</summary>
    /// <param name="targets">The told-edge target count.</param>
    /// <param name="bound">The cap bound.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule EdgePopulationModule(int targets, int bound)
    {
        List<OwlAxiom> axioms =
        [
            SubClassOf(Thing, SomeInverse("r", OneOf("o1", "o2"))),
            SubClassOf(OneOf("o1", "o2"), Max("r", bound, null)),
        ];
        string[] names = new string[targets];
        for(int i = 0; i < targets; i++)
        {
            names[i] = "x" + i;
            axioms.Add(Edge("r", "o1", names[i]));
        }

        axioms.Add(Different(names));

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>Builds one deterministic told-distinct pattern over the nodes: all pairs, no pairs, or the even-index-sum pairs — the under-filled face.</summary>
    /// <param name="nodes">The nodes.</param>
    /// <param name="pattern">The pattern selector: 0 all, 1 none, 2 even-sum.</param>
    /// <returns>The symmetric pair set.</returns>
    private static HashSet<(Utf8String First, Utf8String Second)> BuildDistinctPattern(List<Utf8String> nodes, int pattern)
    {
        HashSet<(Utf8String First, Utf8String Second)> distinct = [];
        for(int i = 0; i < nodes.Count; i++)
        {
            for(int j = i + 1; j < nodes.Count; j++)
            {
                bool include = pattern switch
                {
                    0 => true,
                    1 => false,
                    _ => (i + j) % 2 == 0,
                };
                if(include)
                {
                    distinct.Add((nodes[i], nodes[j]));
                    distinct.Add((nodes[j], nodes[i]));
                }
            }
        }

        return distinct;
    }

    /// <summary>Asserts the verdict's subsumption set equals the expected keys exactly, order-insensitively.</summary>
    /// <param name="verdict">The verdict whose subsumptions are checked.</param>
    /// <param name="expected">The expected subsumption keys.</param>
    private static void AssertSubsumptionKeys(ModuleVerdict verdict, string[] expected)
    {
        List<string> actual = [];
        foreach((NamedNode subClass, NamedNode superClass) in verdict.Subsumptions)
        {
            actual.Add(subClass.Iri.ToString() + "->" + superClass.Iri.ToString());
        }

        actual.Sort(StringComparer.Ordinal);
        List<string> expectedKeys = [.. expected];
        expectedKeys.Sort(StringComparer.Ordinal);
        Assert.AreSequenceEqual(expectedKeys, actual, "The exact subsumption set diverges.");
    }

    /// <summary>The subsumption key for a pair of example-namespace classes.</summary>
    /// <param name="subClass">The subclass's local name.</param>
    /// <param name="superClass">The superclass's local name.</param>
    /// <returns>The key.</returns>
    private static string Sub(string subClass, string superClass)
    {
        return Example + subClass + "->" + Example + superClass;
    }

    /// <summary>The subsumption key for a pair of nominal-battery-namespace classes — the reused fixtures' namespace.</summary>
    /// <param name="subClass">The subclass's local name.</param>
    /// <param name="superClass">The superclass's local name.</param>
    /// <returns>The key.</returns>
    private static string BatterySub(string subClass, string superClass)
    {
        return BatteryExample + subClass + "->" + BatteryExample + superClass;
    }

    /// <summary>Collects emitted progress marks without a lexical closure.</summary>
    private sealed class ProgressMarkCollector
    {
        /// <summary>The collected marks.</summary>
        private List<SaturationProgressTraceEvent> Marks { get; }

        /// <summary>Initialises the collector over the caller's mark list.</summary>
        /// <param name="marks">The list the marks append to.</param>
        public ProgressMarkCollector(List<SaturationProgressTraceEvent> marks)
        {
            Marks = marks;
        }

        /// <summary>Appends one emitted mark.</summary>
        /// <param name="mark">The mark.</param>
        public void Handle(in SaturationProgressTraceEvent mark)
        {
            Marks.Add(mark);
        }
    }

    /// <summary>Attaches a progress sampler to each constructed engine through the engine probe, without a lexical closure.</summary>
    private sealed class EngineSamplerAttacher
    {
        /// <summary>The sampler to attach.</summary>
        private SaturationProgressSampler Sampler { get; }

        /// <summary>Initialises the attacher with its sampler.</summary>
        /// <param name="sampler">The sampler.</param>
        public EngineSamplerAttacher(SaturationProgressSampler sampler)
        {
            Sampler = sampler;
        }

        /// <summary>Attaches the sampler to the constructed engine.</summary>
        /// <param name="engine">The engine.</param>
        public void Attach(ContextSaturationEngine engine)
        {
            engine.Progress = Sampler;
        }
    }

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>Extends a module with additional axioms.</summary>
    /// <param name="baseModule">The base module.</param>
    /// <param name="extra">The axioms to append.</param>
    /// <returns>The extended module.</returns>
    private static ReasoningModule Extend(ReasoningModule baseModule, params OwlAxiom[] extra)
    {
        return new ReasoningModule([.. baseModule.Axioms, .. extra], Violations: []);
    }

    /// <summary>A provenance quad naming the axiom's origin.</summary>
    /// <param name="marker">The origin marker's local name.</param>
    /// <returns>The quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(new NamedNode(Utf8Strings.From(Example + marker)), new NamedNode(Utf8Strings.From(Example + "p")), new NamedNode(Utf8Strings.From(Example + "o")), Graph: null);
    }

    /// <summary>The <c>owl:Thing</c> reference.</summary>
    private static OwlClassReference Thing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing")));

    /// <summary>The <c>owl:Nothing</c> reference — the empty-filler near-miss's qualification.</summary>
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

    /// <summary>An enumeration of individuals in the example namespace.</summary>
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

    /// <summary>An existential restriction over the inverse of a forward role.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="filler">The filler class.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom SomeInverse(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(InverseProperty(property), filler);
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

    /// <summary>An intersection of class expressions.</summary>
    /// <param name="operands">The intersection operands.</param>
    /// <returns>The intersection.</returns>
    private static OwlObjectIntersectionOf Intersection(params OwlClassExpression[] operands)
    {
        return new OwlObjectIntersectionOf([.. operands]);
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

    /// <summary>A disjoint-union axiom defining a named class as the disjoint union of the operands.</summary>
    /// <param name="definedClass">The defined class's local name.</param>
    /// <param name="operands">The disjoint operands.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointUnionAxiom DisjointUnion(string definedClass, params OwlClassExpression[] operands)
    {
        return new OwlDisjointUnionAxiom(new NamedNode(Utf8Strings.From(Example + definedClass)), [.. operands]) { Origin = Origin("disjointUnion") };
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
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.InverseFunctional, Property(role)) { Origin = Origin("inverseFunctional") };
    }

    /// <summary>A told inverse pairing of two named roles in the example namespace.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The pairing axiom.</returns>
    private static OwlInverseObjectPropertiesAxiom InversePair(string first, string second)
    {
        return new OwlInverseObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("inversePair") };
    }

    /// <summary>A named data property in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property node.</returns>
    private static NamedNode DataProperty(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>The <c>xsd:string</c> datatype reference the data-range positions carry.</summary>
    private static OwlDatatypeReference XsdString { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string")));

    /// <summary>A range-less maximum data-cardinality restriction over a named data property.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="cardinality">The bound n.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataCardinality DataMax(string property, int cardinality)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Max, cardinality, DataProperty(property), Range: null);
    }

    /// <summary>A range-less minimum data-cardinality restriction over a named data property — the data gadget form the survey admits at both polarities, so it can stand on an equivalence side.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="cardinality">The bound n.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataCardinality DataMin(string property, int cardinality)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Min, cardinality, DataProperty(property), Range: null);
    }

    /// <summary>A single-property data existential restriction over a named data property and the string datatype.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataSomeValuesFrom DataSome(string property)
    {
        return new OwlDataSomeValuesFrom([DataProperty(property)], XsdString);
    }

    /// <summary>A single-property data universal restriction over a named data property and the string datatype.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataAllValuesFrom DataAll(string property)
    {
        return new OwlDataAllValuesFrom([DataProperty(property)], XsdString);
    }

    /// <summary>A data value restriction pinning a named data property to a string literal.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="value">The pinned literal's lexical value.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataHasValue DataHasValueOf(string property, string value)
    {
        return new OwlDataHasValue(DataProperty(property), new Literal(Utf8Strings.From(value), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string"))));
    }

    /// <summary>A functionality characteristic over a named data property in the example namespace.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <returns>The characteristic axiom.</returns>
    private static OwlFunctionalDataPropertyAxiom FunctionalData(string property)
    {
        return new OwlFunctionalDataPropertyAxiom(DataProperty(property)) { Origin = Origin("functionalData") };
    }

    /// <summary>A sub-property axiom between two named roles in the example namespace.</summary>
    /// <param name="subRole">The sub-role's local name.</param>
    /// <param name="superRole">The super-role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubObjectPropertyOfAxiom SubRole(string subRole, string superRole)
    {
        return new OwlSubObjectPropertyOfAxiom(Property(subRole), Property(superRole)) { Origin = Origin("subrole") };
    }
}
