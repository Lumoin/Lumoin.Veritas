using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// The base class for every node in a parsed Turtle or TriG document
/// — directives, statements, terms, annotations.
/// </summary>
/// <remarks>
/// <para>
/// Every node carries a <see cref="Span"/> identifying the source bytes
/// it was parsed from and a <see cref="NodeId"/> uniquely identifying
/// it within its containing <see cref="TurtleDocument"/>. The
/// <see cref="NodeId"/> is the value carried by
/// <see cref="DocumentNodeRef.Index"/> for emitted quads paired with
/// document-node references, enabling editor consumers to highlight
/// the AST node a quad originated from.
/// </para>
/// </remarks>
[DebuggerDisplay("{GetType().Name,nq} #{NodeId} {Span}")]
public abstract class TurtleAstNode
{
    /// <summary>
    /// Initialises a new <see cref="TurtleAstNode"/> with the given identity and span.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier within the containing document.</param>
    /// <param name="span">The source-byte range the node was parsed from.</param>
    protected TurtleAstNode(int nodeId, SourceSpan span)
    {
        NodeId = nodeId;
        Span = span;
    }

    /// <summary>Gets the parser-assigned identifier of this node within its containing document.</summary>
    public int NodeId { get; }

    /// <summary>Gets the source-byte range this node was parsed from.</summary>
    public SourceSpan Span { get; }
}
