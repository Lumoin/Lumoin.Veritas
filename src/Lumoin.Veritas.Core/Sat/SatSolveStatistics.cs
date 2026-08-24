using System;

namespace Lumoin.Veritas.Core.Sat;

/// <summary>
/// The work a single satisfiability run spent reaching its verdict: the
/// branch decisions taken, unit propagations forced, conflicts hit, clauses
/// learned, and the deepest decision level reached. It is the measurement
/// payload a caller folds across the many world solves of one reasoning
/// decision, so "is the solver the cost" is answered from data rather than a
/// hunt.
/// </summary>
/// <remarks>
/// <para>
/// The counters are honest to the <see cref="SatSearchMode"/> that produced
/// them: <see cref="SatSearchMode.PropagationOnly"/> never learns a clause,
/// so its <see cref="LearnedClauses"/> is always zero and its
/// <see cref="MaxDecisionLevel"/> is the chronological-backtracking decision
/// depth; <see cref="SatSearchMode.ConflictLearning"/> records both. A direct
/// assumption contradiction decided before any search returns
/// <see cref="Empty"/> — no decision, propagation, or conflict was needed.
/// </para>
/// </remarks>
/// <param name="Decisions">The branch decisions taken — each time the search guessed a value for an unassigned variable, including the second branch of a chronological flip.</param>
/// <param name="Propagations">The literals forced by unit propagation across the whole run.</param>
/// <param name="Conflicts">The conflicts the search hit, including the terminal conflict that proved unsatisfiability.</param>
/// <param name="LearnedClauses">The first-UIP clauses added to the database; always zero under <see cref="SatSearchMode.PropagationOnly"/>.</param>
/// <param name="MaxDecisionLevel">The deepest decision level the search reached.</param>
/// <param name="Restarts">The restarts the search took — each time it abandoned the current trail back to the assumption prefix and resumed from the reused learned clauses, variable order, and saved phases. Only the restart-driven engines report it; the scan engines never restart, so their count is always zero.</param>
public readonly record struct SatSolveStatistics(
    int Decisions,
    long Propagations,
    int Conflicts,
    int LearnedClauses,
    int MaxDecisionLevel,
    int Restarts = 0)
{
    /// <summary>The zero counters: the statistics of a run that took no search step.</summary>
    public static SatSolveStatistics Empty => default;

    /// <summary>
    /// Folds two runs' statistics into one running total — counters sum,
    /// the decision level takes the deeper of the two — for accumulating
    /// across the world solves of one reasoning decision.
    /// </summary>
    /// <param name="first">The running total so far.</param>
    /// <param name="second">The next run's statistics.</param>
    /// <returns>The combined statistics.</returns>
    public static SatSolveStatistics Combine(in SatSolveStatistics first, in SatSolveStatistics second)
    {
        return new SatSolveStatistics(
            first.Decisions + second.Decisions,
            first.Propagations + second.Propagations,
            first.Conflicts + second.Conflicts,
            first.LearnedClauses + second.LearnedClauses,
            Math.Max(first.MaxDecisionLevel, second.MaxDecisionLevel),
            first.Restarts + second.Restarts);
    }
}
