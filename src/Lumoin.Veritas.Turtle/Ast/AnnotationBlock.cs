using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// An RDF 1.2 annotation block <c>{| predicateObjectList |}</c>
/// attached to an object. The emitter expands the block by reusing
/// (or allocating) a reifier identifier and asserting the
/// predicate-object list with that reifier as subject.
/// </summary>
[DebuggerDisplay("{{| {Predicates.Length} predicates |}} #{NodeId}")]
public sealed class AnnotationBlock: Annotation
{
    /// <summary>
    /// Initialises a new <see cref="AnnotationBlock"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the annotation block including its delimiters.</param>
    /// <param name="predicates">The predicate-object list inside the block.</param>
    public AnnotationBlock(int nodeId, SourceSpan span, ImmutableArray<PredicateObject> predicates)
        : base(nodeId, span)
    {
        Predicates = predicates;
    }

    /// <summary>Gets the predicate-object list inside the block.</summary>
    public ImmutableArray<PredicateObject> Predicates { get; }
}
