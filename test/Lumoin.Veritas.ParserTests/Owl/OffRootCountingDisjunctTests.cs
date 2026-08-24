using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The off-root counting-merge disjunct-drop battery: the interaction-neighbourhood
/// grid around the F2 defect where an off-root successor context carrying an
/// unqualified <c>≤1 s</c> counting bound over NOMINAL successors, with a
/// disjunction that should permit an escape, must not spuriously derive an
/// unconditional <c>o1 ≈ o2</c> merge and decide a consistent module inconsistent.
/// Every row runs the plain <see cref="ContextSaturationModuleReasoner.DecideModule(ReasoningModule, System.Threading.CancellationToken)"/>
/// path with no <c>HasKey</c> bystander; the off-root rows wrap the counting class
/// under <c>A ⊑ ∃rf.F</c> plus <c>A(a)</c>, the root rows constrain the ABox
/// individual's class directly. The verdict tuple each row reads is
/// (<see cref="ModuleDecision.Outcome"/> / <c>ContextTotals.ContextDecided</c> /
/// <see cref="ModuleVerdict.IsConsistent"/>). The pinned repro (row 6) and the
/// over-delegation guard (row C) carry the exact measured triple; the sound floor
/// each pins in its own summary.
/// </summary>
[TestClass]
internal sealed class OffRootCountingDisjunctTests
{
    /// <summary>The MSTest-supplied per-test context, source of the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the classes, roles, and individuals are drawn from.</summary>
    private const string Example = "http://example.org/offrootcount#";

    /// <summary>Row 1: an off-root <c>≤1 s</c> counting bound over two DISTINCT named successors with NO escape disjunction merges the pair and collides with the told distinctness — a correct DECIDED-INCONSISTENT (the no-disjunction differential that isolates the disjunct as the escape the defect drops).</summary>
    [TestMethod]
    public void OffRootMaxOneNamedNoEscapeDecidesInconsistent()
    {
        ModuleDecision decision = Decide(
            SubClassOf(Class("A"), Some("rf", Class("F"))),
            SubClassOf(Class("F"), Some("s", OneOf("o1"))),
            SubClassOf(Class("F"), Some("s", OneOf("o2"))),
            SubClassOf(Class("F"), Max("s", 1, null)),
            ClassAssertion(Class("A"), Individual("a")),
            Different("o1", "o2"));

        AssertDecidedInconsistent(decision, "Two forced named s-successors under an unqualified ≤1 s bound with no escape merge and collide with the told DifferentIndividuals.");
    }

    /// <summary>Row 2: an off-root <c>≤2 s</c> bound over three pairwise-distinct named successors with no escape forces a merge by pigeonhole and collides with the told distinctness — a correct DECIDED-INCONSISTENT.</summary>
    [TestMethod]
    public void OffRootMaxTwoNamedThreeNoEscapeDecidesInconsistent()
    {
        ModuleDecision decision = Decide(
            SubClassOf(Class("A"), Some("rf", Class("F"))),
            SubClassOf(Class("F"), Some("s", OneOf("o1"))),
            SubClassOf(Class("F"), Some("s", OneOf("o2"))),
            SubClassOf(Class("F"), Some("s", OneOf("o3"))),
            SubClassOf(Class("F"), Max("s", 2, null)),
            ClassAssertion(Class("A"), Individual("a")),
            DifferentAll("o1", "o2", "o3"));

        AssertDecidedInconsistent(decision, "Three distinct named s-successors under ≤2 s force a merge by pigeonhole and collide with the told distinctness.");
    }

    /// <summary>Row 3: the root twin of row 1 — the counting bound constrains the ABox individual's own class, so the merge fires at the root and collides with the told distinctness — a correct DECIDED-INCONSISTENT that guards the fix against altering root behaviour.</summary>
    [TestMethod]
    public void RootMaxOneNamedNoEscapeDecidesInconsistent()
    {
        ModuleDecision decision = Decide(
            SubClassOf(Class("A"), Some("s", OneOf("o1"))),
            SubClassOf(Class("A"), Some("s", OneOf("o2"))),
            SubClassOf(Class("A"), Max("s", 1, null)),
            ClassAssertion(Class("A"), Individual("a")),
            Different("o1", "o2"));

        AssertDecidedInconsistent(decision, "The root-level ≤1 s bound over two distinct named successors merges the pair and collides with the told distinctness.");
    }

