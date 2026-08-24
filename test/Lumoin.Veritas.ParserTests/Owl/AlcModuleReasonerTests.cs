using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

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
        OwlObjectPropertyAssertionAxiom edge = new(Named("a"), new NamedNode(Utf8Strings.From(Example + "s")), Named("b")) { Origin = Origin("edge") };
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

    /// <summary>
    /// The snapshot engine folds a cycle with dynamic equality double
    /// (pairwise) blocking, not subset blocking: on <c>A ⊑ ∃r.A</c> with an
    /// <c>A</c> instance the forest grows one pairwise-repeat deeper than a
    /// subset-blocked search would — the seed, a first successor, and a second
    /// successor blocked by the first — so the largest forest reaches three
    /// nodes. Subset blocking would have folded at the first successor (two
    /// nodes), so the node count pins that the inverse-safe blocking device is
    /// the one in force, and the verdict stays consistent.
    /// </summary>
    [TestMethod]
    public void DoubleBlockingFoldsACycleAtThePairwiseRepeat()
    {
        OwlSubClassOfAxiom cycle = new(
            Reference("A"),
            new OwlObjectSomeValuesFrom(Property("r"), Reference("A")))
        {
            Origin = Origin("cycle"),
        };
        OwlClassAssertionAxiom seed = new(Reference("A"), Named("a")) { Origin = Origin("seed") };

        ModuleDecision decision = AlcModuleReasoner.DecideModule(Module(cycle, seed), TestContext.CancellationToken);

        Assert.IsTrue(decision.Verdict!.IsConsistent);

        //One consistency tableau (the single signature class A yields no
        //subsumption pair), folding the cycle at the pairwise repeat: seed,
        //first successor, second successor blocked by the first.
        Assert.AreEqual(1, decision.Statistics.TableauTotals.TableauRuns);
        Assert.AreEqual(3, decision.Statistics.TableauTotals.MaxNodes);
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

    /// <summary>
    /// The snapshot engine's decision reports its tableau work: it runs no
    /// solver, so the solve count is zero, but the tableau totals carry the
    /// runs and rule applications the consistency check and subsumption sweep
    /// spent.
    /// </summary>
    [TestMethod]
    public void DecideModuleReportsTableauStatistics()
    {
        OwlSubClassOfAxiom aUnderB = new(Reference("A"), Reference("B")) { Origin = Origin("ab") };
        OwlSubClassOfAxiom bUnderC = new(Reference("B"), Reference("C")) { Origin = Origin("bc") };
        ReasoningModule module = Module(aUnderB, bUnderC);

        ModuleDecision decision = AlcModuleReasoner.DecideModule(module, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome);
        Assert.IsNotNull(decision.Verdict);
        Assert.IsTrue(decision.Verdict.IsConsistent);

        //The snapshot engine runs no solver, but does run tableaux.
        Assert.AreEqual(0, decision.Statistics.SolveCount);
        Assert.AreEqual(module.Axioms.Count, decision.Statistics.ModuleAxiomCount);
        Assert.IsGreaterThan(0, decision.Statistics.TableauTotals.TableauRuns, "At least the consistency tableau ran.");
        Assert.IsGreaterThan(0, decision.Statistics.TableauTotals.RuleApplications, "The internalized TBox drove rule applications.");
    }

    /// <summary>The snapshot engine's tableau counters for a known decision are pinned, so a regression that mis-counts the tableau work is caught — the telemetry oracle, not a mere non-zero check.</summary>
    [TestMethod]
    public void DecideModulePinsTheTableauCountersForAKnownDecision()
    {
        OwlSubClassOfAxiom aUnderB = new(Reference("A"), Reference("B")) { Origin = Origin("ab") };
        OwlSubClassOfAxiom bUnderC = new(Reference("B"), Reference("C")) { Origin = Origin("bc") };

        ModuleDecision decision = AlcModuleReasoner.DecideModule(Module(aUnderB, bUnderC), TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome);
        Assert.IsTrue(decision.Verdict!.IsConsistent);
        Assert.AreEqual(0, decision.Statistics.SolveCount);

        //One consistency tableau plus one per ordered subsumption pair over the
        //three signature classes A, B, C: 1 + 3*2 = 7. The rest are pinned, so a
        //regression that mis-counts the tableau work is caught.
        AlcTableauStatistics totals = decision.Statistics.TableauTotals;
        Assert.AreEqual(7, totals.TableauRuns);
        Assert.AreEqual(8L, totals.RuleApplications);
        Assert.AreEqual(8, totals.Branches);
        Assert.AreEqual(8, totals.Clashes);
        Assert.AreEqual(1, totals.MaxNodes);
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
            new NamedNode(Utf8Strings.From("http://www.w3.org/2002/07/owl#bottomObjectProperty")),
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
        OwlObjectPropertyAssertionAxiom edge = new(Named("a"), new NamedNode(Utf8Strings.From(Example + "r")), Named("b")) { Origin = Origin("edge") };
        OwlClassAssertionAxiom isC = new(Reference("C"), Named("b")) { Origin = Origin("isC") };

        Assert.IsTrue(
            Decide(engine, Module(cImpliesD, cImpliesNotD, satisfiableA, edge)).IsConsistent,
            "Without b's C assertion the non-first block carries no clash.");
        Assert.IsFalse(
            Decide(engine, Module(cImpliesD, cImpliesNotD, satisfiableA, edge, isC)).IsConsistent,
            "C ⊑ D and C ⊑ ¬D clash in b's block alone, condemning the module.");
    }

    /// <summary>
    /// A disjoint union <c>Child ≡ Boy ⊔ Girl</c> partitions: a child that is
    /// not a girl is a boy, a child that is neither contradicts the union, and
    /// an instance in both members clashes on the members' disjointness. This is
    /// the New-Feature-DisjointUnion-001 shape, decided directly.
    /// </summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void DisjointUnionPartitionsAndIsExclusive(ConsistencyEngine engine)
    {
        OwlDisjointUnionAxiom partition = new(Named("Child"), [Reference("Boy"), Reference("Girl")]) { Origin = Origin("partition") };
        OwlClassAssertionAxiom isChild = new(Reference("Child"), Named("x")) { Origin = Origin("isChild") };
        OwlClassAssertionAxiom notGirl = new(new OwlObjectComplementOf(Reference("Girl")), Named("x")) { Origin = Origin("notGirl") };
        OwlClassAssertionAxiom notBoy = new(new OwlObjectComplementOf(Reference("Boy")), Named("x")) { Origin = Origin("notBoy") };

        Assert.IsTrue(Decide(engine, Module(partition, isChild, notGirl)).IsConsistent, "A child that is not a girl is a boy.");
        Assert.IsFalse(Decide(engine, Module(partition, isChild, notGirl, notBoy)).IsConsistent, "A child that is neither boy nor girl contradicts the union.");

        OwlClassAssertionAxiom isBoy = new(Reference("Boy"), Named("y")) { Origin = Origin("isBoy") };
        OwlClassAssertionAxiom isGirl = new(Reference("Girl"), Named("y")) { Origin = Origin("isGirl") };

        Assert.IsFalse(Decide(engine, Module(partition, isBoy, isGirl)).IsConsistent, "The union's members are pairwise disjoint.");
    }

    /// <summary>Each member of a disjoint union subsumes into the defined class, since a member is one of the disjuncts the class is equivalent to.</summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void DisjointUnionMembersSubsumeIntoTheClass(ConsistencyEngine engine)
    {
        OwlDisjointUnionAxiom partition = new(Named("Child"), [Reference("Boy"), Reference("Girl")]) { Origin = Origin("partition") };

        ModuleVerdict verdict = Decide(engine, Module(partition));

        Assert.IsTrue(verdict.IsConsistent);
        Assert.Contains(
            pair => Local(pair.SubClass) == "Boy" && Local(pair.SuperClass) == "Child",
            verdict.Subsumptions,
            "A union member subsumes into the class it helps define.");
    }

    /// <summary>A disjoint union with a member outside ALC(H) is named on the remainder whole, not half-internalized, and the supported fragment still decides.</summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void DisjointUnionWithABeyondFragmentMemberIsNamedNotDropped(ConsistencyEngine engine)
    {
        //A nominal member leaves ALC(H), so the whole union axiom is named
        //beyond the fragment rather than partly internalized.
        OwlObjectOneOf nominal = new([Named("only")]);
        OwlDisjointUnionAxiom partition = new(Named("Child"), [Reference("Boy"), nominal]) { Origin = Origin("partition") };
        OwlSubClassOfAxiom supported = new(Reference("A"), Reference("B")) { Origin = Origin("ab") };

        ModuleVerdict verdict = Decide(engine, Module(partition, supported));

        Assert.IsTrue(verdict.IsConsistent);
        Assert.Contains(nameof(OwlDisjointUnionAxiom), verdict.UnsupportedConstructs);
    }

    /// <summary>
    /// Equivalent object properties reach across either role: a universal on
    /// one role propagates over an asserted edge of the other, because each
    /// includes the other through the role hierarchy. The clash needs the
    /// reverse direction of the equivalence, so it pins both told inclusions.
    /// </summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void EquivalentObjectPropertiesReachAcrossEitherRole(ConsistencyEngine engine)
    {
        //p ≡ q; a ∀p.C; a q b; b ¬C — the universal on p reaches b over the q
        //edge because q ⊑ p, the reverse direction of the equivalence.
        OwlEquivalentObjectPropertiesAxiom equivalence = new(Property("p"), Property("q")) { Origin = Origin("equivalence") };
        OwlClassAssertionAxiom all = new(new OwlObjectAllValuesFrom(Property("p"), Reference("C")), Named("a")) { Origin = Origin("all") };
        OwlObjectPropertyAssertionAxiom edge = new(Named("a"), new NamedNode(Utf8Strings.From(Example + "q")), Named("b")) { Origin = Origin("edge") };
        OwlClassAssertionAxiom notC = new(new OwlObjectComplementOf(Reference("C")), Named("b")) { Origin = Origin("notC") };

        Assert.IsFalse(Decide(engine, Module(equivalence, all, edge, notC)).IsConsistent, "The universal on p reaches the q-successor because q ⊑ p.");
        Assert.IsTrue(Decide(engine, Module(all, edge, notC)).IsConsistent, "Without the equivalence the universal does not reach over q.");

        //The mirror pins the other told inclusion: ∀q.C must reach a p-successor
        //because p ⊑ q. A reverse-only translation would pass the case above but
        //fail this one.
        OwlClassAssertionAxiom allOverQ = new(new OwlObjectAllValuesFrom(Property("q"), Reference("C")), Named("a")) { Origin = Origin("allOverQ") };
        OwlObjectPropertyAssertionAxiom edgeOverP = new(Named("a"), new NamedNode(Utf8Strings.From(Example + "p")), Named("b")) { Origin = Origin("edgeOverP") };

        Assert.IsFalse(Decide(engine, Module(equivalence, allOverQ, edgeOverP, notC)).IsConsistent, "The universal on q reaches the p-successor because p ⊑ q.");
    }

    /// <summary>A transitive-role declaration is inside the ALC(H)+S fragment: the survey is empty, so the module decides whole rather than fragment-relative.</summary>
    [TestMethod]
    public void TransitiveRoleIsInsideTheFragment()
    {
        OwlObjectPropertyCharacteristicAxiom transitive = new(OwlPropertyCharacteristic.Transitive, Property("r")) { Origin = Origin("transitive") };

        Assert.IsEmpty(AlcModuleReasoner.Survey(Module(transitive)));
    }

    /// <summary>
    /// A transitive role carries a universal across an asserted chain: with
    /// <c>r</c> transitive, <c>a : ∀r.C</c> reaches not only <c>a</c>'s direct
    /// <c>r</c>-successor but the node two <c>r</c>-steps away, so a far node
    /// asserted <c>¬C</c> clashes. Without the transitivity the universal
    /// reaches only the direct successor and the module is consistent.
    /// </summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void TransitiveRoleCarriesUniversalAcrossAnAssertedChain(ConsistencyEngine engine)
    {
        OwlObjectPropertyCharacteristicAxiom transitive = new(OwlPropertyCharacteristic.Transitive, Property("r")) { Origin = Origin("transitive") };
        OwlClassAssertionAxiom all = new(new OwlObjectAllValuesFrom(Property("r"), Reference("C")), Named("a")) { Origin = Origin("all") };
        OwlObjectPropertyAssertionAxiom ab = new(Named("a"), new NamedNode(Utf8Strings.From(Example + "r")), Named("b")) { Origin = Origin("ab") };
        OwlObjectPropertyAssertionAxiom bc = new(Named("b"), new NamedNode(Utf8Strings.From(Example + "r")), Named("c")) { Origin = Origin("bc") };
        OwlClassAssertionAxiom notC = new(new OwlObjectComplementOf(Reference("C")), Named("c")) { Origin = Origin("notC") };

        Assert.IsFalse(Decide(engine, Module(transitive, all, ab, bc, notC)).IsConsistent, "∀r.C reaches c two r-steps away because r is transitive.");
        Assert.IsTrue(Decide(engine, Module(all, ab, bc, notC)).IsConsistent, "Without transitivity ∀r.C reaches only the direct successor b.");
    }

    /// <summary>
    /// A transitive role carries a universal across tableau-generated
    /// successors, not just asserted edges: <c>a : ∃r.∃r.D</c> builds an
    /// <c>r</c>-chain <c>a → x → y</c>, and with <c>r</c> transitive
    /// <c>a : ∀r.¬D</c> reaches <c>y</c>, clashing with <c>y : D</c>. Without
    /// transitivity the universal stops at the direct successor.
    /// </summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void TransitiveRoleCarriesUniversalAcrossGeneratedSuccessors(ConsistencyEngine engine)
    {
        OwlObjectPropertyCharacteristicAxiom transitive = new(OwlPropertyCharacteristic.Transitive, Property("r")) { Origin = Origin("transitive") };
        OwlClassAssertionAxiom chain = new(
            new OwlObjectSomeValuesFrom(Property("r"), new OwlObjectSomeValuesFrom(Property("r"), Reference("D"))), Named("a"))
        {
            Origin = Origin("chain"),
        };
        OwlClassAssertionAxiom none = new(new OwlObjectAllValuesFrom(Property("r"), new OwlObjectComplementOf(Reference("D"))), Named("a")) { Origin = Origin("none") };

        Assert.IsFalse(Decide(engine, Module(transitive, chain, none)).IsConsistent, "∀r.¬D reaches the two-step successor because r is transitive.");
        Assert.IsTrue(Decide(engine, Module(chain, none)).IsConsistent, "Without transitivity ∀r.¬D reaches only the direct successor.");
    }

    /// <summary>
    /// The ∀⁺-rule fires through the role hierarchy: a universal on a
    /// super-role propagates along a transitive sub-role. With <c>s</c>
    /// transitive and <c>s ⊑ r</c>, <c>a : ∀r.C</c> carries <c>∀s.C</c> down
    /// the <c>s</c>-chain, so a node two <c>s</c>-steps away asserted <c>¬C</c>
    /// clashes — the universal restriction itself, not just its filler, must
    /// re-propagate for this to hold.
    /// </summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void TransitiveSubRoleCarriesASuperRoleUniversal(ConsistencyEngine engine)
    {
        OwlSubObjectPropertyOfAxiom hierarchy = new(Property("s"), Property("r")) { Origin = Origin("hierarchy") };
        OwlObjectPropertyCharacteristicAxiom transitive = new(OwlPropertyCharacteristic.Transitive, Property("s")) { Origin = Origin("transitive") };
        OwlClassAssertionAxiom all = new(new OwlObjectAllValuesFrom(Property("r"), Reference("C")), Named("a")) { Origin = Origin("all") };
        OwlObjectPropertyAssertionAxiom ab = new(Named("a"), new NamedNode(Utf8Strings.From(Example + "s")), Named("b")) { Origin = Origin("ab") };
        OwlObjectPropertyAssertionAxiom bc = new(Named("b"), new NamedNode(Utf8Strings.From(Example + "s")), Named("c")) { Origin = Origin("bc") };
        OwlClassAssertionAxiom notC = new(new OwlObjectComplementOf(Reference("C")), Named("c")) { Origin = Origin("notC") };

        Assert.IsFalse(Decide(engine, Module(hierarchy, transitive, all, ab, bc, notC)).IsConsistent, "∀r.C carries ∀s.C down the transitive sub-role s.");
        Assert.IsTrue(Decide(engine, Module(hierarchy, all, ab, bc, notC)).IsConsistent, "Without s transitive ∀r.C reaches only the direct s-successor.");
    }

    /// <summary>
    /// A necessarily-empty (unsatisfiable) class is subsumed by every class — the
    /// vacuous subsumption an empty extension forces — without dragging an
    /// unrelated satisfiable class along. This pins the codebase against the
    /// emptiness edge case that a Coq formalization of the EL classification
    /// algorithm found in the published completeness proof: a relation assumed
    /// transitive holds only under nonempty models, so an empty class is where
    /// such reasoning silently breaks. Deciding subsumption as the
    /// unsatisfiability of <c>A ⊓ ¬B</c> is robust to it by construction (an
    /// unsatisfiable A makes <c>A ⊓ ¬B</c> unsatisfiable for every B), and this
    /// test keeps it so across both engines.
    /// </summary>
    /// <param name="engine">The engine under test.</param>
    [TestMethod]
    [DataRow(ConsistencyEngine.Snapshot)]
    [DataRow(ConsistencyEngine.SatBacked)]
    public void EmptyClassIsSubsumedByEveryClassWithoutCorruptingOthers(ConsistencyEngine engine)
    {
        //A ⊑ C and A ⊑ ¬C force A's extension empty; D ⊑ E is an unrelated, satisfiable pair.
        OwlSubClassOfAxiom aInC = new(Reference("A"), Reference("C")) { Origin = Origin("aInC") };
        OwlSubClassOfAxiom aInNotC = new(Reference("A"), new OwlObjectComplementOf(Reference("C"))) { Origin = Origin("aInNotC") };
        OwlSubClassOfAxiom dInE = new(Reference("D"), Reference("E")) { Origin = Origin("dInE") };

        ModuleVerdict verdict = Decide(engine, Module(aInC, aInNotC, dInE));

        Assert.IsTrue(verdict.IsConsistent, "An empty class does not make the ontology inconsistent.");
        foreach(string super in new[] { "C", "D", "E" })
        {
            Assert.Contains(
                pair => Local(pair.SubClass) == "A" && Local(pair.SuperClass) == super,
                verdict.Subsumptions,
                $"The empty class A must be subsumed by {super}.");
        }

        //The empty class must not drag an unrelated satisfiable class with it: E is not subsumed by D.
        Assert.DoesNotContain(
            pair => Local(pair.SubClass) == "E" && Local(pair.SuperClass) == "D",
            verdict.Subsumptions,
            "A satisfiable class is not spuriously subsumed.");
    }

    /// <summary>
    /// A starved inference budget abstains the snapshot decision on a module
    /// whose consistency check needs more than one rule application: the whole
    /// decision is a budget abstention carrying the spent tableau counters, and
    /// it surfaces no verdict.
    /// </summary>
    [TestMethod]
    public void DecideModuleAbstainsOnAStarvedInferenceBudget()
    {
        OwlSubClassOfAxiom aUnderB = new(Reference("A"), Reference("B")) { Origin = Origin("ab") };
        OwlSubClassOfAxiom bUnderC = new(Reference("B"), Reference("C")) { Origin = Origin("bc") };

        ModuleDecision decision = AlcModuleReasoner.DecideModule(
            Module(aUnderB, bUnderC),
            new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1),
            TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome);
        Assert.IsNull(decision.Verdict, "A budget abstention surfaces no verdict.");

        //The snapshot engine runs no solver, and the spent rule applications are
        //the abstention's evidence: non-zero, and stopped almost immediately by
        //the one-inference ceiling.
        Assert.AreEqual(0, decision.Statistics.SolveCount);
        Assert.IsGreaterThan(0, decision.Statistics.TableauTotals.RuleApplications, "The abstention carries the rule applications it spent.");
        Assert.IsLessThan(4, decision.Statistics.TableauTotals.RuleApplications, "The one-inference ceiling stops the tableau far short of a full decision.");
    }

    /// <summary>
    /// An inference ceiling crossed during the subsumption sweep abstains the
    /// whole decision, never a partial verdict: the consistency check
    /// completes and at least one subsumption-pair tableau runs before the
    /// ceiling trips, so the sweep-leg abstention is a whole-decision abstention.
    /// </summary>
    [TestMethod]
    public void DecideModuleAbstainsDuringTheSubsumptionSweep()
    {
        OwlSubClassOfAxiom aUnderB = new(Reference("A"), Reference("B")) { Origin = Origin("ab") };
        OwlSubClassOfAxiom bUnderC = new(Reference("B"), Reference("C")) { Origin = Origin("bc") };

        //The unbounded decision spends eight rule applications across the
        //consistency check and the six subsumption-pair tableaux (pinned by
        //DecideModulePinsTheTableauCountersForAKnownDecision). A ceiling of seven
        //clears the consistency check but is crossed inside the sweep.
        ModuleDecision decision = AlcModuleReasoner.DecideModule(
            Module(aUnderB, bUnderC),
            new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 7),
            TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome);
        Assert.IsNull(decision.Verdict, "A sweep-leg abstention is a whole-decision abstention, never a partial verdict.");

        //TableauRuns greater than one proves the abstention fell in the sweep: the
        //consistency run completed and at least one subsumption-pair run began
        //before the ceiling tripped.
        Assert.IsGreaterThan(1, decision.Statistics.TableauTotals.TableauRuns, "The consistency check completed and the sweep had begun.");
        Assert.IsGreaterThan(0, decision.Statistics.TableauTotals.RuleApplications);
    }

    /// <summary>
    /// The inference bound is an inclusive ceiling: the chain fixture's
    /// decision spends exactly eight rule applications
    /// (DecideModulePinsTheTableauCountersForAKnownDecision), so a ceiling equal to
    /// that need is reached and abstains, while a ceiling one unit above completes
    /// the decision — the boundary the ReasoningBudget contract states.
    /// </summary>
    [TestMethod]
    public void InclusiveCeilingAbstainsAtExactNeedAndDecidesOneAbove()
    {
        OwlSubClassOfAxiom aUnderB = new(Reference("A"), Reference("B")) { Origin = Origin("ab") };
        OwlSubClassOfAxiom bUnderC = new(Reference("B"), Reference("C")) { Origin = Origin("bc") };
        ReasoningModule module = Module(aUnderB, bUnderC);

        ModuleDecision atNeed = AlcModuleReasoner.DecideModule(
            module,
            new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 8),
            TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, atNeed.Outcome, "A ceiling equal to the decision's eight-rule-application need is reached, so the inclusive ceiling abstains.");
        Assert.IsNull(atNeed.Verdict, "The at-need abstention carries no verdict.");

        ModuleDecision oneAbove = AlcModuleReasoner.DecideModule(
            module,
            new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 9),
            TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, oneAbove.Outcome, "A ceiling one unit above the eight-rule-application need completes the decision.");
        Assert.IsTrue(oneAbove.Verdict!.IsConsistent, "The chain fixture is consistent.");
    }

    /// <summary>
    /// The contract split: the modules that abstain under a starved budget still
    /// decide on the unbounded surfaces — the verdict-returning entry and both
    /// unbounded <see cref="ModuleDecision"/> entries — since the zero inference
    /// bound of <see cref="ReasoningBudget.Unbounded"/> never trips.
    /// </summary>
    [TestMethod]
    public void UnboundedSurfacesStillDecideWhatAStarvedBudgetAbstains()
    {
        OwlSubClassOfAxiom aUnderB = new(Reference("A"), Reference("B")) { Origin = Origin("ab") };
        OwlSubClassOfAxiom bUnderC = new(Reference("B"), Reference("C")) { Origin = Origin("bc") };
        ReasoningModule module = Module(aUnderB, bUnderC);

        Assert.IsTrue(AlcModuleReasoner.Decide(module, TestContext.CancellationToken).IsConsistent, "The verdict surface is unbounded and decides.");

        ModuleDecision unbounded = AlcModuleReasoner.DecideModule(module, ReasoningBudget.Unbounded, TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, unbounded.Outcome);
        Assert.IsNotNull(unbounded.Verdict);
        Assert.IsTrue(unbounded.Verdict.IsConsistent);

        ModuleDecision noBudgetArgument = AlcModuleReasoner.DecideModule(module, TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, noBudgetArgument.Outcome);
        Assert.IsNotNull(noBudgetArgument.Verdict);
    }

    /// <summary>
    /// The budget-carrying seam delegate abstains directly: invoked under a
    /// starved budget the returned delegate yields a budget abstention, and under
    /// <see cref="ReasoningBudget.Unbounded"/> it decides — the fallback-omitted
    /// composition inherits a bounded snapshot oracle.
    /// </summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task CreateDelegateWithABudgetAbstainsWhenStarvedAndDecidesUnbounded()
    {
        OwlSubClassOfAxiom aUnderB = new(Reference("A"), Reference("B")) { Origin = Origin("ab") };
        OwlSubClassOfAxiom bUnderC = new(Reference("B"), Reference("C")) { Origin = Origin("bc") };
        ReasoningModule module = Module(aUnderB, bUnderC);

        DescriptionLogicDelegate starved = AlcModuleReasoner.CreateDelegate(new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1));
        ModuleDecision starvedDecision = await starved(module, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, starvedDecision.Outcome);
        Assert.IsNull(starvedDecision.Verdict);

        DescriptionLogicDelegate unbounded = AlcModuleReasoner.CreateDelegate(ReasoningBudget.Unbounded);
        ModuleDecision unboundedDecision = await unbounded(module, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, unboundedDecision.Outcome);
        Assert.IsNotNull(unboundedDecision.Verdict);
        Assert.IsTrue(unboundedDecision.Verdict.IsConsistent);
    }

    /// <summary>
    /// The registry- and budget-carrying full decision
    /// <see cref="AlcModuleReasoner.DecideModule(ReasoningModule, DatatypeRegistry, ReasoningBudget, System.Threading.CancellationToken)"/>
    /// on a non-empty registry: a generous finite budget decides the chain fixture,
    /// and a one-inference ceiling abstains with a reason — the registry rides the
    /// bounded decision without changing its outcome on a module carrying no
    /// datatype obligation.
    /// </summary>
    [TestMethod]
    public void DecideModuleWithRegistryAndBudgetDecidesGenerouslyAndAbstainsStarved()
    {
        OwlSubClassOfAxiom aUnderB = new(Reference("A"), Reference("B")) { Origin = Origin("ab") };
        OwlSubClassOfAxiom bUnderC = new(Reference("B"), Reference("C")) { Origin = Origin("bc") };
        ReasoningModule module = Module(aUnderB, bUnderC);
        DatatypeRegistry registry = NonEmptyRegistry();

        ModuleDecision decided = AlcModuleReasoner.DecideModule(module, registry, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1000), TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decided.Outcome, "A generous finite budget decides the chain fixture through the registry-carrying entry.");
        Assert.IsTrue(decided.Verdict!.IsConsistent, "The chain fixture is consistent.");

        ModuleDecision abstained = AlcModuleReasoner.DecideModule(module, registry, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1), TestContext.CancellationToken);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, abstained.Outcome, "A one-inference ceiling abstains through the registry-carrying entry.");
        Assert.IsNull(abstained.Verdict, "The starved abstention carries no verdict.");
    }

    /// <summary>
    /// The composed snapshot engines <see cref="ReasoningEngines.Snapshot(ReasoningBudget)"/>
    /// and <see cref="ReasoningEngines.Snapshot(DatatypeRegistry, ReasoningBudget)"/>
    /// invoked as seam delegates: a generous finite budget decides the chain fixture
    /// and a one-inference ceiling abstains, on both the budget-only and the
    /// registry-carrying wrapper.
    /// </summary>
    /// <returns>The asynchronous test.</returns>
    [TestMethod]
    public async Task SnapshotEnginesBoundTheDecisionUnderTheSuppliedBudget()
    {
        OwlSubClassOfAxiom aUnderB = new(Reference("A"), Reference("B")) { Origin = Origin("ab") };
        OwlSubClassOfAxiom bUnderC = new(Reference("B"), Reference("C")) { Origin = Origin("bc") };
        ReasoningModule module = Module(aUnderB, bUnderC);
        DatatypeRegistry registry = NonEmptyRegistry();

        DescriptionLogicDelegate generous = ReasoningEngines.Snapshot(new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1000));
        ModuleDecision generousDecision = await generous(module, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, generousDecision.Outcome, "The budget-only snapshot engine decides the chain fixture under a generous budget.");

        DescriptionLogicDelegate starved = ReasoningEngines.Snapshot(new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1));
        ModuleDecision starvedDecision = await starved(module, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, starvedDecision.Outcome, "The budget-only snapshot engine abstains under a one-inference ceiling.");

        DescriptionLogicDelegate generousRegistry = ReasoningEngines.Snapshot(registry, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1000));
        ModuleDecision generousRegistryDecision = await generousRegistry(module, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, generousRegistryDecision.Outcome, "The registry-carrying snapshot engine decides under a generous budget.");

        DescriptionLogicDelegate starvedRegistry = ReasoningEngines.Snapshot(registry, new ReasoningBudget(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 1));
        ModuleDecision starvedRegistryDecision = await starvedRegistry(module, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, starvedRegistryDecision.Outcome, "The registry-carrying snapshot engine abstains under a one-inference ceiling.");
    }

    /// <summary>Builds a minimal non-empty registry carrying one bounded datatype, so the registry-carrying decision entries exercise a populated registry.</summary>
    /// <returns>The frozen registry.</returns>
    private static DatatypeRegistry NonEmptyRegistry()
    {
        DatatypeRegistryBuilder builder = new();
        builder.Add(new BoundedDatatype(
            Utf8Strings.From("http://example.org/Percent"),
            Vocabulary.Xsd.Integer,
            [
                new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MinInclusive), new Literal(Utf8Strings.From("0"), new NamedNode(Vocabulary.Xsd.Integer))),
                new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MaxInclusive), new Literal(Utf8Strings.From("100"), new NamedNode(Vocabulary.Xsd.Integer))),
            ]));

        return builder.Build();
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
    private static NamedNode Named(string local)
    {
        return new NamedNode(Utf8Strings.From(Example + local));
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
