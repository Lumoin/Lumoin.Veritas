using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:memberShape</c> (SHACL 1.2) — every member of the value node's
/// RDF list must conform to the shape identified by
/// <see cref="MemberShapeId"/>.
/// </summary>
/// <remarks>
/// Per SHACL 1.2 Core §6.12. Applies only to value nodes that are RDF
/// list heads. The reference is stored as a term id; evaluators resolve
/// it against the loader's shape registry at validation time.
/// </remarks>
/// <param name="MemberShapeId">The term id of the shape each list member must conform to.</param>
public sealed record MemberShapeConstraint(TermId MemberShapeId): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.MemberShape;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [MemberShapeId];
}
