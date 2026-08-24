using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// SROIQ fallback isolation pins, driven
/// DIRECTLY on both tableau arms — the snapshot <see cref="AlcModuleReasoner"/>
/// and the SAT-backed <see cref="SatTableauModuleReasoner"/>, which share the
/// one <see cref="AlcModuleReasoner"/> translation. The translation's own
/// <c>SameIndividual</c> union-find can merge an asserted-distinct pair, so
/// <c>DifferentIndividuals</c> is no vacuous no-op: the ALC bottom concept is
/// seeded on the collided representative, and a
/// <c>{SameIndividual(a,b), DifferentIndividuals(a,b)}</c> collision condemns
/// the module on both arms. GF1/GF2 pin the inconsistency as DECISIVE (the
/// whole-module <see cref="ReasoningDecisionOutcome.Decided"/>, an
/// inconsistency in the supported fragment overriding the inert inverse
/// remainder); GF3 pins that a non-colliding pair stays consistent. Axioms
/// transcribe the certified battery rows verbatim.
/// </summary>
[TestClass]
internal sealed class FallbackDistinctnessIsolationTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The example namespace the roles and individuals are drawn from.</summary>
    private const string Example = "http://example.org/sroiq5#";

    /// <summary>The tableau arms the parametrized rows decide through.</summary>
    internal enum Arm
    {
        /// <summary>The snapshot tableau, <see cref="AlcModuleReasoner"/>.</summary>
        Snapshot,

        /// <summary>The SAT-backed sibling, <see cref="SatTableauModuleReasoner"/>.</summary>
        SatBacked,
    }

    /// <summary>GF1: a <c>SameIndividual</c>/<c>DifferentIndividuals</c> collision over one pair, with an inert inverse axiom, is decided INCONSISTENT and DECISIVE on both arms.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void GF1SameDifferentCollisionIsDecisivelyInconsistent(Arm arm)
    {
        ModuleDecision decision = DecideModule(arm, Module(
            SameIndividual("gf1a", "gf1b"),
            Different("gf1a", "gf1b"),
            Inverse("gf1r", "gf1s")));

        AssertDecisivelyInconsistent(decision, "Same(gf1a,gf1b) and Different(gf1a,gf1b) collide on one representative, so the module is inconsistent regardless of the inert inverse axiom.");
    }

    /// <summary>GF2: the collision reached through a merge chain (a=b, b=c, a≠c) is decided INCONSISTENT and DECISIVE on both arms.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void GF2MergeChainCollisionIsDecisivelyInconsistent(Arm arm)
    {
        ModuleDecision decision = DecideModule(arm, Module(
            SameIndividual("gf2a", "gf2b"),
            SameIndividual("gf2b", "gf2c"),
            Different("gf2a", "gf2c"),
            Inverse("gf2r", "gf2s")));

        AssertDecisivelyInconsistent(decision, "The two Same axioms chain gf2a=gf2b=gf2c onto one representative, which Different(gf2a,gf2c) forbids.");
    }

    /// <summary>GF3: a non-colliding pair (Same on one pair, Different on a disjoint pair) stays CONSISTENT on both arms.</summary>
    /// <param name="arm">The tableau arm.</param>
    [TestMethod]
    [DataRow(Arm.Snapshot)]
    [DataRow(Arm.SatBacked)]
    public void GF3DistinctPairsStayConsistent(Arm arm)
    {
        ModuleVerdict verdict = Decide(arm, Module(
            SameIndividual("gf3a", "gf3b"),
            Different("gf3a", "gf3c"),
            Inverse("gf3r", "gf3s")));

        Assert.IsTrue(verdict.IsConsistent, "Same(gf3a,gf3b) and Different(gf3a,gf3c) touch disjoint representatives, so no collision arises.");
    }

    /// <summary>Asserts the decision condemns the whole module: inconsistent and decisive, not scoped to the supported fragment.</summary>
    /// <param name="decision">The module decision.</param>
    /// <param name="because">The failure message.</param>
    private static void AssertDecisivelyInconsistent(ModuleDecision decision, string because)
    {
        Assert.IsNotNull(decision.Verdict);
        Assert.IsFalse(decision.Verdict.IsConsistent, because);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome, "An inconsistency in the supported fragment condemns the whole module, so the outcome is decisive, not fragment-relative.");
    }

    /// <summary>Decides the module through the arm's consistency-plus-sweep entry.</summary>
    /// <param name="arm">The tableau arm.</param>
    /// <param name="module">The module.</param>
    /// <returns>The verdict.</returns>
    private ModuleVerdict Decide(Arm arm, ReasoningModule module)
    {
        return arm switch
        {
            Arm.SatBacked => SatTableauModuleReasoner.Decide(module, cancellationToken: TestContext.CancellationToken),
            _ => AlcModuleReasoner.Decide(module, TestContext.CancellationToken),
        };
    }

    /// <summary>Decides the module through the arm as a full decision, so its outcome is observable.</summary>
    /// <param name="arm">The tableau arm.</param>
    /// <param name="module">The module.</param>
    /// <returns>The decision.</returns>
    private ModuleDecision DecideModule(Arm arm, ReasoningModule module)
    {
        return arm switch
        {
            Arm.SatBacked => SatTableauModuleReasoner.DecideModule(module, ReasoningBudget.Unbounded, cancellationToken: TestContext.CancellationToken),
            _ => AlcModuleReasoner.DecideModule(module, TestContext.CancellationToken),
        };
    }

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>A named individual in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The node.</returns>
    private static NamedNode Individual(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
    }

    /// <summary>A named object property expression in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property expression.</returns>
    private static OwlObjectPropertyReference Property(string local)
    {
        return new OwlObjectPropertyReference(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>A distinct origin quad for the marker name.</summary>
    /// <param name="marker">The distinguishing marker.</param>
    /// <returns>The origin quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(new NamedNode(Utf8Strings.From(Example + marker)), new NamedNode(Utf8Strings.From(Example + "p")), new NamedNode(Utf8Strings.From(Example + "o")), Graph: null);
    }

    /// <summary>A <c>SameIndividual</c> axiom over a pair.</summary>
    /// <param name="first">The first individual's local name.</param>
    /// <param name="second">The second individual's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlSameIndividualAxiom SameIndividual(string first, string second)
    {
        return new OwlSameIndividualAxiom(Individual(first), Individual(second)) { Origin = Origin("same") };
    }

    /// <summary>A <c>DifferentIndividuals</c> axiom over the named individuals.</summary>
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

    /// <summary>An <c>InverseObjectProperties</c> axiom over a role pair.</summary>
    /// <param name="first">The first role's local name.</param>
    /// <param name="second">The second role's local name.</param>
    /// <returns>The axiom.</returns>
    private static OwlInverseObjectPropertiesAxiom Inverse(string first, string second)
    {
        return new OwlInverseObjectPropertiesAxiom(Property(first), Property(second)) { Origin = Origin("inverse") };
    }
}
