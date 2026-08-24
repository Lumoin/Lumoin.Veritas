using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:singleLine</c> — when <c>true</c>, each value node's string form
/// must not contain any line break.
/// </summary>
/// <remarks>Per SHACL 1.2 Core §6.2.4.</remarks>
/// <param name="SingleLine">Whether the single-line constraint is active.</param>
public sealed record SingleLineConstraint(bool SingleLine): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.SingleLine;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
