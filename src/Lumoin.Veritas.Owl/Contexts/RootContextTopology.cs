namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// How the engine lays out the root tier of a nominal-jurisdiction module:
/// one distinguished root context <c>vr</c> holding every constant (the
/// published single-root calculus), or one nominal-root context <c>v_o</c>
/// per individual <c>o</c> with the context clause <c>⊤ → x ≈ o</c> and the
/// own constant respelled as the central variable on every clause entering
/// <c>v_o</c> (thesis 8.2.2). Under the fragmented topology, cross-individual
/// evidence meets through the inter-nominal propagation rule — a derived or
/// seeded <c>⊤</c>-clause whose head names a foreign individual images into
/// that individual's context under <c>[x/src][o_i/x]</c> — and through the
/// per-slot r-Succ/r-Pred exchanges, so every co-location the shared root
/// table provided is reproduced on imaged premises. The two topologies decide
/// the same fragment with the same verdicts, subsumptions, and census
/// admissions — the topology is a performance layout, never a semantic
/// switch — so the choice is a measured per-workload selection.
/// </summary>
internal enum RootContextTopology
{
    /// <summary>
    /// The default: the one distinguished root context <c>vr</c> where ground
    /// nominal reasoning concentrates — constants stay constants, the root
    /// Hyper runs its constant-anchored odometer, and the r-Pred sweeps read
    /// the single shared table, as the published calculus applies the rules.
    /// </summary>
    SingleRoot,

    /// <summary>
    /// The fragmented topology: one nominal-root context per individual, told
    /// contexts resolved at engine construction and generated-nominal contexts
    /// minted lazily at first seed. Own constants respell as the central
    /// variable at entry, ordinary indexing serves the per-individual tables,
    /// and the inter-nominal propagation rule carries cross-individual
    /// evidence. Selectable; measured against <see cref="SingleRoot"/>.
    /// Composes with every paramodulation scope but not with
    /// <see cref="RootPropagationRelevance.GroundFiltered"/>, whose
    /// ground-conjunct index set is defined over the single shared root table.
    /// </summary>
    PerIndividualRoots,
}
