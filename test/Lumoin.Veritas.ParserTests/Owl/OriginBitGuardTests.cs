using System;
using System.Collections.Generic;
using Lumoin.Veritas.Owl.Contexts;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The origin-bit relay-guard exercisers, in two tiers. The storage-primitive and
/// Eq acting-literal-validation rows drive the isolated predicate, the
/// construction-proven acting-literal witness factories, and the per-context side
/// table on a bare <see cref="Context"/>. The engine-redrive rows
/// drive the guard's own branches through the real rule logic below the
/// module-verdict gates — the Eq arrival declaration and its set/reset around the
/// conclusion sink, the premise taint fold, the four guard-site refusals with the
/// general <c>RootEqualityRidesAChoice</c> latch, the Site D withhold and its
/// absorption-origin re-projection, and the inter-nominal carrier's source-bit
/// inheritance. Every branch is dark on every run where the Eq acting literal stays
/// well-formed, so each is reached here through the internal redrive seam rather
/// than a whole module verdict. Every assertion is a mechanism-tag observation
/// (well-formedness, the side-table bit, unconditional-head membership, the
/// ≈-class fold, the latch census), never a module verdict, since a moved verdict
/// would be a discovery rather than expected behaviour.
/// </summary>
[TestClass]
internal sealed class OriginBitGuardTests
{
    /// <summary>The clause origin marker the fixtures stamp; the origin value is inert for the side-table and withhold logic under test.</summary>
    private const int DerivedOrigin = -1;

    /// <summary>The concept-atom id the fixtures build a <c>C(·)</c> head from.</summary>
    private const int ConceptCAtom = 5;

    /// <summary>The concept-atom id the taint fixtures build a distinct <c>D(·)</c> head from.</summary>
    private const int ConceptDAtom = 6;

    /// <summary>The concept-atom id the multi-hop taint fixture builds a third <c>E(·)</c> head from.</summary>
    private const int ConceptEAtom = 7;

    /// <summary>The function-symbol id the shape-rejection fixtures build <c>f(x)</c> and <c>f(o)</c> terms from.</summary>
    private const int FunctionSymbol = 0;

    /// <summary>The rewrite source term the Eq validation fixtures merge from — a stand-in for <c>o2</c>.</summary>
    private static DlTerm FromTerm { get; } = DlTerm.Individual(2);

    /// <summary>The rewrite replacement term the Eq validation fixtures merge to — a stand-in for <c>o1</c>.</summary>
    private static DlTerm Replacement { get; } = DlTerm.Individual(1);

    /// <summary>A third named individual the shape-rejection and carrier fixtures use as a foreign or other constant — a stand-in for <c>o3</c>.</summary>
    private static DlTerm Other { get; } = DlTerm.Individual(3);

    /// <summary>The wrong-acting-literal shape: an <see cref="ApplyEq"/> firing whose acting literal is a CONCEPT atom (the selected maximal head literal, not the acting equality). V1 rejects it, so the conclusion is declared <c>DerivedUnderChoice</c> — the exact defect the guard exists to catch.</summary>
    [TestMethod]
    public void MalformedConceptActingLiteralIsNotWellFormed()
    {
        DlLiteral conceptActing = DlLiteral.Concept(ConceptCAtom, DlTerm.Central);
        DlLiteral target = DlLiteral.Inequality(FromTerm, Replacement);

        Assert.IsFalse(
            ContextSaturationEngine.IsEqActingLiteralWellFormed(conceptActing, target, FromTerm, Replacement),
            "A concept acting literal is not an equality (V1), so the Eq firing is declared DerivedUnderChoice — the wrong-acting-literal class is caught.");
    }

    /// <summary>A genuine Eq step: the acting equality connects exactly the rewrite terms and the target it subtracts mentions the source. V1/V2/V3 all pass, so the conclusion stays <c>DecidedUnderNoChoice</c> — the zero-movement guarantee for every real Eq firing.</summary>
    [TestMethod]
    public void GenuineActingEqualityIsWellFormed()
    {
        DlLiteral acting = DlLiteral.Equality(FromTerm, Replacement);
        DlLiteral target = DlLiteral.Inequality(FromTerm, Replacement);

        Assert.IsTrue(
            ContextSaturationEngine.IsEqActingLiteralWellFormed(acting, target, FromTerm, Replacement),
            "The acting equality connects the rewrite terms and the target mentions the source, so a genuine Eq firing stays DecidedUnderNoChoice.");
    }

    /// <summary>V2 accepts either storage orientation of the acting equality: an equality stored <c>o1 ≈ o2</c> still genuinely sources a <c>from = o2 -&gt; o1</c> rewrite, so it is well-formed.</summary>
    [TestMethod]
    public void GenuineActingEqualityReversedOrientationIsWellFormed()
    {
        DlLiteral acting = DlLiteral.Equality(Replacement, FromTerm);
        DlLiteral target = DlLiteral.Inequality(FromTerm, Replacement);

        Assert.IsTrue(
            ContextSaturationEngine.IsEqActingLiteralWellFormed(acting, target, FromTerm, Replacement),
            "The acting equality's two sides are the rewrite terms in the reverse orientation, which V2 accepts.");
    }

    /// <summary>V2 rejects a VALID-BUT-WRONG equality: a live equality between other individuals passed as acting does not connect the rewrite terms, so the conclusion is declared <c>DerivedUnderChoice</c> even though the acting literal is an equality.</summary>
    [TestMethod]
    public void WrongEqualityNotConnectingRewriteTermsIsNotWellFormed()
    {
        DlLiteral acting = DlLiteral.Equality(DlTerm.Individual(7), DlTerm.Individual(8));
        DlLiteral target = DlLiteral.Inequality(FromTerm, Replacement);

        Assert.IsFalse(
            ContextSaturationEngine.IsEqActingLiteralWellFormed(acting, target, FromTerm, Replacement),
            "An equality between unrelated individuals does not connect the rewrite terms (V2), so it is not a genuine acting equality.");
    }

    /// <summary>V3 rejects a vacuous rewrite: the target the residual subtracts mentions neither the source nor the replacement, so it was never a rewrite occurrence and the firing is declared <c>DerivedUnderChoice</c>.</summary>
    [TestMethod]
    public void VacuousTargetNotMentioningSourceIsNotWellFormed()
    {
        DlLiteral acting = DlLiteral.Equality(FromTerm, Replacement);
        DlLiteral target = DlLiteral.Concept(ConceptCAtom, DlTerm.Individual(9));

        Assert.IsFalse(
            ContextSaturationEngine.IsEqActingLiteralWellFormed(acting, target, FromTerm, Replacement),
            "The target mentions neither rewrite term (V3), so the acting declaration is vacuous and not well-formed.");
    }