    /// <summary>Row 4: the root twin of row 2 — <c>≤2 s</c> over three distinct named successors at the root forces a merge and collides — a correct DECIDED-INCONSISTENT.</summary>
    [TestMethod]
    public void RootMaxTwoNamedThreeNoEscapeDecidesInconsistent()
    {
        ModuleDecision decision = Decide(
            SubClassOf(Class("A"), Some("s", OneOf("o1"))),
            SubClassOf(Class("A"), Some("s", OneOf("o2"))),
            SubClassOf(Class("A"), Some("s", OneOf("o3"))),
            SubClassOf(Class("A"), Max("s", 2, null)),
            ClassAssertion(Class("A"), Individual("a")),
            DifferentAll("o1", "o2", "o3"));

        AssertDecidedInconsistent(decision, "The root-level ≤2 s bound over three distinct named successors forces a merge by pigeonhole and collides with the told distinctness.");
    }

    /// <summary>Row 5: an off-root <c>≤1 s</c> bound over a single GENERATED (class-filler) successor with no escape is trivially satisfiable — nothing to merge — a DECIDED-CONSISTENT control that a generated successor never over-constrains.</summary>
    [TestMethod]
    public void OffRootMaxOneGeneratedNoEscapeConsistentRepresentative()
    {
        ModuleDecision decision = Decide(
            SubClassOf(Class("A"), Some("rf", Class("F"))),
            SubClassOf(Class("F"), Some("s", Class("B"))),
            SubClassOf(Class("F"), Max("s", 1, null)),
            ClassAssertion(Class("A"), Individual("a")));

        AssertDecidedConsistent(decision, "A single generated s-successor under ≤1 s leaves nothing to merge, so the module is consistent.");
    }

    /// <summary>
    /// Row 6 (F2 PIN, the acceptance pin): an off-root <c>≤1 s</c> bound over a named
    /// successor <c>o1</c> with the escape riding EDGE existence — <c>F ⊑ ∃s.{o2} ⊔ D</c>
    /// — must NOT decide inconsistent. A model exists in which the F-witness takes the
    /// <c>D</c> disjunct, retains only <c>o1</c>, and satisfies <c>≤1 s</c> with nothing
    /// to merge; the correct verdict is CONSISTENT. The BINDING acceptance observable
    /// (amendment A1) is NEGATIVE — the module must not decide inconsistent — and the
    /// exact triple below is the deterministically-measured post-fix outcome pinned as a
    /// regression. The sound floor is: any outcome other than DECIDED-INCONSISTENT
    /// (DECIDED-CONSISTENT preferred, a named delegation acceptable).
    /// </summary>
    [TestMethod]
    public void OffRootMaxOneNamedDisjunctiveEdgeSpuriousInconsistent()
    {
        ModuleDecision decision = Decide(
            SubClassOf(Class("A"), Some("rf", Class("F"))),
            SubClassOf(Class("F"), Some("s", OneOf("o1"))),
            SubClassOf(Class("F"), Union(Some("s", OneOf("o2")), Class("D"))),
            SubClassOf(Class("F"), Max("s", 1, null)),
            ClassAssertion(Class("A"), Individual("a")),
            Different("o1", "o2"));

        Assert.IsFalse(
            decision.Outcome == ReasoningDecisionOutcome.Decided && decision.Verdict is ModuleVerdict verdict && !verdict.IsConsistent,
            "The escape disjunct D lets the F-witness satisfy ≤1 s with only o1, so the module must not decide inconsistent — the negative soundness floor (amendment A1).");
        AssertDecidedConsistent(decision, "The post-fix engine retains the escape disjunct and saturates to a model, deciding the module consistent.");
    }

