using System;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core.Sat;

namespace Lumoin.Veritas.Tests.Sat;

/// <summary>
/// Tests for <see cref="SatSolverSession"/>, the stateful incremental solver: every
/// solve under assumptions must decide the same satisfiability as a fresh
/// <see cref="SatSolver.SolveUnderAssumptions"/> over the same formula — the
/// validated stateless engine is the oracle — across a sequence of related
/// assumption sets that reuse the session's learned clauses, variable order, and
/// saved phases. The contamination sweep guards the soundness keystone: a clause
/// learned under one assumption set must never refute a later, disjoint one.
/// </summary>
[TestClass]
internal sealed class SatSolverSessionTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Each solve in a reused session decides the same satisfiability as a fresh
    /// solve of the same formula and assumptions; satisfying models honour the
    /// formula and assumptions, and failed-assumption cores are subsets that stay
    /// unsatisfiable on their own — over a deterministic sweep of formula then
    /// assumption-set sequence.
    /// </summary>
    [TestMethod]
    public void SequentialSolvesAgreeWithFreshSolve()
    {
        ulong state = 0x243F6A8885A308D3UL;
        int satisfiableSeen = 0;
        int unsatisfiableSeen = 0;

        for(int round = 0; round < 300; round++)
        {
            int variableCount = 1 + (int)(Next(ref state) % 8);
            int clauseCount = 1 + (int)(Next(ref state) % 16);
            List<IReadOnlyList<SatLiteral>> clauses = BuildFormula(ref state, variableCount, clauseCount);

            using SatSolverSession session = new(clauses, variableCount);

            int sequenceLength = 5 + (int)(Next(ref state) % 16);
            for(int call = 0; call < sequenceLength; call++)
            {
                List<SatLiteral> assumptions = BuildAssumptions(ref state, variableCount);

                SatVerdict got = session.Solve(assumptions, cancellationToken: TestContext.CancellationToken);
                SatVerdict oracle = SatSolver.SolveUnderAssumptions(clauses, variableCount, assumptions, mode: SatSearchMode.WatchedLearning, cancellationToken: TestContext.CancellationToken);

                Assert.AreEqual(oracle.IsSatisfiable, got.IsSatisfiable, $"Round {round} call {call}: the session and a fresh solve disagree.");
                if(got.IsSatisfiable)
                {
                    satisfiableSeen++;
                    Assert.IsNotNull(got.Assignment);
                    Assert.IsNull(got.AssumptionCore);
                    Assert.IsTrue(Satisfies(clauses, got.Assignment), $"Round {round} call {call}: the model does not satisfy the formula.");
                    foreach(SatLiteral assumption in assumptions)
                    {
                        Assert.AreEqual(assumption.IsPositive, got.Assignment[assumption.Variable], $"Round {round} call {call}: the model violates an assumption.");
                    }
                }
                else
                {
                    unsatisfiableSeen++;
                    Assert.IsNotNull(got.AssumptionCore);
                    foreach(SatLiteral coreLiteral in got.AssumptionCore)
                    {
                        Assert.Contains(coreLiteral, assumptions, $"Round {round} call {call}: a core literal was not a supplied assumption.");
                    }

                    SatVerdict reStateless = SatSolver.SolveUnderAssumptions(clauses, variableCount, [.. got.AssumptionCore], mode: SatSearchMode.WatchedLearning, cancellationToken: TestContext.CancellationToken);
                    Assert.IsFalse(reStateless.IsSatisfiable, $"Round {round} call {call}: re-solving on the core is satisfiable — the core is unsound.");

                    using SatSolverSession coreSession = new(clauses, variableCount);
                    SatVerdict reSessioned = coreSession.Solve([.. got.AssumptionCore], cancellationToken: TestContext.CancellationToken);
                    Assert.IsFalse(reSessioned.IsSatisfiable, $"Round {round} call {call}: a fresh session on the core is satisfiable — the core is unsound.");
                }
            }
        }

        Assert.IsGreaterThan(20, satisfiableSeen, "The sweep covers satisfiable solves.");
        Assert.IsGreaterThan(20, unsatisfiableSeen, "The sweep covers unsatisfiable-under-assumptions solves.");
    }

    /// <summary>
    /// A clause learned while one assumption set drove the formula unsatisfiable
    /// must never refute a later assumption set the formula in fact satisfies. The
    /// sweep keeps one session over a satisfiable formula and checks every solve
    /// against the fresh oracle, counting the unsatisfiable-then-satisfiable
    /// transitions where contamination would surface.
    /// </summary>
    [TestMethod]
    public void LearnedClausesDoNotContaminateLaterAssumptions()
    {
        ulong state = 0xB7E151628AED2A6BUL;
        int transitionsExercised = 0;

        for(int round = 0; round < 300; round++)
        {
            int variableCount = 2 + (int)(Next(ref state) % 7);
            int clauseCount = 1 + (int)(Next(ref state) % 14);
            List<IReadOnlyList<SatLiteral>> clauses = BuildFormula(ref state, variableCount, clauseCount);

            //Only formulas satisfiable on their own exercise contamination; an
            //unsatisfiable formula latches and every call is trivially unsatisfiable.
            if(!SatSolver.Solve(clauses, variableCount, cancellationToken: TestContext.CancellationToken).IsSatisfiable)
            {
                continue;
            }

            using SatSolverSession session = new(clauses, variableCount);
            bool priorUnsatisfiable = false;
            int sequenceLength = 6 + (int)(Next(ref state) % 10);
            for(int call = 0; call < sequenceLength; call++)
            {
                List<SatLiteral> assumptions = BuildAssumptions(ref state, variableCount);

                SatVerdict got = session.Solve(assumptions, cancellationToken: TestContext.CancellationToken);
                SatVerdict oracle = SatSolver.SolveUnderAssumptions(clauses, variableCount, assumptions, mode: SatSearchMode.WatchedLearning, cancellationToken: TestContext.CancellationToken);

                Assert.AreEqual(oracle.IsSatisfiable, got.IsSatisfiable, $"Round {round} call {call}: the session diverged from a fresh solve — a learned clause may have baked in an earlier assumption set.");
                if(priorUnsatisfiable && got.IsSatisfiable)
                {
                    transitionsExercised++;
                }

                priorUnsatisfiable = !got.IsSatisfiable;
            }
        }

        Assert.IsGreaterThan(0, transitionsExercised, "The sweep exercised unsatisfiable-then-satisfiable sequences where contamination would surface.");
    }

    /// <summary>A reused session, a fresh session per call, and the stateless oracle agree on every solve: the verdict is independent of the accumulated state.</summary>
    [TestMethod]
    public void ReusedSessionMatchesFreshSessionAndOracle()
    {
        ulong state = 0xC3D2E1F0A9B8C7D6UL;

        for(int round = 0; round < 200; round++)
        {
            int variableCount = 1 + (int)(Next(ref state) % 7);
            int clauseCount = 1 + (int)(Next(ref state) % 12);
            List<IReadOnlyList<SatLiteral>> clauses = BuildFormula(ref state, variableCount, clauseCount);

            using SatSolverSession reused = new(clauses, variableCount);

            int sequenceLength = 4 + (int)(Next(ref state) % 8);
            for(int call = 0; call < sequenceLength; call++)
            {
                List<SatLiteral> assumptions = BuildAssumptions(ref state, variableCount);

                bool reusedVerdict = reused.Solve(assumptions, cancellationToken: TestContext.CancellationToken).IsSatisfiable;
                using SatSolverSession fresh = new(clauses, variableCount);
                bool freshVerdict = fresh.Solve(assumptions, cancellationToken: TestContext.CancellationToken).IsSatisfiable;
                bool oracleVerdict = SatSolver.SolveUnderAssumptions(clauses, variableCount, assumptions, mode: SatSearchMode.WatchedLearning, cancellationToken: TestContext.CancellationToken).IsSatisfiable;

                Assert.AreEqual(oracleVerdict, reusedVerdict, $"Round {round} call {call}: the reused session disagrees with the oracle.");
                Assert.AreEqual(oracleVerdict, freshVerdict, $"Round {round} call {call}: a fresh session disagrees with the oracle.");
            }
        }
    }

    /// <summary>A formula unsatisfiable on its own latches: the first and every later solve is unsatisfiable with an empty core, no assumption participating.</summary>
    [TestMethod]
    public void FormulaUnsatisfiableAloneLatchesEmptyCore()
    {
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, IsPositive: true)],
            [new SatLiteral(0, IsPositive: false)],
        ];

        using SatSolverSession session = new(clauses, variableCount: 1);

        SatVerdict first = session.Solve([], cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(first.IsSatisfiable);
        Assert.IsNotNull(first.AssumptionCore);
        Assert.IsEmpty(first.AssumptionCore);

        SatVerdict second = session.Solve([new SatLiteral(0, IsPositive: true)], cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(second.IsSatisfiable);
        Assert.IsNotNull(second.AssumptionCore);
        Assert.IsEmpty(second.AssumptionCore, "A latched formula refutes with no assumption participating.");
    }

    /// <summary>A single assumption that propagation alone refutes is exactly the core, decided through a session.</summary>
    [TestMethod]
    public void SingleRefutedAssumptionIsExactlyTheCore()
    {
        //(~x0 | x1) & (~x0 | ~x1): satisfiable on its own, but assuming x0 forces x1 both ways.
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, false), new SatLiteral(1, true)],
            [new SatLiteral(0, false), new SatLiteral(1, false)],
        ];

        using SatSolverSession session = new(clauses, variableCount: 2);
        SatLiteral assumeX0 = new(0, IsPositive: true);
        SatVerdict verdict = session.Solve([assumeX0], cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(verdict.IsSatisfiable);
        Assert.IsNotNull(verdict.AssumptionCore);
        Assert.HasCount(1, verdict.AssumptionCore);
        Assert.AreEqual(assumeX0, verdict.AssumptionCore[0]);
    }

    /// <summary>Two contradictory assumptions on the same variable are exactly the core, decided through a session.</summary>
    [TestMethod]
    public void ContradictoryAssumptionsAreExactlyTheCore()
    {
        using SatSolverSession session = new([], variableCount: 1);
        SatLiteral assumeX0True = new(0, IsPositive: true);
        SatLiteral assumeX0False = new(0, IsPositive: false);

        SatVerdict verdict = session.Solve([assumeX0True, assumeX0False], cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(verdict.IsSatisfiable);
        Assert.IsNotNull(verdict.AssumptionCore);
        Assert.HasCount(2, verdict.AssumptionCore);
        Assert.Contains(assumeX0True, verdict.AssumptionCore);
        Assert.Contains(assumeX0False, verdict.AssumptionCore);
    }

    /// <summary>A duplicated assumption enters the core at most once.</summary>
    [TestMethod]
    public void DuplicateAssumptionEntersTheCoreOnce()
    {
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, false), new SatLiteral(1, true)],
            [new SatLiteral(0, false), new SatLiteral(1, false)],
        ];

        using SatSolverSession session = new(clauses, variableCount: 2);
        SatLiteral assumeX0 = new(0, IsPositive: true);
        SatVerdict verdict = session.Solve([assumeX0, assumeX0], cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(verdict.IsSatisfiable);
        Assert.IsNotNull(verdict.AssumptionCore);
        Assert.HasCount(1, verdict.AssumptionCore, "The duplicate assumption enters the core once.");
        Assert.AreEqual(assumeX0, verdict.AssumptionCore[0]);
    }

    /// <summary>A satisfiable-under-assumptions solve carries no core and a model honouring the assumption.</summary>
    [TestMethod]
    public void SatisfiableSolveHasNoCore()
    {
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, true), new SatLiteral(1, true)],
        ];

        using SatSolverSession session = new(clauses, variableCount: 2);
        SatVerdict verdict = session.Solve([new SatLiteral(0, true)], cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(verdict.IsSatisfiable);
        Assert.IsNull(verdict.AssumptionCore);
        Assert.IsNotNull(verdict.Assignment);
        Assert.IsTrue(verdict.Assignment[0]);
    }

    /// <summary>Disposing is idempotent and solving after disposal throws.</summary>
    [TestMethod]
    public void DisposeIsIdempotentAndSolveAfterDisposeThrows()
    {
        SatSolverSession session = new([[new SatLiteral(0, IsPositive: true)]], variableCount: 1);
        Assert.IsTrue(session.Solve([], cancellationToken: TestContext.CancellationToken).IsSatisfiable);

        session.Dispose();
        session.Dispose();

        Assert.ThrowsExactly<System.ObjectDisposedException>(() => session.Solve([], cancellationToken: TestContext.CancellationToken));
    }

    /// <summary>
    /// A solve cancelled mid-search must leave the session's reused state consistent: the
    /// move-to-front order bumps its columns in place during a solve, so a cancellation
    /// must not strand the carried-over head out of step with them. The progress hook
    /// cancels DETERMINISTICALLY at the round after the first conflict — after learning
    /// and bumping have mutated the carried state — so the cancellation is guaranteed to
    /// land mid-search and the row asserts it fired. After cancelling a solve of an
    /// unsatisfiable formula, a fresh solve must still decide it unsatisfiable.
    /// </summary>
    [TestMethod]
    public void ReuseAfterCancelledSolveStaysSound()
    {
        //Pigeonhole is unsatisfiable and cannot be refuted by unit propagation alone, so the
        //search is guaranteed to reach a first conflict mid-search, after bumps — the path
        //that could desynchronize the carried-over move-to-front state from its columns.
        (List<IReadOnlyList<SatLiteral>> clauses, int variableCount) = Pigeonhole(holes: 7);

        using SatSolverSession session = new(clauses, variableCount);

        using(CancellationTokenSource cancel = new())
        {
            CancelAtConflictCount trigger = new(cancel, conflictThreshold: 1);
            OperationCanceledException? thrown = null;
            try
            {
                session.Solve([], trigger.Observe, cancel.Token);
            }
            catch(OperationCanceledException ex)
            {
                thrown = ex;
            }

            Assert.IsNotNull(thrown, "The hook-triggered cancellation must land in the round after the first conflict.");
        }

        SatVerdict afterCancel = session.Solve([], cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(afterCancel.IsSatisfiable, "A reused session after a cancelled solve must still decide the pigeonhole formula unsatisfiable.");
    }

    /// <summary>
    /// The deep counterpart of <see cref="ReuseAfterCancelledSolveStaysSound"/>: the solver is
    /// deterministic (no clock, no randomness), so an un-cancelled solve of the same formula
    /// measures the total conflict count exactly, and a second session cancelled at HALF that
    /// count lands deep mid-search — after many learned clauses and move-to-front reorderings,
    /// the state depth the deleted wall-clock trigger only sometimes reached. Reuse must stay
    /// sound from there too.
    /// </summary>
    [TestMethod]
    public void ReuseAfterDeepCancelledSolveStaysSound()
    {
        (List<IReadOnlyList<SatLiteral>> clauses, int variableCount) = Pigeonhole(holes: 7);

        int totalConflicts;
        using(SatSolverSession reference = new(clauses, variableCount))
        {
            SatVerdict verdict = reference.Solve([], cancellationToken: TestContext.CancellationToken);
            Assert.IsFalse(verdict.IsSatisfiable, "The pigeonhole formula is unsatisfiable.");
            totalConflicts = verdict.Statistics.Conflicts;
        }

        int deepThreshold = totalConflicts / 2;
        Assert.IsGreaterThan(1, deepThreshold, "The refutation must take enough conflicts for the deep trigger to sit strictly beyond the first-conflict row.");

        using SatSolverSession session = new(clauses, variableCount);
        using(CancellationTokenSource cancel = new())
        {
            CancelAtConflictCount trigger = new(cancel, deepThreshold);
            OperationCanceledException? thrown = null;
            try
            {
                session.Solve([], trigger.Observe, cancel.Token);
            }
            catch(OperationCanceledException ex)
            {
                thrown = ex;
            }

            Assert.IsNotNull(thrown, "The deep hook-triggered cancellation must land before the refutation completes.");
        }

        SatVerdict afterCancel = session.Solve([], cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(afterCancel.IsSatisfiable, "A reused session after a deep cancelled solve must still decide the pigeonhole formula unsatisfiable.");
    }

    /// <summary>Cancels its token source once the observed conflict count reaches a threshold — the deterministic mid-search cancellation trigger, holding its state explicitly so the observer is a closure-free method group.</summary>
    private sealed class CancelAtConflictCount
    {
        /// <summary>The token source the trigger cancels.</summary>
        private CancellationTokenSource Cancellation { get; }

        /// <summary>The conflict count at which the trigger fires.</summary>
        private int ConflictThreshold { get; }

        /// <summary>Constructs the trigger over its token source and threshold.</summary>
        /// <param name="cancellation">The token source to cancel.</param>
        /// <param name="conflictThreshold">The conflict count at which to cancel.</param>
        public CancelAtConflictCount(CancellationTokenSource cancellation, int conflictThreshold)
        {
            Cancellation = cancellation;
            ConflictThreshold = conflictThreshold;
        }

        /// <summary>The progress observer: cancels when the conflict count reaches the threshold.</summary>
        /// <param name="progress">The solve's current search counters.</param>
        public void Observe(in SatSolveProgress progress)
        {
            if(progress.Conflicts >= ConflictThreshold)
            {
                Cancellation.Cancel();
            }
        }
    }

    /// <summary>
    /// Learned-clause deletion fires over a long sequence and the verdicts stay
    /// correct: a globally satisfiable formula (so the session never latches and
    /// learning accumulates across calls) is interrogated under many assumption sets
    /// with a low deletion threshold; every solve must still agree with a fresh solve,
    /// and the session must report that deletion actually happened.
    /// </summary>
    [TestMethod]
    public void DeletionFiresAndSolvesStayCorrect()
    {
        //Unsatisfiability is driven only by assumptions over a globally-satisfiable
        //phase-transition formula, so the session never latches and learning
        //accumulates across the sequence — what makes the low threshold trip.
        ulong state = 0x1234567890ABCDEFUL;
        int variableCount = 40;
        int clauseCount = (int)(4.26 * variableCount);
        List<IReadOnlyList<SatLiteral>> clauses = FindSatisfiableFormula(ref state, variableCount, clauseCount);

        using SatSolverSession session = new(clauses, variableCount, reduceThreshold: 30);

        int satisfiableSeen = 0;
        int unsatisfiableSeen = 0;
        for(int call = 0; call < 300; call++)
        {
            List<SatLiteral> assumptions = BuildAssumptions(ref state, variableCount);

            SatVerdict got = session.Solve(assumptions, cancellationToken: TestContext.CancellationToken);
            SatVerdict oracle = SatSolver.SolveUnderAssumptions(clauses, variableCount, assumptions, mode: SatSearchMode.WatchedLearning, cancellationToken: TestContext.CancellationToken);

            Assert.AreEqual(oracle.IsSatisfiable, got.IsSatisfiable, $"Call {call}: the session disagrees with a fresh solve under clause deletion.");
            if(got.IsSatisfiable)
            {
                satisfiableSeen++;
            }
            else
            {
                unsatisfiableSeen++;
            }
        }

        Assert.IsGreaterThan(0, session.ReduceRounds, "Clause deletion must fire over the sequence.");
        Assert.IsGreaterThan(0, session.DeletedClauseTotal, "Clause deletion must remove clauses.");
        Assert.IsGreaterThan(0, satisfiableSeen, "The sequence covers satisfiable-under-assumptions solves.");
        Assert.IsGreaterThan(0, unsatisfiableSeen, "The sequence covers unsatisfiable-under-assumptions solves.");
    }

    /// <summary>
    /// Restarts fire over a long sequence and the verdicts stay correct: a globally
    /// satisfiable formula (so the session never latches and learning accumulates
    /// across calls) is interrogated under many assumption sets with restarts firing
    /// after all but the first conflict; every solve must still agree with a fresh
    /// solve, its model must honour the assumptions, and the session must report that
    /// restarts actually happened. A restart that dropped the reused state or the
    /// assumption prefix would surface as a divergent verdict or a violated assumption.
    /// </summary>
    [TestMethod]
    public void RestartsFireAndSolvesStayCorrect()
    {
        ulong state = 0x0FEDCBA987654321UL;
        int variableCount = 40;
        int clauseCount = (int)(4.26 * variableCount);
        List<IReadOnlyList<SatLiteral>> clauses = FindSatisfiableFormula(ref state, variableCount, clauseCount);

        using SatSolverSession session = new(clauses, variableCount, reduceThreshold: int.MaxValue, restartUnit: 1);

        int satisfiableSeen = 0;
        int unsatisfiableSeen = 0;
        long restartsTaken = 0;
        for(int call = 0; call < 300; call++)
        {
            List<SatLiteral> assumptions = BuildAssumptions(ref state, variableCount);

            SatVerdict got = session.Solve(assumptions, cancellationToken: TestContext.CancellationToken);
            SatVerdict oracle = SatSolver.SolveUnderAssumptions(clauses, variableCount, assumptions, mode: SatSearchMode.WatchedLearning, cancellationToken: TestContext.CancellationToken);

            Assert.AreEqual(oracle.IsSatisfiable, got.IsSatisfiable, $"Call {call}: the session disagrees with a fresh solve under frequent restarts.");
            restartsTaken += got.Statistics.Restarts;
            if(got.IsSatisfiable)
            {
                satisfiableSeen++;
                Assert.IsNotNull(got.Assignment);
                Assert.IsTrue(Satisfies(clauses, got.Assignment), $"Call {call}: the model does not satisfy the formula under restarts.");
                foreach(SatLiteral assumption in assumptions)
                {
                    Assert.AreEqual(assumption.IsPositive, got.Assignment[assumption.Variable], $"Call {call}: the model violates an assumption under restarts.");
                }
            }
            else
            {
                unsatisfiableSeen++;

                //The failed-assumption core AnalyzeFinal derives after restarts must be a
                //sound subset: a restart re-places assumptions as fresh decisions, so the
                //core walk must still attribute the refutation only to supplied
                //assumptions, never to a search decision. Every core literal was supplied,
                //and re-solving while assuming only the core stays unsatisfiable per the
                //trusted stateless engine.
                Assert.IsNotNull(got.AssumptionCore);
                foreach(SatLiteral coreLiteral in got.AssumptionCore)
                {
                    Assert.Contains(coreLiteral, assumptions, $"Call {call}: a core literal was not a supplied assumption under restarts.");
                }

                SatVerdict reSolved = SatSolver.SolveUnderAssumptions(clauses, variableCount, [.. got.AssumptionCore], mode: SatSearchMode.WatchedLearning, cancellationToken: TestContext.CancellationToken);
                Assert.IsFalse(reSolved.IsSatisfiable, $"Call {call}: re-solving on the restart-derived core is satisfiable — the core is unsound.");
            }
        }

        Assert.IsGreaterThan(0L, restartsTaken, "Restarts must fire over the sequence.");
        Assert.IsGreaterThan(0, satisfiableSeen, "The sequence covers satisfiable-under-assumptions solves.");
        Assert.IsGreaterThan(0, unsatisfiableSeen, "The sequence covers unsatisfiable-under-assumptions solves.");
    }

    /// <summary>
    /// A session interrogated while its formula GROWS between solves — fresh variables
    /// minted through <see cref="SatSolverSession.EnsureVariableCount"/> and entailed
    /// clauses appended through <see cref="SatSolverSession.AddClause"/>, the pattern a
    /// reasoner drives as it spawns successor worlds and learns modal clauses — must
    /// decide every solve exactly as a fresh stateless solve of the formula as it then
    /// stands. This is the soundness keystone for growth: a clause the session learned
    /// before a growth must never diverge from the oracle over the grown formula, and
    /// a model must satisfy every clause including the appended ones and honour the
    /// assumptions. The sweep crosses the initial column capacity many times, so the
    /// re-rent that copies the reused state into wider buffers is exercised under load.
    /// </summary>
    [TestMethod]
    public void GrowingFormulaSolvesAgreeWithFreshSolve()
    {
        ulong state = 0x9E3779B97F4A7C15UL;
        int satisfiableSeen = 0;
        int unsatisfiableSeen = 0;
        int growthsApplied = 0;

        for(int round = 0; round < 120; round++)
        {
            int variableCount = 2 + (int)(Next(ref state) % 4);
            List<IReadOnlyList<SatLiteral>> clauses = BuildFormula(ref state, variableCount, 1 + (int)(Next(ref state) % 5));

            using SatSolverSession session = new(clauses, variableCount);

            int growthSteps = 4 + (int)(Next(ref state) % 8);
            for(int step = 0; step < growthSteps; step++)
            {
                //Grow the formula as a consumer does: mint a few fresh variables, then
                //append a few clauses over the enlarged space — variables grow before
                //the clauses that may reference them, the order the surface requires.
                int added = (int)(Next(ref state) % 4);
                if(added > 0)
                {
                    variableCount += added;
                    session.EnsureVariableCount(variableCount);
                    growthsApplied++;
                }

                int appended = (int)(Next(ref state) % 4);
                for(int c = 0; c < appended; c++)
                {
                    IReadOnlyList<SatLiteral> clause = BuildClause(ref state, variableCount);
                    clauses.Add(clause);
                    session.AddClause(clause);
                }

                int solves = 3 + (int)(Next(ref state) % 4);
                for(int s = 0; s < solves; s++)
                {
                    List<SatLiteral> assumptions = BuildAssumptions(ref state, variableCount);

                    SatVerdict got = session.Solve(assumptions, cancellationToken: TestContext.CancellationToken);
                    SatVerdict oracle = SatSolver.SolveUnderAssumptions(clauses, variableCount, assumptions, mode: SatSearchMode.WatchedLearning, cancellationToken: TestContext.CancellationToken);

                    Assert.AreEqual(oracle.IsSatisfiable, got.IsSatisfiable, $"Round {round} step {step} solve {s}: the grown session diverged from a fresh solve of the grown formula.");
                    if(got.IsSatisfiable)
                    {
                        satisfiableSeen++;
                        Assert.IsNotNull(got.Assignment);
                        Assert.HasCount(variableCount, got.Assignment);
                        Assert.IsTrue(Satisfies(clauses, got.Assignment), $"Round {round} step {step} solve {s}: the model does not satisfy the grown formula.");
                        foreach(SatLiteral assumption in assumptions)
                        {
                            Assert.AreEqual(assumption.IsPositive, got.Assignment[assumption.Variable], $"Round {round} step {step} solve {s}: the model violates an assumption.");
                        }
                    }
                    else
                    {
                        unsatisfiableSeen++;
                    }
                }
            }
        }

        Assert.IsGreaterThan(0, growthsApplied, "The sweep applied variable growths.");
        Assert.IsGreaterThan(20, satisfiableSeen, "The sweep covers satisfiable solves over grown formulas.");
        Assert.IsGreaterThan(20, unsatisfiableSeen, "The sweep covers unsatisfiable solves over grown formulas.");
    }

    /// <summary>
    /// An empty clause appended between solves latches the formula unsatisfiable, just
    /// as an empty clause at construction does: the formula gained an unsatisfiable
    /// constraint, so every later solve refutes with no assumption participating.
    /// </summary>
    [TestMethod]
    public void AppendedEmptyClauseLatchesUnsatisfiable()
    {
        List<IReadOnlyList<SatLiteral>> clauses = [[new SatLiteral(0, IsPositive: true), new SatLiteral(1, IsPositive: true)]];

        using SatSolverSession session = new(clauses, variableCount: 2);
        Assert.IsTrue(session.Solve([], cancellationToken: TestContext.CancellationToken).IsSatisfiable);

        session.AddClause([]);

        SatVerdict after = session.Solve([new SatLiteral(0, IsPositive: true)], cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(after.IsSatisfiable);
        Assert.IsNotNull(after.AssumptionCore);
        Assert.IsEmpty(after.AssumptionCore, "An appended empty clause latches the formula unsatisfiable with no assumption participating.");
    }

    /// <summary>
    /// Clauses appended over freshly-minted variables constrain later solves: a unit and
    /// an implication added after growth force a fresh variable's value, so assuming
    /// against it is unsatisfiable and a consistent assumption yields the forced model.
    /// </summary>
    [TestMethod]
    public void AppendedClausesOverFreshVariablesConstrainTheModel()
    {
        using SatSolverSession session = new([], variableCount: 1);
        Assert.IsTrue(session.Solve([], cancellationToken: TestContext.CancellationToken).IsSatisfiable);

        session.EnsureVariableCount(3);
        session.AddClause([new SatLiteral(1, IsPositive: true)]);
        session.AddClause([new SatLiteral(1, IsPositive: false), new SatLiteral(2, IsPositive: false)]);

        SatVerdict refuted = session.Solve([new SatLiteral(2, IsPositive: true)], cancellationToken: TestContext.CancellationToken);
        Assert.IsFalse(refuted.IsSatisfiable, "The fresh-variable clauses force x2 false, so assuming x2 true is unsatisfiable.");

        SatVerdict satisfiable = session.Solve([new SatLiteral(2, IsPositive: false)], cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(satisfiable.IsSatisfiable);
        Assert.IsNotNull(satisfiable.Assignment);
        Assert.HasCount(3, satisfiable.Assignment);
        Assert.IsTrue(satisfiable.Assignment[1], "x1 is forced true by the appended unit.");
        Assert.IsFalse(satisfiable.Assignment[2], "x2 is forced false by the appended implication.");
    }

    /// <summary>Growth is monotone: a request at or below the current variable count leaves the session unchanged and later solves still agree with the oracle.</summary>
    [TestMethod]
    public void EnsureVariableCountIsMonotone()
    {
        List<IReadOnlyList<SatLiteral>> clauses = [[new SatLiteral(0, IsPositive: true), new SatLiteral(1, IsPositive: false)]];

        using SatSolverSession session = new(clauses, variableCount: 3);
        session.EnsureVariableCount(1);
        session.EnsureVariableCount(3);

        SatVerdict got = session.Solve([new SatLiteral(0, IsPositive: false)], cancellationToken: TestContext.CancellationToken);
        SatVerdict oracle = SatSolver.SolveUnderAssumptions(clauses, 3, [new SatLiteral(0, IsPositive: false)], mode: SatSearchMode.WatchedLearning, cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual(oracle.IsSatisfiable, got.IsSatisfiable);
    }

    /// <summary>
    /// A carry-OFF session — the policy the reasoner seam runs — decides every solve
    /// exactly as a fresh stateless solve while its formula GROWS between solves, the
    /// pattern a reasoner drives as it spawns successor worlds and learns modal clauses.
    /// Re-filling the saved-phase column at each solve entry changes only the search path,
    /// never the verdict, so the growth-differential guard holds identically to the
    /// carry-ON session: a clause learned before a growth never diverges from the oracle
    /// over the grown formula, and a model satisfies every clause including the appended
    /// ones and honours the assumptions.
    /// </summary>
    [TestMethod]
    public void CarryOffGrowingFormulaSolvesAgreeWithFreshSolve()
    {
        ulong state = 0x8FA3B17C29D64E05UL;
        int satisfiableSeen = 0;
        int unsatisfiableSeen = 0;
        int growthsApplied = 0;

        for(int round = 0; round < 120; round++)
        {
            int variableCount = 2 + (int)(Next(ref state) % 4);
            List<IReadOnlyList<SatLiteral>> clauses = BuildFormula(ref state, variableCount, 1 + (int)(Next(ref state) % 5));

            using SatSolverSession session = new(clauses, variableCount, reduceThreshold: SatSolverSession.DeletionDisabled, carryPhases: false);

            int growthSteps = 4 + (int)(Next(ref state) % 8);
            for(int step = 0; step < growthSteps; step++)
            {
                //Grow the formula as a consumer does: mint a few fresh variables, then
                //append a few clauses over the enlarged space — variables grow before
                //the clauses that may reference them, the order the surface requires.
                int added = (int)(Next(ref state) % 4);
                if(added > 0)
                {
                    variableCount += added;
                    session.EnsureVariableCount(variableCount);
                    growthsApplied++;
                }

                int appended = (int)(Next(ref state) % 4);
                for(int c = 0; c < appended; c++)
                {
                    IReadOnlyList<SatLiteral> clause = BuildClause(ref state, variableCount);
                    clauses.Add(clause);
                    session.AddClause(clause);
                }

                int solves = 3 + (int)(Next(ref state) % 4);
                for(int s = 0; s < solves; s++)
                {
                    List<SatLiteral> assumptions = BuildAssumptions(ref state, variableCount);

                    SatVerdict got = session.Solve(assumptions, cancellationToken: TestContext.CancellationToken);
                    SatVerdict oracle = SatSolver.SolveUnderAssumptions(clauses, variableCount, assumptions, mode: SatSearchMode.WatchedLearning, cancellationToken: TestContext.CancellationToken);

                    Assert.AreEqual(oracle.IsSatisfiable, got.IsSatisfiable, $"Round {round} step {step} solve {s}: the carry-off grown session diverged from a fresh solve of the grown formula.");
                    if(got.IsSatisfiable)
                    {
                        satisfiableSeen++;
                        Assert.IsNotNull(got.Assignment);
                        Assert.HasCount(variableCount, got.Assignment);
                        Assert.IsTrue(Satisfies(clauses, got.Assignment), $"Round {round} step {step} solve {s}: the model does not satisfy the grown formula.");
                        foreach(SatLiteral assumption in assumptions)
                        {
                            Assert.AreEqual(assumption.IsPositive, got.Assignment[assumption.Variable], $"Round {round} step {step} solve {s}: the model violates an assumption.");
                        }
                    }
                    else
                    {
                        unsatisfiableSeen++;
                    }
                }
            }
        }

        Assert.IsGreaterThan(0, growthsApplied, "The sweep applied variable growths.");
        Assert.IsGreaterThan(20, satisfiableSeen, "The sweep covers satisfiable solves over grown formulas.");
        Assert.IsGreaterThan(20, unsatisfiableSeen, "The sweep covers unsatisfiable solves over grown formulas.");
    }

    /// <summary>
    /// The phase-carry knob is verdict-transparent: over the small-instance family the
    /// suite sweeps, a carry-ON and a carry-OFF session decide the same satisfiability as
    /// each other and as the stateless oracle on every solve of a related assumption
    /// sequence, and each satisfiable verdict carries a model honouring the formula and
    /// assumptions. Saved-phase carry biases decision polarity only — it may steer the
    /// search and the model it lands on differently between the two policies — but it can
    /// never change a verdict; this pins that invariant, the guarantee the reasoner seam
    /// relies on when it runs carry-OFF.
    /// </summary>
    [TestMethod]
    public void CarryOnAndCarryOffNeverDifferInVerdict()
    {
        ulong state = 0x6C62272E07BB0142UL;
        int satisfiableSeen = 0;
        int unsatisfiableSeen = 0;

        for(int round = 0; round < 300; round++)
        {
            int variableCount = 1 + (int)(Next(ref state) % 8);
            int clauseCount = 1 + (int)(Next(ref state) % 16);
            List<IReadOnlyList<SatLiteral>> clauses = BuildFormula(ref state, variableCount, clauseCount);

            using SatSolverSession carryOn = new(clauses, variableCount);
            using SatSolverSession carryOff = new(clauses, variableCount, reduceThreshold: SatSolverSession.DeletionDisabled, carryPhases: false);

            int sequenceLength = 5 + (int)(Next(ref state) % 16);
            for(int call = 0; call < sequenceLength; call++)
            {
                List<SatLiteral> assumptions = BuildAssumptions(ref state, variableCount);

                SatVerdict onVerdict = carryOn.Solve(assumptions, cancellationToken: TestContext.CancellationToken);
                SatVerdict offVerdict = carryOff.Solve(assumptions, cancellationToken: TestContext.CancellationToken);
                SatVerdict oracle = SatSolver.SolveUnderAssumptions(clauses, variableCount, assumptions, mode: SatSearchMode.WatchedLearning, cancellationToken: TestContext.CancellationToken);

                Assert.AreEqual(oracle.IsSatisfiable, onVerdict.IsSatisfiable, $"Round {round} call {call}: the carry-on session disagrees with the oracle.");
                Assert.AreEqual(oracle.IsSatisfiable, offVerdict.IsSatisfiable, $"Round {round} call {call}: the carry-off session disagrees with the oracle.");

                if(oracle.IsSatisfiable)
                {
                    satisfiableSeen++;

                    //Each policy independently returns a valid model: carry-off re-seeds
                    //polarity each solve, carry-on reuses it, yet both satisfy the formula
                    //and honour every assumption.
                    Assert.IsNotNull(onVerdict.Assignment);
                    Assert.IsNotNull(offVerdict.Assignment);
                    Assert.IsTrue(Satisfies(clauses, onVerdict.Assignment), $"Round {round} call {call}: the carry-on model does not satisfy the formula.");
                    Assert.IsTrue(Satisfies(clauses, offVerdict.Assignment), $"Round {round} call {call}: the carry-off model does not satisfy the formula.");
                    foreach(SatLiteral assumption in assumptions)
                    {
                        Assert.AreEqual(assumption.IsPositive, onVerdict.Assignment[assumption.Variable], $"Round {round} call {call}: the carry-on model violates an assumption.");
                        Assert.AreEqual(assumption.IsPositive, offVerdict.Assignment[assumption.Variable], $"Round {round} call {call}: the carry-off model violates an assumption.");
                    }
                }
                else
                {
                    unsatisfiableSeen++;
                }
            }
        }

        Assert.IsGreaterThan(20, satisfiableSeen, "The sweep covers satisfiable solves.");
        Assert.IsGreaterThan(20, unsatisfiableSeen, "The sweep covers unsatisfiable-under-assumptions solves.");
    }

    /// <summary>Finds a globally-satisfiable random 3-SAT formula by trying deterministic instances until one is satisfiable on its own.</summary>
    /// <param name="state">The deterministic generator state.</param>
    /// <param name="variableCount">The variable count.</param>
    /// <param name="clauseCount">The clause count.</param>
    /// <returns>A satisfiable formula.</returns>
    private List<IReadOnlyList<SatLiteral>> FindSatisfiableFormula(ref ulong state, int variableCount, int clauseCount)
    {
        List<IReadOnlyList<SatLiteral>> clauses = [];
        bool satisfiable = false;
        for(int attempt = 0; attempt < 200 && !satisfiable; attempt++)
        {
            clauses = BuildRandom3Sat(ref state, variableCount, clauseCount);
            satisfiable = SatSolver.Solve(clauses, variableCount, cancellationToken: TestContext.CancellationToken).IsSatisfiable;
        }

        Assert.IsTrue(satisfiable, "No satisfiable base formula found.");

        return clauses;
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

    /// <summary>Builds the pigeonhole formula: <paramref name="holes"/>+1 pigeons into <paramref name="holes"/> holes, unsatisfiable, each pigeon in some hole and no two sharing a hole.</summary>
    /// <param name="holes">The number of holes.</param>
    /// <returns>The clauses and the variable count; variable <c>pigeon * holes + hole</c> places that pigeon in that hole.</returns>
    private static (List<IReadOnlyList<SatLiteral>> Clauses, int VariableCount) Pigeonhole(int holes)
    {
        int pigeons = holes + 1;
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

    /// <summary>Builds a random CNF formula: <paramref name="clauseCount"/> clauses of width 1–3 over <paramref name="variableCount"/> variables.</summary>
    /// <param name="state">The deterministic generator state.</param>
    /// <param name="variableCount">The variable count.</param>
    /// <param name="clauseCount">The clause count.</param>
    /// <returns>The clauses.</returns>
    private static List<IReadOnlyList<SatLiteral>> BuildFormula(ref ulong state, int variableCount, int clauseCount)
    {
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

        return clauses;
    }

    /// <summary>Builds one random clause of width 1–3 over the variable range.</summary>
    /// <param name="state">The deterministic generator state.</param>
    /// <param name="variableCount">The variable count.</param>
    /// <returns>The clause.</returns>
    private static SatLiteral[] BuildClause(ref ulong state, int variableCount)
    {
        int width = 1 + (int)(Next(ref state) % 3);
        SatLiteral[] clause = new SatLiteral[width];
        for(int j = 0; j < width; j++)
        {
            clause[j] = new SatLiteral((int)(Next(ref state) % (uint)variableCount), (Next(ref state) & 1) == 0);
        }

        return clause;
    }

    /// <summary>Builds a random assumption set of 0–<paramref name="variableCount"/> literals.</summary>
    /// <param name="state">The deterministic generator state.</param>
    /// <param name="variableCount">The variable count.</param>
    /// <returns>The assumptions.</returns>
    private static List<SatLiteral> BuildAssumptions(ref ulong state, int variableCount)
    {
        int count = (int)(Next(ref state) % (uint)(variableCount + 1));
        List<SatLiteral> assumptions = [];
        for(int i = 0; i < count; i++)
        {
            assumptions.Add(new SatLiteral((int)(Next(ref state) % (uint)variableCount), (Next(ref state) & 1) == 0));
        }

        return assumptions;
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
