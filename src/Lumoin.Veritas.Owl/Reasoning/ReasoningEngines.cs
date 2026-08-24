using System;
using Lumoin.Veritas.Core.Sat;
using Lumoin.Veritas.Owl.Datatypes;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The public selection point for the description-logic engine behind the
/// <see cref="DescriptionLogicDelegate"/> seam: the snapshot tableau, the
/// SAT-backed sibling (bounded by a work budget), or the EL-coupled pay-as-you-go
/// engine that decides EL modules by saturation and delegates the rest to a
/// tableau oracle. A composition root picks one and wires it into a
/// <see cref="ReasoningRendezvous"/>. Every choice decides the same fragment and
/// names the same beyond-fragment remainder, so the choice is one of search
/// strategy and resource bound, not of answer.
/// </summary>
public static class ReasoningEngines
{
    /// <summary>
    /// The snapshot tableau engine: an in-place tableau with copy-on-branch
    /// disjunctions. It runs no solver. The parameterless helper wraps the
    /// unbounded surface, so it decides or throws on cancellation; the
    /// budget-carrying <see cref="Snapshot(ReasoningBudget)"/> bounds the same
    /// tableau so a module the search cannot decide within the bound abstains
    /// with a reason.
    /// </summary>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate Snapshot()
    {
        return AlcModuleReasoner.CreateDelegate();
    }

