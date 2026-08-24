using Lumoin.Veritas.Core.Sat;
using Lumoin.Veritas.Owl.Datatypes;
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
    /// Whether an open whose reasoning derives an inconsistency fails loudly with a
    /// <see cref="ReasoningInconsistencyException"/> instead of serving the partial closure;
    /// <see langword="false"/> (the default) serves the closure and surfaces the outcome on
    /// <see cref="VeritasEngine.ReasoningProvenance"/>. The check reads the folded consistency verdict, so a
    /// fired falsity rule and a delegated condemnation both refuse; a fragment-relative CONSISTENT result — a
    /// verdict scoped to the fragment the calculus could read, with a named remainder on
    /// <see cref="ReasoningProvenance.UndecidedConstructs"/> — is not a decided inconsistency and does not
    /// refuse.
    /// </summary>
    public bool RefuseInconsistent { get; init; }

    /// <summary>
    /// The registered-datatype set the description-logic engine consults at the concrete-domain leaves where
    /// the family classifier abstains — operator-defined datatypes decided by declarative facet automata or a
    /// delegate escape hatch. The default is <see cref="DatatypeRegistry.Empty"/>, the null object a host with
    /// no registered datatypes uses, so an unconfigured engine decides exactly the built-in datatype map.
    /// </summary>
    public DatatypeRegistry Datatypes { get; init; } = DatatypeRegistry.Empty;

    /// <summary>
    /// The default reasoning configuration: fully usable — RDFS where it
    /// suffices, then RL, then the three-tier description-logic engine
    /// (EL fast path, context saturation, SAT-backed oracle) beyond RL — bounded
    /// by a generous work budget so a normal ontology always decides while a
    /// pathological one abstains rather than hanging the load. The solve and
    /// conflict ceilings sit well above the hardest measured locality module; the
    /// inference ceiling bounds the context tier and is calibrated at 100× the
    /// maximum budget-checked rule-application count observed over the
    /// context-decided certification set (battery maximum 143, corpus maximum 388),
    /// so no certified context decision abstains at the default while a
    /// budget-explosive module exhausts the ceiling and delegates to the SAT-backed
    /// oracle. That ceiling is a context-tier calibration — its measured inputs are
    /// context-tier rule attempts (battery maximum 143, corpus maximum 388) — and
    /// does not size the snapshot-tableau fallback leg, whose per-rule-application
    /// cost is materially higher. All three are tunables, not measured
    /// per-deployment optima.
    /// </summary>
    public static ReasoningConfiguration Default { get; } = new(
        ReasoningPolicy.Default,
        new ReasoningBudget(MaxSolves: 100_000, MaxConflicts: 1_000_000, MaxInferences: 50_000),
        SatSearchMode.ConflictLearning);
}
