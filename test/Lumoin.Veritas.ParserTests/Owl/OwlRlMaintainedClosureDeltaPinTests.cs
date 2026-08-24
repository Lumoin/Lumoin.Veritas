using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Hand-derived pins for the maintained OWL 2 RL closure's recorded membership
/// deltas (<see cref="OwlRlMaintainedClosure.AllDelta"/> over base ∪ derived and
/// <see cref="OwlRlMaintainedClosure.DerivedDelta"/> over the derived set). Each
/// pin drives one op and asserts the recorded deltas equal the engine's own
/// snapshot diffs — the served set and the derived set snapshotted before and
/// after the Apply — with the net-fold invariant that the entered and left
/// collections are disjoint. The representative shapes span a plain append, a
/// retract cascade, a small <c>owl:sameAs</c> unmerge, the leave-and-re-enter
/// alternate-derivation survival that must fold to neither side, base demotion
/// and seeded promotion, a falsity-introducing Apply, the empty short-circuit,
/// and the rebuild path that records no deltas. A disagreement between the
/// snapshot diff and the recorded delta is a finding to surface, never an
/// expectation to fit.
/// </summary>
[TestClass]
internal sealed class OwlRlMaintainedClosureDeltaPinTests
{
    /// <summary>The MSTest-supplied per-test context; its token aborts derivation between rounds.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Plain append. Adding an instance typing whose subclass axiom already
    /// stands enters the base fact and its one derivation into the served set,
    /// enters only the derivation into the derived set, and leaves nothing.
    /// </summary>
    /// <remarks>
    /// Base <c>{subClassOf(A,B)}</c>; add <c>type(x,A)</c>. cax-sco derives
    /// <c>type(x,B)</c>. The served set gains both the base fact
    /// <c>type(x,A)</c> and the derivation <c>type(x,B)</c>; the derived set
    /// gains only <c>type(x,B)</c>; neither loses anything.
    /// </remarks>
    [TestMethod]
    public void PlainAppendRecordsEnteredFactsAndNoLeavers()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classB);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);
        EncodedTriple xIsB = OwlRlBatteryHelpers.Triple(x, terms.Type, classB);

        HashSet<EncodedTriple> baseBefore = [aSubB];
        OwlRlMaintainedClosure engine = new(baseBefore, terms, cancellationToken: TestContext.CancellationToken);

        HashSet<EncodedTriple> baseAfter = [aSubB, xIsA];
        ApplyAndAssertDeltas(engine, baseBefore, [xIsA], [], baseAfter);

        Assert.Contains(xIsA, engine.AllDelta.Entered, "The added base fact must enter the served set.");
        Assert.Contains(xIsB, engine.AllDelta.Entered, "The cax-sco derivation must enter the served set.");
        Assert.IsEmpty(engine.AllDelta.Left, "A plain append leaves nothing in the served set.");
        Assert.Contains(xIsB, engine.DerivedDelta.Entered, "The cax-sco derivation must enter the derived set.");
        Assert.DoesNotContain(xIsA, engine.DerivedDelta.Entered, "A base fact never enters the derived set.");
        Assert.IsEmpty(engine.DerivedDelta.Left, "A plain append leaves nothing in the derived set.");
    }

    /// <summary>
    /// Retract cascade. Retracting the <c>owl:TransitiveProperty</c> typing
    /// tears down the composed edge and the five deterministic chain triples;
    /// the served-set leavers are the retracted base fact plus every torn-down
    /// derivation, the derived-set leavers are the derivations alone, and
    /// nothing enters.
    /// </summary>
    /// <remarks>
    /// Base <c>{type(p,TransitiveProperty), p(a,b), p(b,c)}</c> derives
    /// <c>p(a,c)</c> and the five <c>p∘p⊑p</c> chain triples. Retracting the
    /// typing leaves the served set of the retracted base fact
    /// <c>type(p,TransitiveProperty)</c> and the six derivations, and the
    /// derived set of the six derivations; the retracted typing was base, so it
    /// leaves the served set but not the derived set.
    /// </remarks>
    [TestMethod]
    public void RetractCascadeRecordsLeaversAndNoEntrants()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "b");
        TermId c = OwlRlBatteryHelpers.Mint(dictionary, "c");

        EncodedTriple transitive = OwlRlBatteryHelpers.Triple(p, terms.Type, terms.TransitiveProperty);
        EncodedTriple ac = OwlRlBatteryHelpers.Triple(a, p, c);
        TermId chainHead = terms.TransitivityChainNode(p, 0);
        TermId chainTail = terms.TransitivityChainNode(p, 1);
        EncodedTriple chainAxiom = OwlRlBatteryHelpers.Triple(p, terms.PropertyChainAxiom, chainHead);
        EncodedTriple chainTailRest = OwlRlBatteryHelpers.Triple(chainTail, terms.Rest, terms.Nil);

        HashSet<EncodedTriple> baseBefore = [transitive, OwlRlBatteryHelpers.Triple(a, p, b), OwlRlBatteryHelpers.Triple(b, p, c)];
        OwlRlMaintainedClosure engine = new(baseBefore, terms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(ac, engine.Current.Derived, "prp-trp must compose p(a,c) before the retract.");

        HashSet<EncodedTriple> baseAfter = [.. baseBefore];
        baseAfter.Remove(transitive);
        ApplyAndAssertDeltas(engine, baseBefore, [], [transitive], baseAfter);

        Assert.IsEmpty(engine.AllDelta.Entered, "A pure retract cascade enters nothing into the served set.");
        Assert.IsEmpty(engine.DerivedDelta.Entered, "A pure retract cascade enters nothing into the derived set.");
        Assert.Contains(transitive, engine.AllDelta.Left, "The retracted base typing must leave the served set.");
        Assert.Contains(ac, engine.AllDelta.Left, "The composed edge must leave the served set.");
        Assert.Contains(chainAxiom, engine.AllDelta.Left, "The chain axiom head must leave the served set.");
        Assert.Contains(chainTailRest, engine.AllDelta.Left, "The chain tail cell must leave the served set.");
        Assert.Contains(ac, engine.DerivedDelta.Left, "The composed edge must leave the derived set.");
        Assert.Contains(chainTailRest, engine.DerivedDelta.Left, "The chain tail cell must leave the derived set.");
        Assert.DoesNotContain(transitive, engine.DerivedDelta.Left, "The retracted base typing was never in the derived set.");
    }

    /// <summary>
    /// Small <c>owl:sameAs</c> unmerge. Two two-member cliques bridged into one
    /// orbit split when the bridge is retracted; the recorded deltas equal the
    /// engine's snapshot diffs, and the served set loses the cross-clique
    /// congruence facts.
    /// </summary>
    /// <remarks>
    /// Base sameAs <c>{a1≡a2, a2≡b1, b1≡b2}</c> merges <c>{a1,a2,b1,b2}</c> into
    /// one orbit. Retracting the bridge <c>a2≡b1</c> splits it into
    /// <c>{a1,a2}</c> and <c>{b1,b2}</c>; every cross-clique <c>owl:sameAs</c>
    /// pair leaves the served set, and the recorded deltas are exactly the
    /// engine's before/after snapshot diffs.
    /// </remarks>
    [TestMethod]
    public void SameAsUnmergeRecordsDeltasEqualToSnapshotDiff()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a1 = OwlRlBatteryHelpers.Mint(dictionary, "a1");
        TermId a2 = OwlRlBatteryHelpers.Mint(dictionary, "a2");
        TermId b1 = OwlRlBatteryHelpers.Mint(dictionary, "b1");
        TermId b2 = OwlRlBatteryHelpers.Mint(dictionary, "b2");

        EncodedTriple Same(TermId left, TermId right) => OwlRlBatteryHelpers.Triple(left, terms.SameAs, right);
        EncodedTriple bridge = Same(a2, b1);
        EncodedTriple crossPair = Same(a1, b2);

        HashSet<EncodedTriple> baseBefore = [Same(a1, a2), bridge, Same(b1, b2)];
        OwlRlMaintainedClosure engine = new(baseBefore, terms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(crossPair, engine.Current.Derived, "The merged orbit must derive the cross-clique congruence before the unmerge.");

        HashSet<EncodedTriple> baseAfter = [.. baseBefore];
        baseAfter.Remove(bridge);
        ApplyAndAssertDeltas(engine, baseBefore, [], [bridge], baseAfter);

        Assert.Contains(crossPair, engine.AllDelta.Left, "The cross-clique congruence must leave the served set on unmerge.");
        Assert.IsNotEmpty(engine.DerivedDelta.Left, "The unmerge must leave cross-clique derivations from the derived set.");
    }

    /// <summary>
    /// Alternate-derivation survival — the leave-and-re-enter net-fold. A fact
    /// with two independent derivations, one of whose premises is retracted,
    /// is overdeleted then rederived within the one Apply, so it appears in
    /// NEITHER side of NEITHER delta.
    /// </summary>
    /// <remarks>
    /// Base <c>{subClassOf(A,C), subClassOf(B,C), type(x,A), type(x,B)}</c>
    /// derives <c>type(x,C)</c> two ways. Retracting <c>type(x,A)</c> marks
    /// <c>type(x,C)</c> for overdeletion (it is reached through the retracted
    /// premise), physically removes it, then the head-bound matcher rederives
    /// it from the surviving <c>subClassOf(B,C) ∧ type(x,B)</c>. The fact leaves
    /// and re-enters both <see cref="All"/> and <see cref="Derived"/> within the
    /// one Apply, so the net-fold cancels it out: it is in neither the entered
    /// nor the left side of either delta. Only the retracted base fact
    /// <c>type(x,A)</c> leaves the served set.
    /// </remarks>
    [TestMethod]
    public void AlternateDerivationSurvivalNetFoldsToNeitherSide()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId classC = OwlRlBatteryHelpers.Mint(dictionary, "C");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        EncodedTriple aSubC = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classC);
        EncodedTriple bSubC = OwlRlBatteryHelpers.Triple(classB, terms.SubClassOf, classC);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);
        EncodedTriple xIsB = OwlRlBatteryHelpers.Triple(x, terms.Type, classB);
        EncodedTriple xIsC = OwlRlBatteryHelpers.Triple(x, terms.Type, classC);

        HashSet<EncodedTriple> baseBefore = [aSubC, bSubC, xIsA, xIsB];
        OwlRlMaintainedClosure engine = new(baseBefore, terms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(xIsC, engine.Current.Derived, "cax-sco must derive type(x,C) before the retract.");

        HashSet<EncodedTriple> baseAfter = [aSubC, bSubC, xIsB];
        OwlRlResult result = ApplyAndAssertDeltas(engine, baseBefore, [], [xIsA], baseAfter);

        Assert.Contains(xIsC, result.Derived, "The surviving derivation must keep type(x,C) present.");
        AssertDeltaOmits(engine.AllDelta, xIsC, "AllDelta");
        AssertDeltaOmits(engine.DerivedDelta, xIsC, "DerivedDelta");
        Assert.Contains(xIsA, engine.AllDelta.Left, "Only the retracted base fact leaves the served set.");
    }

    /// <summary>
    /// Base demotion. Adding a base fact equal to an existing derived one moves
    /// it out of the derived set without changing the served set, so the
    /// <see cref="OwlRlMaintainedClosure.AllDelta"/> is empty for it and the
    /// <see cref="OwlRlMaintainedClosure.DerivedDelta"/> leaves it.
    /// </summary>
    /// <remarks>
    /// Base <c>{subClassOf(A,B), type(x,A)}</c> derives <c>type(x,B)</c>. Adding
    /// <c>type(x,B)</c> as a base fact demotes it: it stays in the served set
    /// (base now, derived before), so the served set is unchanged and
    /// <c>AllDelta</c> is empty; the derived set loses it, so it appears in
    /// <c>DerivedDelta.Left</c>.
    /// </remarks>
    [TestMethod]
    public void DemotionRecordsDerivedLeftAndEmptyAllDelta()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classB);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);
        EncodedTriple xIsB = OwlRlBatteryHelpers.Triple(x, terms.Type, classB);

        HashSet<EncodedTriple> baseBefore = [aSubB, xIsA];
        OwlRlMaintainedClosure engine = new(baseBefore, terms, cancellationToken: TestContext.CancellationToken);
        Assert.Contains(xIsB, engine.Current.Derived, "cax-sco must derive type(x,B) before the demotion.");

        HashSet<EncodedTriple> baseAfter = [aSubB, xIsA, xIsB];
        ApplyAndAssertDeltas(engine, baseBefore, [xIsB], [], baseAfter);

        Assert.AreEqual(1, engine.Statistics.BaseDemotions, "The add must count exactly one base demotion.");
        Assert.IsEmpty(engine.AllDelta.Entered, "A demotion does not change the served set.");
        Assert.IsEmpty(engine.AllDelta.Left, "A demotion does not change the served set.");
        Assert.Contains(xIsB, engine.DerivedDelta.Left, "The demoted fact must leave the derived set.");
        Assert.IsEmpty(engine.DerivedDelta.Entered, "A demotion enters nothing into the derived set.");
    }

    /// <summary>
    /// Seeded promotion. Retracting a datatype-hierarchy seed that is also a
    /// base fact keeps it in the served set and moves it into the derived set,
    /// so the <see cref="OwlRlMaintainedClosure.AllDelta"/> is empty for it and
    /// the <see cref="OwlRlMaintainedClosure.DerivedDelta"/> enters it.
    /// </summary>
    /// <remarks>
    /// The built-in datatype map seeds <c>subClassOf(sub,super)</c> into every
    /// closure. Building over a base that also states it makes it a base fact
    /// (present in the served set, absent from the derived set). Retracting it
    /// promotes it: it stays in the served set (as a seed), so <c>AllDelta</c>
    /// is empty; it enters the derived set, so it appears in
    /// <c>DerivedDelta.Entered</c>.
    /// </remarks>
    [TestMethod]
    public void PromotionRecordsDerivedEnteredAndEmptyAllDelta()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        (TermId subType, TermId superType) = terms.DatatypeHierarchy[0];
        EncodedTriple seed = OwlRlBatteryHelpers.Triple(subType, terms.SubClassOf, superType);

        HashSet<EncodedTriple> baseBefore = [seed];
        OwlRlMaintainedClosure engine = new(baseBefore, terms, cancellationToken: TestContext.CancellationToken);
        Assert.DoesNotContain(seed, engine.Current.Derived, "While the seed is also a base fact it is not in the derived set.");

        HashSet<EncodedTriple> baseAfter = [];
        ApplyAndAssertDeltas(engine, baseBefore, [], [seed], baseAfter);

        Assert.AreEqual(1, engine.Statistics.BasePromotions, "The seeded retract must count exactly one base promotion.");
        Assert.IsEmpty(engine.AllDelta.Entered, "A seeded promotion does not change the served set.");
        Assert.IsEmpty(engine.AllDelta.Left, "A seeded promotion does not change the served set.");
        Assert.Contains(seed, engine.DerivedDelta.Entered, "The promoted seed must enter the derived set.");
        Assert.IsEmpty(engine.DerivedDelta.Left, "A seeded promotion leaves nothing in the derived set.");
    }

    /// <summary>
    /// Falsity-introducing Apply. A consistent-to-inconsistent Apply runs the
    /// incremental pipeline to the falsity short-circuit; the recorded deltas
    /// still equal the engine's own served-set and derived-set snapshot diffs
    /// over the partial state, and the deltas are recorded (not a rebuild).
    /// </summary>
    /// <remarks>
    /// Base <c>{disjointWith(C,D), type(x,C)}</c> is consistent. Adding
    /// <c>type(x,D)</c> fires the cax-dw falsity. The pipeline halts at a
    /// non-fixpoint partial state; the recorded deltas equal the engine's
    /// before/after snapshot diffs of that partial served and derived set,
    /// exactly because the recording logs the same mutations the diff measures.
    /// </remarks>
    [TestMethod]
    public void FalsityIntroducingApplyRecordsDeltasEqualToSnapshotDiff()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId classC = OwlRlBatteryHelpers.Mint(dictionary, "C");
        TermId classD = OwlRlBatteryHelpers.Mint(dictionary, "D");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        EncodedTriple disjoint = OwlRlBatteryHelpers.Triple(classC, terms.DisjointWith, classD);
        EncodedTriple xIsC = OwlRlBatteryHelpers.Triple(x, terms.Type, classC);
        EncodedTriple xIsD = OwlRlBatteryHelpers.Triple(x, terms.Type, classD);

        HashSet<EncodedTriple> baseBefore = [disjoint, xIsC];
        OwlRlMaintainedClosure engine = new(baseBefore, terms, cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(engine.Current.IsConsistent, "The base must be consistent before the clashing typing.");

        HashSet<EncodedTriple> baseAfter = [disjoint, xIsC, xIsD];
        OwlRlResult result = ApplyAndAssertDeltas(engine, baseBefore, [xIsD], [], baseAfter);

        Assert.IsFalse(result.IsConsistent, "The clashing typing must fire the cax-dw falsity.");
        Assert.AreEqual(EntailmentRules.CaxDw, result.InconsistencyRule, "The falsity-introducing Apply must report cax-dw.");
        Assert.IsTrue(engine.HasRecordedDeltas, "A falsity-introducing incremental Apply still records deltas.");
    }

    /// <summary>
    /// Empty short-circuit. An Apply with no facts, and an Apply whose net
    /// effect is empty (re-adding a present base fact), both record an empty
    /// delta while marking the deltas recorded.
    /// </summary>
    /// <remarks>
    /// Over base <c>{subClassOf(A,B), type(x,A)}</c>, <c>Apply([],[])</c> takes
    /// the wrapper short-circuit and <c>Apply([type(x,A)],[])</c> runs the
    /// pipeline to an empty net effect; both leave the served and derived sets
    /// unchanged, so both deltas are empty and
    /// <see cref="OwlRlMaintainedClosure.HasRecordedDeltas"/> is true.
    /// </remarks>
    [TestMethod]
    public void EmptyAndNoOpAppliesRecordEmptyDeltas()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        EncodedTriple aSubB = OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classB);
        EncodedTriple xIsA = OwlRlBatteryHelpers.Triple(x, terms.Type, classA);

        HashSet<EncodedTriple> currentBase = [aSubB, xIsA];
        OwlRlMaintainedClosure engine = new(currentBase, terms, cancellationToken: TestContext.CancellationToken);

        //The wrapper short-circuit: no facts at all.
        ApplyAndAssertDeltas(engine, currentBase, [], [], currentBase);
        AssertDeltaEmpty(engine, "the empty short-circuit");

        //Runs the pipeline but nets to nothing: the re-add of a present fact.
        ApplyAndAssertDeltas(engine, currentBase, [xIsA], [], currentBase);
        AssertDeltaEmpty(engine, "a no-op re-add");
    }

    /// <summary>
    /// Rebuild path records no deltas. An Apply from an inconsistent state
    /// rebuilds the closure from scratch — a wholesale context swap — so it
    /// signals <see cref="OwlRlMaintainedClosure.HasRecordedDeltas"/> false and
    /// exposes empty deltas.
    /// </summary>
    /// <remarks>
    /// Base <c>{disjointWith(C,D), type(x,C), type(x,D)}</c> is inconsistent at
    /// construction. Retracting <c>type(x,D)</c> restores consistency through a
    /// from-scratch rebuild (<see cref="OwlRlMaintenanceMode.RebuildInconsistent"/>),
    /// which records no membership deltas; the wiring diffs the served target
    /// itself on this class of commit.
    /// </remarks>
    [TestMethod]
    public void RebuildFromInconsistentRecordsNoDeltas()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId classC = OwlRlBatteryHelpers.Mint(dictionary, "C");
        TermId classD = OwlRlBatteryHelpers.Mint(dictionary, "D");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        EncodedTriple disjoint = OwlRlBatteryHelpers.Triple(classC, terms.DisjointWith, classD);
        EncodedTriple xIsC = OwlRlBatteryHelpers.Triple(x, terms.Type, classC);
        EncodedTriple xIsD = OwlRlBatteryHelpers.Triple(x, terms.Type, classD);

        OwlRlMaintainedClosure engine = new([disjoint, xIsC, xIsD], terms, cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(engine.Current.IsConsistent, "The base must be inconsistent at construction.");

        OwlRlResult recovered = engine.Apply([], [xIsD], TestContext.CancellationToken);
        Assert.IsTrue(recovered.IsConsistent, "Retracting the clashing typing must restore consistency.");
        Assert.AreEqual(OwlRlMaintenanceMode.RebuildInconsistent, engine.Statistics.Mode, "The Apply from an inconsistent state must rebuild from scratch.");
        Assert.IsFalse(engine.HasRecordedDeltas, "A rebuild records no membership deltas.");
        AssertDeltaEmpty(engine, "a rebuild");
    }

    /// <summary>Applies the op, then asserts the recorded deltas equal the engine's own served-set (base ∪ derived) and derived-set snapshot diffs, with the entered and left collections disjoint.</summary>
    /// <param name="engine">The maintained engine under test.</param>
    /// <param name="baseBefore">The base before the op — the served set is this united with the pre-op derived set.</param>
    /// <param name="added">The op's added facts.</param>
    /// <param name="retracted">The op's retracted facts.</param>
    /// <param name="baseAfter">The base after the op — the served set is this united with the post-op derived set.</param>
    /// <returns>The Apply's result.</returns>
    private OwlRlResult ApplyAndAssertDeltas(
        OwlRlMaintainedClosure engine,
        IReadOnlyCollection<EncodedTriple> baseBefore,
        IReadOnlyCollection<EncodedTriple> added,
        IReadOnlyCollection<EncodedTriple> retracted,
        IReadOnlyCollection<EncodedTriple> baseAfter)
    {
        HashSet<EncodedTriple> derivedBefore = [.. engine.Current.Derived];
        HashSet<EncodedTriple> servedBefore = [.. baseBefore, .. derivedBefore];

        OwlRlResult result = engine.Apply(added, retracted, TestContext.CancellationToken);
        Assert.IsTrue(engine.HasRecordedDeltas, "An incremental Apply must record membership deltas.");

        HashSet<EncodedTriple> derivedAfter = [.. result.Derived];
        HashSet<EncodedTriple> servedAfter = [.. baseAfter, .. derivedAfter];

        AssertDeltaEqualsDiff(servedBefore, servedAfter, engine.AllDelta, "AllDelta");
        AssertDeltaEqualsDiff(derivedBefore, derivedAfter, engine.DerivedDelta, "DerivedDelta");

        return result;
    }

    /// <summary>Asserts a recorded delta equals the before/after snapshot diff, with entered ∩ left empty (the net-fold invariant).</summary>
    /// <param name="before">The tracked set before the op.</param>
    /// <param name="after">The tracked set after the op.</param>
    /// <param name="delta">The recorded delta to check.</param>
    /// <param name="label">The delta's name for assertion messages.</param>
    private static void AssertDeltaEqualsDiff(
        HashSet<EncodedTriple> before,
        HashSet<EncodedTriple> after,
        OwlRlMembershipDelta delta,
        string label)
    {
        HashSet<EncodedTriple> expectedEntered = [.. after];
        expectedEntered.ExceptWith(before);
        HashSet<EncodedTriple> expectedLeft = [.. before];
        expectedLeft.ExceptWith(after);

        HashSet<EncodedTriple> entered = [.. delta.Entered];
        HashSet<EncodedTriple> left = [.. delta.Left];

        Assert.IsTrue(
            entered.SetEquals(expectedEntered),
            $"{label} entered ({entered.Count}) must equal the snapshot diff ({expectedEntered.Count}).");
        Assert.IsTrue(
            left.SetEquals(expectedLeft),
            $"{label} left ({left.Count}) must equal the snapshot diff ({expectedLeft.Count}).");

        HashSet<EncodedTriple> intersection = [.. entered];
        intersection.IntersectWith(left);
        Assert.IsEmpty(intersection, $"{label} entered ∩ left must be empty (the net-fold invariant).");
    }

    /// <summary>Asserts the fact appears in neither the entered nor the left side of a delta — the net-fold result of a leave-and-re-enter.</summary>
    /// <param name="delta">The recorded delta.</param>
    /// <param name="fact">The fact that must be net-folded out.</param>
    /// <param name="label">The delta's name for assertion messages.</param>
    private static void AssertDeltaOmits(OwlRlMembershipDelta delta, EncodedTriple fact, string label)
    {
        Assert.DoesNotContain(fact, delta.Entered, $"{label} entered must not mention a net-folded fact.");
        Assert.DoesNotContain(fact, delta.Left, $"{label} left must not mention a net-folded fact.");
    }

    /// <summary>Asserts both recorded deltas are empty in both directions.</summary>
    /// <param name="engine">The engine whose deltas must be empty.</param>
    /// <param name="context">The situation described for assertion messages.</param>
    private static void AssertDeltaEmpty(OwlRlMaintainedClosure engine, string context)
    {
        Assert.IsEmpty(engine.AllDelta.Entered, $"AllDelta entered must be empty after {context}.");
        Assert.IsEmpty(engine.AllDelta.Left, $"AllDelta left must be empty after {context}.");
        Assert.IsEmpty(engine.DerivedDelta.Entered, $"DerivedDelta entered must be empty after {context}.");
        Assert.IsEmpty(engine.DerivedDelta.Left, $"DerivedDelta left must be empty after {context}.");
    }
}