    /// <summary>
    /// The snapshot tableau engine consulting a registered-datatype set at the concrete-domain leaves — the
    /// registry-carrying counterpart of <see cref="Snapshot()"/>.
    /// </summary>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate Snapshot(DatatypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return AlcModuleReasoner.CreateDelegate(registry);
    }

    /// <summary>
    /// The snapshot tableau engine bounded by <paramref name="budget"/>: the same
    /// in-place tableau as <see cref="Snapshot()"/>, its rule applications bounded
    /// by the budget's inference axis so a module the tableau cannot decide within
    /// the bound abstains with a reason rather than searching without end. This is
    /// the resilient snapshot choice for a long-running host and the bounded
    /// oracle a fallback-omitted context composition inherits.
    /// </summary>
    /// <param name="budget">The work budget; <see cref="ReasoningBudget.Unbounded"/> places no bound.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate Snapshot(ReasoningBudget budget)
    {
        return AlcModuleReasoner.CreateDelegate(budget);
    }

    /// <summary>
    /// The snapshot tableau engine bounded by <paramref name="budget"/> consulting a registered-datatype
    /// set at the concrete-domain leaves — the registry-carrying counterpart of
    /// <see cref="Snapshot(ReasoningBudget)"/>.
    /// </summary>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="budget">The work budget; <see cref="ReasoningBudget.Unbounded"/> places no bound.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate Snapshot(DatatypeRegistry registry, ReasoningBudget budget)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return AlcModuleReasoner.CreateDelegate(registry, budget);
    }

    /// <summary>
    /// The SAT-backed engine: per-world propositional satisfiability over one
    /// growing CNF, bounded by <paramref name="budget"/> so a module the
    /// search cannot decide within the bound abstains with a reason rather
    /// than searching without end. This is the resilient choice for a
    /// long-running host, where an adversarial or pathological ontology must
    /// not wedge the load.
    /// </summary>
    /// <param name="budget">The work budget; <see cref="ReasoningBudget.Unbounded"/> places no bound.</param>
    /// <param name="searchMode">How the solver prunes within one world's boolean structure; both modes decide the same satisfiability.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate SatBacked(ReasoningBudget budget, SatSearchMode searchMode = SatSearchMode.ConflictLearning)
    {
        return SatTableauModuleReasoner.CreateDelegate(searchMode, budget);
    }

    /// <summary>
    /// The SAT-backed engine consulting a registered-datatype set at the concrete-domain leaves — the
    /// registry-carrying counterpart of <see cref="SatBacked(ReasoningBudget, SatSearchMode)"/>.
    /// </summary>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="budget">The work budget; <see cref="ReasoningBudget.Unbounded"/> places no bound.</param>
    /// <param name="searchMode">How the solver prunes within one world's boolean structure; both modes decide the same satisfiability.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate SatBacked(DatatypeRegistry registry, ReasoningBudget budget, SatSearchMode searchMode = SatSearchMode.ConflictLearning)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return SatTableauModuleReasoner.CreateDelegate(registry, searchMode, budget);
    }

    /// <summary>
    /// The EL-coupled pay-as-you-go engine: a module wholly within the EL⊥
    /// fragment is decided by consequence-based saturation alone — no tableau
    /// search, classification past the snapshot engine's pairwise signature cap —
    /// and any module outside it is delegated whole to <paramref name="fallback"/>.
    /// The verdict is identical to the fallback's on every module; the gain is
    /// the fast path for the EL-heavy modules that dominate real workloads.
    /// </summary>
    /// <param name="fallback">The tableau oracle for non-EL modules; the snapshot engine when <see langword="null"/>.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate ElCoupled(DescriptionLogicDelegate? fallback = null)
    {
        return ElCoupledModuleReasoner.CreateDelegate(fallback);
    }

    /// <summary>
    /// The EL-coupled pay-as-you-go engine consulting a registered-datatype set at the concrete-domain
    /// leaves — the registry-carrying counterpart of <see cref="ElCoupled(DescriptionLogicDelegate?)"/>.
    /// </summary>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="fallback">The tableau oracle for non-EL modules; the registry-consulting snapshot engine when <see langword="null"/>.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate ElCoupled(DatatypeRegistry registry, DescriptionLogicDelegate? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return ElCoupledModuleReasoner.CreateDelegate(registry, fallback);
    }

    /// <summary>
    /// The consequence-based context-saturation engine: a sibling behind the same
    /// seam that decides the disjunctive SRIQ slice by saturation and delegates the
    /// rest to <paramref name="fallback"/>. An admitted module is decided by the
    /// saturation under an unbounded inference budget; the verdict is identical to
    /// the fallback's on every module. It is the middle tier of the production
    /// composition <c>ElCoupled(ContextSaturation(SatBacked))</c>: the modules the
    /// EL fast path declines but the disjunctive-SRIQ survey admits.
    /// </summary>
    /// <param name="fallback">The oracle for modules the engine does not admit; the snapshot engine when <see langword="null"/>.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate ContextSaturation(DescriptionLogicDelegate? fallback = null)
    {
        return ContextSaturationModuleReasoner.CreateDelegate(fallback);
    }

    /// <summary>
    /// The consequence-based context-saturation engine under a given inference
    /// budget: a sibling behind the same seam that decides the disjunctive SRIQ
    /// slice by saturation and delegates the rest to <paramref name="fallback"/>. An
    /// admitted module whose saturation exhausts <paramref name="budget"/> behind
    /// the seam composes the exhaustion into a delegation to <paramref name="fallback"/>,
    /// carrying the spent saturation's totals onto the returned decision, so a
    /// budget-explosive module still reaches the oracle — the resilient choice for
    /// a long-running host. It is the middle tier of the production composition
    /// <c>ElCoupled(ContextSaturation(SatBacked))</c>.
    /// </summary>
    /// <param name="budget">The inference budget bounding each admitted module's saturation; <see cref="ReasoningBudget.Unbounded"/> places no bound.</param>
    /// <param name="fallback">The oracle for modules the engine does not admit; the snapshot engine bounded by the same <paramref name="budget"/> when <see langword="null"/>, so a fallback-omitted composition inherits a bounded oracle.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate ContextSaturation(ReasoningBudget budget, DescriptionLogicDelegate? fallback = null)
    {
        return ContextSaturationModuleReasoner.CreateDelegate(budget, fallback);
    }

    /// <summary>
    /// The context-saturation engine under a given inference budget consulting a registered-datatype set at
    /// the datatype sidecar — the registry-carrying counterpart of
    /// <see cref="ContextSaturation(ReasoningBudget, DescriptionLogicDelegate?)"/>.
    /// </summary>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="budget">The inference budget bounding each admitted module's saturation; <see cref="ReasoningBudget.Unbounded"/> places no bound.</param>
    /// <param name="fallback">The oracle for modules the engine does not admit; the registry-consulting snapshot engine bounded by the same <paramref name="budget"/> when <see langword="null"/>.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate ContextSaturation(DatatypeRegistry registry, ReasoningBudget budget, DescriptionLogicDelegate? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return ContextSaturationModuleReasoner.CreateDelegate(registry, budget, fallback);
    }
}
