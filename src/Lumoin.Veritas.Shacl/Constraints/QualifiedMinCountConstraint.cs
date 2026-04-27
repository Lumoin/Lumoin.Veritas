using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:QualifiedMinCountConstraintComponent</c> — the count of value
/// nodes that conform to the inner shape (and that, if
/// <see cref="Disjoint"/> is <c>true</c>, do not also conform to any
/// sibling qualified value shape) must be at least
/// <see cref="MinCount"/>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §4.7.4. This is a sibling of
/// <see cref="QualifiedMaxCountConstraint"/>: a property shape can
/// declare both, sharing the inner shape and the disjoint flag, and
/// the loader emits one record of each type. Each constraint component
/// is independently evaluated by its own evaluator.
/// </para>
/// <para>
/// <b>Why two records rather than one combined record.</b> The SHACL
/// specification defines
/// <c>sh:QualifiedMinCountConstraintComponent</c> and
/// <c>sh:QualifiedMaxCountConstraintComponent</c> as two distinct
/// constraint components. Each is independently identified by its
/// <c>sh:sourceConstraintComponent</c> on validation results and
/// independently violated. Modelling them as one record bundling both
/// counts forced a one-result-per-record contract that did not match
/// the spec's two-results-when-both-fail semantics, and it forced
/// non-local reasoning at edit time ("which other triples are on this
/// shape?"). Two records — each carrying the parameters its component
/// takes, with shared parameters duplicated — keep every constraint
/// instance fully self-describing and locally constructible from a
/// single triple set.
/// </para>
/// <para>
/// <b>Sibling-disjoint subtraction.</b> When <see cref="Disjoint"/>
/// is <c>true</c>, the evaluator excludes from the conforming count
/// any value node that also conforms to a sibling property shape's
/// <c>sh:qualifiedValueShape</c>, where "sibling" follows SHACL Core
/// §4.7.4: another property shape on a node shape that references
/// this constraint's containing shape via <c>sh:property</c>.
/// </para>
/// </remarks>
/// <param name="ValueShapeId">The term id of the inner shape against which values are counted.</param>
/// <param name="MinCount">Inclusive lower bound on the conforming-value count.</param>
/// <param name="Disjoint">Whether sibling qualified-value-shape constraints subtract from the count.</param>
public sealed record QualifiedMinCountConstraint(
    TermId ValueShapeId,
    int MinCount,
    bool Disjoint): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.QualifiedMinCount;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [ValueShapeId];
}
