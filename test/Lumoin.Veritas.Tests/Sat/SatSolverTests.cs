using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sat;

namespace Lumoin.Veritas.Tests.Sat;

/// <summary>
/// Tests for <see cref="SatSolver"/>: fixed verdicts, assignment validity,
/// the pigeonhole contradiction, solving under assumptions, and differential
/// sweeps against exhaustive enumeration and the unit-clause encoding — each
/// sweep run in both search modes.
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

    /// <summary>The solver agrees with exhaustive enumeration over a deterministic formula sweep in both modes, and its satisfying assignments check out.</summary>
    /// <param name="mode">The search mode under test.</param>
    [TestMethod]
    [DataRow(SatSearchMode.PropagationOnly)]
    [DataRow(SatSearchMode.ConflictLearning)]
    public void DifferentialAgainstExhaustiveEnumeration(SatSearchMode mode)
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

            SatVerdict verdict = SatSolver.Solve(clauses, variableCount, mode: mode, cancellationToken: TestContext.CancellationToken);
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
    /// Conflict learning backjumps over an irrelevant decision: a conflict
    /// driven by the first decision must unwind past a later, unrelated one
    /// rather than only flip the topmost branch. The verdict and model still
    /// match the formula.
    /// </summary>
    [TestMethod]
    public void ConflictLearningBackjumpsOverIrrelevantDecision()
    {
        //x0 and x3 are independent; x1, x2 are pinned to x0. Branching x0=true
        //then x3=true reaches a conflict that traces only to x0, so the search
        //backjumps past the x3 decision to assert x0=false.
        //(~x0 | x1) & (~x0 | x2) & (~x1 | ~x2).
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, false), new SatLiteral(1, true)],
            [new SatLiteral(0, false), new SatLiteral(2, true)],
            [new SatLiteral(1, false), new SatLiteral(2, false)],
            [new SatLiteral(3, true), new SatLiteral(0, false)],
        ];

        SatVerdict learned = SatSolver.Solve(clauses, variableCount: 4, mode: SatSearchMode.ConflictLearning, cancellationToken: TestContext.CancellationToken);
        SatVerdict propagation = SatSolver.Solve(clauses, variableCount: 4, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(learned.IsSatisfiable);
        Assert.AreEqual(propagation.IsSatisfiable, learned.IsSatisfiable);
        Assert.IsNotNull(learned.Assignment);
        Assert.IsTrue(Satisfies(clauses, learned.Assignment));
    }

    /// <summary>
    /// Solving under assumptions agrees with solving the formula extended by
    /// the assumptions as unit clauses, over a deterministic sweep in both
    /// modes; every satisfying model honours the formula and all assumptions.
    /// </summary>
    /// <param name="mode">The search mode under test.</param>
    [TestMethod]
    [DataRow(SatSearchMode.PropagationOnly)]
    [DataRow(SatSearchMode.ConflictLearning)]
    public void AssumptionsDifferentialAgainstUnitClauseEncoding(SatSearchMode mode)
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

            SatVerdict assumed = SatSolver.SolveUnderAssumptions(clauses, variableCount, assumptions, mode: mode, cancellationToken: TestContext.CancellationToken);
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

    /// <summary>
    /// The failed-assumption core of an unsatisfiable-under-assumptions verdict
    /// is sound and a subset of the assumptions, over a deterministic sweep in
    /// both modes: re-solving while assuming only the core stays unsatisfiable,
    /// and every core literal was a supplied assumption.
    /// </summary>
    /// <param name="mode">The search mode under test.</param>
    [TestMethod]
    [DataRow(SatSearchMode.PropagationOnly)]
    [DataRow(SatSearchMode.ConflictLearning)]
    public void AssumptionCoreSoundnessSweep(SatSearchMode mode)
    {
        //A deterministic xorshift drives the generation; no entropy APIs.
        ulong state = 0xA0761D6478BD642FUL;
        int satisfiableSeen = 0;
        int unsatisfiableSeen = 0;

        for(int round = 0; round < 300; round++)
        {
            int variableCount = 1 + (int)(Next(ref state) % 7);
            int clauseCount = 1 + (int)(Next(ref state) % 14);
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

            //Bias toward several assumptions so the unsatisfiable-under-
            //assumptions arm fires often: the core is only exercised there.
            int assumptionCount = (int)(Next(ref state) % (uint)(variableCount + 1));
            List<SatLiteral> assumptions = [];
            for(int i = 0; i < assumptionCount; i++)
            {
                assumptions.Add(new SatLiteral((int)(Next(ref state) % (uint)variableCount), (Next(ref state) & 1) == 0));
            }

            SatVerdict verdict = SatSolver.SolveUnderAssumptions(clauses, variableCount, assumptions, mode: mode, cancellationToken: TestContext.CancellationToken);
            if(verdict.IsSatisfiable)
            {
                satisfiableSeen++;
                Assert.IsNull(verdict.AssumptionCore, $"Round {round}: a satisfiable verdict carries no core.");

                continue;
            }

            unsatisfiableSeen++;
            Assert.IsNotNull(verdict.AssumptionCore, $"Round {round}: an unsatisfiable-under-assumptions verdict carries a core.");

            //The core is a subset of the supplied assumptions.
            foreach(SatLiteral coreLiteral in verdict.AssumptionCore)
            {
                Assert.Contains(coreLiteral, assumptions, $"Round {round}: a core literal was not a supplied assumption.");
            }

            //Soundness: re-solving while assuming only the core is still
            //unsatisfiable.
            SatVerdict reSolved = SatSolver.SolveUnderAssumptions(clauses, variableCount, [.. verdict.AssumptionCore], mode: mode, cancellationToken: TestContext.CancellationToken);
            Assert.IsFalse(reSolved.IsSatisfiable, $"Round {round}: assuming only the core is no longer unsatisfiable — the core is unsound.");
        }

        TestContext.WriteLine($"{mode} | rounds {300} | satisfiable {satisfiableSeen} | unsatisfiable {unsatisfiableSeen}");
        Assert.IsGreaterThan(20, satisfiableSeen, "The sweep covers satisfiable-under-assumptions formulas.");
        Assert.IsGreaterThan(20, unsatisfiableSeen, "The sweep covers unsatisfiable-under-assumptions formulas.");
    }

    /// <summary>An unsatisfiable verdict reached without assumptions carries the empty core: the formula alone refutes, so no assumption participates.</summary>
    /// <param name="mode">The search mode under test.</param>
    [TestMethod]
    [DataRow(SatSearchMode.PropagationOnly)]
    [DataRow(SatSearchMode.ConflictLearning)]
    public void EmptyAssumptionSetYieldsEmptyCore(SatSearchMode mode)
    {
        //Contradicting unit clauses: unsatisfiable from the formula alone.
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, IsPositive: true)],
            [new SatLiteral(0, IsPositive: false)],
        ];

        SatVerdict verdict = SatSolver.SolveUnderAssumptions(clauses, variableCount: 1, assumptions: [], mode: mode, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(verdict.IsSatisfiable);
        Assert.IsNotNull(verdict.AssumptionCore);
        Assert.IsEmpty(verdict.AssumptionCore, "The formula alone refutes, so no assumption participates.");
    }

    /// <summary>A single assumption that propagation alone refutes is exactly the core.</summary>
    /// <param name="mode">The search mode under test.</param>
    [TestMethod]
    [DataRow(SatSearchMode.PropagationOnly)]
    [DataRow(SatSearchMode.ConflictLearning)]
    public void SingleRefutedAssumptionIsExactlyTheCore(SatSearchMode mode)
    {
        //(~x0 | x1) & (~x0 | ~x1): satisfiable on its own (x0 false), but
        //assuming x0 forces x1 both ways. The lone assumption is the whole core.
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, false), new SatLiteral(1, true)],
            [new SatLiteral(0, false), new SatLiteral(1, false)],
        ];

        SatLiteral assumeX0 = new(0, IsPositive: true);
        SatVerdict verdict = SatSolver.SolveUnderAssumptions(clauses, variableCount: 2, [assumeX0], mode: mode, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(verdict.IsSatisfiable);
        Assert.IsNotNull(verdict.AssumptionCore);
        Assert.HasCount(1, verdict.AssumptionCore);
        Assert.AreEqual(assumeX0, verdict.AssumptionCore[0]);
    }

    /// <summary>
    /// An assumption on a variable no clause mentions cannot participate in any
    /// refutation: with conflict learning the core excludes it, leaving exactly
    /// the assumptions that drive the conflict. Minimality is provable here —
    /// the irrelevant assumption shares no clause with the refuting pair.
    /// </summary>
    [TestMethod]
    public void IrrelevantAssumptionIsExcludedFromTheCore()
    {
        //x2 appears in no clause. Assuming x0 refutes via (~x0 | x1) & (~x0 |
        //~x1); the assumption on x2 is irrelevant and must not enter the core.
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, false), new SatLiteral(1, true)],
            [new SatLiteral(0, false), new SatLiteral(1, false)],
        ];

        SatLiteral assumeX0 = new(0, IsPositive: true);
        SatLiteral assumeX2 = new(2, IsPositive: true);
        SatVerdict verdict = SatSolver.SolveUnderAssumptions(clauses, variableCount: 3, [assumeX2, assumeX0], mode: SatSearchMode.ConflictLearning, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(verdict.IsSatisfiable);
        Assert.IsNotNull(verdict.AssumptionCore);
        Assert.Contains(assumeX0, verdict.AssumptionCore, "The refuting assumption is in the core.");
        Assert.DoesNotContain(assumeX2, verdict.AssumptionCore, "The irrelevant assumption is excluded.");
        Assert.HasCount(1, verdict.AssumptionCore, "Only the refuting assumption participates.");
    }

    /// <summary>
    /// Two contradictory assumptions on the same variable are exactly the core:
    /// the clash names the pair, and re-solving on that pair stays unsatisfiable.
    /// </summary>
    /// <param name="mode">The search mode under test.</param>
    [TestMethod]
    [DataRow(SatSearchMode.PropagationOnly)]
    [DataRow(SatSearchMode.ConflictLearning)]
    public void ContradictoryAssumptionsAreExactlyTheCore(SatSearchMode mode)
    {
        //An empty formula: the only refutation is the contradictory pair.
        SatLiteral assumeX0True = new(0, IsPositive: true);
        SatLiteral assumeX0False = new(0, IsPositive: false);
        SatVerdict verdict = SatSolver.SolveUnderAssumptions([], variableCount: 1, [assumeX0True, assumeX0False], mode: mode, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(verdict.IsSatisfiable);
        Assert.IsNotNull(verdict.AssumptionCore);
        Assert.HasCount(2, verdict.AssumptionCore);
        Assert.Contains(assumeX0True, verdict.AssumptionCore);
        Assert.Contains(assumeX0False, verdict.AssumptionCore);

        SatVerdict reSolved = SatSolver.SolveUnderAssumptions([], variableCount: 1, [.. verdict.AssumptionCore], mode: mode, cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(reSolved.IsSatisfiable, "Re-solving on the contradictory pair stays unsatisfiable.");
    }

    /// <summary>
    /// A duplicated assumption enters the core at most once: the core stays a
    /// set even when the supplied assumptions repeat a literal.
    /// </summary>
    /// <param name="mode">The search mode under test.</param>
    [TestMethod]
    [DataRow(SatSearchMode.PropagationOnly)]
    [DataRow(SatSearchMode.ConflictLearning)]
    public void DuplicateAssumptionEntersTheCoreOnce(SatSearchMode mode)
    {
        //(~x0 | x1) & (~x0 | ~x1): assuming x0 (twice) refutes. The duplicate
        //must not double the core.
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, false), new SatLiteral(1, true)],
            [new SatLiteral(0, false), new SatLiteral(1, false)],
        ];

        SatLiteral assumeX0 = new(0, IsPositive: true);
        SatVerdict verdict = SatSolver.SolveUnderAssumptions(clauses, variableCount: 2, [assumeX0, assumeX0], mode: mode, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(verdict.IsSatisfiable);
        Assert.IsNotNull(verdict.AssumptionCore);
        Assert.HasCount(1, verdict.AssumptionCore, "The duplicate assumption enters the core once.");
        Assert.AreEqual(assumeX0, verdict.AssumptionCore[0]);
    }

    /// <summary>A satisfiable-under-assumptions verdict carries no core.</summary>
    /// <param name="mode">The search mode under test.</param>
    [TestMethod]
    [DataRow(SatSearchMode.PropagationOnly)]
    [DataRow(SatSearchMode.ConflictLearning)]
    public void SatisfiableVerdictHasNoCore(SatSearchMode mode)
    {
        //(x0 | x1): assuming x0 is satisfiable.
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, true), new SatLiteral(1, true)],
        ];

        SatVerdict verdict = SatSolver.SolveUnderAssumptions(clauses, variableCount: 2, [new SatLiteral(0, true)], mode: mode, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(verdict.IsSatisfiable);
        Assert.IsNull(verdict.AssumptionCore);
    }

    /// <summary>Both modes decide the pigeonhole contradiction unsatisfiable; the search must learn and backjump, not only propagate.</summary>
    /// <param name="mode">The search mode under test.</param>
    [TestMethod]
    [DataRow(SatSearchMode.PropagationOnly)]
    [DataRow(SatSearchMode.ConflictLearning)]
    public void PigeonholeIsUnsatisfiableInBothModes(SatSearchMode mode)
    {
        (List<IReadOnlyList<SatLiteral>> clauses, int variableCount) = Pigeonhole(pigeons: 5, holes: 4);

        Assert.IsFalse(SatSolver.Solve(clauses, variableCount, mode: mode, cancellationToken: TestContext.CancellationToken).IsSatisfiable);
    }

    /// <summary>
    /// Conflict learning agrees with propagation-only on the pigeonhole
    /// family while it records its timing. The assertion holds the modes to
    /// the same verdict; the timing it prints is the mode comparison.
    /// </summary>
    [TestMethod]
    public void PigeonholeModeComparison()
    {
        TestContext.WriteLine("pigeons->holes | variables | clauses | PropagationOnly (ms) | ConflictLearning (ms)");
        for(int holes = 5; holes <= 7; holes++)
        {
            (List<IReadOnlyList<SatLiteral>> clauses, int variableCount) = Pigeonhole(pigeons: holes + 1, holes);

            double propagationOnlyMs = TimeUnsatisfiable(clauses, variableCount, SatSearchMode.PropagationOnly);
            double conflictLearningMs = TimeUnsatisfiable(clauses, variableCount, SatSearchMode.ConflictLearning);

            TestContext.WriteLine($"{holes + 1}->{holes} | {variableCount} | {clauses.Count} | {propagationOnlyMs:F1} | {conflictLearningMs:F1}");
        }
    }

    /// <summary>Solves the formula as unsatisfiable in the given mode, asserting the verdict and returning the elapsed milliseconds.</summary>
    /// <param name="clauses">The formula, expected unsatisfiable.</param>
    /// <param name="variableCount">The variable count.</param>
    /// <param name="mode">The search mode to time.</param>
    /// <returns>The elapsed wall-clock time in milliseconds.</returns>
    private double TimeUnsatisfiable(List<IReadOnlyList<SatLiteral>> clauses, int variableCount, SatSearchMode mode)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        SatVerdict verdict = SatSolver.Solve(clauses, variableCount, mode: mode, cancellationToken: TestContext.CancellationToken);
        double elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        Assert.IsFalse(verdict.IsSatisfiable, $"The pigeonhole formula must be unsatisfiable in {mode}.");

        return elapsedMs;
    }

    /// <summary>
    /// Builds the pigeonhole formula: every pigeon sits in some hole and no
    /// two pigeons share a hole. It is unsatisfiable exactly when there are
    /// more pigeons than holes, and propagation alone cannot prune it.
    /// </summary>
    /// <param name="pigeons">The pigeon count.</param>
    /// <param name="holes">The hole count.</param>
    /// <returns>The clauses and the variable count; variable <c>pigeon * holes + hole</c> places that pigeon in that hole.</returns>
    private static (List<IReadOnlyList<SatLiteral>> Clauses, int VariableCount) Pigeonhole(int pigeons, int holes)
    {
        List<IReadOnlyList<SatLiteral>> clauses = [];
        for(int pigeon = 0; pigeon < pigeons; pigeon++)
        {
            SatLiteral[] someHole = new SatLiteral[holes];
            for(int hole = 0; hole < holes; hole++)
            {
                someHole[hole] = new SatLiteral((pigeon * holes) + hole, true);
            }

            clauses.Add(someHole);
        }

        for(int hole = 0; hole < holes; hole++)
        {
            for(int first = 0; first < pigeons; first++)
            {
                for(int second = first + 1; second < pigeons; second++)
                {
                    clauses.Add([new SatLiteral((first * holes) + hole, false), new SatLiteral((second * holes) + hole, false)]);
                }
            }
        }

        return (clauses, pigeons * holes);
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
