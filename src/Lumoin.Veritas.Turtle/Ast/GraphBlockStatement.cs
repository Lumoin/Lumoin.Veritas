using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// A TriG named-graph block: <c>iri { triples }</c>, <c>_:b { triples }</c>,
/// or <c>GRAPH iri { triples }</c>. Carries the graph label and the
/// triple statements scoped to that graph.
/// </summary>
/// <remarks>
/// When the block was written without an explicit label (a bare
/// <c>{ ... }</c>), <see cref="Label"/> is <c>null</c> and the inner
/// triples are emitted into the default graph. The
/// <see cref="HasGraphKeyword"/> flag preserves whether the source
/// used the SPARQL-style <c>GRAPH</c> keyword form so a Level 2
/// writer can restore it on round-trip.
/// </remarks>
[DebuggerDisplay("GraphBlock label={Label} ({Triples.Length} triples) #{NodeId}")]
public sealed class GraphBlockStatement: Statement
{
    /// <summary>
    /// Initialises a new <see cref="GraphBlockStatement"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the block including its braces.</param>
    /// <param name="label">The graph label, or <c>null</c> for a labelless block.</param>
    /// <param name="hasGraphKeyword">Whether the source used the <c>GRAPH</c> keyword form.</param>
    /// <param name="triples">The triple statements inside the block.</param>
    public GraphBlockStatement(
        int nodeId,
        SourceSpan span,
        Term? label,
        bool hasGraphKeyword,
        ImmutableArray<TripleStatement> triples)
        : base(nodeId, span)
    {
        Label = label;
        HasGraphKeyword = hasGraphKeyword;
        Triples = triples;
    }

    /// <summary>Gets the graph label, or <c>null</c> when the block has no explicit label.</summary>
    public Term? Label { get; }

    /// <summary>Gets a value indicating whether the source used the <c>GRAPH</c> keyword form.</summary>
    public bool HasGraphKeyword { get; }

    /// <summary>Gets the triple statements inside the graph block.</summary>
    public ImmutableArray<TripleStatement> Triples { get; }
}
