using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:qualifiedValueShape</c> with <c>sh:qualifiedMinCount</c>,
/// <c>sh:qualifiedMaxCount</c>, and optionally
/// <c>sh:qualifiedValueShapesDisjoint</c> — counts the number of value
/// nodes conforming to an inner shape and requires the count to fall
/// within the given range.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.9.3 through §6.9.6. These four parameters are
/// logically bound together: a shape uses <c>sh:qualifiedValueShape</c>
/// to declare an inner shape, and at least one of
/// <c>sh:qualifiedMinCount</c> or <c>sh:qualifiedMaxCount</c> to specify
/// the cardinality constraint.
/// </para>
/// <para>
/// <b>Composite modeling rationale.</b> The spec splits these into four
/// separate constraint components but their semantics only compose when
/// all come from the same property shape instance. Modeling them as one
/// record makes loader validation and evaluator dispatch natural. The
/// <see cref="ConstraintComponentIri"/> override selects between
/// <c>sh:QualifiedMinCountConstraintComponent</c> and
/// <c>sh:QualifiedMaxCountConstraintComponent</c> based on which count
/// parameter is present, matching the spec's bifurcated component
/// identity.
/// </para>
/// </remarks>
/// <param name="ValueShapeId">The term id of the inner shape against which values are counted.</param>
/// <param name="MinCount">Inclusive lower bound on the conforming-value count, or <c>null</c> if absent.</param>
/// <param name="MaxCount">Inclusive upper bound on the conforming-value count, or <c>null</c> if absent.</param>
/// <param name="Disjoint">Whether sibling qualified-value-shape constraints must be checked for disjointness.</param>
public sealed record QualifiedValueShapeConstraint(
    TermId ValueShapeId,
    int? MinCount,
    int? MaxCount,
    bool Disjoint): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => MinCount.HasValue
        ? ShaclComponentVocabulary.QualifiedMinCount
        : ShaclComponentVocabulary.QualifiedMaxCount;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [ValueShapeId];
}
