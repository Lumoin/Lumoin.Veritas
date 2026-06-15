using Lumoin.Veritas.Core.Sat;
using Lumoin.Veritas.Owl.Reasoning;

namespace Lumoin.Veritas.Database;

/// <summary>
/// The reasoning knob of <see cref="VeritasEngineOptions"/>: the policy that
/// governs how far the engine reasons, the work budget that bounds it, and the
/// solver search mode. Its presence on the options is the wiring decision — a
/// non-<c>null</c> configuration composes the reasoner, and leaving it
/// <c>null</c> is the lean-deployment optimisation that links no reasoning
/// machinery — and its contents are the behaviour knobs over the wired
/// reasoner.
/// </summary>
/// <param name="Policy">How far to reason: RDFS where it suffices, then RL, then the description-logic engine beyond RL.</param>
/// <param name="Budget">The work bound on each beyond-RL decision, so a module the search cannot decide within the bound abstains with a reason rather than wedging the load.</param>
/// <param name="SearchMode">How the SAT-backed engine prunes within one world's boolean structure.</param>
public sealed record ReasoningConfiguration(
    ReasoningPolicy Policy,
    ReasoningBudget Budget,
    SatSearchMode SearchMode)
{
    /// <summary>
    /// The default reasoning configuration: fully usable — RDFS where it
    /// suffices, then RL, then the SAT-backed description-logic engine beyond
    /// RL — bounded by a generous work budget so a normal ontology always
    /// decides while a pathological one abstains rather than hanging the load.
    /// The budget ceilings sit well above the hardest measured locality
    /// module and are a tunable, not a measured per-deployment optimum.
    /// </summary>
    public static ReasoningConfiguration Default { get; } = new(
        ReasoningPolicy.Default,
        new ReasoningBudget(MaxSolves: 100_000, MaxConflicts: 1_000_000),
        SatSearchMode.ConflictLearning);
}
