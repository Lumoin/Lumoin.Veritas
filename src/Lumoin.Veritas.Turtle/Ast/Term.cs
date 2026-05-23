using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// The base type for every Turtle term that can appear in subject,
/// predicate, or object position: IRIs, prefixed names, blank nodes,
/// literals, collections, blank-node property lists, triple terms, and
/// reified triples.
/// </summary>
public abstract class Term: TurtleAstNode
{
    /// <summary>
    /// Initialises a new <see cref="Term"/> with the given identity and span.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier within the containing document.</param>
    /// <param name="span">The source-byte range the term was parsed from.</param>
    protected Term(int nodeId, SourceSpan span)
        : base(nodeId, span)
    {
    }
}
