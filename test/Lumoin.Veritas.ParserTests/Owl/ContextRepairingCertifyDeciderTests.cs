using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The restriction-rich-ground habitat decider's battery: the two corpus
/// premises certified by their repaired described models with the census
/// riding by name, the told closure operator carrying an invented edge
/// through the inverse, symmetric and transitive rules, the told-sameness
/// quotient and the content-equal keying underneath it, the deterministic
/// forced-value repair and the cardinality arms it discharges, the bounded
/// witness supply with its vacuity guard and its guard-off control, the
/// bounded choice walk over a closed enumeration, every named bound's
/// overflow silence, the complement post-pass placed either side of the
/// mints, the four monotone clash reasons under both the Shape R and the
/// Shape W label, the window boundaries, the explicit dark control, the
/// proposal-side pruning filter that declares nothing, and the vacuous
/// universal firing the standing widening lock must never exclude. Every
/// completeness limit is asserted as a SILENCE carrying its measurement,
/// never as a verdict; the six differential rows reach their variation points
/// through the decider's own construction-options seam.
/// </summary>
[TestClass]
internal sealed class ContextRepairingCertifyDeciderTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the battery's classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/repairingcertifycsp#";

    /// <summary>Both repairing faces lit — the selection the deciding rows drive.</summary>
    private const EnumerationDeciderFaces RepairingFaces = EnumerationDeciderFaces.RepairingGroundClash | EnumerationDeciderFaces.RepairingCertify;

    /// <summary>Every decider face the recognizer's registry lights, read from the production fold — the selection the jurisdiction rows drive.</summary>
    private static EnumerationDeciderFaces AllFaces { get; } = ContextHabitatRecognizer.EveryFaceLit;

    /// <summary>The Shape R clash reason family's leading identifier — the prefix every repairing reason opens with.</summary>
    private const string RepairingPrefix = "Repairing";

    /// <summary>The Shape W clash reason family's leading identifier — the prefix every told-ground-witness reason opens with.</summary>
    private const string ToldGroundPrefix = "ToldGroundWitness";

    /// <summary>The complemented-membership clash reason's kind.</summary>
    private const string ComplementedMembership = "ComplementedMembership(";

    /// <summary>The empty-class assertion clash reason's kind.</summary>
    private const string AssertedNothingMembership = "AssertedNothingMembership(";

    /// <summary>The disjointness clash reason's kind.</summary>
    private const string DisjointMembership = "DisjointMembership(";

    /// <summary>The denied-edge clash reason's kind.</summary>
    private const string ContradictoryEdge = "ContradictoryEdge(";

    /// <summary>The first corpus premise of the habitat's measured instance pair — a mutually importing partner that expands to the merged module either way.</summary>
    private const string FirstCorpusIdentifier = "WebOnt-miscellaneous-001";

    /// <summary>The second corpus premise of the pair, which expands through the same mutual import to the same merged module.</summary>
    private const string SecondCorpusIdentifier = "WebOnt-miscellaneous-002";

    /// <summary>The padding individuals a Shape R module carries: a count comfortably above the recognizer's told-individual floor, so a row's own terms never have to be counted against it.</summary>
    private const int GroundFloorTerms = 24;

    /// <summary>The enumerated colour members the bounded-choice rows walk, only the last of which the walk's own filter admits — the length the near-miss node bound is derived against.</summary>
    private const int ColourMembers = 4;

    /// <summary>The bounded budget the silence rows drive: enough for the engine to fire rules on a restriction-rich module, far below what its saturation would need.</summary>
    private static ReasoningBudget ProbeBudget { get; } = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 4096);

    /// <summary>The ceiling the dark control drives: one inference attempt, so an admitted module the faces are dark on exhausts the engine budget and the census rides an abstention record rather than a decision.</summary>
    private static ReasoningBudget DarkBudget { get; } = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1);

    /// <summary>The approved-status W3C OWL 2 manifest's test cases, loaded once from the source tree — the corpus rows' only reach into the conformance material.</summary>
    private static ImmutableArray<Owl2TestCase> ApprovedCorpusCases { get; } = Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", "approved", "all.rdf"));

    /// <summary>
    /// The first corpus premise of the habitat's measured instance: the
    /// manifest-exact RDF/XML premise expanded through its mutual import
    /// closure into ONE merged module, whose told ABox no told edge satisfies.
    /// The repaired described model discharges every failing obligation and the
    /// certify face decides the premise consistent pre-engine, with all five
    /// statistics fields riding the decision record by name.
    /// </summary>
    [TestMethod]
    public void Rc1RepairedDescribedModelCertifiesTheFirstCorpusPremise()
    {
        ReasoningModule module = CorpusPremiseModule(FirstCorpusIdentifier);
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(module);

        Assert.IsTrue(outcome.Consistent, "Rc1 FirstCorpusPremise: the repaired described model satisfies every admitted axiom of the merged module.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, outcome.Route, "Rc1 FirstCorpusPremise: the repaired-described-model route names the certificate.");
        Assert.IsNull(outcome.ClashReason, "Rc1 FirstCorpusPremise: a certificate names no clash reason.");
        Assert.AreEqual(0, outcome.Window.WindowSilences, "Rc1 FirstCorpusPremise: the merged module sits inside every window ceiling.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, RepairingFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, totals.EnumerationHabitat, "Rc1 FirstCorpusPremise: the merged module carries the Shape R census label.");
        Assert.AreEqual(1, totals.RepairingDeciderCertifications, "Rc1 FirstCorpusPremise: the certify face's counter reads the decision.");
        Assert.AreEqual(0, totals.RepairingDeciderClashes, "Rc1 FirstCorpusPremise: a certified module takes no clash.");
        Assert.AreEqual(outcome.Window.CarrierCount, totals.RepairingCarrierCount, "Rc1 FirstCorpusPremise: the measured domain size rides the statistics record.");
        Assert.AreEqual(outcome.Window.CommittedEdges, totals.RepairingCommittedEdgeCount, "Rc1 FirstCorpusPremise: the committed edge count rides the statistics record.");
        Assert.AreEqual(0, totals.RepairingWindowExceededCarriers, "Rc1 FirstCorpusPremise: no window silence on the merged module.");
    }

    /// <summary>
    /// The second corpus premise: the mutually importing partner expands
    /// through the same closure to the same merged module and certifies
    /// identically, so the pair moves whole or not at all. The census rides by
    /// name here too, and the two premises' measured windows agree.
    /// </summary>
    [TestMethod]
    public void Rc2RepairedDescribedModelCertifiesTheSecondCorpusPremise()
    {
        ReasoningModule module = CorpusPremiseModule(SecondCorpusIdentifier);
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(module);

        Assert.IsTrue(outcome.Consistent, "Rc2 SecondCorpusPremise: the partner premise expands to the same merged module and certifies with it.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, outcome.Route, "Rc2 SecondCorpusPremise: the repaired-described-model route names the certificate.");
        Assert.IsNull(outcome.ClashReason, "Rc2 SecondCorpusPremise: a certificate names no clash reason.");
        Assert.AreEqual(ContextRepairingCertifyDecider.Run(CorpusPremiseModule(FirstCorpusIdentifier)).Window.CarrierCount, outcome.Window.CarrierCount, "Rc2 SecondCorpusPremise: the mutual import expands either way to one domain.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, RepairingFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, totals.EnumerationHabitat, "Rc2 SecondCorpusPremise: the merged module carries the Shape R census label.");
        Assert.AreEqual(1, totals.RepairingDeciderCertifications, "Rc2 SecondCorpusPremise: the certify face's counter reads the decision.");
        Assert.AreEqual(0, totals.RepairingDeciderClashes, "Rc2 SecondCorpusPremise: a certified module takes no clash.");
        Assert.AreEqual(outcome.Window.CarrierCount, totals.RepairingCarrierCount, "Rc2 SecondCorpusPremise: the measured domain size rides the statistics record.");
        Assert.AreEqual(outcome.Window.CommittedEdges, totals.RepairingCommittedEdgeCount, "Rc2 SecondCorpusPremise: the committed edge count rides the statistics record.");
        Assert.AreEqual(0, totals.RepairingWindowExceededCarriers, "Rc2 SecondCorpusPremise: no window silence on the merged module.");
    }

    /// <summary>
    /// The told sub-property closure discharges its own obligation family with
    /// no invention: one told edge on the subproperty closes into the
    /// superproperty, the superproperty's told range and the universal over it
    /// both read the derived edge, and the inclusion axiom is re-checked
    /// against the finished relation rather than trusted from the operator that
    /// built it. Nothing is minted.
    /// </summary>
    [TestMethod]
    public void Rc3ToldSubPropertyClosureDischargesItsObligations()
    {
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(SubPropertyClosureModule());

        Assert.IsTrue(outcome.Consistent, "Rc3 SubPropertyClosure: the closed relation satisfies the inclusion, the range and the universal alike.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, outcome.Route, "Rc3 SubPropertyClosure: the repaired-described-model route names the certificate.");
        Assert.AreEqual(2, outcome.Window.CommittedEdges, "Rc3 SubPropertyClosure: the one told edge closes to two under the told sub-property inclusion.");
        Assert.AreEqual(0, outcome.Window.MintedElements, "Rc3 SubPropertyClosure: a told closure invents no element.");
        Assert.AreEqual(0, outcome.Window.ChoicePointsOpened, "Rc3 SubPropertyClosure: the deterministic prefix opens no choice frame.");
    }

    /// <summary>
    /// The closure is an OPERATOR re-applied at every commit, not a prologue:
    /// a phase-1 forced edge on an inverse-paired role is mirrored before the
    /// inverse axiom's both-directions re-check reads it, so the module
    /// certifies over two committed edges. Under the single-prologue variant —
    /// selected through the construction-options seam — the closure ran once
    /// over the told seeding and never again, the invented edge stays
    /// unmirrored, and the exact-converse check fails into SILENCE.
    /// </summary>
    [TestMethod]
    public void Rc4InvertedEdgeIsMirroredAtEveryCommit()
    {
        RepairingOutcome production = ContextRepairingCertifyDecider.Run(InvertedForcedEdgeModule());

        Assert.IsTrue(production.Consistent, "Rc4 InvertedEdge: the re-applied operator mirrors the invented edge, so the both-directions re-check passes.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, production.Route, "Rc4 InvertedEdge: the repaired-described-model route names the certificate.");
        Assert.AreEqual(2, production.Window.CommittedEdges, "Rc4 InvertedEdge: the invented edge and its mirror are the two committed edges.");

        RepairingOutcome prologue = ContextRepairingCertifyDecider.Run(InvertedForcedEdgeModule(), new RepairingConstructionOptions
        {
            ClosureMode = RepairClosureMode.SinglePrologue,
        });

        Assert.IsNull(prologue.Consistent, "Rc4 InvertedEdge: with the closure run once the unmirrored edge fails the converse check and the face is silent.");
        Assert.IsNull(prologue.Route, "Rc4 InvertedEdge: a silent certify face names no route.");
        Assert.IsNull(prologue.ClashReason, "Rc4 InvertedEdge: the certify face never refutes.");
        Assert.AreEqual(1, prologue.Window.CommittedEdges, "Rc4 InvertedEdge: the prologue variant leaves the invented edge unmirrored.");
    }

    /// <summary>
    /// Told symmetry carries the invented edge: the forced value pin inserts one
    /// edge and the re-applied closure operator supplies its mirror, so the
    /// symmetry axiom's own re-check passes against the finished relation.
    /// </summary>
    [TestMethod]
    public void Rc5SymmetricClosureCarriesTheInventedEdge()
    {
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(SymmetricForcedEdgeModule());

        Assert.IsTrue(outcome.Consistent, "Rc5 SymmetricClosure: the mirrored invented edge satisfies the told symmetry.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, outcome.Route, "Rc5 SymmetricClosure: the repaired-described-model route names the certificate.");
        Assert.AreEqual(2, outcome.Window.CommittedEdges, "Rc5 SymmetricClosure: the invented edge and its mirror are the two committed edges.");
        Assert.AreEqual(0, outcome.Window.MintedElements, "Rc5 SymmetricClosure: a symmetric mirror is a derived edge, never a fresh element.");
    }

    /// <summary>
    /// Told transitivity carries the invented edge, and the obligations it
    /// opens land at the SAME carrier: the forced pin inserts one edge into a
    /// told transitive role whose composition with a told edge is derived by the
    /// re-applied operator, so the transitivity re-check passes over four
    /// committed edges with nothing minted.
    /// </summary>
    [TestMethod]
    public void Rc6TransitiveClosureCarriesTheInventedEdge()
    {
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(TransitiveForcedEdgeModule());

        Assert.IsTrue(outcome.Consistent, "Rc6 TransitiveClosure: the composed invented edge satisfies the told transitivity.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, outcome.Route, "Rc6 TransitiveClosure: the repaired-described-model route names the certificate.");
        Assert.AreEqual(4, outcome.Window.CommittedEdges, "Rc6 TransitiveClosure: the two told edges, the invented edge, and its composition are the four committed edges.");
        Assert.AreEqual(0, outcome.Window.MintedElements, "Rc6 TransitiveClosure: a transitive composition is a derived edge, never a fresh element.");
    }

    /// <summary>
    /// The keying and the quotient, pinned as a pair. Without a told sameness
    /// axiom the domain holds exactly four elements: a term parsed twice into
    /// two OBJECT-DISTINCT but content-equal references resolves to ONE carrier
    /// before any quotienting, and two axioms mentioning the same blank label
    /// resolve to ONE carrier as well — object-identity or ordinal keying would
    /// read six. With the told sameness axiom added the domain drops to three:
    /// the union-find merges exactly the told-equality class and nothing else.
    /// </summary>
    [TestMethod]
    public void Rc7ToldSamenessQuotientMergesExactlyTheToldClasses()
    {
        RepairingOutcome unquotiented = ContextRepairingCertifyDecider.Run(KeyingModule());

        Assert.AreEqual(4, unquotiented.Window.CarrierCount, "Rc7 ToldSamenessQuotient: the repeated IRI and the repeated blank label each key to one carrier before any quotienting.");
        Assert.IsTrue(unquotiented.Consistent, "Rc7 ToldSamenessQuotient: the described model satisfies every told axiom.");

        RepairingOutcome quotiented = ContextRepairingCertifyDecider.Run(KeyingModule(Same("chablis", "chablisEstate")));

        Assert.AreEqual(3, quotiented.Window.CarrierCount, "Rc7 ToldSamenessQuotient: the one told-sameness class merges exactly one pair, so the domain drops by exactly one.");
        Assert.IsTrue(quotiented.Consistent, "Rc7 ToldSamenessQuotient: the quotiented model satisfies the sameness axiom and every other told axiom.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, quotiented.Route, "Rc7 ToldSamenessQuotient: the repaired-described-model route names the certificate.");
    }

    /// <summary>
    /// Distinctness is inequality of CARRIER INDEX AFTER QUOTIENT: a module
    /// telling both sameness and difference of one pair holds two syntactically
    /// distinct and quotient-identical terms, so a check written to a
    /// "pairwise syntactically distinct" letter would pass while the constructed
    /// model violates the axiom. The index comparison fails it instead and the
    /// face is SILENT — never a consistency over the difference surface.
    /// </summary>
    [TestMethod]
    public void Rc8QuotientIdenticalTermsFailThePairwiseDistinctnessCheck()
    {
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(SameAndDifferentModule());

        Assert.IsNull(outcome.Consistent, "Rc8 QuotientIdenticalTerms: the post-quotient distinctness check fails, so no certificate is taken.");
        Assert.IsNull(outcome.Route, "Rc8 QuotientIdenticalTerms: a silent certify face names no route.");
        Assert.IsNull(outcome.ClashReason, "Rc8 QuotientIdenticalTerms: the certify face never refutes, and no told ground rule reaches a clash.");
        Assert.AreEqual(1, outcome.Window.CarrierCount, "Rc8 QuotientIdenticalTerms: the told sameness merges the pair into one element, which is exactly what the difference axiom then fails on.");
    }

    /// <summary>
    /// The deterministic forced-value repair discharges a value-pin obligation:
    /// the carrier the frozen class table places in the restricting class takes
    /// one invented edge to the pinned told individual, and the module certifies
    /// in the zero-choice regime with nothing minted.
    /// </summary>
    [TestMethod]
    public void Rc9ForcedValueInsertionDischargesAValuePinObligation()
    {
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(ValuePinModule());

        Assert.IsTrue(outcome.Consistent, "Rc9 ForcedValueInsertion: the forced edge satisfies the value pin on re-check.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, outcome.Route, "Rc9 ForcedValueInsertion: the repaired-described-model route names the certificate.");
        Assert.AreEqual(0, outcome.Window.ChoicePointsOpened, "Rc9 ForcedValueInsertion: a deterministic repair opens no choice frame.");
        Assert.AreEqual(0, outcome.Window.MintedElements, "Rc9 ForcedValueInsertion: a value pin names a told individual, so nothing is minted.");
        Assert.AreEqual(1, outcome.Window.CommittedEdges, "Rc9 ForcedValueInsertion: the forced edge is the one committed edge.");
    }

    /// <summary>
    /// Two value pins on one told functional role are inserted SIMULTANEOUSLY
    /// over the frozen class table, so the round's result is independent of the
    /// order the pins were collected in: the functional re-check reads a count
    /// of two against a bound of one and the face is SILENT. Reversing the two
    /// pin axioms reaches the same silence, which is what makes the outcome a
    /// bound failure rather than a canonicity-dependent verdict.
    /// </summary>
    [TestMethod]
    public void Rc10TwoPinsOnOneFunctionalRoleSilenceDeterministically()
    {
        ReasoningModule module = TwoPinsOnAFunctionalRoleModule(reversed: false);
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Rc10 TwoPins: the simultaneous insertion breaks the functional bound, so no certificate is taken.");
        Assert.IsNull(outcome.Route, "Rc10 TwoPins: a silent certify face names no route.");
        Assert.IsNull(outcome.ClashReason, "Rc10 TwoPins: the certify face never refutes.");

        RepairingOutcome reversed = ContextRepairingCertifyDecider.Run(TwoPinsOnAFunctionalRoleModule(reversed: true));

        Assert.IsNull(reversed.Consistent, "Rc10 TwoPins: the reversed pin order reaches the same silence — the whole forced set goes in at once.");
        Assert.AreEqual(outcome.Window.CommittedEdges, reversed.Window.CommittedEdges, "Rc10 TwoPins: both pin orders commit the same edge relation.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, RepairingFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, totals.EnumerationHabitat, "Rc10 TwoPins: the recognized-but-silent module still carries the Shape R census label.");
        Assert.AreEqual(0, totals.RepairingDeciderCertifications, "Rc10 TwoPins: no certificate on the near-miss module.");
        Assert.AreEqual(0, totals.RepairingDeciderClashes, "Rc10 TwoPins: no clash either.");
    }

    /// <summary>
    /// The value-pin conjunct on the DEFINING side of an equivalence takes the
    /// same forced edge the subclass route takes and invents nothing separate:
    /// the intersection's members carry the pin exactly as a superclass
    /// position's would, and the module certifies over the one committed edge
    /// the subclass row also commits.
    /// </summary>
    [TestMethod]
    public void Rc11IntersectionConjunctPinTakesTheSameForcedEdge()
    {
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(IntersectionConjunctPinModule());

        Assert.IsTrue(outcome.Consistent, "Rc11 IntersectionConjunctPin: the forced edge satisfies both directions of the defining equivalence.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, outcome.Route, "Rc11 IntersectionConjunctPin: the repaired-described-model route names the certificate.");
        Assert.AreEqual(ContextRepairingCertifyDecider.Run(ValuePinModule()).Window.CommittedEdges, outcome.Window.CommittedEdges, "Rc11 IntersectionConjunctPin: the defining-side route commits exactly the subclass route's edge and nothing more.");
        Assert.AreEqual(0, outcome.Window.MintedElements, "Rc11 IntersectionConjunctPin: a value pin names a told individual, so nothing is minted.");
        Assert.AreEqual(0, outcome.Window.ChoicePointsOpened, "Rc11 IntersectionConjunctPin: the deterministic prefix opens no choice frame.");
    }

    /// <summary>
    /// An intersection equivalence whose conjuncts are all NAMED classes
    /// carries an independent obligation with no edge to invent: the defined
    /// class holds a told member the intersection does not, no repair move
    /// reaches a membership, and the face is SILENT rather than certifying a
    /// structure the equivalence rejects.
    /// </summary>
    [TestMethod]
    public void Rc12PureNamedClassIntersectionFailureStaysSilent()
    {
        ReasoningModule module = PureNamedIntersectionModule();
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Rc12 PureNamedIntersection: the equivalence's own check fails and no invention can repair a membership.");
        Assert.IsNull(outcome.Route, "Rc12 PureNamedIntersection: a silent certify face names no route.");
        Assert.IsNull(outcome.ClashReason, "Rc12 PureNamedIntersection: the certify face never refutes.");
        Assert.AreEqual(0, outcome.Window.MintedElements, "Rc12 PureNamedIntersection: an edge-free obligation mints nothing.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, RepairingFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, totals.EnumerationHabitat, "Rc12 PureNamedIntersection: the recognized-but-silent module still carries the Shape R census label.");
        Assert.AreEqual(0, totals.RepairingDeciderCertifications, "Rc12 PureNamedIntersection: no certificate on the near-miss module.");
        Assert.AreEqual(0, totals.RepairingDeciderClashes, "Rc12 PureNamedIntersection: no clash either.");
    }

    /// <summary>
    /// The split family's deterministic side: an exact-cardinality obligation
    /// whose co-occurring value pin already forces its one successor is
    /// discharged by phase 1 alone, so the demand extraction over the frozen
    /// table finds no deficit, the walk opens no frame, and the bound pre-check
    /// is never consulted because no mint is proposed.
    /// </summary>
    [TestMethod]
    public void Rc13ExactCardinalityForcedArmDischargesWithoutAChoice()
    {
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(ExactCardinalityForcedArmModule());

        Assert.IsTrue(outcome.Consistent, "Rc13 ExactCardinalityForcedArm: the forced edge meets the exact bound on re-check.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, outcome.Route, "Rc13 ExactCardinalityForcedArm: the repaired-described-model route names the certificate.");
        Assert.AreEqual(0, outcome.Window.ChoicePointsOpened, "Rc13 ExactCardinalityForcedArm: the forced arm needs no choice frame.");
        Assert.AreEqual(0, outcome.Window.MintedElements, "Rc13 ExactCardinalityForcedArm: the pinned successor is a told individual.");
        Assert.AreEqual(1, outcome.Window.CommittedEdges, "Rc13 ExactCardinalityForcedArm: the forced edge is the one committed edge.");
    }

    /// <summary>
    /// The bound pre-check reads the committed, closed edge relation before a
    /// mint is assembled: the minimum-cardinality deficit is real, but the told
    /// functional characteristic on the same role caps the carrier at one
    /// successor it already holds, so the mint is REFUSED and the face is
    /// SILENT — a structure certain to fail the re-check is never proposed.
    /// </summary>
    [TestMethod]
    public void Rc14BoundPreCheckRefusesAMintThatWouldBreakAMaximum()
    {
        ReasoningModule module = BoundPreCheckModule();
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Rc14 BoundPreCheck: the refused mint leaves the demand unmet and the face silent.");
        Assert.IsNull(outcome.Route, "Rc14 BoundPreCheck: a silent certify face names no route.");
        Assert.IsNull(outcome.ClashReason, "Rc14 BoundPreCheck: the certify face never refutes.");
        Assert.AreEqual(0, outcome.Window.MintedElements, "Rc14 BoundPreCheck: the pre-check refuses before the domain grows.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, RepairingFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, totals.EnumerationHabitat, "Rc14 BoundPreCheck: the recognized-but-silent module still carries the Shape R census label.");
        Assert.AreEqual(0, totals.RepairingDeciderCertifications, "Rc14 BoundPreCheck: no certificate on the near-miss module.");
        Assert.AreEqual(0, totals.RepairingDeciderClashes, "Rc14 BoundPreCheck: no clash either.");
    }

    /// <summary>
    /// The class table is recomputed FROM SCRATCH at every commit, never
    /// patched: a universal is ANTI-monotone in the edge relation, so the
    /// carrier that held the universal vacuously before the forced edge went in
    /// must LEAVE its extension once the edge names a successor outside the
    /// filler. The told disjointness against the forcing class is what makes the
    /// difference observable — a monotone patch would keep the stale membership
    /// and fail that check, while the recomputed table certifies.
    /// </summary>
    [TestMethod]
    public void Rc15RecomputedClassTableShrinksAUniversalExtension()
    {
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(AntiMonotoneUniversalModule());

        Assert.IsTrue(outcome.Consistent, "Rc15 RecomputedClassTable: the shrunk universal extension leaves the told disjointness satisfied.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, outcome.Route, "Rc15 RecomputedClassTable: the repaired-described-model route names the certificate.");
        Assert.AreEqual(1, outcome.Window.CommittedEdges, "Rc15 RecomputedClassTable: the forced edge is the one committed edge, and it is what shrinks the universal.");
        Assert.AreEqual(0, outcome.Window.MintedElements, "Rc15 RecomputedClassTable: nothing is minted on the anti-monotone path.");
    }

    /// <summary>
    /// The told-pairs reading interprets a data property as EXACTLY its told
    /// pairs, and all three data-side checks are exact on the exhibited
    /// structure: the assertion's pair is in the extension, the domain holds
    /// every told subject, and every told literal carries the range's datatype
    /// IRI. A data RANGE EXPRESSION in that position is admission-REJECT — it
    /// would put a lower bound back on the extension — so the same module
    /// carrying one is SILENT instead.
    /// </summary>
    [TestMethod]
    public void Rc16ToldPairsReadingVerifiesTheDataSideShapes()
    {
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(ToldDataPairModule());

        Assert.IsTrue(outcome.Consistent, "Rc16 ToldPairsReading: the assertion, the domain and the range all pass against the told pairs.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, outcome.Route, "Rc16 ToldPairsReading: the repaired-described-model route names the certificate.");

        RepairingOutcome ranged = ContextRepairingCertifyDecider.Run(ToldDataRangeExpressionModule());

        Assert.IsNull(ranged.Consistent, "Rc16 ToldPairsReading: a data range expression reopens the lower-bound leg, so the whole-module admission rejects it.");
        Assert.IsNull(ranged.Route, "Rc16 ToldPairsReading: a silent certify face names no route.");
        Assert.IsNull(ranged.ClashReason, "Rc16 ToldPairsReading: the certify face never refutes.");
    }

    /// <summary>
    /// The two jurisdictions are asymmetric by design. A key axiom, a property
    /// chain, an inverse-functional characteristic and a told denial each sit
    /// outside the certify face's whole-module admission — the first three
    /// because an invented edge can MANUFACTURE the merge they force, the last
    /// because the monotone clash face consumes it instead — so each silences a
    /// module that otherwise certifies. Beside a told ground contradiction the
    /// clash face still decides every one of them, its refutation over a told
    /// subset condemning every superset.
    /// </summary>
    [TestMethod]
    public void Rc17RejectedAxiomKindsSilenceCertifyOnly()
    {
        Assert.IsTrue(ContextRepairingCertifyDecider.Run(ValuePinModule()).Consistent, "Rc17 RejectedAxiomKinds: the unperturbed base module certifies, so each row's silence is the rejected kind's doing.");

        AssertRejectedKindSilencesCertifyOnly(Key("Wine", "madeFrom"), "Rc17 RejectedAxiomKinds (key axiom)", TestContext.CancellationToken);
        AssertRejectedKindSilencesCertifyOnly(Chain("madeFrom", "grownIn", "originatesIn"), "Rc17 RejectedAxiomKinds (property chain)", TestContext.CancellationToken);
        AssertRejectedKindSilencesCertifyOnly(InverseFunctional("madeFrom"), "Rc17 RejectedAxiomKinds (inverse-functional characteristic)", TestContext.CancellationToken);
        AssertRejectedKindSilencesCertifyOnly(DeniedEdge("madeFrom", Individual("grape"), Individual("chablis")), "Rc17 RejectedAxiomKinds (told denial)", TestContext.CancellationToken);
    }

    /// <summary>
    /// The probe chain keeps the sibling labels on BOTH of the recognizer's
    /// paths: a module whose functional characteristic, told inverse pair and
    /// told existential are bound to ONE role stays Shape B; a told-ground
    /// module inside its own carrier window, carrying no obligation-position
    /// restriction at all, stays Shape W; and only a module carrying
    /// obligation-position restrictions over a told individual population above
    /// the told-ground ceiling takes Shape R — once through the nominal path a
    /// value pin opens, and once through the nominal-free path a cardinality
    /// opens.
    /// </summary>
    [TestMethod]
    public void Rc18HabitatOrderingKeepsTheSiblingLabels()
    {
        ContextSaturationStatistics bijection = ContextSaturationModuleReasoner.DecideModule(RoleLinkedModule(), AllFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.BijectionChainArithmetic, bijection.EnumerationHabitat, "Rc18 HabitatOrdering: the role-linked module is claimed by the bijection-chain probe ahead of Shape R.");
        Assert.AreEqual(0, bijection.RepairingDeciderCertifications, "Rc18 HabitatOrdering: no repairing certificate on a module the bijection-chain habitat claims.");
        Assert.AreEqual(0, bijection.RepairingDeciderClashes, "Rc18 HabitatOrdering: no repairing clash on it either.");

        ContextSaturationStatistics toldGround = ContextSaturationModuleReasoner.DecideModule(ToldGroundWitnessModule(), AllFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.ToldGroundWitness, toldGround.EnumerationHabitat, "Rc18 HabitatOrdering: a told-ground module carrying no obligation-position restriction declines Shape R and keeps Shape W.");
        Assert.AreEqual(0, toldGround.RepairingDeciderCertifications, "Rc18 HabitatOrdering: no repairing certificate on a module the told-ground habitat keeps.");

        ContextSaturationStatistics nominal = ContextSaturationModuleReasoner.DecideModule(GroundModule(ValuePinAxioms()), AllFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, nominal.EnumerationHabitat, "Rc18 HabitatOrdering: the value-pin module reaches the Shape R probe on the nominal path.");
        Assert.AreEqual(1, nominal.RepairingDeciderCertifications, "Rc18 HabitatOrdering: the Shape R label hands the module to the repairing certify face.");

        ContextSaturationStatistics nominalFree = ContextSaturationModuleReasoner.DecideModule(NominalFreeGroundModule(), AllFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, nominalFree.EnumerationHabitat, "Rc18 HabitatOrdering: a cardinality-bearing module with no nominal mention reaches the Shape R probe on the nominal-free path.");
    }

    /// <summary>
    /// An OPEN demand set mints ONE fresh element, types it into the set's
    /// named classes, re-closes and re-enters the deterministic stage. Three
    /// mint claims ride the measured window directly: the mint lands on a role
    /// carrying NO told edge, so the role index set covers every role MENTIONED
    /// rather than every role told; the domain grows by exactly one, so the
    /// demand received exactly one witness; and a second owner of the same
    /// obligation takes its OWN witness rather than sharing the first.
    /// </summary>
    [TestMethod]
    public void Rc19OpenFillerMintIsChoiceFree()
    {
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(OpenFillerMintModule(owners: 1));

        Assert.IsTrue(outcome.Consistent, "Rc19 OpenFillerMint: the minted witness satisfies the existential on re-check.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, outcome.Route, "Rc19 OpenFillerMint: the repaired-described-model route names the certificate.");
        Assert.AreEqual(1, outcome.Window.MintedElements, "Rc19 OpenFillerMint: the demand receives exactly one fresh element.");
        Assert.AreEqual(2, outcome.Window.CarrierCount, "Rc19 OpenFillerMint: the told owner and its witness are the two domain elements.");
        Assert.AreEqual(1, outcome.Window.CommittedEdges, "Rc19 OpenFillerMint: the mint edge lands on a role no told assertion ever named.");
        Assert.AreEqual(0, outcome.Window.ChoicePointsOpened, "Rc19 OpenFillerMint: an open demand set is repaired choice-free.");

        RepairingOutcome shared = ContextRepairingCertifyDecider.Run(OpenFillerMintModule(owners: 2));

        Assert.IsTrue(shared.Consistent, "Rc19 OpenFillerMint: both owners' witnesses satisfy the existential.");
        Assert.AreEqual(2, shared.Window.MintedElements, "Rc19 OpenFillerMint: each owner mints its OWN witness — no two owners share one.");
        Assert.AreEqual(4, shared.Window.CarrierCount, "Rc19 OpenFillerMint: two told owners and their two witnesses are the four domain elements.");
    }

    /// <summary>
    /// A demand set holding a class an enumeration CLOSES refuses the mint and
    /// enumerates that enumeration's members instead, in TOLD DOCUMENT ORDER:
    /// with every member admissible the walk commits the FIRST told member and
    /// spends one node, and with the first member inadmissible it spends two —
    /// the ordering pin. Nothing is minted on either run.
    /// </summary>
    [TestMethod]
    public void Rc20ClosedFillerOpensAChoiceFrameAndNeverMints()
    {
        RepairingOutcome first = ContextRepairingCertifyDecider.Run(ClosedFillerModule(admissibleFrom: 0));

        Assert.IsTrue(first.Consistent, "Rc20 ClosedFiller: the enumerated member satisfies the existential on re-check.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, first.Route, "Rc20 ClosedFiller: the repaired-described-model route names the certificate.");
        Assert.AreEqual(0, first.Window.MintedElements, "Rc20 ClosedFiller: a closed demand set never mints.");
        Assert.AreEqual(1, first.Window.ChoicePointsOpened, "Rc20 ClosedFiller: the closed demand opens exactly one choice frame.");
        Assert.AreEqual(1, first.Window.EvaluatedNodes, "Rc20 ClosedFiller: the FIRST enumerated member in told document order is the first candidate proposed.");

        RepairingOutcome second = ContextRepairingCertifyDecider.Run(ClosedFillerModule(admissibleFrom: 1));

        Assert.IsTrue(second.Consistent, "Rc20 ClosedFiller: the walk moves on to the next told member and certifies.");
        Assert.AreEqual(2, second.Window.EvaluatedNodes, "Rc20 ClosedFiller: with the first told member inadmissible the walk spends one node on it before the second.");
        Assert.AreEqual(0, second.Window.MintedElements, "Rc20 ClosedFiller: the enumerated candidates are told individuals, so nothing is minted.");
    }

    /// <summary>
    /// The vacuity guard admits the escape mint: the universal narrowing the
    /// demand set holds at the carrier ONLY because the role under repair is
    /// empty there, so its activating membership is not re-derivable over the
    /// restricted class table and its enumeration-closed filler is EXCLUDED
    /// from the demand set. The set reads open, the witness is a choice-free
    /// fresh element, and the module certifies.
    /// </summary>
    [TestMethod]
    public void Rc21VacuityGuardAdmitsTheEscapeMint()
    {
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(VacuousUniversalEscapeModule());

        Assert.IsTrue(outcome.Consistent, "Rc21 VacuityGuard: the guarded demand set reads open, so the escape mint proceeds and the module certifies.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, outcome.Route, "Rc21 VacuityGuard: the repaired-described-model route names the certificate.");
        Assert.AreEqual(1, outcome.Window.MintedElements, "Rc21 VacuityGuard: the escape witness is exactly one fresh element.");
        Assert.AreEqual(0, outcome.Window.ChoicePointsOpened, "Rc21 VacuityGuard: the guarded set is open, so no choice frame is opened at all.");
        Assert.IsNull(outcome.ClashReason, "Rc21 VacuityGuard: a certificate names no clash reason.");
    }

    /// <summary>
    /// The guard-off control, reached through the construction-options seam:
    /// with every active universal narrowing the demand set, the vacuously
    /// activated universal closes the very demand the repair is trying to open.
    /// The set reads CLOSED, a two-candidate frame opens over the enumeration,
    /// both branches fail, and the face is SILENT — never a wrong verdict in
    /// either direction, which is what makes the guard a yield mechanism rather
    /// than a soundness fix.
    /// </summary>
    [TestMethod]
    public void Rc22UnguardedDemandSetWouldCloseAndSilence()
    {
        ReasoningModule module = VacuousUniversalEscapeModule();
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(module, new RepairingConstructionOptions
        {
            VacuityGuardMode = RepairVacuityGuardMode.Unguarded,
        });

        Assert.IsNull(outcome.Consistent, "Rc22 UnguardedDemandSet: with the guard off both branches of the closed frame fail and the face is silent.");
        Assert.IsNull(outcome.Route, "Rc22 UnguardedDemandSet: a silent certify face names no route.");
        Assert.IsNull(outcome.ClashReason, "Rc22 UnguardedDemandSet: the certify face never refutes, whatever the demand set reads.");
        Assert.AreEqual(0, outcome.Window.MintedElements, "Rc22 UnguardedDemandSet: a closed demand set refuses the mint.");
        Assert.AreEqual(1, outcome.Window.ChoicePointsOpened, "Rc22 UnguardedDemandSet: the closed demand opens the frame the guarded run never opens.");
        Assert.AreEqual(2, outcome.Window.EvaluatedNodes, "Rc22 UnguardedDemandSet: both enumerated candidates are spent before the component exhausts.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, RepairingFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, totals.EnumerationHabitat, "Rc22 UnguardedDemandSet: the module carries the Shape R census label on either guard setting.");
        Assert.AreEqual(0, totals.RepairingDeciderClashes, "Rc22 UnguardedDemandSet: no clash on the guard-off control's module.");
    }

    /// <summary>
    /// The fresh element triggers no defined class — no value pin matches a
    /// carrier that holds no edge — so the pinned class's own forced obligation
    /// fires exactly once, on the told member. The structural closure rides the
    /// same run: an OPEN demand set reaches neither candidate source, so its
    /// candidate list is EMPTY and no existing individual is proposable there,
    /// the filler-eligible smallest-IRI told individual included. The domain
    /// grows by the mint instead.
    /// </summary>
    [TestMethod]
    public void Rc23FreshMintBackDerivesNothingAndNoExistingCandidateIsReachable()
    {
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(FreshMintBackDerivationModule());

        Assert.IsTrue(outcome.Consistent, "Rc23 FreshMint: the fresh witness satisfies the existential and back-derives nothing.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, outcome.Route, "Rc23 FreshMint: the repaired-described-model route names the certificate.");
        Assert.AreEqual(1, outcome.Window.MintedElements, "Rc23 FreshMint: the open demand takes a fresh element, never the filler-eligible told individual beside it.");
        Assert.AreEqual(5, outcome.Window.CarrierCount, "Rc23 FreshMint: the four told terms and the one minted witness are the domain.");
        Assert.AreEqual(3, outcome.Window.CommittedEdges, "Rc23 FreshMint: the told edge, the ONE forced edge the defined class demands of its told member, and the mint edge — the fresh element enters no defined class, so it demands no fourth.");
        Assert.AreEqual(0, outcome.Window.ChoicePointsOpened, "Rc23 FreshMint: an open demand set has no candidate list, so no frame is opened over the existing individuals.");
    }

    /// <summary>
    /// Choice exhaustion is a SILENCE with its measurement on the record: every
    /// candidate the closed enumeration offers is committed, fully re-verified
    /// and rejected by an axiom failing AT the demand's own carrier, so the
    /// attribution rule advances that component until it wraps. The face goes
    /// silent with the spent nodes and passes measured — never a refutation
    /// over an exhausted, deliberately truncated walk.
    /// </summary>
    [TestMethod]
    public void Rc24ChoiceExhaustionSilences()
    {
        ReasoningModule module = ChoiceExhaustionModule();
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Rc24 ChoiceExhaustion: an exhausted component silences the face.");
        Assert.IsNull(outcome.Route, "Rc24 ChoiceExhaustion: a silent certify face names no route.");
        Assert.IsNull(outcome.ClashReason, "Rc24 ChoiceExhaustion: exhaustion is never a refutation.");
        Assert.AreEqual(1, outcome.Window.ChoicePointsOpened, "Rc24 ChoiceExhaustion: the one closed demand opens one frame.");
        Assert.AreEqual(3, outcome.Window.EvaluatedNodes, "Rc24 ChoiceExhaustion: all three enumerated candidates are evaluated before the component wraps.");
        Assert.AreEqual(3, outcome.Window.ModelVerifyPasses, "Rc24 ChoiceExhaustion: each candidate assignment is a complete leaf and takes its own whole-module pass.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, RepairingFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, totals.EnumerationHabitat, "Rc24 ChoiceExhaustion: the recognized-but-silent module still carries the Shape R census label.");
        Assert.AreEqual(0, totals.RepairingDeciderCertifications, "Rc24 ChoiceExhaustion: no certificate on the near-miss module.");
        Assert.AreEqual(0, totals.RepairingDeciderClashes, "Rc24 ChoiceExhaustion: no clash either.");
    }

    /// <summary>
    /// The mint ceiling is a named constant with overflow-SILENCE: a module
    /// whose owners demand one fresh element more than the ceiling admits stops
    /// at the ceiling and the face goes silent carrying the measurement. The
    /// owner count is derived from the constant itself, so no literal duplicates
    /// it.
    /// </summary>
    [TestMethod]
    public void Rc25MintBudgetOverflowSilences()
    {
        ReasoningModule module = MintBudgetOverflowModule(ContextRepairingCertifyDecider.RepairMintBound + 1);
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Rc25 MintBudgetOverflow: the refused mint past the ceiling silences the face.");
        Assert.IsNull(outcome.Route, "Rc25 MintBudgetOverflow: a silent certify face names no route.");
        Assert.IsNull(outcome.ClashReason, "Rc25 MintBudgetOverflow: a tripped bound is never a refutation.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairMintBound, outcome.Window.MintedElements, "Rc25 MintBudgetOverflow: the measurement records exactly the ceiling's many fresh elements.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, RepairingFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, totals.EnumerationHabitat, "Rc25 MintBudgetOverflow: the recognized-but-silent module still carries the Shape R census label.");
        Assert.AreEqual(0, totals.RepairingDeciderCertifications, "Rc25 MintBudgetOverflow: no certificate past the mint ceiling.");
        Assert.AreEqual(0, totals.RepairingDeciderClashes, "Rc25 MintBudgetOverflow: no clash either.");
    }

    /// <summary>
    /// The cascade ceiling is a named constant with overflow-SILENCE: each
    /// minted witness is typed into a class carrying the next existential, so
    /// the deterministic stage re-opens once per hop, and the hop past the
    /// ceiling silences the face with the mints it spent on the record. The
    /// chain length is derived from the constant itself.
    /// </summary>
    [TestMethod]
    public void Rc26CascadeDepthOverflowSilences()
    {
        ReasoningModule module = CascadeDepthModule(ContextRepairingCertifyDecider.RepairCascadeDepthBound + 1);
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Rc26 CascadeDepthOverflow: the hop past the cascade ceiling silences the face.");
        Assert.IsNull(outcome.Route, "Rc26 CascadeDepthOverflow: a silent certify face names no route.");
        Assert.IsNull(outcome.ClashReason, "Rc26 CascadeDepthOverflow: a tripped bound is never a refutation.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairCascadeDepthBound + 1, outcome.Window.MintedElements, "Rc26 CascadeDepthOverflow: one witness per hop, the hop that trips the ceiling included.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, RepairingFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, totals.EnumerationHabitat, "Rc26 CascadeDepthOverflow: the recognized-but-silent module still carries the Shape R census label.");
        Assert.AreEqual(0, totals.RepairingDeciderCertifications, "Rc26 CascadeDepthOverflow: no certificate past the cascade ceiling.");
        Assert.AreEqual(0, totals.RepairingDeciderClashes, "Rc26 CascadeDepthOverflow: no clash either.");
    }

    /// <summary>
    /// A verification failure the attribution rule cannot charge to any one
    /// component declares the decomposition INVALID and routes to SILENCE:
    /// the two closed demands sit in two computed components, the first failed
    /// check names an element neither of them owns, and the walk stops there
    /// rather than falling back to the cross-component product the node bound
    /// would then be unable to bind.
    /// </summary>
    [TestMethod]
    public void Rc27ComponentSpanningFailureSilencesWithoutAProductFallback()
    {
        ReasoningModule module = ComponentSpanningModule();
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Rc27 ComponentSpanningFailure: the unattributable failure silences the face.");
        Assert.IsNull(outcome.Route, "Rc27 ComponentSpanningFailure: a silent certify face names no route.");
        Assert.IsNull(outcome.ClashReason, "Rc27 ComponentSpanningFailure: an invalid decomposition is never a refutation.");
        Assert.AreEqual(2, outcome.Window.ChoicePointsOpened, "Rc27 ComponentSpanningFailure: the two closed demands open two frames.");
        Assert.AreEqual(2, outcome.Window.EvaluatedNodes, "Rc27 ComponentSpanningFailure: one node per component is spent before the pass runs.");
        Assert.AreEqual(1, outcome.Window.ModelVerifyPasses, "Rc27 ComponentSpanningFailure: the walk stops on the FIRST unattributable pass rather than enumerating the product.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, RepairingFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, totals.EnumerationHabitat, "Rc27 ComponentSpanningFailure: the recognized-but-silent module still carries the Shape R census label.");
        Assert.AreEqual(0, totals.RepairingDeciderCertifications, "Rc27 ComponentSpanningFailure: no certificate on the near-miss module.");
        Assert.AreEqual(0, totals.RepairingDeciderClashes, "Rc27 ComponentSpanningFailure: no clash either.");
    }

    /// <summary>
    /// The complement post-pass runs PER CANDIDATE MODEL, after the last mint
    /// and immediately before verification, so every complement is evaluated
    /// against the domain the verifier reads and the minted witness lands
    /// inside it. Placed at the end of the deterministic stage instead —
    /// selected through the construction-options seam — the same complement is
    /// frozen over a smaller domain, the witness falls outside it, and the face
    /// is SILENT. The pinned universal class rides the same run: it is re-read
    /// after the final mint rather than snapshotted before it.
    /// </summary>
    [TestMethod]
    public void Rc28ComplementPostPassRunsPerCandidateModelAfterTheLastMint()
    {
        RepairingOutcome production = ContextRepairingCertifyDecider.Run(ComplementAfterMintModule());

        Assert.IsTrue(production.Consistent, "Rc28 ComplementPostPass: the complement evaluated after the mint holds the witness, and the pinned universal class grows with the domain.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, production.Route, "Rc28 ComplementPostPass: the repaired-described-model route names the certificate.");
        Assert.AreEqual(1, production.Window.MintedElements, "Rc28 ComplementPostPass: the existential takes one fresh witness.");

        RepairingOutcome early = ContextRepairingCertifyDecider.Run(ComplementAfterMintModule(), new RepairingConstructionOptions
        {
            ComplementPlacement = RepairComplementPlacement.BeforeMints,
        });

        Assert.IsNull(early.Consistent, "Rc28 ComplementPostPass: a complement frozen over the pre-mint domain leaves the witness outside it and the face silent.");
        Assert.IsNull(early.Route, "Rc28 ComplementPostPass: a silent certify face names no route.");
        Assert.IsNull(early.ClashReason, "Rc28 ComplementPostPass: the certify face never refutes.");
    }

    /// <summary>
    /// A complement-defined class standing in an OBLIGATION-ACTIVATING position
    /// — the subclass side of an axiom whose superclass carries a value pin —
    /// is a DISJUNCTIVE MEMBERSHIP obligation, and this rung carries no
    /// membership-choice repair move at all: it invents edges and mints, never
    /// a membership. The module is SILENT rather than repaired.
    /// </summary>
    [TestMethod]
    public void Rc29ComplementInAnObligationActivatingPositionSilences()
    {
        ReasoningModule module = ComplementObligationPositionModule();
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Rc29 ComplementObligationPosition: a disjunctive obligation is unrepairable by construction, so the face is silent.");
        Assert.IsNull(outcome.Route, "Rc29 ComplementObligationPosition: a silent certify face names no route.");
        Assert.IsNull(outcome.ClashReason, "Rc29 ComplementObligationPosition: the certify face never refutes.");
        Assert.AreEqual(0, outcome.Window.MintedElements, "Rc29 ComplementObligationPosition: the module silences before any repair is proposed.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, RepairingFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, totals.EnumerationHabitat, "Rc29 ComplementObligationPosition: the recognized-but-silent module still carries the Shape R census label.");
        Assert.AreEqual(0, totals.RepairingDeciderCertifications, "Rc29 ComplementObligationPosition: no certificate on the near-miss module.");
        Assert.AreEqual(0, totals.RepairingDeciderClashes, "Rc29 ComplementObligationPosition: no clash either.");
    }

    /// <summary>
    /// The deterministic regime is the zero-choice regime of ONE algorithm, not
    /// a separate face, and it reports itself: a module the phase-0 closure and
    /// the phase-1 forced-value repair close alone certifies with no choice
    /// frame opened, no fresh element minted, and exactly ONE whole-module
    /// verification pass spent.
    /// </summary>
    [TestMethod]
    public void Rc30DeterministicRegimeReportsZeroChoicePointsOpened()
    {
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(ValuePinModule());

        Assert.IsTrue(outcome.Consistent, "Rc30 DeterministicRegime: the deterministic prefix closes the module alone.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, outcome.Route, "Rc30 DeterministicRegime: the repaired-described-model route names the certificate.");
        Assert.AreEqual(0, outcome.Window.ChoicePointsOpened, "Rc30 DeterministicRegime: zero choice points opened is the deterministic regime's marker.");
        Assert.AreEqual(0, outcome.Window.MintedElements, "Rc30 DeterministicRegime: the deterministic prefix invents no element.");
        Assert.AreEqual(1, outcome.Window.ModelVerifyPasses, "Rc30 DeterministicRegime: one candidate model, one whole-module verification pass.");
    }

    /// <summary>
    /// The bounds cost COMPLETENESS only, and the battery proves it directly: a
    /// satisfiable module whose passing assignment sits one node past a
    /// narrowed per-component node ceiling is SILENT, and the same module under
    /// the production ceiling — the options value left at its zero-means-
    /// production default for every other bound — certifies through the same
    /// node count. The narrowed ceiling is derived from the module's own
    /// candidate list, so no literal duplicates a constant.
    /// </summary>
    [TestMethod]
    public void Rc31NearMissBoundSilencesWhatAWiderWalkWouldCertify()
    {
        ReasoningModule module = NodeBoundModule();
        RepairingOutcome narrowed = ContextRepairingCertifyDecider.Run(module, new RepairingConstructionOptions
        {
            Bounds = new RepairingBounds(0, 0, 0, ColourMembers - 1, 0, 0, 0, 0),
        });

        Assert.IsNull(narrowed.Consistent, "Rc31 NearMissBound: one node past the narrowed ceiling the walk silences.");
        Assert.IsNull(narrowed.Route, "Rc31 NearMissBound: a silent certify face names no route.");
        Assert.IsNull(narrowed.ClashReason, "Rc31 NearMissBound: a tripped bound is never a refutation.");
        Assert.AreEqual(ColourMembers, narrowed.Window.EvaluatedNodes, "Rc31 NearMissBound: the ceiling trips on the node that would have carried the passing candidate.");

        RepairingOutcome widened = ContextRepairingCertifyDecider.Run(module);

        Assert.IsTrue(widened.Consistent, "Rc31 NearMissBound: under the production ceiling the same module certifies — the bound cost completeness, never soundness.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, widened.Route, "Rc31 NearMissBound: the repaired-described-model route names the certificate.");
        Assert.AreEqual(ColourMembers, widened.Window.EvaluatedNodes, "Rc31 NearMissBound: the widened walk spends exactly the nodes the narrowed one was cut at.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, RepairingFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, totals.EnumerationHabitat, "Rc31 NearMissBound: the module carries the Shape R census label on either ceiling.");
        Assert.AreEqual(1, totals.RepairingDeciderCertifications, "Rc31 NearMissBound: the production call path passes no options, so the reasoner reads the widened walk's certificate.");
    }

    /// <summary>
    /// An obligation no invention can satisfy is a SILENCE, never a
    /// refutation: the existential's filler is the empty class, so every
    /// proposed witness leaves the demand unmet, the repair runs out of moves,
    /// and the verdict stays absent. The certify face declares
    /// <see langword="false"/> on no path — exhaustion of a deliberately
    /// truncated, injective search says nothing about the model space.
    /// </summary>
    [TestMethod]
    public void Rc32UnrepairableObligationNeverRefutes()
    {
        ReasoningModule module = UnrepairableObligationModule();
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Rc32 UnrepairableObligation: the face is silent, and the null verdict is the whole assertion.");
        Assert.IsNull(outcome.Route, "Rc32 UnrepairableObligation: a silent certify face names no route.");
        Assert.IsNull(outcome.ClashReason, "Rc32 UnrepairableObligation: an unrepairable obligation is never a clash.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, RepairingFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, totals.EnumerationHabitat, "Rc32 UnrepairableObligation: the recognized-but-silent module still carries the Shape R census label.");
        Assert.AreEqual(0, totals.RepairingDeciderClashes, "Rc32 UnrepairableObligation: the certify face's limits never move the clash counter.");
        Assert.AreEqual(0, totals.RepairingDeciderCertifications, "Rc32 UnrepairableObligation: no certificate either.");
    }

    /// <summary>
    /// The monotone told-only clash face carries the told-ground discipline to
    /// the window Shape R opens: each of the four named reasons —
    /// complemented membership, asserted empty class, told disjoint partner and
    /// denied edge — refutes a Shape R module, and the same contradiction under
    /// a Shape W label is decided with the same verdict and the same reason
    /// body. Every module additionally carries a told sameness axiom whose
    /// quotient WOULD change carrier identity: the verdict and the reason are
    /// identical with the certify side running behind the face and with the
    /// clash-only entry point in front of it, which is the battery's pin that
    /// the quotient is never shared with the clash face.
    /// </summary>
    [TestMethod]
    public void Rc33ClashFaceRefutesAToldGroundContradictionUnderShapeR()
    {
        AssertClashPairAgrees(ComplementedMembershipCore(), ComplementedMembership, "Rc33 ClashFace (complemented membership)", TestContext.CancellationToken);
        AssertClashPairAgrees(AssertedNothingCore(), AssertedNothingMembership, "Rc33 ClashFace (asserted empty class)", TestContext.CancellationToken);
        AssertClashPairAgrees(DisjointMembershipCore(), DisjointMembership, "Rc33 ClashFace (told disjoint partner)", TestContext.CancellationToken);
        AssertClashPairAgrees(ContradictoryEdgeCore(), ContradictoryEdge, "Rc33 ClashFace (denied edge)", TestContext.CancellationToken);
    }

    /// <summary>
    /// The clash face is MONOTONE and the certify face is WHOLE-MODULE, and one
    /// axiom outside the evaluable grammar separates them: the certify face
    /// silences over the whole module, because consistency is not preserved
    /// under axiom addition, while the clash face — whose refutation over a told
    /// subset condemns every superset — still decides the same module
    /// inconsistent.
    /// </summary>
    [TestMethod]
    public void Rc34ClashFaceStaysMonotoneUnderAnUnadmittedAxiom()
    {
        RepairingOutcome silent = ContextRepairingCertifyDecider.Run(GroundModule(ValuePinAxioms(), SubClassOf(Class("Wine"), HasSelf("madeFrom"))));

        Assert.IsNull(silent.Consistent, "Rc34 MonotoneClash: an axiom outside the evaluable grammar silences the certify face over the whole module.");
        Assert.IsNull(silent.Route, "Rc34 MonotoneClash: a silent certify face names no route.");

        ReasoningModule module = GroundModule(DisjointMembershipCore(), SubClassOf(Class("Wine"), HasSelf("madeFrom")));
        RepairingOutcome refuted = ContextRepairingCertifyDecider.Run(module);

        Assert.IsFalse(refuted.Consistent, "Rc34 MonotoneClash: the monotone clash face ignores the unadmitted axiom and decides the told contradiction.");
        Assert.StartsWith(RepairingPrefix + DisjointMembership, refuted.ClashReason, StringComparison.Ordinal, "Rc34 MonotoneClash: the clash reason names the told disjointness.");
        Assert.IsNull(refuted.Route, "Rc34 MonotoneClash: a refutation names no certificate route.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, RepairingFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(1, totals.RepairingDeciderClashes, "Rc34 MonotoneClash: the clash face's counter reads the decision.");
        Assert.AreEqual(0, totals.RepairingDeciderCertifications, "Rc34 MonotoneClash: no certificate on the same module.");
    }

    /// <summary>
    /// The three window ceilings silence BOTH faces with the measurement on the
    /// record, each overflow derived from its own constant rather than from a
    /// literal: one carrier, one named class and one role past their ceilings
    /// each leave a module whose told arithmetic clashes outright undecided,
    /// and the carrier template sitting exactly AT its ceiling still decides,
    /// so the boundary is inclusive and the silence begins one element later.
    /// </summary>
    [TestMethod]
    public void Rc35WindowBoundariesSilenceBothFaces()
    {
        int carriers = ContextRepairingCertifyDecider.RepairCarrierBound + 1;
        ReasoningModule carrierModule = CarrierWindowModule(carriers);
        RepairingOutcome carrierOutcome = ContextRepairingCertifyDecider.Run(carrierModule);

        Assert.IsNull(carrierOutcome.Consistent, "Rc35 WindowBoundaries: both faces are silent past the carrier ceiling.");
        Assert.AreEqual(carriers, carrierOutcome.Window.CarrierCount, "Rc35 WindowBoundaries: the measured domain size is reported past the ceiling.");
        Assert.AreEqual(1, carrierOutcome.Window.WindowSilences, "Rc35 WindowBoundaries: the carrier overflow is charged to the window counter.");
        Assert.IsNull(carrierOutcome.ClashReason, "Rc35 WindowBoundaries: no reason past the ceiling, even where the told arithmetic clashes.");
        Assert.IsNull(carrierOutcome.Route, "Rc35 WindowBoundaries: no route past the ceiling.");

        int classes = ContextRepairingCertifyDecider.RepairClassBound + 1;
        RepairingOutcome classOutcome = ContextRepairingCertifyDecider.Run(ClassWindowModule(classes));

        Assert.IsNull(classOutcome.Consistent, "Rc35 WindowBoundaries: both faces are silent past the named-class ceiling.");
        Assert.AreEqual(classes, classOutcome.Window.ClassCount, "Rc35 WindowBoundaries: the measured class count is reported past the ceiling.");
        Assert.AreEqual(1, classOutcome.Window.WindowSilences, "Rc35 WindowBoundaries: the class overflow is charged to the window counter.");

        int roles = ContextRepairingCertifyDecider.RepairRoleBound + 1;
        RepairingOutcome roleOutcome = ContextRepairingCertifyDecider.Run(RoleWindowModule(roles));

        Assert.IsNull(roleOutcome.Consistent, "Rc35 WindowBoundaries: both faces are silent past the role ceiling.");
        Assert.AreEqual(roles, roleOutcome.Window.RoleCount, "Rc35 WindowBoundaries: the measured role count is reported past the ceiling.");
        Assert.AreEqual(1, roleOutcome.Window.WindowSilences, "Rc35 WindowBoundaries: the role overflow is charged to the window counter.");

        RepairingOutcome atBound = ContextRepairingCertifyDecider.Run(CarrierWindowModule(ContextRepairingCertifyDecider.RepairCarrierBound));

        Assert.IsFalse(atBound.Consistent, "Rc35 WindowBoundaries: the clash face decides AT the carrier ceiling — the boundary is inclusive.");
        Assert.AreEqual(0, atBound.Window.WindowSilences, "Rc35 WindowBoundaries: no window silence exactly at the ceiling.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairCarrierBound, atBound.Window.CarrierCount, "Rc35 WindowBoundaries: the measured carrier ceiling is the face's own constant.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairClassBound, atBound.Window.CarrierCount, "Rc35 WindowBoundaries: the named-class ceiling shares the measured carrier ceiling — this family's own boundary discipline.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(carrierModule, RepairingFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, totals.EnumerationHabitat, "Rc35 WindowBoundaries: the window-silent module still carries the Shape R census label.");
        Assert.AreEqual(1, totals.RepairingWindowExceededCarriers, "Rc35 WindowBoundaries: the window silence rides the statistics record.");
        Assert.AreEqual(carriers, totals.RepairingCarrierCount, "Rc35 WindowBoundaries: the measured carriers ride the statistics record.");
        Assert.AreEqual(0, totals.RepairingDeciderClashes, "Rc35 WindowBoundaries: no clash past the carrier ceiling.");
        Assert.AreEqual(0, totals.RepairingDeciderCertifications, "Rc35 WindowBoundaries: no certificate past the carrier ceiling.");
    }

    /// <summary>
    /// The dark control: the measurement-only entry point computes the census
    /// window identically dark and lit and forms no verdict on any path, and
    /// under the explicit <see cref="EnumerationDeciderFaces.None"/> selection
    /// the module keeps its engine-face budget abstention while the census still
    /// ships — the habitat label and both measured numbers on the record with
    /// neither decision counter moved.
    /// </summary>
    [TestMethod]
    public void Rc36DarkFacesDecideNothing()
    {
        ReasoningModule module = DarkControlModule();
        RepairingOutcome measured = ContextRepairingCertifyDecider.Measure(module);

        Assert.IsNull(measured.Consistent, "Rc36 DarkFaces: the measurement-only entry point forms no verdict.");
        Assert.IsNull(measured.Route, "Rc36 DarkFaces: a measurement names no certificate route.");
        Assert.IsNull(measured.ClashReason, "Rc36 DarkFaces: a measurement names no clash reason.");
        Assert.AreEqual(GroundFloorTerms, measured.Window.CarrierCount, "Rc36 DarkFaces: the told carriers are measured with no construction behind them.");
        Assert.AreEqual(0, measured.Window.MintedElements, "Rc36 DarkFaces: a measurement invents nothing.");
        Assert.AreEqual(0, measured.Window.WindowSilences, "Rc36 DarkFaces: the module sits inside every ceiling.");

        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, DarkBudget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome, "Rc36 DarkFaces: with both bits dark the module keeps its engine abstention.");
        Assert.IsNull(decision.Verdict, "Rc36 DarkFaces: the dark abstention carries no verdict.");
        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, totals.EnumerationHabitat, "Rc36 DarkFaces: the habitat label rides the dark abstention record.");
        Assert.AreEqual(measured.Window.CarrierCount, totals.RepairingCarrierCount, "Rc36 DarkFaces: the carriers are measured dark.");
        Assert.AreEqual(measured.Window.CommittedEdges, totals.RepairingCommittedEdgeCount, "Rc36 DarkFaces: the told edges are measured dark.");
        Assert.AreEqual(0, totals.RepairingWindowExceededCarriers, "Rc36 DarkFaces: no window silence dark.");
        Assert.AreEqual(0, totals.RepairingDeciderClashes, "Rc36 DarkFaces: no clash decision with the faces dark.");
        Assert.AreEqual(0, totals.RepairingDeciderCertifications, "Rc36 DarkFaces: no certificate with the faces dark.");
    }

    /// <summary>
    /// The phase-3 pruning filter is a DISTINCT OBJECT from the clash face: it
    /// reads the committed edge set the monotone face may never see, and it
    /// DECLARES NOTHING. On a module whose only passing candidate sits behind
    /// an intermediate node the filter rejects — the told range that would admit
    /// the candidate is only seeded once the candidate is committed — every
    /// branch is cut before a verification pass runs, the face is SILENT, no
    /// clash reason is written, and face fourteen's own counter does not move.
    /// </summary>
    [TestMethod]
    public void Rc37PruningFilterOverPrunesIntoSilenceAndNeverDeclares()
    {
        ReasoningModule module = OverPrunedModule();
        RepairingOutcome outcome = ContextRepairingCertifyDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Rc37 PruningFilter: over-pruning routes to the same exhaustion silence as an exhausted branch.");
        Assert.IsNull(outcome.Route, "Rc37 PruningFilter: a silent certify face names no route.");
        Assert.IsNull(outcome.ClashReason, "Rc37 PruningFilter: the filter writes no clash reason on any path.");
        Assert.AreEqual(2, outcome.Window.EvaluatedNodes, "Rc37 PruningFilter: both candidates are cut at the intermediate node.");
        Assert.AreEqual(0, outcome.Window.ModelVerifyPasses, "Rc37 PruningFilter: a pruned branch produces no candidate model, so no verification pass runs.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, RepairingFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, totals.EnumerationHabitat, "Rc37 PruningFilter: the recognized-but-silent module still carries the Shape R census label.");
        Assert.AreEqual(0, totals.RepairingDeciderClashes, "Rc37 PruningFilter: the filter moves no statistics field the clash face owns.");
        Assert.AreEqual(0, totals.RepairingDeciderCertifications, "Rc37 PruningFilter: no certificate either.");
    }

    /// <summary>
    /// The universal-as-generator rule's SUPERSET-DIRECTION VACUOUS FIRING is
    /// sound and load-bearing: a universal firing at a carrier with no
    /// successor is what carries its defining equivalence to VERIFIED, and the
    /// module certifies. With that firing suppressed — the exclusion the
    /// standing widening lock forbids, reached through the construction-options
    /// seam — the same equivalence FAILS and no certificate is taken. The
    /// obligation is the opposite of an exclusion: admit the generator and pair
    /// it with a repair able to falsify it.
    /// </summary>
    [TestMethod]
    public void Rc38VacuousUniversalFiringKeepsTheEquivalenceVerified()
    {
        RepairingOutcome production = ContextRepairingCertifyDecider.Run(VacuousFiringModule());

        Assert.IsTrue(production.Consistent, "Rc38 VacuousUniversalFiring: the vacuous firing carries the equivalence to verified and the module certifies.");
        Assert.AreEqual(ContextRepairingCertifyDecider.RepairedDescribedModelCertificate, production.Route, "Rc38 VacuousUniversalFiring: the repaired-described-model route names the certificate.");

        RepairingOutcome suppressed = ContextRepairingCertifyDecider.Run(VacuousFiringModule(), new RepairingConstructionOptions
        {
            GeneratorMode = RepairGeneratorMode.UniversalRequiresSuccessor,
        });

        Assert.IsNull(suppressed.Consistent, "Rc38 VacuousUniversalFiring: with the firing suppressed the equivalence fails and no certificate is taken.");
        Assert.IsNull(suppressed.Route, "Rc38 VacuousUniversalFiring: a silent certify face names no route.");
        Assert.IsNull(suppressed.ClashReason, "Rc38 VacuousUniversalFiring: the suppression is a yield hazard, never a soundness one — the face still never refutes.");
    }

    /// <summary>Asserts that one axiom kind outside the certify admission silences a module that otherwise certifies, while the monotone clash face still decides the same kind beside a told ground contradiction.</summary>
    /// <param name="rejected">The rejected axiom appended to both modules.</param>
    /// <param name="row">The row and case label the assertion messages open with.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    private static void AssertRejectedKindSilencesCertifyOnly(OwlAxiom rejected, string row, CancellationToken cancellationToken)
    {
        RepairingOutcome silent = ContextRepairingCertifyDecider.Run(GroundModule(ValuePinAxioms(), rejected));

        Assert.IsNull(silent.Consistent, row + ": the rejected kind silences the certify face over the whole module.");
        Assert.IsNull(silent.Route, row + ": a silent certify face names no route.");
        Assert.IsNull(silent.ClashReason, row + ": the certify face never refutes on the rejection path.");

        ReasoningModule clashing = GroundModule(DisjointMembershipCore(), rejected);
        RepairingOutcome refuted = ContextRepairingCertifyDecider.Run(clashing);

        Assert.IsFalse(refuted.Consistent, row + ": the monotone clash face decides the told contradiction beside the rejected kind.");
        Assert.StartsWith(RepairingPrefix + DisjointMembership, refuted.ClashReason, StringComparison.Ordinal, row + ": the clash reason names the told disjointness.");
        Assert.IsNull(refuted.Route, row + ": a refutation names no certificate route.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(clashing, RepairingFaces, ProbeBudget, cancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(1, totals.RepairingDeciderClashes, row + ": the clash face's counter reads the decision.");
        Assert.AreEqual(0, totals.RepairingDeciderCertifications, row + ": no certificate on the same module.");
    }

    /// <summary>Asserts that one told ground contradiction is refuted under the Shape R label with and without the certify side running behind it, and that the same contradiction under a Shape W label is decided with the same verdict and the same reason body.</summary>
    /// <param name="core">The clash core the two modules are built around.</param>
    /// <param name="reason">The clash reason kind both labels name.</param>
    /// <param name="row">The row and case label the assertion messages open with.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    private static void AssertClashPairAgrees(OwlAxiom[] core, string reason, string row, CancellationToken cancellationToken)
    {
        ReasoningModule shapeR = GroundModule(core);
        RepairingOutcome repairing = ContextRepairingCertifyDecider.Run(shapeR);

        Assert.IsFalse(repairing.Consistent, row + ": the monotone told-only clash face decides the Shape R module.");
        Assert.StartsWith(RepairingPrefix + reason, repairing.ClashReason, StringComparison.Ordinal, row + ": the clash reason names the told contradiction.");
        Assert.IsNull(repairing.Route, row + ": a refutation names no certificate route.");

        RepairingOutcome clashOnly = ContextRepairingCertifyDecider.RunClashOnly(shapeR);

        Assert.IsFalse(clashOnly.Consistent, row + ": the clash-only entry point decides the same module with no construction behind it.");
        Assert.AreEqual(repairing.ClashReason, clashOnly.ClashReason, row + ": the verdict and the reason are identical with and without the certify side running, so the quotient never reaches the clash face.");

        ReasoningModule shapeW = ToldGroundClashModule(core);
        ToldGroundWitnessOutcome told = ContextToldGroundWitnessDecider.Run(shapeW);

        Assert.IsFalse(told.Consistent, row + ": the same contradiction under a Shape W label is decided identically.");
        Assert.AreEqual(repairing.ClashReason![RepairingPrefix.Length..], told.ClashReason![ToldGroundPrefix.Length..], row + ": the two labels' reasons name the same kind over the same term.");

        ContextSaturationStatistics repairingTotals = ContextSaturationModuleReasoner.DecideModule(shapeR, AllFaces, ProbeBudget, cancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, repairingTotals.EnumerationHabitat, row + ": the clash-bearing module carries the Shape R census label.");
        Assert.AreEqual(1, repairingTotals.RepairingDeciderClashes, row + ": face fourteen's counter reads the decision.");
        Assert.AreEqual(0, repairingTotals.RepairingDeciderCertifications, row + ": a refuted module takes no certificate.");

        ContextSaturationStatistics toldTotals = ContextSaturationModuleReasoner.DecideModule(shapeW, AllFaces, ProbeBudget, cancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.ToldGroundWitness, toldTotals.EnumerationHabitat, row + ": the relabelled module carries the Shape W census label.");
        Assert.AreEqual(1, toldTotals.ToldGroundWitnessDeciderClashes, row + ": the told-ground clash face's counter reads the same decision.");
    }

    /// <summary>The manifest-exact premise module of one corpus test: the inline RDF/XML premise parsed against the test's own base IRI, expanded through the imports closure the test supplies, and mapped to structural form.</summary>
    /// <param name="identifier">The manifest test identifier.</param>
    /// <returns>The premise module.</returns>
    private static ReasoningModule CorpusPremiseModule(string identifier)
    {
        Owl2TestCase? found = null;
        for(int index = 0; index < ApprovedCorpusCases.Length; index++)
        {
            if(string.Equals(ApprovedCorpusCases[index].Identifier, identifier, StringComparison.Ordinal))
            {
                found = ApprovedCorpusCases[index];
                break;
            }
        }

        Assert.IsNotNull(found, identifier + ": the approved manifest declares no such test case; the row cannot be set up.");
        Assert.IsNotNull(found.RdfXmlPremise, identifier + ": the test declares no RDF/XML premise document; the row cannot be set up.");

        DiagnosticBag diagnostics = new();
        List<Quad> quads = [.. RdfXmlReader.Read(found.RdfXmlPremise.Value.Memory, diagnostics, baseIri: Utf8Strings.From(found.Uri.AbsoluteUri))];

        Assert.IsFalse(diagnostics.HasErrors, identifier + ": the premise document did not parse as RDF/XML; the row cannot be set up.");

        OwlOntologyDocument premise = OwlRdfMapper.Map(Owl2ImportResolver.Expand(found, quads));

        Assert.IsFalse(premise.Diagnostics.HasErrors, identifier + ": the premise closure did not map to structural form; the row cannot be set up.");

        return new ReasoningModule([.. premise.Axioms], Violations: []);
    }

    /// <summary>Builds a Shape R module: the row's own axioms, then the two obligation-position restrictions that carry the recognizer's counting mention, then the padding individuals that clear its told-individual floor.</summary>
    /// <param name="core">The row's own axioms.</param>
    /// <param name="extra">The axioms the row appends after its core.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule GroundModule(OwlAxiom[] core, params OwlAxiom[] extra)
    {
        List<OwlAxiom> axioms = [.. core, .. extra];
        axioms.Add(SubClassOf(Class("ShapeRAnchor"), Max("anchorRole", 1)));
        axioms.Add(SubClassOf(Class("ShapeRAnchor"), All("anchorRole", Class("AnchorFiller"))));
        for(int index = 0; index < GroundFloorTerms; index++)
        {
            axioms.Add(ClassAssertion(Class("Padding"), Individual("pad" + index)));
        }

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The Shape W counterpart of a clash core: the same told contradiction beside the told object-property assertion, the told inverse pair, the told plain-role existential and the nominal anchor the told-ground-witness probe reads, over a told population inside that face's own carrier window.</summary>
    /// <param name="core">The clash core.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ToldGroundClashModule(OwlAxiom[] core)
    {
        List<OwlAxiom> axioms = [.. core];
        axioms.Add(Edge("hasEuroMP", Individual("uk"), Individual("kinnock")));
        axioms.Add(InverseProperties("isEuroMPFrom", "hasEuroMP"));
        axioms.Add(Equivalent(Class("EuroMP"), Some("isEuroMPFrom", Thing)));
        axioms.Add(Equivalent(Class("Anchor"), OneOf("uk")));

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The complemented-membership clash core: one told term derived into a named class and denied of the same class, beside a told sameness whose quotient would change carrier identity.</summary>
    /// <returns>The core axioms.</returns>
    private static OwlAxiom[] ComplementedMembershipCore()
    {
        return
        [
            ClassAssertion(Class("Clashing"), Individual("a")),
            ClassAssertion(Complement(Class("Clashing")), Individual("a")),
            Same("a", "aAlias"),
            ClassAssertion(Class("Alias"), Individual("aAlias")),
        ];
    }

    /// <summary>The empty-class assertion clash core: a told typing with <c>owl:Nothing</c>, beside a told sameness whose quotient would change carrier identity.</summary>
    /// <returns>The core axioms.</returns>
    private static OwlAxiom[] AssertedNothingCore()
    {
        return
        [
            ClassAssertion(Nothing, Individual("a")),
            Same("a", "aAlias"),
            ClassAssertion(Class("Alias"), Individual("aAlias")),
        ];
    }

    /// <summary>The disjointness clash core: one told term in two told-disjoint named classes, beside a told sameness whose quotient would change carrier identity.</summary>
    /// <returns>The core axioms.</returns>
    private static OwlAxiom[] DisjointMembershipCore()
    {
        return
        [
            Disjoint(Class("Clashing"), Class("Partner")),
            ClassAssertion(Class("Clashing"), Individual("a")),
            ClassAssertion(Class("Partner"), Individual("a")),
            Same("a", "aAlias"),
            ClassAssertion(Class("Alias"), Individual("aAlias")),
        ];
    }

    /// <summary>The denied-edge clash core: one told edge met by its own told denial, beside a told sameness whose quotient would change carrier identity.</summary>
    /// <returns>The core axioms.</returns>
    private static OwlAxiom[] ContradictoryEdgeCore()
    {
        return
        [
            Edge("denied", Individual("a"), Individual("b")),
            DeniedEdge("denied", Individual("a"), Individual("b")),
            Same("a", "aAlias"),
            ClassAssertion(Class("Alias"), Individual("aAlias")),
        ];
    }

    /// <summary>The sub-property closure module: one told edge on a subproperty, the told inclusion that carries it into the superproperty, and the told range and universal that read the derived edge.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SubPropertyClosureModule()
    {
        return Module(
            SubProperty("hasPart", "hasComponent"),
            Edge("hasPart", Individual("whole"), Individual("part")),
            ClassAssertion(Class("Assembly"), Individual("whole")),
            SubClassOf(Class("Assembly"), All("hasComponent", Class("Part"))),
            Range("hasComponent", Class("Part")));
    }

    /// <summary>The inverse-mirroring module: a value pin whose forced edge lands on a told inverse-paired role, so the exact-converse re-check reads whatever the closure discipline left behind.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule InvertedForcedEdgeModule()
    {
        return Module(
            InverseProperties("owns", "ownedBy"),
            ClassAssertion(Class("Owner"), Individual("alice")),
            SubClassOf(Class("Owner"), HasValue("owns", Individual("car"))),
            ClassAssertion(Class("Vehicle"), Individual("car")));
    }

    /// <summary>The symmetry module: a value pin whose forced edge lands on a told symmetric role.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SymmetricForcedEdgeModule()
    {
        return Module(
            Symmetric("knows"),
            ClassAssertion(Class("Sociable"), Individual("ann")),
            SubClassOf(Class("Sociable"), HasValue("knows", Individual("bob"))),
            ClassAssertion(Class("Person"), Individual("bob")));
    }

    /// <summary>The transitivity module: a value pin whose forced edge composes with a told edge on the same told transitive role, so the derived obligation lands at the forcing carrier itself.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule TransitiveForcedEdgeModule()
    {
        return Module(
            Transitive("locatedIn"),
            Edge("locatedIn", Individual("cellar"), Individual("house")),
            Edge("locatedIn", Individual("region"), Individual("country")),
            ClassAssertion(Class("Bottle"), Individual("cellar")),
            SubClassOf(Class("Bottle"), HasValue("locatedIn", Individual("region"))));
    }

    /// <summary>The keying module: one IRI mentioned through two object-distinct but content-equal references, one blank label mentioned through two separately constructed anonymous terms, and two further named terms, with the row's own perturbation appended last.</summary>
    /// <param name="extra">The axioms the row appends.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule KeyingModule(params OwlAxiom[] extra)
    {
        List<OwlAxiom> axioms =
        [
            ClassAssertion(Class("Wine"), Individual("chablis")),
            ClassAssertion(Class("White"), Individual("chablis")),
            ClassAssertion(Class("Region"), Anonymous("estate")),
            ClassAssertion(Class("Area"), Anonymous("estate")),
            ClassAssertion(Class("Estate"), Individual("chablisEstate")),
            ClassAssertion(Class("Cellar"), Individual("cave")),
            .. extra,
        ];

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The sameness-and-difference module: one pair told both same and different, whose two terms are syntactically distinct and quotient-identical.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SameAndDifferentModule()
    {
        return Module(
            Same("x", "y"),
            Different("x", "y"),
            ClassAssertion(Class("Term"), Individual("x")));
    }

    /// <summary>The value-pin axioms: one told typing, the value pin its class carries in obligation position, and the pinned individual's own typing.</summary>
    /// <returns>The axioms.</returns>
    private static OwlAxiom[] ValuePinAxioms()
    {
        return
        [
            ClassAssertion(Class("Wine"), Individual("chablis")),
            SubClassOf(Class("Wine"), HasValue("madeFrom", Individual("grape"))),
            ClassAssertion(Class("Grape"), Individual("grape")),
        ];
    }

    /// <summary>The value-pin module: the deterministic repair's minimal shape.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ValuePinModule()
    {
        return Module(ValuePinAxioms());
    }

    /// <summary>The two-pin module: one carrier in two classes whose value pins name different individuals on one told functional role, with the pin axioms told in either order.</summary>
    /// <param name="reversed">Whether the two pin axioms are told in the reversed order.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule TwoPinsOnAFunctionalRoleModule(bool reversed)
    {
        OwlAxiom first = SubClassOf(Class("Wine"), HasValue("madeFrom", Individual("grapeA")));
        OwlAxiom second = SubClassOf(Class("Blend"), HasValue("madeFrom", Individual("grapeB")));

        return GroundModule(
        [
            Functional("madeFrom"),
            ClassAssertion(Class("Wine"), Individual("cuvee")),
            ClassAssertion(Class("Blend"), Individual("cuvee")),
            reversed ? second : first,
            reversed ? first : second,
        ]);
    }

    /// <summary>The intersection-conjunct module: the value pin told as a conjunct on the DEFINING side of an equivalence rather than as a subclass superclass.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule IntersectionConjunctPinModule()
    {
        return Module(
            ClassAssertion(Class("Wine"), Individual("chablis")),
            ClassAssertion(Class("Drink"), Individual("chablis")),
            Equivalent(Class("Wine"), Intersection(Class("Drink"), HasValue("madeFrom", Individual("grape")))),
            ClassAssertion(Class("Grape"), Individual("grape")));
    }

    /// <summary>The uninvertible-conjunct module: an equivalence whose defining side carries a universal conjunct a told edge already falsifies, so its failing direction names an obligation no INVENTED edge can meet — only a retraction would, and a told edge is never retracted.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule PureNamedIntersectionModule()
    {
        return GroundModule(
        [
            Equivalent(Class("Premium"), Intersection(Class("Red"), All("tastes", Class("Fine")))),
            ClassAssertion(Class("Premium"), Individual("barolo")),
            Edge("tastes", Individual("barolo"), Individual("plain")),
            ClassAssertion(Class("Plain"), Individual("plain")),
        ]);
    }

    /// <summary>The exact-cardinality forced-arm module: an exact bound of one beside the value pin that already forces its successor.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ExactCardinalityForcedArmModule()
    {
        return Module(
            ClassAssertion(Class("Wine"), Individual("chablis")),
            SubClassOf(Class("Wine"), Intersection(HasValue("madeFrom", Individual("grape")), Exact("madeFrom", 1))),
            ClassAssertion(Class("Grape"), Individual("grape")));
    }

    /// <summary>The bound pre-check module: a minimum-cardinality deficit the deterministic repair leaves at one successor short, on a role whose told maximum the carrier already meets, so the mint the deficit asks for would break that maximum.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule BoundPreCheckModule()
    {
        return GroundModule(
        [
            ClassAssertion(Class("Wine"), Individual("chablis")),
            SubClassOf(Class("Wine"), HasValue("hasMaker", Individual("maker"))),
            SubClassOf(Class("Wine"), Min("hasMaker", 2)),
            SubClassOf(Class("Wine"), Max("hasMaker", 1)),
            ClassAssertion(Class("Maker"), Individual("maker")),
        ]);
    }

    /// <summary>The anti-monotone module: a class defined by a universal that holds vacuously before the forced edge goes in and must LEAVE the carrier once the edge names a successor outside the filler, with a told disjointness against the forcing class as the observable.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule AntiMonotoneUniversalModule()
    {
        return Module(
            Equivalent(Class("Universal"), All("tastes", Class("Filler"))),
            ClassAssertion(Class("Ground"), Individual("x")),
            SubClassOf(Class("Ground"), HasValue("tastes", Individual("y"))),
            ClassAssertion(Class("Other"), Individual("y")),
            Disjoint(Class("Universal"), Class("Ground")));
    }

    /// <summary>The told-data-pair module: one told data-property assertion with the domain and the plain-datatype range the told-pairs reading verifies against it.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ToldDataPairModule()
    {
        return Module(
            ClassAssertion(Class("Vintage"), Individual("v1")),
            DataAssertion(Individual("v1"), "year", TypedLiteral("1998")),
            DataDomain("year", Class("Vintage")),
            DataRangeAxiom("year", new OwlDatatypeReference(Datatype())));
    }

    /// <summary>The data-range-expression module: the same told pair under a data RANGE EXPRESSION, which puts a lower bound back on the extension and is admission-reject.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ToldDataRangeExpressionModule()
    {
        return Module(
            ClassAssertion(Class("Vintage"), Individual("v1")),
            DataAssertion(Individual("v1"), "year", TypedLiteral("1998")),
            DataDomain("year", Class("Vintage")),
            DataRangeAxiom("year", new OwlDataOneOf([TypedLiteral("1998")])));
    }

    /// <summary>The role-linked module: a told functional characteristic, a told inverse pair and a told existential all bound to ONE role — the bijection-chain signal, consulted ahead of Shape R on both paths.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule RoleLinkedModule()
    {
        return Module(
            Functional("isEuroMPFrom"),
            InverseProperties("isEuroMPFrom", "hasEuroMP"),
            Equivalent(Class("EuroMP"), Some("isEuroMPFrom", Thing)),
            Edge("hasEuroMP", Individual("uk"), Individual("kinnock")),
            Equivalent(Class("Anchor"), OneOf("uk")));
    }

    /// <summary>The told-ground module: the Shape W signal inside that face's own carrier window, carrying no obligation-position restriction at all, so the Shape R probe declines it.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ToldGroundWitnessModule()
    {
        return Module(
            InverseProperties("isEuroMPFrom", "hasEuroMP"),
            Equivalent(Class("EuroMP"), Some("isEuroMPFrom", Thing)),
            Edge("hasEuroMP", Individual("uk"), Individual("kinnock")),
            Equivalent(Class("Anchor"), OneOf("uk")));
    }

    /// <summary>The nominal-free Shape R module: a cardinality obligation and a universal over a told population above the told-ground ceiling, with no value pin and no enumeration anywhere, so the recognizer reaches the Shape R probe through its nominal-free path.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NominalFreeGroundModule()
    {
        List<OwlAxiom> axioms =
        [
            ClassAssertion(Class("Wine"), Individual("w0")),
            SubClassOf(Class("Wine"), Min("madeFrom", 1)),
            SubClassOf(Class("Wine"), All("madeFrom", Class("Grape"))),
        ];
        for(int index = 0; index < GroundFloorTerms; index++)
        {
            axioms.Add(ClassAssertion(Class("Padding"), Individual("pad" + index)));
        }

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The open-filler module: the requested number of owners of one existential obligation whose filler no enumeration closes, over a role no told assertion ever names.</summary>
    /// <param name="owners">The carriers carrying the obligation.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule OpenFillerMintModule(int owners)
    {
        List<OwlAxiom> axioms = [];
        for(int index = 0; index < owners; index++)
        {
            axioms.Add(ClassAssertion(Class("Wine"), Individual("owner" + index)));
        }

        axioms.Add(SubClassOf(Class("Wine"), Some("hasMaker", Class("Maker"))));

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The closed-filler module: an existential into an enumeration-equated class beside a told range, with the enumerated members from the given told position onwards admitted into that range and the earlier ones left outside it.</summary>
    /// <param name="admissibleFrom">The told position the range admits from.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ClosedFillerModule(int admissibleFrom)
    {
        string[] members = ["red", "white", "rose"];
        List<OwlAxiom> axioms =
        [
            Equivalent(Class("Colour"), OneOf(members)),
            ClassAssertion(Class("Wine"), Individual("chablis")),
            SubClassOf(Class("Wine"), Some("hasColour", Class("Colour"))),
            Range("hasColour", Class("Pale")),
        ];
        for(int index = admissibleFrom; index < members.Length; index++)
        {
            axioms.Add(ClassAssertion(Class("Pale"), Individual(members[index])));
        }

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The vacuous-universal escape module: a universal over the role under repair whose activating class holds at the carrier ONLY because that role is empty there, and whose filler an enumeration closes — the shape the guard admits the escape mint through and the guard-off control closes.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule VacuousUniversalEscapeModule()
    {
        return GroundModule(
        [
            Equivalent(Class("Vacuous"), All("hasMaker", Class("Closed"))),
            Equivalent(Class("Closed"), OneOf("m1", "m2")),
            ClassAssertion(Class("Wine"), Individual("chablis")),
            SubClassOf(Class("Wine"), Some("hasMaker", Class("Maker"))),
        ]);
    }

    /// <summary>The fresh-mint module: an open existential demand beside a filler-eligible told individual whose own membership in a value-pinned defined class back-derives a further obligation — one the fresh witness never inherits, because no value pin matches a carrier holding no edge.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule FreshMintBackDerivationModule()
    {
        return Module(
            ClassAssertion(Class("Wine"), Individual("chablis")),
            SubClassOf(Class("Wine"), Some("hasMaker", Class("Maker"))),
            ClassAssertion(Class("Maker"), Individual("aardvarkMaker")),
            Edge("locatedIn", Individual("aardvarkMaker"), Individual("burgundy")),
            Equivalent(Class("Pinned"), HasValue("locatedIn", Individual("burgundy"))),
            SubClassOf(Class("Pinned"), HasValue("registeredIn", Individual("registry"))));
    }

    /// <summary>The choice-exhaustion module: an enumeration-closed demand every candidate of which is committed, re-verified and rejected by a cap failing at the demand's own carrier.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ChoiceExhaustionModule()
    {
        return GroundModule(
        [
            Equivalent(Class("Colour"), OneOf("red", "white", "rose")),
            ClassAssertion(Class("Wine"), Individual("chablis")),
            SubClassOf(Class("Wine"), Some("hasColour", Class("Colour"))),
            SubClassOf(Class("Wine"), Max("hasColour", 0)),
        ]);
    }

    /// <summary>The mint-overflow module: the requested number of owners of one open existential obligation, each demanding its own fresh witness.</summary>
    /// <param name="owners">The carriers carrying the obligation.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule MintBudgetOverflowModule(int owners)
    {
        List<OwlAxiom> axioms = [];
        for(int index = 0; index < owners; index++)
        {
            axioms.Add(ClassAssertion(Class("Wine"), Individual("owner" + index)));
        }

        axioms.Add(SubClassOf(Class("Wine"), Some("hasMaker", Class("Maker"))));

        return GroundModule([.. axioms]);
    }

    /// <summary>The cascade module: a chain of existentials each of whose fresh witnesses is typed into the class carrying the next, so the deterministic stage re-opens once per hop.</summary>
    /// <param name="steps">The chain hops.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule CascadeDepthModule(int steps)
    {
        List<OwlAxiom> axioms = [ClassAssertion(Class("C0"), Individual("seed"))];
        for(int index = 0; index < steps; index++)
        {
            axioms.Add(SubClassOf(Class("C" + index), Some("cascade", Class("C" + (index + 1)))));
        }

        return GroundModule([.. axioms]);
    }

    /// <summary>The component-spanning module: two closed demands the coupling relation computes into two components, beside a cap failing at a carrier neither of them owns.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ComponentSpanningModule()
    {
        return GroundModule(
        [
            Equivalent(Class("Colour"), OneOf("red", "white")),
            Equivalent(Class("Body"), OneOf("light", "full")),
            ClassAssertion(Class("WineA"), Individual("wineA")),
            SubClassOf(Class("WineA"), Some("hasColour", Class("Colour"))),
            ClassAssertion(Class("WineB"), Individual("wineB")),
            SubClassOf(Class("WineB"), Some("hasBody", Class("Body"))),
            ClassAssertion(Class("Broken"), Individual("orphan")),
            Edge("brokenRole", Individual("orphan"), Individual("sink")),
            SubClassOf(Class("Broken"), Max("brokenRole", 0)),
        ]);
    }

    /// <summary>The complement-placement module: a minted witness that must lie inside a complement evaluated over the FINAL domain, beside a class equated with <c>owl:Thing</c> that must grow with that domain.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ComplementAfterMintModule()
    {
        return Module(
            ClassAssertion(Class("Wine"), Individual("chablis")),
            SubClassOf(Class("Wine"), Some("hasMaker", Class("Maker"))),
            SubClassOf(Class("Maker"), Complement(Class("Wine"))),
            Equivalent(Class("Everything"), Thing),
            SubClassOf(Class("Maker"), Class("Everything")));
    }

    /// <summary>The complement-position module: a complement-defined class standing on the subclass side of an axiom whose superclass carries a value pin — a disjunctive membership obligation this rung has no repair move for.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ComplementObligationPositionModule()
    {
        return GroundModule(
        [
            Equivalent(Class("NotMaker"), Complement(Class("Maker"))),
            SubClassOf(Class("NotMaker"), HasValue("needs", Individual("service"))),
            ClassAssertion(Class("Maker"), Individual("maker0")),
        ]);
    }

    /// <summary>The node-bound module: an enumeration-closed demand whose only admissible candidate is the LAST in told document order, so the walk spends one node per member before it certifies.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NodeBoundModule()
    {
        List<OwlAxiom> members = [];
        List<RdfTerm> terms = [];
        for(int index = 0; index < ColourMembers; index++)
        {
            terms.Add(Individual("c" + index));
        }

        members.Add(Equivalent(Class("Colour"), new OwlObjectOneOf(terms)));
        members.Add(ClassAssertion(Class("Wine"), Individual("chablis")));
        members.Add(SubClassOf(Class("Wine"), Some("hasColour", Class("Colour"))));
        members.Add(Range("hasColour", Class("Pale")));
        members.Add(ClassAssertion(Class("Pale"), Individual("c" + (ColourMembers - 1))));

        return GroundModule([.. members]);
    }

    /// <summary>The unrepairable module: an existential whose filler is the empty class, so no proposal — fresh or told — can ever meet the demand.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule UnrepairableObligationModule()
    {
        return GroundModule(
        [
            ClassAssertion(Class("Wine"), Individual("chablis")),
            SubClassOf(Class("Wine"), Some("hasMaker", Nothing)),
        ]);
    }

    /// <summary>The carrier-window template: the requested number of distinct individuals, the first of them typed into two told-disjoint classes so the module's own told arithmetic clashes wherever the window admits it.</summary>
    /// <param name="carriers">The distinct individuals the module names.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule CarrierWindowModule(int carriers)
    {
        List<OwlAxiom> axioms =
        [
            Disjoint(Class("C"), Class("D")),
            ClassAssertion(Class("C"), Individual("m0")),
            ClassAssertion(Class("D"), Individual("m0")),
            SubClassOf(Class("C"), Max("anchorRole", 1)),
            SubClassOf(Class("C"), All("anchorRole", Class("D"))),
        ];
        for(int index = 1; index < carriers; index++)
        {
            axioms.Add(ClassAssertion(Class("Padding"), Individual("m" + index)));
        }

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The class-window template: the requested number of distinct named classes over one told individual, with the two obligation-position restrictions and the told floor drawn from the same class names so the measured count is exactly the requested one.</summary>
    /// <param name="classes">The distinct named classes the module names.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ClassWindowModule(int classes)
    {
        List<OwlAxiom> axioms =
        [
            SubClassOf(Class("k0"), Max("anchorRole", 1)),
            SubClassOf(Class("k0"), All("anchorRole", Class("k1"))),
        ];
        for(int index = 0; index < classes; index++)
        {
            axioms.Add(ClassAssertion(Class("k" + index), Individual("m0")));
        }

        for(int index = 0; index < GroundFloorTerms; index++)
        {
            axioms.Add(ClassAssertion(Class("k0"), Individual("pad" + index)));
        }

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The role-window template: the requested number of distinct roles, each mentioned by a universal in obligation position, over the told floor and two named classes.</summary>
    /// <param name="roles">The distinct roles the module mentions.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule RoleWindowModule(int roles)
    {
        List<OwlAxiom> axioms = [SubClassOf(Class("Anchor0"), Max("r0", 1))];
        for(int index = 0; index < roles; index++)
        {
            axioms.Add(SubClassOf(Class("Anchor0"), All("r" + index, Class("Anchor1"))));
        }

        for(int index = 0; index < GroundFloorTerms; index++)
        {
            axioms.Add(ClassAssertion(Class("Anchor0"), Individual("pad" + index)));
        }

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The dark-control module: a told population every member of which demands three successors inside its own class, so the engine's saturation runs past its ceiling while the repairing measurement reads the census either way.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule DarkControlModule()
    {
        List<OwlAxiom> axioms =
        [
            SubClassOf(Class("Seed"), Min("hasPart", 3)),
            SubClassOf(Class("Seed"), All("hasPart", Class("Seed"))),
        ];
        for(int index = 0; index < GroundFloorTerms; index++)
        {
            axioms.Add(ClassAssertion(Class("Seed"), Individual("pad" + index)));
        }

        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The over-pruned module: an enumeration-closed demand whose told range admits every candidate only ONCE the candidate is committed, so the proposal-side filter cuts every branch at the intermediate node.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule OverPrunedModule()
    {
        return GroundModule(
        [
            Equivalent(Class("Colour"), OneOf("red", "white")),
            ClassAssertion(Class("Wine"), Individual("chablis")),
            SubClassOf(Class("Wine"), Some("hasColour", Class("Colour"))),
            Range("hasColour", Class("Pale")),
        ]);
    }

    /// <summary>The vacuous-firing module: a class equated with a universal whose extension the generator rule fills at every successor-free carrier, and a told member of that class the equivalence's superset direction then needs.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule VacuousFiringModule()
    {
        return Module(
            Equivalent(Class("Vacuous"), All("hasMaker", Class("Maker"))),
            ClassAssertion(Class("Vacuous"), Individual("chablis")),
            ClassAssertion(Class("Maker"), Individual("maker0")));
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

    /// <summary>The <c>owl:Thing</c> reference — the universal class the construction pins to the whole CURRENT domain.</summary>
    private static OwlClassReference Thing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing")));

    /// <summary>The <c>owl:Nothing</c> reference — the empty class the construction pins to the empty set.</summary>
    private static OwlClassReference Nothing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Nothing")));

    /// <summary>The plain datatype IRI node the data-side rows read their told literals against.</summary>
    /// <returns>The datatype node.</returns>
    private static NamedNode Datatype()
    {
        return new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#integer"));
    }

    /// <summary>A told literal carrying the plain datatype IRI.</summary>
    /// <param name="value">The lexical value.</param>
    /// <returns>The literal.</returns>
    private static Literal TypedLiteral(string value)
    {
        return new Literal(Utf8Strings.From(value), Datatype());
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

    /// <summary>An anonymous individual — the carrier whose identity is its label rather than an IRI.</summary>
    /// <param name="label">The blank node's label.</param>
    /// <returns>The blank node.</returns>
    private static BlankNode Anonymous(string label)
    {
        return new BlankNode(Utf8Strings.From(label));
    }

    /// <summary>An enumeration of named individuals in the example namespace, in told document order.</summary>
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

    /// <summary>A complement of a class expression.</summary>
    /// <param name="operand">The complemented operand.</param>
    /// <returns>The complement.</returns>
    private static OwlObjectComplementOf Complement(OwlClassExpression operand)
    {
        return new OwlObjectComplementOf(operand);
    }

    /// <summary>An intersection of class expressions.</summary>
    /// <param name="operands">The conjuncts, in told document order.</param>
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

    /// <summary>A universal restriction over a named forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectAllValuesFrom All(string property, OwlClassExpression filler)
    {
        return new OwlObjectAllValuesFrom(Property(property), filler);
    }

    /// <summary>A value pin over a named forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="individual">The pinned value individual.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasValue HasValue(string property, RdfTerm individual)
    {
        return new OwlObjectHasValue(Property(property), individual);
    }

    /// <summary>A local-reflexivity restriction — the shape outside the evaluable grammar the monotone clash row reads.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasSelf HasSelf(string property)
    {
        return new OwlObjectHasSelf(Property(property));
    }

    /// <summary>An unqualified minimum-cardinality restriction over a named forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Min(string property, int cardinality)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Min, cardinality, Property(property), Filler: null);
    }

    /// <summary>An unqualified maximum-cardinality restriction over a named forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Max(string property, int cardinality)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), Filler: null);
    }

    /// <summary>An unqualified exact-cardinality restriction over a named forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Exact(string property, int cardinality)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Exact, cardinality, Property(property), Filler: null);
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

    /// <summary>A told object-property assertion between two individual terms.</summary>
    /// <param name="role">The role's local name.</param>
    /// <param name="source">The source individual.</param>
    /// <param name="target">The target individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom Edge(string role, RdfTerm source, RdfTerm target)
    {
        return new OwlObjectPropertyAssertionAxiom(source, new NamedNode(Utf8Strings.From(Example + role)), target) { Origin = Origin("edge") };
    }

    /// <summary>A told negative object-property assertion — the denial the certify admission rejects and the monotone clash face consumes.</summary>
    /// <param name="role">The role's local name.</param>
    /// <param name="source">The source individual.</param>
    /// <param name="target">The target individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlNegativeObjectPropertyAssertionAxiom DeniedEdge(string role, RdfTerm source, RdfTerm target)
    {
        return new OwlNegativeObjectPropertyAssertionAxiom(source, Property(role), target) { Origin = Origin("denied") };
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

    /// <summary>A told sameness axiom over two named individuals — the union-find input the quotient consumes.</summary>
    /// <param name="first">The first individual's local name.</param>
    /// <param name="second">The second individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSameIndividualAxiom Same(string first, string second)
    {
        return new OwlSameIndividualAxiom(Individual(first), Individual(second)) { Origin = Origin("same") };
    }

    /// <summary>A functionality characteristic over a named role.</summary>
    /// <param name="role">The role's local name.</param>
    /// <returns>The characteristic axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Functional(string role)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, Property(role)) { Origin = Origin("functional") };
    }

    /// <summary>An inverse-functionality characteristic over a named role — the collision kind a repairing face rejects, since an invented edge could manufacture the merge it forces.</summary>
    /// <param name="role">The role's local name.</param>
    /// <returns>The characteristic axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom InverseFunctional(string role)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.InverseFunctional, Property(role)) { Origin = Origin("inversefunctional") };
    }

    /// <summary>A symmetry characteristic over a named role.</summary>
    /// <param name="role">The role's local name.</param>
    /// <returns>The characteristic axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Symmetric(string role)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Symmetric, Property(role)) { Origin = Origin("symmetric") };
    }

    /// <summary>A transitivity characteristic over a named role.</summary>
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

    /// <summary>A told plain sub-property inclusion.</summary>
    /// <param name="sub">The subproperty's local name.</param>
    /// <param name="super">The superproperty's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubObjectPropertyOfAxiom SubProperty(string sub, string super)
    {
        return new OwlSubObjectPropertyOfAxiom(Property(sub), Property(super)) { Origin = Origin("subproperty") };
    }

    /// <summary>A told property chain — the collision kind a repairing face rejects.</summary>
    /// <param name="first">The first chain link's local name.</param>
    /// <param name="second">The second chain link's local name.</param>
    /// <param name="super">The superproperty's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlPropertyChainAxiom Chain(string first, string second, string super)
    {
        return new OwlPropertyChainAxiom([Property(first), Property(second)], Property(super)) { Origin = Origin("chain") };
    }

    /// <summary>A told key axiom — the collision kind a repairing face rejects.</summary>
    /// <param name="keyed">The keyed class's local name.</param>
    /// <param name="role">The key component role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlHasKeyAxiom Key(string keyed, string role)
    {
        return new OwlHasKeyAxiom(Class(keyed), [Property(role)], []) { Origin = Origin("key") };
    }

    /// <summary>A told object-property range axiom — the declared range the demand set intersects.</summary>
    /// <param name="role">The role's local name.</param>
    /// <param name="range">The range class expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyRangeAxiom Range(string role, OwlClassExpression range)
    {
        return new OwlObjectPropertyRangeAxiom(Property(role), range) { Origin = Origin("range") };
    }

    /// <summary>A told data-property assertion — one pair of the whole extension the told-pairs reading gives that property.</summary>
    /// <param name="source">The subject individual.</param>
    /// <param name="property">The data property's local name.</param>
    /// <param name="value">The told literal.</param>
    /// <returns>The axiom.</returns>
    private static OwlDataPropertyAssertionAxiom DataAssertion(RdfTerm source, string property, Literal value)
    {
        return new OwlDataPropertyAssertionAxiom(source, new NamedNode(Utf8Strings.From(Example + property)), value) { Origin = Origin("datapair") };
    }

    /// <summary>A told data-property domain axiom.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="domain">The domain class expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlDataPropertyDomainAxiom DataDomain(string property, OwlClassExpression domain)
    {
        return new OwlDataPropertyDomainAxiom(new NamedNode(Utf8Strings.From(Example + property)), domain) { Origin = Origin("datadomain") };
    }

    /// <summary>A told data-property range axiom over the supplied data range.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="range">The told range.</param>
    /// <returns>The axiom.</returns>
    private static OwlDataPropertyRangeAxiom DataRangeAxiom(string property, OwlDataRange range)
    {
        return new OwlDataPropertyRangeAxiom(new NamedNode(Utf8Strings.From(Example + property)), range) { Origin = Origin("datarange") };
    }
}
