using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// The base type for an RDF 1.2 annotation attached to an object:
/// either a reifier (<c>~</c> with an optional IRI or blank-node
/// reference) or an annotation block (<c>{| pol |}</c>).
/// </summary>
public abstract class Annotation: TurtleAstNode
{
    /// <summary>
    /// Initialises a new <see cref="Annotation"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the annotation.</param>
    protected Annotation(int nodeId, SourceSpan span)
        : base(nodeId, span)
    {
    }
}
