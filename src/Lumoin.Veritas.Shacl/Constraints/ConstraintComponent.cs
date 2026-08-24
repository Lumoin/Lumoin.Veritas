using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Shacl.Constraints;

/// <summary>
/// Abstract base for SHACL constraint components.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §3, a constraint component pairs parameters (the
/// source-shape data) with an evaluation procedure. This AST captures the
/// parameters; evaluators in <c>Lumoin.Veritas.Shacl.Evaluators</c> apply
/// the procedure against a data graph.
/// </para>
/// <para>
/// <b>Uniform traversal via term ids.</b> <see cref="ReferencedShapeIds"/>
/// is the uniform accessor. Leaf constraints return an empty enumerable;
/// shape-referencing constraints (<c>sh:not</c>, <c>sh:and</c>,
/// <c>sh:or</c>, <c>sh:xone</c>, <c>sh:node</c>, <c>sh:property</c>,
/// <c>sh:qualifiedValueShape</c>, <c>sh:memberShape</c>,
/// <c>sh:reifierShape</c>) return the term ids of the shapes they
/// reference. A shape-tree walker resolves each id against the
/// registry produced by the loader to obtain the actual
/// <see cref="Shape"/>.
/// </para>
/// <para>
/// <b>Why ids rather than shape references.</b> Holding
/// <see cref="Shape"/> values directly would force the loader into a
/// two-phase construction with snapshot-partial semantics on cycles.
/// By holding
/// <see cref="TermId"/> values and deferring resolution to evaluation
/// time, cycles become a non-issue: every lookup sees the final
/// populated <see cref="Shape"/>, regardless of the order in which
/// shapes were constructed. The cost is one dictionary lookup per
/// reference-following at evaluation time, which is negligible against
/// the work of actually validating against a data graph.
/// </para>
/// <para>
/// <see cref="ConstraintComponentIri"/> is the IRI of the constraint
/// component itself (e.g. <c>sh:MinCountConstraintComponent</c>), used as
/// <c>sh:sourceConstraintComponent</c> in validation results per SHACL
/// Core §4.3. Returned as a <see cref="Utf8String"/> pointing at the
/// corresponding entry in <c>ShaclComponentVocabulary</c> — no
/// per-access string concatenation, constant-time byte-content equality
/// with dictionary-interned IRIs.
/// </para>
/// </remarks>
public abstract record ConstraintComponent
{
    /// <summary>
    /// The SHACL constraint-component IRI, emitted as
    /// <c>sh:sourceConstraintComponent</c> on validation results.
    /// </summary>
    public abstract Utf8String ConstraintComponentIri { get; }

    /// <summary>
    /// The term ids of shapes structurally referenced by this
    /// constraint. Empty for leaf constraints; contains one or more
    /// ids for combinators and shape-referencing constraints. Resolve
    /// each id against the loader-produced shape registry to obtain
    /// the actual <see cref="Shape"/>.
    /// </summary>
    public abstract IEnumerable<TermId> ReferencedShapeIds { get; }

    /// <summary>Optional source-range annotation for diagnostics. <c>null</c> when loaded from RDF.</summary>
    public SourceSpan? Span { get; init; }
}
