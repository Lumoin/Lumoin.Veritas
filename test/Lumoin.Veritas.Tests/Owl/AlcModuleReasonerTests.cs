using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// Tests for <see cref="AlcModuleReasoner"/>: satisfiable and clashing
/// modules across the rule set (conjunction, disjunction branching,
/// existential successors, universal propagation, role hierarchy), subset
/// blocking on a cyclic TBox, module-local subsumptions, and the
/// fragment-honesty contract for axioms beyond ALC(H). The in-scope
/// behavioural tests parametrize over both the snapshot engine and the
/// SAT-backed sibling <see cref="SatTableauModuleReasoner"/>.
/// </summary>
[TestClass]
internal sealed class AlcModuleReasonerTests
{
    /// <summary>The engines the parametrized tests decide through.</summary>
    internal enum ConsistencyEngine
    {
        /// <summary>The snapshot tableau's full entry, <see cref="AlcModuleReasoner.Decide"/>.</summary>
        Snapshot,

        /// <summary>The SAT-backed sibling's full entry, <see cref="SatTableauModuleReasoner.Decide"/>.</summary>
        SatBacked,
    }

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private const string Example = "http://example.org/";

    /// <summary>Disjoint classes sharing an instance clash; without the shared instance the module is consistent.</summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void DisjointnessClashesOnTheSharedInstance(ConsistencyEngine engine)
    {
        OwlDisjointClassesAxiom disjoint = new([Reference("A"), Reference("B")]) { Origin = Origin("disjoint") };
        OwlClassAssertionAxiom isA = new(Reference("A"), Named("x")) { Origin = Origin("isA") };
        OwlClassAssertionAxiom isB = new(Reference("B"), Named("x")) { Origin = Origin("isB") };

        Assert.IsFalse(Decide(engine, Module(disjoint, isA, isB)).IsConsistent);
        Assert.IsTrue(Decide(engine, Module(disjoint, isA)).IsConsistent);
    }

    /// <summary>An existential meets a universal of the complement filler and clashes in the successor.</summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void ExistentialMeetsUniversalComplement(ConsistencyEngine engine)
    {
        OwlClassAssertionAxiom some = new(
            new OwlObjectSomeValuesFrom(Property("r"), Reference("C")), Named("a"))
        {
            Origin = Origin("some"),
        };
        OwlClassAssertionAxiom all = new(
            new OwlObjectAllValuesFrom(Property("r"), new OwlObjectComplementOf(Reference("C"))), Named("a"))
        {
            Origin = Origin("all"),
        };

        Assert.IsFalse(Decide(engine, Module(some, all)).IsConsistent);
    }

    /// <summary>The universal propagates over an asserted edge through the told role hierarchy.</summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void UniversalPropagatesThroughTheRoleHierarchy(ConsistencyEngine engine)
    {
        //s ⊑ r; a ∀r.C; a s b; b ¬C — the universal reaches b through s.
        OwlSubObjectPropertyOfAxiom hierarchy = new(Property("s"), Property("r")) { Origin = Origin("hierarchy") };
        OwlClassAssertionAxiom all = new(
            new OwlObjectAllValuesFrom(Property("r"), Reference("C")), Named("a"))
        {
            Origin = Origin("all"),
        };
        OwlObjectPropertyAssertionAxiom edge = new(Named("a"), new NamedNode(Utf8String.FromString(Example + "s")), Named("b")) { Origin = Origin("edge") };
        OwlClassAssertionAxiom notC = new(new OwlObjectComplementOf(Reference("C")), Named("b")) { Origin = Origin("notC") };

        Assert.IsFalse(Decide(engine, Module(hierarchy, all, edge, notC)).IsConsistent);
        Assert.IsTrue(Decide(engine, Module(all, edge, notC)).IsConsistent, "Without the hierarchy the universal does not reach over s.");
    }