    /// <summary>INV-2, genuine orientation: an acting-equality literal <c>o2 ≈ o1</c> whose two sides ARE the rewrite terms in the source-first order builds a witness against its own singleton maximal set, and the witness carries the literal and the attested maximal index verbatim.</summary>
    [TestMethod]
    public void ActingEqualityFactoryAcceptsGenuineOrientation()
    {
        DlLiteral[] head = [DlLiteral.Equality(FromTerm, Replacement)];

        Assert.IsTrue(ActingEquality.TryFrom(head, [0], 0, FromTerm, Replacement, out ActingEquality witness), "The literal's sides are the rewrite terms source-first (INV-2), so the factory builds the witness.");
        Assert.AreEqual(DlLiteral.Equality(FromTerm, Replacement), witness.Literal, "The witness carries the acting equality literal.");
        Assert.AreEqual(0, witness.Index.Value, "The witness records the attested maximal index.");
    }

    /// <summary>INV-2, reversed orientation: an acting-equality literal stored <c>o1 ≈ o2</c> — the two sides in the reverse order — still genuinely sources a <c>from = o2</c> rewrite, so the unordered-pair invariant admits it and the witness sources from <c>o2</c>. An ordered <c>First == fromTerm</c> check would wrongly refuse it.</summary>
    [TestMethod]
    public void ActingEqualityFactoryAcceptsReversedOrientation()
    {
        DlLiteral[] head = [DlLiteral.Equality(Replacement, FromTerm)];

        Assert.IsTrue(ActingEquality.TryFrom(head, [0], 0, FromTerm, Replacement, out ActingEquality witness), "The sides are the rewrite terms reversed, which the unordered-pair invariant admits (INV-2).");
        Assert.AreEqual(FromTerm, witness.FromTerm, "The witness sources from o2 regardless of the stored side order.");
    }

    /// <summary>INV-2 refusal: an equality between unrelated individuals does not connect the rewrite terms, so the factory refuses to treat it as the acting equality even though it is an equality at a maximal index.</summary>
    [TestMethod]
    public void ActingEqualityFactoryRejectsSidesNotConnectingRewriteTerms()
    {
        DlLiteral[] head = [DlLiteral.Equality(DlTerm.Individual(7), DlTerm.Individual(8))];

        Assert.IsFalse(ActingEquality.TryFrom(head, [0], 0, FromTerm, Replacement, out _), "The equality's sides are not the rewrite terms in either orientation (INV-2), so the factory refuses it.");
    }

    /// <summary>INV-3 completeness pin: an incomparable equality <c>x ≈ o3</c> — a variable against a named individual — is stored variable-first and has no strictly-greater side, yet its CONSTANT side <c>o3</c> is an admissible rewrite source. The factory delegates to the partial-order source test verbatim, so it admits the <c>o3 -&gt; x</c> firing; an ordered or maximal-side-only encoding would refuse this legitimate firing and lose completeness.</summary>
    [TestMethod]
    public void ActingEqualityFactoryAdmitsIncomparableConstantSource()
    {
        DlLiteral[] head = [DlLiteral.Equality(DlTerm.Central, Other)];

        Assert.IsTrue(ActingEquality.TryFrom(head, [0], 0, Other, DlTerm.Central, out ActingEquality witness), "The constant side of the incomparable pair is an admissible rewrite source (INV-3), so the factory admits it.");
        Assert.AreEqual(Other, witness.FromTerm, "The witness sources from the constant side o3.");
    }

    /// <summary>INV-3 refusal: the VARIABLE side of the same incomparable pair <c>x ≈ o3</c> is never a rewrite source, so a firing declaring <c>x</c> as the source is refused — the source test rules out a variable source.</summary>
    [TestMethod]
    public void ActingEqualityFactoryRejectsVariableSource()
    {
        DlLiteral[] head = [DlLiteral.Equality(DlTerm.Central, Other)];

        Assert.IsFalse(ActingEquality.TryFrom(head, [0], 0, DlTerm.Central, Other, out _), "A variable is never a rewrite source (INV-3), so the factory refuses the x -> o3 firing.");
    }

    /// <summary>INV-4 acceptance at a genuinely-maximal index: in the two-maximal head <c>D(x), o2 ≈ o1</c> the acting equality sits at index 1 — not the first-maximal <c>D(x)</c> at index 0 — and the factory admits it because index 1 is a member of the attested maximal set. The witness records index 1, so the acting literal is the source-bearing maximal, never a first-maximal shortcut value.</summary>
    [TestMethod]
    public void ActingEqualityFactoryAcceptsMaximalIndex()
    {
        DlLiteral[] head = [DlLiteral.Concept(ConceptDAtom, DlTerm.Central), DlLiteral.Equality(FromTerm, Replacement)];

        Assert.IsTrue(ActingEquality.TryFrom(head, [0, 1], 1, FromTerm, Replacement, out ActingEquality witness), "Index 1 is a member of the attested maximal set (INV-4), so the factory admits the acting equality at a non-first maximal.");
        Assert.AreEqual(1, witness.Index.Value, "The witness records the source-bearing maximal index, not the first-maximal.");
        Assert.AreEqual(DlLiteral.Equality(FromTerm, Replacement), witness.Literal, "The witness carries the acting equality at index 1.");
    }

    /// <summary>INV-4 refusal at a non-maximal index: attesting index 1 against a maximal set that holds only index 0 refuses the witness, even though the literal at index 1 is otherwise a genuine acting equality. Maximality is proven as set membership at the attested index, so a literal that is not attested maximal cannot be the acting equality.</summary>
    [TestMethod]
    public void ActingEqualityFactoryRejectsNonMaximalIndex()
    {
        DlLiteral[] head = [DlLiteral.Concept(ConceptDAtom, DlTerm.Central), DlLiteral.Equality(FromTerm, Replacement)];

        Assert.IsFalse(ActingEquality.TryFrom(head, [0], 1, FromTerm, Replacement, out _), "Index 1 is not a member of the attested maximal set [0] (INV-4), so the factory refuses the acting equality.");
    }

    /// <summary>INV-b acceptance: a concept atom <c>C(o2)</c> carries the rewrite source in its sole first-slot argument, a rewrite-eligible position, so the factory builds the acting target.</summary>
    [TestMethod]
    public void ActingTargetFactoryAdmitsConceptFirstSlotMention()
    {
        Assert.IsTrue(ActingTarget.TryFrom(DlLiteral.Concept(ConceptCAtom, FromTerm), FromTerm, out ActingTarget witness), "A concept atom mentions the source in its rewrite-eligible first slot (INV-b), so the factory admits it.");
        Assert.AreEqual(DlLiteral.Concept(ConceptCAtom, FromTerm), witness.Literal, "The witness carries the acting target literal.");
    }

    /// <summary>INV-b carve-out refusal: a concept atom carries its sole argument in the first slot; its second slot is never a rewrite position — even when it holds a non-variable individual the generic side-eligibility arm would admit as a rewritable second side. A concept whose vestigial second slot holds the queried source <c>o3</c>, and whose first slot (the central variable) does not, is refused solely because the concept second-slot carve-out is preserved verbatim from the slot-eligibility predicate; the refusal is the carve-out at work, not merely a variable slot.</summary>
    [TestMethod]
    public void ActingTargetFactoryRefusesConceptSecondSlotOccupancy()
    {
        DlLiteral conceptWithIndividualSecondSlot = new(DlLiteralKind.Concept, ConceptCAtom, DlTerm.Central, Other);

        Assert.IsFalse(ActingTarget.TryFrom(conceptWithIndividualSecondSlot, Other, out _), "The concept's carved-out second slot is never a rewrite position even holding the individual o3, which the generic side-eligibility arm would admit as a rewritable second side, so a source occupying only that slot is refused.");
    }

