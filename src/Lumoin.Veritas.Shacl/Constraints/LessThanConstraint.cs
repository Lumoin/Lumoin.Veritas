using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:lessThan</c> — every value node must be strictly less than every
/// value of the focus node at <see cref="OtherPredicateId"/>.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.8.3.</remarks>
/// <param name="OtherPredicateId">The predicate IRI for the comparison set.</param>
public sealed record LessThanConstraint(IriId OtherPredicateId): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.LessThan;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
