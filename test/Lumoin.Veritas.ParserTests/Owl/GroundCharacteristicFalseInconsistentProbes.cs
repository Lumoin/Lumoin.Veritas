using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Adversarial FALSE-INCONSISTENT probes for the asymmetric/irreflexive ground-graph characteristic
/// tier. Every module here HAS a model, spelled out in a comment as an explicit hand-built
/// interpretation, independent of the characteristic-blind tableau. A tier that condemns any of them
/// (final verdict inconsistent) is over-condemning — a soundness BLOCKER. Several probes are tuned to
/// fire only if a closure direction was inverted (super-role edges bleeding onto a sub-role constraint,
/// told reflexivity descending to sub-roles, an unordered pair set treating a duplicate as a reverse).
/// </summary>
[TestClass]
internal sealed class GroundCharacteristicFalseInconsistentProbes
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Every consistent module must come back consistent — decided-consistent or delegated-consistent, never condemned. The report names any module the tier falsely condemned.</summary>
    [TestMethod]
    public void ConsistentGroundCharacteristicModulesAreNeverCondemned()
    {
        (string Name, ReasoningModule Module)[] cases =
        [
            //P1 — model D = {x}, q = r = {}, s = {}: Reflexive(r) forces r(x, x) only; s two levels below r
            //(s ⊑ q ⊑ r) gains nothing, so the asymmetric s is vacuously satisfied. Reflexivity does not
            //descend two hierarchy levels. Consistent.
            ("P1_ReflexiveSuperTwoLevelsAsymmetricBottom", Module(
                Reflexive("r"),
                SubProperty("q", "r"),
                SubProperty("s", "q"),
                Asymmetric("s"))),

            //P2 — model D = {a, b, c}, u = {(a, a)}, r = {(a, b), (b, c)}: Reflexive(u) sits on an unrelated
            //role, and the asymmetric r's one-directional chain has no reverse and no self-edge. Consistent.
            ("P2_ReflexiveUnrelatedPlusLegalAsymmetricChain", Module(
                Reflexive("u"),
                Asymmetric("r"),
                Edge("a", "r", "b"),
                Edge("b", "r", "c"))),

            //P3 — model after the SameIndividual(a, b) merge: D = {ab, c, d}, r = {(ab, c), (ab, d)}. The
            //merge fuses the two SOURCES; the targets c, d stay distinct, so no reverse pair. Consistent.
            ("P3_SameIndividualMergesSourcesOnly", Module(
                SameIndividual(Individual("a"), Individual("b")),
                Asymmetric("r"),
                Edge("a", "r", "c"),
                Edge("b", "r", "d"))),

            //P4 — model after the functional collapse f(x, a), f(x, b) => a = b: D = {x, ab, c},
            //r = {(ab, c)}. Both r(a, c) and r(b, c) become the SAME ordered edge (ab, c) — a post-merge
            //duplicate, not a reverse. Consistent.
            ("P4_FunctionalCollapseMergesTargetsToDuplicate", Module(
                Functional("f"),
                Edge("x", "f", "a"),
                Edge("x", "f", "b"),
                Asymmetric("r"),
                Edge("a", "r", "c"),
                Edge("b", "r", "c"))),

            //P5 — model D = {a, b}, s1 = {(a, b)}, s2 = {(a, b)}, r = {(a, b)}: s1 ⊑ r and s2 ⊑ r both send
            //the SAME direction (a, b), so the asymmetric r bears one ordered edge (a, b) and its duplicate,
            //never a reverse. An unordered pair set would mis-fire here. Consistent.
            ("P5_SameDirectionViaTwoSubRolesIsDuplicate", Module(
                SubProperty("s1", "r"),
                SubProperty("s2", "r"),
                Asymmetric("r"),
                Edge("a", "s1", "b"),
                Edge("a", "s2", "b"))),

            //P6 — model D = {a}, r = {(a, a)}, s = {}: the self-edge sits on the SUPER-role r; the irreflexive
            //constraint is on the SUB-role s, which bears no edge. A super-role self-edge is not a sub-role
            //self-edge, so s is vacuously irreflexive. Fires only if the scan closes UPWARD not downward.
            //Consistent.
            ("P6_SuperRoleSelfEdgeIrreflexiveSubRole", Module(
                Irreflexive("s"),
                SubProperty("s", "r"),
                Edge("a", "r", "a"))),

            //P7 — model D = {a, b, c}, r = {(a, b), (b, c)}, a, b, c pairwise distinct: DifferentIndividuals
            //asserts the distinctness the model already has; the asymmetric r's chain has no reverse.
            //Consistent.
            ("P7_DifferentIndividualsPlusLegalAsymmetricEdges", Module(
                Different("a", "b", "c"),
                Asymmetric("r"),
                Edge("a", "r", "b"),
                Edge("b", "r", "c"))),

            //P8 — model D = {x}, r = {(x, x)}, s = {}: Reflexive(r) forces the self-edge on r ONLY; it does
            //not descend to the sub-role s, so the irreflexive s is vacuously satisfied. Fires only if the
            //told check closes DOWNWARD from the reflexive role instead of upward. Consistent.
            ("P8_ReflexiveSuperOfIrreflexiveSub", Module(
                Reflexive("r"),
                SubProperty("s", "r"),
                Irreflexive("s"))),

            //P9 — model D = {a, b}, r = {(a, b)}, q = {(a, b)}: q ≡ r makes q(a, b) the same r-edge (a, b) as
            //the asserted r(a, b) — a duplicate over the equivalent role, not a reverse. Consistent.
            ("P9_EquivalentPropertySameDirectionDuplicate", Module(
                EquivalentProperties("q", "r"),
                Asymmetric("r"),
                Edge("a", "r", "b"),
                Edge("a", "q", "b"))),

            //P10 — model after the bare-nominal fold x : {a} => x = a: D = {a, b}, r = {(a, b)}. The asserted
            //r(x, b) and r(a, b) both become the ordered edge (a, b) — a duplicate, not a self-edge and not a
            //reverse. Consistent.
            ("P10_BareNominalFoldDuplicateNotSelfEdge", Module(
                ClassAssertion(OneOf("a"), Individual("x")),
                Asymmetric("r"),
                Edge("x", "r", "b"),
                Edge("a", "r", "b"))),

            //P11 — model D = {x}, r = {(x, x)}, s = {}, t = {}: Reflexive(r) forces r(x, x); the asymmetric
            //siblings s and t both sit BELOW r (s ⊑ r, t ⊑ r) and bear no edge. Reflexivity forces nothing on
            //sub-roles. Consistent.
            ("P11_ReflexiveSuperTwoAsymmetricSubSiblings", Module(
                Reflexive("r"),
                SubProperty("s", "r"),
                SubProperty("t", "r"),
                Asymmetric("s"),
                Asymmetric("t"))),

            //P12 — model D = {a, b}, r = {(a, b)}: the reverse edge lies on the unconstrained q, and the
            //asymmetric r keeps its single forward edge; the irreflexive constraint on the unrelated role u
            //(no edges) is vacuous. Consistent.
            ("P12_MixedConstraintsReverseOnUnrelated", Module(
                Asymmetric("r"),
                Irreflexive("u"),
                Edge("a", "r", "b"),
                Edge("b", "q", "a"))),

            //P13 — model D = {a, b, c}, r = {(a, b), (b, c), (a, c)}: a transitively-shaped fan with no
            //reverse and no self-edge over the asymmetric r (the edges are asserted, not composed).
            //Consistent.
            ("P13_AsymmetricFanNoReverse", Module(
                Asymmetric("r"),
                Edge("a", "r", "b"),
                Edge("b", "r", "c"),
                Edge("a", "r", "c"))),

            //P14 — model D = {a, b}, r = {(a, b)}, s = {(a, b)}: s ⊑ r, the asymmetric constraint is on the
            //SUB-role s, and the asserted s(a, b) plus r(a, b) are one forward direction. No reverse on s.
            //Consistent (super-role duplicate must not read as a sub-role reverse).
            ("P14_SubRoleAsymmetricForwardPlusSuperEdge", Module(
                SubProperty("s", "r"),
                Asymmetric("s"),
                Edge("a", "s", "b"),
                Edge("a", "r", "b"))),
        ];

        System.Text.StringBuilder report = new();
        report.AppendLine("\ncase | trueConsistent | finalConsistent | path | tableau | verdict");
        List<string> condemned = [];
        foreach((string name, ReasoningModule module) in cases)
        {
            ModuleDecision decision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);
            string path = decision.Statistics.ElTotals.ElDecided ? "Decided" : "Delegated";
            bool finalConsistent = decision.Verdict!.IsConsistent;
            bool tableauConsistent = AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent;
            report.AppendLine(name + " | True | " + finalConsistent + " | " + path + " | " + tableauConsistent + " | " + (finalConsistent ? "OK" : "FALSE-INCONSISTENT"));
            if(!finalConsistent)
            {
                condemned.Add(name + " was condemned (final inconsistent) but has a model; path=" + path);
            }
        }

        Assert.IsEmpty(condemned, report.ToString());
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

    /// <summary>An individual-value restriction <c>ObjectHasValue(r, a)</c>.</summary>
    /// <param name="property">The role's local name.</param>
    /// <param name="individual">The required value individual's local name.</param>
    /// <returns>The restriction.</returns>
    private static OwlObjectHasValue HasValue(string property, string individual)
    {
        return new OwlObjectHasValue(Property(property), Individual(individual));
    }

    /// <summary>An enumeration of individuals (<c>ObjectOneOf</c>); a single individual is the nominal <c>{a}</c>.</summary>
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

    /// <summary>A subclass inclusion.</summary>
    /// <param name="sub">The subclass expression.</param>
    /// <param name="super">The superclass expression.</param>
    /// <returns>The axiom.</returns>
    private static OwlSubClassOfAxiom SubClassOf(OwlClassExpression sub, OwlClassExpression super)
    {
        return new OwlSubClassOfAxiom(sub, super) { Origin = Origin("sub") };
    }

    /// <summary>A reflexive-role characteristic axiom.</summary>
    /// <param name="property">The role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlObjectPropertyCharacteristicAxiom Reflexive(string property)
    {
        return new OwlObjectPropertyCharacteristicAxiom(OwlPropertyCharacteristic.Reflexive, Property(property)) { Origin = Origin("reflexive") };
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

    /// <summary>An equivalence of two object properties — bidirectional sub-role inclusion.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlEquivalentObjectPropertiesAxiom EquivalentProperties(string first, string second)
    {
        return new OwlEquivalentObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("equivalentrole") };
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

    /// <summary>A different-individuals axiom over the named individuals.</summary>
    /// <param name="individuals">The mutually distinct individuals' local names.</param>
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

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }
}
