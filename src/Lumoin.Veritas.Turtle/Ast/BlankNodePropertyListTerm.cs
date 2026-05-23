using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// A blank-node property list: <c>[ p1 o1 ; p2 o2 ]</c>. The emitter
/// allocates a fresh blank node and emits a triple per predicate-object
/// pair with the blank node as subject.
/// </summary>
[DebuggerDisplay("[ {Predicates.Length} predicates ] #{NodeId}")]
public sealed class BlankNodePropertyListTerm: Term
{
    /// <summary>
    /// Initialises a new <see cref="BlankNodePropertyListTerm"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the property list including its brackets.</param>
    /// <param name="predicates">The predicate-object list.</param>
    public BlankNodePropertyListTerm(
        int nodeId,
        SourceSpan span,
        ImmutableArray<PredicateObject> predicates)
        : base(nodeId, span)
    {
        Predicates = predicates;
    }

    /// <summary>Gets the predicate-object list inside the brackets.</summary>
    public ImmutableArray<PredicateObject> Predicates { get; }
}
