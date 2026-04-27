using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:disjoint</c> — the set of value nodes must be disjoint from the
/// set of values of the focus node at <see cref="OtherPredicateId"/>.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.8.2.</remarks>
/// <param name="OtherPredicateId">The predicate IRI for the comparison set.</param>
public sealed record DisjointConstraint(IriId OtherPredicateId): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.Disjoint;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
