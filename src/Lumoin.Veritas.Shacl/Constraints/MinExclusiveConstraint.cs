using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:minExclusive</c> — every value node must be strictly greater than
/// <see cref="Bound"/>.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §6.1.1. Comparison follows SPARQL operators for the
/// common numeric, date, and string orderings.
/// </remarks>
/// <param name="Bound">
/// The exclusive lower bound, as a term identifier. The bound is always a
/// literal in practice, but <see cref="TermId"/> is used rather than a
/// narrower type because the SHACL spec does not prohibit IRI or blank-node
/// bounds; evaluators reject non-literal bounds per §6.1.
/// </param>
public sealed record MinExclusiveConstraint(TermId Bound): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.MinExclusive;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
