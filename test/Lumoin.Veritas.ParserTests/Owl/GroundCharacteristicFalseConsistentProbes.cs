using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Adversarial FALSE-CONSISTENT / gate-leak probes for the asymmetric/irreflexive ground-graph
/// characteristic tier. Every module here has NO model — an explicit unsat derivation is in each
/// comment, independent of the characteristic-blind tableau. The dangerous outcome is a module the
/// tier DECIDES (<c>ElTotals.ElDecided</c> true) and returns CONSISTENT: that is a whole-module
/// unsound claim, a BLOCKER. A module that DELEGATES (ElDecided false) is honest abstention even
/// though the blind fallback then answers it consistent, so delegation is NOT a defect here. The
/// failing condition is therefore precisely <c>ElDecided AND the decided verdict disagrees with the
/// hand-derived ground truth</c>.
/// </summary>
[TestClass]
internal sealed class GroundCharacteristicFalseConsistentProbes
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Every inconsistent module here must be either decided INCONSISTENT (the capability gain) or
    /// delegated (honest abstention) — never DECIDED CONSISTENT. The single consistent control must be
    /// decided consistent, proving the tier does not merely delegate everything. The report names any
    /// module whose decided verdict contradicts its hand-built ground truth.
    /// </summary>
    [TestMethod]
    public void InconsistentGroundCharacteristicModulesAreNeverDecidedConsistent()
    {
        (string Name, ReasoningModule Module, bool TrueConsistent)[] cases =
        [
            //L1 — Symmetric(r) forces r(b, a) from r(a, b); Asymmetric(r) forbids the coexisting reverse.
            //No model. The forced-empty reduction decides r empty (symmetric-in-effect × asymmetric), so the
            //seeded ∃r.⊤ ⊑ ⊥ condemns a, the source of the asserted r(a, b): decided INCONSISTENT via the
            //reduction, matching the hand-derived truth.
            ("L1_SymmetricPlusAsymmetric", Module(
                Symmetric("r"),
                Asymmetric("r"),
                Edge("a", "r", "b")), false),

            //L2 — Inverse(r, q) makes q(a, b) force r(b, a); with r(a, b) asserted, Asymmetric(r) clashes.
            //The reverse r-edge is mirror-created. Must delegate (r is a mirror target).
            ("L2_InversePairAsymmetric", Module(
                Inverse("r", "q"),
                Asymmetric("r"),
                Edge("a", "r", "b"),
                Edge("a", "q", "b")), false),

            //L3 — s⁻ ⊑ r makes s(a, b) force r(b, a); with r(a, b) asserted, Asymmetric(r) clashes. The
            //mirror-target arm must catch r (r receives the one-directional mirror). Must delegate.
            ("L3_OneDirInverseSubAsymmetric", Module(
                InverseSubProperty("s", "r"),
                Asymmetric("r"),
                Edge("a", "s", "b"),
                Edge("a", "r", "b")), false),

            //L4 — Transitive(r) composes r(a, c) from r(a, b), r(b, c); with r(c, a) asserted the pair
            //r(a, c), r(c, a) violates Asymmetric(r). The composed edge is saturation-created (r is
            //edge-generating via the transitivity chain), so must delegate — never decided consistent.
            ("L4_TransitiveAsymmetricCycle", Module(
                Transitive("r"),
                Asymmetric("r"),
                Edge("a", "r", "b"),
                Edge("b", "r", "c"),
                Edge("c", "r", "a")), false),

            //L5 — Transitive(s), s ⊑ r, Asymmetric(r): s(a, b), s(b, a) promote to r(a, b), r(b, a) (scan-
            //visible reverse) AND s composes s(a, a) -> r(a, a) (self-edge). Either route condemns. s is
            //edge-generating (chain) so r is too via upward closure; decided-inconsistent or delegated,
            //never decided-consistent.
            ("L5_TransitiveSubRoleAsymmetricSuper", Module(
                Transitive("s"),
                SubProperty("s", "r"),
                Asymmetric("r"),
                Edge("a", "s", "b"),
                Edge("b", "s", "a")), false),

            //L6 — Chain p∘q ⊑ r, p(a, b), q(b, a) compose r(a, a): a self-edge on the asymmetric r. The
            //composed edge is saturation-created (r is a chain conclusion, edge-generating). Must delegate.
            ("L6_ChainConclusionAsymmetricSelf", Module(
                Chain("r", "p", "q"),
                Asymmetric("r"),
                Edge("a", "p", "b"),
                Edge("b", "q", "a")), false),

            //L7 — B ⊑ ∃r.Self and x : B force r(x, x); Irreflexive(r) forbids it. The class-level Self
            //demand is edge-generating, so r must delegate. Must NOT decide consistent.
            ("L7_ClassSelfDemandIrreflexive", Module(
                SubClassOf(Class("B"), HasSelf("r")),
                Irreflexive("r"),
                ClassAssertion(Class("B"), Individual("x"))), false),

            //L8 — asserted x : ∃r.Self forces the self-edge r(x, x); Irreflexive(r) forbids it. The
            //assertion route registers a self-demand on x, making r edge-generating. Delegated or
            //decided-inconsistent, never decided-consistent.
            ("L8_AssertedSelfDemandIrreflexive", Module(
                ClassAssertion(HasSelf("r"), Individual("x")),
                Irreflexive("r")), false),

            //L9 — x : (B ⊓ ObjectHasValue(r, a)) seeds the asserted edge r(x, a); r(a, x) asserted. The
            //pair r(x, a), r(a, x) violates Asymmetric(r). The nested HasValue must reach AssertedEdges
            //so the scan decides INCONSISTENT. A vanished nested edge would be a decided-consistent BLOCKER.
            ("L9_NestedHasValueAsymmetric", Module(
                ClassAssertion(IntersectionOf(Class("B"), HasValue("r", "a")), Individual("x")),
                Edge("a", "r", "x"),
                Asymmetric("r")), false),

            //L10 — ⊤ ⊑ ∃r.Self forces r(x, x) on every domain element (non-empty domain); Irreflexive(r)
            //forbids it. The told check decides INCONSISTENT outright. A miss would be decided-consistent.
            ("L10_ToldGlobalSelfIrreflexive", Module(
                TopSubClassOfHasSelf("r"),
                Irreflexive("r")), false),

            //L11 — Reflexive(r) forces r(x, x) on every element; Irreflexive(r) forbids it. TBox-only
            //told clash. Must be decided inconsistent, never consistent.
            ("L11_ReflexiveIrreflexiveSameRole", Module(
                Reflexive("r"),
                Irreflexive("r")), false),

            //L12 — q ≡ r makes q(b, a) the r-edge r(b, a); with r(a, b) asserted, Asymmetric(r) clashes.
            //Both are asserted ground edges over the equivalent (mutual sub) roles, so the scan decides
            //INCONSISTENT.
            ("L12_EquivalentPropertyReverseAsymmetric", Module(
                EquivalentProperties("q", "r"),
                Asymmetric("r"),
                Edge("a", "r", "b"),
                Edge("b", "q", "a")), false),

            //L13 — SameIndividual(a, c) merges a = c; the asserted r(a, b), r(b, c) become r(a, b),
            //r(b, a) — a reverse pair over the asymmetric r. Post-merge scan decides INCONSISTENT.
            ("L13_MergeCreatedReverseAsymmetric", Module(
                SameIndividual(Individual("a"), Individual("c")),
                Asymmetric("r"),
                Edge("a", "r", "b"),
                Edge("b", "r", "c")), false),

            //L14 — s1 ⊑ r, s2 ⊑ r, Asymmetric(r), s1(a, b), s2(b, a): both promote to r, giving the
            //reverse pair r(a, b), r(b, a). Scan over the sub-role closure decides INCONSISTENT.
            ("L14_ReversePairViaTwoSubRoles", Module(
                SubProperty("s1", "r"),
                SubProperty("s2", "r"),
                Asymmetric("r"),
                Edge("a", "s1", "b"),
                Edge("b", "s2", "a")), false),

            //L15 — Functional(f), f(x, a), f(x, b) collapse a = b; the asserted r(a, b) becomes r(a, a) —
            //a self-edge over the irreflexive r. The post-fixpoint merge must be seen by the scan
            //(decided inconsistent), not missed (decided consistent).
            ("L15_FunctionalCollapseSelfEdgeIrreflexive", Module(
                Functional("f"),
                Edge("x", "f", "a"),
                Edge("x", "f", "b"),
                Irreflexive("r"),
                Edge("a", "r", "b")), false),

            //L16 — CONSISTENT control: model Δ = {a, b, c}, r = {(a, b), (b, c)}. A one-directional chain
            //over an asymmetric role, no reverse, no self-edge. Must be DECIDED consistent (the tier does
            //not merely delegate everything).
            ("L16_ConsistentControlOneDirectional", Module(
                Asymmetric("r"),
                Edge("a", "r", "b"),
                Edge("b", "r", "c")), true),

            //L17 — s⁻ ⊑ r with Irreflexive(r): s(a, a) forces r(a, a) (the reverse of a self-edge is
            //itself), a self-edge over the irreflexive r. The reverse s-edge onto r is mirror-created,
            //so r must delegate (r is a mirror target); it must not decide consistent.
            ("L17_InverseSubSelfIrreflexive", Module(
                InverseSubProperty("s", "r"),
                Irreflexive("r"),
                Edge("a", "s", "a")), false),

            //L18 — the ORDER hazard: a two-step functional collapse f(x, a), f(x, b) => a = b, then
            //f(a, c), f(b, d) => c = d. With Asymmetric(r), r(c, e), r(e, d): post-fixpoint c = d makes
            //the pair r(c, e), r(e, c) a forbidden reverse. The scan must run over the FIXPOINT
            //identities, not a pre-merge snapshot, or it misses the clash and decides consistent.
            ("L18_TwoStepCollapseReverseAsymmetric", Module(
                Functional("f"),
                Edge("x", "f", "a"),
                Edge("x", "f", "b"),
                Edge("a", "f", "c"),
                Edge("b", "f", "d"),
                Asymmetric("r"),
                Edge("c", "r", "e"),
                Edge("e", "r", "d")), false),

            //L19 — scan-beats-gate: A ⊑ ∃r.B makes r edge-generating (the gate alone would delegate), but
            //the asserted r(a, b), r(b, a) is already a reverse pair. The scan runs BEFORE the gate, so the
            //module must be DECIDED inconsistent — and above all must not be decided consistent.
            ("L19_ScanBeatsGateAsymmetric", Module(
                Asymmetric("r"),
                SubClassOf(Class("A"), Some("r", Class("B"))),
                Edge("a", "r", "b"),
                Edge("b", "r", "a")), false),

            //L20 — a superclass inverse existential A ⊑ ∃r⁻.C admits per-owner minting (r becomes the
            //generator's mirror target), co-existing with Asymmetric(r) and an asserted reverse pair
            //r(m, n), r(n, m). Even with minting admitted, the constrained r must be caught — the scan
            //decides the asserted reverse, or the gate delegates. Never decided consistent.
            ("L20_MintingAdmittedPlusAsymmetricReverse", Module(
                SubClassOf(Class("A"), SomeInverse("r", Class("C"))),
                ClassAssertion(Class("A"), Individual("o")),
                Asymmetric("r"),
                Edge("m", "r", "n"),
                Edge("n", "r", "m")), false),
        ];

        System.Text.StringBuilder report = new();
        report.AppendLine("\ncase | trueConsistent | finalConsistent | path | tableau | verdict");
        List<string> blockers = [];
        foreach((string name, ReasoningModule module, bool trueConsistent) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            bool elDecided = decision.Statistics.ElTotals.ElDecided;
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool tableauConsistent = AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent;
            string path = elDecided ? "Decided" : "Delegated";

            //A decided verdict must equal the hand-derived truth; a delegated verdict is honest
            //abstention regardless of what the blind fallback answered.
            bool blocker = elDecided && finalConsistent != trueConsistent;
            string verdict = blocker
                ? (trueConsistent ? "FALSE-INCONSISTENT(decided)" : "FALSE-CONSISTENT(decided)")
                : "OK";
            report.AppendLine(name + " | " + trueConsistent + " | " + finalConsistent + " | " + path + " | " + tableauConsistent + " | " + verdict);
            if(blocker)
            {
                blockers.Add(name + ": elDecided=True finalConsistent=" + finalConsistent + " trueConsistent=" + trueConsistent + " (" + verdict + ")");
            }
        }

        Assert.IsEmpty(blockers, report.ToString());
    }

    //Local minimal replicas of the private construction helpers in ElCoupledModuleReasonerTests.

    /// <summary>The IRI prefix the probe classes, roles, and individuals live under.</summary>
    private const string Example = "http://example.org/";

    /// <summary>The fixed-⊤ class reference, <c>owl:Thing</c>.</summary>
    private static OwlClassReference ThingReference { get; } = new(new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#Thing")));

    /// <summary>A distinct origin quad for the marker name.</summary>
    /// <param name="marker">The distinguishing marker.</param>
    /// <returns>The origin quad.</returns>
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

    /// <summary>A named individual in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The node.</returns>
    private static NamedNode Individual(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>An existential restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectSomeValuesFrom Some(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(Property(property), filler);
    }

    /// <summary>An inverse existential restriction <c>∃r⁻.C</c> over an <c>ObjectInverseOf</c> property expression.</summary>
    /// <param name="property">The forward role's local name.</param>
    /// <param name="filler">The filler.</param>
    /// <returns>The inverse existential restriction.</returns>
    private static OwlObjectSomeValuesFrom SomeInverse(string property, OwlClassExpression filler)
    {
        return new OwlObjectSomeValuesFrom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + property))), filler);
    }

    /// <summary>An individual-value restriction <c>ObjectHasValue(r, a)</c>.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="individual">The required value individual's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasValue HasValue(string property, string individual)
    {
        return new OwlObjectHasValue(Property(property), Individual(individual));
    }

    /// <summary>A self-restriction over a forward role.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasSelf HasSelf(string property)
    {
        return new OwlObjectHasSelf(Property(property));
    }

    /// <summary>A conjunction of class expressions.</summary>
    /// <param name="operands">The conjuncts.</param>
    /// <returns>The intersection.</returns>
    private static OwlObjectIntersectionOf IntersectionOf(params OwlClassExpression[] operands)
    {
        return new OwlObjectIntersectionOf(operands);
    }

    /// <summary>A subclass inclusion.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>A superclass-position self-restriction demand <c>⊤ ⊑ ∃r.Self</c> — global reflexivity spelled through <c>ObjectHasSelf</c>.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom TopSubClassOfHasSelf(string property)
    {
        return new OwlSubClassOfAxiom(ThingReference, new OwlObjectHasSelf(Property(property))) { Origin = Origin("topself") };
    }

    /// <summary>A reflexive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Reflexive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Reflexive, Property(property)) { Origin = Origin("reflexive") };
    }

    /// <summary>A symmetric-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Symmetric(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Symmetric, Property(property)) { Origin = Origin("symmetric") };
    }

    /// <summary>A transitive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Transitive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Transitive, Property(property)) { Origin = Origin("transitive") };
    }

    /// <summary>A functional-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Functional(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Functional, Property(property)) { Origin = Origin("functional") };
    }

    /// <summary>An asymmetric-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Asymmetric(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Asymmetric, Property(property)) { Origin = Origin("asymmetric") };
    }

    /// <summary>An irreflexive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Irreflexive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Irreflexive, Property(property)) { Origin = Origin("irreflexive") };
    }

    /// <summary>A subrole inclusion <c>sub ⊑ super</c>.</summary>
    /// <param name="sub">The subrole's local name.</param>
    /// <param name="super">The superrole's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubObjectPropertyOfAxiom SubProperty(string sub, string super)
    {
        return new OwlSubObjectPropertyOfAxiom(Property(sub), Property(super)) { Origin = Origin("subrole") };
    }

    /// <summary>An inverse sub-property inclusion <c>ObjectInverseOf(sub) ⊑ super</c> — that is, <c>sub⁻ ⊑ super</c>.</summary>
    /// <param name="sub">The inverted subproperty's local name.</param>
    /// <param name="super">The superproperty's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubObjectPropertyOfAxiom InverseSubProperty(string sub, string super)
    {
        return new OwlSubObjectPropertyOfAxiom(new OwlInverseObjectProperty(new NamedNode(Utf8Strings.From(Example + sub))), Property(super)) { Origin = Origin("inversesubrole") };
    }

    /// <summary>An equivalence of two object properties — bidirectional sub-role inclusion.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentObjectPropertiesAxiom EquivalentProperties(string first, string second)
    {
        return new OwlEquivalentObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("equivalentrole") };
    }

    /// <summary>An <c>InverseObjectProperties</c> axiom pairing two roles as each other's reverse.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlInverseObjectPropertiesAxiom Inverse(string first, string second)
    {
        return new OwlInverseObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("inverse") };
    }

    /// <summary>A property-chain sub-role inclusion — a single link is a plain sub-role, several compose.</summary>
    /// <param name="superProperty">The superproperty's local name.</param>
    /// <param name="links">The chain links' local names.</param>
    /// <returns>The axiom.</returns>
    private static OwlPropertyChainAxiom Chain(string superProperty, params string[] links)
    {
        OwlObjectPropertyExpression[] chain = new OwlObjectPropertyExpression[links.Length];
        for(int index = 0; index < links.Length; index++)
        {
            chain[index] = Property(links[index]);
        }

        return new OwlPropertyChainAxiom(chain, Property(superProperty)) { Origin = Origin("chain") };
    }

    /// <summary>A class assertion typing an individual.</summary>
    /// <param name="type">The asserted type.</param>
    /// <param name="individual">The individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlClassAssertionAxiom ClassAssertion(OwlClassExpression type, NamedNode individual)
    {
        return new OwlClassAssertionAxiom(type, individual) { Origin = Origin("assert") };
    }

    /// <summary>An asserted role edge between two individuals.</summary>
    /// <param name="from">The source individual's local name.</param>
    /// <param name="role">The role's local name.</param>
    /// <param name="to">The target individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom Edge(string from, string role, string to)
    {
        return new OwlObjectPropertyAssertionAxiom(Individual(from), Individual(role), Individual(to)) { Origin = Origin($"edge-{from}-{to}") };
    }

    /// <summary>A same-individual axiom.</summary>
    /// <param name="first">The first individual.</param>
    /// <param name="second">The second individual.</param>
    /// <returns>The axiom.</returns>
    private static OwlSameIndividualAxiom SameIndividual(NamedNode first, NamedNode second)
    {
        return new OwlSameIndividualAxiom(first, second) { Origin = Origin("same") };
    }

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }
}
