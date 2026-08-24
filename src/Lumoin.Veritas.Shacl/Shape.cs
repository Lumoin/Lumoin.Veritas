using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Shacl.Targets;

namespace Lumoin.Veritas.Shacl;

/// <summary>
/// Abstract base for SHACL shapes.
/// </summary>
/// <remarks>
/// <para>
/// A shape describes a set of conditions that focus nodes must satisfy.
/// Per SHACL 1.2 Core §2, shapes are classified as <see cref="NodeShape"/>
/// (apply to a focus node itself) or <see cref="PropertyShape"/> (apply
/// to value nodes along a path from the focus node).
/// </para>
/// <para>
/// <b>Uniform traversal via term ids.</b> <see cref="ReferencedShapeIds"/>
/// enables any shape-tree walker — validator, serializer, graph renderer,
/// future code generator — to uniformly follow references by
/// <see cref="TermId"/> without knowing each constraint or shape type
/// individually. Callers resolve ids against the loader-produced
/// registry to obtain actual <see cref="Shape"/> values.
/// </para>
/// <para>
/// Shapes are immutable records with structural equality. The shape
/// loader in <c>Lumoin.Veritas.Shacl.Loading</c> builds them in a single
/// population pass; because shape-referencing constraints hold
/// <see cref="TermId"/> values rather than <see cref="Shape"/>
/// references, cycles in the shape graph pose no loader-time ordering
/// problem — every id can be captured immediately and resolved later.
/// </para>
/// </remarks>
public abstract record Shape
{
    /// <summary>
    /// The identifier of this shape — an IRI or blank-node term.
    /// </summary>
    public required TermId Id { get; init; }

    /// <summary>
    /// The targets declared on this shape. Each target yields a set of
    /// focus nodes when expanded against a data graph.
    /// </summary>
    public ImmutableArray<Target> Targets { get; init; } = [];

    /// <summary>
    /// The severity assigned to validation results produced by this
    /// shape's constraints. Defaults to <see cref="Shacl.Severity.Violation"/>.
    /// </summary>
    public Severity Severity { get; init; } = Severity.Violation;

    /// <summary>
    /// If <c>true</c>, the shape is inactive — constraints are not
    /// evaluated, targets are not expanded. Per SHACL Core §4.5.
    /// </summary>
    public bool Deactivated { get; init; }

    /// <summary>
    /// Human-readable messages to attach to validation results from this
    /// shape, keyed by language tag (<c>""</c> for non-tagged). Per
    /// SHACL Core §4.7.
    /// </summary>
    public ImmutableDictionary<string, string> Messages { get; init; } = ImmutableDictionary<string, string>.Empty;

    /// <summary>
    /// The constraint components attached to this shape. Each represents
    /// one constraint parameter from the source shapes graph.
    /// </summary>
    public ImmutableArray<ConstraintComponent> Constraints { get; init; } = [];

    /// <summary>Optional source-range annotation for diagnostics. <c>null</c> when loaded from RDF.</summary>
    public SourceSpan? Span { get; init; }

    /// <summary>
    /// The term ids of shapes structurally referenced by this shape
    /// through its constraints.
    /// </summary>
    /// <remarks>
    /// This uniform accessor is the basis for shape-tree walkers. A walker
    /// collects this enumerable per shape, resolves each
    /// <see cref="TermId"/> against the loader's shape registry to obtain
    /// the referenced <see cref="Shape"/>, and recurses. New constraint
    /// types automatically participate by implementing
    /// <see cref="ConstraintComponent.ReferencedShapeIds"/>.
    /// </remarks>
    public IEnumerable<TermId> ReferencedShapeIds
        => Constraints.SelectMany(static c => c.ReferencedShapeIds);
}
