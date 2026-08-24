using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:maxExclusive</c> — every value node must be strictly less than
/// <see cref="Bound"/>.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.1.2.</remarks>
/// <param name="Bound">
/// The exclusive upper bound, as a term identifier. Evaluators reject
/// non-literal bounds per §6.1.
/// </param>
public sealed record MaxExclusiveConstraint(TermId Bound): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.MaxExclusive;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
