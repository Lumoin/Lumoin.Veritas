using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:uniqueValuesFor</c> — each value node must not also appear as a
/// value of any other focus node at any of the listed predicates.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.10.2. Expresses key-like uniqueness constraints:
/// "this person's email must be globally unique among people."
/// </para>
/// </remarks>
/// <param name="PredicateIds">
/// The predicate IRIs across which the uniqueness is checked.
/// </param>
public sealed record UniqueValuesForConstraint(ImmutableArray<IriId> PredicateIds): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.UniqueValuesFor;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