    /// <summary>Row 7: the root twin of row 6 — the disjunctive edge escape at the root — decides CONSISTENT both before and after the fix (the root single-root path already honours the acting-literal discipline). It guards the fix against over-correcting root behaviour.</summary>
    [TestMethod]
    public void RootMaxOneNamedDisjunctiveEdgeDecidesConsistent()
    {
        ModuleDecision decision = Decide(
            SubClassOf(Class("A"), Some("s", OneOf("o1"))),
            SubClassOf(Class("A"), Union(Some("s", OneOf("o2")), Class("D"))),
            SubClassOf(Class("A"), Max("s", 1, null)),
            ClassAssertion(Class("A"), Individual("a")),
            Different("o1", "o2"));

        AssertDecidedConsistent(decision, "The root-level escape disjunct retains a model in which only o1 is the s-successor, so the module is consistent.");
    }

    /// <summary>
    /// Row 7 lint-armed twin (the conditionality-loss lint's binding mechanism-detector
    /// observable driven on a real decision): the CONSISTENT root-placement control of
    /// row 7 run through the production <see cref="ContextSaturationModuleReasoner.DecideModule(ReasoningModule, EnumerationDeciderFaces, NominalParamodulationScope, RootContextTopology, RootPropagationRelevance, ReasoningBudget, SaturationEngineProbeDelegate, System.Threading.CancellationToken)"/>
    /// path with the dark conditionality-loss lint armed on the constructed engine before
    /// seeding, through the engine probe. The module still decides CONSISTENT — arming a
    /// dark census that only latches and counts moves no verdict — AND the lint fires: the
    /// correctly-consistent control's own emission latently narrows a disjunctive head, the
    /// mechanism the lint names. This drives the lint on a whole real saturation rather than a
    /// hand-built redrive fixture, proving it is a mechanism detector that fires on a
    /// derivation it does not turn into a wrong verdict.
    /// </summary>
    [TestMethod]
    public void RootMaxOneNamedDisjunctiveEdgeLintFiresYetDecidesConsistent()
    {
        EngineLintCapture capture = new();
        ModuleDecision decision = ContextSaturationModuleReasoner.DecideModule(
            new ReasoningModule(
            [
                SubClassOf(Class("A"), Some("s", OneOf("o1"))),
                SubClassOf(Class("A"), Union(Some("s", OneOf("o2")), Class("D"))),
                SubClassOf(Class("A"), Max("s", 1, null)),
                ClassAssertion(Class("A"), Individual("a")),
                Different("o1", "o2"),
            ], Violations: []),
            EnumerationDeciderFaces.None,
            NominalParamodulationScope.QueryScoped,
            RootContextTopology.SingleRoot,
            RootPropagationRelevance.Unrestricted,
            ReasoningConfiguration.Default.Budget,
            capture.Handle,
            TestContext.CancellationToken);

        AssertDecidedConsistent(decision, "Arming the dark conditionality-loss lint on the constructed engine moves no verdict: the root-level escape disjunct retains a model in which only o1 is the s-successor, so the module is consistent.");
        Assert.IsNotNull(capture.Engine, "The engine probe captured the engine the decision constructed (the enumeration decider faces are off, so the context arm always runs).");
        Assert.IsTrue(capture.Engine.HasConditionalityDropped, "The armed lint fires on the correctly-consistent root-placement control's own latent head-disjunct narrowing — a mechanism detected on a derivation that flips no verdict.");
        Assert.IsGreaterThanOrEqualTo(1, capture.Engine.ConditionalityDroppedCount, "The census counts the observed narrowing at least once.");
    }

    /// <summary>Row 8: an off-root <c>≤2 s</c> bound over two named successors plus a disjunctive edge escape on a third named successor — the F-witness takes D and keeps o1, o2 (two successors, within ≤2 s) — a DECIDED-CONSISTENT the defect otherwise flips to spurious inconsistent.</summary>
    [TestMethod]
    public void OffRootMaxTwoNamedThreeDisjunctiveEdgeConsistent()
    {
        ModuleDecision decision = Decide(
            SubClassOf(Class("A"), Some("rf", Class("F"))),
            SubClassOf(Class("F"), Some("s", OneOf("o1"))),
            SubClassOf(Class("F"), Some("s", OneOf("o2"))),
            SubClassOf(Class("F"), Union(Some("s", OneOf("o3")), Class("D"))),
            SubClassOf(Class("F"), Max("s", 2, null)),
            ClassAssertion(Class("A"), Individual("a")),
            DifferentAll("o1", "o2", "o3"));

        AssertDecidedConsistent(decision, "The escape disjunct D lets the F-witness retain o1 and o2 within ≤2 s, so the module is consistent.");
    }

