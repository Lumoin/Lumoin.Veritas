using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:QualifiedMaxCountConstraintComponent</c> — the count of value
/// nodes that conform to the inner shape (and that, if
/// <see cref="Disjoint"/> is <c>true</c>, do not also conform to any
/// sibling qualified value shape) must be at most
/// <see cref="MaxCount"/>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §4.7.5. Sibling of
/// <see cref="QualifiedMinCountConstraint"/>; see that record for the
/// rationale behind keeping the two as separate types.
/// </para>
/// </remarks>
/// <param name="ValueShapeId">The term id of the inner shape against which values are counted.</param>
/// <param name="MaxCount">Inclusive upper bound on the conforming-value count.</param>
/// <param name="Disjoint">Whether sibling qualified-value-shape constraints subtract from the count.</param>
public sealed record QualifiedMaxCountConstraint(
    TermId ValueShapeId,
    int MaxCount,
    bool Disjoint): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.QualifiedMaxCount;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [ValueShapeId];
}
