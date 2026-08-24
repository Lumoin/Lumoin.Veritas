using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sat;
using Lumoin.Veritas.Owl.Reasoning;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Tests for the description-logic decision vocabulary: the work-based
/// <see cref="ReasoningBudget"/> and its exhaustion predicates, the empty
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
        ReasoningBudget budget = new(MaxSolves: 10, MaxConflicts: 0, MaxInferences: 0);

        Assert.IsFalse(budget.IsExhaustedBy(solveCount: 9, SatSolveStatistics.Empty));
        Assert.IsTrue(budget.IsExhaustedBy(solveCount: 10, SatSolveStatistics.Empty));
        Assert.IsTrue(budget.IsExhaustedBy(solveCount: 11, SatSolveStatistics.Empty));
    }

    /// <summary>The conflict bound triggers on accumulated solver conflicts independent of the solve count.</summary>
    [TestMethod]
    public void ConflictBoundTriggersOnAccumulatedConflicts()
    {
        ReasoningBudget budget = new(MaxSolves: 0, MaxConflicts: 50, MaxInferences: 0);
        SatSolveStatistics under = new(Decisions: 0, Propagations: 0, Conflicts: 49, LearnedClauses: 0, MaxDecisionLevel: 0);
        SatSolveStatistics over = new(Decisions: 0, Propagations: 0, Conflicts: 50, LearnedClauses: 0, MaxDecisionLevel: 0);

        Assert.IsFalse(budget.IsExhaustedBy(solveCount: 100, under));
        Assert.IsTrue(budget.IsExhaustedBy(solveCount: 1, over));
    }

    /// <summary>Either bound alone is enough to exhaust the budget.</summary>
    [TestMethod]
    public void EitherBoundExhaustsTheBudget()
    {
        ReasoningBudget budget = new(MaxSolves: 10, MaxConflicts: 50, MaxInferences: 0);
        SatSolveStatistics fewConflicts = new(Decisions: 0, Propagations: 0, Conflicts: 1, LearnedClauses: 0, MaxDecisionLevel: 0);
        SatSolveStatistics manyConflicts = new(Decisions: 0, Propagations: 0, Conflicts: 60, LearnedClauses: 0, MaxDecisionLevel: 0);

        Assert.IsTrue(budget.IsExhaustedBy(solveCount: 10, fewConflicts), "Solve bound reached.");
        Assert.IsTrue(budget.IsExhaustedBy(solveCount: 1, manyConflicts), "Conflict bound reached.");
        Assert.IsFalse(budget.IsExhaustedBy(solveCount: 1, fewConflicts), "Neither bound reached.");
    }

    /// <summary>A zero population bound never exhausts, whatever the clause population — and an omitted population axis IS the zero bound, so a budget built from the three older axes alone stays unbounded on the population.</summary>
    [TestMethod]
    public void PopulationBudgetZeroNeverExhausts()
    {
        Assert.IsFalse(ReasoningBudget.Unbounded.IsExhaustedByPopulation(int.MaxValue), "The unbounded budget places no population limit.");

        ReasoningBudget otherAxesOnly = new(MaxSolves: 10, MaxConflicts: 50, MaxInferences: 100);

        Assert.IsFalse(otherAxesOnly.IsExhaustedByPopulation(int.MaxValue), "An omitted population axis is the zero bound, which never triggers.");
    }

    /// <summary>The population bound is an inclusive ceiling: below it the decision may continue, at it and beyond it the decision is over budget.</summary>
    [TestMethod]
    public void PopulationBudgetIsAnInclusiveCeiling()
    {
        ReasoningBudget budget = new(MaxSolves: 0, MaxConflicts: 0, MaxInferences: 0, MaxDerivedClauses: 10);

        Assert.IsFalse(budget.IsExhaustedByPopulation(9), "One clause below the ceiling the decision may continue.");
        Assert.IsTrue(budget.IsExhaustedByPopulation(10), "Reaching the ceiling exhausts the axis: the bound is inclusive.");
        Assert.IsTrue(budget.IsExhaustedByPopulation(11), "Past the ceiling the axis stays exhausted.");
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

    /// <summary>A consistent verdict naming an excluded construct is fragment-relative: the factory derives <see cref="ReasoningDecisionOutcome.DecidedFragmentRelative"/> from the verdict's decisiveness, and the verdict is not decisive.</summary>
    [TestMethod]
    public void ConsistentVerdictWithRemainderIsFragmentRelative()
    {
        ModuleVerdict verdict = new(IsConsistent: true, Subsumptions: []) { UnsupportedConstructs = ["OwlHasKeyAxiom"] };
        ReasoningDecisionStatistics statistics = new(ModuleAxiomCount: 4, SolveCount: 1, new SatSolveStatistics(2, 6, 0, 0, 3));

        ModuleDecision decision = ModuleDecision.Decided(verdict, statistics);

        Assert.AreEqual(ReasoningDecisionOutcome.DecidedFragmentRelative, decision.Outcome);
        Assert.IsNotNull(decision.Verdict);
        Assert.IsFalse(decision.Verdict.IsDecisive, "A consistent verdict with a named remainder is scoped to the supported fragment.");
    }

    /// <summary>An inconsistent verdict carrying a non-empty remainder still records <see cref="ReasoningDecisionOutcome.Decided"/>: the condemnation covers the module whole, so the verdict is decisive despite the remainder.</summary>
    [TestMethod]
    public void InconsistentVerdictWithRemainderCondemnsTheModuleWhole()
    {
        ModuleVerdict verdict = new(IsConsistent: false, Subsumptions: []) { UnsupportedConstructs = ["OwlHasKeyAxiom"] };
        ReasoningDecisionStatistics statistics = new(ModuleAxiomCount: 4, SolveCount: 1, new SatSolveStatistics(2, 6, 1, 0, 3));

        ModuleDecision decision = ModuleDecision.Decided(verdict, statistics);

        Assert.AreEqual(ReasoningDecisionOutcome.Decided, decision.Outcome);
        Assert.IsNotNull(decision.Verdict);
        Assert.IsTrue(decision.Verdict.IsDecisive, "An inconsistency in the supported fragment condemns the module regardless of the remainder.");
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
