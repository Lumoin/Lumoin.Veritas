using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sat;
using Lumoin.Veritas.Owl.Reasoning;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Tests for <see cref="ConceptCnf"/>: ⊤/⊥ simplification in every
/// structural position, the top-level assertion shortcut, subformula-table
/// dedup, opaque restriction atoms, modal-atom registration, encoding after
/// caller-appended clauses, block replication arithmetic, and a truth-table
/// differential sweep against recursive negation-normal-form evaluation.
/// </summary>
[TestClass]
internal sealed class ConceptCnfTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The IRI prefix the test atoms and roles live under.</summary>
    private const string Example = "http://example.org/";

    /// <summary>The valuation bit the first restriction leaf of the sweep reads.</summary>
    private const int ExistsLeafBit = 4;

    /// <summary>The valuation bit the second restriction leaf of the sweep reads.</summary>
    private const int ForAllLeafBit = 5;

    /// <summary>The atoms of the differential sweep; atom <c>i</c> reads valuation bit <c>i</c>.</summary>
    private static readonly AlcAtom[] SweepAtoms = [Atom("A0"), Atom("A1"), Atom("A2"), Atom("A3")];

    /// <summary>⊤ and ⊥ as whole concepts encode to the pinned constant literals, and assuming them decides accordingly.</summary>
    [TestMethod]
    public void TopAndBottomAsWholeConcepts()
    {
        ConceptCnf cnf = new();

        Assert.AreEqual(cnf.TrueLiteral, cnf.GetLiteral(AlcTop.Instance));
        Assert.AreEqual(cnf.FalseLiteral, cnf.GetLiteral(AlcBottom.Instance));
        Assert.IsTrue(Solve(cnf, cnf.TrueLiteral).IsSatisfiable);
        Assert.IsFalse(Solve(cnf, cnf.FalseLiteral).IsSatisfiable);
    }

    /// <summary>⊤ drops from a conjunction and collapses a disjunction to true.</summary>
    [TestMethod]
    public void TopSimplifiesInEveryPosition()
    {
        ConceptCnf cnf = new();
        SatLiteral atom = cnf.GetLiteral(Atom("A"));

        Assert.AreEqual(atom, cnf.GetLiteral(new AlcAnd([Atom("A"), AlcTop.Instance])));
        Assert.AreEqual(cnf.TrueLiteral, cnf.GetLiteral(new AlcOr([Atom("A"), AlcTop.Instance])));
        Assert.AreEqual(cnf.TrueLiteral, cnf.GetLiteral(new AlcAnd([AlcTop.Instance, AlcTop.Instance])));
    }

    /// <summary>⊥ collapses a conjunction to false and drops from a disjunction.</summary>
    [TestMethod]
    public void BottomSimplifiesInEveryPosition()
    {
        ConceptCnf cnf = new();
        SatLiteral atom = cnf.GetLiteral(Atom("A"));

        Assert.AreEqual(cnf.FalseLiteral, cnf.GetLiteral(new AlcAnd([Atom("A"), AlcBottom.Instance])));
        Assert.AreEqual(atom, cnf.GetLiteral(new AlcOr([Atom("A"), AlcBottom.Instance])));
        Assert.AreEqual(cnf.FalseLiteral, cnf.GetLiteral(new AlcOr([AlcBottom.Instance, AlcBottom.Instance])));
    }

    /// <summary>Constant collapse composes through nesting, and empty connectives encode as their neutral constants.</summary>
    [TestMethod]
    public void NestedAndEmptyConnectivesCollapse()
    {
        ConceptCnf cnf = new();

        Assert.AreEqual(cnf.FalseLiteral, cnf.GetLiteral(new AlcAnd([new AlcOr([AlcBottom.Instance, AlcBottom.Instance]), Atom("A")])));
        Assert.AreEqual(cnf.TrueLiteral, cnf.GetLiteral(new AlcOr([new AlcAnd([AlcTop.Instance, AlcTop.Instance]), Atom("A")])));
        Assert.AreEqual(cnf.TrueLiteral, cnf.GetLiteral(new AlcAnd([])));
        Assert.AreEqual(cnf.FalseLiteral, cnf.GetLiteral(new AlcOr([])));
    }

    /// <summary>A top-level atom or negated atom asserts as a unit clause, and the opposite assumption contradicts it.</summary>
    [TestMethod]
    public void TopLevelAtomsAssertAsUnits()
    {
        ConceptCnf cnf = new();
        cnf.AssertFact(Atom("A"));

        Assert.AreEqual(2, cnf.VariableCount, "Only the reserved constant and the atom allocate.");
        Assert.HasCount(2, cnf.Clauses, "Only the pinned constant unit and the asserted unit exist.");
        Assert.IsTrue(Solve(cnf).IsSatisfiable);
        Assert.IsFalse(Solve(cnf, cnf.GetLiteral(Not("A"))).IsSatisfiable);

        cnf.AssertFact(Not("B"));

        Assert.AreEqual(3, cnf.VariableCount);
        Assert.IsTrue(Solve(cnf).IsSatisfiable);
        Assert.IsFalse(Solve(cnf, cnf.GetLiteral(Atom("B"))).IsSatisfiable);
    }

    /// <summary>A top-level disjunction asserts as one clause of its disjunct literals, with no auxiliary variable.</summary>
    [TestMethod]
    public void TopLevelDisjunctionNeedsNoAuxiliaryVariable()
    {
        ConceptCnf cnf = new();
        cnf.AssertFact(new AlcOr([Atom("A"), Atom("B")]));

        Assert.AreEqual(3, cnf.VariableCount, "Only the reserved constant and the two atoms allocate.");
        Assert.HasCount(2, cnf.Clauses, "Only the pinned constant unit and the disjunction clause exist.");
        Assert.HasCount(2, cnf.Clauses[1]);
        Assert.IsFalse(Solve(cnf, cnf.GetLiteral(Not("A")), cnf.GetLiteral(Not("B"))).IsSatisfiable);
        Assert.IsTrue(Solve(cnf, cnf.GetLiteral(Not("A"))).IsSatisfiable);
    }

    /// <summary>A top-level conjunction of disjunctions recurses into its conjuncts: one clause per disjunction, no auxiliary variables.</summary>
    [TestMethod]
    public void TopLevelConjunctionRecursesIntoConjuncts()
    {
        ConceptCnf cnf = new();
        cnf.AssertFact(new AlcAnd([new AlcOr([Atom("A"), Atom("B")]), new AlcOr([Atom("C"), Not("D")])]));

        Assert.AreEqual(5, cnf.VariableCount, "Only the reserved constant and the four atoms allocate.");
        Assert.HasCount(3, cnf.Clauses, "Only the pinned constant unit and the two disjunction clauses exist.");
        Assert.IsTrue(Solve(cnf).IsSatisfiable);
    }

    /// <summary>Asserting ⊤ emits nothing; asserting ⊥ emits the empty clause and the formula goes unsatisfiable.</summary>
    [TestMethod]
    public void TopLevelConstantsAssertStructurally()
    {
        ConceptCnf cnf = new();
        cnf.AssertFact(AlcTop.Instance);

        Assert.HasCount(1, cnf.Clauses, "Asserting ⊤ adds no clause beyond the pinned constant unit.");
        Assert.IsTrue(Solve(cnf).IsSatisfiable);

        cnf.AssertFact(AlcBottom.Instance);

        Assert.HasCount(2, cnf.Clauses);
        Assert.IsEmpty(cnf.Clauses[1], "Asserting ⊥ emits the empty clause.");
        Assert.IsFalse(Solve(cnf).IsSatisfiable);
    }

    /// <summary>Duplicate subformulas across separate encode calls map to the same variable and emit no second definition.</summary>
    [TestMethod]
    public void DuplicateSubformulasShareTheirVariable()
    {
        ConceptCnf cnf = new();
        SatLiteral first = cnf.GetLiteral(new AlcAnd([Atom("A"), Atom("B")]));
        int variables = cnf.VariableCount;
        int clauseCount = cnf.Clauses.Count;

        Assert.AreEqual(first, cnf.GetLiteral(new AlcAnd([Atom("A"), Atom("B")])));
        Assert.AreEqual(variables, cnf.VariableCount, "Re-encoding allocates nothing.");
        Assert.HasCount(clauseCount, cnf.Clauses, "Re-encoding emits nothing.");

        //The disjunction over the same atoms reuses both atom variables, so
        //only its own variable is new.
        cnf.GetLiteral(new AlcOr([Atom("A"), Atom("B")]));

        Assert.AreEqual(variables + 1, cnf.VariableCount);
    }

    /// <summary>Encoding keeps working after callers append clauses of their own: a new concept takes the next dense id and the appended clause binds.</summary>
    [TestMethod]
    public void ConceptsEncodeAfterCallerAppendedClauses()
    {
        ConceptCnf cnf = new();
        SatLiteral exists = cnf.GetLiteral(Exists("r", Atom("C")));
        SatLiteral forAll = cnf.GetLiteral(ForAll("r", Atom("D")));
        cnf.Append([exists.Negated(), forAll.Negated()]);

        int before = cnf.VariableCount;
        SatLiteral late = cnf.GetLiteral(Atom("E"));

        Assert.AreEqual(before, late.Variable, "The late concept takes the next dense id.");
        Assert.AreEqual(before + 1, cnf.VariableCount);
        Assert.IsFalse(Solve(cnf, exists, forAll).IsSatisfiable, "The appended clause forbids the pair.");
        Assert.IsTrue(Solve(cnf, exists, late).IsSatisfiable);
    }

    /// <summary>Restrictions are opaque atoms: the same role with different fillers differs, ∃ and ∀ over the same filler differ, and the same restriction re-encodes to the same variable.</summary>
    [TestMethod]
    public void RestrictionsAreOpaqueAndDistinct()
    {
        ConceptCnf cnf = new();
        SatLiteral existsA = cnf.GetLiteral(Exists("r", Atom("A")));
        SatLiteral existsB = cnf.GetLiteral(Exists("r", Atom("B")));
        SatLiteral forAllA = cnf.GetLiteral(ForAll("r", Atom("A")));

        Assert.AreNotEqual(existsA.Variable, existsB.Variable);
        Assert.AreNotEqual(existsA.Variable, forAllA.Variable);
        Assert.AreNotEqual(existsB.Variable, forAllA.Variable);
        Assert.AreEqual(existsA, cnf.GetLiteral(Exists("r", Atom("A"))));
    }

    /// <summary>A restriction's filler does not encode — one variable for the restriction, none for the filler's parts — and constant fillers do not collapse the restriction.</summary>
    [TestMethod]
    public void FillersStayUnencoded()
    {
        ConceptCnf cnf = new();
        cnf.GetLiteral(Exists("r", new AlcAnd([Atom("A"), Atom("B")])));

        Assert.AreEqual(2, cnf.VariableCount, "Only the reserved constant and the restriction allocate.");
        Assert.HasCount(1, cnf.Clauses, "Only the pinned constant unit exists.");

        SatLiteral existsBottom = cnf.GetLiteral(Exists("r", AlcBottom.Instance));
        SatLiteral forAllTop = cnf.GetLiteral(ForAll("r", AlcTop.Instance));

        Assert.AreNotEqual(cnf.FalseLiteral, existsBottom, "∃r.⊥ stays an opaque atom.");
        Assert.AreNotEqual(cnf.TrueLiteral, forAllTop, "∀r.⊤ stays an opaque atom.");
        Assert.IsTrue(Solve(cnf, existsBottom, forAllTop.Negated()).IsSatisfiable);
    }

    /// <summary>Modal atoms register once each in allocation order with their concepts; atoms and boolean connectives register nothing.</summary>
    [TestMethod]
    public void ModalAtomsRegisterInAllocationOrder()
    {
        ConceptCnf cnf = new();
        SatLiteral exists = cnf.GetLiteral(Exists("r", Atom("A")));
        cnf.GetLiteral(new AlcAnd([Atom("B"), Atom("C")]));
        SatLiteral forAll = cnf.GetLiteral(ForAll("r", Atom("A")));
        cnf.GetLiteral(Exists("r", Atom("A")));

        Assert.HasCount(2, cnf.ModalAtoms, "Each restriction registers once; atoms and connectives register nothing.");
        Assert.AreEqual(exists.Variable, cnf.ModalAtoms[0].Variable);
        Assert.AreEqual(Exists("r", Atom("A")), cnf.ModalAtoms[0].Concept);
        Assert.AreEqual(forAll.Variable, cnf.ModalAtoms[1].Variable);
        Assert.AreEqual(ForAll("r", Atom("A")), cnf.ModalAtoms[1].Concept);
        Assert.IsLessThan(cnf.ModalAtoms[1].Variable, cnf.ModalAtoms[0].Variable, "Registration order ascends by variable.");
    }

    /// <summary>Asserting a concept as a fact carries the concept's own satisfiability: a satisfiable conjunction stays satisfiable, a contradictory one goes unsatisfiable.</summary>
    [TestMethod]
    public void AssertedFactsCarryTheConceptsSatisfiability()
    {
        ConceptCnf satisfiable = new();
        satisfiable.AssertFact(new AlcAnd([Atom("A"), new AlcOr([Not("A"), Atom("B")])]));

        Assert.IsTrue(Solve(satisfiable).IsSatisfiable);

        ConceptCnf contradictory = new();
        contradictory.AssertFact(new AlcAnd([Atom("A"), Not("A")]));

        Assert.IsFalse(Solve(contradictory).IsSatisfiable);
    }

    /// <summary>Block replication arithmetic: block zero is the identity, offsets stride by the width, and clause instantiation preserves polarity.</summary>
    [TestMethod]
    public void BlockReplicationOffsetsAndInstantiation()
    {
        Assert.AreEqual(3, ConceptCnf.BlockVariable(blockIndex: 0, variable: 3, blockWidth: 10));
        Assert.AreEqual(23, ConceptCnf.BlockVariable(blockIndex: 2, variable: 3, blockWidth: 10));

        IReadOnlyList<SatLiteral> template = [new SatLiteral(0, true), new SatLiteral(4, false)];
        IReadOnlyList<SatLiteral> instantiated = ConceptCnf.InstantiateInBlock(template, blockIndex: 3, blockWidth: 5);

        Assert.HasCount(2, instantiated);
        Assert.AreEqual(new SatLiteral(15, true), instantiated[0]);
        Assert.AreEqual(new SatLiteral(19, false), instantiated[1]);

        IReadOnlyList<SatLiteral> identity = ConceptCnf.InstantiateInBlock(template, blockIndex: 0, blockWidth: 5);

        Assert.AreEqual(template[0], identity[0]);
        Assert.AreEqual(template[1], identity[1]);
    }

    /// <summary>
    /// The encoding agrees with recursive negation-normal-form evaluation
    /// over a deterministic concept sweep: assuming the concept's literal is
    /// satisfiable exactly when some valuation of its atoms and opaque
    /// restriction leaves makes the concept true, and asserting the concept
    /// as a fact decides the same. Both verdicts occur.
    /// </summary>
    [TestMethod]
    public void TruthTableDifferentialOverGeneratedConcepts()
    {
        //A deterministic xorshift drives the generation; no entropy APIs.
        ulong state = 0x2545F4914F6CDD1DUL;
        AlcConcept[] leaves = BuildLeafPool();
        int satisfiableSeen = 0;
        int unsatisfiableSeen = 0;

        for(int round = 0; round < 300; round++)
        {
            AlcConcept concept = Generate(ref state, depth: 1 + (int)(Next(ref state) % 3), leaves);
            bool expected = ExistsSatisfyingValuation(concept);

            ConceptCnf assumed = new();
            SatLiteral literal = assumed.GetLiteral(concept);

            Assert.AreEqual(expected, Solve(assumed, literal).IsSatisfiable, $"Round {round}: the assumed literal disagrees with evaluation.");

            ConceptCnf asserted = new();
            asserted.AssertFact(concept);

            Assert.AreEqual(expected, Solve(asserted).IsSatisfiable, $"Round {round}: the asserted fact disagrees with evaluation.");

            if(expected)
            {
                satisfiableSeen++;
            }
            else
            {
                unsatisfiableSeen++;
            }
        }

        Assert.IsGreaterThan(20, satisfiableSeen, "The sweep covers satisfiable concepts.");
        Assert.IsGreaterThan(20, unsatisfiableSeen, "The sweep covers unsatisfiable concepts.");
    }

    /// <summary>Solves the encoding under the given assumption literals.</summary>
    /// <param name="cnf">The encoding.</param>
    /// <param name="assumptions">The assumption literals.</param>
    /// <returns>The verdict.</returns>
    private SatVerdict Solve(ConceptCnf cnf, params SatLiteral[] assumptions)
    {
        return SatSolver.SolveUnderAssumptions(cnf.Clauses, cnf.VariableCount, assumptions, cancellationToken: TestContext.CancellationToken);
    }

    /// <summary>A named atom under the example namespace.</summary>
    /// <param name="name">The local name.</param>
    /// <returns>The atom.</returns>
    private static AlcAtom Atom(string name)
    {
        return new AlcAtom(Utf8Strings.From(Example + name));
    }

    /// <summary>The negation of a named atom.</summary>
    /// <param name="name">The local name.</param>
    /// <returns>The negated atom.</returns>
    private static AlcNot Not(string name)
    {
        return new AlcNot(Atom(name));
    }

    /// <summary>An existential restriction over a named role.</summary>
    /// <param name="role">The role's local name.</param>
    /// <param name="filler">The filler concept.</param>
    /// <returns>The restriction.</returns>
    private static AlcExists Exists(string role, AlcConcept filler)
    {
        return new AlcExists(AlcRole.Forward(Utf8Strings.From(Example + role)), filler);
    }

    /// <summary>A universal restriction over a named role.</summary>
    /// <param name="role">The role's local name.</param>
    /// <param name="filler">The filler concept.</param>
    /// <returns>The restriction.</returns>
    private static AlcForAll ForAll(string role, AlcConcept filler)
    {
        return new AlcForAll(AlcRole.Forward(Utf8Strings.From(Example + role)), filler);
    }

    /// <summary>
    /// The sweep's leaf pool: the four atoms, their negations, ⊤, ⊥, and
    /// two restriction leaves treated as opaque propositions.
    /// </summary>
    /// <returns>The leaves.</returns>
    private static AlcConcept[] BuildLeafPool()
    {
        return
        [
            SweepAtoms[0],
            SweepAtoms[1],
            SweepAtoms[2],
            SweepAtoms[3],
            new AlcNot(SweepAtoms[0]),
            new AlcNot(SweepAtoms[1]),
            new AlcNot(SweepAtoms[2]),
            new AlcNot(SweepAtoms[3]),
            AlcTop.Instance,
            AlcBottom.Instance,
            Exists("r", Atom("M0")),
            ForAll("s", Atom("M1")),
        ];
    }

    /// <summary>Generates a deterministic concept: leaves from the pool, conjunctions and disjunctions of two or three operands down to the depth budget.</summary>
    /// <param name="state">The xorshift state.</param>
    /// <param name="depth">The remaining depth budget.</param>
    /// <param name="leaves">The leaf pool.</param>
    /// <returns>The concept.</returns>
    private static AlcConcept Generate(ref ulong state, int depth, AlcConcept[] leaves)
    {
        if(depth == 0)
        {
            return leaves[(int)(Next(ref state) % (uint)leaves.Length)];
        }

        int kind = (int)(Next(ref state) % 5);
        if(kind == 0)
        {
            return leaves[(int)(Next(ref state) % (uint)leaves.Length)];
        }

        int width = 2 + (int)(Next(ref state) % 2);
        List<AlcConcept> operands = [];
        for(int i = 0; i < width; i++)
        {
            operands.Add(Generate(ref state, depth - 1, leaves));
        }

        return kind <= 2 ? new AlcAnd(operands) : new AlcOr(operands);
    }

    /// <summary>Whether any of the 64 valuations over the four atoms and two restriction leaves makes the concept true — the differential oracle.</summary>
    /// <param name="concept">The concept.</param>
    /// <returns><see langword="true"/> when a satisfying valuation exists.</returns>
    private static bool ExistsSatisfyingValuation(AlcConcept concept)
    {
        for(int valuation = 0; valuation < 1 << 6; valuation++)
        {
            if(Evaluate(concept, valuation))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The naive recursive negation-normal-form evaluation: atoms and restriction leaves read their valuation bits, connectives fold their operands.</summary>
    /// <param name="concept">The concept.</param>
    /// <param name="valuation">The valuation bits: atoms at their index, the restriction leaves at bits four and five.</param>
    /// <returns>The concept's truth under the valuation.</returns>
    private static bool Evaluate(AlcConcept concept, int valuation)
    {
        return concept switch
        {
            AlcTop => true,
            AlcBottom => false,
            AlcAtom atom => (valuation & (1 << AtomBit(atom))) != 0,
            AlcNot negation => (valuation & (1 << AtomBit(negation.Operand))) == 0,
            AlcExists => (valuation & (1 << ExistsLeafBit)) != 0,
            AlcForAll => (valuation & (1 << ForAllLeafBit)) != 0,
            AlcAnd and => EvaluateAll(and.Operands, valuation),
            AlcOr or => EvaluateAny(or.Operands, valuation),
            _ => throw new AssertFailedException("The sweep generated an unexpected concept kind.")
        };
    }

    /// <summary>Whether every operand evaluates true under the valuation.</summary>
    /// <param name="operands">The conjuncts.</param>
    /// <param name="valuation">The valuation bits.</param>
    /// <returns><see langword="true"/> when all hold.</returns>
    private static bool EvaluateAll(IReadOnlyList<AlcConcept> operands, int valuation)
    {
        foreach(AlcConcept operand in operands)
        {
            if(!Evaluate(operand, valuation))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether any operand evaluates true under the valuation.</summary>
    /// <param name="operands">The disjuncts.</param>
    /// <param name="valuation">The valuation bits.</param>
    /// <returns><see langword="true"/> when one holds.</returns>
    private static bool EvaluateAny(IReadOnlyList<AlcConcept> operands, int valuation)
    {
        foreach(AlcConcept operand in operands)
        {
            if(Evaluate(operand, valuation))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The sweep atom's valuation bit.</summary>
    /// <param name="atom">The atom; it must come from the sweep pool.</param>
    /// <returns>The bit index.</returns>
    private static int AtomBit(AlcAtom atom)
    {
        for(int i = 0; i < SweepAtoms.Length; i++)
        {
            if(SweepAtoms[i].Equals(atom))
            {
                return i;
            }
        }

        throw new AssertFailedException("The sweep generated an atom outside its pool.");
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
