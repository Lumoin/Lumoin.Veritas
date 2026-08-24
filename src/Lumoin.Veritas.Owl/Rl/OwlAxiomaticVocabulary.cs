namespace Lumoin.Veritas.Owl.Rl;

/// <summary>
/// Which axiomatic vocabulary table the closure seeds — the rows entailed
/// by the empty graph that enter every closure as derived knowledge.
/// </summary>
/// <remarks>
/// <para>
/// The shared table carries the rows sound under every semantics the
/// calculus serves. <see cref="MetaclassMerged"/> additionally commits to
/// the OWL 2 RDF-Based Semantics' metaclass merge — <c>owl:Class</c> and
/// <c>rdfs:Class</c> denote the same class extension — which holds in
/// every RDF-Based interpretation and does not hold under the Direct
/// Semantics, where the two vocabularies stay distinct. The merge is
/// therefore a per-semantics mode rather than a shared-table row: a
/// consumer requesting it states which semantics its verdicts claim.
/// </para>
/// <para>
/// Unlike <see cref="OwlComprehension"/> — a check-time grant confined to
/// the entailment path — the vocabulary mode seeds axioms: rows true in
/// every interpretation of the requested semantics. It is therefore valid
/// on consistency verdicts too, and no fallback-degrade path exists: a
/// clash reachable only with true axioms present is a real clash, and
/// retrying without them would mask genuine inconsistency.
/// </para>
/// </remarks>
public enum OwlAxiomaticVocabulary
{
    /// <summary>The census-restricted shared table alone; the metaclass merge stays out.</summary>
    Shared = 0,

    /// <summary>The shared table plus the RDF-Based <c>owl:Class</c>/<c>rdfs:Class</c> metaclass merge: both subsumptions and both metaclass self-typings.</summary>
    MetaclassMerged = 1,
}
