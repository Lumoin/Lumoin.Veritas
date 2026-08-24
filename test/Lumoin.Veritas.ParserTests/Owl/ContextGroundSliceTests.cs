using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The SROIQ ground-slice battery through the CONTEXT arm
/// (<see cref="ContextSaturationModuleReasoner"/>, the consequence-based
/// saturation engine). Every row is transcribed VERBATIM from the certified
/// battery table, whose semantic cells were derived independently of the
/// engine; the semantic (consistent / inconsistent) cell is the ground-truth
/// surface, the decision path and remainder are engine expectations. The
/// context-decided families (GS, B1/B2, GE, B3, GM, GC) assert the path and the
/// consistency verdict; B1 and B2 additionally probe the internal seam that the
/// bottom lands in the shared/target ground context, not the trivial global
/// context (the MU3 discriminator). The honesty families
/// (GH1/GH2/B5, GH3–GH8) assert the fallback's fragment-relative delegation
/// discipline: the context arm must delegate, and the
/// honest fallback answers fragment-relative over a named unsupported remainder
/// rather than a decisive verdict it cannot soundly reach. GH5 pins reserved-scan
/// precedence: a co-occurring reserved-role assertion and a
/// same-different collision delegate on the reserved remainder rather than decide
/// inconsistent at pre-merge.
/// </summary>
[TestClass]
internal sealed class ContextGroundSliceTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/sroiq5#";

    //§5.1 — Ground clash / no-clash pairs (context-decided).

    /// <summary>GS1: two disjoint class assertions on one individual collapse its ground context.</summary>
    [TestMethod]
    public void GS1DisjointClassAssertionsInconsistent()
    {
        AssertInconsistent(
            ClassAssertion(Reference("Gs1A"), Individual("gs1a")),
            ClassAssertion(Reference("Gs1B"), Individual("gs1a")),
            Disjoint("Gs1A", "Gs1B"));
    }

    /// <summary>GS2: the same disjointness over two DIFFERENT individuals is consistent.</summary>
    [TestMethod]
    public void GS2DisjointClassesDistinctIndividualsConsistent()
    {
        AssertConsistent(
            ClassAssertion(Reference("Gs2A"), Individual("gs2a")),
            ClassAssertion(Reference("Gs2B"), Individual("gs2b")),
            Disjoint("Gs2A", "Gs2B"));
    }

    /// <summary>GS3: an existential into a bottom class over a minted edge collapses the carrier's ground context.</summary>
    [TestMethod]
    public void GS3ExistentialIntoBottomInconsistent()
    {
        AssertInconsistent(
            ClassAssertion(Reference("Gs3A"), Individual("gs3a")),
            SubClassOf(Reference("Gs3A"), Some("gs3r", Reference("Gs3B"))),
            SubClassOf(Reference("Gs3B"), Nothing));
    }

    /// <summary>GS4: the same existential into a satisfiable class is consistent.</summary>
    [TestMethod]
    public void GS4ExistentialIntoSatisfiableConsistent()
    {
        AssertConsistent(
            ClassAssertion(Reference("Gs4A"), Individual("gs4a")),
            SubClassOf(Reference("Gs4A"), Some("gs4r", Reference("Gs4B"))),
            SubClassOf(Reference("Gs4B"), Reference("Gs4C")));
    }

    /// <summary>GS5: a domain over an asserted edge types the source into a bottom class.</summary>
    [TestMethod]
    public void GS5DomainIntoBottomInconsistent()
    {
        AssertInconsistent(
            ObjectPropertyAssertion("gs5r", "gs5a", "gs5b"),
            Domain("gs5r", Reference("Gs5C")),
            SubClassOf(Reference("Gs5C"), Nothing));
    }

    /// <summary>GS6: a range over an asserted edge types the target into a bottom class (bottom in the target's ground context).</summary>
    [TestMethod]
    public void GS6RangeIntoBottomInconsistent()
    {
        AssertInconsistent(
            ObjectPropertyAssertion("gs6r", "gs6a", "gs6b"),
            Range("gs6r", Reference("Gs6D")),
            SubClassOf(Reference("Gs6D"), Nothing));
    }

    /// <summary>GS7: a domain and range over an asserted edge, neither bottom, is consistent.</summary>
    [TestMethod]
    public void GS7DomainAndRangeConsistent()
    {
        AssertConsistent(
            ObjectPropertyAssertion("gs7r", "gs7a", "gs7b"),
            Domain("gs7r", Reference("Gs7C")),
            Range("gs7r", Reference("Gs7D")));
    }

    /// <summary>GS8: a universal restriction flowing over an asserted edge types the target into a bottom class.</summary>
    [TestMethod]
    public void GS8UniversalOverEdgeIntoBottomInconsistent()
    {
        AssertInconsistent(
            ClassAssertion(Reference("Gs8C"), Individual("gs8a")),
            SubClassOf(Reference("Gs8C"), All("gs8r", Reference("Gs8D"))),
            SubClassOf(Reference("Gs8D"), Nothing),
            ObjectPropertyAssertion("gs8r", "gs8a", "gs8b"));
    }

    /// <summary>B1: the in-degree-two diamond — two distinct predecessors force disjoint range concepts onto the shared target, whose ground context clashes only through the unconditional K1 seeding.</summary>
    [TestMethod]
    public void B1InDegreeTwoDiamondInconsistentInSharedTarget()
    {
        AssertBottomInGroundContext("b1b",
            ObjectPropertyAssertion("b1r", "b1a", "b1b"),
            ObjectPropertyAssertion("b1s", "b1c", "b1b"),
            Range("b1r", Reference("B1D")),
            Range("b1s", Reference("B1E")),
            Disjoint("B1D", "B1E"));
    }

    /// <summary>B1t: the diamond without the disjointness is consistent.</summary>
    [TestMethod]
    public void B1tInDegreeTwoDiamondConsistentTwin()
    {
        AssertConsistent(
            ObjectPropertyAssertion("b1tr", "b1ta", "b1tb"),
            ObjectPropertyAssertion("b1ts", "b1tc", "b1tb"),
            Range("b1tr", Reference("B1tD")),
            Range("b1ts", Reference("B1tE")));
    }

    /// <summary>B2: a universal over an asserted edge and a disjoint class assertion on the target collapse the target's ground context — a non-global bottom no trivial-context criterion catches (the MU3 discriminator).</summary>
    [TestMethod]
    public void B2BLocalClashInconsistentInTargetContext()
    {
        AssertBottomInGroundContext("b2b",
            ClassAssertion(Reference("B2C"), Individual("b2a")),
            SubClassOf(Reference("B2C"), All("b2r", Reference("B2D"))),
            ObjectPropertyAssertion("b2r", "b2a", "b2b"),
            ClassAssertion(Reference("B2E"), Individual("b2b")),
            Disjoint("B2D", "B2E"));
    }

    /// <summary>B2t: the same universal flow with a non-disjoint class assertion on the target is consistent.</summary>
    [TestMethod]
    public void B2tBLocalConsistentTwin()
    {
        AssertConsistent(
            ClassAssertion(Reference("B2tC"), Individual("b2ta")),
            SubClassOf(Reference("B2tC"), All("b2tr", Reference("B2tD"))),
            ObjectPropertyAssertion("b2tr", "b2ta", "b2tb"),
            ClassAssertion(Reference("B2tF"), Individual("b2tb")));
    }

    //§5.2 — Edge-entailment rows (ground closure) + Self-ghost family.

    /// <summary>GE1: an asserted edge and a negative assertion on the same directed pair clash directly.</summary>
    [TestMethod]
    public void GE1DirectNegativeAssertionInconsistent()
    {
        AssertInconsistent(
            ObjectPropertyAssertion("ge1r", "ge1a", "ge1b"),
            NegativeObjectPropertyAssertion(ObjectProperty("ge1r"), "ge1a", "ge1b"));
    }

    /// <summary>GE2: a sub-role edge entails the super-role edge the negative assertion denies.</summary>
    [TestMethod]
    public void GE2HierarchyNegativeAssertionInconsistent()
    {
        AssertInconsistent(
            ObjectPropertyAssertion("ge2s", "ge2a", "ge2b"),
            SubObjectPropertyOf("ge2s", "ge2r"),
            NegativeObjectPropertyAssertion(ObjectProperty("ge2r"), "ge2a", "ge2b"));
    }

    /// <summary>GE3: an inverse-normalized negative assertion clashes with the mirrored asserted edge.</summary>
    [TestMethod]
    public void GE3InverseNormalizedNegativeAssertionInconsistent()
    {
        AssertInconsistent(
            ObjectPropertyAssertion("ge3r", "ge3b", "ge3a"),
            NegativeObjectPropertyAssertion(InverseProperty("ge3r"), "ge3a", "ge3b"));
    }

    /// <summary>GE4: a symmetric role mirrors the asserted edge the negative assertion denies.</summary>
    [TestMethod]
    public void GE4SymmetricMirrorNegativeAssertionInconsistent()
    {
        AssertInconsistent(
            Symmetric("ge4r"),
            ObjectPropertyAssertion("ge4r", "ge4b", "ge4a"),
            NegativeObjectPropertyAssertion(ObjectProperty("ge4r"), "ge4a", "ge4b"));
    }

    /// <summary>GE5: a transitive role composes the asserted path into the edge the negative assertion denies.</summary>
    [TestMethod]
    public void GE5TransitivePathNegativeAssertionInconsistent()
    {
        AssertInconsistent(
            Transitive("ge5r"),
            ObjectPropertyAssertion("ge5r", "ge5a", "ge5c"),
            ObjectPropertyAssertion("ge5r", "ge5c", "ge5b"),
            NegativeObjectPropertyAssertion(ObjectProperty("ge5r"), "ge5a", "ge5b"));
    }

    /// <summary>GE6: a role chain composes the asserted links into the super-role edge the negative assertion denies.</summary>
    [TestMethod]
    public void GE6ChainCompositionNegativeAssertionInconsistent()
    {
        AssertInconsistent(
            Chain(["ge6p", "ge6q"], "ge6r"),
            ObjectPropertyAssertion("ge6p", "ge6a", "ge6c"),
            ObjectPropertyAssertion("ge6q", "ge6c", "ge6b"),
            NegativeObjectPropertyAssertion(ObjectProperty("ge6r"), "ge6a", "ge6b"));
    }

    /// <summary>GE7: a negative assertion on a pair the closure does not reach is consistent.</summary>
    [TestMethod]
    public void GE7NegativeAssertionDifferentPairConsistent()
    {
        AssertConsistent(
            ObjectPropertyAssertion("ge7r", "ge7a", "ge7c"),
            NegativeObjectPropertyAssertion(ObjectProperty("ge7r"), "ge7a", "ge7b"));
    }

    /// <summary>GE8: a chain whose middle nodes do not meet composes no super-role edge, so the negative assertion is consistent.</summary>
    [TestMethod]
    public void GE8BrokenChainConsistent()
    {
        AssertConsistent(
            Chain(["ge8p", "ge8q"], "ge8r"),
            ObjectPropertyAssertion("ge8p", "ge8a", "ge8c"),
            ObjectPropertyAssertion("ge8q", "ge8d", "ge8b"),
            NegativeObjectPropertyAssertion(ObjectProperty("ge8r"), "ge8a", "ge8b"));
    }

    /// <summary>GE9: an asymmetric role carrying a 2-cycle clashes.</summary>
    [TestMethod]
    public void GE9AsymmetricTwoCycleInconsistent()
    {
        AssertInconsistent(
            Asymmetric("ge9r"),
            ObjectPropertyAssertion("ge9r", "ge9a", "ge9b"),
            ObjectPropertyAssertion("ge9r", "ge9b", "ge9a"));
    }

    /// <summary>GE10: an asymmetric role carrying a self-loop clashes (asymmetry entails irreflexivity).</summary>
    [TestMethod]
    public void GE10AsymmetricDiagonalInconsistent()
    {
        AssertInconsistent(
            Asymmetric("ge10r"),
            ObjectPropertyAssertion("ge10r", "ge10a", "ge10a"));
    }

    /// <summary>GE11: an irreflexive role carrying an asserted self-loop clashes.</summary>
    [TestMethod]
    public void GE11IrreflexiveSelfLoopInconsistent()
    {
        AssertInconsistent(
            Irreflexive("ge11r"),
            ObjectPropertyAssertion("ge11r", "ge11a", "ge11a"));
    }

    /// <summary>GE12: a reflexive sub-role's loop lifts to its irreflexive super-role and clashes.</summary>
    [TestMethod]
    public void GE12ReflexiveLoopLiftedMeetsIrreflexiveInconsistent()
    {
        AssertInconsistent(
            ClassAssertion(Reference("Ge12C"), Individual("ge12a")),
            Reflexive("ge12s"),
            SubObjectPropertyOf("ge12s", "ge12r"),
            Irreflexive("ge12r"));
    }

    /// <summary>GE13: two disjoint roles carrying the same asserted edge clash.</summary>
    [TestMethod]
    public void GE13DisjointParallelEdgesInconsistent()
    {
        AssertInconsistent(
            DisjointRoles("ge13r", "ge13s"),
            ObjectPropertyAssertion("ge13r", "ge13a", "ge13b"),
            ObjectPropertyAssertion("ge13s", "ge13a", "ge13b"));
    }

    /// <summary>GE14: a sub-role edge lifts to a disjoint role already carrying the pair and clashes.</summary>
    [TestMethod]
    public void GE14DisjointViaSubRoleInconsistent()
    {
        AssertInconsistent(
            DisjointRoles("ge14r", "ge14s"),
            ObjectPropertyAssertion("ge14t", "ge14a", "ge14b"),
            SubObjectPropertyOf("ge14t", "ge14r"),
            ObjectPropertyAssertion("ge14s", "ge14a", "ge14b"));
    }

    /// <summary>GE15: a Self restriction entails a loop the negative self-assertion denies (the ghost pass).</summary>
    [TestMethod]
    public void GE15SelfLoopMeetsNegativeSelfAssertionInconsistent()
    {
        AssertInconsistent(
            ClassAssertion(HasSelf("ge15r"), Individual("ge15a")),
            NegativeObjectPropertyAssertion(ObjectProperty("ge15r"), "ge15a", "ge15a"));
    }

    /// <summary>GE16: a Self restriction's loop meets an irreflexive role's ghost-consumer clause in the ground context.</summary>
    [TestMethod]
    public void GE16SelfLoopMeetsIrreflexiveInconsistent()
    {
        AssertInconsistent(
            ClassAssertion(HasSelf("ge16r"), Individual("ge16a")),
            Irreflexive("ge16r"));
    }

    /// <summary>
    /// GE17: a role asserted both reflexive and irreflexive over an individual is
    /// inconsistent — the reflexive characteristic seeds the per-representative self-loop
    /// the ground closure's irreflexivity check condemns. The pre-saturation ground closure
    /// — built and clash-checked at clausification, BEFORE the saturation engine exists —
    /// decides the clash budget-independently, so a <c>MaxInferences=1</c> budget still
    /// returns <see cref="ReasoningDecisionOutcome.Decided"/> inconsistent rather than a
    /// budget abstention. Irreflexivity has no concept-level or Self-ghost-pass
    /// representation — it is a ground-closure-only check — so no other budget-independent
    /// path substitutes for the reflexive-loop seeding, and this row pins that seeding
    /// specifically: dropping it flips the budgeted decision to a budget abstention. The
    /// unbounded companion reaches the same inconsistency, self-documenting both faces.
    /// </summary>
    [TestMethod]
    public void GE17ReflexiveIrreflexiveDecidedUnderTinyBudget()
    {
        ReasoningModule module = Module(
            ClassAssertion(Reference("Ge17C"), Individual("ge17a")),
            Reflexive("ge17r"),
            Irreflexive("ge17r"));

        ModuleDecision budgeted = ContextSaturationModuleReasoner.DecideModule(module, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1), progressSampler: null, TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, budgeted.Outcome, "The pre-saturation ground closure decides the reflexive/irreflexive clash before the engine exists, budget-independently.");
        Assert.IsFalse(budgeted.Verdict!.IsConsistent, "A role both reflexive and irreflexive over an individual is inconsistent.");

        ModuleDecision unbounded = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsFalse(unbounded.Verdict!.IsConsistent, "The unbounded decision reaches the same inconsistency.");
    }

    /// <summary>
    /// GE18: an asserted self-loop on a role mutually included with an IRREFLEXIVE
    /// one is inconsistent — the ground closure lifts the raw loop onto every
    /// spelling of the mutual class, and the irreflexivity check condemns it there.
    /// The closure is built and clash-checked at clausification, BEFORE the
    /// saturation engine exists, so a <c>MaxInferences=1</c> budget still returns
    /// <see cref="ReasoningDecisionOutcome.Decided"/> inconsistent rather than a
    /// budget abstention. The row pins the graph's raw-space uniformity under an
    /// equivalence: the edge is told under one spelling and the obligation carried
    /// under the other, and only the closure's lift joins them. The clause space
    /// folds Self atoms across the same quotient, so the unbounded companion has a
    /// second path to the clash — but under the tiny budget no saturation path can
    /// decide, and that is what isolates the graph path here.
    /// </summary>
    [TestMethod]
    public void GE18AssertedSelfLoopViolatesIrreflexivityAcrossEquivalence()
    {
        ReasoningModule module = Module(
            ObjectPropertyAssertion("ge18p", "ge18a", "ge18a"),
            EquivalentRoles("ge18p", "ge18q"),
            Irreflexive("ge18q"));

        ModuleDecision budgeted = ContextSaturationModuleReasoner.DecideModule(module, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1), progressSampler: null, TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, budgeted.Outcome, "The pre-saturation ground closure lifts the loop across the mutual class and decides the clash before the engine exists.");
        Assert.IsFalse(budgeted.Verdict!.IsConsistent, "An asserted loop on a role equivalent to an irreflexive one is inconsistent.");

        ModuleDecision unbounded = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsFalse(unbounded.Verdict!.IsConsistent, "The unbounded decision reaches the same inconsistency.");
    }

    /// <summary>
    /// GE19: a Self restriction's derived loop meets a negative self-assertion
    /// denying the loop under an EQUIVALENT spelling. The denied edge lives only in
    /// the graph's negative obligations, and the derived loop reaches it only
    /// through the ghost pass writing the loop concept's representative into the raw
    /// graph under its raw member and the re-closure lifting it across the mutual
    /// class — so the row pins that cross-space write end to end, from the derived
    /// Self atom to the raw obligation. Without the equivalence the same module is
    /// consistent; the asymmetry is the pin.
    /// </summary>
    [TestMethod]
    public void GE19GhostSelfLoopEntailsDeniedEdgeAcrossEquivalence()
    {
        AssertInconsistent(
            ClassAssertion(HasSelf("ge19p"), Individual("ge19a")),
            EquivalentRoles("ge19p", "ge19q"),
            NegativeObjectPropertyAssertion(ObjectProperty("ge19q"), "ge19a", "ge19a"));
    }

    /// <summary>B3: a Self ghost loop recomposes with an asserted edge under a chain into the super-role edge the negative assertion denies — the full-closure re-run.</summary>
    [TestMethod]
    public void B3GhostChainRecompositionInconsistent()
    {
        AssertInconsistent(
            Chain(["b3p", "b3q"], "b3r"),
            ClassAssertion(HasSelf("b3p"), Individual("b3a")),
            ObjectPropertyAssertion("b3q", "b3a", "b3b"),
            NegativeObjectPropertyAssertion(ObjectProperty("b3r"), "b3a", "b3b"));
    }

    /// <summary>B3t: the ghost chain recomposes to a pair the negative assertion does not deny, so it is consistent.</summary>
    [TestMethod]
    public void B3tGhostChainConsistentTwin()
    {
        AssertConsistent(
            Chain(["b3tp", "b3tq"], "b3tr"),
            ClassAssertion(HasSelf("b3tp"), Individual("b3ta")),
            ObjectPropertyAssertion("b3tq", "b3ta", "b3tb"),
            NegativeObjectPropertyAssertion(ObjectProperty("b3tr"), "b3ta", "b3tc"));
    }

    //§5.3 — Merge rows (pre-merge union-find).

    /// <summary>GM1: a same-different collision on one pair is decided inconsistent at pre-merge.</summary>
    [TestMethod]
    public void GM1SameDifferentCollisionInconsistent()
    {
        AssertInconsistent(
            Same("gm1a", "gm1b"),
            Different("gm1a", "gm1b"));
    }

    /// <summary>GM2: a merge chain collides transitively with a different assertion at pre-merge.</summary>
    [TestMethod]
    public void GM2MergeChainCollisionInconsistent()
    {
        AssertInconsistent(
            Same("gm2a", "gm2b"),
            Same("gm2b", "gm2c"),
            Different("gm2a", "gm2c"));
    }

    /// <summary>GM3: a same-individual merge rewires an incoming edge onto the shared representative, colliding a range concept with a disjoint class assertion.</summary>
    [TestMethod]
    public void GM3MergeCreatedEdgeInconsistent()
    {
        AssertInconsistent(
            Same("gm3a", "gm3b"),
            ObjectPropertyAssertion("gm3r", "gm3c", "gm3a"),
            Range("gm3r", Reference("Gm3D")),
            ClassAssertion(Reference("Gm3E"), Individual("gm3b")),
            Disjoint("Gm3D", "Gm3E"));
    }

    /// <summary>GM4: a degenerate different-individuals over a repeated term collides with itself at pre-merge.</summary>
    [TestMethod]
    public void GM4RepeatedTermCollisionInconsistent()
    {
        AssertInconsistent(Different("gm4a", "gm4a"));
    }

    /// <summary>GM5: the QL shape — a wide n-ary different-individuals with no collision — is consistent.</summary>
    [TestMethod]
    public void GM5WideDifferentIndividualsConsistent()
    {
        List<OwlAxiom> axioms = [Different(
            "gm5i01", "gm5i02", "gm5i03", "gm5i04", "gm5i05", "gm5i06", "gm5i07",
            "gm5i08", "gm5i09", "gm5i10", "gm5i11", "gm5i12", "gm5i13", "gm5i14", "gm5i15", "gm5i16",
            "gm5i17", "gm5i18", "gm5i19", "gm5i20", "gm5i21", "gm5i22", "gm5i23", "gm5i24", "gm5i25",
            "gm5i26", "gm5i27", "gm5i28"),
            ClassAssertion(Reference("Gm5A"), Individual("gm5i01"))];

        AssertConsistent([.. axioms]);
    }

    /// <summary>GM6: the merge-created-edge shape without the disjointness is consistent.</summary>
    [TestMethod]
    public void GM6MergeCreatedEdgeConsistentTwin()
    {
        AssertConsistent(
            Same("gm6a", "gm6b"),
            ObjectPropertyAssertion("gm6r", "gm6c", "gm6a"),
            Range("gm6r", Reference("Gm6D")),
            ClassAssertion(Reference("Gm6E"), Individual("gm6b")));
    }

    /// <summary>
    /// GM7: two same-individual trees — <c>gm7b</c> under <c>gm7a</c>, <c>gm7d</c> under
    /// <c>gm7c</c> — merge through the bridging pair <c>gm7b=gm7d</c>, attaching the second
    /// tree's root <c>gm7c</c> under <c>gm7a</c> and leaving the depth-2 merge chain
    /// <c>gm7d -&gt; gm7c -&gt; gm7a</c>. The different assertion collides only when the
    /// representative resolution walks that chain to its root: a Find that stops at the
    /// direct parent resolves <c>gm7d</c> to <c>gm7c</c> and misses the collision with
    /// <c>gm7a</c>. The depth-2 chain is the two-tree-union discriminator.
    /// </summary>
    [TestMethod]
    public void GM7MergeChainAcrossTwoTreesInconsistent()
    {
        AssertInconsistent(
            Same("gm7a", "gm7b"),
            Same("gm7c", "gm7d"),
            Same("gm7b", "gm7d"),
            Different("gm7a", "gm7d"));
    }

    //§5.4 — Cycle / self-edge mechanics.

    /// <summary>GC1: a 2-cycle flows a range concept back onto the source over the incoming edge, colliding with a disjoint class assertion.</summary>
    [TestMethod]
    public void GC1TwoCycleRangeFlowInconsistent()
    {
        AssertInconsistent(
            ClassAssertion(Reference("Gc1C"), Individual("gc1a")),
            ObjectPropertyAssertion("gc1r", "gc1a", "gc1b"),
            ObjectPropertyAssertion("gc1r", "gc1b", "gc1a"),
            Range("gc1r", Reference("Gc1D")),
            Disjoint("Gc1C", "Gc1D"));
    }

    /// <summary>GC2: a self-edge flows a range concept within one ground context, colliding with a disjoint class assertion.</summary>
    [TestMethod]
    public void GC2SelfEdgeRangeFlowInconsistent()
    {
        AssertInconsistent(
            ClassAssertion(Reference("Gc2C"), Individual("gc2a")),
            ObjectPropertyAssertion("gc2r", "gc2a", "gc2a"),
            Range("gc2r", Reference("Gc2D")),
            Disjoint("Gc2C", "Gc2D"));
    }

    /// <summary>GC3: a 2-cycle with a range but no disjointness terminates and is consistent (the cycle-termination pin).</summary>
    [TestMethod]
    public void GC3CycleTerminatesConsistent()
    {
        AssertConsistent(
            ObjectPropertyAssertion("gc3r", "gc3a", "gc3b"),
            ObjectPropertyAssertion("gc3r", "gc3b", "gc3a"),
            Range("gc3r", Reference("Gc3D")));
    }

    //§5.5 — Honesty rows (Delegated + named remainder) + counting family.

    /// <summary>GH1: a functional role carrying two asserted edges over a distinct pair is out of the slice (counting-times-edge), so the module delegates and the honest fallback answers fragment-relative.</summary>
    [TestMethod]
    public void GH1FunctionalTimesEdgeDelegates()
    {
        AssertDelegatedFragmentRelative(
            Functional("gh1r"),
            ObjectPropertyAssertion("gh1r", "gh1a", "gh1b"),
            ObjectPropertyAssertion("gh1r", "gh1a", "gh1c"),
            Different("gh1b", "gh1c"));
    }

    /// <summary>GH2: a role reaching a max-cardinality super-role and carrying an asserted edge is out of the slice (laundering), so the module delegates.</summary>
    [TestMethod]
    public void GH2MaxCardinalityLaunderingDelegates()
    {
        AssertDelegatedFragmentRelative(
            SubClassOf(Reference("Gh2A"), Max("gh2s", 1, Thing)),
            SubObjectPropertyOf("gh2r", "gh2s"),
            ObjectPropertyAssertion("gh2r", "gh2a", "gh2b"),
            ClassAssertion(Reference("Gh2A"), Individual("gh2a")));
    }

    /// <summary>B5: an exact-cardinality carrier with an asserted edge on its counting role is out of the slice (exact in the counting family), so the module delegates.</summary>
    [TestMethod]
    public void B5ExactCardinalityTimesEdgeDelegates()
    {
        AssertDelegatedFragmentRelative(
            ClassAssertion(Exact("b5r", 1, Thing), Individual("b5a")),
            ObjectPropertyAssertion("b5r", "b5a", "b5b"));
    }

    /// <summary>GC4: the told bound sits on one role and BOTH successor edges are asserted on its mutually included alias — the closure lifts the alias edges onto the counted role, so the told-counting constraint reads two told-distinct closed successors under a bound of one and the ground pigeonhole decides inconsistent. The row pins that the raw-recorded counted role queries the closure's mutual-class lift: assert on the alias, count on the property.</summary>
    [TestMethod]
    public void GC4CountingPigeonholeFiresAcrossEquivalentRoleSpelling()
    {
        AssertInconsistent(
            EquivalentRoles("gc4p", "gc4q"),
            ClassAssertion(Max("gc4p", 1, Thing), Individual("gc4a")),
            ObjectPropertyAssertion("gc4q", "gc4a", "gc4b"),
            ObjectPropertyAssertion("gc4q", "gc4a", "gc4c"),
            Different("gc4b", "gc4c"));
    }

    /// <summary>GC5: the other direction of the same lift — the told bound sits on the alias and the successor edges on the role it is mutually included with — so the closed edges reach the counted spelling from the opposite side and the same pigeonhole decides inconsistent.</summary>
    [TestMethod]
    public void GC5CountingPigeonholeFiresWhenCountedRoleIsTheAlias()
    {
        AssertInconsistent(
            EquivalentRoles("gc5p", "gc5q"),
            ClassAssertion(Max("gc5q", 1, Thing), Individual("gc5a")),
            ObjectPropertyAssertion("gc5p", "gc5a", "gc5b"),
            ObjectPropertyAssertion("gc5p", "gc5a", "gc5c"),
            Different("gc5b", "gc5c"));
    }

    /// <summary>GC6: the negative control — the two told-distinct successors ride a role UNRELATED to the counted one, which no inclusion lifts onto it, so the counted role has no closed successor, no clique is searched, and the module decides consistent. The lift is exactly the mutual-inclusion class and no wider.</summary>
    [TestMethod]
    public void GC6CountingPigeonholeUnrelatedRoleStaysConsistent()
    {
        AssertConsistent(
            ClassAssertion(Max("gc6p", 1, Thing), Individual("gc6a")),
            ObjectPropertyAssertion("gc6r", "gc6a", "gc6b"),
            ObjectPropertyAssertion("gc6r", "gc6a", "gc6c"),
            Different("gc6b", "gc6c"));
    }

    /// <summary>GH3: a reserved-role object-property assertion delegates on the reserved remainder.</summary>
    [TestMethod]
    public void GH3ReservedObjectPropertyAssertionDelegates()
    {
        AssertDelegatedFragmentRelative(
            ObjectPropertyAssertion(OwlVocabulary.TopObjectProperty, "gh3a", "gh3b"));
    }

    /// <summary>GH4: a reserved-role negative object-property assertion delegates on the reserved remainder.</summary>
    [TestMethod]
    public void GH4ReservedNegativeObjectPropertyAssertionDelegates()
    {
        AssertDelegatedFragmentRelative(
            NegativeObjectPropertyAssertion(new OwlObjectPropertyReference(new NamedNode(OwlVocabulary.BottomObjectProperty)), "gh4a", "gh4b"));
    }

    /// <summary>GH5: a reserved-role assertion co-occurring with a same-different collision delegates from the context arm rather than deciding at pre-merge (reserved-scan-wins); the S3-fixed fallback then decides the collision inconsistent decisively — the row's ground truth.</summary>
    [TestMethod]
    public void GH5ReservedScanWinsOverCollisionDelegates()
    {
        AssertDelegatedInconsistent(
            ObjectPropertyAssertion(OwlVocabulary.TopObjectProperty, "gh5a", "gh5b"),
            Same("gh5c", "gh5d"),
            Different("gh5c", "gh5d"));
    }

    /// <summary>GH6: a literal in an individual position is malformed for the slice, so the module delegates on the literal remainder.</summary>
    [TestMethod]
    public void GH6LiteralIndividualDelegates()
    {
        AssertDelegatedFragmentRelative(
            new OwlClassAssertionAxiom(Reference("Gh6A"), IntegerLiteral(5)) { Origin = Origin("gh6") });
    }

    /// <summary>GH7: a bare data-property assertion is a ground key-value fact within the slice — no key axiom consumes it and the key-scoped belt finds no entanglement — so the module decides whole and consistent (the HasKey ground S2 intake).</summary>
    [TestMethod]
    public void GH7DataPropertyAssertionDecidesConsistent()
    {
        AssertConsistent(
            new OwlDataPropertyAssertionAxiom(Individual("gh7a"), new NamedNode(Iri("gh7d")), IntegerLiteral(5)) { Origin = Origin("gh7") });
    }

    /// <summary>GH8: an admitted key module with a single typed holder decides whole and consistent — the ground key join finds no pair to force (the HasKey ground S8 admission).</summary>
    [TestMethod]
    public void GH8HasKeySingleHolderDecidesConsistent()
    {
        AssertConsistent(
            new OwlHasKeyAxiom(Reference("Gh8A"), [ObjectProperty("gh8r")], []) { Origin = Origin("gh8") },
            ClassAssertion(Reference("Gh8A"), Individual("gh8a")));
    }

    //§9b — Reserved roles inside class-expression positions of ABox and data axioms (F1) and
    //ground self-loop entailment of the self restriction (F2). The pointwise-constant reserved
    //shapes fold at the front door (ReservedVocabularyFold) to owl:Thing / owl:Nothing, so the
    //arm now decides them rather than delegating on a reserved remainder.

    /// <summary>FW7: a class assertion whose class is an existential over the empty reserved role folds to owl:Nothing, so the individual is typed the empty class and the module decides INCONSISTENT.</summary>
    [TestMethod]
    public void FW7ReservedBottomExistentialInClassAssertionDecidesInconsistent()
    {
        AssertInconsistent(
            ClassAssertion(new OwlObjectSomeValuesFrom(new OwlObjectPropertyReference(new NamedNode(OwlVocabulary.BottomObjectProperty)), Thing), Individual("fw7a")));
    }

    /// <summary>FW8: a class assertion whose class complements an existential over the universal reserved role folds to the complement of owl:Thing, so the module decides INCONSISTENT.</summary>
    [TestMethod]
    public void FW8ReservedTopUnderComplementInClassAssertionDecidesInconsistent()
    {
        AssertInconsistent(
            ClassAssertion(new OwlObjectComplementOf(new OwlObjectSomeValuesFrom(new OwlObjectPropertyReference(new NamedNode(OwlVocabulary.TopObjectProperty)), Thing)), Individual("fw8a")));
    }

    /// <summary>FD1: a class assertion whose class is a universal over the empty reserved role folds to owl:Thing, a vacuous assertion, so the module decides CONSISTENT.</summary>
    [TestMethod]
    public void FD1ReservedBottomUniversalInClassAssertionDecidesConsistent()
    {
        AssertConsistent(
            ClassAssertion(new OwlObjectAllValuesFrom(new OwlObjectPropertyReference(new NamedNode(OwlVocabulary.BottomObjectProperty)), Reference("Fd1C")), Individual("fd1a")));
    }

    /// <summary>FE1: a data-property domain whose domain class is an existential over the empty reserved role folds to owl:Nothing; with no asserted value the domain has no source, so the module decides CONSISTENT.</summary>
    [TestMethod]
    public void FE1ReservedBottomInDataPropertyDomainDecidesConsistent()
    {
        AssertConsistent(
            new OwlDataPropertyDomainAxiom(new NamedNode(Iri("fe1d")), new OwlObjectSomeValuesFrom(new OwlObjectPropertyReference(new NamedNode(OwlVocabulary.BottomObjectProperty)), Thing)) { Origin = Origin("fe1") });
    }

    /// <summary>FE2: the data-domain folds to owl:Nothing, so an asserted value on that data property types its source the empty domain class and the module decides INCONSISTENT.</summary>
    [TestMethod]
    public void FE2ReservedDataDomainWithAssertedValueDecidesInconsistent()
    {
        AssertInconsistent(
            new OwlDataPropertyDomainAxiom(new NamedNode(Iri("fe2d")), new OwlObjectSomeValuesFrom(new OwlObjectPropertyReference(new NamedNode(OwlVocabulary.BottomObjectProperty)), Thing)) { Origin = Origin("fe2") },
            new OwlDataPropertyAssertionAxiom(Individual("fe2a"), new NamedNode(Iri("fe2d")), IntegerLiteral(5)) { Origin = Origin("fe2v") });
    }

    /// <summary>FS1: an asserted self-loop entails the self restriction, so its complement on the same individual clashes in the ground context.</summary>
    [TestMethod]
    public void FS1SelfLoopEntailsSelfRestrictionInconsistent()
    {
        AssertInconsistent(
            ObjectPropertyAssertion("fs1p", "fs1a", "fs1a"),
            ClassAssertion(new OwlObjectComplementOf(HasSelf("fs1p")), Individual("fs1a")));
    }

    /// <summary>FS2: the seeded self restriction feeds its consumer, so denying the consumer's conclusion clashes.</summary>
    [TestMethod]
    public void FS2SelfLoopFeedsConsumerInconsistent()
    {
        AssertInconsistent(
            ObjectPropertyAssertion("fs2r", "fs2a", "fs2a"),
            SubClassOf(HasSelf("fs2r"), Reference("Fs2B")),
            ClassAssertion(new OwlObjectComplementOf(Reference("Fs2B")), Individual("fs2a")));
    }

    /// <summary>FS3: the closure lifts the loop through the hierarchy, so the super-role's self restriction is entailed too.</summary>
    [TestMethod]
    public void FS3SelfLoopLiftedThroughHierarchyInconsistent()
    {
        AssertInconsistent(
            ObjectPropertyAssertion("fs3r", "fs3a", "fs3a"),
            SubObjectPropertyOf("fs3r", "fs3s"),
            SubClassOf(HasSelf("fs3s"), Reference("Fs3B")),
            ClassAssertion(new OwlObjectComplementOf(Reference("Fs3B")), Individual("fs3a")));
    }

    /// <summary>FS4: a pre-merge equality collapses an asserted edge into a self-loop, which then entails the self restriction.</summary>
    [TestMethod]
    public void FS4MergeCreatedSelfLoopInconsistent()
    {
        AssertInconsistent(
            Same("fs4a", "fs4b"),
            ObjectPropertyAssertion("fs4r", "fs4a", "fs4b"),
            SubClassOf(HasSelf("fs4r"), Reference("Fs4B")),
            ClassAssertion(new OwlObjectComplementOf(Reference("Fs4B")), Individual("fs4a")));
    }

    /// <summary>FS5: a plain edge between distinct individuals seeds no loop, so the consumer's conclusion is not entailed and the module stays consistent.</summary>
    [TestMethod]
    public void FS5NoSelfLoopNoEntailmentConsistent()
    {
        AssertConsistent(
            ObjectPropertyAssertion("fs5r", "fs5a", "fs5b"),
            SubClassOf(HasSelf("fs5r"), Reference("Fs5B")),
            ClassAssertion(new OwlObjectComplementOf(Reference("Fs5B")), Individual("fs5a")));
    }

    //Assertion helpers.

    /// <summary>Asserts the module delegates from the context arm and the fallback stays non-decisive, whatever consistency bit it reports — the reserved-scan face for a module the fallback cannot decide whole either.</summary>
    /// <param name="axioms">The module axioms.</param>
    private void AssertDelegatedNonDecisive(params OwlAxiom[] axioms)
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Module(axioms), TestContext.CancellationToken);
        Assert.IsFalse(decision.Statistics.ContextTotals.ContextDecided, "The module must delegate: the context arm must not decide what the reserved scan forecloses.");
        Assert.IsFalse(decision.Verdict!.IsDecisive, "Neither arm reaches a decisive verdict on the reserved mention.");
    }

    /// <summary>Asserts the module is context-decided and consistent.</summary>
    /// <param name="axioms">The module axioms.</param>
    private void AssertConsistent(params OwlAxiom[] axioms)
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Module(axioms), TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "A consistent ground-slice row must be context-decided, not delegated.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The row is consistent.");
    }

    /// <summary>Asserts the module is context-decided and inconsistent.</summary>
    /// <param name="axioms">The module axioms.</param>
    private void AssertInconsistent(params OwlAxiom[] axioms)
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Module(axioms), TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "An inconsistent ground-slice row must be context-decided, not delegated.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The row is inconsistent.");
    }

    /// <summary>
    /// Asserts the module is context-decided and inconsistent, and probes the
    /// internal seam that the bottom is non-global — absent from the trivial context
    /// — and lands in the named target individual's ground context (the MU3
    /// discriminator).
    /// </summary>
    /// <param name="targetIndividual">The individual whose ground context must carry the bottom.</param>
    /// <param name="axioms">The module axioms.</param>
    private void AssertBottomInGroundContext(string targetIndividual, params OwlAxiom[] axioms)
    {
        ReasoningModule module = Module(axioms);
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The row must be context-decided, not delegated.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The row is inconsistent.");

        ClausificationResult clausification = ContextClausifier.Clausify(module);
        ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification);
        engine.Saturate(ReasoningBudget.Unbounded, TestContext.CancellationToken);
        engine.RunGroundGhostPass();

        Assert.IsTrue(engine.IsInconsistent, "The saturated structure is inconsistent.");
        Assert.IsFalse(engine.TrivialContextHasEmptyClause, "The bottom is b-local, not a global trivial-context collapse (the MU3 discriminator).");
        int marker = clausification.GroundMarkers[Iri(targetIndividual)];
        Assert.IsTrue(engine.GroundContextHasEmptyClause(marker), "The bottom lands in the target individual's ground context.");
    }

    /// <summary>Asserts the module falls outside the slice and delegates, and the honest fallback answers fragment-relative over a named unsupported remainder.</summary>
    /// <param name="axioms">The module axioms.</param>
    private void AssertDelegatedFragmentRelative(params OwlAxiom[] axioms)
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Module(axioms), TestContext.CancellationToken);
        Assert.IsFalse(decision.Statistics.ContextTotals.ContextDecided, "The module must delegate: the context arm must not claim a fragment-relative verdict.");
        Assert.AreEqual(ReasoningDecisionOutcome.DecidedFragmentRelative, decision.Outcome, "The honest fallback answers fragment-relative, never a decisive verdict it cannot soundly reach.");
        Assert.IsNotEmpty(decision.Verdict!.UnsupportedConstructs, "The unsupported construct is named, never silently dropped.");
    }

    /// <summary>Asserts the module falls outside the slice and delegates, and the fallback decides it consistent (a construct the fallback handles decisively).</summary>
    /// <param name="axioms">The module axioms.</param>
    private void AssertDelegatedConsistent(params OwlAxiom[] axioms)
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Module(axioms), TestContext.CancellationToken);
        Assert.IsFalse(decision.Statistics.ContextTotals.ContextDecided, "The module must delegate: the context arm must not claim a fragment-relative verdict.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, "The delegated fallback decides the module consistent.");
    }

    /// <summary>
    /// Asserts the module falls outside the slice and delegates, and the fallback
    /// decides it inconsistent decisively. The S3 fallback fix seeds the ALC bottom
    /// on a <c>SameIndividual</c>/<c>DifferentIndividuals</c> representative collision,
    /// so a delegated module carrying such a collision is condemned whole rather than
    /// answered fragment-relative-consistent.
    /// </summary>
    /// <param name="axioms">The module axioms.</param>
    private void AssertDelegatedInconsistent(params OwlAxiom[] axioms)
    {
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(Module(axioms), TestContext.CancellationToken);
        Assert.IsFalse(decision.Statistics.ContextTotals.ContextDecided, "The module must delegate: the context arm must not decide it.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, "The delegated fallback decides the collision inconsistent.");
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "An inconsistency condemns the whole module, so the delegated verdict is decisive.");
    }

    //Module and axiom builders.

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>The full IRI of an example-namespace local name.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The IRI.</returns>
    private static Utf8String Iri(string local)
    {
        return Utf8Strings.From(Example + local);
    }

    /// <summary>A distinct origin quad for the marker name.</summary>
    /// <param name="marker">The distinguishing marker.</param>
    /// <returns>The origin quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(new NamedNode(Iri(marker)), new NamedNode(Iri("p")), new NamedNode(Iri("o")), Graph: null);
    }

    /// <summary>A named-class reference in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The reference.</returns>
    private static OwlClassReference Reference(string local)
    {
        return new OwlClassReference(new NamedNode(Iri(local)));
    }

    /// <summary>The <c>owl:Thing</c> reference.</summary>
    private static OwlClassReference Thing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing")));

    /// <summary>The <c>owl:Nothing</c> reference.</summary>
    private static OwlClassReference Nothing { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Nothing")));

    /// <summary>A named object property expression in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property expression.</returns>
    private static OwlObjectPropertyReference ObjectProperty(string local)
    {
        return new OwlObjectPropertyReference(new NamedNode(Iri(local)));
    }

    /// <summary>The inverse of a named object property in the example namespace.</summary>
    /// <param name="local">The forward role's local name.</param>
    /// <returns>The inverse property expression.</returns>
    private static OwlInverseObjectProperty InverseProperty(string local)
    {
        return new OwlInverseObjectProperty(new NamedNode(Iri(local)));
    }

    /// <summary>A named individual in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The node.</returns>
    private static NamedNode Individual(string local)
    {
        return new NamedNode(Iri(local));
    }

    /// <summary>An existential restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(ObjectProperty(property), filler);
    }

    /// <summary>A universal restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectAllValuesFrom All(string property, OwlClassExpression filler)
    {
        return new OwlObjectAllValuesFrom(ObjectProperty(property), filler);
    }

    /// <summary>A self-restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasSelf HasSelf(string property)
    {
        return new OwlObjectHasSelf(ObjectProperty(property));
    }

    /// <summary>A qualified maximum-cardinality restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The maximum count.</param>
    /// <param name="filler">The qualifying filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Max(string property, int cardinality, OwlClassExpression filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, ObjectProperty(property), filler);
    }

    /// <summary>A qualified exact-cardinality restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="cardinality">The exact count.</param>
    /// <param name="filler">The qualifying filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectCardinality Exact(string property, int cardinality, OwlClassExpression filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Exact, cardinality, ObjectProperty(property), filler);
    }

    /// <summary>A subclass inclusion.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>A pairwise class disjointness axiom over two named classes.</summary>
    /// <param name="first">The first class local name.</param>
    /// <param name="second">The second class local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointClassesAxiom Disjoint(string first, string second)
    {
        return new OwlDisjointClassesAxiom([Reference(first), Reference(second)]) { Origin = Origin("disjoint") };
    }

    /// <summary>An object-property domain axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="domain">The domain class expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyDomainAxiom Domain(string property, OwlClassExpression domain)
    {
        return new OwlObjectPropertyDomainAxiom(ObjectProperty(property), domain) { Origin = Origin("domain") };
    }

    /// <summary>An object-property range axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="range">The range class expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyRangeAxiom Range(string property, OwlClassExpression range)
    {
        return new OwlObjectPropertyRangeAxiom(ObjectProperty(property), range) { Origin = Origin("range") };
    }

    /// <summary>A sub-object-property axiom over two named roles.</summary>
    /// <param name="sub">The sub-role local name.</param>
    /// <param name="super">The super-role local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubObjectPropertyOfAxiom SubObjectPropertyOf(string sub, string super)
    {
        return new OwlSubObjectPropertyOfAxiom(ObjectProperty(sub), ObjectProperty(super)) { Origin = Origin("subrole") };
    }

    /// <summary>An equivalent-object-properties axiom over two named roles — the two mutual inclusions the role closure quotients into one class.</summary>
    /// <param name="first">The first role local name.</param>
    /// <param name="second">The second role local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentObjectPropertiesAxiom EquivalentRoles(string first, string second)
    {
        return new OwlEquivalentObjectPropertiesAxiom(ObjectProperty(first), ObjectProperty(second)) { Origin = Origin("equivalentroles") };
    }

    /// <summary>A property-chain axiom over named roles.</summary>
    /// <param name="links">The chain link local names.</param>
    /// <param name="super">The super-role local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlPropertyChainAxiom Chain(string[] links, string super)
    {
        OwlObjectPropertyExpression[] chain = new OwlObjectPropertyExpression[links.Length];
        for(int i = 0; i < links.Length; i++)
        {
            chain[i] = ObjectProperty(links[i]);
        }

        return new OwlPropertyChainAxiom(chain, ObjectProperty(super)) { Origin = Origin("chain") };
    }

    /// <summary>A disjoint-object-properties axiom over two named roles.</summary>
    /// <param name="first">The first role local name.</param>
    /// <param name="second">The second role local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointObjectPropertiesAxiom DisjointRoles(string first, string second)
    {
        return new OwlDisjointObjectPropertiesAxiom([ObjectProperty(first), ObjectProperty(second)]) { Origin = Origin("disjointroles") };
    }

    /// <summary>A symmetric-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Symmetric(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Symmetric, ObjectProperty(property)) { Origin = Origin("symmetric") };
    }

    /// <summary>A transitive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Transitive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Transitive, ObjectProperty(property)) { Origin = Origin("transitive") };
    }

    /// <summary>An asymmetric-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Asymmetric(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Asymmetric, ObjectProperty(property)) { Origin = Origin("asymmetric") };
    }

    /// <summary>A reflexive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Reflexive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Reflexive, ObjectProperty(property)) { Origin = Origin("reflexive") };
    }

    /// <summary>An irreflexive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Irreflexive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Irreflexive, ObjectProperty(property)) { Origin = Origin("irreflexive") };
    }

    /// <summary>A functional-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Functional(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, ObjectProperty(property)) { Origin = Origin("functional") };
    }

    /// <summary>A class assertion.</summary>
    /// <param name="type">The asserted class expression.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, NamedNode individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

    /// <summary>An object-property assertion over two named individuals.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="source">The source individual's local name.</param>
    /// <param name="target">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom ObjectPropertyAssertion(string property, string source, string target)
    {
        return new OwlObjectPropertyAssertionAxiom(Individual(source), new NamedNode(Iri(property)), Individual(target)) { Origin = Origin("edge") };
    }

    /// <summary>An object-property assertion over a reserved role IRI.</summary>
    /// <param name="propertyIri">The role IRI.</param>
    /// <param name="source">The source individual's local name.</param>
    /// <param name="target">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom ObjectPropertyAssertion(Utf8String propertyIri, string source, string target)
    {
        return new OwlObjectPropertyAssertionAxiom(Individual(source), new NamedNode(propertyIri), Individual(target)) { Origin = Origin("edge") };
    }

    /// <summary>A negative object-property assertion over a role expression and two named individuals.</summary>
    /// <param name="property">The denied role expression.</param>
    /// <param name="source">The source individual's local name.</param>
    /// <param name="target">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlNegativeObjectPropertyAssertionAxiom NegativeObjectPropertyAssertion(OwlObjectPropertyExpression property, string source, string target)
    {
        return new OwlNegativeObjectPropertyAssertionAxiom(Individual(source), property, Individual(target)) { Origin = Origin("negativeedge") };
    }

    /// <summary>A same-individual axiom over two named individuals.</summary>
    /// <param name="first">The first individual's local name.</param>
    /// <param name="second">The second individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSameIndividualAxiom Same(string first, string second)
    {
        return new OwlSameIndividualAxiom(Individual(first), Individual(second)) { Origin = Origin("same") };
    }

    /// <summary>A different-individuals axiom over named individuals.</summary>
    /// <param name="individuals">The individuals' local names.</param>
    /// <returns>The axiom.</returns>
    private static OwlDifferentIndividualsAxiom Different(params string[] individuals)
    {
        RdfTerm[] terms = new RdfTerm[individuals.Length];
        for(int i = 0; i < individuals.Length; i++)
        {
            terms[i] = Individual(individuals[i]);
        }

        return new OwlDifferentIndividualsAxiom(terms) { Origin = Origin("different") };
    }

    /// <summary>An <c>xsd:integer</c> typed literal.</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The literal.</returns>
    private static Literal IntegerLiteral(int value)
    {
        return new Literal(Utf8Strings.From(value.ToString(CultureInfo.InvariantCulture)), new NamedNode(Vocabulary.Xsd.Integer));
    }
}