    /// <summary>INV-b acceptance of a non-concept second slot: an equality <c>x ≈ o3</c> mentions the source <c>o3</c> in its second side, a rewrite-eligible position unlike a concept second slot, so the factory admits the acting target.</summary>
    [TestMethod]
    public void ActingTargetFactoryAcceptsEqualityRewritableSide()
    {
        Assert.IsTrue(ActingTarget.TryFrom(DlLiteral.Equality(DlTerm.Central, Other), Other, out ActingTarget witness), "The equality's constant second side is a rewrite-eligible position (INV-b), so the factory admits the mention.");
        Assert.AreEqual(DlLiteral.Equality(DlTerm.Central, Other), witness.Literal, "The witness carries the equality acting target.");
    }

    /// <summary>The told/seed default: a clause inserted with no origin tag reads <c>DecidedUnderNoChoice</c> off the side table, and an id past the (unallocated) tag list also reads <c>DecidedUnderNoChoice</c> — the zero-allocation default every production run rides.</summary>
    [TestMethod]
    public void UntaggedClauseDefaultsDecidedUnderNoChoice()
    {
        Context context = NewContext(isRoot: false);
        int id = context.Insert(UnconditionalEquality(), isPredEligible: false, decidedUnderNoChoice: true, Selected());

        Assert.IsFalse(context.IsDerivedUnderChoice(id), "An untagged clause defaults DecidedUnderNoChoice.");
        Assert.IsFalse(context.IsDerivedUnderChoice(id + 100), "An id past the unallocated tag list reads DecidedUnderNoChoice.");
    }

    /// <summary>The side-table set: tagging a clause <c>DerivedUnderChoice</c> is observed through the accessor — the taint-fold and arrival-declaration storage primitive.</summary>
    [TestMethod]
    public void SetDerivedUnderChoiceTagsTheClause()
    {
        Context context = NewContext(isRoot: false);
        int id = context.Insert(UnconditionalEquality(), isPredEligible: false, decidedUnderNoChoice: true, Selected());

        context.SetDerivedUnderChoice(id);

        Assert.IsTrue(context.IsDerivedUnderChoice(id), "A set clause reads DerivedUnderChoice.");
    }

    /// <summary>The absorption-upgrade primitive: clearing the tag moves a clause toward <c>DecidedUnderNoChoice</c>, the direction a choice-free duplicate merges onto a previously choice-tainted survivor.</summary>
    [TestMethod]
    public void ClearDerivedUnderChoiceUpgradesTheClause()
    {
        Context context = NewContext(isRoot: false);
        int id = context.Insert(UnconditionalEquality(), isPredEligible: false, decidedUnderNoChoice: true, Selected());
        context.SetDerivedUnderChoice(id);

        context.ClearDerivedUnderChoice(id);

        Assert.IsFalse(context.IsDerivedUnderChoice(id), "A cleared clause reads DecidedUnderNoChoice — the merge-toward-decided direction.");
    }

    /// <summary>Tagging a higher id pads the lower ids with <c>DecidedUnderNoChoice</c>, so the side table stays index-aligned with the clause store and an earlier untagged clause is unaffected.</summary>
    [TestMethod]
    public void TagOnAHigherIdPadsLowerIdsDecidedUnderNoChoice()
    {
        Context context = NewContext(isRoot: false);
        int first = context.Insert(UnconditionalEquality(), isPredEligible: false, decidedUnderNoChoice: true, Selected());
        int second = context.Insert(UnconditionalConcept(), isPredEligible: false, decidedUnderNoChoice: true, Selected());

        context.SetDerivedUnderChoice(second);

        Assert.IsFalse(context.IsDerivedUnderChoice(first), "The padded lower id stays DecidedUnderNoChoice.");
        Assert.IsTrue(context.IsDerivedUnderChoice(second), "The tagged higher id reads DerivedUnderChoice.");
    }

    /// <summary>The Site D withhold primitive plus the storage-level re-projection: a <c>DerivedUnderChoice</c> unconditional equality head is withheld from <see cref="Context.UnconditionalContains"/> at insert (recording it as unconditional would seed a spurious clash the read-off must not trust), then the <see cref="Context.ProjectUnconditionalHead"/> storage primitive re-offers it once the tag clears. This drives the projection primitive directly; the engine's absorption-origin merge that CALLS it is driven by <see cref="DerivedUnderChoiceUnconditionalEqualityWithheldAtAddClauseThenReofferedByAbsorption"/>.</summary>
    [TestMethod]
    public void DerivedUnderChoiceUnconditionalEqualityHeadWithheldThenProjectedByPrimitive()
    {
        Context context = NewContext(isRoot: false);
        DlClause clause = UnconditionalEquality();
        int id = context.Insert(clause, isPredEligible: false, decidedUnderNoChoice: false, Selected());

        Assert.IsFalse(context.UnconditionalContains(clause.Head[0]), "A DerivedUnderChoice unconditional equality head is withheld from the unconditional-head set at insert.");

        context.ProjectUnconditionalHead(id);

        Assert.IsTrue(context.UnconditionalContains(clause.Head[0]), "The re-projection primitive projects the head once the tag clears to DecidedUnderNoChoice.");
    }

    /// <summary>A <c>DecidedUnderNoChoice</c> unconditional equality head is projected at insert directly — the un-withheld baseline the Site D gate leaves untouched.</summary>
    [TestMethod]
    public void DecidedUnderNoChoiceUnconditionalEqualityHeadProjectedAtInsert()
    {
        Context context = NewContext(isRoot: false);
        DlClause clause = UnconditionalEquality();
        context.Insert(clause, isPredEligible: false, decidedUnderNoChoice: true, Selected());

        Assert.IsTrue(context.UnconditionalContains(clause.Head[0]), "A DecidedUnderNoChoice unconditional equality head is projected at insert.");
    }

    /// <summary>The Site D withhold for a NON-equality head plus the storage-level re-projection: a <c>DerivedUnderChoice</c> unconditional concept head is likewise withheld at insert (the withhold is head-kind-agnostic — only the engine-side latch arming is equality-only), then re-projected by the storage primitive.</summary>
    [TestMethod]
    public void DerivedUnderChoiceUnconditionalConceptHeadWithheldThenProjected()
    {
        Context context = NewContext(isRoot: false);
        DlClause clause = UnconditionalConcept();
        int id = context.Insert(clause, isPredEligible: false, decidedUnderNoChoice: false, Selected());

        Assert.IsFalse(context.UnconditionalContains(clause.Head[0]), "A DerivedUnderChoice unconditional concept head is withheld from the unconditional-head set at insert.");

        context.ProjectUnconditionalHead(id);

        Assert.IsTrue(context.UnconditionalContains(clause.Head[0]), "The re-projection primitive projects the withheld concept head once the tag clears.");
    }

