using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:lessThanOrEquals</c> — every value node must be less than or
/// equal to every value of the focus node at <see cref="OtherPredicateId"/>.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.8.4.</remarks>
/// <param name="OtherPredicateId">The predicate IRI for the comparison set.</param>
public sealed record LessThanOrEqualsConstraint(IriId OtherPredicateId): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.LessThanOrEquals;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