    /// <summary>A cyclic TBox terminates through subset blocking and stays consistent.</summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void CyclicTBoxTerminatesThroughBlocking(ConsistencyEngine engine)
    {
        //A ⊑ ∃r.A with an A instance: an unblocked tableau would unfold
        //forever; subset blocking folds the cycle.
        OwlSubClassOfAxiom cycle = new(
            Reference("A"),
            new OwlObjectSomeValuesFrom(Property("r"), Reference("A")))
        {
            Origin = Origin("cycle"),
        };
        OwlClassAssertionAxiom seed = new(Reference("A"), Named("a")) { Origin = Origin("seed") };

        Assert.IsTrue(Decide(engine, Module(cycle, seed)).IsConsistent);
    }

    /// <summary>Disjunction branching explores both disjuncts: a clash on the first branch does not condemn the module.</summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void DisjunctionBranchingRecovers(ConsistencyEngine engine)
    {
        //a : (A ⊔ B); a : ¬A — only the B branch survives.
        OwlClassAssertionAxiom union = new(
            new OwlObjectUnionOf([Reference("A"), Reference("B")]), Named("a"))
        {
            Origin = Origin("union"),
        };
        OwlClassAssertionAxiom notA = new(new OwlObjectComplementOf(Reference("A")), Named("a")) { Origin = Origin("notA") };

        Assert.IsTrue(Decide(engine, Module(union, notA)).IsConsistent);

        OwlClassAssertionAxiom notB = new(new OwlObjectComplementOf(Reference("B")), Named("a")) { Origin = Origin("notB") };

        Assert.IsFalse(Decide(engine, Module(union, notA, notB)).IsConsistent);
    }

    /// <summary>Module-local subsumptions surface: a subclass chain yields its transitive pair.</summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void SubsumptionsSurfaceTheChain(ConsistencyEngine engine)
    {
        OwlSubClassOfAxiom aUnderB = new(Reference("A"), Reference("B")) { Origin = Origin("ab") };
        OwlSubClassOfAxiom bUnderC = new(Reference("B"), Reference("C")) { Origin = Origin("bc") };

        ModuleVerdict verdict = Decide(engine, Module(aUnderB, bUnderC));

        Assert.IsTrue(verdict.IsConsistent);
        Assert.Contains(
            pair => Local(pair.SubClass) == "A" && Local(pair.SuperClass) == "C",
            verdict.Subsumptions,
            "The transitive subsumption A ⊑ C surfaces.");
    }

    /// <summary>An axiom beyond ALC(H) is named on the verdict, and the supported fragment still decides.</summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void BeyondFragmentAxiomsAreNamedNotDropped(ConsistencyEngine engine)
    {
        OwlSubClassOfAxiom supported = new(Reference("A"), Reference("B")) { Origin = Origin("ab") };
        OwlHasKeyAxiom beyond = new(Reference("A"), [Property("r")], []) { Origin = Origin("key") };

        ModuleVerdict verdict = Decide(engine, Module(supported, beyond));

        Assert.IsTrue(verdict.IsConsistent);
        Assert.Contains(nameof(OwlHasKeyAxiom), verdict.UnsupportedConstructs);
    }

    /// <summary>The survey names the beyond-fragment remainder without deciding, and an in-fragment module surveys empty.</summary>
    [TestMethod]
    public void SurveyNamesTheRemainderWithoutDeciding()
    {
        OwlSubClassOfAxiom supported = new(Reference("A"), Reference("B")) { Origin = Origin("ab") };
        OwlHasKeyAxiom beyond = new(Reference("A"), [Property("r")], []) { Origin = Origin("key") };

        Assert.IsEmpty(AlcModuleReasoner.Survey(Module(supported)));
        Assert.Contains(nameof(OwlHasKeyAxiom), AlcModuleReasoner.Survey(Module(supported, beyond)));
    }

