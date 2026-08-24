using System.Collections.Generic;
using Lumoin.Veritas.Core.Sat;

namespace Lumoin.Veritas.Tests.Sat;

/// <summary>
/// Tests for <see cref="SatSolveStatistics"/> and the counters
/// <see cref="SatSolver"/> reports on its verdict: the empty and combine
/// algebra, the counters a propagation-decided and a branch-decided run
/// produce, the mode-honest learned-clause count, and the empty statistics of
/// a verdict reached before any search step.
/// </summary>
[TestClass]
internal sealed class SatSolveStatisticsTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The empty statistics are all zero.</summary>
    [TestMethod]
    public void EmptyIsAllZero()
    {
        SatSolveStatistics empty = SatSolveStatistics.Empty;

        Assert.AreEqual(0, empty.Decisions);
        Assert.AreEqual(0L, empty.Propagations);
        Assert.AreEqual(0, empty.Conflicts);
        Assert.AreEqual(0, empty.LearnedClauses);
        Assert.AreEqual(0, empty.MaxDecisionLevel);
        Assert.AreEqual(0, empty.Restarts);
    }

    /// <summary>Combine sums the counters and takes the deeper decision level.</summary>
    [TestMethod]
    public void CombineSumsCountersAndMaxesTheDecisionLevel()
    {
        SatSolveStatistics first = new(Decisions: 2, Propagations: 5, Conflicts: 1, LearnedClauses: 0, MaxDecisionLevel: 3, Restarts: 1);
        SatSolveStatistics second = new(Decisions: 4, Propagations: 7, Conflicts: 3, LearnedClauses: 2, MaxDecisionLevel: 2, Restarts: 2);

        SatSolveStatistics combined = SatSolveStatistics.Combine(first, second);

        Assert.AreEqual(6, combined.Decisions);
        Assert.AreEqual(12L, combined.Propagations);
        Assert.AreEqual(4, combined.Conflicts);
        Assert.AreEqual(2, combined.LearnedClauses);
        Assert.AreEqual(3, combined.MaxDecisionLevel);
        Assert.AreEqual(3, combined.Restarts);
    }

    /// <summary>Combining with the empty statistics is the identity.</summary>
    [TestMethod]
    public void CombineWithEmptyIsIdentity()
    {
        SatSolveStatistics stats = new(Decisions: 2, Propagations: 5, Conflicts: 1, LearnedClauses: 0, MaxDecisionLevel: 3);

        Assert.AreEqual(stats, SatSolveStatistics.Combine(stats, SatSolveStatistics.Empty));
        Assert.AreEqual(stats, SatSolveStatistics.Combine(SatSolveStatistics.Empty, stats));
    }

    /// <summary>A formula decided by pure unit propagation reports propagations and no branch decisions.</summary>
    [TestMethod]
    public void PropagationDecidedRunReportsPropagationsAndNoDecisions()
    {
        //A single unit clause forces its variable; nothing branches.
        List<IReadOnlyList<SatLiteral>> clauses = [[new SatLiteral(0, IsPositive: true)]];

        SatVerdict verdict = SatSolver.Solve(clauses, variableCount: 1, cancellationToken: TestContext.CancellationToken);

        Assert.IsTrue(verdict.IsSatisfiable);
        Assert.AreEqual(0, verdict.Statistics.Decisions);
        Assert.AreEqual(0, verdict.Statistics.Conflicts);
        Assert.AreEqual(0, verdict.Statistics.LearnedClauses);
        Assert.IsGreaterThanOrEqualTo(1L, verdict.Statistics.Propagations);
    }

    /// <summary>Contradicting unit clauses are decided by one conflict at level zero, with no branching.</summary>
    [TestMethod]
    public void ContradictionByPropagationReportsAConflictAndNoDecisions()
    {
        List<IReadOnlyList<SatLiteral>> clauses =
        [
            [new SatLiteral(0, IsPositive: true)],
            [new SatLiteral(0, IsPositive: false)],
        ];

        SatVerdict verdict = SatSolver.Solve(clauses, variableCount: 1, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(verdict.IsSatisfiable);
        Assert.AreEqual(0, verdict.Statistics.Decisions);
        Assert.IsGreaterThanOrEqualTo(1, verdict.Statistics.Conflicts);
    }

    /// <summary>The branch-decided pigeonhole refutation reports branching, conflicts, and a positive decision depth in both modes.</summary>
    /// <param name="mode">The search mode under test.</param>
    [TestMethod]
    [DataRow(SatSearchMode.PropagationOnly)]
    [DataRow(SatSearchMode.ConflictLearning)]
    public void PigeonholeReportsBranchingAndConflicts(SatSearchMode mode)
    {
        SatVerdict verdict = SatSolver.Solve(Pigeonhole(), variableCount: 6, mode: mode, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(verdict.IsSatisfiable);
        Assert.IsGreaterThan(0, verdict.Statistics.Decisions);
        Assert.IsGreaterThan(0, verdict.Statistics.Conflicts);
        Assert.IsGreaterThan(0, verdict.Statistics.MaxDecisionLevel);
    }

    /// <summary>The propagation-only mode never learns a clause; the conflict-learning mode does on a branch-decided refutation.</summary>
    [TestMethod]
    public void LearnedClauseCountIsHonestToTheSearchMode()
    {
        SatVerdict propagationOnly = SatSolver.Solve(Pigeonhole(), variableCount: 6, mode: SatSearchMode.PropagationOnly, cancellationToken: TestContext.CancellationToken);
        SatVerdict conflictLearning = SatSolver.Solve(Pigeonhole(), variableCount: 6, mode: SatSearchMode.ConflictLearning, cancellationToken: TestContext.CancellationToken);

        Assert.AreEqual(0, propagationOnly.Statistics.LearnedClauses);
        Assert.IsGreaterThan(0, conflictLearning.Statistics.LearnedClauses);
    }

    /// <summary>A verdict reached before any search step — a direct assumption contradiction — carries the empty statistics.</summary>
    [TestMethod]
    public void DirectAssumptionContradictionReportsEmptyStatistics()
    {
        List<SatLiteral> assumptions = [new SatLiteral(0, IsPositive: true), new SatLiteral(0, IsPositive: false)];

        SatVerdict verdict = SatSolver.SolveUnderAssumptions([], variableCount: 1, assumptions, cancellationToken: TestContext.CancellationToken);

        Assert.IsFalse(verdict.IsSatisfiable);
        Assert.AreEqual(SatSolveStatistics.Empty, verdict.Statistics);
    }

    /// <summary>Three pigeons into two holes: each pigeon somewhere, no two sharing a hole. Variable p*2 + h means pigeon p sits in hole h.</summary>
    /// <returns>The unsatisfiable pigeonhole clauses over six variables.</returns>
    private static List<IReadOnlyList<SatLiteral>> Pigeonhole()
    {
        List<IReadOnlyList<SatLiteral>> clauses = [];
        for(int pigeon = 0; pigeon < 3; pigeon++)
        {
            clauses.Add([new SatLiteral(pigeon * 2, IsPositive: true), new SatLiteral((pigeon * 2) + 1, IsPositive: true)]);
        }

        for(int hole = 0; hole < 2; hole++)
        {
            for(int first = 0; first < 3; first++)
            {
                for(int second = first + 1; second < 3; second++)
                {
                    clauses.Add([new SatLiteral((first * 2) + hole, IsPositive: false), new SatLiteral((second * 2) + hole, IsPositive: false)]);
                }
            }
        }

        return clauses;
    }
}
