using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:subsetOf</c> — the set of value nodes must be a subset of the
/// set of values of the focus node at predicate <see cref="OtherPredicateId"/>.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.8.5.</remarks>
/// <param name="OtherPredicateId">The predicate IRI for the superset.</param>
public sealed record SubsetOfConstraint(IriId OtherPredicateId): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.SubsetOf;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
