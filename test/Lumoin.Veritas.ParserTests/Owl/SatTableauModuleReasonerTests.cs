using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sat;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Tests for <see cref="SatTableauModuleReasoner"/>: deterministic
/// differential sweeps of the SAT-backed and EL-coupled engines against the
/// snapshot reference over generated ALC(H) modules — consistency agreement, and
/// full-verdict parity including the module-local subsumption sets, with the
/// EL fast-path exercised on its in-fragment rounds — the tableau boundary matrix (existentials into ⊥, ∀-only
/// labels, blocking cycles, shared successor sets, per-individual worlds,
/// fragment honesty), the joint-instance boundary matrix over asserted
/// role edges (edge universal propagation, told super-role reach, cycle
/// transitivity, self-edges, joint-to-chain handoff, chain failures
/// teaching the joint root, inconsistent TBoxes), the propagation-only
/// search mode, and a hard propositional reach case decided by this engine
/// alone.
/// </summary>
[TestClass]
internal sealed class SatTableauModuleReasonerTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The IRI prefix the test classes, roles, and individuals live under.</summary>
    private const string Example = "http://example.org/";

    /// <summary>The <c>owl:Thing</c> IRI, the fixed-⊤ class reference.</summary>
    private const string Thing = "http://www.w3.org/2002/07/owl#Thing";

    /// <summary>The <c>owl:Nothing</c> IRI, the fixed-⊥ class reference.</summary>
    private const string Nothing = "http://www.w3.org/2002/07/owl#Nothing";

    /// <summary>The named classes the differential sweep draws from.</summary>
    private static readonly string[] SweepClasses = ["A0", "A1", "A2", "A3"];

    /// <summary>The roles the differential sweep draws from; <c>s</c> occasionally gains a told super-role.</summary>
    private static readonly string[] SweepRoles = ["r0", "r1", "s"];

    /// <summary>
    /// The SAT-backed verdict agrees with the snapshot engine round for
    /// round over a deterministic sweep of generated in-scope ALC(H)
    /// modules — disjunction-heavy TBoxes, occasional role-hierarchy pairs
    /// and cyclic inclusions, zero to two asserted individuals, zero to
    /// three asserted role edges including self-edges and cycles — and
    /// both verdicts occur.
    /// </summary>
    [TestMethod]
    public void DifferentialSweepAgreesWithTheSnapshotEngine()
    {
        //A deterministic xorshift drives the generation; no entropy APIs.
        ulong state = 0xB5297A4D3F84D5B5UL;
        int consistentSeen = 0;
        int inconsistentSeen = 0;
        int elDecidedSeen = 0;

        for(int round = 0; round < 300; round++)
        {
            ReasoningModule module = GenerateModule(ref state);
            bool snapshot = AlcModuleReasoner.DecideConsistency(module, TestContext.CancellationToken).IsConsistent;
            bool satBacked = SatTableauModuleReasoner.DecideConsistency(module, cancellationToken: TestContext.CancellationToken).IsConsistent;
            ModuleDecision elDecision = ElCoupledModuleReasoner.DecideModule(module, TestContext.CancellationToken);

            Assert.AreEqual(snapshot, satBacked, $"Round {round}: the SAT-backed and snapshot engines disagree on consistency.");
            Assert.AreEqual(snapshot, elDecision.Verdict!.IsConsistent, $"Round {round}: the EL-coupled and snapshot engines disagree on consistency.");

            if(elDecision.Statistics.ElTotals.ElDecided)
            {
                elDecidedSeen++;
            }

            if(snapshot)
            {
                consistentSeen++;
            }
            else
            {
                inconsistentSeen++;
            }
        }

        Assert.IsGreaterThan(20, consistentSeen, "The sweep covers consistent modules.");
        Assert.IsGreaterThan(20, inconsistentSeen, "The sweep covers inconsistent modules.");
        Assert.IsGreaterThan(0, elDecidedSeen, "The sweep exercises the EL fast-path, not only its fallback.");
    }

    /// <summary>
    /// The full <see cref="SatTableauModuleReasoner.Decide"/> verdict
    /// agrees with the snapshot engine's round for round over a
    /// deterministic sweep of generated modules within the subsumption
    /// signature cap: consistency matches and the module-local subsumption
    /// sets are equal under order-insensitive comparison, with rounds that
    /// surface subsumptions covered.
    /// </summary>
    [TestMethod]
    public void DecideParityAgreesWithTheSnapshotEngine()
    {
        //A deterministic xorshift drives the generation; no entropy APIs.
        ulong state = 0x9E3779B97F4A7C15UL;
        int subsumptionsSeen = 0;

        for(int round = 0; round < 150; round++)
        {
            ReasoningModule module = GenerateModule(ref state);
            ModuleVerdict snapshot = AlcModuleReasoner.Decide(module, TestContext.CancellationToken);
            ModuleVerdict satBacked = SatTableauModuleReasoner.Decide(module, cancellationToken: TestContext.CancellationToken);
            ModuleVerdict elCoupled = ElCoupledModuleReasoner.Decide(module, TestContext.CancellationToken);

            Assert.AreEqual(snapshot.IsConsistent, satBacked.IsConsistent, $"Round {round}: the SAT-backed and snapshot engines disagree on consistency.");
            Assert.AreEqual(snapshot.IsConsistent, elCoupled.IsConsistent, $"Round {round}: the EL-coupled and snapshot engines disagree on consistency.");

            List<string> snapshotPairs = SubsumptionKeys(snapshot);
            List<string> satBackedPairs = SubsumptionKeys(satBacked);
            List<string> elCoupledPairs = SubsumptionKeys(elCoupled);
            Assert.AreSequenceEqual(snapshotPairs, satBackedPairs, $"Round {round}: the SAT-backed and snapshot engines disagree on the subsumption set.");
            Assert.AreSequenceEqual(snapshotPairs, elCoupledPairs, $"Round {round}: the EL-coupled and snapshot engines disagree on the subsumption set.");
            subsumptionsSeen += snapshotPairs.Count;
        }

        Assert.IsGreaterThan(0, subsumptionsSeen, "The sweep covers rounds that surface subsumptions.");
    }

    /// <summary>
    /// The incremental-session path decides exactly as the stateless per-solve path
    /// over a deterministic sweep of generated in-scope ALC(H) modules: consistency
    /// matches and the module-local subsumption sets are equal. This holds the opt-in
    /// session verdict-equivalent to the default engine as the shared CNF grows across
    /// a decision's world solves — the correctness baseline the reuse rests on.
    /// </summary>
    [TestMethod]
    public void IncrementalSessionAgreesWithTheStatelessEngine()
    {
        //A deterministic xorshift drives the generation; no entropy APIs.
        ulong state = 0x2545F4914F6CDD1DUL;
        int consistentSeen = 0;
        int inconsistentSeen = 0;
        int subsumptionsSeen = 0;

        for(int round = 0; round < 300; round++)
        {
            ReasoningModule module = GenerateModule(ref state);
            ModuleVerdict stateless = SatTableauModuleReasoner.DecideModule(module, ReasoningBudget.Unbounded, SatSearchMode.ConflictLearning, useIncrementalSession: false, TestContext.CancellationToken).Verdict!;
            ModuleVerdict sessioned = SatTableauModuleReasoner.DecideModule(module, ReasoningBudget.Unbounded, SatSearchMode.ConflictLearning, useIncrementalSession: true, TestContext.CancellationToken).Verdict!;

            Assert.AreEqual(stateless.IsConsistent, sessioned.IsConsistent, $"Round {round}: the incremental-session and stateless engines disagree on consistency.");
            Assert.AreSequenceEqual(SubsumptionKeys(stateless), SubsumptionKeys(sessioned), $"Round {round}: the incremental-session and stateless engines disagree on the subsumption set.");

            if(stateless.IsConsistent)
            {
                consistentSeen++;
            }
            else
            {
                inconsistentSeen++;
            }

            subsumptionsSeen += stateless.Subsumptions.Count;
        }

        Assert.IsGreaterThan(20, consistentSeen, "The sweep covers consistent modules.");
        Assert.IsGreaterThan(20, inconsistentSeen, "The sweep covers inconsistent modules.");
        Assert.IsGreaterThan(0, subsumptionsSeen, "The sweep covers rounds that surface subsumptions.");
    }

    /// <summary>The verdict's subsumption pairs as sorted comparison keys, one <c>sub→super</c> string per pair.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The keys, sorted ordinally.</returns>
    private static List<string> SubsumptionKeys(ModuleVerdict verdict)
    {
        List<string> keys = new(verdict.Subsumptions.Count);
        foreach((NamedNode subClass, NamedNode superClass) in verdict.Subsumptions)
        {
            keys.Add($"{subClass.Iri}→{superClass.Iri}");
        }

        keys.Sort(StringComparer.Ordinal);

        return keys;
    }

    /// <summary>
    /// An existential into ⊥ refutes its successor, the learned modal
    /// clause forces the existential off, and the world re-solves: the
    /// guarded inclusion stays consistent with an empty subclass, while the
    /// global inclusion leaves the world no way out.
    /// </summary>
    [TestMethod]
    public void ExistentialIntoBottomLearnsAndRecovers()
    {
        OwlSubClassOfAxiom guarded = new(
            Reference("A"),
            new OwlObjectSomeValuesFrom(Property("r"), Reference(Nothing, qualified: true)))
        {
            Origin = Origin("guarded"),
        };

        Assert.IsTrue(DecideSat(Module(guarded)).IsConsistent, "A ⊑ ∃r.⊥ holds with A empty.");

        OwlSubClassOfAxiom global = new(
            Reference(Thing, qualified: true),
            new OwlObjectSomeValuesFrom(Property("r"), Reference(Nothing, qualified: true)))
        {
            Origin = Origin("global"),
        };

        Assert.IsFalse(DecideSat(Module(global)).IsConsistent, "⊤ ⊑ ∃r.⊥ leaves no world satisfiable.");
    }

    /// <summary>A world whose label carries only universals spawns no successors and stays consistent.</summary>
    [TestMethod]
    public void UniversalOnlyLabelsSpawnNoSuccessors()
    {
        OwlClassAssertionAxiom allBottom = new(
            new OwlObjectAllValuesFrom(Property("r"), Reference(Nothing, qualified: true)),
            Named("a"))
        {
            Origin = Origin("allBottom"),
        };
        OwlSubClassOfAxiom range = new(
            Reference(Thing, qualified: true),
            new OwlObjectAllValuesFrom(Property("r"), Reference("C")))
        {
            Origin = Origin("range"),
        };

        Assert.IsTrue(DecideSat(Module(allBottom, range)).IsConsistent);
    }

    /// <summary>A cyclic inclusion terminates through blocking and stays consistent, with and without a seeding individual.</summary>
    [TestMethod]
    public void BlockingCycleStaysConsistent()
    {
        OwlSubClassOfAxiom cycle = new(
            Reference("C"),
            new OwlObjectSomeValuesFrom(Property("r"), Reference("C")))
        {
            Origin = Origin("cycle"),
        };

        Assert.IsTrue(DecideSat(Module(cycle)).IsConsistent, "The pure TBox cycle is consistent.");

        OwlClassAssertionAxiom seed = new(Reference("C"), Named("a")) { Origin = Origin("seed") };

        Assert.IsTrue(DecideSat(Module(cycle, seed)).IsConsistent, "The seeded cycle blocks and stays consistent.");

        OwlSubClassOfAxiom global = new(
            Reference(Thing, qualified: true),
            new OwlObjectSomeValuesFrom(Property("r"), Reference("C")))
        {
            Origin = Origin("global"),
        };

        Assert.IsTrue(DecideSat(Module(cycle, global)).IsConsistent, "Every world spawns into the cycle and blocks.");
    }

    /// <summary>Two existentials over different roles building the same successor set answer through the memo table and stay consistent.</summary>
    [TestMethod]
    public void SharedSuccessorSetsMemoise()
    {
        OwlSubClassOfAxiom viaR = new(
            Reference("A"),
            new OwlObjectSomeValuesFrom(Property("r"), Reference("C")))
        {
            Origin = Origin("viaR"),
        };
        OwlSubClassOfAxiom viaS = new(
            Reference("A"),
            new OwlObjectSomeValuesFrom(Property("s"), Reference("C")))
        {
            Origin = Origin("viaS"),
        };
        OwlClassAssertionAxiom seed = new(Reference("A"), Named("a")) { Origin = Origin("seed") };

        Assert.IsTrue(DecideSat(Module(viaR, viaS, seed)).IsConsistent);

        //The unsatisfiable variant: the shared successor set {C} refutes
        //once C is globally empty, and both existentials must come off —
        //which the asserted A forbids.
        OwlSubClassOfAxiom cEmpty = new(Reference("C"), Reference(Nothing, qualified: true)) { Origin = Origin("cEmpty") };

        Assert.IsFalse(DecideSat(Module(viaR, viaS, seed, cEmpty)).IsConsistent);
    }

    /// <summary>An individual asserted ⊥ makes the module inconsistent.</summary>
    [TestMethod]
    public void IndividualAssertedBottomIsInconsistent()
    {
        OwlClassAssertionAxiom impossible = new(Reference(Nothing, qualified: true), Named("a")) { Origin = Origin("impossible") };

        Assert.IsFalse(DecideSat(Module(impossible)).IsConsistent);
    }

    /// <summary>Individuals are independent worlds, and one unsatisfiable world condemns the module.</summary>
    [TestMethod]
    public void OneBadIndividualCondemnsTheModule()
    {
        OwlClassAssertionAxiom fine = new(Reference("A"), Named("a")) { Origin = Origin("fine") };
        OwlClassAssertionAxiom contradictory = new(
            new OwlObjectIntersectionOf([Reference("B"), new OwlObjectComplementOf(Reference("B"))]),
            Named("b"))
        {
            Origin = Origin("contradictory"),
        };

        Assert.IsTrue(DecideSat(Module(fine)).IsConsistent);
        Assert.IsFalse(DecideSat(Module(fine, contradictory)).IsConsistent);
    }

    /// <summary>A universal propagates across an asserted role edge in the joint instance: the target carries the filler and clashes with its complement assertion.</summary>
    [TestMethod]
    public void EdgeUniversalPropagatesAcrossTheAssertedEdge()
    {
        OwlClassAssertionAxiom forbids = new(
            new OwlObjectAllValuesFrom(Property("r"), new OwlObjectComplementOf(Reference("C"))), Named("a"))
        {
            Origin = Origin("forbids"),
        };
        OwlObjectPropertyAssertionAxiom edge = Edge("a", "r", "b", "edge");
        OwlClassAssertionAxiom isC = new(Reference("C"), Named("b")) { Origin = Origin("isC") };

        Assert.IsFalse(DecideSat(Module(forbids, edge, isC)).IsConsistent, "∀r.¬C at a reaches b over the edge and clashes with C.");
        Assert.IsTrue(DecideSat(Module(forbids, edge)).IsConsistent, "The edge alone carries no clash.");
        Assert.IsTrue(DecideSat(Module(forbids, isC)).IsConsistent, "Without the edge the universal reaches nothing.");
    }

    /// <summary>The edge universal propagates through a told super-role: an edge over the sub-role triggers the super-role's universal.</summary>
    [TestMethod]
    public void EdgeUniversalReachesThroughTheToldSuperRole()
    {
        OwlSubObjectPropertyOfAxiom hierarchy = new(Property("s"), Property("r")) { Origin = Origin("hierarchy") };
        OwlClassAssertionAxiom forbids = new(
            new OwlObjectAllValuesFrom(Property("r"), new OwlObjectComplementOf(Reference("C"))), Named("a"))
        {
            Origin = Origin("forbids"),
        };
        OwlObjectPropertyAssertionAxiom edge = Edge("a", "s", "b", "edge");
        OwlClassAssertionAxiom isC = new(Reference("C"), Named("b")) { Origin = Origin("isC") };

        Assert.IsFalse(DecideSat(Module(hierarchy, forbids, edge, isC)).IsConsistent, "The s-edge reaches the ∀r restriction through s ⊑ r.");
        Assert.IsTrue(DecideSat(Module(forbids, edge, isC)).IsConsistent, "Without the hierarchy the universal does not reach over s.");
    }

    /// <summary>Universal propagation runs transitively around a two-cycle of asserted edges: the consequence returns to its origin after both hops.</summary>
    [TestMethod]
    public void EdgeCyclePropagatesTransitively()
    {
        //A ⊑ ∀r.B and B ⊑ ∀r.C: A at a puts B at b over a→r→b, and B at b
        //puts C back at a over b→r→a — two hops through the cycle.
        OwlSubClassOfAxiom aAll = new(
            Reference("A"),
            new OwlObjectAllValuesFrom(Property("r"), Reference("B")))
        {
            Origin = Origin("aAll"),
        };
        OwlSubClassOfAxiom bAll = new(
            Reference("B"),
            new OwlObjectAllValuesFrom(Property("r"), Reference("C")))
        {
            Origin = Origin("bAll"),
        };
        OwlObjectPropertyAssertionAxiom forward = Edge("a", "r", "b", "forward");
        OwlObjectPropertyAssertionAxiom backward = Edge("b", "r", "a", "backward");
        OwlClassAssertionAxiom seedA = new(Reference("A"), Named("a")) { Origin = Origin("seedA") };
        OwlClassAssertionAxiom notC = new(new OwlObjectComplementOf(Reference("C")), Named("a")) { Origin = Origin("notC") };

        Assert.IsFalse(DecideSat(Module(aAll, bAll, forward, backward, seedA, notC)).IsConsistent, "C returns to a around the cycle and clashes with ¬C.");
        Assert.IsTrue(DecideSat(Module(aAll, bAll, forward, seedA, notC)).IsConsistent, "Without the return edge nothing carries C back to a.");
    }

    /// <summary>A self-edge propagates the individual's own universal onto itself.</summary>
    [TestMethod]
    public void SelfEdgePropagatesOntoTheIndividual()
    {
        OwlObjectPropertyAssertionAxiom loop = Edge("a", "r", "a", "loop");
        OwlClassAssertionAxiom isC = new(Reference("C"), Named("a")) { Origin = Origin("isC") };
        OwlClassAssertionAxiom forbids = new(
            new OwlObjectAllValuesFrom(Property("r"), new OwlObjectComplementOf(Reference("C"))), Named("a"))
        {
            Origin = Origin("forbids"),
        };
        OwlClassAssertionAxiom keeps = new(
            new OwlObjectAllValuesFrom(Property("r"), Reference("C")), Named("a"))
        {
            Origin = Origin("keeps"),
        };

        Assert.IsFalse(DecideSat(Module(loop, isC, forbids)).IsConsistent, "∀r.¬C over the self-edge contradicts the individual's own C.");
        Assert.IsTrue(DecideSat(Module(loop, isC, keeps)).IsConsistent, "∀r.C over the self-edge agrees with the individual's own C.");
    }

    /// <summary>An edge target forced into an existential spawns an anonymous successor chain off the joint instance, and the chain's verdict decides the module.</summary>
    [TestMethod]
    public void EdgeTargetSpawnsAnAnonymousSuccessor()
    {
        //a: ∀r.(∃s.C) and a →r→ b force ∃s.C at b, whose successor is an
        //anonymous world off the joint instance.
        OwlClassAssertionAxiom all = new(
            new OwlObjectAllValuesFrom(
                Property("r"),
                new OwlObjectSomeValuesFrom(Property("s"), Reference("C"))),
            Named("a"))
        {
            Origin = Origin("all"),
        };
        OwlObjectPropertyAssertionAxiom edge = Edge("a", "r", "b", "edge");

        Assert.IsTrue(DecideSat(Module(all, edge)).IsConsistent, "The anonymous successor carries C and stands.");

        OwlSubClassOfAxiom cEmpty = new(Reference("C"), Reference(Nothing, qualified: true)) { Origin = Origin("cEmpty") };

        Assert.IsFalse(DecideSat(Module(all, edge, cEmpty)).IsConsistent, "With C globally empty the forced chain fails and condemns the joint root.");
    }

    /// <summary>A successor-chain failure teaches the joint instance: the learned template clause instantiates into every block, forcing the existential off where an alternative exists and condemning the root where none does.</summary>
    [TestMethod]
    public void ChainFailureTeachesTheJointInstance()
    {
        OwlObjectPropertyAssertionAxiom edge = Edge("a", "r", "b", "edge");
        OwlClassAssertionAxiom impossible = new(
            new OwlObjectSomeValuesFrom(Property("s"), Reference(Nothing, qualified: true)), Named("a"))
        {
            Origin = Origin("impossible"),
        };

        Assert.IsFalse(DecideSat(Module(edge, impossible)).IsConsistent, "∃s.⊥ asserted with edges present condemns the joint root.");

        OwlClassAssertionAxiom recoverable = new(
            new OwlObjectUnionOf(
            [
                new OwlObjectSomeValuesFrom(Property("s"), Reference(Nothing, qualified: true)),
                Reference("B"),
            ]),
            Named("a"))
        {
            Origin = Origin("recoverable"),
        };

        Assert.IsTrue(DecideSat(Module(edge, recoverable)).IsConsistent, "The learned clause forces the existential off and the B disjunct survives.");
    }

    /// <summary>An inconsistent TBox condemns a module with asserted edges: every block carries the contradiction.</summary>
    [TestMethod]
    public void EdgesWithAnInconsistentTBoxAreCondemned()
    {
        OwlSubClassOfAxiom contradiction = new(
            Reference(Thing, qualified: true),
            Reference(Nothing, qualified: true))
        {
            Origin = Origin("contradiction"),
        };
        OwlObjectPropertyAssertionAxiom edge = Edge("a", "r", "b", "edge");

        Assert.IsFalse(DecideSat(Module(contradiction, edge)).IsConsistent);
    }

    /// <summary>The beyond-fragment remainder is named identically to the snapshot engine, and the supported fragment still decides.</summary>
    [TestMethod]
    public void FragmentHonestyMatchesTheSnapshotEngine()
    {
        OwlSubClassOfAxiom supported = new(Reference("A"), Reference("B")) { Origin = Origin("ab") };
        OwlHasKeyAxiom beyond = new(Reference("A"), [Property("r")], []) { Origin = Origin("key") };
        ReasoningModule module = Module(supported, beyond);

        ModuleVerdict satBacked = DecideSat(module);
        ModuleVerdict snapshot = AlcModuleReasoner.DecideConsistency(module, TestContext.CancellationToken);

        Assert.IsTrue(satBacked.IsConsistent);
        Assert.IsEmpty(satBacked.Subsumptions, "The consistency entry surfaces no subsumptions.");
        List<string> snapshotNames = [.. snapshot.UnsupportedConstructs];
        List<string> satBackedNames = [.. satBacked.UnsupportedConstructs];
        Assert.AreSequenceEqual(snapshotNames, satBackedNames, "The remainder naming is identical.");
        Assert.Contains(nameof(OwlHasKeyAxiom), satBacked.UnsupportedConstructs);
    }

    /// <summary>The propagation-only search mode decides the same verdicts as the default conflict-learning mode.</summary>
    [TestMethod]
    public void PropagationOnlySearchModeAgrees()
    {
        OwlSubClassOfAxiom cycle = new(
            Reference("C"),
            new OwlObjectSomeValuesFrom(Property("r"), Reference("C")))
        {
            Origin = Origin("cycle"),
        };
        OwlClassAssertionAxiom seed = new(Reference("C"), Named("a")) { Origin = Origin("seed") };

        Assert.IsTrue(SatTableauModuleReasoner.DecideConsistency(Module(cycle, seed), SatSearchMode.PropagationOnly, TestContext.CancellationToken).IsConsistent);

        OwlSubClassOfAxiom global = new(
            Reference(Thing, qualified: true),
            new OwlObjectSomeValuesFrom(Property("r"), Reference(Nothing, qualified: true)))
        {
            Origin = Origin("global"),
        };

        Assert.IsFalse(SatTableauModuleReasoner.DecideConsistency(Module(global), SatSearchMode.PropagationOnly, TestContext.CancellationToken).IsConsistent);
    }

    /// <summary>
    /// A work budget bounds the decision: an acyclic existential chain
    /// (<c>C ⊑ ∃r.D</c> with <c>C(a)</c>) needs a second world solve for the
    /// successor, so a one-solve budget cannot finish it and the decision
    /// abstains with a reason and no verdict — while an unbounded budget
    /// decides it. The differential oracle compares verdicts, not solve
    /// counts, so the budget gets its own test.
    /// </summary>
    [TestMethod]
    public void DecideModuleAbstainsWhenTheBudgetIsExhausted()
    {
        OwlSubClassOfAxiom chain = new(
            Reference("C"),
            new OwlObjectSomeValuesFrom(Property("r"), Reference("D")))
        {
            Origin = Origin("chain"),
        };
        OwlClassAssertionAxiom seed = new(Reference("C"), Named("a")) { Origin = Origin("seed") };
        ReasoningModule module = Module(chain, seed);

        //Unbounded: the decision completes and reports more than one solve.
        ModuleDecision unbounded = SatTableauModuleReasoner.DecideModule(
            module, ReasoningBudget.Unbounded, SatSearchMode.ConflictLearning, cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, unbounded.Outcome);
        Assert.IsNotNull(unbounded.Verdict);
        Assert.IsTrue(unbounded.Verdict.IsConsistent);
        Assert.IsGreaterThan(1, unbounded.Statistics.SolveCount, "The chain needs more than the seed solve.");

        //A one-solve budget cannot finish: the decision abstains, carrying no
        //verdict but the work it spent up to the bound.
        ModuleDecision bounded = SatTableauModuleReasoner.DecideModule(
            module, new ReasoningBudget(MaxSolves: 1, MaxConflicts: 0, MaxInferences: 0), SatSearchMode.ConflictLearning, cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, bounded.Outcome);
        Assert.IsNull(bounded.Verdict);
        Assert.AreEqual(1, bounded.Statistics.SolveCount, "Exactly the budgeted one solve ran before abstaining.");
    }

    /// <summary>The SAT engine's counters for a known decision are pinned, so a regression that mis-counts the solver work is caught — the telemetry oracle, not a mere non-zero check.</summary>
    [TestMethod]
    public void DecideModulePinsTheSolverCountersForAKnownDecision()
    {
        ModuleDecision decision = SatTableauModuleReasoner.DecideModule(
            PigeonholeModule(pigeons: 4, holes: 2), ReasoningBudget.Unbounded, SatSearchMode.ConflictLearning, cancellationToken: TestContext.CancellationToken);

        //Four pigeons into two holes is inconsistent; with no asserted
        //individuals the engine decides it in one anonymous-root world solve.
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome);
        Assert.IsFalse(decision.Verdict!.IsConsistent);
        Assert.AreEqual(1, decision.Statistics.SolveCount);

        //The solver counters for that single solve are pinned, so a regression
        //that mis-counts the work is caught.
        SatSolveStatistics totals = decision.Statistics.SolverTotals;
        Assert.AreEqual(1, totals.Decisions);
        Assert.AreEqual(19L, totals.Propagations);
        Assert.AreEqual(2, totals.Conflicts);
        Assert.AreEqual(1, totals.LearnedClauses);
        Assert.AreEqual(1, totals.MaxDecisionLevel);
    }

    /// <summary>
    /// The stateless and the reused-session lanes each carry pinned solver
    /// counters for a fixed decision that spawns a successor world, refutes it,
    /// learns the modal conflict clause, and re-solves the seed — the golden
    /// oracle holding each lane to today's measured search, so a deterministic
    /// drift in either lane's solve pipeline fails by name rather than slipping
    /// through an engines-agree comparison.
    /// </summary>
    [TestMethod]
    public void BothSolveLanesPinTheCountersForALearningDecision()
    {
        OwlSubClassOfAxiom chain = new(
            Reference("C"),
            new OwlObjectSomeValuesFrom(Property("r"), Reference("D")))
        {
            Origin = Origin("chain"),
        };
        OwlSubClassOfAxiom refuted = new(
            Reference("D"),
            Reference(Nothing, qualified: true))
        {
            Origin = Origin("refuted"),
        };
        OwlClassAssertionAxiom seed = new(Reference("C"), Named("a")) { Origin = Origin("seed") };
        ReasoningModule module = Module(chain, refuted, seed);

        ModuleDecision stateless = SatTableauModuleReasoner.DecideModule(
            module, ReasoningBudget.Unbounded, SatSearchMode.ConflictLearning, useIncrementalSession: false, TestContext.CancellationToken);
        ModuleDecision session = SatTableauModuleReasoner.DecideModule(
            module, ReasoningBudget.Unbounded, SatSearchMode.ConflictLearning, useIncrementalSession: true, TestContext.CancellationToken);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, stateless.Outcome);
        Assert.AreEqual(ReasoningDecisionOutcome.Decided, session.Outcome);
        Assert.IsFalse(stateless.Verdict!.IsConsistent, "The refuted successor propagates to the seed.");
        Assert.IsFalse(session.Verdict!.IsConsistent, "The lanes agree on the verdict.");

        //Three solves each lane: the seed model, the refuted successor, and the
        //seed re-solve under the learned modal clause. The counter tuples pin each
        //lane's own deterministic search.
        Assert.AreEqual(3, stateless.Statistics.SolveCount, "The stateless lane's solve count moved.");
        Assert.AreEqual(3, session.Statistics.SolveCount, "The session lane's solve count moved.");
        Assert.AreEqual(
            new SatSolveStatistics(Decisions: 0, Propagations: 7, Conflicts: 2, LearnedClauses: 0, MaxDecisionLevel: 0),
            stateless.Statistics.SolverTotals,
            "The stateless lane's pinned counters moved.");
        Assert.AreEqual(
            new SatSolveStatistics(Decisions: 1, Propagations: 2, Conflicts: 0, LearnedClauses: 0, MaxDecisionLevel: 1),
            session.Statistics.SolverTotals,
            "The session lane's pinned counters moved.");
    }

    /// <summary>
    /// A learned conflict core of mutually exclusive universal fillers
    /// keeps its existential trigger: the disjunct that satisfies both
    /// universals vacuously — asserting no successor — survives the modal
    /// clause its failing sibling disjunct teaches, and the module stays
    /// consistent on both engines. The extra atom keeps the vacuous
    /// disjunct structurally distinct from the failing disjunct's
    /// subformulas, and listing it first is the operand order under which
    /// the solve-and-shrink keeps the existential-bearing disjunct and
    /// spawns its failing successor — the order that exercises the learned
    /// clause's trigger.
    /// </summary>
    [TestMethod]
    public void UniversalOnlyConflictCoreKeepsItsExistentialTrigger()
    {
        OwlSubClassOfAxiom disjoint = new(
            Reference("D1"),
            new OwlObjectComplementOf(Reference("D2")))
        {
            Origin = Origin("disjoint"),
        };
        OwlClassAssertionAxiom choice = new(
            new OwlObjectUnionOf(
            [
                new OwlObjectIntersectionOf(
                [
                    Reference("B"),
                    new OwlObjectAllValuesFrom(Property("r"), Reference("D1")),
                    new OwlObjectAllValuesFrom(Property("r"), Reference("D2")),
                ]),
                new OwlObjectIntersectionOf(
                [
                    new OwlObjectSomeValuesFrom(Property("r"), Reference("C")),
                    new OwlObjectAllValuesFrom(Property("r"), Reference("D1")),
                    new OwlObjectAllValuesFrom(Property("r"), Reference("D2")),
                ]),
            ]),
            Named("a"))
        {
            Origin = Origin("choice"),
        };
        ReasoningModule module = Module(disjoint, choice);

        Assert.IsTrue(DecideSat(module).IsConsistent, "The all-universal disjunct holds vacuously with no successor asserted.");
        Assert.IsTrue(AlcModuleReasoner.DecideConsistency(module, TestContext.CancellationToken).IsConsistent, "The snapshot engine agrees.");
    }

    /// <summary>
    /// A pigeonhole-shaped TBox — every pigeon in some hole, no two
    /// pigeons sharing one — is hard propositional disjunction structure:
    /// the SAT-backed engine alone decides the over-full instance
    /// inconsistent and the fitting instance consistent.
    /// </summary>
    [TestMethod]
    public void HardPropositionalDisjunctionsDecide()
    {
        Assert.IsFalse(DecideSat(PigeonholeModule(pigeons: 4, holes: 2)).IsConsistent, "Four pigeons cannot fit two holes.");
        Assert.IsTrue(DecideSat(PigeonholeModule(pigeons: 2, holes: 2)).IsConsistent, "Two pigeons fit two holes.");
    }

    /// <summary>Builds the pigeonhole TBox: one global union per pigeon over its hole atoms, one disjointness per hole over its pigeon atoms.</summary>
    /// <param name="pigeons">The pigeon count.</param>
    /// <param name="holes">The hole count.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule PigeonholeModule(int pigeons, int holes)
    {
        List<OwlAxiom> axioms = [];
        for(int pigeon = 0; pigeon < pigeons; pigeon++)
        {
            List<OwlClassExpression> placements = [];
            for(int hole = 0; hole < holes; hole++)
            {
                placements.Add(Reference($"H{pigeon}_{hole}"));
            }

            axioms.Add(new OwlSubClassOfAxiom(Reference(Thing, qualified: true), new OwlObjectUnionOf(placements))
            {
                Origin = Origin($"pigeon{pigeon}"),
            });
        }

        for(int hole = 0; hole < holes; hole++)
        {
            List<OwlClassExpression> occupants = [];
            for(int pigeon = 0; pigeon < pigeons; pigeon++)
            {
                occupants.Add(Reference($"H{pigeon}_{hole}"));
            }

            axioms.Add(new OwlDisjointClassesAxiom(occupants) { Origin = Origin($"hole{hole}") });
        }

        return new ReasoningModule(axioms, Violations: []);
    }

    /// <summary>Generates one in-scope module: one to three TBox axioms biased toward disjunction-heavy inclusions, with occasional global inclusions, cycles, disjointness, and role-hierarchy pairs, plus zero to two asserted individuals and zero to three asserted role edges among up to three individuals — self-edges and cycles included.</summary>
    /// <param name="state">The xorshift state.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule GenerateModule(ref ulong state)
    {
        List<OwlAxiom> axioms = [];
        int axiomCount = 1 + (int)(Next(ref state) % 3);
        for(int i = 0; i < axiomCount; i++)
        {
            switch((int)(Next(ref state) % 8))
            {
                case 0 or 1 or 2:
                {
                    axioms.Add(new OwlSubClassOfAxiom(Reference(NextClass(ref state)), GenerateExpression(ref state, depth: 2))
                    {
                        Origin = Origin($"sub{i}"),
                    });

                    break;
                }

                case 3:
                {
                    axioms.Add(new OwlSubClassOfAxiom(Reference(Thing, qualified: true), GenerateExpression(ref state, depth: 2))
                    {
                        Origin = Origin($"global{i}"),
                    });

                    break;
                }

                case 4:
                {
                    string cyclic = NextClass(ref state);
                    axioms.Add(new OwlSubClassOfAxiom(
                        Reference(cyclic),
                        new OwlObjectSomeValuesFrom(Property(NextRole(ref state)), Reference(cyclic)))
                    {
                        Origin = Origin($"cycle{i}"),
                    });

                    break;
                }

                case 5:
                {
                    string first = NextClass(ref state);
                    string second = NextClass(ref state);
                    if(first == second)
                    {
                        second = SweepClasses[(Array.IndexOf(SweepClasses, first) + 1) % SweepClasses.Length];
                    }

                    axioms.Add(new OwlDisjointClassesAxiom([Reference(first), Reference(second)]) { Origin = Origin($"disjoint{i}") });

                    break;
                }

                default:
                {
                    axioms.Add(new OwlSubObjectPropertyOfAxiom(Property("s"), Property("r0")) { Origin = Origin($"hierarchy{i}") });

                    break;
                }
            }
        }

        int individualCount = (int)(Next(ref state) % 3);
        for(int individual = 0; individual < individualCount; individual++)
        {
            int assertionCount = 1 + (int)(Next(ref state) % 3);
            for(int assertion = 0; assertion < assertionCount; assertion++)
            {
                axioms.Add(new OwlClassAssertionAxiom(GenerateExpression(ref state, depth: 2), Named($"i{individual}"))
                {
                    Origin = Origin($"assert{individual}_{assertion}"),
                });
            }
        }

        int edgeCount = (int)(Next(ref state) % 4);
        for(int edge = 0; edge < edgeCount; edge++)
        {
            string from = $"i{Next(ref state) % 3}";
            string to = $"i{Next(ref state) % 3}";
            axioms.Add(Edge(from, NextRole(ref state), to, $"edge{edge}"));
        }

        return new ReasoningModule(axioms, Violations: []);
    }

    /// <summary>Generates a deterministic class expression with a disjunction-heavy bias: unions and intersections of two operands, complements, and existential and universal restrictions down to the depth budget.</summary>
    /// <param name="state">The xorshift state.</param>
    /// <param name="depth">The remaining depth budget.</param>
    /// <returns>The expression.</returns>
    private static OwlClassExpression GenerateExpression(ref ulong state, int depth)
    {
        if(depth == 0)
        {
            return GenerateLeaf(ref state);
        }

        return (int)(Next(ref state) % 10) switch
        {
            0 or 1 => GenerateLeaf(ref state),
            2 or 3 or 4 => new OwlObjectUnionOf([GenerateExpression(ref state, depth - 1), GenerateExpression(ref state, depth - 1)]),
            5 => new OwlObjectIntersectionOf([GenerateExpression(ref state, depth - 1), GenerateExpression(ref state, depth - 1)]),
            6 => new OwlObjectComplementOf(GenerateExpression(ref state, depth - 1)),
            7 or 8 => new OwlObjectSomeValuesFrom(Property(NextRole(ref state)), GenerateExpression(ref state, depth - 1)),
            _ => new OwlObjectAllValuesFrom(Property(NextRole(ref state)), GenerateExpression(ref state, depth - 1)),
        };
    }

    /// <summary>Generates a leaf: a named class or its complement.</summary>
    /// <param name="state">The xorshift state.</param>
    /// <returns>The leaf expression.</returns>
    private static OwlClassExpression GenerateLeaf(ref ulong state)
    {
        OwlClassReference reference = Reference(NextClass(ref state));

        return Next(ref state) % 2 == 0 ? reference : new OwlObjectComplementOf(reference);
    }

    /// <summary>The next class name of the sweep signature.</summary>
    /// <param name="state">The xorshift state.</param>
    /// <returns>The class name.</returns>
    private static string NextClass(ref ulong state)
    {
        return SweepClasses[(int)(Next(ref state) % (uint)SweepClasses.Length)];
    }

    /// <summary>The next role name of the sweep signature.</summary>
    /// <param name="state">The xorshift state.</param>
    /// <returns>The role name.</returns>
    private static string NextRole(ref ulong state)
    {
        return SweepRoles[(int)(Next(ref state) % (uint)SweepRoles.Length)];
    }

    /// <summary>Decides the module through the SAT-backed engine under the default search mode.</summary>
    /// <param name="module">The module.</param>
    /// <returns>The verdict.</returns>
    private ModuleVerdict DecideSat(ReasoningModule module)
    {
        return SatTableauModuleReasoner.DecideConsistency(module, cancellationToken: TestContext.CancellationToken);
    }

    /// <summary>Builds a module over the axioms with no violations attached.</summary>
    /// <param name="axioms">The module axioms.</param>
    /// <returns>The module.</returns>
    private static ReasoningModule Module(params OwlAxiom[] axioms)
    {
        return new ReasoningModule([.. axioms], Violations: []);
    }

    /// <summary>A class reference: a local name under the example namespace, or a full IRI when qualified.</summary>
    /// <param name="name">The local name, or the full IRI when <paramref name="qualified"/>.</param>
    /// <param name="qualified">Whether <paramref name="name"/> is already a full IRI.</param>
    /// <returns>The reference.</returns>
    private static OwlClassReference Reference(string name, bool qualified = false)
    {
        return new OwlClassReference(new NamedNode(Utf8Strings.From(qualified ? name : Example + name)));
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

    /// <summary>An asserted role edge between two individuals in the example namespace.</summary>
    /// <param name="from">The source individual's local name.</param>
    /// <param name="role">The role's local name.</param>
    /// <param name="to">The target individual's local name.</param>
    /// <param name="marker">The origin marker.</param>
    /// <returns>The assertion axiom.</returns>
    private static OwlObjectPropertyAssertionAxiom Edge(string from, string role, string to, string marker)
    {
        return new OwlObjectPropertyAssertionAxiom(Named(from), Named(role), Named(to)) { Origin = Origin(marker) };
    }

    /// <summary>A distinct origin quad for the marker name.</summary>
    /// <param name="marker">The distinguishing marker.</param>
    /// <returns>The origin quad.</returns>
    private static Quad Origin(string marker)
    {
        return new Quad(Named(marker), Named("p"), Named("o"), Graph: null);
    }

    /// <summary>The next value of the deterministic xorshift sequence.</summary>
    /// <param name="state">The generator state.</param>
    /// <returns>The next value.</returns>
    private static ulong Next(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;

        return state;
    }
}
