using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:closed</c> with <c>sh:ignoredProperties</c> — the focus node
/// must not have outgoing triples with predicates outside the set of
/// predicates explicitly named by the shape's property shapes, plus
/// <see cref="IgnoredPredicateIds"/>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.10.1. Only applies to node shapes. The "allowed"
/// set is the union of the predicates named by the shape's own property
/// shapes and the ignored-properties list — i.e., the shape itself does
/// not carry this list; the loader resolves it from the context.
/// </para>
/// <para>
/// The allowed predicates are determined by the containing shape — this
/// record captures only the constraint-specific parts
/// (<c>sh:closed</c> activation and the optional ignored-properties list).
/// </para>
/// </remarks>
/// <param name="Closed">Whether the closed-world constraint is active.</param>
/// <param name="IgnoredPredicateIds">Predicates explicitly allowed in addition to the shape's property shapes.</param>
public sealed record ClosedConstraint(
    bool Closed,
    ImmutableArray<IriId> IgnoredPredicateIds): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.Closed;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
