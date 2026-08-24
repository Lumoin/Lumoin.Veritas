using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:minLength</c> — each value node's string form must have length
/// at least <see cref="MinLength"/>.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §6.2.1. The string form is the lexical form for
/// literals and the IRI string for IRIs. Blank nodes fail the constraint.
/// </remarks>
/// <param name="MinLength">Inclusive lower bound on string length.</param>
public sealed record MinLengthConstraint(int MinLength): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.MinLength;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
