using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:property</c> — the focus node must conform to the nested
/// property shape identified by <see cref="PropertyShapeId"/>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.9.2. Property constraints are the main mechanism
/// for expressing "a shape also requires this property-shaped condition."
/// </para>
/// <para>
/// <b>Kind contract.</b> The referenced shape must be a
/// <see cref="PropertyShape"/>. The loader enforces this at discovery:
/// the object of <c>sh:property</c> is classified as a property shape
/// regardless of other signals. Evaluators can safely cast the resolved
/// <see cref="Shape"/> to <see cref="PropertyShape"/>, but should
/// defensively verify the cast — a shape graph that violates the spec
/// (e.g., an <c>sh:property</c> pointing at a term also typed as
/// <c>sh:NodeShape</c>) will load, and the evaluator's validation
/// result should reflect the constraint-graph error rather than
/// crashing.
/// </para>
/// </remarks>
/// <param name="PropertyShapeId">The term id of the nested property shape.</param>
public sealed record PropertyConstraint(TermId PropertyShapeId): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.Property;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [PropertyShapeId];
}
