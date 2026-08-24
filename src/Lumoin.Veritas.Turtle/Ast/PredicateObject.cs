using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// One predicate paired with its object list inside a
/// predicate-object-list: the unit produced when the parser sees
/// <c>verb objectList</c>.
/// </summary>
/// <remarks>
/// The objects are <see cref="AnnotatedObject"/> instances so any
/// reifiers and annotation blocks attached to an object in source
/// are preserved on the AST.
/// </remarks>
[DebuggerDisplay("PredicateObject({Objects.Length} objects) #{NodeId}")]
public sealed class PredicateObject: TurtleAstNode
{
    /// <summary>
    /// Initialises a new <see cref="PredicateObject"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the predicate-and-objects group.</param>
    /// <param name="predicate">The predicate term.</param>
    /// <param name="objects">The objects, each carrying its own optional annotation list.</param>
    public PredicateObject(
        int nodeId,
        SourceSpan span,
        Term predicate,
        ImmutableArray<AnnotatedObject> objects)
        : base(nodeId, span)
    {
        Predicate = predicate;
        Objects = objects;
    }

    /// <summary>Gets the predicate term.</summary>
    public Term Predicate { get; }

    /// <summary>Gets the objects bound to <see cref="Predicate"/>.</summary>
    public ImmutableArray<AnnotatedObject> Objects { get; }
}
