using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle.Ast;

namespace Lumoin.Veritas.Turtle.Parser;

/// <summary>
/// One frame on <see cref="TurtleParser"/>'s explicit work stack.
/// Carries the production the frame is in, the in-progress accumulators
/// it has built so far, and a stage counter the driver uses to advance
/// the production one step at a time.
/// </summary>
/// <remarks>
/// <para>
/// Fields are intentionally optional and indexed by <see cref="Kind"/>:
/// a single frame layout supports every production the driver knows
/// about, avoiding a frame-type hierarchy whose dispatch would still
/// land in a switch. The accumulator lists are allocated lazily by
/// the step methods that need them.
/// </para>
/// </remarks>
[DebuggerDisplay("{Kind} stage={Stage} {StartSpan}")]
internal sealed class ParseFrame
{
    /// <summary>Gets or sets the production this frame represents.</summary>
    public ParseFrameKind Kind { get; set; }

    /// <summary>Gets or sets the sub-stage within <see cref="Kind"/> the driver should resume at.</summary>
    public int Stage { get; set; }

    /// <summary>Gets or sets the source span of the first token that started this production.</summary>
    public SourceSpan StartSpan { get; set; }

    /// <summary>Gets or sets the accumulated term items, used by <see cref="ParseFrameKind.Collection"/>.</summary>
    public List<Term>? TermItems { get; set; }

    /// <summary>Gets or sets the accumulated predicate-object pairs, used by every list-producing kind.</summary>
    public List<PredicateObject>? PredicateObjects { get; set; }

    /// <summary>Gets or sets the accumulated annotated objects, used by <see cref="ParseFrameKind.ObjectList"/>.</summary>
    public List<AnnotatedObject>? AnnotatedObjects { get; set; }

    /// <summary>Gets or sets the accumulated annotations, used by <see cref="ParseFrameKind.AnnotatedObject"/>.</summary>
    public List<Annotation>? Annotations { get; set; }

    /// <summary>Gets or sets the subject slot for triple-term and reified-triple frames.</summary>
    public Term? Subject { get; set; }

    /// <summary>Gets or sets the predicate slot for triple-term, reified-triple, and predicate-object frames.</summary>
    public Term? Predicate { get; set; }

    /// <summary>Gets or sets the object slot for triple-term and reified-triple frames.</summary>
    public Term? Object { get; set; }

    /// <summary>Gets or sets the explicit reifier slot for reified-triple and reifier-annotation frames.</summary>
    public Term? Reifier { get; set; }

    /// <summary>Gets or sets the in-progress object's term for an <see cref="ParseFrameKind.AnnotatedObject"/> frame.</summary>
    public Term? CurrentObject { get; set; }

    /// <summary>Gets or sets the accumulated triple statements of a <see cref="ParseFrameKind.GraphBlock"/> body.</summary>
    public List<TripleStatement>? Triples { get; set; }

    /// <summary>Gets or sets the graph label of a <see cref="ParseFrameKind.GraphBlock"/> frame; <see langword="null"/> for the default graph.</summary>
    public Term? Label { get; set; }

    /// <summary>Gets or sets whether a <see cref="ParseFrameKind.GraphBlock"/> was introduced by the <c>GRAPH</c> keyword (TriG).</summary>
    public bool HasKeyword { get; set; }

    /// <summary>Gets or sets whether a <see cref="ParseFrameKind.GraphBlock"/> reads a leaf label token before its opening brace.</summary>
    public bool HasLabel { get; set; }

    /// <summary>Gets or sets the <see cref="TurtleParser"/>'s graph-block flag as it stood before a <see cref="ParseFrameKind.GraphBlock"/> frame entered, restored when the frame finalises.</summary>
    public bool SavedInGraphBlock { get; set; }
}
