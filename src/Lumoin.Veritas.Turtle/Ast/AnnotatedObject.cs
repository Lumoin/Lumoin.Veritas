using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// An object together with any RDF 1.2 annotation markers attached to
/// it in source: tilde reifiers and/or annotation blocks.
/// </summary>
/// <remarks>
/// <para>
/// The RDF 1.2 grammar attaches an <c>annotation</c> production to
/// every object inside an object-list: <c>object (reifier|annotationBlock)*</c>.
/// Each reifier is either a named reifier <c>~ &lt;iri&gt;</c> or
/// <c>~ _:b</c>, or a bare <c>~</c> denoting "allocate a fresh
/// blank node." Each annotation block <c>{| pol |}</c> is shorthand
/// for "allocate (or reuse) a reifier and assert the predicate-object
/// list with that reifier as subject."
/// </para>
/// </remarks>
[DebuggerDisplay("AnnotatedObject {Object.NodeId} ({Annotations.Length} annotations) #{NodeId}")]
public sealed class AnnotatedObject: TurtleAstNode
{
    /// <summary>
    /// Initialises a new <see cref="AnnotatedObject"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the object plus all attached annotations.</param>
    /// <param name="objectTerm">The object term.</param>
    /// <param name="annotations">Reifiers and annotation blocks attached to the object, in source order.</param>
    public AnnotatedObject(
        int nodeId,
        SourceSpan span,
        Term objectTerm,
        ImmutableArray<Annotation> annotations)
        : base(nodeId, span)
    {
        Object = objectTerm;
        Annotations = annotations;
    }

    /// <summary>Gets the object term.</summary>
    public Term Object { get; }

    /// <summary>Gets the annotations attached to the object in source order.</summary>
    public ImmutableArray<Annotation> Annotations { get; }
}