    /// <summary>An assertion over a reserved built-in role is named beyond the fragment, not read as a plain edge.</summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void ReservedRolesAreNamedBeyondTheFragment(ConsistencyEngine engine)
    {
        OwlObjectPropertyAssertionAxiom viaBottom = new(
            Named("a"),
            new NamedNode(Utf8String.FromString("http://www.w3.org/2002/07/owl#bottomObjectProperty")),
            Named("b"))
        {
            Origin = Origin("edge"),
        };

        ModuleVerdict verdict = Decide(engine, Module(viaBottom));

        Assert.IsTrue(verdict.IsConsistent);
        Assert.Contains(nameof(OwlObjectPropertyAssertionAxiom), verdict.UnsupportedConstructs);
    }

    /// <summary>The consistency-only entry agrees with the full verdict on both outcomes and surfaces no subsumptions.</summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void ConsistencyOnlyEntryAgreesWithoutSubsumptions(ConsistencyEngine engine)
    {
        OwlSubClassOfAxiom aUnderB = new(Reference("A"), Reference("B")) { Origin = Origin("ab") };
        OwlSubClassOfAxiom bUnderC = new(Reference("B"), Reference("C")) { Origin = Origin("bc") };

        ModuleVerdict full = Decide(engine, Module(aUnderB, bUnderC));
        ModuleVerdict consistencyOnly = DecideConsistencyOnly(engine, Module(aUnderB, bUnderC));

        Assert.AreEqual(full.IsConsistent, consistencyOnly.IsConsistent);
        Assert.IsTrue(consistencyOnly.IsConsistent);
        Assert.IsEmpty(consistencyOnly.Subsumptions, "The consistency-only entry runs no subsumption sweep.");

        OwlDisjointClassesAxiom disjoint = new([Reference("A"), Reference("B")]) { Origin = Origin("disjoint") };
        OwlClassAssertionAxiom isA = new(Reference("A"), Named("x")) { Origin = Origin("isA") };
        OwlClassAssertionAxiom isB = new(Reference("B"), Named("x")) { Origin = Origin("isB") };

        Assert.IsFalse(DecideConsistencyOnly(engine, Module(disjoint, isA, isB)).IsConsistent);
    }

    /// <summary>SameIndividual merges its nodes: the merged individual carries both assertions into the clash.</summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void SameIndividualMergesNodes(ConsistencyEngine engine)
    {
        OwlDisjointClassesAxiom disjoint = new([Reference("A"), Reference("B")]) { Origin = Origin("disjoint") };
        OwlClassAssertionAxiom isA = new(Reference("A"), Named("x")) { Origin = Origin("isA") };
        OwlClassAssertionAxiom isB = new(Reference("B"), Named("y")) { Origin = Origin("isB") };

        Assert.IsTrue(Decide(engine, Module(disjoint, isA, isB)).IsConsistent, "Distinct individuals carry one class each.");

        OwlSameIndividualAxiom same = new(Named("x"), Named("y")) { Origin = Origin("same") };

        Assert.IsFalse(Decide(engine, Module(disjoint, isA, isB, same)).IsConsistent, "Merged, the individual is in both disjoint classes.");
    }

