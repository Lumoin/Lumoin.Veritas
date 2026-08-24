using System;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The work the snapshot tableau engine spent on one decision: the tableau
/// runs it started — one for the consistency check and one per
/// subsumption-pair probe — and, summed across them, the rules it applied,
/// the disjunction branches it opened, the clashes it backtracked from, and
/// the largest forest it reached. It is the snapshot engine's counterpart to
/// <see cref="Lumoin.Veritas.Core.Sat.SatSolveStatistics"/>: where the
/// SAT-backed engine reports solver counters, this engine reports tableau
/// counters, and a <see cref="ReasoningDecisionStatistics"/> carries whichever
/// the deciding engine produced.
/// </summary>
/// <param name="TableauRuns">The number of tableau runs — one consistency check plus one per subsumption-pair probe.</param>
/// <param name="RuleApplications">The deterministic and branching rule applications summed across the runs.</param>
/// <param name="Branches">The disjunction branch points opened across the runs.</param>
/// <param name="Clashes">The clashes the search backtracked from across the runs.</param>
/// <param name="MaxNodes">The largest node forest any run reached.</param>
public readonly record struct AlcTableauStatistics(
    int TableauRuns,
    long RuleApplications,
    int Branches,
    int Clashes,
    int MaxNodes)
{
    /// <summary>The zero counters: the statistics of a decision that ran no tableau.</summary>
    public static AlcTableauStatistics Empty => default;

    /// <summary>
    /// Folds two runs' statistics into one running total — counters sum, the
    /// node count takes the larger of the two — for accumulating across the
    /// tableau runs of one decision.
    /// </summary>
    /// <param name="first">The running total so far.</param>
    /// <param name="second">The next run's statistics.</param>
    /// <returns>The combined statistics.</returns>
    public static AlcTableauStatistics Combine(in AlcTableauStatistics first, in AlcTableauStatistics second)
    {
        return new AlcTableauStatistics(
            first.TableauRuns + second.TableauRuns,
            first.RuleApplications + second.RuleApplications,
            first.Branches + second.Branches,
            first.Clashes + second.Clashes,
            Math.Max(first.MaxNodes, second.MaxNodes));
    }
}
