using Lumoin.Veritas.Core.Hypertrie.Query;
using System;

namespace Lumoin.Veritas.Core.Hypertrie.Planning;

/// <summary>
/// The decision a <see cref="Planner"/> returns at one
/// consultation point: descend by a chosen variable, skip the
/// current branch, yield the current binding as a solution, or
/// stop the query entirely.
/// </summary>
/// <remarks>
/// <para>
/// Modelled as a tagged <c>readonly record struct</c>:
/// <see cref="Kind"/> selects the variant and per-variant
/// payload fields are populated or defaulted accordingly. The
/// factory methods (<see cref="DescendVariable"/>,
/// <see cref="SkipBranch"/>, <see cref="YieldSolution"/>,
/// <see cref="StopQuery"/>) enforce correct population.
/// </para>
/// <para>
/// Equality is value-based across all fields including the
/// inactive payload, but the factories zero the inactive
/// payload so this is not a concern in practice.
/// </para>
/// </remarks>
public readonly record struct PlannerDecision
{
    /// <summary>The active discriminator.</summary>
    public PlannerDecisionKind Kind { get; init; }

    /// <summary>For <see cref="PlannerDecisionKind.DescendVariable"/>: the variable to descend by.</summary>
    public Variable Variable { get; init; }

    /// <summary>
    /// Constructs a <see cref="PlannerDecisionKind.DescendVariable"/>
    /// decision instructing the driver to bind the next level
    /// to <paramref name="variable"/>.
    /// </summary>
    public static PlannerDecision DescendVariable(Variable variable) => new()
    {
        Kind = PlannerDecisionKind.DescendVariable,
        Variable = variable,
    };

    /// <summary>
    /// Constructs a <see cref="PlannerDecisionKind.SkipBranch"/>
    /// decision instructing the driver to abandon the current
    /// branch.
    /// </summary>
    public static PlannerDecision SkipBranch() => new()
    {
        Kind = PlannerDecisionKind.SkipBranch,
        Variable = default,
    };

    /// <summary>
    /// Constructs a <see cref="PlannerDecisionKind.YieldSolution"/>
    /// decision instructing the driver to emit the current
    /// binding as a solution.
    /// </summary>
    public static PlannerDecision YieldSolution() => new()
    {
        Kind = PlannerDecisionKind.YieldSolution,
        Variable = default,
    };

    /// <summary>
    /// Constructs a <see cref="PlannerDecisionKind.StopQuery"/>
    /// decision instructing the driver to halt query execution.
    /// </summary>
    public static PlannerDecision StopQuery() => new()
    {
        Kind = PlannerDecisionKind.StopQuery,
        Variable = default,
    };

    /// <summary>
    /// Returns the variable the planner chose to descend by, or
    /// throws if this decision is not a
    /// <see cref="PlannerDecisionKind.DescendVariable"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">This decision is not a DescendVariable decision.</exception>
    public Variable AsDescendVariable()
    {
        if(Kind != PlannerDecisionKind.DescendVariable)
        {
            throw new InvalidOperationException($"PlannerDecision is {Kind}, not DescendVariable.");
        }

        return Variable;
    }
}