    /// <summary>
    /// A per-individual contradiction reachable only through the
    /// non-first individual's block condemns the module: an asserted role
    /// edge <c>r(a, b)</c> places <c>a</c> first and <c>b</c> second, the
    /// concepts <c>a</c> carries are satisfiable on their own, and <c>b</c>
    /// carries <c>C</c> while the TBox forces <c>C ⊑ D</c> and
    /// <c>C ⊑ ¬D</c> — a clash <c>a</c> never meets, since <c>a</c> is never
    /// <c>C</c>. The clash lands purely in <c>b</c>'s block; an engine that
    /// instantiated the TBox into the first block alone would see only
    /// <c>a</c>'s satisfiable block and wrongly report the module
    /// consistent.
    /// </summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void NonFirstBlockContradictionCondemnsTheModule(ConsistencyEngine engine)
    {
        OwlSubClassOfAxiom cImpliesD = new(Reference("C"), Reference("D")) { Origin = Origin("cImpliesD") };
        OwlSubClassOfAxiom cImpliesNotD = new(Reference("C"), new OwlObjectComplementOf(Reference("D"))) { Origin = Origin("cImpliesNotD") };
        OwlClassAssertionAxiom satisfiableA = new(Reference("A"), Named("a")) { Origin = Origin("satisfiableA") };

        //The discriminating power depends on b landing in a non-first block: the joint instance
        //allocates blocks by first encounter, and the edge presents its source a before its target b,
        //so a takes block 0 and b block 1. The contradiction is asserted on b alone, so a block-0-only
        //instantiation would miss it. Keep the edge oriented a -> b for this reason.
        OwlObjectPropertyAssertionAxiom edge = new(Named("a"), new NamedNode(Utf8String.FromString(Example + "r")), Named("b")) { Origin = Origin("edge") };
        OwlClassAssertionAxiom isC = new(Reference("C"), Named("b")) { Origin = Origin("isC") };

        Assert.IsTrue(
            Decide(engine, Module(cImpliesD, cImpliesNotD, satisfiableA, edge)).IsConsistent,
            "Without b's C assertion the non-first block carries no clash.");
        Assert.IsFalse(
            Decide(engine, Module(cImpliesD, cImpliesNotD, satisfiableA, edge, isC)).IsConsistent,
            "C ⊑ D and C ⊑ ¬D clash in b's block alone, condemning the module.");
    }

    /// <summary>Decides the module through the selected engine's full entry — consistency plus the module-local subsumption sweep.</summary>
    /// <param name="engine">The engine to decide through.</param>
    /// <param name="module">The module.</param>
    /// <returns>The verdict.</returns>
    private ModuleVerdict Decide(ConsistencyEngine engine, ReasoningModule module)
    {
        return engine switch
        {
            ConsistencyEngine.SatBacked => SatTableauModuleReasoner.Decide(module, cancellationToken: TestContext.CancellationToken),
            _ => AlcModuleReasoner.Decide(module, TestContext.CancellationToken),
        };
    }

    /// <summary>Decides the module through the selected engine's consistency-only entry.</summary>
    /// <param name="engine">The engine to decide through.</param>
    /// <param name="module">The module.</param>
    /// <returns>The verdict, with an empty subsumption list.</returns>
    private ModuleVerdict DecideConsistencyOnly(ConsistencyEngine engine, ReasoningModule module)
    {
        return engine switch
        {
            ConsistencyEngine.SatBacked => SatTableauModuleReasoner.DecideConsistency(module, cancellationToken: TestContext.CancellationToken),
            _ => AlcModuleReasoner.DecideConsistency(module, TestContext.CancellationToken),
        };
    }

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>A named-class reference in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The reference.</returns>
    private static OwlClassReference Reference(string local)
    {
        return new OwlClassReference(new NamedNode(Utf8String.FromString(Example + local)));
    }

    /// <summary>A named object property expression in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The property expression.</returns>
    private static OwlObjectPropertyReference Property(string local)
    {
        return new OwlObjectPropertyReference(new NamedNode(Utf8String.FromString(Example + local)));
    }

    /// <summary>A named individual in the example namespace.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The node.</returns>
    private static NamedNode Named(string local)
    {
        return new NamedNode(Utf8String.FromString(Example + local));
    }

    /// <summary>A distinct origin quad for the marker name.</summary>
    /// <param name="marker">The distinguishing marker.</param>
    /// <returns>The origin quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(Named(marker), Named("p"), Named("o"), Graph: null);
    }

    /// <summary>The local name of a node in the example namespace.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The local name.</returns>
    private static string Local(NamedNode node)
    {
        return node.Iri.ToString()[Example.Length..];
    }
}