    /// <summary>Row 9 (the nominal-specificity discriminator): the off-root disjunctive-edge shape with GENERATED successors and disjoint fillers decides CONSISTENT both before and after the fix — the defect is nominal-specific, so a generated successor never triggers it and the fix's blast radius is not widened.</summary>
    [TestMethod]
    public void OffRootMaxOneGeneratedDisjunctiveEdgeControlDiscriminator()
    {
        ModuleDecision decision = Decide(
            SubClassOf(Class("A"), Some("rf", Class("F"))),
            SubClassOf(Class("F"), Some("s", Class("B"))),
            SubClassOf(Class("F"), Union(Some("s", Class("C")), Class("D"))),
            SubClassOf(Class("F"), Max("s", 1, null)),
            Disjoint(Class("B"), Class("C")),
            ClassAssertion(Class("A"), Individual("a")));

        AssertDecidedConsistent(decision, "With generated successors the defect never fires: the F-witness takes D and keeps only the B-successor, so the module is consistent — the discriminator that bounds the fix to the named-nominal habitat.");
    }

    /// <summary>Row 10: an off-root <c>≤2 s</c> bound over one named and one generated successor plus a disjunctive edge escape on a second named successor — the F-witness takes D and keeps the named o1 and the generated successor within ≤2 s — a DECIDED-CONSISTENT confirming the defect fires (and the fix repairs it) whenever a NAMED nominal sits under the disjunction, even mixed with generated successors.</summary>
    [TestMethod]
    public void OffRootMaxTwoMixedThreeDisjunctiveEdgeConsistent()
    {
        ModuleDecision decision = Decide(
            SubClassOf(Class("A"), Some("rf", Class("F"))),
            SubClassOf(Class("F"), Some("s", OneOf("o1"))),
            SubClassOf(Class("F"), Some("s", Class("B"))),
            SubClassOf(Class("F"), Union(Some("s", OneOf("o2")), Class("D"))),
            SubClassOf(Class("F"), Max("s", 2, null)),
            ClassAssertion(Class("A"), Individual("a")),
            Different("o1", "o2"));

        AssertDecidedConsistent(decision, "The escape disjunct retains the named o1 and the generated B-successor within ≤2 s, so the mixed module is consistent.");
    }

    /// <summary>Row 13: the off-root disjunctive-edge shape whose escape rides a DATA existential — <c>F ⊑ ∃s.{o2} ⊔ ∃dp.(xsd:integer minExclusive 0)</c> — clausifies cleanly and decides CONSISTENT under the same emission fix (no separate data-path edit): the F-witness takes the data arm, keeps only o1, and satisfies <c>≤1 s</c>.</summary>
    [TestMethod]
    public void OffRootMaxOneNamedDataEscapeConsistent()
    {
        ModuleDecision decision = Decide(
            SubClassOf(Class("A"), Some("rf", Class("F"))),
            SubClassOf(Class("F"), Some("s", OneOf("o1"))),
            SubClassOf(Class("F"), Union(Some("s", OneOf("o2")), DataSome("dp", IntegerAbove(0)))),
            SubClassOf(Class("F"), Max("s", 1, null)),
            ClassAssertion(Class("A"), Individual("a")),
            Different("o1", "o2"));

        AssertDecidedConsistent(decision, "The data-existential escape lets the F-witness keep only o1 within ≤1 s, so the module is consistent — the defect fixed identically on the data arm.");
    }

