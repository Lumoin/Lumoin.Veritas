using Lumoin.Veritas.Core.Sat;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// A work-based bound on one reasoning decision: the most world solves and
/// accumulated solver conflicts the SAT search may spend, the most rule
/// attempts of the deciding calculus — a saturation engine's rule attempts or a
/// tableau engine's rule applications — and the most clauses the decision may
/// insert, before the engine abstains with a reason rather than search without
/// end. It is the budget half of budget-with-reason — a wall-clock deadline is
/// already expressible through the decision's cancellation token, so this bounds
/// the work a token cannot see (the many world solves or rule applications of one
/// decision), not the time. Every axis is a counter the engine already maintains,
/// so a bounded decision reads no clock and no allocator.
/// </summary>
/// <remarks>
/// <para>
/// A zero bound is unbounded on that axis, so <see cref="Unbounded"/> places
/// no limit at all and the decision runs until it decides or the token
/// fires. The bounds are inclusive ceilings: a decision is over budget once
/// it reaches them, so the engine checks between units of work and stops
/// before starting the solve or rule application that would exceed the budget.
/// A budget must therefore exceed a decision's measured rule-attempt need by at
/// least one unit to guarantee the decision completes rather than abstains.
/// </para>
/// <para>
/// The budget rides only the surfaces that can carry its outcome: a
/// <see cref="ModuleVerdict"/>-returning reasoner surface is unbounded-only —
/// its shape has no abstention slot — so a <see cref="ReasoningBudget"/> appears
/// only on <see cref="ModuleDecision"/>-returning surfaces, whose outcome can
/// carry the budget abstention.
/// </para>
/// </remarks>
/// <param name="MaxSolves">The most world solves the decision may spend, or zero for no limit on solves.</param>
/// <param name="MaxConflicts">The most accumulated solver conflicts the decision may spend, or zero for no limit on conflicts.</param>
/// <param name="MaxInferences">The most rule attempts of the deciding calculus the decision may spend — a saturation engine's rule attempts (every conclusion offered, productive or redundant) or a tableau engine's rule applications — or zero for no limit on rule attempts.</param>
/// <param name="MaxDerivedClauses">The most clauses the decision may insert in total — every clause registered into a context, summed over the whole decision — or zero for no limit on the clause population. This is the memory-faithful axis: the registration structures a saturation carries grow with the clauses ever inserted, not with the clauses still live and not with the largest single context, so a bound on total insertions is the one that bounds the decision's footprint.</param>
public readonly record struct ReasoningBudget(int MaxSolves, int MaxConflicts, int MaxInferences, int MaxDerivedClauses = 0)
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

    /// <summary>
    /// Whether a decision that has spent <paramref name="ruleAttempts"/>
    /// rule attempts of the deciding calculus has reached its bound and must
    /// abstain. A zero bound never triggers; the bound is an inclusive ceiling,
    /// so the decision is over budget once its attempts reach it.
    /// </summary>
    /// <param name="ruleAttempts">The rule attempts of the deciding calculus the decision has spent so far — a saturation engine's rule attempts or a tableau engine's rule applications.</param>
    /// <returns><c>true</c> when the bound is reached.</returns>
    public bool IsExhaustedByInferences(long ruleAttempts)
    {
        return MaxInferences > 0 && ruleAttempts >= MaxInferences;
    }

    /// <summary>
    /// Whether a decision that has inserted <paramref name="derivedClauses"/>
    /// clauses has reached its population bound and must abstain. A zero bound
    /// never triggers; the bound is an inclusive ceiling, so the decision is over
    /// budget once its insertions reach it. The engine reads it between charged
    /// units of work, exactly as it reads the attempt axis, so a unit that seeds a
    /// bounded burst of clauses may carry the recorded population past the ceiling
    /// before the next check.
    /// </summary>
    /// <param name="derivedClauses">The clauses the decision has inserted so far, summed over its rounds.</param>
    /// <returns><c>true</c> when the bound is reached.</returns>
    public bool IsExhaustedByPopulation(int derivedClauses)
    {
        return MaxDerivedClauses > 0 && derivedClauses >= MaxDerivedClauses;
    }
}
