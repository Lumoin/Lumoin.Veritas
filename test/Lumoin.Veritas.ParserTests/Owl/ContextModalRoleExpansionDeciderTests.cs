using System;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The bounded skolem-expansion modal habitat decider's battery: the measured
/// instance's module through the builders with all seven statistics fields
/// riding the decision, the six rule rows carrying the seed, the two unfolding
/// halves, the spawn dedupe, the eager inverse mirroring, the told-transitive
/// push and the two clash forms, the sixteen attack rows one per named guard —
/// the disjunction refusal, the equivalence direction, the alien conjunct, the
/// nominal filler, the non-simple role, the transitive over-firing, the absent
/// edge, the spawned-successor count, the definition cycle, the unresolved
/// import, the property identity, the qualified bound, the equality axioms,
/// the told-edge frontier, the transitive-inverse limit and the omitted choose
/// rule — the five window rows with their bound discriminants, the near-miss
/// bound against the widened walk, the explicit dark control, and the habitat
/// ordering. Every completeness limit is asserted as a SILENCE carrying its
/// measurement, never as a verdict, and every guard row carries the
/// discrimination control whose correct reading DOES find the clash.
/// </summary>
[TestClass]
internal sealed class ContextModalRoleExpansionDeciderTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the battery's classes, roles, properties, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/modalexpansioncsp#";

    /// <summary>The second namespace the local-name rows draw their same-local-name twins from — the namespace split the measured instance itself carries.</summary>
    private const string Alternate = "http://example.org/modalexpansionalt#";

    /// <summary>The <c>owl:Thing</c> IRI — the unrestricted filler the measured instance's existentials carry.</summary>
    private const string OwlThing = "http://www.w3.org/2002/07/owl#Thing";

    /// <summary>The <c>owl:Nothing</c> IRI — the empty class whose membership is a clash.</summary>
    private const string OwlNothing = "http://www.w3.org/2002/07/owl#Nothing";

    /// <summary>A datatype IRI that qualifies a data cardinality restriction, taking it outside the admission grammar.</summary>
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    /// <summary>The modal-expansion clash face lit — the selection the deciding rows drive. The face has no certify counterpart.</summary>
    private const EnumerationDeciderFaces ModalFaces = EnumerationDeciderFaces.ModalExpansionClash;

    /// <summary>Every decider face the recognizer's registry lights, read from the production fold — the selection the jurisdiction and ordering rows drive against the explicit dark control.</summary>
    private static EnumerationDeciderFaces AllFaces { get; } = ContextHabitatRecognizer.EveryFaceLit;

    /// <summary>Every face lit EXCEPT the modal-expansion one — the selection the dark control compares the explicit dark run against.</summary>
    private static EnumerationDeciderFaces AllFacesButModal { get; } = AllFaces & ~EnumerationDeciderFaces.ModalExpansionClash;

    /// <summary>The node-local numeric clash reason's leading identifier, the property IRI following it in parentheses.</summary>
    private const string NumericBoundReason = "ModalExpansionNodeLocalNumericBound(";

    /// <summary>The bounded budget the silence rows drive: enough for the engine to fire rules on a modal module, far below what its saturation would need.</summary>
    private static ReasoningBudget ProbeBudget { get; } = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 4096);

    /// <summary>The ceiling the dark control drives: one inference attempt, so an admitted module the face is dark on exhausts the engine budget and the census rides an abstention record rather than a decision.</summary>
    private static ReasoningBudget DarkBudget { get; } = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1);

    /// <summary>
    /// The habitat's measured instance told through the builders: the sixteen
    /// logical axioms of the dynamic-blocking module beside the inline data
    /// property declaration its two kind-agnostic cardinality restrictions stand
    /// on. The clash runs three hops down through spawned successors and three
    /// hops back up through told inverse roles into the anonymous root, where a
    /// minimum of one meets a maximum of zero on one data property, and the face
    /// decides the premise inconsistent pre-engine. All SEVEN statistics fields
    /// ride the decision record by name against the specified discipline
    /// figures: an edge count of twelve or an application count of forty-six
    /// would mean a transitively closed edge relation shipped, which is the
    /// mechanism the transitive-universal rule replaces.
    /// </summary>
    [TestMethod]
    public void Me1ModalExpansionClashDecidesTheCorpusPremiseInconsistent()
    {
        ReasoningModule module = CorpusShapedModule();
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, ModalFaces, ReasoningConfiguration.Default.Budget, TestContext.CancellationToken);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "Me1 CorpusPremise: the clash face decides the measured instance at the production ceiling.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "Me1 CorpusPremise: the anonymous root carries a minimum of one above a maximum of zero, so no model exists.");
        Assert.AreEqual(0L, totals.InferenceAttempts, "Me1 CorpusPremise: a pre-engine decision spends zero inference attempts.");
        Assert.AreEqual(0, totals.ContextsCreated, "Me1 CorpusPremise: no engine was constructed — the seat is upstream of every engine axis.");
        Assert.AreEqual(EnumerationHabitatClass.ModalRoleExpansion, totals.EnumerationHabitat, "Me1 CorpusPremise: the module carries the Shape M census label.");
        Assert.AreEqual(5, totals.ModalExpansionNodesSpawned, "Me1 CorpusPremise: the specified discipline spawns five successors, the two dead-end branches included.");
        Assert.AreEqual(3, totals.ModalExpansionMaxDepthReached, "Me1 CorpusPremise: the clash sits at the level-three fixpoint.");
        Assert.AreEqual(11, totals.ModalExpansionPeakLabelSize, "Me1 CorpusPremise: the peak counted label is eleven, tied at the two nodes the transitive push loads.");
        Assert.AreEqual(10, totals.ModalExpansionEdgesMaterialised, "Me1 CorpusPremise: ten directed edges — five spawn-forward and five materialised inverse, and never one derived from transitivity.");
        Assert.AreEqual(50, totals.ModalExpansionRuleApplications, "Me1 CorpusPremise: fifty rule firings under the counting conventions, a spawn charging one for its edge fact and its membership fact together.");
        Assert.AreEqual(1, totals.ModalExpansionDeciderClashes, "Me1 CorpusPremise: the clash face's counter reads the decision.");
        Assert.AreEqual(0, totals.ModalExpansionWindowSilences, "Me1 CorpusPremise: the module sits inside every window ceiling.");
        Assert.AreEqual(NumericBoundReason + Example + "P.1)", ContextModalRoleExpansionDecider.Run(module).ClashReason, "Me1 CorpusPremise: the clash reason names the bounded data property.");
    }

    /// <summary>
    /// A blank-node-rooted class assertion seeds the expansion as a first-class
    /// root: a blank-node individual is an ordinary domain element in every
    /// model, so the same module rooted at a named individual decides
    /// identically, measurement for measurement.
    /// </summary>
    [TestMethod]
    public void Me2AnonymousRootSeedsTheExpansionAsFirstClass()
    {
        ModalExpansionOutcome anonymous = ContextModalRoleExpansionDecider.Run(ChassisModule());
        ModalExpansionOutcome named = ContextModalRoleExpansionDecider.Run(NamedRootChassisModule());

        Assert.IsFalse(anonymous.Consistent, "Me2 AnonymousRoot: the blank-node-rooted assertion seeds the expansion and the clash is reached.");
        Assert.IsFalse(named.Consistent, "Me2 AnonymousRoot: the named-root module reaches the same clash.");
        Assert.AreEqual(anonymous.ClashReason, named.ClashReason, "Me2 AnonymousRoot: both roots name the same bounded property.");
        Assert.AreEqual(anonymous.Window, named.Window, "Me2 AnonymousRoot: the two roots measure identically — the blank case is no fallback.");
        Assert.AreEqual(1, anonymous.Window.NodesSpawned, "Me2 AnonymousRoot: one successor carries the whole upward channel.");
    }

    /// <summary>
    /// A told equivalence unfolds in the NAME-TO-DEFINITION direction only, and
    /// which operand is the name is decided by CONSTRUCT rather than by argument
    /// position. A module whose only route to the clash needs the omitted
    /// definition-to-name half is SILENT; the name-to-definition half decides its
    /// own module; the complex expression written FIRST decides identically to
    /// the same equivalence written the other way round; an equivalence with two
    /// class-IRI operands derives BOTH directions, since neither drops a
    /// conjunct; and an equivalence with no class-IRI operand drops whole.
    /// </summary>
    [TestMethod]
    public void Me3EquivalenceUnfoldsNameToDefinitionOnly()
    {
        ModalExpansionOutcome backward = ContextModalRoleExpansionDecider.Run(DefinitionToNameOnlyModule());

        Assert.IsNull(backward.Consistent, "Me3 EquivalenceDirection: a clash reachable only through the definition-to-name half is never reached.");
        Assert.IsNull(backward.ClashReason, "Me3 EquivalenceDirection: a silent face names no clash reason.");

        ModalExpansionOutcome forward = ContextModalRoleExpansionDecider.Run(ChassisModule());
        ModalExpansionOutcome swapped = ContextModalRoleExpansionDecider.Run(SwappedEquivalenceChassisModule());

        Assert.IsFalse(forward.Consistent, "Me3 EquivalenceDirection: the name-to-definition half decides its own module.");
        Assert.IsFalse(swapped.Consistent, "Me3 EquivalenceDirection: the complex expression written first decides the same module.");
        Assert.AreEqual(forward.Window, swapped.Window, "Me3 EquivalenceDirection: the name side is chosen by construct, so argument order moves no measurement.");

        ModalExpansionOutcome firstNamed = ContextModalRoleExpansionDecider.Run(BothNamedEquivalenceModule(definedFirst: true));
        ModalExpansionOutcome secondNamed = ContextModalRoleExpansionDecider.Run(BothNamedEquivalenceModule(definedFirst: false));

        Assert.IsFalse(firstNamed.Consistent, "Me3 EquivalenceDirection: two class-IRI operands derive the first-to-second direction.");
        Assert.IsFalse(secondNamed.Consistent, "Me3 EquivalenceDirection: two class-IRI operands derive the second-to-first direction as well — neither drops a conjunct.");

        ModalExpansionOutcome unnamed = ContextModalRoleExpansionDecider.Run(NoNamedOperandEquivalenceModule());

        Assert.IsNull(unnamed.Consistent, "Me3 EquivalenceDirection: an equivalence with no class-IRI operand drops whole, so its clash route is never opened.");
    }

    /// <summary>
    /// The intersection handler never consumes a union: the chassis with one
    /// universal filler replaced by a two-member union that WOULD clash under the
    /// conjunctive misreading is silent, and the module-wide disjunction refusal
    /// is visible in the census label as well as in the outcome — the recognizer
    /// declines the module whole, so the shape is not even offered to the face.
    /// </summary>
    [TestMethod]
    public void Me4IntersectionHandlerNeverConsumesAUnion()
    {
        ReasoningModule module = UnionFillerModule();
        ModalExpansionOutcome outcome = ContextModalRoleExpansionDecider.Run(module);

        Assert.IsNull(outcome.Consistent, "Me4 UnionFiller: a union anywhere in the module refuses the face whole.");
        Assert.IsNull(outcome.ClashReason, "Me4 UnionFiller: a silent face names no clash reason.");
        Assert.IsFalse(ContextModalRoleExpansionDecider.Run(ChassisModule()).Consistent, "Me4 UnionFiller: the same shape with the union replaced by its clashing member decides, so the silence is the refusal's doing.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(module, ModalFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreNotEqual(EnumerationHabitatClass.ModalRoleExpansion, totals.EnumerationHabitat, "Me4 UnionFiller: the recognizer's module-wide disjunction clause declines the shape.");
        Assert.AreEqual(0, totals.ModalExpansionDeciderClashes, "Me4 UnionFiller: no clash decision on a disjunctive module.");
    }

    /// <summary>
    /// A whitelisted axiom carrying an alien conjunct derives ONLY whitelisted
    /// consequences: the whole axiom is dropped rather than approximated, under
    /// BOTH the subsumption and the equivalence spelling read
    /// name-to-definition, so no consequence of the alien conjunct's neighbours
    /// reaches a label. The discrimination control removes the alien conjunct and
    /// the same module clashes, so each silence is the drop's doing.
    /// </summary>
    [TestMethod]
    public void Me5WhitelistedAxiomWithAnAlienConjunctDerivesOnlyWhitelistedConsequences()
    {
        Assert.IsNull(ContextModalRoleExpansionDecider.Run(AlienConjunctModule(equivalence: false, alien: true)).Consistent, "Me5 AlienConjunct: a subsumption carrying an alien conjunct derives none of its neighbours.");
        Assert.IsNull(ContextModalRoleExpansionDecider.Run(AlienConjunctModule(equivalence: true, alien: true)).Consistent, "Me5 AlienConjunct: an equivalence read name-to-definition carrying an alien conjunct derives none of its neighbours either.");
        Assert.IsFalse(ContextModalRoleExpansionDecider.Run(AlienConjunctModule(equivalence: false, alien: false)).Consistent, "Me5 AlienConjunct: the subsumption without the alien conjunct decides, so the silence is the whole-axiom drop.");
        Assert.IsFalse(ContextModalRoleExpansionDecider.Run(AlienConjunctModule(equivalence: true, alien: false)).Consistent, "Me5 AlienConjunct: the equivalence without the alien conjunct decides the same way.");
    }

    /// <summary>
    /// A singleton enumeration standing in a filler position silences the module
    /// whole: an enumeration is disjunctive, and a face with no disjunction
    /// handler could only misread one. The discrimination control drops the
    /// enumeration from the same conjunction and the module clashes.
    /// </summary>
    [TestMethod]
    public void Me6SingletonNominalFillerSilencesTheModule()
    {
        Assert.IsNull(ContextModalRoleExpansionDecider.Run(SingletonNominalModule()).Consistent, "Me6 SingletonNominal: an enumeration anywhere in the module refuses the face whole.");
        Assert.IsFalse(ContextModalRoleExpansionDecider.Run(ChassisModule()).Consistent, "Me6 SingletonNominal: the same shape without the enumeration decides, so the silence is the refusal's doing.");
    }

    /// <summary>
    /// A cardinality restriction on a NON-SIMPLE role silences the module whole,
    /// the told inverse of a transitive role included, since transitivity of a
    /// role and of its inverse are the same fact. Four modules carry the claim:
    /// the bounds moved onto the transitive role; the bounds placed on its told
    /// inverse; the DISPOSITION leg, where an independent clean clash beside the
    /// non-simple bound is STILL not reached, proving the module is silenced
    /// whole rather than the offending axiom dropped; and the sub-property leg,
    /// where the outright rejection DROPS the axiom and the module decides
    /// identically to the same module without it.
    /// </summary>
    [TestMethod]
    public void Me7CardinalityOnANonSimpleRoleSilencesIncludingTheToldInverse()
    {
        Assert.IsNull(ContextModalRoleExpansionDecider.Run(ObjectBoundChassisModule("t", transitive: true, inverseOfTransitive: false)).Consistent, "Me7 NonSimpleRole: bounds on the told-transitive role leave the module outside OWL 2 DL, so the face abstains.");
        Assert.IsFalse(ContextModalRoleExpansionDecider.Run(ObjectBoundChassisModule("t", transitive: false, inverseOfTransitive: false)).Consistent, "Me7 NonSimpleRole: the same bounds on a simple role decide, so the silence is the gate's doing.");

        Assert.IsNull(ContextModalRoleExpansionDecider.Run(ObjectBoundChassisModule("invT", transitive: true, inverseOfTransitive: true)).Consistent, "Me7 NonSimpleRole: the non-simple set closes under told inverses, so bounds on the transitive role's inverse abstain too.");
        Assert.IsFalse(ContextModalRoleExpansionDecider.Run(ObjectBoundChassisModule("invT", transitive: false, inverseOfTransitive: true)).Consistent, "Me7 NonSimpleRole: with the transitivity axiom removed the inverse is simple and the same bounds decide.");

        Assert.IsNull(ContextModalRoleExpansionDecider.Run(NonSimpleBesideCleanClashModule(transitive: true)).Consistent, "Me7 NonSimpleRole: an independent clean clash beside the non-simple bound is STILL not reached — the gate silences the module whole rather than dropping one axiom.");
        Assert.IsFalse(ContextModalRoleExpansionDecider.Run(NonSimpleBesideCleanClashModule(transitive: false)).Consistent, "Me7 NonSimpleRole: the same module with the role simple reaches the clean clash.");

        ModalExpansionOutcome withSubProperty = ContextModalRoleExpansionDecider.Run(SubPropertyChassisModule());
        ModalExpansionOutcome withoutSubProperty = ContextModalRoleExpansionDecider.Run(ChassisModule());

        Assert.IsFalse(withSubProperty.Consistent, "Me7 NonSimpleRole: a rejected sub-property axiom is DROPPED and the module continues.");
        Assert.AreEqual(withoutSubProperty.Window, withSubProperty.Window, "Me7 NonSimpleRole: the dropped axiom moves no measurement, so the rejection is a drop and not a silence.");
    }

    /// <summary>
    /// A role that is not told transitive neither propagates a universal to a
    /// non-successor nor has its edge relation closed: the two-hop chain's
    /// deepest node is reached by a fact only a manufactured edge could deliver,
    /// and the module is silent. The discrimination control tells the same role
    /// transitive and the universal PUSH — the single mechanism for that fact —
    /// carries it down one link at a time and the module clashes. A told-
    /// transitive sibling role carrying its own universal fires beside the
    /// non-transitive one and changes nothing.
    /// </summary>
    [TestMethod]
    public void Me8NonTransitiveRoleNeitherPropagatesNorClosesItsEdgeRelation()
    {
        Assert.IsNull(ContextModalRoleExpansionDecider.Run(ChainClosureModule(transitive: false, siblingTransitive: false)).Consistent, "Me8 EdgeClosure: a universal over a non-transitive role reaches its successors only, and the edge relation is never closed.");
        Assert.IsFalse(ContextModalRoleExpansionDecider.Run(ChainClosureModule(transitive: true, siblingTransitive: false)).Consistent, "Me8 EdgeClosure: told transitive, the universal pushes itself down the chain and the deepest node clashes.");
        Assert.IsNull(ContextModalRoleExpansionDecider.Run(ChainClosureModule(transitive: false, siblingTransitive: true)).Consistent, "Me8 EdgeClosure: a told-transitive SIBLING role firing its own universal leaks nothing onto the non-transitive one.");
    }

    /// <summary>
    /// A universal over a role with no materialised successors derives nothing:
    /// the asymmetric fixture carries two told inverse pairs and puts the
    /// universal on the one whose partner the spawned node has no edge for, so
    /// reading one inverse role as another would produce a clash and the correct
    /// reading does not. The namespace leg puts an existential and a universal on
    /// two roles sharing a LOCAL NAME across namespaces with an unsatisfiable
    /// filler: a role-edge lookup fusing them by local name is a wrong
    /// inconsistent that the numeric clash's own property guard does not reach.
    /// </summary>
    [TestMethod]
    public void Me9UniversalOverAnAbsentEdgeDerivesNothing()
    {
        Assert.IsNull(ContextModalRoleExpansionDecider.Run(WrongInverseModule("invW")).Consistent, "Me9 AbsentEdge: the universal's role has no materialised successor at the spawned node, so nothing is delivered.");

        ModalExpansionOutcome corrected = ContextModalRoleExpansionDecider.Run(WrongInverseModule("invS"));

        Assert.IsFalse(corrected.Consistent, "Me9 AbsentEdge: the correct inverse role DOES find the clash, so the row proves a discrimination and not a blanket silence.");
        Assert.StartsWith(NumericBoundReason, corrected.ClashReason!, "Me9 AbsentEdge: the discrimination control's clash is the node-local numeric one.");

        Assert.IsNull(ContextModalRoleExpansionDecider.Run(NamespaceRoleModule(Alternate)).Consistent, "Me9 AbsentEdge: two roles sharing a local name across namespaces are different roles, so the universal finds no edge.");

        ModalExpansionOutcome fused = ContextModalRoleExpansionDecider.Run(NamespaceRoleModule(Example));

        Assert.IsFalse(fused.Consistent, "Me9 AbsentEdge: the same universal over the SAME full IRI does deliver the empty class and the module clashes.");
        Assert.AreEqual(ModalExpansionClashReasons.AssertedNothingMembership, fused.ClashReason, "Me9 AbsentEdge: the namespace control's clash is the asserted-empty-class one.");
    }

    /// <summary>
    /// Spawned successors are never counted against a maximum: a node carrying a
    /// maximum of one beside two distinct existentials over that same role
    /// allocates two fresh successors and stays silent, because counting them
    /// would need a distinctness assumption the face never makes. The direct leg
    /// reads the spawn count off the window — one fresh node per unsatisfied
    /// existential, never a reused witness.
    /// </summary>
    [TestMethod]
    public void Me10SpawnedSuccessorsAreNeverCountedAgainstAMaximum()
    {
        ModalExpansionOutcome outcome = ContextModalRoleExpansionDecider.Run(MaxAgainstSpawnsModule());

        Assert.IsNull(outcome.Consistent, "Me10 SpawnCounting: a maximum is never compared against a count of materialised successors.");
        Assert.IsNull(outcome.ClashReason, "Me10 SpawnCounting: a silent face names no clash reason.");
        Assert.AreEqual(2, outcome.Window.NodesSpawned, "Me10 SpawnCounting: the arena grows by exactly one per unsatisfied existential.");
    }

    /// <summary>
    /// The existential spawn dedupes per node and per STRUCTURAL expression, and
    /// the level batch freezes its semantic skip check at the batch boundary.
    /// Five legs: the same existential reached twice spawns once; two distinct
    /// existentials on one role spawn twice; two syntactically distinct
    /// expressions with identical content spawn once, so the count is a property
    /// of the module and not of its serialisation; an unrestricted existential
    /// beside a restricted one on the same role spawns twice in either written
    /// order, because the skip check reads the frozen snapshot; and an
    /// existential arriving at an already-processed ancestor through an upward
    /// inverse fact spawns in the NEXT level's batch, the depth accounting
    /// following the batch rather than the ancestor.
    /// </summary>
    [TestMethod]
    public void Me11ExistentialSpawnDedupesPerNodeAndExpression()
    {
        Assert.AreEqual(1, ContextModalRoleExpansionDecider.Run(SpawnDedupeModule(secondFiller: "Alpha")).Window.NodesSpawned, "Me11 SpawnDedupe: the same existential reached by two routes spawns once.");
        Assert.AreEqual(2, ContextModalRoleExpansionDecider.Run(SpawnDedupeModule(secondFiller: "Beta")).Window.NodesSpawned, "Me11 SpawnDedupe: two distinct existentials on one role spawn twice.");
        Assert.AreEqual(1, ContextModalRoleExpansionDecider.Run(StructuralKeyModule()).Window.NodesSpawned, "Me11 SpawnDedupe: two structurally identical expressions are ONE key, so the spawn count is a property of the module.");
        Assert.AreEqual(2, ContextModalRoleExpansionDecider.Run(BatchFreezeModule(unrestrictedFirst: true)).Window.NodesSpawned, "Me11 SpawnDedupe: the frozen skip check spawns both existentials with the unrestricted one written first.");
        Assert.AreEqual(2, ContextModalRoleExpansionDecider.Run(BatchFreezeModule(unrestrictedFirst: false)).Window.NodesSpawned, "Me11 SpawnDedupe: and both with it written second — the count is independent of the processing order.");

        ModalExpansionOutcome ancestor = ContextModalRoleExpansionDecider.Run(AncestorSpawnModule());

        Assert.AreEqual(2, ancestor.Window.NodesSpawned, "Me11 SpawnDedupe: an existential arriving at a processed ancestor is spawned rather than dropped.");
        Assert.AreEqual(2, ancestor.Window.MaxDepthReached, "Me11 SpawnDedupe: the late arrival spawns in the NEXT level's batch, so the depth follows the batch and no closed level is reopened.");
    }

    /// <summary>
    /// A told inverse pair mirrors every edge EAGERLY at creation time, in both
    /// argument orders, so no later rule ever observes an unmirrored edge: the
    /// chassis materialises one spawn-forward edge and its inverse, and writing
    /// the pair the other way round moves no measurement.
    /// </summary>
    [TestMethod]
    public void Me12InverseMaterialisationIsEagerAndSymmetric()
    {
        ModalExpansionOutcome forward = ContextModalRoleExpansionDecider.Run(ChassisModule());
        ModalExpansionOutcome reversed = ContextModalRoleExpansionDecider.Run(ReversedInversePairChassisModule());

        Assert.IsFalse(forward.Consistent, "Me12 InverseMirroring: the mirrored edge is what carries the fact back up to the root.");
        Assert.IsFalse(reversed.Consistent, "Me12 InverseMirroring: the pair written the other way round mirrors the same edge.");
        Assert.AreEqual(forward.Window, reversed.Window, "Me12 InverseMirroring: argument order moves no measurement.");
        Assert.AreEqual(2, forward.Window.EdgesMaterialised, "Me12 InverseMirroring: one spawn-forward edge and its materialised inverse, counted separately.");
    }

    /// <summary>
    /// The transitive-universal push fires only for the property transitivity is
    /// told for: the two-link chain over a told-transitive role carries the
    /// universal itself down to the second link and the module clashes, while the
    /// identical chain over a simple sibling role delivers to the first link
    /// only and is silent.
    /// </summary>
    [TestMethod]
    public void Me13TransitiveUniversalPushesOnlyForTheToldTransitiveProperty()
    {
        Assert.IsFalse(ContextModalRoleExpansionDecider.Run(TransitivePushModule(chainRole: "t", chainInverse: "invT")).Consistent, "Me13 TransitivePush: the told-transitive role carries the universal to the second link.");
        Assert.IsNull(ContextModalRoleExpansionDecider.Run(TransitivePushModule(chainRole: "g", chainInverse: "invG")).Consistent, "Me13 TransitivePush: the simple sibling role delivers the filler to its successors only.");
    }

    /// <summary>
    /// An exact cardinality is read as its unqualified minimum and maximum halves
    /// together: an exact bound of zero beside a minimum of one on the same
    /// property IRI and kind clashes, while the qualified counterpart drops its
    /// whole axiom and never reaches the numeric clash.
    /// </summary>
    [TestMethod]
    public void Me14ExactCardinalityReadsAsUnqualifiedMinAndMax()
    {
        ModalExpansionOutcome unqualified = ContextModalRoleExpansionDecider.Run(ExactCardinalityModule(qualified: false));

        Assert.IsFalse(unqualified.Consistent, "Me14 ExactCardinality: an exact zero carries a maximum of zero, which a minimum of one contradicts.");
        Assert.StartsWith(NumericBoundReason, unqualified.ClashReason!, "Me14 ExactCardinality: the clash is the node-local numeric one.");
        Assert.IsNull(ContextModalRoleExpansionDecider.Run(ExactCardinalityModule(qualified: true)).Consistent, "Me14 ExactCardinality: a qualified exact restriction is outside the grammar and its axiom drops whole.");
    }

    /// <summary>
    /// An asserted empty-class membership clashes wherever it lands: once with
    /// <c>owl:Nothing</c> reaching a spawned node as a told superclass, and once
    /// with it reaching one as a universal's filler. Both carry the asserted-
    /// empty-class reason rather than the numeric one.
    /// </summary>
    [TestMethod]
    public void Me15AssertedNothingMembershipClashes()
    {
        ModalExpansionOutcome superclass = ContextModalRoleExpansionDecider.Run(NothingSuperclassModule());
        ModalExpansionOutcome filler = ContextModalRoleExpansionDecider.Run(NothingFillerModule());

        Assert.IsFalse(superclass.Consistent, "Me15 AssertedNothing: the empty class reached through a told superclass refutes the module.");
        Assert.AreEqual(ModalExpansionClashReasons.AssertedNothingMembership, superclass.ClashReason, "Me15 AssertedNothing: the superclass route names the asserted-empty-class reason.");
        Assert.IsFalse(filler.Consistent, "Me15 AssertedNothing: the empty class delivered as a universal's filler refutes the module too.");
        Assert.AreEqual(ModalExpansionClashReasons.AssertedNothingMembership, filler.ClashReason, "Me15 AssertedNothing: the filler route names the same reason.");
    }

    /// <summary>
    /// Property identity is the FULL IRI paired with the property kind. Five
    /// legs: two identical local names in different namespaces carrying a minimum
    /// of one and a maximum of zero onto one node stay silent; the kind-punned
    /// module, one IRI in an object restriction beside a data one, silences the
    /// module whole; the control that puts both bounds on ONE full IRI clashes;
    /// the kind-UNDETERMINED leg — the measured instance's own legacy spelling on
    /// an undeclared IRI — is silent because the kind is fixed by neither
    /// constructor nor declaration; and the same module with the declaration
    /// ADDED clashes, which is what makes the rule a determination rather than a
    /// default.
    /// </summary>
    [TestMethod]
    public void Me16PropertyIdentityIsFullIriPairedWithKind()
    {
        Assert.IsNull(ContextModalRoleExpansionDecider.Run(LocalNameSplitModule(sameNamespace: false)).Consistent, "Me16 PropertyIdentity: a minimum and a maximum on two differently-namespaced properties are two properties.");
        Assert.IsNull(ContextModalRoleExpansionDecider.Run(KindPunModule()).Consistent, "Me16 PropertyIdentity: an IRI occurring in both an object and a data restriction has an ambiguous kind, so the module is silenced whole.");

        ModalExpansionOutcome control = ContextModalRoleExpansionDecider.Run(LocalNameSplitModule(sameNamespace: true));

        Assert.IsFalse(control.Consistent, "Me16 PropertyIdentity: both bounds on ONE full IRI do clash, so the row proves a discrimination.");
        Assert.AreEqual(NumericBoundReason + Example + "d)", control.ClashReason, "Me16 PropertyIdentity: the clash reason names the full IRI of the bounded property.");

        Assert.IsNull(ContextModalRoleExpansionDecider.Run(KindUndeterminedModule(declared: false)).Consistent, "Me16 PropertyIdentity: a legacy kind-agnostic restriction on an undeclared IRI leaves the kind undetermined, and guessing it is guessing half the clash's key.");
        Assert.IsFalse(ContextModalRoleExpansionDecider.Run(KindUndeterminedModule(declared: true)).Consistent, "Me16 PropertyIdentity: the declaration determines the kind and the same module clashes — the measured instance clashes because its declaration is present.");
    }

    /// <summary>
    /// A qualified cardinality never feeds the numeric clash: a minimum of one
    /// over a role qualified by one filler beside a maximum of zero over the same
    /// role qualified by another is satisfiable, and an unqualified reading of it
    /// would be a wrong inconsistent. Three legs: the complement-free qualified
    /// module is silent with every window counter zero and the census label still
    /// reads Shape M, which is the recognizer's stated looseness as a visible
    /// state; the same module with the FILLERS DELETED clashes; and the no-spawn
    /// variant with a minimum of two against a maximum of one is silent too.
    /// </summary>
    [TestMethod]
    public void Me17QualifiedCardinalityNeverFeedsTheNumericClash()
    {
        ReasoningModule qualified = QualifiedBoundModule(minimum: 1, maximum: 0, fillersDeleted: false);
        ModalExpansionOutcome outcome = ContextModalRoleExpansionDecider.Run(qualified);

        Assert.IsNull(outcome.Consistent, "Me17 QualifiedBound: a qualified restriction drops its whole axiom and may never feed the numeric clash.");
        Assert.AreEqual(ModalExpansionWindow.Empty, outcome.Window, "Me17 QualifiedBound: with no admitted bound there is no clash template, so nothing is expanded and every counter is zero.");

        ContextSaturationStatistics totals = ContextSaturationModuleReasoner.DecideModule(qualified, ModalFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreEqual(EnumerationHabitatClass.ModalRoleExpansion, totals.EnumerationHabitat, "Me17 QualifiedBound: the recognizer's clash-template clause does not check qualification, so a recognized-but-silent module is an expected, visible census state.");
        Assert.AreEqual(0, totals.ModalExpansionDeciderClashes, "Me17 QualifiedBound: no clash decision behind the recognized label.");

        Assert.IsFalse(ContextModalRoleExpansionDecider.Run(QualifiedBoundModule(minimum: 1, maximum: 0, fillersDeleted: true)).Consistent, "Me17 QualifiedBound: the same module with the fillers deleted is an unqualified contradiction and clashes.");
        Assert.IsNull(ContextModalRoleExpansionDecider.Run(QualifiedBoundModule(minimum: 2, maximum: 1, fillersDeleted: false)).Consistent, "Me17 QualifiedBound: the no-spawn variant is silent on the same ground.");
    }

    /// <summary>
    /// A definition cycle terminates by LABEL-SET DEDUPE rather than by an
    /// unfolding counter: two classes defined as each other unfold once each and
    /// the expansion reaches its fixpoint silently, with no bound tripped at all.
    /// </summary>
    [TestMethod]
    public void Me18DefinitionCycleTerminatesSilentlyWithinBudget()
    {
        ModalExpansionOutcome outcome = ContextModalRoleExpansionDecider.Run(DefinitionCycleModule());

        Assert.IsNull(outcome.Consistent, "Me18 DefinitionCycle: the cyclic module carries no reachable clash.");
        Assert.AreEqual(0, outcome.Window.WindowSilences, "Me18 DefinitionCycle: termination is structural — a fact derived at most once per node — so no bound was tripped.");
        Assert.IsGreaterThan(0, outcome.Window.RuleApplications, "Me18 DefinitionCycle: the cycle was actually unfolded rather than refused.");
    }

    /// <summary>
    /// An unresolved import is a non-logical passthrough and the clash decision is
    /// byte-identical with and without it: ignoring an axiom is sound in the
    /// clash direction over a monotone logic. The anti-monotone leg states the
    /// limit of that licence — the jurisdiction gates are properties a LARGER
    /// axiom set can fail — and pins the one gate that cannot default: a property
    /// the local module leaves undeclared is kind-undetermined and silences,
    /// rather than taking a kind an unresolved import might have carried.
    /// </summary>
    [TestMethod]
    public void Me19ImportsBearingModuleDecidesIdentically()
    {
        ModalExpansionOutcome imported = ContextModalRoleExpansionDecider.Run(ImportBearingModule(declared: true));
        ModalExpansionOutcome plain = ContextModalRoleExpansionDecider.Run(ChassisModule());

        Assert.IsFalse(imported.Consistent, "Me19 Imports: the import-bearing module reaches the same clash.");
        Assert.AreEqual(plain.ClashReason, imported.ClashReason, "Me19 Imports: the decision is byte-identical with the import present.");
        Assert.AreEqual(plain.Window, imported.Window, "Me19 Imports: the import moves no measurement.");
        Assert.IsNull(ContextModalRoleExpansionDecider.Run(ImportBearingModule(declared: false)).Consistent, "Me19 Imports: the same module with the property left undeclared silences on the kind rule rather than defaulting to a kind an import might carry.");
    }

    /// <summary>
    /// Told sameness and told distinctness are ignored soundly: the module
    /// carrying both decides identically to the same module without them, and
    /// neither axiom changes how any admitted axiom is READ — which is the reason
    /// the licence is the monotonicity lemma and not an accident.
    /// </summary>
    [TestMethod]
    public void Me20EqualityAxiomsAreIgnoredSoundly()
    {
        ModalExpansionOutcome withEquality = ContextModalRoleExpansionDecider.Run(EqualityBearingModule());
        ModalExpansionOutcome plain = ContextModalRoleExpansionDecider.Run(ChassisModule());

        Assert.IsFalse(withEquality.Consistent, "Me20 EqualityAxioms: the equality-bearing module reaches the same clash.");
        Assert.AreEqual(plain.ClashReason, withEquality.ClashReason, "Me20 EqualityAxioms: the decision is unchanged.");
        Assert.AreEqual(plain.Window, withEquality.Window, "Me20 EqualityAxioms: the ignored axioms move no measurement.");
    }

    /// <summary>
    /// Told edges and spawned edges share ONE depth accounting: two told
    /// individuals joined by a told edge are level-zero nodes whose edge is
    /// mirrored under the told inverse pair exactly as a spawned one is, each
    /// spawns its own successor at level one, and the fact that closes the clash
    /// travels up through both a spawned inverse and a told one. No rule assumes
    /// a tree, and no unique-name assumption is made over the told individuals.
    /// </summary>
    [TestMethod]
    public void Me21ToldEdgesAndSpawnedEdgesShareOneDepthAccounting()
    {
        ModalExpansionOutcome outcome = ContextModalRoleExpansionDecider.Run(ToldEdgeModule());

        Assert.IsFalse(outcome.Consistent, "Me21 ToldFrontier: the clash is reached across a told edge and a spawned one alike.");
        Assert.AreEqual(2, outcome.Window.NodesSpawned, "Me21 ToldFrontier: each told individual spawns its own successor.");
        Assert.AreEqual(1, outcome.Window.MaxDepthReached, "Me21 ToldFrontier: depth is measured from the told frontier, which sits at level zero.");
        Assert.AreEqual(6, outcome.Window.EdgesMaterialised, "Me21 ToldFrontier: the told edge and both spawn-forward edges are each mirrored under the told inverse pairs.");
    }

    /// <summary>
    /// The transitive-inverse limit is a named COMPLETENESS limit and stays a
    /// silence: a module whose clash needs the universal to travel UPWARD along
    /// the inverse of a told-transitive role is not decided, because the push
    /// fires only for the property transitivity is told for. Missing it loses
    /// clashes and never creates one. The limit is bound to the non-simple-role
    /// gate, whose set closes under told inverses BECAUSE transitivity of a role
    /// and of its inverse are the same fact, and the bound-bearing variant is
    /// silenced by that gate.
    /// </summary>
    [TestMethod]
    public void Me22TransitiveInverseCompletenessLimitStaysSilent()
    {
        ModalExpansionOutcome outcome = ContextModalRoleExpansionDecider.Run(TransitiveInverseLimitModule(objectBounds: false));

        Assert.IsNull(outcome.Consistent, "Me22 TransitiveInverseLimit: upward transitive propagation along a told inverse is a named completeness limit, so the module is silent rather than decided.");
        Assert.IsNull(outcome.ClashReason, "Me22 TransitiveInverseLimit: a completeness limit never leaks into a verdict.");
        Assert.AreEqual(0, outcome.Window.WindowSilences, "Me22 TransitiveInverseLimit: the silence is the missing rule's, not a bound trip's.");
        Assert.IsNull(ContextModalRoleExpansionDecider.Run(TransitiveInverseLimitModule(objectBounds: true)).Consistent, "Me22 TransitiveInverseLimit: the bound-bearing variant is silenced by the non-simple-role gate, whose set closes under told inverses for exactly this reason — the two are one pair.");
    }

    /// <summary>
    /// The omitted choose rule stays a silence on a COMPLEMENT-FREE module: a
    /// maximum of one over a role with two existentials whose fillers carry
    /// contradictory bounds would clash only if a nondeterministic rule merged the
    /// two successors. Omitting the rule is sound; guessing a branch is not. The
    /// module carries no complement, so the silence is charged to the omitted
    /// exclusion and not to the disjunction gate firing first.
    /// </summary>
    [TestMethod]
    public void Me23OmittedChooseRuleStaysSilentOnAComplementFreeModule()
    {
        ModalExpansionOutcome outcome = ContextModalRoleExpansionDecider.Run(ChooseRuleModule());

        Assert.IsNull(outcome.Consistent, "Me23 ChooseRule: the merge and choose rules are named admission-grammar exclusions, so a clash needing one is never found.");
        Assert.AreEqual(2, outcome.Window.NodesSpawned, "Me23 ChooseRule: both successors were allocated fresh, so the module was expanded rather than refused.");
        Assert.AreEqual(0, outcome.Window.WindowSilences, "Me23 ChooseRule: the silence is the omitted rule's, not a bound trip's.");
    }

    /// <summary>
    /// The node arena bounds told level-zero nodes and spawned successors
    /// TOGETHER. The measured instance records five spawns well inside it; a
    /// branching module demanding one arena node past the ceiling is silent with
    /// the arena standing exactly AT the ceiling and the other four quantities
    /// strictly below theirs; and the told-arena leg — a module whose told
    /// individuals alone exceed the ceiling, with no existential anywhere — is
    /// silent with the same counter charged and nothing spawned at all.
    /// </summary>
    [TestMethod]
    public void Me24NodeBoundWindowRecordsAndSilences()
    {
        Assert.AreEqual(5, ContextModalRoleExpansionDecider.Run(CorpusShapedModule()).Window.NodesSpawned, "Me24 NodeBound: the measured instance's five spawns ride the deciding window.");
        Assert.IsGreaterThan(5, ContextModalRoleExpansionDecider.ModalExpansionNodeBound, "Me24 NodeBound: the measured instance sits well inside the arena.");

        ModalExpansionOutcome overflow = ContextModalRoleExpansionDecider.Run(BranchingLoopModule());

        Assert.IsNull(overflow.Consistent, "Me24 NodeBound: a module past the arena is silent — a bound trip is never a verdict.");
        Assert.AreEqual(1, overflow.Window.WindowSilences, "Me24 NodeBound: the silence is charged to the window counter.");
        Assert.AreEqual(ContextModalRoleExpansionDecider.ModalExpansionNodeBound, overflow.Window.NodesSpawned + 1, "Me24 NodeBound: one told node plus the spawned ones stands exactly at the arena ceiling.");
        AssertOtherBoundsBelowTheirCeilings(overflow.Window, "Me24 NodeBound", node: false, depth: true, label: true, edge: true, step: true);

        ModalExpansionOutcome toldArena = ContextModalRoleExpansionDecider.Run(ToldArenaModule());

        Assert.IsNull(toldArena.Consistent, "Me24 NodeBound: a told population past the arena is silent too.");
        Assert.AreEqual(1, toldArena.Window.WindowSilences, "Me24 NodeBound: the arena bound covers told nodes, so the told-only overflow charges the same counter.");
        Assert.AreEqual(0, toldArena.Window.NodesSpawned, "Me24 NodeBound: nothing was spawned — the bound is the arena's, not the spawn count's.");
    }

    /// <summary>
    /// The spawn-depth ceiling records the measured instance's three levels and
    /// silences a linear chain that would run past it, with the depth standing
    /// exactly AT the ceiling and the other four quantities strictly below
    /// theirs.
    /// </summary>
    [TestMethod]
    public void Me25DepthBoundWindowRecordsAndSilences()
    {
        Assert.AreEqual(3, ContextModalRoleExpansionDecider.Run(CorpusShapedModule()).Window.MaxDepthReached, "Me25 DepthBound: the measured instance's clash sits three levels below the told frontier.");

        ModalExpansionOutcome overflow = ContextModalRoleExpansionDecider.Run(LinearChainModule());

        Assert.IsNull(overflow.Consistent, "Me25 DepthBound: a chain past the depth ceiling is silent.");
        Assert.AreEqual(1, overflow.Window.WindowSilences, "Me25 DepthBound: the silence is charged to the window counter.");
        Assert.AreEqual(ContextModalRoleExpansionDecider.ModalExpansionDepthBound, overflow.Window.MaxDepthReached, "Me25 DepthBound: the depth stands exactly at its ceiling.");
        AssertOtherBoundsBelowTheirCeilings(overflow.Window, "Me25 DepthBound", node: true, depth: false, label: true, edge: true, step: true);
    }

    /// <summary>
    /// The per-node label ceiling records the measured instance's peak of eleven
    /// — tied at the two nodes the transitive push loads — and silences a node
    /// whose definition drives its counted label past the ceiling, with the peak
    /// standing exactly AT the ceiling and the other four quantities strictly
    /// below theirs.
    /// </summary>
    [TestMethod]
    public void Me26LabelBoundWindowRecordsAndSilences()
    {
        Assert.AreEqual(11, ContextModalRoleExpansionDecider.Run(CorpusShapedModule()).Window.PeakLabelSize, "Me26 LabelBound: the measured instance's peak counted label is eleven.");

        ModalExpansionOutcome overflow = ContextModalRoleExpansionDecider.Run(WideLabelModule());

        Assert.IsNull(overflow.Consistent, "Me26 LabelBound: a node past the label ceiling is silent.");
        Assert.AreEqual(1, overflow.Window.WindowSilences, "Me26 LabelBound: the silence is charged to the window counter.");
        Assert.AreEqual(ContextModalRoleExpansionDecider.ModalExpansionLabelBound, overflow.Window.PeakLabelSize, "Me26 LabelBound: the peak counted label stands exactly at its ceiling.");
        AssertOtherBoundsBelowTheirCeilings(overflow.Window, "Me26 LabelBound", node: true, depth: true, label: false, edge: true, step: true);
    }

    /// <summary>
    /// The directed-edge ceiling records the measured instance's ten edges — five
    /// spawn-forward and five materialised inverse, and never one derived from
    /// transitivity — and silences a told-edge-dense module, which is the only
    /// shape that can reach it: every spawned node arrives with one forward edge
    /// and its mirror, so the spawned structure alone cannot bind the bound.
    /// </summary>
    [TestMethod]
    public void Me27EdgeBoundWindowRecordsAndSilences()
    {
        Assert.AreEqual(10, ContextModalRoleExpansionDecider.Run(CorpusShapedModule()).Window.EdgesMaterialised, "Me27 EdgeBound: the measured instance materialises ten directed edges under the counting conventions.");

        ModalExpansionOutcome overflow = ContextModalRoleExpansionDecider.Run(DenseToldEdgeModule());

        Assert.IsNull(overflow.Consistent, "Me27 EdgeBound: a told-edge-dense module past the edge ceiling is silent.");
        Assert.AreEqual(1, overflow.Window.WindowSilences, "Me27 EdgeBound: the silence is charged to the window counter.");
        Assert.AreEqual(ContextModalRoleExpansionDecider.ModalExpansionEdgeBound, overflow.Window.EdgesMaterialised, "Me27 EdgeBound: the directed-edge count stands exactly at its ceiling.");
        AssertOtherBoundsBelowTheirCeilings(overflow.Window, "Me27 EdgeBound", node: true, depth: true, label: true, edge: false, step: true);
    }

    /// <summary>
    /// The rule-application ceiling records the measured instance's fifty firings
    /// under the counting conventions — a spawn charging ONE for its edge fact and
    /// its membership fact together — and silences a module driven past a step
    /// ceiling supplied through the bounds seam, with the applications standing
    /// exactly AT that ceiling and the other four quantities strictly below their
    /// production ones. The seam is what makes the leg writable: the shipped step
    /// ceiling sits above the product of the arena and the label ceiling, so no
    /// module can reach it while the other four hold.
    /// </summary>
    [TestMethod]
    public void Me28StepBoundWindowRecordsAndSilences()
    {
        Assert.AreEqual(50, ContextModalRoleExpansionDecider.Run(CorpusShapedModule()).Window.RuleApplications, "Me28 StepBound: the measured instance charges fifty rule firings.");

        int stepCeiling = ContextModalRoleExpansionDecider.ModalExpansionStepBound / StepCeilingDivisor;
        ModalExpansionConstructionOptions options = new(ModalExpansionEntry.Decide, new ModalExpansionBounds(0, 0, 0, 0, stepCeiling));
        ModalExpansionOutcome overflow = ContextModalRoleExpansionDecider.Run(WideBranchingModule(), options);

        Assert.IsNull(overflow.Consistent, "Me28 StepBound: a module past the step ceiling is silent.");
        Assert.AreEqual(1, overflow.Window.WindowSilences, "Me28 StepBound: the silence is charged to the window counter.");
        Assert.AreEqual(stepCeiling, overflow.Window.RuleApplications, "Me28 StepBound: the application count stands exactly at its ceiling.");
        AssertOtherBoundsBelowTheirCeilings(overflow.Window, "Me28 StepBound", node: true, depth: true, label: true, edge: true, step: false);
    }

    /// <summary>
    /// A bound costs COMPLETENESS only and never correctness: the chain module's
    /// clash sits past the node arena, so the face is silent under the production
    /// ceiling; the SAME module under a widened arena supplied through the bounds
    /// seam, where zero means production per member, decides the module
    /// inconsistent.
    /// </summary>
    [TestMethod]
    public void Me29NearMissBoundSilencesWhatAWiderWalkWouldDecide()
    {
        ReasoningModule module = NearMissChainModule();
        ModalExpansionOutcome narrow = ContextModalRoleExpansionDecider.Run(module);

        Assert.IsNull(narrow.Consistent, "Me29 NearMissBound: the clash sits past the arena, so the face abstains.");
        Assert.AreEqual(1, narrow.Window.WindowSilences, "Me29 NearMissBound: the abstention is a charged window silence.");

        int widened = ContextModalRoleExpansionDecider.ModalExpansionNodeBound + ContextModalRoleExpansionDecider.ModalExpansionDepthBound;
        ModalExpansionOutcome wide = ContextModalRoleExpansionDecider.Run(module, new ModalExpansionConstructionOptions(ModalExpansionEntry.Decide, new ModalExpansionBounds(widened, 0, 0, 0, 0)));

        Assert.IsFalse(wide.Consistent, "Me29 NearMissBound: the same module under a widened arena reaches the same clash the narrow walk could not.");
        Assert.AreEqual(0, wide.Window.WindowSilences, "Me29 NearMissBound: the widened walk trips nothing.");
        Assert.StartsWith(NumericBoundReason, wide.ClashReason!, "Me29 NearMissBound: the widened walk's clash is the node-local numeric one.");
    }

    /// <summary>
    /// The dark control: with the face bit clear the module keeps the engine-face
    /// budget abstention byte for byte — the abstained outcome, no verdict, the
    /// inclusive ceiling spent — and the census still ships, the habitat label
    /// riding the abstention record while the decision counter stays at zero. The
    /// measurement path never expands, so it forms no verdict on any input.
    /// </summary>
    [TestMethod]
    public void Me30DarkFaceDecidesNothingAndTheCensusRides()
    {
        ReasoningModule module = CorpusShapedModule();
        ModuleDecision dark = ContextSaturationModuleReasoner.DecideModule(module, EnumerationDeciderFaces.None, DarkBudget, TestContext.CancellationToken);
        ModuleDecision litElsewhere = ContextSaturationModuleReasoner.DecideModule(module, AllFacesButModal, DarkBudget, TestContext.CancellationToken);
        ContextSaturationStatistics darkTotals = dark.Statistics.ContextTotals;

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, dark.Outcome, "Me30 DarkFace: the module abstains honestly with every face dark.");
        Assert.IsNull(dark.Verdict, "Me30 DarkFace: the dark abstention carries no verdict.");
        Assert.IsGreaterThan(0L, darkTotals.InferenceAttempts, "Me30 DarkFace: the dark exhaust is an admitted saturation, not a non-admission.");
        Assert.AreEqual(EnumerationHabitatClass.ModalRoleExpansion, darkTotals.EnumerationHabitat, "Me30 DarkFace: the habitat label rides the dark abstention record.");
        Assert.AreEqual(0, darkTotals.ModalExpansionDeciderClashes, "Me30 DarkFace: no clash decision with the face dark.");
        Assert.AreEqual(0, darkTotals.ModalExpansionRuleApplications, "Me30 DarkFace: the measurement path compares window ceilings and expands nothing.");
        Assert.AreEqual(0, darkTotals.ModalExpansionWindowSilences, "Me30 DarkFace: the told frontier sits inside the arena, so the measurement charges no silence.");

        ContextSaturationStatistics litTotals = litElsewhere.Statistics.ContextTotals;

        Assert.AreEqual(dark.Outcome, litElsewhere.Outcome, "Me30 DarkFace: lighting every OTHER face leaves the abstention record identical.");
        Assert.AreEqual(darkTotals.InferenceAttempts, litTotals.InferenceAttempts, "Me30 DarkFace: and spends the same attempts.");
        Assert.AreEqual(darkTotals.EnumerationHabitat, litTotals.EnumerationHabitat, "Me30 DarkFace: and carries the same census label.");
        Assert.AreEqual(0, litTotals.ModalExpansionDeciderClashes, "Me30 DarkFace: no sibling face claims the module either.");

        Assert.IsNull(ContextModalRoleExpansionDecider.Measure(module).Consistent, "Me30 DarkFace: the measurement surface never forms a verdict.");
        Assert.AreEqual(ModalExpansionWindow.Empty, ContextModalRoleExpansionDecider.Measure(module).Window, "Me30 DarkFace: the measurement compares ceilings only and charges nothing on a module inside them.");
    }

    /// <summary>
    /// The probe chain keeps every sibling label: the modal probe answers LAST on
    /// both of the recognizer's paths, so the only labels it can take are none
    /// labels and taking them IS the change. The target shape reads Shape M on
    /// BOTH paths; the gadget, partition, spy-point, bijection-chain,
    /// told-ground-witness and restriction-rich modules each keep their own; no
    /// nominal-battery or partition-battery row moves its label to Shape M, takes
    /// a modal decision, or loses its verdict; and the probe-ceiling leg reads
    /// none on a module whose only spawner sits deeper than the probe's bounded
    /// walk, which is a reach loss and never a wrong verdict.
    /// </summary>
    [TestMethod]
    public void Me31HabitatOrderingKeepsEverySiblingLabel()
    {
        ReasoningModule target = CorpusShapedModule();

        Assert.AreEqual(EnumerationHabitatClass.ModalRoleExpansion, ContextHabitatRecognizer.Classify(target, mentionsNominals: false, mentionsCounting: true), "Me31 HabitatOrdering: the target shape is reached on the nominal-free path.");
        Assert.AreEqual(EnumerationHabitatClass.ModalRoleExpansion, ContextHabitatRecognizer.Classify(target, mentionsNominals: true, mentionsCounting: true), "Me31 HabitatOrdering: and on the nominal path, where it answers behind every sibling probe.");

        Assert.AreEqual(EnumerationHabitatClass.BooleanCardinalityGadget, ContextHabitatRecognizer.Classify(GadgetShapedModule(), mentionsNominals: false, mentionsCounting: true), "Me31 HabitatOrdering: the gadget module keeps Shape G.");
        Assert.AreEqual(EnumerationHabitatClass.PartitionCounting, ContextHabitatRecognizer.Classify(PartitionShapedModule(), mentionsNominals: false, mentionsCounting: true), "Me31 HabitatOrdering: the partition module keeps Shape P.");
        Assert.AreEqual(EnumerationHabitatClass.BijectionChainArithmetic, ContextHabitatRecognizer.Classify(BijectionShapedModule(), mentionsNominals: false, mentionsCounting: true), "Me31 HabitatOrdering: the role-linked module keeps Shape B.");
        Assert.AreEqual(EnumerationHabitatClass.ToldGroundWitness, ContextHabitatRecognizer.Classify(ToldGroundShapedModule(), mentionsNominals: false, mentionsCounting: true), "Me31 HabitatOrdering: the told-ground module keeps Shape W.");
        Assert.AreEqual(EnumerationHabitatClass.RestrictionRichGround, ContextHabitatRecognizer.Classify(RestrictionRichShapedModule(), mentionsNominals: false, mentionsCounting: true), "Me31 HabitatOrdering: the restriction-rich module keeps Shape R.");
        Assert.AreEqual(EnumerationHabitatClass.SpyPointDomainBound, ContextHabitatRecognizer.Classify(SpyPointShapedModule(), mentionsNominals: true, mentionsCounting: true), "Me31 HabitatOrdering: the spy-point module keeps Shape S on the nominal path.");

        List<string> mismatches = [];
        foreach((string name, ReasoningModule module, bool _, string[] _) in ContextNominalBatteryTests.BatteryRows())
        {
            AppendSiblingRowMismatch(name, module, expectedConsistent: null, mismatchesToAppendTo: mismatches, token: TestContext.CancellationToken);
        }

        foreach((string name, ReasoningModule module, bool consistent) in ContextPartitionDeciderTests.PartitionRows())
        {
            AppendSiblingRowMismatch(name, module, expectedConsistent: consistent, mismatchesToAppendTo: mismatches, token: TestContext.CancellationToken);
        }

        Assert.IsEmpty(mismatches, string.Join(Environment.NewLine, mismatches));

        ReasoningModule beyondCeiling = ProbeCeilingModule(hops: ContextModalRoleExpansionDecider.ModalExpansionDepthBound / 2);

        Assert.AreEqual(EnumerationHabitatClass.None, ContextHabitatRecognizer.Classify(beyondCeiling, mentionsNominals: false, mentionsCounting: true), "Me31 HabitatOrdering: a spawner deeper than the probe's bounded walk reads none — a probe reach loss, never a wrong verdict.");
        Assert.AreEqual(EnumerationHabitatClass.ModalRoleExpansion, ContextHabitatRecognizer.Classify(ProbeCeilingModule(hops: 1), mentionsNominals: false, mentionsCounting: true), "Me31 HabitatOrdering: the same shape with the spawner inside the walk reads Shape M, so the ceiling is what the none reading measures.");

        ContextSaturationStatistics declined = ContextSaturationModuleReasoner.DecideModule(beyondCeiling, AllFaces, ProbeBudget, TestContext.CancellationToken).Statistics.ContextTotals;

        Assert.AreNotEqual(EnumerationHabitatClass.ModalRoleExpansion, declined.EnumerationHabitat, "Me31 HabitatOrdering: the declined module carries no Shape M label.");
        Assert.AreEqual(0, declined.ModalExpansionDeciderClashes, "Me31 HabitatOrdering: no verdict moves on the module the probe declines — the face is never reached, so the probe's boundedness costs reach only.");
    }

    /// <summary>The divisor the step-ceiling row derives its supplied ceiling from, so the row pins a value chained to the production constant rather than a literal.</summary>
    private const int StepCeilingDivisor = 16;

    /// <summary>Asserts that the four window quantities other than the one under test sit STRICTLY BELOW their production ceilings, which is how a window row identifies WHICH bound tripped: one silence counter is charged for any of the five.</summary>
    /// <param name="window">The measured window of the silent run.</param>
    /// <param name="row">The row prefix the messages open with.</param>
    /// <param name="node">Whether the node arena is one of the four to check.</param>
    /// <param name="depth">Whether the spawn depth is one of the four to check.</param>
    /// <param name="label">Whether the per-node label is one of the four to check.</param>
    /// <param name="edge">Whether the directed-edge count is one of the four to check.</param>
    /// <param name="step">Whether the rule-application count is one of the four to check.</param>
    private static void AssertOtherBoundsBelowTheirCeilings(ModalExpansionWindow window, string row, bool node, bool depth, bool label, bool edge, bool step)
    {
        if(node)
        {
            Assert.IsGreaterThan(window.NodesSpawned + 1, ContextModalRoleExpansionDecider.ModalExpansionNodeBound, row + ": the node arena stayed strictly below its ceiling, so the silence was charged elsewhere.");
        }

        if(depth)
        {
            Assert.IsGreaterThan(window.MaxDepthReached, ContextModalRoleExpansionDecider.ModalExpansionDepthBound, row + ": the spawn depth stayed strictly below its ceiling.");
        }

        if(label)
        {
            Assert.IsGreaterThan(window.PeakLabelSize, ContextModalRoleExpansionDecider.ModalExpansionLabelBound, row + ": the peak label stayed strictly below its ceiling.");
        }

        if(edge)
        {
            Assert.IsGreaterThan(window.EdgesMaterialised, ContextModalRoleExpansionDecider.ModalExpansionEdgeBound, row + ": the directed-edge count stayed strictly below its ceiling.");
        }

        if(step)
        {
            Assert.IsGreaterThan(window.RuleApplications, ContextModalRoleExpansionDecider.ModalExpansionStepBound, row + ": the rule-application count stayed strictly below its ceiling.");
        }
    }

    /// <summary>Appends one sibling-battery row's ordering mismatches: the census label may never move to Shape M, the modal face may take no decision, and a row with a known verdict must keep it.</summary>
    /// <param name="name">The row's name.</param>
    /// <param name="module">The row's module.</param>
    /// <param name="expectedConsistent">The row's certified verdict, or <see langword="null"/> when the row carries none.</param>
    /// <param name="mismatchesToAppendTo">The mismatch list.</param>
    /// <param name="token">The cancellation token.</param>
    private static void AppendSiblingRowMismatch(string name, ReasoningModule module, bool? expectedConsistent, List<string> mismatchesToAppendTo, CancellationToken token)
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, AllFaces, ProbeBudget, token);
        ContextSaturationStatistics totals = decision.Statistics.ContextTotals;
        if(totals.EnumerationHabitat == EnumerationHabitatClass.ModalRoleExpansion)
        {
            mismatchesToAppendTo.Add(name + ": a sibling-battery row's census label moved to Shape M.");

            return;
        }

        if(totals.ModalExpansionDeciderClashes > 0)
        {
            mismatchesToAppendTo.Add(name + ": a sibling-battery row was claimed by the modal-expansion face.");

            return;
        }

        if(expectedConsistent is bool consistent && (decision.Outcome != ReasoningDecisionOutcome.Decided || decision.Verdict is null || decision.Verdict.IsConsistent != consistent))
        {
            mismatchesToAppendTo.Add(name + ": the sibling row lost its certified verdict under the modal-lit faces.");
        }
    }

    /// <summary>
    /// The habitat's measured instance: the sixteen logical axioms of the
    /// dynamic-blocking module — one anonymous root, two subsumptions out of it,
    /// eight definitional equivalences, three told inverse pairs and one
    /// transitivity axiom — beside the inline data-property declaration the two
    /// kind-agnostic cardinality restrictions on the bounded property stand on.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule CorpusShapedModule()
    {
        return Module(
            EquivalentClasses(Class("A.2"), Intersection(Some("r", Thing), Some("p", Thing), All("r", Class("c")), All("p", Class("V.3")), All("p", Class("V.4")), All("p", Class("V.5")))),
            EquivalentClasses(Class("a.comp"), MinData("P.1", 1)),
            EquivalentClasses(Class("V.7"), All("invP", Class("V.6"))),
            EquivalentClasses(Class("V.6"), All("invS", Class("a.comp"))),
            EquivalentClasses(Class("V.5"), All("r", Class("c"))),
            EquivalentClasses(Class("V.4"), Some("p", Thing)),
            EquivalentClasses(Class("V.3"), Some("r", Thing)),
            SubClassOf(Class("Unsatisfiable"), Class("a")),
            SubClassOf(Class("Unsatisfiable"), Some("s", Class("A.2"))),
            EquivalentClasses(Class("c"), All("invR", Class("V.7"))),
            EquivalentClasses(Class("a"), MaxData("P.1", 0)),
            InverseProperties("invP", "p"),
            InverseProperties("invS", "s"),
            Transitive("p"),
            InverseProperties("invR", "r"),
            ClassAssertion(Class("Unsatisfiable"), Anonymous("x")),
            Declare(OwlEntityKind.DataProperty, Example + "P.1"));
    }

    /// <summary>
    /// The battery's canonical chassis: an anonymous root carrying a minimum of
    /// one on a declared data property beside an existential whose spawned
    /// successor unfolds to a universal over the told inverse of the spawning
    /// role, delivering a maximum of zero on the same property back to the root.
    /// One spawn, one mirrored edge, one upward hop, one clash.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ChassisModule()
    {
        return Module([.. ChassisAxioms()]);
    }

    /// <summary>The chassis axioms, so a perturbation row can extend or replace one of them without restating the rest.</summary>
    /// <returns>The axioms.</returns>
    private static List<OwlAxiom> ChassisAxioms()
    {
        return
        [
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Some("s", Class("Down"))),
            SubClassOf(Class("Root"), MinData("d", 1)),
            EquivalentClasses(Class("Down"), All("invS", Class("Cap"))),
            EquivalentClasses(Class("Cap"), MaxData("d", 0)),
            InverseProperties("invS", "s"),
            Declare(OwlEntityKind.DataProperty, Example + "d"),
        ];
    }

    /// <summary>The chassis rooted at a NAMED individual instead of a blank node — the control the first-class-anonymous-root row compares against.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NamedRootChassisModule()
    {
        List<OwlAxiom> axioms = ChassisAxioms();
        axioms[0] = ClassAssertion(Class("Root"), Individual("root"));

        return Module([.. axioms]);
    }

    /// <summary>The chassis with the defining equivalence written with the complex expression FIRST — the argument-order control proving the name side is chosen by construct.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SwappedEquivalenceChassisModule()
    {
        List<OwlAxiom> axioms = ChassisAxioms();
        axioms[3] = EquivalentClasses(All("invS", Class("Cap")), Class("Down"));

        return Module([.. axioms]);
    }

    /// <summary>The chassis with the told inverse pair written in the opposite argument order — the control proving the mirroring is symmetric.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ReversedInversePairChassisModule()
    {
        List<OwlAxiom> axioms = ChassisAxioms();
        axioms[5] = InverseProperties("s", "invS");

        return Module([.. axioms]);
    }

    /// <summary>The chassis with a rejected sub-property axiom added — the leg proving the outright rejection DROPS the axiom and lets the module continue.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SubPropertyChassisModule()
    {
        List<OwlAxiom> axioms = ChassisAxioms();
        axioms.Add(SubPropertyOf("sub", "s"));

        return Module([.. axioms]);
    }

    /// <summary>The chassis with an unresolved import added, and optionally with the bounded property's declaration removed so the kind rule silences rather than defaulting to a kind the import might carry.</summary>
    /// <param name="declared">Whether the local module declares the bounded property.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ImportBearingModule(bool declared)
    {
        List<OwlAxiom> axioms = ChassisAxioms();
        if(!declared)
        {
            axioms.RemoveAt(axioms.Count - 1);
        }

        axioms.Add(Imports(Alternate + "imported"));

        return Module([.. axioms]);
    }

    /// <summary>The chassis with told sameness and told distinctness added — both ignored soundly, since ignoring an axiom can only lose clashes.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule EqualityBearingModule()
    {
        List<OwlAxiom> axioms = ChassisAxioms();
        axioms.Add(SameAs(Individual("left"), Individual("right")));
        axioms.Add(DifferentFrom(Individual("left"), Individual("other")));

        return Module([.. axioms]);
    }

    /// <summary>The chassis with the bounded property's declaration present or absent — the kind-determination pair, where the legacy kind-agnostic spelling is decidable only through the declaration.</summary>
    /// <param name="declared">Whether the module declares the bounded property.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule KindUndeterminedModule(bool declared)
    {
        List<OwlAxiom> axioms = ChassisAxioms();
        if(!declared)
        {
            axioms.RemoveAt(axioms.Count - 1);
        }

        return Module([.. axioms]);
    }

    /// <summary>
    /// The module whose only route to the clash runs through the omitted
    /// definition-to-name half: the spawned node carries the definition an alias
    /// class shares, and only deriving that alias from its definition would open
    /// the second universal that closes the clash.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule DefinitionToNameOnlyModule()
    {
        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Some("s", Class("Payload"))),
            SubClassOf(Class("Root"), MinData("d", 1)),
            EquivalentClasses(Class("Payload"), All("invS", Class("Marker"))),
            EquivalentClasses(Class("Alias"), All("invS", Class("Marker"))),
            SubClassOf(Class("Alias"), All("invS", Class("Cap"))),
            EquivalentClasses(Class("Cap"), MaxData("d", 0)),
            InverseProperties("invS", "s"),
            Declare(OwlEntityKind.DataProperty, Example + "d"));
    }

    /// <summary>The module whose clash needs a told equivalence between two class IRIs read in one direction or the other — both derived, since neither side drops a conjunct.</summary>
    /// <param name="definedFirst">Whether the spawned node's own class stands first in the equivalence.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule BothNamedEquivalenceModule(bool definedFirst)
    {
        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Some("s", Class("Down"))),
            SubClassOf(Class("Root"), MinData("d", 1)),
            definedFirst ? EquivalentClasses(Class("Down"), Class("Beta")) : EquivalentClasses(Class("Beta"), Class("Down")),
            SubClassOf(Class("Beta"), All("invS", Class("Cap"))),
            EquivalentClasses(Class("Cap"), MaxData("d", 0)),
            InverseProperties("invS", "s"),
            Declare(OwlEntityKind.DataProperty, Example + "d"));
    }

    /// <summary>The module whose clash route runs through an equivalence with NO class-IRI operand, which drops whole; a second equivalence keeps the upward channel and the clash template present, so the silence is the drop's doing.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NoNamedOperandEquivalenceModule()
    {
        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Some("s", Class("Down"))),
            SubClassOf(Class("Root"), MinData("d", 1)),
            EquivalentClasses(Class("Down"), All("invS", Class("Marker"))),
            EquivalentClasses(Intersection(Class("Down")), All("invS", Class("Cap"))),
            EquivalentClasses(Class("Cap"), MaxData("d", 0)),
            InverseProperties("invS", "s"),
            Declare(OwlEntityKind.DataProperty, Example + "d"));
    }

    /// <summary>The chassis with the delivered class replaced by a two-member union whose first member is the clashing bound — a conjunctive misreading of the union would refute the module.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule UnionFillerModule()
    {
        List<OwlAxiom> axioms = ChassisAxioms();
        axioms[4] = EquivalentClasses(Class("Cap"), Union(MaxData("d", 0), Class("Safe")));

        return Module([.. axioms]);
    }

    /// <summary>The chassis with the delivered bound wrapped in a conjunction beside a singleton enumeration — an enumeration is disjunctive and refuses the module whole.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SingletonNominalModule()
    {
        List<OwlAxiom> axioms = ChassisAxioms();
        axioms[4] = EquivalentClasses(Class("Cap"), Intersection(MaxData("d", 0), OneOf("member")));

        return Module([.. axioms]);
    }

    /// <summary>The chassis whose delivered class is defined by a conjunction carrying an alien conjunct beside the clashing bound, under either the subsumption or the equivalence spelling.</summary>
    /// <param name="equivalence">Whether the definition is told as an equivalence rather than a subsumption.</param>
    /// <param name="alien">Whether the conjunction carries the alien conjunct.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule AlienConjunctModule(bool equivalence, bool alien)
    {
        OwlClassExpression definition = alien
            ? Intersection(Class("Safe"), HasSelf("g"))
            : Intersection(Class("Safe"));
        List<OwlAxiom> axioms = ChassisAxioms();
        axioms[4] = equivalence ? EquivalentClasses(Class("Cap"), definition) : SubClassOf(Class("Cap"), definition);
        axioms.Add(EquivalentClasses(Class("Safe"), MaxData("d", 0)));

        return Module([.. axioms]);
    }

    /// <summary>The chassis with object bounds on a named role instead of data bounds on a data property, optionally told transitive or told as the inverse of a transitive role — the non-simple-role gate's four legs.</summary>
    /// <param name="boundedRole">The role the bounds sit on.</param>
    /// <param name="transitive">Whether the transitivity axiom is told.</param>
    /// <param name="inverseOfTransitive">Whether the bounded role is told the inverse of the transitive one rather than being it.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ObjectBoundChassisModule(string boundedRole, bool transitive, bool inverseOfTransitive)
    {
        List<OwlAxiom> axioms =
        [
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Some("s", Class("Down"))),
            SubClassOf(Class("Root"), MinObject(boundedRole, 1)),
            EquivalentClasses(Class("Down"), All("invS", Class("Cap"))),
            EquivalentClasses(Class("Cap"), MaxObject(boundedRole, 0)),
            InverseProperties("invS", "s"),
            Declare(OwlEntityKind.ObjectProperty, Example + boundedRole),
        ];
        if(inverseOfTransitive)
        {
            axioms.Add(InverseProperties("invT", "t"));
        }

        if(transitive)
        {
            axioms.Add(Transitive("t"));
        }

        return Module([.. axioms]);
    }

    /// <summary>The chassis's clean data-property clash beside an unrelated cardinality restriction on a role told transitive — the disposition leg, where the gate silences the module WHOLE rather than dropping the offending axiom.</summary>
    /// <param name="transitive">Whether the unrelated role's transitivity is told.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule NonSimpleBesideCleanClashModule(bool transitive)
    {
        List<OwlAxiom> axioms = ChassisAxioms();
        axioms.Add(SubClassOf(Class("Extra"), MaxObject("t", 0)));
        axioms.Add(Declare(OwlEntityKind.ObjectProperty, Example + "t"));
        if(transitive)
        {
            axioms.Add(Transitive("t"));
        }

        return Module([.. axioms]);
    }

    /// <summary>
    /// The two-link chain whose deepest node carries a maximum while the chain's
    /// head carries a universal delivering the contradicting minimum: only a
    /// transitively closed edge relation, or the universal PUSH the told
    /// transitivity licenses, reaches that node.
    /// </summary>
    /// <param name="transitive">Whether the chain role's transitivity is told.</param>
    /// <param name="siblingTransitive">Whether a second, unrelated role is told transitive and carries its own universal.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ChainClosureModule(bool transitive, bool siblingTransitive)
    {
        List<OwlAxiom> axioms =
        [
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Some("n", Class("Mid"))),
            SubClassOf(Class("Root"), All("n", Class("Bump"))),
            EquivalentClasses(Class("Mid"), Some("n", Class("Leaf"))),
            EquivalentClasses(Class("Bump"), MinData("d", 1)),
            SubClassOf(Class("Leaf"), MaxData("d", 0)),
            SubClassOf(Class("Leaf"), All("invN", Class("Marker"))),
            InverseProperties("invN", "n"),
            Declare(OwlEntityKind.DataProperty, Example + "d"),
        ];
        if(transitive)
        {
            axioms.Add(Transitive("n"));
        }

        if(siblingTransitive)
        {
            axioms.Add(Transitive("g"));
            axioms.Add(SubClassOf(Class("Root"), Some("g", Class("Pad"))));
            axioms.Add(SubClassOf(Class("Root"), All("g", Class("Marker"))));
        }

        return Module([.. axioms]);
    }

    /// <summary>The asymmetric inverse fixture: two told inverse pairs, the spawned node carrying a universal over one of them, and only the pair whose partner it actually has an edge for delivering the clashing bound.</summary>
    /// <param name="universalRole">The inverse role the spawned node's universal quantifies.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule WrongInverseModule(string universalRole)
    {
        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Some("s", Class("Down"))),
            SubClassOf(Class("Root"), Some("w", Class("Pad"))),
            SubClassOf(Class("Root"), MinData("d", 1)),
            EquivalentClasses(Class("Down"), All(universalRole, Class("Cap"))),
            EquivalentClasses(Class("Cap"), MaxData("d", 0)),
            InverseProperties("invS", "s"),
            InverseProperties("invW", "w"),
            Declare(OwlEntityKind.DataProperty, Example + "d"));
    }

    /// <summary>The namespace fixture: an existential over one namespace's role beside a universal over another namespace's role of the SAME local name, whose filler is the empty class.</summary>
    /// <param name="universalNamespace">The namespace the universal's role is drawn from.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule NamespaceRoleModule(string universalNamespace)
    {
        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Intersection(SomeIn(Example, "r", Thing), AllIn(universalNamespace, "r", Nothing))),
            SubClassOf(Class("Root"), Some("s", Class("Down"))),
            EquivalentClasses(Class("Down"), All("invS", Class("Marker"))),
            InverseProperties("invS", "s"));
    }

    /// <summary>The maximum-against-spawns fixture: two distinct existentials on one role beside an unqualified maximum of one on that same role, with the clash template carried by an unreachable class so the expansion runs.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule MaxAgainstSpawnsModule()
    {
        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Intersection(Some("q", Class("Alpha")), Some("q", Class("Beta")), MaxObject("q", 1))),
            SubClassOf(Class("Alpha"), All("invQ", Class("Marker"))),
            InverseProperties("invQ", "q"),
            SubClassOf(Class("Unused"), MinObject("q", 2)),
            Declare(OwlEntityKind.ObjectProperty, Example + "q"));
    }

    /// <summary>The spawn-dedupe fixture: the root reaches one existential directly and a second through an alias class, the two fillers being the same class or two different ones.</summary>
    /// <param name="secondFiller">The alias route's existential filler.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule SpawnDedupeModule(string secondFiller)
    {
        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Class("Alias")),
            SubClassOf(Class("Root"), Some("q", Class("Alpha"))),
            SubClassOf(Class("Alias"), Some("q", Class(secondFiller))),
            EquivalentClasses(Class("Alpha"), All("invQ", Class("Marker"))),
            EquivalentClasses(Class("Beta"), All("invQ", Class("Marker"))),
            InverseProperties("invQ", "q"),
            SubClassOf(Class("Unused"), Intersection(MinObject("q", 2), MaxObject("q", 1))),
            Declare(OwlEntityKind.ObjectProperty, Example + "q"));
    }

    /// <summary>The structural-key fixture: two separately written existentials whose conjunctive fillers list the same operands in opposite order, so the two expressions are ONE key.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule StructuralKeyModule()
    {
        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Some("q", Intersection(Class("Alpha"), Class("Gamma")))),
            SubClassOf(Class("Root"), Some("q", Intersection(Class("Gamma"), Class("Alpha")))),
            EquivalentClasses(Class("Alpha"), All("invQ", Class("Marker"))),
            InverseProperties("invQ", "q"),
            SubClassOf(Class("Unused"), Intersection(MinObject("q", 2), MaxObject("q", 1))),
            Declare(OwlEntityKind.ObjectProperty, Example + "q"));
    }

    /// <summary>The batch-freeze fixture: an unrestricted existential beside a restricted one on the same role, written in either order — the frozen skip check spawns both whichever is processed first.</summary>
    /// <param name="unrestrictedFirst">Whether the unrestricted existential is the first conjunct.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule BatchFreezeModule(bool unrestrictedFirst)
    {
        OwlClassExpression conjunction = unrestrictedFirst
            ? Intersection(Some("q", Thing), Some("q", Class("Alpha")))
            : Intersection(Some("q", Class("Alpha")), Some("q", Thing));

        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), conjunction),
            EquivalentClasses(Class("Alpha"), All("invQ", Class("Marker"))),
            InverseProperties("invQ", "q"),
            SubClassOf(Class("Unused"), Intersection(MinObject("q", 2), MaxObject("q", 1))),
            Declare(OwlEntityKind.ObjectProperty, Example + "q"));
    }

    /// <summary>The ancestor fixture: the spawned node's universal pushes a class back up to the already-processed root, whose definition carries a second existential — spawned in the NEXT level's batch.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule AncestorSpawnModule()
    {
        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Some("q", Class("Alpha"))),
            EquivalentClasses(Class("Alpha"), All("invQ", Class("Late"))),
            EquivalentClasses(Class("Late"), Some("w", Class("Tail"))),
            InverseProperties("invQ", "q"),
            SubClassOf(Class("Unused"), Intersection(MinObject("q", 2), MaxObject("q", 1))),
            Declare(OwlEntityKind.ObjectProperty, Example + "q"));
    }

    /// <summary>The transitive-push fixture: a two-link chain over the named role beside a universal at its head, with one role told transitive and a simple sibling role carrying the identical shape.</summary>
    /// <param name="chainRole">The role the chain and the universal run over.</param>
    /// <param name="chainInverse">The told inverse of the chain role, carrying the upward channel signal.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule TransitivePushModule(string chainRole, string chainInverse)
    {
        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Intersection(Some(chainRole, Class("Mid")), All(chainRole, Class("Cap")))),
            EquivalentClasses(Class("Mid"), Some(chainRole, Class("Leaf"))),
            EquivalentClasses(Class("Cap"), MinData("d", 1)),
            SubClassOf(Class("Leaf"), MaxData("d", 0)),
            SubClassOf(Class("Leaf"), All(chainInverse, Class("Marker"))),
            InverseProperties("invT", "t"),
            InverseProperties("invG", "g"),
            Transitive("t"),
            Declare(OwlEntityKind.DataProperty, Example + "d"));
    }

    /// <summary>The exact-cardinality fixture: the chassis with the delivered bound told as an exact restriction, qualified or not, and an unreachable maximum keeping the clash template present in either case.</summary>
    /// <param name="qualified">Whether the exact restriction carries a qualifying range.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ExactCardinalityModule(bool qualified)
    {
        List<OwlAxiom> axioms = ChassisAxioms();
        axioms[4] = EquivalentClasses(Class("Cap"), qualified ? ExactDataQualified("d", 0) : ExactData("d", 0));
        axioms.Add(SubClassOf(Class("Unused"), MaxData("d", 0)));

        return Module([.. axioms]);
    }

    /// <summary>The empty class reaching a spawned node through a told superclass.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NothingSuperclassModule()
    {
        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Some("s", Class("Down"))),
            SubClassOf(Class("Down"), Nothing),
            SubClassOf(Class("Down"), All("invS", Class("Marker"))),
            InverseProperties("invS", "s"));
    }

    /// <summary>The empty class reaching a spawned node as a universal's filler.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NothingFillerModule()
    {
        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Intersection(Some("s", Class("Down")), All("s", Nothing))),
            EquivalentClasses(Class("Down"), All("invS", Class("Marker"))),
            InverseProperties("invS", "s"));
    }

    /// <summary>The local-name fixture: a minimum on one namespace's property meeting a maximum on the same local name in the other namespace, or on the very same full IRI in the control.</summary>
    /// <param name="sameNamespace">Whether the maximum sits on the SAME full IRI as the minimum.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule LocalNameSplitModule(bool sameNamespace)
    {
        string maximumNamespace = sameNamespace ? Example : Alternate;

        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Some("s", Class("Down"))),
            SubClassOf(Class("Root"), MinDataIn(Example, "d", 1)),
            EquivalentClasses(Class("Down"), All("invS", Class("Cap"))),
            EquivalentClasses(Class("Cap"), MaxDataIn(maximumNamespace, "d", 0)),
            SubClassOf(Class("Unused"), Intersection(MinDataIn(Example, "e", 1), MaxDataIn(Example, "e", 0))),
            InverseProperties("invS", "s"),
            Declare(OwlEntityKind.DataProperty, Example + "d"),
            Declare(OwlEntityKind.DataProperty, Alternate + "d"),
            Declare(OwlEntityKind.DataProperty, Example + "e"));
    }

    /// <summary>The kind-punned fixture: one IRI carrying an object minimum and a data maximum, whose kind is therefore ambiguous.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule KindPunModule()
    {
        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Some("s", Class("Down"))),
            SubClassOf(Class("Root"), MinObject("q", 1)),
            EquivalentClasses(Class("Down"), All("invS", Class("Cap"))),
            EquivalentClasses(Class("Cap"), MaxData("q", 0)),
            InverseProperties("invS", "s"),
            Declare(OwlEntityKind.ObjectProperty, Example + "q"));
    }

    /// <summary>The qualified-bound fixture: a minimum and a maximum over one role, each qualified by its own filler or with both fillers deleted, reached from a root through two subsumptions beside a spawner and an upward channel.</summary>
    /// <param name="minimum">The told minimum.</param>
    /// <param name="maximum">The told maximum.</param>
    /// <param name="fillersDeleted">Whether both qualifying fillers are removed, making the two bounds unqualified.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule QualifiedBoundModule(int minimum, int maximum, bool fillersDeleted)
    {
        OwlClassExpression low = fillersDeleted ? MinObject("R", minimum) : MinObjectQualified("R", minimum, Class("A"));
        OwlClassExpression high = fillersDeleted ? MaxObject("R", maximum) : MaxObjectQualified("R", maximum, Class("B"));

        return Module(
            ClassAssertion(Class("D"), Anonymous("x")),
            SubClassOf(Class("D"), Class("Lo")),
            SubClassOf(Class("D"), Class("Hi")),
            EquivalentClasses(Class("Lo"), low),
            EquivalentClasses(Class("Hi"), high),
            SubClassOf(Class("D"), Some("s", Class("Down"))),
            EquivalentClasses(Class("Down"), All("invS", Class("Marker"))),
            InverseProperties("invS", "s"),
            Declare(OwlEntityKind.ObjectProperty, Example + "R"));
    }

    /// <summary>The definition-cycle fixture: two pairs of classes each defined as the other, so the unfolding terminates by label-set dedupe rather than by any counter.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule DefinitionCycleModule()
    {
        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Some("s", Class("Down"))),
            EquivalentClasses(Class("Down"), Class("Cycle")),
            EquivalentClasses(Class("Cycle"), Class("Down")),
            SubClassOf(Class("Down"), All("invS", Class("Marker"))),
            EquivalentClasses(Class("Marker"), Class("MarkerAlias")),
            EquivalentClasses(Class("MarkerAlias"), Class("Marker")),
            InverseProperties("invS", "s"),
            SubClassOf(Class("Unused"), Intersection(MinData("d", 1), MaxData("d", 0))),
            Declare(OwlEntityKind.DataProperty, Example + "d"));
    }

    /// <summary>The told-frontier fixture: two told individuals joined by a told edge, each with its own existential, the clash closing through one spawned inverse hop and one told inverse hop.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ToldEdgeModule()
    {
        return Module(
            ClassAssertion(Class("Left"), Individual("i1")),
            ClassAssertion(Class("Right"), Individual("i2")),
            PropertyAssertion(Individual("i1"), "s", Individual("i2")),
            SubClassOf(Class("Left"), Some("q", Class("Alpha"))),
            SubClassOf(Class("Left"), MinData("d", 1)),
            SubClassOf(Class("Right"), Some("q", Class("Beta"))),
            EquivalentClasses(Class("Beta"), All("invQ", Class("Push"))),
            EquivalentClasses(Class("Push"), All("invS", Class("Cap"))),
            EquivalentClasses(Class("Cap"), MaxData("d", 0)),
            InverseProperties("invS", "s"),
            InverseProperties("invQ", "q"),
            Declare(OwlEntityKind.DataProperty, Example + "d"));
    }

    /// <summary>The transitive-inverse fixture: a two-link chain over a told-transitive role whose deepest node carries a universal over the chain role's told INVERSE, needing upward transitive propagation to reach the root.</summary>
    /// <param name="objectBounds">Whether the bounds sit on the inverse role itself, which the non-simple-role gate then silences.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule TransitiveInverseLimitModule(bool objectBounds)
    {
        OwlClassExpression minimum = objectBounds ? MinObject("invT", 1) : MinData("d", 1);
        OwlClassExpression maximum = objectBounds ? MaxObject("invT", 0) : MaxData("d", 0);
        List<OwlAxiom> axioms =
        [
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Some("t", Class("Mid"))),
            SubClassOf(Class("Root"), minimum),
            EquivalentClasses(Class("Mid"), Some("t", Class("Leaf"))),
            EquivalentClasses(Class("Leaf"), All("invT", Class("Cap"))),
            EquivalentClasses(Class("Cap"), maximum),
            Transitive("t"),
            InverseProperties("invT", "t"),
            Declare(objectBounds ? OwlEntityKind.ObjectProperty : OwlEntityKind.DataProperty, Example + (objectBounds ? "invT" : "d")),
        ];

        return Module([.. axioms]);
    }

    /// <summary>The choose-rule fixture: a maximum of one over a role carrying two existentials whose fillers hold contradictory bounds, so only a merge of the two successors would clash. The module carries no complement.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ChooseRuleModule()
    {
        return Module(
            ClassAssertion(Class("Root"), Anonymous("x")),
            SubClassOf(Class("Root"), Intersection(Some("q", Class("Alpha")), Some("q", Class("Beta")), MaxObject("q", 1))),
            SubClassOf(Class("Alpha"), MinData("d", 1)),
            SubClassOf(Class("Beta"), MaxData("d", 0)),
            SubClassOf(Class("Alpha"), All("invQ", Class("Marker"))),
            InverseProperties("invQ", "q"),
            Declare(OwlEntityKind.ObjectProperty, Example + "q"),
            Declare(OwlEntityKind.DataProperty, Example + "d"));
    }

    /// <summary>The branching module whose two-way self-definition fills the node arena before any other bound binds.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule BranchingLoopModule()
    {
        return Module(
            ClassAssertion(Intersection(Class("Loop"), Some("s", Class("Down"))), Anonymous("x")),
            EquivalentClasses(Class("Loop"), Intersection(Some("q1", Class("Loop")), Some("q2", Class("Loop")))),
            EquivalentClasses(Class("Down"), All("invS", Class("Marker"))),
            InverseProperties("invS", "s"),
            SubClassOf(Class("Unused"), Intersection(MinData("d", 1), MaxData("d", 0))),
            Declare(OwlEntityKind.DataProperty, Example + "d"));
    }

    /// <summary>The branching module with a wide definition, so the rule applications outrun a supplied step ceiling while the arena, the depth, the labels and the edges all stay inside their production ones.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule WideBranchingModule()
    {
        List<OwlClassExpression> operands = [Some("q1", Class("Loop")), Some("q2", Class("Loop"))];
        for(int index = 0; index < WideDefinitionNames; index++)
        {
            operands.Add(Class("Pad" + index));
        }

        return Module(
            ClassAssertion(Intersection(Class("Loop"), Some("s", Class("Down"))), Anonymous("x")),
            EquivalentClasses(Class("Loop"), Intersection([.. operands])),
            EquivalentClasses(Class("Down"), All("invS", Class("Marker"))),
            InverseProperties("invS", "s"),
            SubClassOf(Class("Unused"), Intersection(MinData("d", 1), MaxData("d", 0))),
            Declare(OwlEntityKind.DataProperty, Example + "d"));
    }

    /// <summary>The named classes the wide branching definition carries beside its two existentials, so every spawned node charges a fixed batch of applications well before the arena fills.</summary>
    private const int WideDefinitionNames = 14;

    /// <summary>The told-arena module: one class assertion per told individual, one individual past the node arena, and no existential anywhere.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ToldArenaModule()
    {
        List<OwlAxiom> axioms = [];
        for(int index = 0; index <= ContextModalRoleExpansionDecider.ModalExpansionNodeBound; index++)
        {
            axioms.Add(ClassAssertion(Class("Ground"), Individual("ground" + index)));
        }

        return Module([.. axioms]);
    }

    /// <summary>The linear chain whose single self-definition spawns one successor per level, running past the spawn-depth ceiling.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule LinearChainModule()
    {
        return Module(
            ClassAssertion(Intersection(Class("Chain"), Some("s", Class("Down"))), Anonymous("x")),
            EquivalentClasses(Class("Chain"), Some("q", Class("Chain"))),
            EquivalentClasses(Class("Down"), All("invS", Class("Marker"))),
            InverseProperties("invS", "s"),
            SubClassOf(Class("Unused"), Intersection(MinData("d", 1), MaxData("d", 0))),
            Declare(OwlEntityKind.DataProperty, Example + "d"));
    }

    /// <summary>The wide-label module: one class whose definition conjoins one named class more than the per-node label ceiling admits.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule WideLabelModule()
    {
        List<OwlClassExpression> operands = [];
        for(int index = 0; index <= ContextModalRoleExpansionDecider.ModalExpansionLabelBound; index++)
        {
            operands.Add(Class("Wide" + index));
        }

        return Module(
            ClassAssertion(Intersection(Class("Wide"), Some("s", Class("Down"))), Anonymous("x")),
            EquivalentClasses(Class("Wide"), Intersection([.. operands])),
            EquivalentClasses(Class("Down"), All("invS", Class("Marker"))),
            InverseProperties("invS", "s"),
            SubClassOf(Class("Unused"), Intersection(MinData("d", 1), MaxData("d", 0))),
            Declare(OwlEntityKind.DataProperty, Example + "d"));
    }

    /// <summary>The told-edge-dense module: every ordered pair of a told individual population joined by a told edge whose inverse is mirrored, so the directed-edge ceiling binds while the arena stays well inside its own.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule DenseToldEdgeModule()
    {
        int terms = 2;
        while(2 * terms * (terms - 1) <= ContextModalRoleExpansionDecider.ModalExpansionEdgeBound)
        {
            terms++;
        }

        List<OwlAxiom> axioms = [];
        for(int source = 0; source < terms; source++)
        {
            axioms.Add(ClassAssertion(Class("Ground"), Individual("dense" + source)));
            for(int target = 0; target < terms; target++)
            {
                if(source != target)
                {
                    axioms.Add(PropertyAssertion(Individual("dense" + source), "s", Individual("dense" + target)));
                }
            }
        }

        axioms.Add(SubClassOf(Class("Ground"), Some("q", Class("Alpha"))));
        axioms.Add(EquivalentClasses(Class("Alpha"), All("invQ", Class("Marker"))));
        axioms.Add(InverseProperties("invQ", "q"));
        axioms.Add(InverseProperties("invS", "s"));
        axioms.Add(SubClassOf(Class("Unused"), Intersection(MinData("d", 1), MaxData("d", 0))));
        axioms.Add(Declare(OwlEntityKind.DataProperty, Example + "d"));

        return Module([.. axioms]);
    }

    /// <summary>
    /// The near-miss chain: every link spawns the next link beside four padding
    /// successors, so the arena fills several links before the last one — which
    /// carries the whole clash — is ever allocated. A widened arena reaches it.
    /// </summary>
    /// <returns>The module.</returns>
    private static ReasoningModule NearMissChainModule()
    {
        List<OwlAxiom> axioms =
        [
            ClassAssertion(Class("Link0"), Anonymous("x")),
            EquivalentClasses(Class("Pad"), All("invQ", Class("Marker"))),
            InverseProperties("invQ", "q"),
            Declare(OwlEntityKind.DataProperty, Example + "d"),
        ];
        for(int link = 0; link < NearMissChainLinks; link++)
        {
            axioms.Add(EquivalentClasses(
                Class("Link" + link),
                Intersection(
                    Some("q", Class("Link" + (link + 1))),
                    Some("f1", Class("Pad")),
                    Some("f2", Class("Pad")),
                    Some("f3", Class("Pad")),
                    Some("f4", Class("Pad")))));
        }

        axioms.Add(EquivalentClasses(Class("Link" + NearMissChainLinks), Intersection(MinData("d", 1), MaxData("d", 0))));

        return Module([.. axioms]);
    }

    /// <summary>The near-miss chain's link count: deep enough that the arena fills before the clash-carrying link is allocated, and shallow enough that a widened arena reaches it inside the spawn-depth ceiling.</summary>
    private const int NearMissChainLinks = 15;

    /// <summary>The probe-ceiling module: the root's class unfolds through a chain of named classes before the first existential appears, so the recognizer's bounded walk reaches the spawner only for a short chain.</summary>
    /// <param name="hops">The named classes standing between the root's class and the spawner.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule ProbeCeilingModule(int hops)
    {
        List<OwlAxiom> axioms =
        [
            ClassAssertion(Class("Step0"), Anonymous("x")),
            EquivalentClasses(Class("Down"), All("invS", Class("Cap"))),
            EquivalentClasses(Class("Cap"), MaxData("d", 0)),
            SubClassOf(Class("Step0"), MinData("d", 1)),
            InverseProperties("invS", "s"),
            Declare(OwlEntityKind.DataProperty, Example + "d"),
        ];
        for(int hop = 0; hop < hops; hop++)
        {
            axioms.Add(SubClassOf(Class("Step" + hop), Class("Step" + (hop + 1))));
        }

        axioms.Add(SubClassOf(Class("Step" + hops), Some("s", Class("Down"))));

        return Module([.. axioms]);
    }

    /// <summary>A module the boolean-cardinality-gadget probe claims: a told equivalence carrying a bare boolean cardinality beside one carrying a named-only intersection.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule GadgetShapedModule()
    {
        return Module(
            EquivalentClasses(Class("Gadget"), MaxObject("g", 1)),
            EquivalentClasses(Class("Compound"), Intersection(Class("Ga"), Class("Gb"))),
            ClassAssertion(Class("Compound"), Individual("gadget")));
    }

    /// <summary>A module the partition-counting probe claims: a told equivalence whose intersection carries two existentials and exactly one unqualified maximum, all over one role.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule PartitionShapedModule()
    {
        return Module(
            EquivalentClasses(Class("Part"), Intersection(Some("pr", Class("Pa")), Some("pr", Class("Pb")), MaxObject("pr", 1))),
            ClassAssertion(Class("Part"), Individual("part")));
    }

    /// <summary>A module the bijection-chain probe claims: one role told functional that also stands in a told inverse pair and heads a told existential.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule BijectionShapedModule()
    {
        return Module(
            Functional("bq"),
            InverseProperties("bq", "invBq"),
            SubClassOf(Class("Ba"), Some("bq", Class("Bb"))),
            ClassAssertion(Class("Ba"), Individual("bijection")));
    }

    /// <summary>A module the told-ground-witness probe claims: a told object-property assertion beside a told inverse pair and a told existential, with one counting mention and a told population well inside the told-ground carrier ceiling.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule ToldGroundShapedModule()
    {
        return Module(
            PropertyAssertion(Individual("w1"), "wr", Individual("w2")),
            InverseProperties("wr", "invWr"),
            SubClassOf(Class("Wa"), Some("wr", Class("Wb"))),
            SubClassOf(Class("Wc"), MaxObject("wc", 1)),
            ClassAssertion(Class("Wa"), Individual("w1")));
    }

    /// <summary>A module the restriction-rich-ground probe claims: two obligation-position restrictions beside a told individual population above the told-ground carrier ceiling.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule RestrictionRichShapedModule()
    {
        List<OwlAxiom> axioms =
        [
            SubClassOf(Class("Ra"), MaxObject("rr", 1)),
            SubClassOf(Class("Rb"), All("rr", Class("Rc"))),
        ];
        for(int index = 0; index < RestrictionRichTerms; index++)
        {
            axioms.Add(ClassAssertion(Class("Ra"), Individual("rich" + index)));
        }

        return Module([.. axioms]);
    }

    /// <summary>The told individual population the restriction-rich module carries — comfortably above the floor its probe reads.</summary>
    private const int RestrictionRichTerms = 20;

    /// <summary>A module the spy-point probe claims on the nominal path: the whole domain funnelled into a singleton enumeration beside a told unqualified cap.</summary>
    /// <returns>The module.</returns>
    private static ReasoningModule SpyPointShapedModule()
    {
        return Module(
            SubClassOf(Thing, Some("sp", OneOf("spy"))),
            InverseProperties("sp", "invSp"),
            ClassAssertion(MaxObject("invSp", 2), Individual("spy")),
            SubClassOf(Class("Su"), MinObject("sr", 3)),
            ClassAssertion(Class("Su"), Anonymous("spyroot")));
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
        return new Quad(new NamedNode(Utf8Strings.From(Example + marker)), new NamedNode(Utf8Strings.From(Example + "origin")), new NamedNode(Utf8Strings.From(Example + "root")), Graph: null);
    }

    /// <summary>The <c>owl:Thing</c> reference — the unrestricted filler.</summary>
    private static OwlClassReference Thing { get; } = new(new NamedNode(Utf8Strings.From(OwlThing)));

    /// <summary>The <c>owl:Nothing</c> reference — the empty class whose membership is a clash.</summary>
    private static OwlClassReference Nothing { get; } = new(new NamedNode(Utf8Strings.From(OwlNothing)));

    /// <summary>A named class reference in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The reference.</returns>
    private static OwlClassReference Class(string local)
    {
        return new OwlClassReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A named object property expression in the given namespace.</summary>
    /// <param name="space">The namespace.</param>
    /// <param name="local">The local name.</param>
    /// <returns>The property expression.</returns>
    private static OwlObjectPropertyReference PropertyIn(string space, string local)
    {
        return new OwlObjectPropertyReference(new NamedNode(Utf8Strings.From(space + local)));
    }

    /// <summary>A named object property expression in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property expression.</returns>
    private static OwlObjectPropertyReference Property(string local)
    {
        return PropertyIn(Example, local);
    }

    /// <summary>A named data property node in the given namespace.</summary>
    /// <param name="space">The namespace.</param>
    /// <param name="local">The local name.</param>
    /// <returns>The property node.</returns>
    private static NamedNode DataPropertyIn(string space, string local)
    {
        return new NamedNode(Utf8Strings.From(space + local));
    }

    /// <summary>A named individual in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The individual node.</returns>
    private static NamedNode Individual(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>An anonymous individual — the measured instance's own root carrier.</summary>
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

    /// <summary>An intersection of class expressions.</summary>
    /// <param name="operands">The intersection operands.</param>
    /// <returns>The intersection.</returns>
    private static OwlObjectIntersectionOf Intersection(params OwlClassExpression[] operands)
    {
        return new OwlObjectIntersectionOf([.. operands]);
    }

    /// <summary>A union of class expressions — the construct the module-wide disjunction gate refuses.</summary>
    /// <param name="operands">The union operands.</param>
    /// <returns>The union.</returns>
    private static OwlObjectUnionOf Union(params OwlClassExpression[] operands)
    {
        return new OwlObjectUnionOf([.. operands]);
    }

    /// <summary>A self-restriction — an alien conjunct outside the admission grammar, whose presence drops its whole axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasSelf HasSelf(string property)
    {
        return new OwlObjectHasSelf(Property(property));
    }

    /// <summary>An existential restriction over a named role in the given namespace.</summary>
    /// <param name="space">The role's namespace.</param>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom SomeIn(string space, string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(PropertyIn(space, property), filler);
    }

    /// <summary>An existential restriction over a named role in the example namespace.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return SomeIn(Example, property, filler);
    }

    /// <summary>A universal restriction over a named role in the given namespace.</summary>
    /// <param name="space">The role's namespace.</param>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectAllValuesFrom AllIn(string space, string property, OwlClassExpression filler)
    {
        return new OwlObjectAllValuesFrom(PropertyIn(space, property), filler);
    }

    /// <summary>A universal restriction over a named role in the example namespace.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class expression.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectAllValuesFrom All(string property, OwlClassExpression filler)
    {
        return AllIn(Example, property, filler);
    }

    /// <summary>An unqualified minimum-cardinality restriction over a named role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality MinObject(string property, int cardinality)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Min, cardinality, Property(property), Filler: null);
    }

    /// <summary>A qualified minimum-cardinality restriction over a named role — outside the admission grammar, so its whole axiom drops.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <param name="filler">The qualifying filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality MinObjectQualified(string property, int cardinality, OwlClassExpression filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Min, cardinality, Property(property), filler);
    }

    /// <summary>An unqualified maximum-cardinality restriction over a named role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality MaxObject(string property, int cardinality)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), Filler: null);
    }

    /// <summary>A qualified maximum-cardinality restriction over a named role — outside the admission grammar, so its whole axiom drops.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <param name="filler">The qualifying filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality MaxObjectQualified(string property, int cardinality, OwlClassExpression filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), filler);
    }

    /// <summary>An unqualified minimum-cardinality restriction over a data property in the given namespace — the measured instance's legacy kind-agnostic spelling.</summary>
    /// <param name="space">The property's namespace.</param>
    /// <param name="property">The property's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataCardinality MinDataIn(string space, string property, int cardinality)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Min, cardinality, DataPropertyIn(space, property), Range: null);
    }

    /// <summary>An unqualified minimum-cardinality restriction over a data property in the example namespace.</summary>
    /// <param name="property">The property's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataCardinality MinData(string property, int cardinality)
    {
        return MinDataIn(Example, property, cardinality);
    }

    /// <summary>An unqualified maximum-cardinality restriction over a data property in the given namespace.</summary>
    /// <param name="space">The property's namespace.</param>
    /// <param name="property">The property's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataCardinality MaxDataIn(string space, string property, int cardinality)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Max, cardinality, DataPropertyIn(space, property), Range: null);
    }

    /// <summary>An unqualified maximum-cardinality restriction over a data property in the example namespace.</summary>
    /// <param name="property">The property's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataCardinality MaxData(string property, int cardinality)
    {
        return MaxDataIn(Example, property, cardinality);
    }

    /// <summary>An unqualified exact-cardinality restriction over a data property — read as its minimum and maximum halves together.</summary>
    /// <param name="property">The property's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataCardinality ExactData(string property, int cardinality)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Exact, cardinality, DataPropertyIn(Example, property), Range: null);
    }

    /// <summary>A QUALIFIED exact-cardinality restriction over a data property — outside the admission grammar, so its whole axiom drops.</summary>
    /// <param name="property">The property's local name.</param>
    /// <param name="cardinality">The bound.</param>
    /// <returns>The restriction.</returns>
    private static OwlDataCardinality ExactDataQualified(string property, int cardinality)
    {
        return new OwlDataCardinality(OwlCardinalityKind.Exact, cardinality, DataPropertyIn(Example, property), new OwlDatatypeReference(new NamedNode(Utf8Strings.From(XsdString))));
    }

    /// <summary>A subclass axiom.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>A told equivalence axiom.</summary>
    /// <param name="first">The first operand.</param>
    /// <param name="second">The second operand.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentClassesAxiom EquivalentClasses(OwlClassExpression first, OwlClassExpression second)
    {
        return new OwlEquivalentClassesAxiom(first, second) { Origin = Origin("equivalent") };
    }

    /// <summary>A class assertion typing an individual.</summary>
    /// <param name="type">The asserted class expression.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, RdfTerm individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

    /// <summary>A told object-property assertion — an ordinary materialised edge between two level-zero nodes.</summary>
    /// <param name="source">The edge's source individual.</param>
    /// <param name="property">The role's local name.</param>
    /// <param name="target">The edge's target individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom PropertyAssertion(RdfTerm source, string property, RdfTerm target)
    {
        return new OwlObjectPropertyAssertionAxiom(source, new NamedNode(Utf8Strings.From(Example + property)), target) { Origin = Origin("edge") };
    }

    /// <summary>A told inverse between two named object properties.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlInverseObjectPropertiesAxiom InverseProperties(string first, string second)
    {
        return new OwlInverseObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("inverse") };
    }

    /// <summary>A told transitivity characteristic — the only licence the universal push reads.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Transitive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Transitive, Property(property)) { Origin = Origin("transitive") };
    }

    /// <summary>A told functional characteristic — the bijection-chain probe's own signal.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Functional(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, Property(property)) { Origin = Origin("functional") };
    }

    /// <summary>A told sub-property axiom — outside the admission grammar, DROPPED whole while the module continues.</summary>
    /// <param name="sub">The sub-role's local name.</param>
    /// <param name="super">The super-role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubObjectPropertyOfAxiom SubPropertyOf(string sub, string super)
    {
        return new OwlSubObjectPropertyOfAxiom(Property(sub), Property(super)) { Origin = Origin("subproperty") };
    }

    /// <summary>An entity declaration — the property-kind evidence the numeric clash's second key half stands on.</summary>
    /// <param name="kind">The declared entity kind.</param>
    /// <param name="iri">The declared entity's IRI.</param>
    /// <returns>The axiom.</returns>
    private static OwlDeclarationAxiom Declare(OwlEntityKind kind, string iri)
    {
        return new OwlDeclarationAxiom(kind, new NamedNode(Utf8Strings.From(iri))) { Origin = Origin("declare") };
    }

    /// <summary>A told sameness axiom — ignored soundly in the clash direction.</summary>
    /// <param name="first">The first individual.</param>
    /// <param name="second">The second individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlSameIndividualAxiom SameAs(RdfTerm first, RdfTerm second)
    {
        return new OwlSameIndividualAxiom(first, second) { Origin = Origin("same") };
    }

    /// <summary>A told distinctness axiom — ignored soundly in the clash direction.</summary>
    /// <param name="first">The first individual.</param>
    /// <param name="second">The second individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlDifferentIndividualsAxiom DifferentFrom(RdfTerm first, RdfTerm second)
    {
        return new OwlDifferentIndividualsAxiom([first, second]) { Origin = Origin("different") };
    }

    /// <summary>An unresolved import — a non-logical passthrough the expansion never reads.</summary>
    /// <param name="iri">The imported ontology's IRI.</param>
    /// <returns>The axiom.</returns>
    private static OwlImportAxiom Imports(string iri)
    {
        return new OwlImportAxiom(new NamedNode(Utf8Strings.From(iri))) { Origin = Origin("import") };
    }
}
