using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Owl.Rl;

/// <summary>
/// The kind of an ill-formed encoding the closure declined to read.
/// </summary>
public enum MalformedShapeKind
{
    /// <summary>An RDF collection chain ended before <c>rdf:nil</c> — a cell lacked <c>rdf:first</c> or <c>rdf:rest</c>.</summary>
    BrokenListChain = 0,

    /// <summary>An RDF collection chain revisited a cell before reaching <c>rdf:nil</c>.</summary>
    CyclicListChain = 1,
}

/// <summary>
/// One ill-formed encoding the closure declined to read, recorded on the
/// result instead of silently skipping the consuming derivation.
/// </summary>
/// <remarks>
/// The closure's derivations are unaffected by the shapes recorded here —
/// each record names an axiom reading that produced nothing. A consumer
/// weighing a verdict computed beside recorded shapes knows the closure
/// read less than the graph asserts.
/// </remarks>
/// <param name="Subject">The node whose reading declined — the offending list cell, the list head, or the ambiguous subject.</param>
/// <param name="Predicate">The position that declined, or <see cref="TermId.None"/> when the shape is not predicate-specific.</param>
/// <param name="Kind">The kind of ill-formed encoding.</param>
public readonly record struct MalformedShape(TermId Subject, TermId Predicate, MalformedShapeKind Kind);
