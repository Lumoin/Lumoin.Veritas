using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sat;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// Tests for <see cref="SatTableauModuleReasoner"/>: a deterministic
/// differential sweep against the snapshot engine over generated ALC(H)
/// modules, the tableau boundary matrix (existentials into ⊥, ∀-only
/// labels, blocking cycles, shared successor sets, per-individual worlds,
/// the asserted-edge scope guard, fragment honesty), the propagation-only
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
    /// and cyclic inclusions, zero to two asserted individuals — and both
    /// verdicts occur.
    /// </summary>
    [TestMethod]
    public void DifferentialSweepAgreesWithTheSnapshotEngine()
    {
        //A deterministic xorshift drives the generation; no entropy APIs.
        ulong state = 0xB5297A4D3F84D5B5UL;
        int consistentSeen = 0;
        int inconsistentSeen = 0;

        for(int round = 0; round < 300; round++)
        {
            ReasoningModule module = GenerateModule(ref state);
            bool snapshot = AlcModuleReasoner.DecideConsistency(module, TestContext.CancellationToken).IsConsistent;
            bool satBacked = SatTableauModuleReasoner.DecideConsistency(module, cancellationToken: TestContext.CancellationToken).IsConsistent;

            Assert.AreEqual(snapshot, satBacked, $"Round {round}: the engines disagree on consistency.");

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

    /// <summary>A module with asserted role edges is outside this engine's scope and throws.</summary>
    [TestMethod]
    public void AssertedRoleEdgesAreNotSupported()
    {
        OwlObjectPropertyAssertionAxiom edge = new(Named("a"), Named("r"), Named("b")) { Origin = Origin("edge") };

        Assert.ThrowsExactly<NotSupportedException>(
            () => DecideSat(Module(edge)));
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
        CollectionAssert.AreEqual(snapshotNames, satBackedNames, "The remainder naming is identical.");
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

    /// <summary>Generates one in-scope module: one to three TBox axioms biased toward disjunction-heavy inclusions, with occasional global inclusions, cycles, disjointness, and role-hierarchy pairs, plus zero to two asserted individuals.</summary>
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
        return new OwlClassReference(new NamedNode(Utf8String.FromString(qualified ? name : Example + name)));
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
