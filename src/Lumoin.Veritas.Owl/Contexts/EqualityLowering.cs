namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// How the clausifier lowers a module-level functionality constraint (an
/// unqualified <c>≤1</c> over a directioned role, the reduction of
/// <c>Functional</c> / <c>InverseFunctional</c>) into DL-clauses. Both lowerings
/// decide the same fragment with the same verdicts and subsumptions — they
/// differ only in whether a same-owner functional successor is merged by
/// equality REASONING after the fact or by CONSTRUCTION up front — so the choice
/// is a measured per-workload knob, not a semantic switch.
/// </summary>
internal enum EqualityLowering
{
    /// <summary>
    /// The published lowering: every superclass-position existential mints a
    /// distinct successor function symbol, and a module-level <c>≤1</c> merges
    /// two same-owner successors through the DL4 counting clause and the engine's
    /// Eq rule. The default.
    /// </summary>
    GeneralClause,

    /// <summary>
    /// The successor-sharing (V-node) lowering: for an existential or min-1 over
    /// a directioned role carrying a module-level unqualified <c>≤1</c>
    /// (<c>Functional(r)</c> makes the forward role functional,
    /// <c>InverseFunctional(r)</c> the inverse role), every such existential
    /// reuses ONE shared successor function symbol per directioned role, so
    /// same-owner functional successors merge by construction rather than by the
    /// DL4 counting clause and the Eq rule. The sharing key is the DIRECTIONED
    /// role: forward and inverse spellings never share, and a qualified <c>≤1</c>
    /// gets no sharing (the general clause covers what sharing cannot — the
    /// <c>f(x) ≈ y</c> successor-predecessor merge and cross-context effects — so
    /// the DL4 clauses are still emitted unchanged). Selectable; measured against
    /// <see cref="GeneralClause"/>.
    /// </summary>
    SuccessorSharing,
}
