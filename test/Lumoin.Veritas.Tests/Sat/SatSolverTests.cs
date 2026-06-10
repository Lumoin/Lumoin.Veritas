using System.Collections.Generic;
using Lumoin.Veritas.Core.Sat;

namespace Lumoin.Veritas.Tests.Sat;

/// <summary>
/// Tests for <see cref="SatSolver"/>: fixed verdicts, assignment validity,
/// the pigeonhole contradiction, and a differential sweep against
/// exhaustive enumeration over deterministically generated formulas.
/// </summary>
[TestClass]
internal sealed class SatSolverTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>An empty formula is satisfiable; an empty clause is not.</summary>
    [TestMethod]
    public void EmptyFormulaAndEmptyClause()
    {
        Assert.IsTrue(SatSolver.Solve([], variableCount: 0, cancellationToken: TestContext.CancellationToken).IsSatisfiable);
        Assert.IsTrue(SatSolver.Solve([], variableCount: 3, cancellationToken: TestContext.CancellationToken).IsSatisfiable);
        Assert.IsFalse(SatSolver.Solve([[]], variableCount: 0, cancellationToken: TestContext.CancellationToken).IsSatisfiable);
    }

    /// <summary>Contradicting unit clauses are unsatisfiable; propagation finds it without branching.</summary>
    [TestMethod]
    public void ContradictingUnitsAreUnsatisfiable()
    {
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, IsPositive: true)],
            [new SatLiteral(0, IsPositive: false)],
        ];

        Assert.IsFalse(SatSolver.Solve(clauses, variableCount: 1, cancellationToken: TestContext.CancellationToken).IsSatisfiable);
    }

    /// <summary>A satisfying assignment satisfies every clause.</summary>
    [TestMethod]
    public void AssignmentSatisfiesTheFormula()
    {
        //(x0 | x1) & (~x0 | x2) & (~x1 | ~x2) & (x1 | x2).
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, true), new SatLiteral(1, true)],
            [new SatLiteral(0, false), new SatLiteral(2, true)],
            [new SatLiteral(1, false), new SatLiteral(2, false)],
            [new SatLiteral(1, true), new SatLiteral(2, true)],
        ];

        SatVerdict verdict = SatSolver.Solve(clauses, variableCount: 3, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(verdict.IsSatisfiable);
        Assert.IsNotNull(verdict.Assignment);
        Assert.IsTrue(Satisfies(clauses, verdict.Assignment));
    }

    /// <summary>Three pigeons into two holes is unsatisfiable — the branching path, not just propagation.</summary>
    [TestMethod]
    public void PigeonholeIsUnsatisfiable()
    {
        //Variable p*2 + h: pigeon p sits in hole h. Each pigeon somewhere;
        //no two pigeons share a hole.
        List<IReadOnlyList<SatLiteral>> clauses = [];
        for(int pigeon = 0; pigeon < 3; pigeon++)
        {
            clauses.Add([new SatLiteral(pigeon * 2, true), new SatLiteral((pigeon * 2) + 1, true)]);
        }

        for(int hole = 0; hole < 2; hole++)
        {
            for(int first = 0; first < 3; first++)
            {
                for(int second = first + 1; second < 3; second++)
                {
                    clauses.Add([new SatLiteral((first * 2) + hole, false), new SatLiteral((second * 2) + hole, false)]);
                }
            }
        }

        Assert.IsFalse(SatSolver.Solve(clauses, variableCount: 6, cancellationToken: TestContext.CancellationToken).IsSatisfiable);
    }

    /// <summary>The solver agrees with exhaustive enumeration over a deterministic formula sweep, and its satisfying assignments check out.</summary>
    [TestMethod]
    public void DifferentialAgainstExhaustiveEnumeration()
    {
        //A deterministic xorshift drives the generation; no entropy APIs.
        ulong state = 0x9E3779B97F4A7C15UL;
        int satisfiableSeen = 0;
        int unsatisfiableSeen = 0;

        for(int round = 0; round < 300; round++)
        {
            int variableCount = 1 + (int)(Next(ref state) % 9);
            int clauseCount = 1 + (int)(Next(ref state) % 18);
            List<IReadOnlyList<SatLiteral>> clauses = [];
            for(int i = 0; i < clauseCount; i++)
            {
                int width = 1 + (int)(Next(ref state) % 3);
                SatLiteral[] clause = new SatLiteral[width];
                for(int j = 0; j < width; j++)
                {
                    clause[j] = new SatLiteral((int)(Next(ref state) % (uint)variableCount), (Next(ref state) & 1) == 0);
                }

                clauses.Add(clause);
            }

            SatVerdict verdict = SatSolver.Solve(clauses, variableCount, cancellationToken: TestContext.CancellationToken);
            bool expected = ExistsSatisfyingAssignment(clauses, variableCount);

            Assert.AreEqual(expected, verdict.IsSatisfiable, $"Round {round}: solver and enumeration disagree.");
            if(verdict.IsSatisfiable)
            {
                satisfiableSeen++;
                Assert.IsNotNull(verdict.Assignment);
                Assert.IsTrue(Satisfies(clauses, verdict.Assignment), $"Round {round}: the returned assignment does not satisfy the formula.");
            }
            else
            {
                unsatisfiableSeen++;
            }
        }

        Assert.IsGreaterThan(20, satisfiableSeen, "The sweep covers satisfiable formulas.");
        Assert.IsGreaterThan(20, unsatisfiableSeen, "The sweep covers unsatisfiable formulas.");
    }

    /// <summary>Assumptions consistent with a model are satisfiable and the model honours them; contradicting assumptions are not.</summary>
    [TestMethod]
    public void AssumptionsFixLiteralsBeforeBranching()
    {
        //(x0 | x1) & (~x0 | x2).
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, true), new SatLiteral(1, true)],
            [new SatLiteral(0, false), new SatLiteral(2, true)],
        ];

        //Assuming x0 forces x2 by propagation; the model must honour both.
        SatVerdict assumed = SatSolver.SolveUnderAssumptions(clauses, variableCount: 3, [new SatLiteral(0, true)], cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(assumed.IsSatisfiable);
        Assert.IsNotNull(assumed.Assignment);
        Assert.IsTrue(assumed.Assignment[0]);
        Assert.IsTrue(assumed.Assignment[2]);
        Assert.IsTrue(Satisfies(clauses, assumed.Assignment));

        //Assumptions that contradict each other are unsatisfiable under them.
        SatVerdict contradicting = SatSolver.SolveUnderAssumptions(clauses, variableCount: 3, [new SatLiteral(1, true), new SatLiteral(1, false)], cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(contradicting.IsSatisfiable);
    }

    /// <summary>An assumption that drives propagation into a conflict is unsatisfiable under assumptions — the conflict traces to the prefix.</summary>
    [TestMethod]
    public void AssumptionConflictThroughPropagationIsUnsatisfiable()
    {
        //(~x0 | x1) & (~x0 | ~x1): satisfiable on its own (x0 false), but
        //assuming x0 forces x1 both ways.
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, false), new SatLiteral(1, true)],
            [new SatLiteral(0, false), new SatLiteral(1, false)],
        ];

        Assert.IsTrue(SatSolver.Solve(clauses, variableCount: 2, cancellationToken: TestContext.CancellationToken).IsSatisfiable);
        Assert.IsFalse(SatSolver.SolveUnderAssumptions(clauses, variableCount: 2, [new SatLiteral(0, true)], cancellationToken: TestContext.CancellationToken).IsSatisfiable);
    }

    /// <summary>
    /// Solving under assumptions agrees with solving the formula extended by
    /// the assumptions as unit clauses, over a deterministic sweep; every
    /// satisfying model honours the formula and all assumptions.
    /// </summary>
    [TestMethod]
    public void AssumptionsDifferentialAgainstUnitClauseEncoding()
    {
        //A deterministic xorshift drives the generation; no entropy APIs.
        ulong state = 0xD1B54A32D192ED03UL;
        int satisfiableSeen = 0;
        int unsatisfiableSeen = 0;

        for(int round = 0; round < 300; round++)
        {
            int variableCount = 1 + (int)(Next(ref state) % 9);
            int clauseCount = 1 + (int)(Next(ref state) % 18);
            List<IReadOnlyList<SatLiteral>> clauses = [];
            for(int i = 0; i < clauseCount; i++)
            {
                int width = 1 + (int)(Next(ref state) % 3);
                SatLiteral[] clause = new SatLiteral[width];
                for(int j = 0; j < width; j++)
                {
                    clause[j] = new SatLiteral((int)(Next(ref state) % (uint)variableCount), (Next(ref state) & 1) == 0);
                }

                clauses.Add(clause);
            }

            int assumptionCount = (int)(Next(ref state) % 4);
            List<SatLiteral> assumptions = [];
            for(int i = 0; i < assumptionCount; i++)
            {
                assumptions.Add(new SatLiteral((int)(Next(ref state) % (uint)variableCount), (Next(ref state) & 1) == 0));
            }

            //The oracle: the assumptions as unit clauses appended to the
            //formula must give the same satisfiability.
            List<IReadOnlyList<SatLiteral>> extended = [.. clauses];
            foreach(SatLiteral assumption in assumptions)
            {
                extended.Add([assumption]);
            }

            SatVerdict assumed = SatSolver.SolveUnderAssumptions(clauses, variableCount, assumptions, cancellationToken: TestContext.CancellationToken);
            SatVerdict reference = SatSolver.Solve(extended, variableCount, cancellationToken: TestContext.CancellationToken);

            Assert.AreEqual(reference.IsSatisfiable, assumed.IsSatisfiable, $"Round {round}: assumptions and unit-clause encoding disagree.");
            if(assumed.IsSatisfiable)
            {
                satisfiableSeen++;
                Assert.IsNotNull(assumed.Assignment);
                Assert.IsTrue(Satisfies(clauses, assumed.Assignment), $"Round {round}: the model does not satisfy the formula.");
                foreach(SatLiteral assumption in assumptions)
                {
                    Assert.AreEqual(assumption.IsPositive, assumed.Assignment[assumption.Variable], $"Round {round}: the model does not honour an assumption.");
                }
            }
            else
            {
                unsatisfiableSeen++;
            }
        }

        Assert.IsGreaterThan(20, satisfiableSeen, "The sweep covers satisfiable-under-assumptions formulas.");
        Assert.IsGreaterThan(20, unsatisfiableSeen, "The sweep covers unsatisfiable-under-assumptions formulas.");
    }

    /// <summary>Whether the assignment satisfies every clause.</summary>
    /// <param name="clauses">The formula.</param>
    /// <param name="assignment">The assignment.</param>
    /// <returns><see langword="true"/> when every clause has a true literal.</returns>
    private static bool Satisfies(List<IReadOnlyList<SatLiteral>> clauses, IReadOnlyList<bool> assignment)
    {
        foreach(IReadOnlyList<SatLiteral> clause in clauses)
        {
            bool satisfied = false;
            foreach(SatLiteral literal in clause)
            {
                if(assignment[literal.Variable] == literal.IsPositive)
                {
                    satisfied = true;

                    break;
                }
            }

            if(!satisfied)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Exhaustively enumerates the assignment space — the differential oracle.</summary>
    /// <param name="clauses">The formula.</param>
    /// <param name="variableCount">The variable count.</param>
    /// <returns><see langword="true"/> when any assignment satisfies the formula.</returns>
    private static bool ExistsSatisfyingAssignment(List<IReadOnlyList<SatLiteral>> clauses, int variableCount)
    {
        bool[] assignment = new bool[variableCount];
        for(long mask = 0; mask < 1L << variableCount; mask++)
        {
            for(int i = 0; i < variableCount; i++)
            {
                assignment[i] = (mask & (1L << i)) != 0;
            }

            if(Satisfies(clauses, assignment))
            {
                return true;
            }
        }

        return false;
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