    /// <summary>Row 16: the root twin of row 8 — <c>≤2 s</c> over two named successors plus a disjunctive edge escape on a third at the root — decides CONSISTENT both before and after the fix, guarding root behaviour.</summary>
    [TestMethod]
    public void RootMaxTwoNamedThreeDisjunctiveEdgeDecidesConsistent()
    {
        ModuleDecision decision = Decide(
            SubClassOf(Class("A"), Some("s", OneOf("o1"))),
            SubClassOf(Class("A"), Some("s", OneOf("o2"))),
            SubClassOf(Class("A"), Union(Some("s", OneOf("o3")), Class("D"))),
            SubClassOf(Class("A"), Max("s", 2, null)),
            ClassAssertion(Class("A"), Individual("a")),
            DifferentAll("o1", "o2", "o3"));

        AssertDecidedConsistent(decision, "The root-level escape disjunct retains o1 and o2 within ≤2 s, so the module is consistent.");
    }

    /// <summary>
    /// Row 12 (the plain <see cref="ContextSaturationModuleReasoner.DecideModule(ReasoningModule, System.Threading.CancellationToken)"/>-path
    /// counterpart of the qualified-filler backstop shape
    /// <see cref="ContextCooccurrenceLiftTests.Kvr16OffRootEqualityOffTheFoldLatchesBackstop"/>):
    /// a ROOT qualified <c>≤1 s.G</c> counting bound over two named successors
    /// <c>{o1}, {o2}</c>, each told merely <c>{oi} ⊑ G ⊔ H</c>, so the counted merge
    /// fires only when BOTH are G and stays UNDECIDED under the disjunction — a model
    /// keeps o1 as G and o2 as H within <c>≤1 s.G</c>. The differential from row 11
    /// (that Kvr16 backstop row): NO <c>HasKey</c> bystander and NO key-join backstop,
    /// so the module rides the plain path and any delegation is the general
    /// <c>RootEqualityRidesAChoice</c> latch, never the key-join backstop. The binding
    /// floor is NEGATIVE — never DECIDED-INCONSISTENT — and the verdict is consistent
    /// whether the context arm decides it or the reasoner delegates it named.
    /// </summary>
    [TestMethod]
    public void RootMaxOneQualifiedNamedDisjunctiveFillerDecidesOrDelegates()
    {
        ModuleDecision decision = Decide(
            SubClassOf(Class("A"), Some("s", OneOf("o1"))),
            SubClassOf(Class("A"), Some("s", OneOf("o2"))),
            SubClassOf(Class("A"), Max("s", 1, Class("G"))),
            SubClassOf(OneOf("o1"), Union(Class("G"), Class("H"))),
            SubClassOf(OneOf("o2"), Union(Class("G"), Class("H"))),
            ClassAssertion(Class("A"), Individual("a")),
            Different("o1", "o2"));

        AssertConsistentOrDelegated(decision, "Each named successor is only G ⊔ H, so the qualified ≤1 s.G merge stays undecided and the module must not decide inconsistent — a model keeps o1 as G and o2 as H within ≤1 s.G.");
    }

    /// <summary>
    /// Row 14 (the ROOT twin of the data-escape row 13
    /// <see cref="OffRootMaxOneNamedDataEscapeConsistent"/>): the disjunctive escape
    /// rides a DATA existential — <c>A ⊑ ∃s.{o2} ⊔ ∃dp.(xsd:integer minExclusive 0)</c>
    /// — directly on the ABox individual's class under a root <c>≤1 s</c> bound. The
    /// A-individual takes the data arm, keeps only o1, and satisfies <c>≤1 s</c> with
    /// nothing to merge, so the module is CONSISTENT. Measured: unlike the off-root
    /// row 13 (decided whole on the context arm), the root data existential is a real
    /// ABox datatype obligation on the A-individual, so the module delegates to the
    /// datatype-aware fallback and is decided consistent FRAGMENT-RELATIVE (a named
    /// datatype remainder scopes the verdict) — a pre-existing root data-arm behaviour,
    /// never inconsistent. The relay guard adds no movement here: the data-arm escape
    /// forces no named merge, so no choice-riding equality is tagged and the
    /// <c>RootEqualityRidesAChoice</c> relay guard never arms (its census stays zero).
    /// </summary>
    [TestMethod]
    public void RootMaxOneNamedDataEscapeDecidesConsistent()
    {
        ModuleDecision decision = Decide(
            SubClassOf(Class("A"), Some("s", OneOf("o1"))),
            SubClassOf(Class("A"), Union(Some("s", OneOf("o2")), DataSome("dp", IntegerAbove(0)))),
            SubClassOf(Class("A"), Max("s", 1, null)),
            ClassAssertion(Class("A"), Individual("a")),
            Different("o1", "o2"));

        AssertConsistentOrDelegated(decision, "The root-level data-existential escape lets the A-individual keep only o1 within ≤1 s, so the module is consistent (fragment-relative via the datatype-aware fallback), never inconsistent.");
        Assert.AreEqual(0L, decision.Statistics.ContextTotals.RootEqualityRidesAChoiceHeads, "The data-arm escape forces no named merge, so the RootEqualityRidesAChoice relay guard never arms — the consistency is reached without a choice-riding equality (no relay-guard movement on the root data twin).");
    }