    /// <summary>The wrong-acting-literal shape refused at construction: the equality premise <c>⊤ -&gt; D(x), o2 ≈ o1</c> carries a concept disjunct <c>D(x)</c> at its index-0 maximal, and a firing that declares <c>D(x)</c> as the acting equality is unconstructible — <see cref="ActingEquality.TryFrom"/> refuses the concept (INV-1: its kind is concept, not equality). Driving the raw literal through the redrive seam therefore lands no conclusion, so the malformed input is a factory refusal rather than a tainted conclusion.</summary>
    [TestMethod]
    public void MalformedConceptActingLiteralRefusedByActingEqualityFactory()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewContext(isRoot: false);
        int equalityId = context.Insert(ConceptAndEquality(), isPredEligible: false, decidedUnderNoChoice: true, SelectedPair());
        int targetId = context.Insert(ConceptOnFrom(), isPredEligible: false, decidedUnderNoChoice: true, Selected());

        DlLiteral[] equalityHead = [DlLiteral.Concept(ConceptDAtom, DlTerm.Central), DlLiteral.Equality(FromTerm, Replacement)];
        Assert.IsFalse(ActingEquality.TryFrom(equalityHead, [0, 1], 0, FromTerm, Replacement, out _), "The index-0 maximal D(x) is a concept, not an equality (INV-1), so the factory refuses to build an acting equality from it.");

