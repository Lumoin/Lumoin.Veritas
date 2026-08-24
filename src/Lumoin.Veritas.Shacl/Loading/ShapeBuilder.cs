using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Shacl.Targets;

namespace Lumoin.Veritas.Shacl.Loading;

/// <summary>
/// Internal mutable carrier used during shape population. Not exposed
/// to callers; the loader converts a populated builder into an
/// immutable <see cref="Shape"/> before handing it out.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a builder.</b> <see cref="Shape"/> is an immutable record with
/// required init-only properties. The loader accumulates per-shape
/// state in a builder during the population pass — targets, metadata,
/// parsed path, constraints — and calls <see cref="Build"/> once to
/// produce the sealed <see cref="Shape"/>.
/// </para>
/// <para>
/// <b>Single-pass population.</b> Because shape-referencing constraints
/// hold <see cref="TermId"/> values rather than <see cref="Shape"/>
/// references, the loader no longer needs a phase split between
/// "non-shape-referencing" and "shape-referencing" constraint
/// population — every factory can run during one population pass
/// regardless of what other shapes its output refers to, because
/// references are captured as ids resolved later against the finished
/// registry. All constraint components accumulate into a single
/// <see cref="Constraints"/> builder.
/// </para>
/// <para>
/// <b>Shape kind.</b> <see cref="IsPropertyShape"/> is decided during
/// discovery based on whether the shape carries a <c>sh:path</c>.
/// The parsed <see cref="Path"/> is populated during the population
/// pass along with targets and metadata.
/// </para>
/// </remarks>
internal sealed class ShapeBuilder
{
    /// <summary>
    /// Creates a builder for a newly-discovered shape. Only
    /// <see cref="Id"/> and <see cref="IsPropertyShape"/> are known at
    /// this point; everything else is filled in during the population
    /// pass.
    /// </summary>
    /// <param name="id">The shape's term identifier.</param>
    /// <param name="isPropertyShape">
    /// <c>true</c> if the shape carries <c>sh:path</c> and is therefore a
    /// <see cref="PropertyShape"/>; <c>false</c> for a <see cref="NodeShape"/>.
    /// </param>
    public ShapeBuilder(TermId id, bool isPropertyShape)
    {
        Id = id;
        IsPropertyShape = isPropertyShape;
    }

    /// <summary>The shape's term identifier.</summary>
    public TermId Id { get; }

    /// <summary>
    /// <c>true</c> if this builder will produce a
    /// <see cref="PropertyShape"/>; <c>false</c> for a
    /// <see cref="NodeShape"/>.
    /// </summary>
    public bool IsPropertyShape { get; }

    /// <summary>
    /// For <see cref="PropertyShape"/> builders: the parsed property
    /// path. <c>null</c> on node-shape builders and on property-shape
    /// builders that have not yet completed population.
    /// </summary>
    public PropertyPath? Path { get; set; }

    /// <summary>Targets declared on the shape.</summary>
    public ImmutableArray<Target>.Builder Targets { get; } = ImmutableArray.CreateBuilder<Target>();

    /// <summary>
    /// Severity. Defaults to <see cref="Severity.Violation"/>; overridden
    /// during population if the shape carries an explicit <c>sh:severity</c>.
    /// </summary>
    public Severity Severity { get; set; } = Severity.Violation;

    /// <summary>Whether the shape is deactivated.</summary>
    public bool Deactivated { get; set; }

    /// <summary>
    /// Human-readable validation-result messages, keyed by language tag
    /// (<c>""</c> for non-tagged).
    /// </summary>
    public Dictionary<string, string> Messages { get; } = [];

    /// <summary>
    /// All constraint components on this shape, in population order.
    /// Leaf constraints and shape-referencing constraints live together
    /// in one list — the R-4 refactor eliminated the need for a split,
    /// because shape references are now captured as
    /// <see cref="TermId"/> values rather than <see cref="Shape"/>
    /// references.
    /// </summary>
    public ImmutableArray<ConstraintComponent>.Builder Constraints { get; } = ImmutableArray.CreateBuilder<ConstraintComponent>();

    /// <summary>Optional source-range annotation.</summary>
    public SourceSpan? Span { get; set; }

    /// <summary>
    /// Produces the immutable <see cref="Shape"/> from the accumulated
    /// builder state. Called once per builder, after the population
    /// pass completes for this shape.
    /// </summary>
    /// <returns>The sealed shape record.</returns>
    public Shape Build()
    {
        ImmutableDictionary<string, string> messages = Messages.Count == 0
            ? ImmutableDictionary<string, string>.Empty
            : Messages.ToImmutableDictionary();

        if(IsPropertyShape)
        {
            if(Path is null)
            {
                throw new InvalidOperationException(
                    $"Property shape {Id} has no parsed sh:path; loader bug.");
            }

            return new PropertyShape
            {
                Id = Id,
                Path = Path,
                Targets = Targets.ToImmutable(),
                Severity = Severity,
                Deactivated = Deactivated,
                Messages = messages,
                Constraints = Constraints.ToImmutable(),
                Span = Span,
            };
        }

        return new NodeShape
        {
            Id = Id,
            Targets = Targets.ToImmutable(),
            Severity = Severity,
            Deactivated = Deactivated,
            Messages = messages,
            Constraints = Constraints.ToImmutable(),
            Span = Span,
        };
    }
}