    /// <summary>
    /// Row 15 (the plain <see cref="ContextSaturationModuleReasoner.DecideModule(ReasoningModule, System.Threading.CancellationToken)"/>-path
    /// three-nominal counterpart of the qualified-filler backstop shape
    /// <see cref="ContextCooccurrenceLiftTests.Kvr16OffRootEqualityOffTheFoldLatchesBackstop"/>):
    /// an OFF-ROOT qualified <c>≤2 s.G</c> bound over three pairwise-distinct named
    /// successors <c>{o1}, {o2}, {o3}</c>, each told merely <c>{oi} ⊑ G ⊔ H</c>. Which
    /// pair (if any) the bound merges among the three is a genuine choice, and each
    /// successor's G-membership is itself disjunctive, so no pair merges
    /// unconditionally and the module must not decide inconsistent. The differential
    /// from row 11 (that Kvr16 backstop row): NO <c>HasKey</c> bystander and NO
    /// key-join backstop, so any delegation is the general <c>RootEqualityRidesAChoice</c>
    /// latch on the plain path. The binding floor is NEGATIVE — never
    /// DECIDED-INCONSISTENT — with a consistent verdict, decided or delegated named.
    /// </summary>
    [TestMethod]
    public void OffRootMaxTwoQualifiedNamedThreeDisjunctiveFillerDelegates()
    {
        ModuleDecision decision = Decide(
            SubClassOf(Class("A"), Some("rf", Class("F"))),
            SubClassOf(Class("F"), Some("s", OneOf("o1"))),
            SubClassOf(Class("F"), Some("s", OneOf("o2"))),
            SubClassOf(Class("F"), Some("s", OneOf("o3"))),
            SubClassOf(Class("F"), Max("s", 2, Class("G"))),
            SubClassOf(OneOf("o1"), Union(Class("G"), Class("H"))),
            SubClassOf(OneOf("o2"), Union(Class("G"), Class("H"))),
            SubClassOf(OneOf("o3"), Union(Class("G"), Class("H"))),
            ClassAssertion(Class("A"), Individual("a")),
            DifferentAll("o1", "o2", "o3"));

        AssertConsistentOrDelegated(decision, "Each of the three named successors is only G ⊔ H and which pair the ≤2 s.G bound merges is a genuine choice, so no pair merges unconditionally and the module must not decide inconsistent.");
    }

    /// <summary>
    /// Row C (the over-delegation soundness gate, amendment A2): the CONVERGENT
    /// disjunction — <c>F ⊑ D1 ⊔ D2</c> where BOTH arms independently force
    /// <c>∃s.{o2}</c> — so the counted-successor merge <c>o1 ≈ o2</c> is entailed
    /// regardless of the disjunct choice. This correctly decides DECIDED-INCONSISTENT
    /// against the told distinctness, and the emission fix must PRESERVE that verdict:
    /// a DECIDED-CONSISTENT flip would be a new false-consistent (unsound, worse than
    /// the bug); a delegation flip would be a named completeness regression. The exact
    /// measured triple is pinned as the semantic-invariance hard gate's baseline.
    /// </summary>
    [TestMethod]
    public void ConvergentDisjunctionMaxOneNamedDecidesInconsistent()
    {
        ModuleDecision decision = Decide(
            SubClassOf(Class("A"), Some("rf", Class("F"))),
            SubClassOf(Class("F"), Some("s", OneOf("o1"))),
            SubClassOf(Class("F"), Union(Class("D1"), Class("D2"))),
            SubClassOf(Class("D1"), Some("s", OneOf("o2"))),
            SubClassOf(Class("D2"), Some("s", OneOf("o2"))),
            SubClassOf(Class("F"), Max("s", 1, null)),
            ClassAssertion(Class("A"), Individual("a")),
            Different("o1", "o2"));

        AssertDecidedInconsistent(decision, "Every arm of the convergent disjunction forces ∃s.{o2}, so o1 ≈ o2 is entailed regardless of choice and collides with the told distinctness — the emission fix must not over-generalize the escape to this shape.");
    }

