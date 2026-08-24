using System;
using System.Collections.Generic;
using Lumoin.Veritas.Owl.Contexts;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The conditionality-loss lint exercisers: a ground-truth-free mechanism census over the
/// <see cref="ContextSaturationEngine"/> AddClause funnel that counts derivation steps whose
/// conclusion head is strictly narrower in choice-conditions (fewer head disjuncts) than their
/// widest same-context premise. Every row drives the dark lint through the internal redrive seam
/// — <see cref="ContextSaturationEngine.CreateForOriginRedrive"/>, the internal
/// <c>RedriveArmConditionalityLint</c> switch, and <see cref="ContextSaturationEngine.RedriveAddClause"/>
/// — reusing the origin-bit battery's premise and conclusion fixture shapes. The armed row fires on
/// the legitimate post-fix <c>D(x)</c> narrowing (a genuine complementary resolution that flips no
/// verdict), proving the lint is a mechanism detector rather than a verdict oracle; the boundary rows
/// pin the strict-decrease predicate at equal width, empty-head refutation, and premise-free told
/// clauses; the unarmed row pins the zero-cost default. Every assertion is a census observation
/// (the sticky latch and its count), never a module verdict.
/// </summary>
[TestClass]
internal sealed class ConditionalityLossLintTests
{
    /// <summary>The clause origin marker the fixtures stamp; the origin value is inert for the lint census under test.</summary>
    private const int DerivedOrigin = -1;

    /// <summary>The concept-atom id the fixtures build a <c>C(·)</c> head from.</summary>
    private const int ConceptCAtom = 5;

    /// <summary>The concept-atom id the fixtures build the escape-disjunct <c>D(·)</c> head from.</summary>
    private const int ConceptDAtom = 6;

    /// <summary>The concept-atom id the equal-width fixture builds a distinct <c>E(·)</c> head from.</summary>
    private const int ConceptEAtom = 7;

    /// <summary>The rewrite source term the equality premise merges from — a stand-in for <c>o2</c>.</summary>
    private static DlTerm FromTerm { get; } = DlTerm.Individual(2);

    /// <summary>The rewrite replacement term the equality premise merges to — a stand-in for <c>o1</c>.</summary>
    private static DlTerm Replacement { get; } = DlTerm.Individual(1);

    /// <summary>The armed lint fires on the harmless <c>D(x), o2 ≈ o1</c> (head 2) ⟶ <c>D(x)</c> (head 1) narrowing — the legitimate complementary resolution the correct engine performs — charging the sticky latch and its count exactly once. This is the mechanism-detector observable: the lint fires on a step that flips no verdict.</summary>
    [TestMethod]
    public void ArmedLintFiresOnHarmlessDisjunctNarrowing()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewContext(isRoot: false);
        int premiseId = context.Insert(ConceptAndEquality(), isPredEligible: false, decidedUnderNoChoice: true, SelectedPair());
        engine.RedriveArmConditionalityLint();

        engine.RedriveAddClause(context, UnconditionalConceptOf(ConceptDAtom), [premiseId]);

