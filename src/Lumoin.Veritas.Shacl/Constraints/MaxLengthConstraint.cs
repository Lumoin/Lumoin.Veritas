using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:maxLength</c> — each value node's string form must have length
/// at most <see cref="MaxLength"/>.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.2.2.</remarks>
/// <param name="MaxLength">Inclusive upper bound on string length.</param>
public sealed record MaxLengthConstraint(int MaxLength): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.MaxLength;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
