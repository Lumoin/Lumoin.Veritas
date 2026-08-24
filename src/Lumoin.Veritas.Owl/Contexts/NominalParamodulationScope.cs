namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// Where the Eq rule is allowed to paramodulate a central-variable-versus-individual
/// equality <c>x ≈ o</c> — the rewrite of a named individual <c>o</c> down to the
/// central variable <c>x</c> (arXiv:1805.01396 Table 2 Eq; the constant side is the
/// sole rewrite source of the unoriented <c>x ≈ o</c>). Both scopes decide the same
/// fragment with the same verdicts and subsumptions — they differ only in which
/// contexts spend the equated-enumeration paramodulation traffic — so the choice is
/// a measured per-workload knob, not a semantic switch. The context-variable form
/// <c>y ≈ o</c> and the ground constant form <c>o ≈ o′</c> are never scoped: both
/// feed the ground successor-trigger propagation the consistency surface reads.
/// </summary>
internal enum NominalParamodulationScope
{
    /// <summary>
    /// The default: the central-variable-versus-individual paramodulation fires
    /// only in a read-off context — the root context, the trivial consistency
    /// context, a ground context, or a query-initialized context — the contexts a
    /// verdict surface inspects for a central-variable consequence. Its
    /// central-variable products never cross a context boundary (no successor,
    /// predecessor, or root trigger carries a bare-<c>x</c> atom), so a
    /// non-read-off context's <c>x ≈ o</c> rewrites are unobservable and dropping
    /// them leaves both read-off surfaces complete while cutting the
    /// equated-enumeration clause population.
    /// </summary>
    QueryScoped,

    /// <summary>
    /// The license-shaped two-axis widening of the thesis's Eq restriction
    /// (arXiv:1805.01396 8.2.3: the <c>x ≈ o</c> applications "are only required
    /// in contexts introduced upon initialisation based on the query, and are
    /// only necessary for paramodulation inferences on query atoms"), selectable
    /// and dark. ATOM AXIS, every query-initialized context under both
    /// topologies: the scopable rewrite fires only when the acted-on target
    /// literal is a query atom — a named-class concept atom of the subsumption
    /// read-off signature. CONTEXT AXIS, root-class contexts under the
    /// fragmented topology only: the blanket root-class exemption narrows to a
    /// push-provenance gate — the rewrite fires only when the acting equality
    /// clause carries the transitively inherited push tag; the single root stays
    /// fully exempt (the consistency read-off lives there), and the trivial and
    /// ground contexts stay exempt under both topologies. Because the thesis
    /// proof covers only its query-only read-off while the engine also reads
    /// consistency off root-class, trivial, and ground contexts, every run under
    /// this scope carries the two-surface blocked-live latch: a run in which a
    /// scopable rewrite was blocked on a read-off surface may not certify the
    /// corresponding positive read — sound-or-silent at runtime.
    /// </summary>
    LicenseScoped,

    /// <summary>
    /// The reference behaviour: the central-variable-versus-individual
    /// paramodulation fires in every context, as the published calculus applies
    /// it. Selectable; measured against <see cref="QueryScoped"/>.
    /// </summary>
    Unrestricted,
}
