using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Turtle.Ast;

/// <summary>
/// A reifier marker <c>~</c> with an optional explicit reifier
/// identifier — an IRI or a blank node. A bare <c>~</c> instructs the
/// emitter to allocate a fresh blank node as the reifier.
/// </summary>
[DebuggerDisplay("~ {Identifier} #{NodeId}")]
public sealed class ReifierAnnotation: Annotation
{
    /// <summary>
    /// Initialises a new <see cref="ReifierAnnotation"/>.
    /// </summary>
    /// <param name="nodeId">The parser-assigned identifier.</param>
    /// <param name="span">The source-byte range covering the reifier marker and its optional identifier.</param>
    /// <param name="identifier">The explicit reifier term, or <c>null</c> when the source wrote bare <c>~</c>.</param>
    public ReifierAnnotation(int nodeId, SourceSpan span, Term? identifier)
        : base(nodeId, span)
    {
        Identifier = identifier;
    }

    /// <summary>Gets the explicit reifier term, or <c>null</c> for a bare <c>~</c>.</summary>
    public Term? Identifier { get; }
}
