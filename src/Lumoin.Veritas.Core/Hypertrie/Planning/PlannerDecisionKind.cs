using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Core.Hypertrie.Planning;

/// <summary>
/// Discriminator for the union cases of
/// <see cref="PlannerDecision"/>.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1028:Enum storage should be Int32",
    Justification = "PlannerDecisionKind is a discriminator stored alongside other fields inside the PlannerDecision value type. The byte underlying type keeps the discriminator at one byte and packs cleanly with the decision's payload fields. Four values fit trivially.")]
public enum PlannerDecisionKind: byte
{
    /// <summary>Descend by binding the next variable. Carries the variable to descend by.</summary>
    DescendVariable = 0,

    /// <summary>Skip the current branch — the planner has determined further descent here cannot produce solutions. Carries no payload.</summary>
    SkipBranch = 1,

    /// <summary>Yield the current binding as a solution. Used at terminal levels of the variable order. Carries no payload.</summary>
    YieldSolution = 2,

    /// <summary>Stop the query entirely — the planner has determined no further solutions are possible. Carries no payload.</summary>
    StopQuery = 3
}
