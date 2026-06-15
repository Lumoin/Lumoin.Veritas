using Lumoin.Veritas.Core.Sat;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The public selection point for the description-logic engine behind the
/// <see cref="DescriptionLogicDelegate"/> seam: the snapshot tableau or the
/// SAT-backed sibling, the latter bounded by a work budget. A composition root
/// picks one and wires it into a <see cref="ReasoningRendezvous"/>. Both decide
/// the same ALC(H) fragment and name the same beyond-fragment remainder, so the
/// choice is one of search strategy and resource bound, not of answer.
/// </summary>
public static class ReasoningEngines
{
    /// <summary>
    /// The snapshot tableau engine: an in-place tableau with copy-on-branch
    /// disjunctions. It runs no solver and carries no work budget, so it
    /// always decides or throws on cancellation — it never abstains on a
    /// budget.
    /// </summary>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate Snapshot()
    {
        return AlcModuleReasoner.CreateDelegate();
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
}
