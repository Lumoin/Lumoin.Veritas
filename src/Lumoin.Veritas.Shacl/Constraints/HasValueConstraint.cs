using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:hasValue</c> — at least one value node must be term-equal to
/// <see cref="RequiredValueId"/>.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §6.4.1. The required value can be any term — an
/// IRI, a literal, or a blank node — so <see cref="TermId"/> is the
/// appropriate type rather than a narrower <c>IriId</c>.
/// </remarks>
/// <param name="RequiredValueId">The required value identifier.</param>
public sealed record HasValueConstraint(TermId RequiredValueId): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.HasValue;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [];
}