        DlLiteral malformedActing = DlLiteral.Concept(ConceptDAtom, DlTerm.Central);
        DlLiteral actingTarget = DlLiteral.Concept(ConceptCAtom, FromTerm);
        Assert.AreEqual(-1, engine.RedriveApplyEq(context, equalityId, targetId, malformedActing, actingTarget, FromTerm, Replacement), "The refused acting equality lands no conclusion, so the malformed firing is unconstructible rather than tainting a conclusion.");
    }

    /// <summary>The Eq arrival declaration reset on success: the SAME premises (<c>⊤ -&gt; D(x), o2 ≈ o1</c> and <c>⊤ -&gt; C(o2)</c>) driven with the GENUINE acting equality <c>o2 ≈ o1</c> subtract exactly the equality, KEEP the <c>D(x)</c> escape disjunct, and pass validation, so the arrival is declared choice-free and the stored conclusion carries no side-table bit — the reset half of the arrival wiring, the zero-movement guarantee for every real Eq firing.</summary>
    [TestMethod]
    public void GenuineActingEqualityLeavesApplyEqConclusionDecided()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewContext(isRoot: false);
        int equalityId = context.Insert(ConceptAndEquality(), isPredEligible: false, decidedUnderNoChoice: true, SelectedPair());
        int targetId = context.Insert(ConceptOnFrom(), isPredEligible: false, decidedUnderNoChoice: true, Selected());

        DlLiteral genuineActing = DlLiteral.Equality(FromTerm, Replacement);
        DlLiteral actingTarget = DlLiteral.Concept(ConceptCAtom, FromTerm);
        int conclusionId = engine.RedriveApplyEq(context, equalityId, targetId, genuineActing, actingTarget, FromTerm, Replacement);

        Assert.AreNotEqual(-1, conclusionId, "The genuine Eq firing lands a non-redundant conclusion.");
        Assert.IsFalse(context.IsDerivedUnderChoice(conclusionId), "A genuine acting equality passes validation, so ApplyEq leaves the conclusion DecidedUnderNoChoice.");
    }

    /// <summary>The Eq sink folds its premise ids into the conclusion's origin tag: the SAME genuine firing driven with the equality premise tagged <c>DerivedUnderChoice</c> lands a conclusion carrying the tag, since the sink offers the conclusion with both premise ids and the fold reads them. A sink offering an empty premise set would land the same conclusion choice-free, laundering the tainted premise — the taint rows above cannot see that, because they reach the fold through <see cref="ContextSaturationEngine.RedriveAddClause"/> rather than through the rule's own sink call.</summary>
    [TestMethod]
    public void TheEqSinkInheritsATaintedPremisesChoiceTag()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewContext(isRoot: false);
        int equalityId = context.Insert(ConceptAndEquality(), isPredEligible: false, decidedUnderNoChoice: true, SelectedPair());
        int targetId = context.Insert(ConceptOnFrom(), isPredEligible: false, decidedUnderNoChoice: true, Selected());
        context.SetDerivedUnderChoice(equalityId);

        DlLiteral genuineActing = DlLiteral.Equality(FromTerm, Replacement);
        DlLiteral actingTarget = DlLiteral.Concept(ConceptCAtom, FromTerm);
        int conclusionId = engine.RedriveApplyEq(context, equalityId, targetId, genuineActing, actingTarget, FromTerm, Replacement);

        Assert.AreNotEqual(-1, conclusionId, "The genuine Eq firing lands a non-redundant conclusion.");
        Assert.IsTrue(context.IsDerivedUnderChoice(conclusionId), "The Eq conclusion inherits the tagged equality premise's DerivedUnderChoice bit through the sink's premise-id fold.");
    }

    /// <summary>The taint fold, single hop: a conclusion whose one premise is tagged <c>DerivedUnderChoice</c> inherits the tag through the <see cref="ApplyEq"/>-independent <see cref="ContextSaturationEngine.RedriveAddClause"/> premise fold, even though the conclusion's own rule step declares nothing.</summary>
    [TestMethod]
    public void TaintedPremisePropagatesDerivedUnderChoice()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewContext(isRoot: false);
        int premiseId = context.Insert(ConditionalConcept(ConceptCAtom, ConceptDAtom), isPredEligible: false, decidedUnderNoChoice: true, Selected());
        context.SetDerivedUnderChoice(premiseId);

        int conclusionId = engine.RedriveAddClause(context, UnconditionalConceptOf(ConceptDAtom), [premiseId]);

        Assert.AreNotEqual(-1, conclusionId, "The conclusion is a non-redundant clause.");
        Assert.IsTrue(context.IsDerivedUnderChoice(conclusionId), "A DerivedUnderChoice premise taints the conclusion through the AddClause fold.");
    }

    /// <summary>The taint fold, second hop: a conclusion built from a premise that was itself tainted only by inheritance still computes <c>DerivedUnderChoice</c> — the monotone-upward fold holds across a second non-Eq step on the default single-root path, so a fold accidentally nested in the dark push-tag block would be caught.</summary>
    [TestMethod]
    public void TaintedPremisePropagatesAcrossSecondHop()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewContext(isRoot: false);
        int seedId = context.Insert(ConditionalConcept(ConceptCAtom, ConceptDAtom), isPredEligible: false, decidedUnderNoChoice: true, Selected());
        context.SetDerivedUnderChoice(seedId);

        int firstHopId = engine.RedriveAddClause(context, UnconditionalConceptOf(ConceptDAtom), [seedId]);
        int secondHopId = engine.RedriveAddClause(context, UnconditionalConceptOf(ConceptEAtom), [firstHopId]);

        Assert.IsTrue(context.IsDerivedUnderChoice(firstHopId), "The first hop inherits the seed's tag.");
        Assert.IsTrue(context.IsDerivedUnderChoice(secondHopId), "The second hop inherits the first hop's inherited tag — the fold is monotone across hops.");
    }

    /// <summary>Site A, ground equality: a <c>DerivedUnderChoice</c> clause with a ground equality head is refused Pred eligibility and the general latch arms — the ground equality the predecessor image would ground to a root identity, refused at the source.</summary>
    [TestMethod]
    public void GroundEqualityHeadRefusedPredEligibilityArmsLatch()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        DlClause clause = UnconditionalEquality();

        bool eligible = engine.RedriveIsPredEligible(clause, decidedUnderNoChoice: false);

        Assert.IsFalse(eligible, "A ground equality head on a DerivedUnderChoice clause is refused Pred eligibility.");
        Assert.IsTrue(engine.HasRootEqualityRidesAChoice, "The Site A refusal arms the general latch.");
        Assert.AreEqual(1L, engine.RootEqualityRidesAChoiceHeads, "The refusal charges the latch census once.");
    }

    /// <summary>Site A, nonground trigger equality: a <c>DerivedUnderChoice</c> clause with a nonground <c>y ≈ o</c> trigger equality head passes the trigger screen and is then refused Pred eligibility, arming the latch.</summary>
    [TestMethod]
    public void NonGroundTriggerEqualityHeadRefusedPredEligibilityArmsLatch()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        DlClause clause = Unconditional(DlLiteral.Equality(DlTerm.Context, Replacement));

        bool eligible = engine.RedriveIsPredEligible(clause, decidedUnderNoChoice: false);

        Assert.IsFalse(eligible, "A nonground y ≈ o trigger equality on a DerivedUnderChoice clause is refused Pred eligibility.");
        Assert.IsTrue(engine.HasRootEqualityRidesAChoice, "The Site A refusal on the trigger shape arms the latch.");
        Assert.AreEqual(1L, engine.RootEqualityRidesAChoiceHeads, "The refusal charges the latch census once.");
    }

    /// <summary>Site A, screen-first ordering: a <c>DerivedUnderChoice</c> clause with a nonground NON-trigger equality head (<c>f(x) ≈ o</c>) is refused eligibility by the trigger screen BEFORE the equality-refusal check, so the latch does NOT arm — a screen-rejected equality never forces a delegation.</summary>
    [TestMethod]
    public void ShapeRejectedNonTriggerEqualityDoesNotArmLatchAtSiteA()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        DlClause clause = Unconditional(DlLiteral.Equality(DlTerm.Function(FunctionSymbol), Replacement));

        bool eligible = engine.RedriveIsPredEligible(clause, decidedUnderNoChoice: false);

        Assert.IsFalse(eligible, "A nonground non-trigger equality is refused eligibility by the trigger screen.");
        Assert.IsFalse(engine.HasRootEqualityRidesAChoice, "The screen rejects before the equality-refusal check, so the latch does not arm.");
        Assert.AreEqual(0L, engine.RootEqualityRidesAChoiceHeads, "No screen-rejected equality charges the latch census.");
    }

    /// <summary>Site B, ground equality: a <c>DerivedUnderChoice</c> clause with a ground equality head is refused r-Pred registration (via the <c>IsGroundLiteral</c> admission arm) and the general latch arms.</summary>
    [TestMethod]
    public void GroundEqualityHeadRefusedRootPredRegistrationArmsLatch()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context root = NewContext(isRoot: true);
        DlClause clause = UnconditionalEquality();

        engine.RedriveRegisterRootPredEligible(root, clauseId: 0, clause, decidedUnderNoChoice: false);

        Assert.IsTrue(engine.HasRootEqualityRidesAChoice, "The Site B refusal on the ground admission arm arms the latch.");
        Assert.AreEqual(1L, engine.RootEqualityRidesAChoiceHeads, "The refusal charges the latch census once.");
    }

    /// <summary>Site B, nonground trigger equality: a <c>DerivedUnderChoice</c> clause with a nonground <c>y ≈ o</c> trigger equality head is refused r-Pred registration (via the trigger-shape admission arm) and the latch arms — the F5 both-arms unification.</summary>
    [TestMethod]
    public void NonGroundTriggerEqualityHeadRefusedRootPredRegistrationArmsLatch()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context root = NewContext(isRoot: true);
        DlClause clause = Unconditional(DlLiteral.Equality(DlTerm.Context, Replacement));

        engine.RedriveRegisterRootPredEligible(root, clauseId: 0, clause, decidedUnderNoChoice: false);

        Assert.IsTrue(engine.HasRootEqualityRidesAChoice, "The Site B refusal on the trigger-shape admission arm arms the latch.");
        Assert.AreEqual(1L, engine.RootEqualityRidesAChoiceHeads, "The refusal charges the latch census once.");
    }

    /// <summary>Site B, screen-first ordering: a <c>DerivedUnderChoice</c> clause with an <c>f(o)</c>-bearing equality head is rejected by the shape screen BEFORE the equality-refusal check, so the latch does NOT arm — an f(o)-bearing equality is an r-Succ witness the shape screen rejects, never an r-Pred disjunct.</summary>
    [TestMethod]
    public void FunctionOfIndividualEqualityShapeRejectedDoesNotArmLatchAtSiteB()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context root = NewContext(isRoot: true);
        DlClause clause = Unconditional(DlLiteral.Equality(DlTerm.FunctionOf(FunctionSymbol, 3), Replacement));

        engine.RedriveRegisterRootPredEligible(root, clauseId: 0, clause, decidedUnderNoChoice: false);

        Assert.IsFalse(engine.HasRootEqualityRidesAChoice, "The f(o)-bearing shape is rejected before the equality-refusal check, so the latch does not arm.");
        Assert.AreEqual(0L, engine.RootEqualityRidesAChoiceHeads, "No shape-rejected equality charges the latch census.");
    }

    /// <summary>Site C, refusal: a <c>DerivedUnderChoice</c> body-0/head-1 root equality is refused the root ≈-class fold-feed — the two constants' classes stay unmerged and the general latch arms, so the read-time union never sees an identity an unrecorded drop manufactured.</summary>
    [TestMethod]
    public void DerivedUnderChoiceRootEqualityRefusedFoldMergeArmsLatch()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context root = NewContext(isRoot: true);
        int premiseId = InsertConditional(root);
        root.SetDerivedUnderChoice(premiseId);

        int conclusionId = engine.RedriveAddClause(root, UnconditionalEquality(), [premiseId]);

        Assert.AreNotEqual(-1, conclusionId, "The choice-riding root equality is a non-redundant clause.");
        Assert.IsTrue(root.IsDerivedUnderChoice(conclusionId), "The tainted root equality is tagged DerivedUnderChoice.");
        Assert.IsFalse(engine.RootApproxSameClass(2, 1), "Site C refuses the union, so the two constants stay in distinct ≈-classes.");
        Assert.IsTrue(engine.HasRootEqualityRidesAChoice, "The Site C refusal arms the general latch.");
    }

    /// <summary>Site C, merge: a <c>DecidedUnderNoChoice</c> body-0/head-1 root equality feeds the ≈-class fold — the two constants merge into one class and the latch stays disarmed, the baseline the guard leaves untouched.</summary>
    [TestMethod]
    public void DecidedUnderNoChoiceRootEqualityFeedsFoldMerge()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context root = NewContext(isRoot: true);

        int conclusionId = engine.RedriveAddClause(root, UnconditionalEquality(), []);

        Assert.AreNotEqual(-1, conclusionId, "The choice-free root equality is a non-redundant clause.");
        Assert.IsTrue(engine.RootApproxSameClass(2, 1), "Site C feeds the union, so the two constants share one ≈-class.");
        Assert.IsFalse(engine.HasRootEqualityRidesAChoice, "A choice-free equality arms no latch.");
    }

    /// <summary>Site D through the engine, plus the absorption-origin merge: a <c>DerivedUnderChoice</c> unconditional equality head lands through <see cref="ContextSaturationEngine.RedriveAddClause"/> withheld from the unconditional-head set and arms the latch; a later choice-free duplicate is absorbed, and the engine's absorption-origin merge clears the tag, re-projects the withheld head, and re-enqueues — after which the head IS an unconditional head.</summary>
    [TestMethod]
    public void DerivedUnderChoiceUnconditionalEqualityWithheldAtAddClauseThenReofferedByAbsorption()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewContext(isRoot: false);
        int premiseId = context.Insert(ConditionalConcept(ConceptCAtom, ConceptDAtom), isPredEligible: false, decidedUnderNoChoice: true, Selected());
        context.SetDerivedUnderChoice(premiseId);

        int taggedId = engine.RedriveAddClause(context, UnconditionalEquality(), [premiseId]);
        Assert.AreNotEqual(-1, taggedId, "The choice-riding unconditional equality lands as a new clause.");
        DlLiteral storedHead = context.At(taggedId).Head[0];
        Assert.IsTrue(context.IsDerivedUnderChoice(taggedId), "The tainted arrival is tagged DerivedUnderChoice.");
        Assert.IsFalse(context.UnconditionalContains(storedHead), "The Site D gate withholds the choice-riding unconditional equality head at insert.");
        Assert.IsTrue(engine.HasRootEqualityRidesAChoice, "The equality flavour of the Site D withhold arms the latch.");

        int duplicateId = engine.RedriveAddClause(context, UnconditionalEquality(), []);

        Assert.AreEqual(-1, duplicateId, "The choice-free duplicate is absorbed, not inserted.");
        Assert.IsFalse(context.IsDerivedUnderChoice(taggedId), "The absorption-origin merge clears the survivor's tag toward DecidedUnderNoChoice.");
        Assert.IsTrue(context.UnconditionalContains(storedHead), "The absorption-origin merge re-projects the withheld head once the tag clears.");
        Assert.AreEqual(1L, engine.BuildStatistics(contextDecided: false).OriginClearReenqueues, "The merge re-enqueues the survivor exactly once.");

        int repeatId = engine.RedriveAddClause(context, UnconditionalEquality(), []);

        Assert.AreEqual(-1, repeatId, "A second choice-free duplicate is absorbed as well.");
        Assert.AreEqual(1L, engine.BuildStatistics(contextDecided: false).OriginClearReenqueues, "The clear is one-shot per id, so the second absorption reaches the merge and takes no action — a counter charged before that guard would read two.");
    }

    /// <summary>Latch-to-delegation: an armed <c>RootEqualityRidesAChoice</c> latch surfaces a positive head count, the census the module-tier delegation leg reads. The module-tier <c>ContextDecided=false</c> delegation itself is reachable only through a whole <c>DecideModule</c> run (its consumer is <c>ContextSaturationModuleReasoner</c>'s completion-block and re-saturation legs), so it is pinned here at the engine surface the leg consumes: a positive <c>RootEqualityRidesAChoiceHeads</c> is exactly the condition under which the module reports the context arm did not decide the module whole and delegates it named.</summary>
    [TestMethod]
    public void ArmedLatchSurfacesHeadCountForModuleDelegation()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();

        bool eligible = engine.RedriveIsPredEligible(UnconditionalEquality(), decidedUnderNoChoice: false);

        Assert.IsFalse(eligible, "The choice-riding equality is refused a guard site, arming the latch.");
        Assert.IsTrue(engine.HasRootEqualityRidesAChoice, "The armed latch is the module-tier delegation condition.");
        Assert.IsGreaterThan(0L, engine.RootEqualityRidesAChoiceHeads, "The positive head count is the census the ContextDecided=false delegation leg surfaces on the delegated module's totals.");
    }

    /// <summary>Carrier inheritance: a <c>DerivedUnderChoice</c> body-empty source clause imaged by the inter-nominal carrier lands at the foreign nominal root TAGGED — the source bit is threaded through, so a choice-riding clause is never laundered to <c>DecidedUnderNoChoice</c> across the carrier.</summary>
    [TestMethod]
    public void CarrierImageInheritsSourceDerivedUnderChoice()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedriveFragmented();
        Context source = engine.RedriveRootForIndividual(2);
        DlClause carried = DlClause.Create(Array.Empty<DlLiteral>(), new[] { DlLiteral.Concept(ConceptCAtom, Other) }, DerivedOrigin);

        engine.RedriveFireInterNominalCarrier(source, carried, sourceDerived: true);

        Context foreign = engine.RedriveRootForIndividual(3);
        Assert.IsTrue(foreign.HasDerivedUnderChoiceTags, "The carrier image inherits the source's DerivedUnderChoice tag at the foreign nominal root.");
    }

    /// <summary>Carrier baseline: a <c>DecidedUnderNoChoice</c> body-empty source clause images to a foreign nominal root that carries no choice tag — the carrier relays the source bit faithfully in the decided direction too.</summary>
    [TestMethod]
    public void CarrierImageOfDecidedSourceStaysDecided()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedriveFragmented();
        Context source = engine.RedriveRootForIndividual(2);
        DlClause carried = DlClause.Create(Array.Empty<DlLiteral>(), new[] { DlLiteral.Concept(ConceptCAtom, Other) }, DerivedOrigin);

        engine.RedriveFireInterNominalCarrier(source, carried, sourceDerived: false);

        Context foreign = engine.RedriveRootForIndividual(3);
        Assert.IsFalse(foreign.HasDerivedUnderChoiceTags, "The choice-free source image carries no tag at the foreign root — no laundering in either direction.");
    }

    /// <summary>The told default through the engine: a no-premise told clause driven through <see cref="ContextSaturationEngine.RedriveAddClause"/> with the empty premise set is <c>DecidedUnderNoChoice</c>, and the side table stays unallocated — the zero-allocation default the whole taint fold rides.</summary>
    [TestMethod]
    public void ToldNoPremiseClauseThroughAddClauseDefaultsDecidedUnderNoChoice()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewContext(isRoot: false);

        int conclusionId = engine.RedriveAddClause(context, UnconditionalConcept(), []);

        Assert.AreNotEqual(-1, conclusionId, "The told clause is a non-redundant clause.");
        Assert.IsFalse(context.IsDerivedUnderChoice(conclusionId), "A no-premise told clause defaults DecidedUnderNoChoice.");
        Assert.IsFalse(context.HasDerivedUnderChoiceTags, "No tag is set, so the side table stays unallocated — the zero-allocation default.");
    }

    /// <summary>The unified acting-target dispatch on the divergent two-maximal case. The rewrite target <c>⊤ -&gt; D(x), o3 ≈ o1</c> carries two mutually unordered head literals — a concept over the central variable and an equality over named individuals are order-incomparable — so both are maximal and <c>D(x)</c> is the selected first-maximal, while the non-selected <c>o3 ≈ o1</c> is the source-bearing maximal. Driven by the merge equality <c>⊤ -&gt; o3 ≈ o2</c> sourcing <c>o3 -&gt; o2</c>, the dispatch skips the non-mentioning <c>D(x)</c> and rewrites <c>o3 ≈ o1</c> to <c>o2 ≈ o1</c>, subtracts exactly that literal keeping <c>D(x)</c>, and passes the acting-literal validation, so the conclusion stays <c>DecidedUnderNoChoice</c>. The conclusion shape — both <c>o2 ≈ o1</c> and <c>D(x)</c>, untainted — is what this row asserts: selecting the source-bearing maximal produces <c>o2 ≈ o1</c>, subtracting exactly it keeps the non-acting <c>D(x)</c>, and the genuine acting equality leaves the conclusion choice-free, so a head carrying both disjuncts choice-free characterises correct source-bearing selection over a two-maximal target.</summary>
    [TestMethod]
    public void RootTwoMaximalTargetNonSelectedSourceBearingRewritesCorrectDisjunctStaysDecided()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewContext(isRoot: false);
        int equalityId = context.Insert(SourceMergeEquality(), isPredEligible: false, decidedUnderNoChoice: true, Selected());
        int targetId = context.Insert(TwoMaximalTargetOnSource(), isPredEligible: false, decidedUnderNoChoice: true, SelectedPair());

        DlLiteral actingEquality = DlLiteral.Equality(Other, FromTerm);
        int conclusionId = engine.RedriveDispatchEqOverTargetMaximal(context, equalityId, actingEquality, targetId, Other, FromTerm);

        Assert.AreNotEqual(-1, conclusionId, "The unified dispatch fires on the source-bearing maximal and lands a conclusion.");
        Assert.IsTrue(HeadContains(context.At(conclusionId), DlLiteral.Equality(FromTerm, Replacement)), "The conclusion carries the rewritten disjunct o2 ≈ o1.");
        Assert.IsTrue(HeadContains(context.At(conclusionId), DlLiteral.Concept(ConceptDAtom, DlTerm.Central)), "The non-acting maximal D(x) survives into the conclusion.");
        Assert.IsFalse(context.IsDerivedUnderChoice(conclusionId), "The genuine acting equality passes validation, so the conclusion stays DecidedUnderNoChoice — no spurious origin taint.");
    }

    /// <summary>The unified target-lookup-and-dispatch step over the divergent two-maximal case, entered at the step that finds the source-mentioning targets. Driving the merge equality <c>⊤ -&gt; o3 ≈ o2</c> through <see cref="ContextSaturationEngine.RedriveRewriteMentionsOfSource"/> reaches the two-maximal target <c>⊤ -&gt; D(x), o3 ≈ o1</c> through the source-mention index — the target registers under <c>o3</c> via its non-selected <c>o3 ≈ o1</c> maximal — and the acting-target dispatch rewrites that source-bearing maximal to <c>o2 ≈ o1</c> while the selected <c>D(x)</c> rides along, so the conclusion carries both disjuncts choice-free. Entering at the target-lookup step rather than the dispatch callee pins the whole step for every topology: the source term reaches the target through the mention index and the acting-target selection lands on the source-bearing maximal, not on the selected first-maximal <c>D(x)</c>.</summary>
    [TestMethod]
    public void RootTwoMaximalTargetThroughRewriteMentionsRewritesSourceBearingMaximalStaysDecided()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewContext(isRoot: false);
        int equalityId = context.Insert(SourceMergeEquality(), isPredEligible: false, decidedUnderNoChoice: true, Selected());
        context.Insert(TwoMaximalTargetOnSource(), isPredEligible: false, decidedUnderNoChoice: true, SelectedPair());

        DlLiteral actingEquality = DlLiteral.Equality(Other, FromTerm);
        int conclusionId = engine.RedriveRewriteMentionsOfSource(context, equalityId, actingEquality, Other, FromTerm);

        Assert.AreNotEqual(-1, conclusionId, "The target-lookup step reaches the two-maximal target through the source-mention index and the acting-target dispatch lands a conclusion.");
        Assert.IsTrue(HeadContains(context.At(conclusionId), DlLiteral.Equality(FromTerm, Replacement)), "The conclusion carries the rewritten source-bearing disjunct o2 ≈ o1.");
        Assert.IsTrue(HeadContains(context.At(conclusionId), DlLiteral.Concept(ConceptDAtom, DlTerm.Central)), "The selected non-acting maximal D(x) survives into the conclusion.");
        Assert.IsFalse(context.IsDerivedUnderChoice(conclusionId), "The genuine acting equality passes validation, so the conclusion stays DecidedUnderNoChoice — no spurious origin taint.");
    }

    /// <summary>INV-d singleton-maximal dispatch: a single-literal rewrite target <c>⊤ -&gt; C(o3)</c> has one maximal head literal, so the unified dispatch enumerates exactly it and rewrites it to <c>C(o2)</c> under the merge equality <c>o3 ≈ o2</c>. The singleton shape routes through the same maximal enumeration and rewrite-slot gate as a multi-maximal target and leaves the conclusion choice-free.</summary>
    [TestMethod]
    public void SingletonMaximalTargetDispatchMatchesUnifiedPath()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewContext(isRoot: false);
        int equalityId = context.Insert(SourceMergeEquality(), isPredEligible: false, decidedUnderNoChoice: true, Selected());
        int targetId = context.Insert(SingletonTargetOnSource(), isPredEligible: false, decidedUnderNoChoice: true, Selected());

        DlLiteral actingEquality = DlLiteral.Equality(Other, FromTerm);
        int conclusionId = engine.RedriveDispatchEqOverTargetMaximal(context, equalityId, actingEquality, targetId, Other, FromTerm);

        Assert.AreNotEqual(-1, conclusionId, "The singleton-maximal target dispatches and lands a conclusion.");
        Assert.IsTrue(HeadContains(context.At(conclusionId), DlLiteral.Concept(ConceptCAtom, FromTerm)), "The sole maximal literal C(o3) is rewritten to C(o2).");
        Assert.IsFalse(context.IsDerivedUnderChoice(conclusionId), "The genuine acting equality passes validation, so the conclusion stays DecidedUnderNoChoice — no spurious origin taint.");
    }

    /// <summary>Builds an empty context the side-table, withhold, and redrive fixtures insert into.</summary>
    /// <param name="isRoot">Whether the context is a root-class context.</param>
    /// <returns>The fresh context.</returns>
    private static Context NewContext(bool isRoot)
    {
        return new Context(0, Array.Empty<DlLiteral>(), isRoot, -1, new HashSet<int>());
    }

    /// <summary>Inserts a body-nonempty conditional concept clause into a context as the taint premise — a shape neither projected into the unconditional-head set nor the root index, so it seeds the taint without perturbing the guard sites under test.</summary>
    /// <param name="context">The context to insert into.</param>
    /// <returns>The inserted clause's id.</returns>
    private static int InsertConditional(Context context)
    {
        return context.Insert(ConditionalConcept(ConceptCAtom, ConceptDAtom), isPredEligible: false, decidedUnderNoChoice: true, Selected());
    }

    /// <summary>A body-empty single-literal unconditional equality-head clause <c>⊤ -&gt; o2 ≈ o1</c>.</summary>
    /// <returns>The clause.</returns>
    private static DlClause UnconditionalEquality()
    {
        return DlClause.Create(Array.Empty<DlLiteral>(), new[] { DlLiteral.Equality(FromTerm, Replacement) }, DerivedOrigin);
    }

    /// <summary>A body-empty single-literal unconditional concept-head clause <c>⊤ -&gt; C(o1)</c>.</summary>
    /// <returns>The clause.</returns>
    private static DlClause UnconditionalConcept()
    {
        return DlClause.Create(Array.Empty<DlLiteral>(), new[] { DlLiteral.Concept(ConceptCAtom, Replacement) }, DerivedOrigin);
    }

    /// <summary>A body-empty single-literal unconditional concept-head clause <c>⊤ -&gt; A(x)</c> over the central variable for a chosen concept atom — the taint fold's conclusion shape.</summary>
    /// <param name="atom">The concept atom.</param>
    /// <returns>The clause.</returns>
    private static DlClause UnconditionalConceptOf(int atom)
    {
        return DlClause.Create(Array.Empty<DlLiteral>(), new[] { DlLiteral.Concept(atom, DlTerm.Central) }, DerivedOrigin);
    }

    /// <summary>A body-empty single-literal unconditional clause over a chosen head literal.</summary>
    /// <param name="head">The single head literal.</param>
    /// <returns>The clause.</returns>
    private static DlClause Unconditional(DlLiteral head)
    {
        return DlClause.Create(Array.Empty<DlLiteral>(), new[] { head }, DerivedOrigin);
    }

    /// <summary>A body-empty single-literal concept-head clause <c>⊤ -&gt; C(o2)</c> mentioning the rewrite source — the Eq redrive's rewrite target.</summary>
    /// <returns>The clause.</returns>
    private static DlClause ConceptOnFrom()
    {
        return DlClause.Create(Array.Empty<DlLiteral>(), new[] { DlLiteral.Concept(ConceptCAtom, FromTerm) }, DerivedOrigin);
    }

    /// <summary>A body-empty two-literal head clause <c>⊤ -&gt; D(x), o2 ≈ o1</c> — the Eq redrive's equality premise carrying an escape concept disjunct beside the acting equality, so the wrong-acting-literal firing (subtracting <c>D(x)</c>) drops a disjunct while the genuine firing (subtracting the equality) keeps it.</summary>
    /// <returns>The clause.</returns>
    private static DlClause ConceptAndEquality()
    {
        return DlClause.Create(Array.Empty<DlLiteral>(), new[] { DlLiteral.Concept(ConceptDAtom, DlTerm.Central), DlLiteral.Equality(FromTerm, Replacement) }, DerivedOrigin);
    }

    /// <summary>A body-empty single-literal unconditional equality-head clause <c>⊤ -&gt; o3 ≈ o2</c> — the merge equality of the two-maximal exerciser, sourcing the rewrite from <c>o3</c> to <c>o2</c> (<c>o3 ≻ o2</c>, so <c>o3</c> is the legal rewrite source).</summary>
    /// <returns>The clause.</returns>
    private static DlClause SourceMergeEquality()
    {
        return DlClause.Create(Array.Empty<DlLiteral>(), new[] { DlLiteral.Equality(Other, FromTerm) }, DerivedOrigin);
    }

    /// <summary>A body-empty two-literal head clause <c>⊤ -&gt; D(x), o3 ≈ o1</c> — the two-maximal rewrite target: the central-variable concept <c>D(x)</c> and the named-individual equality <c>o3 ≈ o1</c> are order-incomparable, so both are maximal, <c>D(x)</c> is the selected first-maximal, and the non-selected <c>o3 ≈ o1</c> is the source-bearing maximal the unified dispatch rewrites.</summary>
    /// <returns>The clause.</returns>
    private static DlClause TwoMaximalTargetOnSource()
    {
        return DlClause.Create(Array.Empty<DlLiteral>(), new[] { DlLiteral.Concept(ConceptDAtom, DlTerm.Central), DlLiteral.Equality(Other, Replacement) }, DerivedOrigin);
    }

    /// <summary>A body-empty single-literal concept-head clause <c>⊤ -&gt; C(o3)</c> mentioning the merge source <c>o3</c> — the singleton-maximal rewrite target whose sole head literal is its one maximal, so the acting-target dispatch enumerates exactly it.</summary>
    /// <returns>The clause.</returns>
    private static DlClause SingletonTargetOnSource()
    {
        return DlClause.Create(Array.Empty<DlLiteral>(), new[] { DlLiteral.Concept(ConceptCAtom, Other) }, DerivedOrigin);
    }

    /// <summary>A body-nonempty conditional concept clause <c>A(x) -&gt; B(x)</c> — a shape the Site D and root-index projections skip, so it seeds a taint premise cleanly.</summary>
    /// <param name="bodyAtom">The body concept atom.</param>
    /// <param name="headAtom">The head concept atom.</param>
    /// <returns>The clause.</returns>
    private static DlClause ConditionalConcept(int bodyAtom, int headAtom)
    {
        return DlClause.Create(new[] { DlLiteral.Concept(bodyAtom, DlTerm.Central) }, new[] { DlLiteral.Concept(headAtom, DlTerm.Central) }, DerivedOrigin);
    }

    /// <summary>The maximal-index list for a single-literal head — the sole head literal at index zero.</summary>
    /// <returns>The maximal-index list.</returns>
    private static List<int> Selected()
    {
        return [0];
    }

    /// <summary>The maximal-index list for a two-literal head — both literals maximal, so the redrive premise indexes fully.</summary>
    /// <returns>The maximal-index list.</returns>
    private static List<int> SelectedPair()
    {
        return [0, 1];
    }

    /// <summary>Whether a clause's head span carries a given literal — the two-maximal exerciser's disjunct-membership check over the canonical head.</summary>
    /// <param name="clause">The clause whose head is scanned.</param>
    /// <param name="literal">The literal to look for.</param>
    /// <returns><see langword="true"/> when the head contains the literal.</returns>
    private static bool HeadContains(DlClause clause, DlLiteral literal)
    {
        ReadOnlySpan<DlLiteral> head = clause.Head;
        for(int i = 0; i < head.Length; i++)
        {
            if(head[i].Equals(literal))
            {
                return true;
            }
        }

        return false;
    }
}