    /// <summary>Asserts the decision is DECIDED-INCONSISTENT: the context arm decided the module whole and the verdict is inconsistent.</summary>
    /// <param name="decision">The module decision under test.</param>
    /// <param name="because">The soundness rationale for the row.</param>
    private static void AssertDecidedInconsistent(ModuleDecision decision, string because)
    {
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, because);
        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The context arm decided the module whole.");
        Assert.IsTrue(decision.Verdict is ModuleVerdict, "A decided module carries a verdict.");
        Assert.IsFalse(decision.Verdict!.IsConsistent, because);
    }

    /// <summary>Asserts the decision is DECIDED-CONSISTENT via the context arm: the context arm decided the module whole and the verdict is consistent.</summary>
    /// <param name="decision">The module decision under test.</param>
    /// <param name="because">The soundness rationale for the row.</param>
    private static void AssertDecidedConsistent(ModuleDecision decision, string because)
    {
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, because);
        Assert.IsTrue(decision.Statistics.ContextTotals.ContextDecided, "The context arm decided the module whole.");
        Assert.IsTrue(decision.Verdict is ModuleVerdict, "A decided module carries a verdict.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, because);
    }

    /// <summary>Asserts the module is NOT DECIDED-INCONSISTENT and carries a consistent verdict, without pinning <c>ContextDecided</c>: the acceptable floor for a row whose named-nominal disjunction leaves a counted merge undecided, so the module either decides consistent on the context arm or delegates named through the general <c>RootEqualityRidesAChoice</c> latch — both land a consistent verdict, and only a spurious DECIDED-INCONSISTENT is the failure.</summary>
    /// <param name="decision">The module decision under test.</param>
    /// <param name="because">The soundness rationale for the row.</param>
    private static void AssertConsistentOrDelegated(ModuleDecision decision, string because)
    {
        Assert.IsTrue(decision.Verdict is ModuleVerdict, "A resolved module carries a verdict, whether the context arm decided it or the reasoner delegated it named.");
        Assert.IsTrue(decision.Verdict!.IsConsistent, because);
    }

    /// <summary>Decides a module over the axioms through the plain context-saturation reasoner path with no HasKey bystander.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module decision.</returns>
    private ModuleDecision Decide(params OwlAxiom[] axioms)
    {
        return ContextSaturationModuleReasoner.DecideModule(new ReasoningModule([.. axioms], Violations: []), TestContext.CancellationToken);
    }

    /// <summary>Captures the saturation engine a module decision constructs and arms the dark conditionality-loss lint on it before seeding, so a row can read the lint's census off the engine after the decision returns; a key-merge fixpoint invokes the probe per round and the last engine wins. Arming the lint only latches and counts, never gates, so the captured decision is verdict-identical to the unarmed production run.</summary>
    private sealed class EngineLintCapture
    {
        /// <summary>The last engine the decision constructed, or <see langword="null"/> before the first round.</summary>
        public ContextSaturationEngine? Engine { get; private set; }

        /// <summary>Receives one constructed engine before seeding, arming the conditionality-loss lint on it and keeping it for the post-run census read.</summary>
        /// <param name="engine">The created engine.</param>
        public void Handle(ContextSaturationEngine engine)
        {
            Engine = engine;
            engine.RedriveArmConditionalityLint();
        }
    }

    /// <summary>A provenance quad naming an axiom's origin.</summary>
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

    /// <summary>A named object property reference in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The reference.</returns>
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

    /// <summary>A named data property in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The data property node.</returns>
    private static NamedNode DataProperty(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>A subclass axiom.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>A class assertion typing a named individual.</summary>
    /// <param name="type">The asserted type.</param>
    /// <param name="individual">The individual term.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, RdfTerm individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

    /// <summary>An existential restriction over a forward role — the successor-forcing shape.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler class.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>An enumeration of named individuals in the example namespace — the <c>ObjectOneOf</c> nominal construct.</summary>
    /// <param name="individuals">The enumerated individuals' local names.</param>
    /// <returns>The enumeration expression.</returns>
    private static OwlObjectOneOf OneOf(params string[] individuals)
    {
        RdfTerm[] terms = new RdfTerm[individuals.Length];
        for(int index = 0; index < individuals.Length; index++)
        {
            terms[index] = Individual(individuals[index]);
        }

        return new OwlObjectOneOf(terms);
    }

    /// <summary>An object union of two class expressions — the <c>ObjectUnionOf</c> disjunction the escape rides.</summary>
    /// <param name="first">The first class expression.</param>
    /// <param name="second">The second class expression.</param>
    /// <returns>The union expression.</returns>
    private static OwlObjectUnionOf Union(OwlClassExpression first, OwlClassExpression second)
    {
        return new OwlObjectUnionOf([first, second]);
    }

    /// <summary>An unqualified maximum-cardinality restriction over a forward role — the counting bound that merges role successors into one, deriving their equality in the restriction-bearing context.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="cardinality">The bound n.</param>
    /// <param name="filler">The filler class, or <see langword="null"/> for the unqualified form.</param>
    /// <returns>The forward maximum-cardinality restriction.</returns>
    private static OwlObjectCardinality Max(string property, int cardinality, OwlClassExpression? filler)
    {
        return new OwlObjectCardinality(OwlCardinalityKind.Max, cardinality, Property(property), filler);
    }

    /// <summary>A different-individuals axiom asserting two named individuals distinct.</summary>
    /// <param name="first">The first individual's local name.</param>
    /// <param name="second">The second individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlDifferentIndividualsAxiom Different(string first, string second)
    {
        return new OwlDifferentIndividualsAxiom([Individual(first), Individual(second)]) { Origin = Origin("different") };
    }

    /// <summary>A different-individuals axiom asserting three named individuals pairwise distinct.</summary>
    /// <param name="first">The first individual's local name.</param>
    /// <param name="second">The second individual's local name.</param>
    /// <param name="third">The third individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlDifferentIndividualsAxiom DifferentAll(string first, string second, string third)
    {
        return new OwlDifferentIndividualsAxiom([Individual(first), Individual(second), Individual(third)]) { Origin = Origin("different") };
    }

    /// <summary>A disjoint-classes axiom asserting two class expressions have no common member — the closure that keeps two generated successors from being identified.</summary>
    /// <param name="first">The first class expression.</param>
    /// <param name="second">The second class expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlDisjointClassesAxiom Disjoint(OwlClassExpression first, OwlClassExpression second)
    {
        return new OwlDisjointClassesAxiom([first, second]) { Origin = Origin("disjoint") };
    }

    /// <summary>A single-property data existential (<c>DataSomeValuesFrom</c>) — the value-forcing escape the disjunction rides on the data arm.</summary>
    /// <param name="property">The data property's local name.</param>
    /// <param name="range">The demanded range.</param>
    /// <returns>The class expression.</returns>
    private static OwlDataSomeValuesFrom DataSome(string property, OwlDataRange range)
    {
        return new OwlDataSomeValuesFrom([DataProperty(property)], range);
    }

    /// <summary>An integer range bounded below exclusively — a value strictly above the bound.</summary>
    /// <param name="bound">The exclusive lower bound.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction IntegerAbove(int bound)
    {
        List<OwlFacetRestriction> facets =
        [
            new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MinExclusive), new Literal(Utf8Strings.From(bound.ToString(CultureInfo.InvariantCulture)), new NamedNode(Vocabulary.Xsd.Integer))),
        ];

        return new OwlDatatypeRestriction(new NamedNode(Vocabulary.Xsd.Integer), facets);
    }
}
