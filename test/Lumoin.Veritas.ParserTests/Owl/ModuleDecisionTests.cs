using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sat;
using Lumoin.Veritas.Owl.Reasoning;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Tests for the description-logic decision vocabulary: the work-based
/// <see cref="ReasoningBudget"/> and its exhaustion predicate, the empty
/// <see cref="ReasoningDecisionStatistics"/>, and the
/// <see cref="ModuleDecision"/> factories' consistent outcome/verdict
/// pairings.
/// </summary>
[TestClass]
internal sealed class ModuleDecisionTests
{
    /// <summary>The unbounded budget never reports exhaustion, whatever the spend.</summary>
    [TestMethod]
    public void UnboundedBudgetNeverExhausts()
    {
        SatSolveStatistics totals = new(Decisions: 1000, Propagations: 9000, Conflicts: 5000, LearnedClauses: 100, MaxDecisionLevel: 40);

        Assert.IsFalse(ReasoningBudget.Unbounded.IsExhaustedBy(solveCount: 100000, totals));
    }

    /// <summary>The solve bound triggers at and beyond its ceiling, and a zero solve bound never triggers on solves.</summary>
    [TestMethod]
    public void SolveBoundTriggersAtItsCeiling()
    {
        ReasoningBudget budget = new(MaxSolves: 10, MaxConflicts: 0);

        Assert.IsFalse(budget.IsExhaustedBy(solveCount: 9, SatSolveStatistics.Empty));
        Assert.IsTrue(budget.IsExhaustedBy(solveCount: 10, SatSolveStatistics.Empty));
        Assert.IsTrue(budget.IsExhaustedBy(solveCount: 11, SatSolveStatistics.Empty));
    }

    /// <summary>The conflict bound triggers on accumulated solver conflicts independent of the solve count.</summary>
    [TestMethod]
    public void ConflictBoundTriggersOnAccumulatedConflicts()
    {
        ReasoningBudget budget = new(MaxSolves: 0, MaxConflicts: 50);
        SatSolveStatistics under = new(Decisions: 0, Propagations: 0, Conflicts: 49, LearnedClauses: 0, MaxDecisionLevel: 0);
        SatSolveStatistics over = new(Decisions: 0, Propagations: 0, Conflicts: 50, LearnedClauses: 0, MaxDecisionLevel: 0);

        Assert.IsFalse(budget.IsExhaustedBy(solveCount: 100, under));
        Assert.IsTrue(budget.IsExhaustedBy(solveCount: 1, over));
    }

    /// <summary>Either bound alone is enough to exhaust the budget.</summary>
    [TestMethod]
    public void EitherBoundExhaustsTheBudget()
    {
        ReasoningBudget budget = new(MaxSolves: 10, MaxConflicts: 50);
        SatSolveStatistics fewConflicts = new(Decisions: 0, Propagations: 0, Conflicts: 1, LearnedClauses: 0, MaxDecisionLevel: 0);
        SatSolveStatistics manyConflicts = new(Decisions: 0, Propagations: 0, Conflicts: 60, LearnedClauses: 0, MaxDecisionLevel: 0);

        Assert.IsTrue(budget.IsExhaustedBy(solveCount: 10, fewConflicts), "Solve bound reached.");
        Assert.IsTrue(budget.IsExhaustedBy(solveCount: 1, manyConflicts), "Conflict bound reached.");
        Assert.IsFalse(budget.IsExhaustedBy(solveCount: 1, fewConflicts), "Neither bound reached.");
    }

    /// <summary>The empty statistics are the zero counters.</summary>
    [TestMethod]
    public void EmptyStatisticsAreZero()
    {
        ReasoningDecisionStatistics empty = ReasoningDecisionStatistics.Empty;

        Assert.AreEqual(0, empty.ModuleAxiomCount);
        Assert.AreEqual(0, empty.SolveCount);
        Assert.AreEqual(SatSolveStatistics.Empty, empty.SolverTotals);
    }

    /// <summary>A decided result carries its verdict under the decided outcome.</summary>
    [TestMethod]
    public void DecidedCarriesTheVerdict()
    {
        ModuleVerdict verdict = new(IsConsistent: true, Subsumptions: []);
        ReasoningDecisionStatistics statistics = new(ModuleAxiomCount: 3, SolveCount: 2, new SatSolveStatistics(1, 4, 0, 0, 2));

        ModuleDecision decision = ModuleDecision.Decided(verdict, statistics);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome);
        Assert.IsNotNull(decision.Verdict);
        Assert.IsTrue(decision.Verdict.IsConsistent);
        Assert.AreEqual(statistics, decision.Statistics);
    }

    /// <summary>An abstaining result carries no verdict under the budget-abstention outcome, but keeps the work it spent.</summary>
    [TestMethod]
    public void AbstainedOnBudgetCarriesNoVerdict()
    {
        ReasoningDecisionStatistics statistics = new(ModuleAxiomCount: 9, SolveCount: 5000, new SatSolveStatistics(4000, 90000, 5000, 800, 60));

        ModuleDecision decision = ModuleDecision.AbstainedOnBudget(statistics);

        Assert.AreEqual(ReasoningDecisionOutcome.AbstainedBudget, decision.Outcome);
        Assert.IsNull(decision.Verdict);
        Assert.AreEqual(statistics, decision.Statistics);
    }
}
