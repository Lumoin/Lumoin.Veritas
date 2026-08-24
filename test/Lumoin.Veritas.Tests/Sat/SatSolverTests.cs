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
    [DataRow(SatSearchMode.WatchedLearning)]
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

    /// <summary>
    /// The watched-learning engine with restarts firing after all but the first
    /// conflict still agrees with exhaustive enumeration and returns valid models,
    /// over the same deterministic sweep. A restart changes the search path, never
    /// the verdict, and this frequent-restart schedule drives the abandon-and-resume
    /// path an instance small enough for a brute-force oracle would rarely reach at
    /// the production restart unit — so restart soundness is checked against ground truth.
    /// </summary>
    [TestMethod]
    public void WatchedLearningWithFrequentRestartsMatchesEnumeration()
    {
        //A deterministic xorshift drives the generation; no entropy APIs.
        ulong state = 0x9E3779B97F4A7C15UL;
        int satisfiableSeen = 0;
        int unsatisfiableSeen = 0;
        long restartsTaken = 0;

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

            SatVerdict verdict = SatSolver.SolveWatchedLearningForTest(clauses, variableCount, assumptions: [], lubyUnit: 1, cancellationToken: TestContext.CancellationToken);
            bool expected = ExistsSatisfyingAssignment(clauses, variableCount);

            Assert.AreEqual(expected, verdict.IsSatisfiable, $"Round {round}: the restart engine and enumeration disagree.");
            restartsTaken += verdict.Statistics.Restarts;
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
        Assert.IsGreaterThan(0L, restartsTaken, "The frequent-restart schedule actually restarts over the sweep.");
    }

    /// <summary>
    /// The restart engine under assumptions agrees with the assumptions-as-unit-clauses
    /// encoding, over a deterministic sweep with restarts firing after all but the first
    /// conflict: the assumption prefix a restart abandons back to must be preserved, so a
    /// model still honours every assumption and the verdict still matches. A restart that
    /// wrongly unwound the assumption prefix would surface here as a violated assumption
    /// or a divergent verdict.
    /// </summary>
    [TestMethod]
    public void AssumptionsWithFrequentRestartsMatchUnitClauseEncoding()
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

            List<IReadOnlyList<SatLiteral>> extended = [.. clauses];
            foreach(SatLiteral assumption in assumptions)
            {
                extended.Add([assumption]);
            }

            SatVerdict assumed = SatSolver.SolveWatchedLearningForTest(clauses, variableCount, assumptions, lubyUnit: 1, cancellationToken: TestContext.CancellationToken);
            SatVerdict reference = SatSolver.Solve(extended, variableCount, cancellationToken: TestContext.CancellationToken);

            Assert.AreEqual(reference.IsSatisfiable, assumed.IsSatisfiable, $"Round {round}: the restart engine under assumptions and the unit-clause encoding disagree.");
            if(assumed.IsSatisfiable)
            {
                satisfiableSeen++;
                Assert.IsNotNull(assumed.Assignment);
                Assert.IsTrue(Satisfies(clauses, assumed.Assignment), $"Round {round}: the model does not satisfy the formula.");
                foreach(SatLiteral assumption in assumptions)
                {
                    Assert.AreEqual(assumption.IsPositive, assumed.Assignment[assumption.Variable], $"Round {round}: a restart dropped or flipped the assumption prefix.");
                }
            }
            else
            {
                unsatisfiableSeen++;

                //The failed-assumption core the restart engine returns must be a sound
                //subset: a restart must not let a search decision masquerade as an
                //assumption in the core walk. Every core literal was supplied, and
                //re-solving while assuming only the core stays unsatisfiable per the
                //trusted no-restart engine.
                Assert.IsNotNull(assumed.AssumptionCore);
                foreach(SatLiteral coreLiteral in assumed.AssumptionCore)
                {
                    Assert.Contains(coreLiteral, assumptions, $"Round {round}: a core literal was not a supplied assumption under restarts.");
                }

                SatVerdict reSolved = SatSolver.SolveUnderAssumptions(clauses, variableCount, [.. assumed.AssumptionCore], mode: SatSearchMode.WatchedLearning, cancellationToken: TestContext.CancellationToken);
                Assert.IsFalse(reSolved.IsSatisfiable, $"Round {round}: re-solving on the restart-derived core is satisfiable — the core is unsound.");
            }
        }

        Assert.IsGreaterThan(20, satisfiableSeen, "The sweep covers satisfiable-under-assumptions formulas.");
        Assert.IsGreaterThan(20, unsatisfiableSeen, "The sweep covers unsatisfiable-under-assumptions formulas.");
    }

    /// <summary>
    /// Self-subsumption minimization removes a redundant lower-level literal from a
    /// first-UIP clause. On a hand-built conflict whose learned clause is
    /// {¬x2, ¬x0, ¬x1} — where x1 was forced by (¬x0 ∨ x1), so ¬x1 is already implied by
    /// the ¬x0 the clause carries — minimization drops ¬x1, while the same analysis
    /// without minimization keeps it. This checks the algorithm directly, not only that
    /// it preserves verdicts.
    /// </summary>
    [TestMethod]
    public void MinimizationRemovesASelfSubsumedLiteral()
    {
        //x0@1 decision; x1@1 forced by (¬x0 ∨ x1); x2@2 decision; x3@2 forced by
        //(¬x2 ∨ x3); x4@2 forced by (¬x2 ∨ x4); conflict (¬x3 ∨ ¬x4 ∨ ¬x0 ∨ ¬x1). Every
        //variable is assigned true, so every clause literal above is currently false.
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, false), new SatLiteral(1, true)],
            [new SatLiteral(2, false), new SatLiteral(3, true)],
            [new SatLiteral(2, false), new SatLiteral(4, true)],
            [new SatLiteral(3, false), new SatLiteral(4, false), new SatLiteral(0, false), new SatLiteral(1, false)],
        ];

        SatSolver.ClauseArena arena = new(clauses);
        int[] values = [1, 1, 1, 1, 1];
        int[] levels = [1, 1, 2, 2, 2];
        int[] reasons = [SatSolver.NoReason, 0, SatSolver.NoReason, 1, 2];
        int[] trail = [0, 1, 2, 3, 4];
        int[] seen = new int[5];

        List<SatLiteral> withoutMinimization = [];
        SatSolver.Analyze(arena, conflict: 3, values, levels, reasons, trail, trailCount: 5, currentLevel: 2, seen, withoutMinimization, bumped: null, minimizeStack: null, minimizeToClear: null, out SatLiteral assertingOff);

        List<SatLiteral> withMinimization = [];
        List<int> minimizeStack = [];
        List<int> minimizeToClear = [];
        SatSolver.Analyze(arena, conflict: 3, values, levels, reasons, trail, trailCount: 5, currentLevel: 2, seen, withMinimization, bumped: null, minimizeStack, minimizeToClear, out SatLiteral assertingOn);

        SatLiteral uip = new(2, IsPositive: false);
        SatLiteral kept = new(0, IsPositive: false);
        SatLiteral redundant = new(1, IsPositive: false);

        Assert.AreEqual(uip, assertingOff, "The implication point is ¬x2 either way.");
        Assert.AreEqual(uip, assertingOn);
        Assert.HasCount(3, withoutMinimization, "The un-minimized first-UIP clause keeps the redundant literal.");
        Assert.Contains(redundant, withoutMinimization);
        Assert.HasCount(2, withMinimization, "Minimization drops the self-subsumed literal.");
        Assert.DoesNotContain(redundant, withMinimization, "The self-subsumed literal ¬x1 is removed.");
        Assert.Contains(uip, withMinimization);
        Assert.Contains(kept, withMinimization);
    }

    /// <summary>
    /// The watched-learning engine with learned-clause minimization turned off still
    /// agrees with exhaustive enumeration over the deterministic sweep: the minimize-off
    /// comparand is correct, as the default minimize-on path (exercised by the main
    /// exhaustive differential) is. Minimization changes which clauses are learned, not
    /// the verdict.
    /// </summary>
    [TestMethod]
    public void WatchedLearningWithoutMinimizationMatchesEnumeration()
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

            SatVerdict verdict = SatSolver.SolveWatchedLearningForTest(clauses, variableCount, assumptions: [], lubyUnit: 0, cancellationToken: TestContext.CancellationToken, minimize: false);
            bool expected = ExistsSatisfyingAssignment(clauses, variableCount);

            Assert.AreEqual(expected, verdict.IsSatisfiable, $"Round {round}: the minimize-off engine and enumeration disagree.");
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

    /// <summary>
    /// The watched-learning engine with the dynamic literal-block-distance restart policy
    /// still agrees with exhaustive enumeration over the deterministic sweep. Every conflict
    /// computes the learned clause's LBD and folds it into the two moving averages, so this
    /// exercises that path over hundreds of instances even where the trigger never fires; a
    /// corrupted LBD computation or moving-average state would diverge from the oracle.
    /// </summary>
    [TestMethod]
    public void DynamicRestartMatchesEnumeration()
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

            SatVerdict verdict = SatSolver.SolveWatchedLearningForTest(clauses, variableCount, assumptions: [], lubyUnit: 0, cancellationToken: TestContext.CancellationToken, dynamicRestart: true);
            bool expected = ExistsSatisfyingAssignment(clauses, variableCount);

            Assert.AreEqual(expected, verdict.IsSatisfiable, $"Round {round}: the dynamic-restart engine and enumeration disagree.");
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

    /// <summary>
    /// The dynamic literal-block-distance restart policy actually fires on the pigeonhole
    /// family — whose symmetric conflicts span many decision levels, so the fast LBD average
    /// rises above the slow — and the formula stays unsatisfiable through the restarts: the
    /// firing path is exercised and remains verdict-preserving on a known-answer instance.
    /// </summary>
    [TestMethod]
    public void DynamicRestartFiresAndDecidesPigeonhole()
    {
        (List<IReadOnlyList<SatLiteral>> clauses, int variableCount) = Pigeonhole(pigeons: 7, holes: 6);

        SatVerdict verdict = SatSolver.SolveWatchedLearningForTest(clauses, variableCount, assumptions: [], lubyUnit: 0, cancellationToken: TestContext.CancellationToken, dynamicRestart: true);

        Assert.IsFalse(verdict.IsSatisfiable, "The pigeonhole formula is unsatisfiable, and dynamic restarts must not change that.");
        Assert.IsGreaterThan(0, verdict.Statistics.Restarts, "The LBD-driven policy restarts on the pigeonhole family's high-LBD conflicts.");
    }

    /// <summary>
    /// Trail blocking is verdict-preserving: over a deterministic sweep of near-transition
    /// random 3-SAT the dynamic policy with blocking decides exactly what the trusted engine
    /// does and its models satisfy the formula. Blocking only changes when a restart fires,
    /// never the answer.
    /// </summary>
    [TestMethod]
    public void TrailBlockingIsVerdictPreserving()
    {
        ulong state = 0x14057B7EF767814FUL;
        int satisfiableSeen = 0;
        int unsatisfiableSeen = 0;

        for(int round = 0; round < 120; round++)
        {
            int variableCount = 14 + (int)(Next(ref state) % 15);
            int clauseCount = (int)(4.2 * variableCount);
            List<IReadOnlyList<SatLiteral>> clauses = BuildRandom3Sat(ref state, variableCount, clauseCount);

            SatVerdict blocked = SatSolver.SolveWatchedLearningForTest(clauses, variableCount, assumptions: [], lubyUnit: 0, cancellationToken: TestContext.CancellationToken, dynamicRestart: true, trailBlocking: true);
            bool expected = SatSolver.Solve(clauses, variableCount, cancellationToken: TestContext.CancellationToken).IsSatisfiable;

            Assert.AreEqual(expected, blocked.IsSatisfiable, $"Round {round}: trail-blocking and the trusted engine disagree.");
            if(blocked.IsSatisfiable)
            {
                satisfiableSeen++;
                Assert.IsNotNull(blocked.Assignment);
                Assert.IsTrue(Satisfies(clauses, blocked.Assignment), $"Round {round}: the blocked model does not satisfy the formula.");
            }
            else
            {
                unsatisfiableSeen++;
            }
        }

        Assert.IsGreaterThan(0, satisfiableSeen, "The sweep covers satisfiable formulas.");
        Assert.IsGreaterThan(0, unsatisfiableSeen, "The sweep covers unsatisfiable formulas.");
    }

    /// <summary>
    /// Trail blocking's path is exercised, not dormant. Blocking postpones a restart while the
    /// assignment grows toward a model, so it fires on hard satisfiable instances — where the
    /// trail runs above its recent average — rather than on symmetric unsatisfiable ones like
    /// pigeonhole where no model is approached. Over a deterministic sweep hard enough to pass
    /// the dynamic policy's minimum conflict interval, blocking alters the restart count the
    /// dynamic policy takes on at least one instance, while the verdict never changes.
    /// </summary>
    [TestMethod]
    public void TrailBlockingFiresOnHardSatisfiableInstances()
    {
        ulong state = 0x9E6C63A2B4F1D058UL;
        int blockingChangedRestarts = 0;

        for(int round = 0; round < 40; round++)
        {
            int variableCount = 50 + (int)(Next(ref state) % 9);
            int clauseCount = (int)(4.25 * variableCount);
            List<IReadOnlyList<SatLiteral>> clauses = BuildRandom3Sat(ref state, variableCount, clauseCount);

            SatVerdict blocked = SatSolver.SolveWatchedLearningForTest(clauses, variableCount, assumptions: [], lubyUnit: 0, cancellationToken: TestContext.CancellationToken, dynamicRestart: true, trailBlocking: true);
            SatVerdict unblocked = SatSolver.SolveWatchedLearningForTest(clauses, variableCount, assumptions: [], lubyUnit: 0, cancellationToken: TestContext.CancellationToken, dynamicRestart: true);

            Assert.AreEqual(unblocked.IsSatisfiable, blocked.IsSatisfiable, $"Round {round}: blocking changed the verdict — it must only change when a restart fires, never the answer.");
            if(blocked.Statistics.Restarts != unblocked.Statistics.Restarts)
            {
                blockingChangedRestarts++;
            }
        }

        Assert.IsGreaterThan(0, blockingChangedRestarts, "Blocking alters the restart count on at least one hard satisfiable instance, so the blocking path is exercised.");
    }

    /// <summary>The Luby generator produces the reluctant-doubling restart sequence 1, 1, 2, 1, 1, 2, 4, 1, 1, 2, 1, 1, 2, 4, 8, ….</summary>
    [TestMethod]
    public void LubyGeneratorProducesTheReluctantDoublingSequence()
    {
        long[] expected = [1, 1, 2, 1, 1, 2, 4, 1, 1, 2, 1, 1, 2, 4, 8, 1, 1, 2];
        long u = 1;
        long v = 1;

        //The first term is the initial state; each later term is one advance.
        Assert.AreEqual(expected[0], v);
        for(int index = 1; index < expected.Length; index++)
        {
            Assert.AreEqual(expected[index], SatSolver.LubyNext(ref u, ref v), $"Luby term {index} mismatched.");
        }
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
    [DataRow(SatSearchMode.WatchedLearning)]
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
    [DataRow(SatSearchMode.WatchedLearning)]
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
    [DataRow(SatSearchMode.WatchedLearning)]
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
    [DataRow(SatSearchMode.WatchedLearning)]
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
    [DataRow(SatSearchMode.WatchedLearning)]
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
    [DataRow(SatSearchMode.WatchedLearning)]
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
    [DataRow(SatSearchMode.WatchedLearning)]
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
    [DataRow(SatSearchMode.WatchedLearning)]
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

    /// <summary>
    /// The borrowed-arena entry decides exactly what the public seam decides, in
    /// every mode: same verdict, same assignment or core, same statistics — and it
    /// returns the arena at its entry clause count, so the learned clauses of a
    /// learning mode demonstrably do not remain.
    /// </summary>
    /// <param name="mode">The search mode under test.</param>
    [TestMethod]
    [DataRow(SatSearchMode.PropagationOnly)]
    [DataRow(SatSearchMode.ConflictLearning)]
    [DataRow(SatSearchMode.WatchedLearning)]
    public void ArenaEntryMatchesThePublicSeam(SatSearchMode mode)
    {
        (List<IReadOnlyList<SatLiteral>> unsatClauses, int unsatVariables) = Pigeonhole(pigeons: 4, holes: 3);
        SatVerdict publicUnsat = SatSolver.SolveUnderAssumptions(unsatClauses, unsatVariables, assumptions: [], mode: mode, cancellationToken: TestContext.CancellationToken);
        SatSolver.ClauseArena unsatArena = new(unsatClauses);
        SatVerdict arenaUnsat = SatSolver.SolveUnderAssumptionsOnArena(unsatArena, unsatVariables, assumptions: [], pool: null, mode, TestContext.CancellationToken);

        AssertVerdictsEqual(publicUnsat, arenaUnsat, $"{mode} unsatisfiable");
        Assert.AreEqual(unsatClauses.Count, unsatArena.Count, "The arena must return at its entry clause count after an unsatisfiable solve.");

        (List<IReadOnlyList<SatLiteral>> satClauses, int satVariables) = Pigeonhole(pigeons: 3, holes: 3);
        List<SatLiteral> assumptions = [new SatLiteral(0, IsPositive: true)];
        SatVerdict publicSat = SatSolver.SolveUnderAssumptions(satClauses, satVariables, assumptions, mode: mode, cancellationToken: TestContext.CancellationToken);
        SatSolver.ClauseArena satArena = new(satClauses);
        SatVerdict arenaSat = SatSolver.SolveUnderAssumptionsOnArena(satArena, satVariables, assumptions, pool: null, mode, TestContext.CancellationToken);

        AssertVerdictsEqual(publicSat, arenaSat, $"{mode} satisfiable");
        Assert.AreEqual(satClauses.Count, satArena.Count, "The arena must return at its entry clause count after a satisfiable solve.");
    }

    /// <summary>
    /// One arena reused across solves under different assumption sets decides each
    /// solve exactly as a fresh arena would: a learned clause of one solve is a
    /// consequence of the formula together with that solve's assumptions, so any
    /// clause surviving the restore would move a later solve's statistics — the
    /// equality here is the leak detector.
    /// </summary>
    [TestMethod]
    public void ArenaReuseAcrossSolvesMatchesFreshRuns()
    {
        (List<IReadOnlyList<SatLiteral>> clauses, int variableCount) = Pigeonhole(pigeons: 4, holes: 3);
        List<List<SatLiteral>> assumptionSets =
        [
            [],
            [new SatLiteral(0, IsPositive: true)],
            [],
        ];

        SatSolver.ClauseArena arena = new(clauses);
        for(int setIndex = 0; setIndex < assumptionSets.Count; setIndex++)
        {
            List<SatLiteral> assumptions = assumptionSets[setIndex];
            SatVerdict reused = SatSolver.SolveUnderAssumptionsOnArena(arena, variableCount, assumptions, pool: null, SatSearchMode.ConflictLearning, TestContext.CancellationToken);
            SatVerdict fresh = SatSolver.SolveUnderAssumptions(clauses, variableCount, assumptions, mode: SatSearchMode.ConflictLearning, cancellationToken: TestContext.CancellationToken);

            AssertVerdictsEqual(fresh, reused, $"assumption set {setIndex}");
            Assert.AreEqual(clauses.Count, arena.Count, $"Assumption set {setIndex}: the arena must return at its entry clause count between solves.");
        }
    }

    /// <summary>
    /// A cancelled call restores the arena before the cancellation propagates —
    /// the same <c>finally</c> every exit shares — and the arena then serves a
    /// later solve exactly as a fresh one.
    /// </summary>
    [TestMethod]
    public void ArenaRestoresAfterCancellation()
    {
        (List<IReadOnlyList<SatLiteral>> clauses, int variableCount) = Pigeonhole(pigeons: 4, holes: 3);
        SatSolver.ClauseArena arena = new(clauses);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        bool observedCancellation = false;
        try
        {
            SatSolver.SolveUnderAssumptionsOnArena(arena, variableCount, assumptions: [], pool: null, SatSearchMode.ConflictLearning, cancelled.Token);
        }
        catch(OperationCanceledException)
        {
            observedCancellation = true;
        }

        Assert.IsTrue(observedCancellation, "The pre-cancelled token must abort the solve.");
        Assert.AreEqual(clauses.Count, arena.Count, "The cancelled call must leave the arena at its entry clause count.");

        SatVerdict reused = SatSolver.SolveUnderAssumptionsOnArena(arena, variableCount, assumptions: [], pool: null, SatSearchMode.ConflictLearning, TestContext.CancellationToken);
        SatVerdict fresh = SatSolver.SolveUnderAssumptions(clauses, variableCount, assumptions: [], mode: SatSearchMode.ConflictLearning, cancellationToken: TestContext.CancellationToken);

        AssertVerdictsEqual(fresh, reused, "the solve after the cancelled call");
    }

    /// <summary>
    /// Truncation restores exactly the boundary: appended clauses vanish, the
    /// boundary clause reads back intact, a count at or past the current one is a
    /// no-op, and the arena accepts appends again after the cut.
    /// </summary>
    [TestMethod]
    public void TruncateRestoresTheBoundaryClause()
    {
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, true), new SatLiteral(1, true)],
            [new SatLiteral(1, false), new SatLiteral(2, true)],
            [new SatLiteral(2, false), new SatLiteral(0, false), new SatLiteral(1, true)],
        ];
        SatSolver.ClauseArena arena = new(clauses);
        int[] boundaryCodes = arena.Literals(2).ToArray();

        arena.TruncateTo(arena.Count);
        Assert.AreEqual(3, arena.Count, "Truncating at the current boundary is a no-op.");

        arena.Add([new SatLiteral(0, true)]);
        arena.Add([new SatLiteral(2, true), new SatLiteral(1, false)]);
        Assert.AreEqual(5, arena.Count, "The appends extend the arena.");

        arena.TruncateTo(3);
        Assert.AreEqual(3, arena.Count, "Truncation removes exactly the appended clauses.");
        Assert.IsTrue(arena.Literals(2).SequenceEqual(boundaryCodes), "The boundary clause reads back intact after truncation.");

        arena.TruncateTo(5);
        Assert.AreEqual(3, arena.Count, "Truncating past the current boundary is a no-op.");

        int reAddedIndex = arena.Add([new SatLiteral(1, true)]);
        Assert.AreEqual(3, reAddedIndex, "An append after truncation takes the next index at the restored boundary.");
        Assert.IsTrue(arena.Literals(3).SequenceEqual([SatSolver.LiteralCode(new SatLiteral(1, true))]), "The re-added clause reads back at the boundary.");
    }

    /// <summary>Asserts two verdicts are identical: satisfiability, assignment sequence, assumption-core sequence, and statistics.</summary>
    /// <param name="expected">The reference verdict.</param>
    /// <param name="actual">The verdict under test.</param>
    /// <param name="context">The failure-message context naming the compared case.</param>
    private static void AssertVerdictsEqual(SatVerdict expected, SatVerdict actual, string context)
    {
        Assert.AreEqual(expected.IsSatisfiable, actual.IsSatisfiable, $"{context}: the verdicts disagree on satisfiability.");
        Assert.AreEqual(expected.Statistics, actual.Statistics, $"{context}: the statistics differ.");
        Assert.AreEqual(expected.Assignment is null, actual.Assignment is null, $"{context}: one verdict carries an assignment and the other does not.");
        if(expected.Assignment is not null && actual.Assignment is not null)
        {
            Assert.HasCount(expected.Assignment.Count, actual.Assignment, $"{context}: the assignments differ in length.");
            for(int i = 0; i < expected.Assignment.Count; i++)
            {
                Assert.AreEqual(expected.Assignment[i], actual.Assignment[i], $"{context}: the assignments differ at variable {i}.");
            }
        }

        Assert.AreEqual(expected.AssumptionCore is null, actual.AssumptionCore is null, $"{context}: one verdict carries a core and the other does not.");
        if(expected.AssumptionCore is not null && actual.AssumptionCore is not null)
        {
            Assert.HasCount(expected.AssumptionCore.Count, actual.AssumptionCore, $"{context}: the cores differ in length.");
            for(int i = 0; i < expected.AssumptionCore.Count; i++)
            {
                Assert.AreEqual(expected.AssumptionCore[i], actual.AssumptionCore[i], $"{context}: the cores differ at position {i}.");
            }
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

    /// <summary>Builds a random 3-SAT formula: each clause three distinct variables with random polarity.</summary>
    /// <param name="state">The deterministic generator state.</param>
    /// <param name="variableCount">The variable count; at least three.</param>
    /// <param name="clauseCount">The clause count.</param>
    /// <returns>The clauses.</returns>
    private static List<IReadOnlyList<SatLiteral>> BuildRandom3Sat(ref ulong state, int variableCount, int clauseCount)
    {
        List<IReadOnlyList<SatLiteral>> clauses = new(clauseCount);
        for(int i = 0; i < clauseCount; i++)
        {
            int first = (int)(Next(ref state) % (uint)variableCount);
            int second = (int)(Next(ref state) % (uint)variableCount);
            while(second == first)
            {
                second = (int)(Next(ref state) % (uint)variableCount);
            }

            int third = (int)(Next(ref state) % (uint)variableCount);
            while(third == first || third == second)
            {
                third = (int)(Next(ref state) % (uint)variableCount);
            }

            clauses.Add(
            [
                new SatLiteral(first, (Next(ref state) & 1) == 0),
                new SatLiteral(second, (Next(ref state) & 1) == 0),
                new SatLiteral(third, (Next(ref state) & 1) == 0),
            ]);
        }

        return clauses;
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
