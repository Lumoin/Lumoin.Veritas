using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Shacl.Components;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// <c>sh:reifierShape</c> (SHACL 1.2) — reifier nodes of each value triple
/// term must conform to the shape identified by
/// <see cref="ReifierShapeId"/>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.11. Applies when value nodes are
/// <see href="https://www.w3.org/TR/rdf12-concepts/">RDF 1.2 triple terms</see>.
/// </para>
/// <para>
/// Evaluation requires RDF 1.2 support in the underlying graph store and
/// is deferred until the triple-term plumbing reaches the Rdf project.
/// The record is loaded and structurally represented; the evaluator
/// throws <see cref="System.NotImplementedException"/>.
/// </para>
/// </remarks>
/// <param name="ReifierShapeId">The term id of the shape reifier nodes must conform to.</param>
/// <param name="ReificationRequired">Whether reification is mandatory (<c>sh:reificationRequired true</c>).</param>
public sealed record ReifierShapeConstraint(
    TermId ReifierShapeId,
    bool ReificationRequired): ConstraintComponent
{
    /// <inheritdoc/>
    public override Utf8String ConstraintComponentIri => ShaclComponentVocabulary.ReifierShape;

    /// <inheritdoc/>
    public override IEnumerable<TermId> ReferencedShapeIds => [ReifierShapeId];
}
