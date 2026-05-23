using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// A collection: <c>( a b c )</c>. The emitter expands collections
/// into <c>rdf:first</c> / <c>rdf:rest</c> / <c>rdf:nil</c> chains
/// rooted at a fresh blank node per element.
/// </summary>
/// <remarks>
/// An empty collection <c>()</c> denotes the <c>rdf:nil</c> term
/// directly with no chain links emitted; the AST node still exists so
/// the original source position is preserved for editor consumers.
/// </remarks>
[DebuggerDisplay("Collection({Items.Length} items) #{NodeId}")]
public sealed class CollectionTerm: Term
{
    /// <summary>
    /// Initialises a new <see cref="CollectionTerm"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the collection including its parentheses.</param>
    /// <param name="items">The collection's items in order.</param>
    public CollectionTerm(int nodeId, SourceSpan span, ImmutableArray<Term> items)
        : base(nodeId, span)
    {
        Items = items;
    }

    /// <summary>Gets the collection's items in source order.</summary>
    public ImmutableArray<Term> Items { get; }
}
