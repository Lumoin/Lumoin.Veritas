namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// Whether the r-Pred rule propagates a root clause into a context that cannot
/// discharge the clause's GROUND body conjuncts <c>Cᵢ</c>. The rule's nonground
/// <c>Sur</c> body atoms are already relevance-conditioned by the unblocked-premise
/// requirement, but the ground conjuncts are carried through untouched, so an
/// unrestricted propagation lands conditional clauses in contexts that can never
/// use them — the measured root-churn pathology. The filtered mode blocks exactly
/// those offers: a target qualifies for <c>Cᵢ</c> when it holds <c>Cᵢ</c> itself
/// as a maximal head atom of a live clause (the Join form-(a) discharge witness)
/// or a bridge-premise pair — an empty-body maximal x-abstraction of <c>Cᵢ</c>
/// together with an empty-body maximal <c>x ≈ o</c> for the abstracted individual
/// (the Join form-(b) witness) — and blocked offers are compensated by the
/// downward <c>A → A</c> relevance flood and re-offered when a qualification
/// lands later. Both modes decide the same fragment with the same verdicts,
/// subsumptions, and census admissions — the mode is a performance knob, never a
/// semantic switch — so the choice is a measured per-workload selection.
/// </summary>
internal enum RootPropagationRelevance
{
    /// <summary>
    /// The default: every r-Pred completion and broadcast lands in its target
    /// context regardless of the target's ability to discharge the conclusion's
    /// ground conjuncts, as the published calculus applies the rule.
    /// </summary>
    Unrestricted,

    /// <summary>
    /// The ground-relevance filter: an r-Pred offer — swept completion or
    /// broadcast image — lands only in a target holding, for EVERY ground body
    /// conjunct, the form-(a) maximal-head witness or the form-(b) bridge-premise
    /// pair; blocked offers spend no budget, the downward tautology flood
    /// re-qualifies descendants of a discharging context, and the re-offer
    /// triggers replay a blocked offer when its qualification first lands.
    /// Selectable; measured against <see cref="Unrestricted"/>.
    /// </summary>
    GroundFiltered,
}
