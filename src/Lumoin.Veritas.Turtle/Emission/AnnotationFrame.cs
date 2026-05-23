using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Turtle.Ast;

namespace Lumoin.Veritas.Turtle.Emission;

/// <summary>
/// One unit of pending annotation work on the quad emitter's explicit
/// stack: an annotated object together with the asserted triple its
/// annotations attach to.
/// </summary>
/// <remarks>
/// Annotation blocks nest — a block attached to an object inside
/// another block annotates that outer block's annotation triple. The
/// emitter walks this nesting with a <see cref="System.Collections.Generic.Stack{T}"/>
/// of frames rather than by recursion: processing a frame may push
/// further frames for the annotations carried by the objects inside a
/// block, each frame recording the <see cref="Subject"/>,
/// <see cref="Predicate"/>, and <see cref="ObjectTerm"/> of the triple
/// those annotations reify.
/// </remarks>
/// <param name="AnnotatedObject">The annotated object whose annotations are to be emitted.</param>
/// <param name="Subject">The subject of the triple the annotations attach to.</param>
/// <param name="Predicate">The predicate of the triple the annotations attach to.</param>
/// <param name="ObjectTerm">The object of the triple the annotations attach to.</param>
[DebuggerDisplay("AnnotationFrame {Predicate}")]
internal readonly record struct AnnotationFrame(
    AnnotatedObject AnnotatedObject,
    RdfTerm Subject,
    NamedNode Predicate,
    RdfTerm ObjectTerm);
