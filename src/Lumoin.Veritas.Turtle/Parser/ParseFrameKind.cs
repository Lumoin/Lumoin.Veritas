namespace Lumoin.Veritas.Turtle.Parser;

/// <summary>
/// The discriminator on <see cref="ParseFrame"/>. Each value names one
/// grammar production the work-stack driver in
/// <see cref="TurtleParser"/> can be inside. The driver dispatches on
/// this enum and the frame's <see cref="ParseFrame.Stage"/> to advance
/// the parse without recursive descent.
/// </summary>
/// <remarks>
/// Public because the caret-aware completion seam
/// (<see cref="TurtleParser.OpenFrames"/> and the <c>Completion</c> namespace) reports the open production
/// chain at a caret as a sequence of these values — the enclosing-production context a completion consumer
/// renders.
/// </remarks>
public enum ParseFrameKind
{
    /// <summary>A top-level statement dispatch: a directive, a TriG graph block, or a subject-starting statement.</summary>
    Statement,

    /// <summary>A subject-starting statement: a subject term followed by a predicate-object list and terminator, or — in TriG — a labelled graph block.</summary>
    SubjectStatement,

    /// <summary>A TriG graph block <c>label? { triples }</c>: accumulates the inner triple statements until the close brace.</summary>
    GraphBlock,

    /// <summary>A term-position dispatch: reads the current token and either produces a leaf or pushes a sub-frame for a complex form.</summary>
    Term,

    /// <summary>Inside <c>( ... )</c>: accumulating term items until the close paren.</summary>
    Collection,

    /// <summary>Inside <c>[ ... ]</c>: accumulating predicate-object pairs until the close bracket.</summary>
    BlankNodePropertyList,

    /// <summary>Inside <c>&lt;&lt;( ... )&gt;&gt;</c>: parses subject, predicate, object.</summary>
    TripleTerm,

    /// <summary>Inside <c>&lt;&lt; ... &gt;&gt;</c>: parses subject, predicate, object, optional reifier.</summary>
    ReifiedTriple,

    /// <summary>A top-level predicate-object list (used inside a triple statement). Accumulates predicate-object pairs separated by <c>;</c>.</summary>
    PredicateObjectList,

    /// <summary>One predicate paired with an object list.</summary>
    PredicateObject,

    /// <summary>An object list: one or more annotated objects separated by <c>,</c>.</summary>
    ObjectList,

    /// <summary>An object together with its trailing annotations.</summary>
    AnnotatedObject,

    /// <summary>An annotation block <c>{| pol |}</c>: accumulates the inner predicate-object list.</summary>
    AnnotationBlock,

    /// <summary>A reifier annotation <c>~</c> with an optional identifier.</summary>
    Reifier
}
