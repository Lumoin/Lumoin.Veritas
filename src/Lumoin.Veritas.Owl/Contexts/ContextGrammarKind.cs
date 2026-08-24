namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// The context-kind key the clause grammar and the term-order band select on:
/// the pair (root-class membership, engine topology) collapsed to the three
/// reachable kinds — topologies never mix within one engine, so a root-class
/// context is either the single root or a per-individual nominal root, never
/// both. The grammar guard and the condition-6 minimal band both key on this
/// value: an ordinary context uses the ordinary literal universe and the
/// module <c>Pr(O)</c> band; the single root uses the published root universe
/// and the <c>Prr</c> band; a nominal root uses the per-position pair grammar
/// over {central, context, function, individual, function-of-individual} and
/// the root band widened with the ordinary central/context handling.
/// </summary>
internal enum ContextGrammarKind
{
    /// <summary>An ordinary (trivial, ground, query, or cautious-successor) context.</summary>
    Ordinary,

    /// <summary>The distinguished single root context <c>vr</c> under <see cref="RootContextTopology.SingleRoot"/>.</summary>
    Root,

    /// <summary>A per-individual nominal-root context <c>v_o</c> under <see cref="RootContextTopology.PerIndividualRoots"/>.</summary>
    NominalRoot,
}