        Assert.IsTrue(engine.HasConditionalityDropped, "A head-1 conclusion from a head-2 premise is a strict choice-condition narrowing the armed lint counts.");
        Assert.AreEqual(1L, engine.ConditionalityDroppedCount, "The single narrowing step charges the census exactly once.");
    }

    /// <summary>The same head-2 ⟶ head-1 narrowing driven WITHOUT arming leaves the latch and its count at their zero-cost default — the dark seam pays nothing when unarmed.</summary>
    [TestMethod]
    public void UnarmedLintStaysSilentAndZero()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewContext(isRoot: false);
        int premiseId = context.Insert(ConceptAndEquality(), isPredEligible: false, decidedUnderNoChoice: true, SelectedPair());

        engine.RedriveAddClause(context, UnconditionalConceptOf(ConceptDAtom), [premiseId]);

        Assert.IsFalse(engine.HasConditionalityDropped, "An unarmed engine never charges the latch.");
        Assert.AreEqual(0L, engine.ConditionalityDroppedCount, "An unarmed engine leaves the census at zero.");
    }

    /// <summary>A conclusion whose head width equals its widest premise's (head 1 from a head-1 premise) is not a strict narrowing, so the armed lint does not fire — the <c>&lt;</c> boundary of the predicate.</summary>
    [TestMethod]
    public void EqualWidthConclusionDoesNotFireLint()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewContext(isRoot: false);
        int premiseId = context.Insert(ConditionalConcept(ConceptCAtom, ConceptDAtom), isPredEligible: false, decidedUnderNoChoice: true, Selected());
        engine.RedriveArmConditionalityLint();

        engine.RedriveAddClause(context, UnconditionalConceptOf(ConceptEAtom), [premiseId]);

        Assert.IsFalse(engine.HasConditionalityDropped, "An equal-width conclusion is not a strict narrowing, so the lint does not fire.");
        Assert.AreEqual(0L, engine.ConditionalityDroppedCount, "No equal-width step charges the census.");
    }

    /// <summary>An empty-head conclusion (a complementary resolution / refutation) from a head-2 premise does not fire the armed lint — the <c>Head.Length &gt;= 1</c> exclusion keeps the lint from firing on every inconsistency proof.</summary>
    [TestMethod]
    public void EmptyHeadRefutationDoesNotFireLint()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewContext(isRoot: false);
        int premiseId = context.Insert(ConceptAndEquality(), isPredEligible: false, decidedUnderNoChoice: true, SelectedPair());
        engine.RedriveArmConditionalityLint();

        engine.RedriveAddClause(context, EmptyHeadConstraint(), [premiseId]);

        Assert.IsFalse(engine.HasConditionalityDropped, "An empty-head refutation is excluded, so the lint does not fire.");
        Assert.AreEqual(0L, engine.ConditionalityDroppedCount, "No refutation step charges the census.");
    }

    /// <summary>A premise-free told clause has a zero widest-premise head, so the strict-narrowing predicate never holds and the armed lint does not fire.</summary>
    [TestMethod]
    public void NoPremiseToldClauseDoesNotFireLint()
    {
        ContextSaturationEngine engine = ContextSaturationEngine.CreateForOriginRedrive();
        Context context = NewContext(isRoot: false);
        engine.RedriveArmConditionalityLint();

        engine.RedriveAddClause(context, UnconditionalConceptOf(ConceptDAtom), []);

        Assert.IsFalse(engine.HasConditionalityDropped, "A premise-free told clause has a zero widest-premise head, so the lint does not fire.");
        Assert.AreEqual(0L, engine.ConditionalityDroppedCount, "No premise-free step charges the census.");
    }

    /// <summary>Builds an empty context the lint fixtures insert into.</summary>
    /// <param name="isRoot">Whether the context is a root-class context.</param>
    /// <returns>The fresh context.</returns>
    private static Context NewContext(bool isRoot)
    {
        return new Context(0, Array.Empty<DlLiteral>(), isRoot, -1, new HashSet<int>());
    }

    /// <summary>A body-empty two-literal head clause <c>⊤ -&gt; D(x), o2 ≈ o1</c> — the head-2 premise carrying an escape concept disjunct beside the equality, the widest premise the narrowing lint measures against.</summary>
    /// <returns>The clause.</returns>
    private static DlClause ConceptAndEquality()
    {
        return DlClause.Create(Array.Empty<DlLiteral>(), new[] { DlLiteral.Concept(ConceptDAtom, DlTerm.Central), DlLiteral.Equality(FromTerm, Replacement) }, DerivedOrigin);
    }

    /// <summary>A body-empty single-literal unconditional concept-head clause <c>⊤ -&gt; A(x)</c> over the central variable for a chosen concept atom — the head-1 conclusion shape.</summary>
    /// <param name="atom">The concept atom.</param>
    /// <returns>The clause.</returns>
    private static DlClause UnconditionalConceptOf(int atom)
    {
        return DlClause.Create(Array.Empty<DlLiteral>(), new[] { DlLiteral.Concept(atom, DlTerm.Central) }, DerivedOrigin);
    }

    /// <summary>A body-nonempty conditional concept clause <c>A(x) -&gt; B(x)</c> — a head-1 premise shape the projections skip, so it seeds the equal-width comparison cleanly.</summary>
    /// <param name="bodyAtom">The body concept atom.</param>
    /// <param name="headAtom">The head concept atom.</param>
    /// <returns>The clause.</returns>
    private static DlClause ConditionalConcept(int bodyAtom, int headAtom)
    {
        return DlClause.Create(new[] { DlLiteral.Concept(bodyAtom, DlTerm.Central) }, new[] { DlLiteral.Concept(headAtom, DlTerm.Central) }, DerivedOrigin);
    }

    /// <summary>A body-nonempty empty-head constraint clause <c>C(x) -&gt; ⊥</c> — the head-0 refutation shape the lint excludes.</summary>
    /// <returns>The clause.</returns>
    private static DlClause EmptyHeadConstraint()
    {
        return DlClause.Create(new[] { DlLiteral.Concept(ConceptCAtom, DlTerm.Central) }, Array.Empty<DlLiteral>(), DerivedOrigin);
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
}
