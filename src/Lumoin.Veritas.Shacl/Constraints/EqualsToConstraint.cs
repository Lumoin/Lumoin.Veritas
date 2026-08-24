using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:equals</c> — the set of value nodes must be equal (as a term
/// set) to the set of values of the focus node at predicate
/// <see cref="OtherPredicateId"/>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.8.1. Unlike most constraints, this one compares
/// two sets derived from the same focus node — the shape's own value-node
/// set against the set found at a separately named predicate.
/// </para>
/// <para>
/// Named <c>EqualsToConstraint</c> (rather than <c>EqualsConstraint</c>)
/// to avoid visual conflict with <see cref="object.Equals(object)"/> in
/// pattern-match switches and call sites.
/// </para>
/// </remarks>
/// <param name="OtherPredicateId">The predicate IRI for the comparison set.</param>
public sealed record EqualsToConstraint(IriId OtherPredicateId): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.EqualsTo;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
