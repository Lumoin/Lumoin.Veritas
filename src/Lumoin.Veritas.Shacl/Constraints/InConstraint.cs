using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:in</c> — each value node must be term-equal to some member of
/// <see cref="AllowedValues"/>.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §6.4.2. Members can be any kind of term (IRIs,
/// literals, blank nodes), so <see cref="TermId"/> is used rather than a
/// narrower handle.
/// </remarks>
/// <param name="AllowedValues">The allowed value identifiers.</param>
public sealed record InConstraint(ImmutableArray<TermId> AllowedValues): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.In;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
