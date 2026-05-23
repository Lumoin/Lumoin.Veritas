using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// The base type for top-level statements: triple statements,
/// directives, and TriG graph-block wrappers.
/// </summary>
public abstract class Statement: TurtleAstNode
{
    /// <summary>
    /// Initialises a new <see cref="Statement"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the statement.</param>
    protected Statement(int nodeId, SourceSpan span)
        : base(nodeId, span)
    {
    }
}
