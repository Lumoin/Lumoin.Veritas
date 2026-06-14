using Lumoin.Veritas.Core.Sat;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// A work-based bound on one reasoning decision: the most world solves and
/// accumulated solver conflicts the engine may spend before it abstains with
/// a reason rather than search without end. It is the budget half of
/// budget-with-reason — a wall-clock deadline is already expressible through
/// the decision's cancellation token, so this bounds the work a token cannot
/// see (the many world solves of one decision), not the time.
/// </summary>
/// <remarks>
/// A zero bound is unbounded on that axis, so <see cref="Unbounded"/> places
/// no limit at all and the decision runs until it decides or the token
/// fires. The bounds are inclusive ceilings: a decision is over budget once
/// it reaches them, so the engine checks between world solves and stops
/// before starting the solve that would exceed the budget.
/// </remarks>
/// <param name="MaxSolves">The most world solves the decision may spend, or zero for no limit on solves.</param>
/// <param name="MaxConflicts">The most accumulated solver conflicts the decision may spend, or zero for no limit on conflicts.</param>
public readonly record struct ReasoningBudget(int MaxSolves, int MaxConflicts)
{
    /// <summary>The budget that bounds nothing: the decision runs until it decides or its token fires.</summary>
    public static ReasoningBudget Unbounded => default;

    /// <summary>
    /// Whether a decision that has spent <paramref name="solveCount"/> world
    /// solves accumulating <paramref name="solverTotals"/> has reached a
    /// bound and must abstain. A zero bound never triggers.
    /// </summary>
    /// <param name="solveCount">The world solves the decision has spent so far.</param>
    /// <param name="solverTotals">The solver counters accumulated so far.</param>
    /// <returns><c>true</c> when a bound is reached.</returns>
    public bool IsExhaustedBy(int solveCount, in SatSolveStatistics solverTotals)
    {
        return (MaxSolves > 0 && solveCount >= MaxSolves)
            || (MaxConflicts > 0 && solverTotals.Conflicts >= MaxConflicts);
    }
}
