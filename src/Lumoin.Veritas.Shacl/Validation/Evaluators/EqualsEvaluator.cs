using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:EqualsConstraintComponent</c> — the constraint
/// record is named <see cref="EqualsToConstraint"/> in this project to
/// avoid clashing with <see cref="object.Equals(object)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.8.1: the value-node set must equal — as a
/// term set — the value set obtained from the focus node via the
/// constraint's other predicate.
/// </para>
/// <para>
/// <b>Term equality.</b> Equality here is RDF-term equality
/// (<see cref="TermId"/> identity in the dictionary), not
/// SPARQL-ordering equality. Two literals
/// <c>"5"^^xsd:integer</c> and <c>"5.0"^^xsd:decimal</c> are
/// <em>not</em> equal under this constraint even though their values
/// are numerically equal.
/// </para>
/// <para>
/// <b>Result shape.</b> Per-element. Two kinds of mismatch are
/// reported: a value node not present in the comparison set (with
/// <see cref="ValidationResult.ValueNode"/> set to that value), and a
/// comparison-set value not present in the value nodes (with the
/// comparison-set value as the <see cref="ValidationResult.ValueNode"/>).
/// </para>
/// </remarks>
public static class EqualsEvaluator
{
    /// <summary>
    /// The evaluator function. Matches the
    /// <see cref="ConstraintEvaluator"/> delegate shape.
    /// </summary>
    public static async ValueTask<ImmutableArray<ValidationResult>> EvaluateAsync(
        Shape shape,
        ConstraintComponent constraint,
        TermId focusNode,
        ImmutableArray<TermId> valueNodes,
        PropertyPath? path,
        ValidationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        EqualsToConstraint equalsTo = (EqualsToConstraint)constraint;
        ImmutableArray<TermId> comparisonSet = await PairPropertyComparisonSet.CollectAsync(
            focusNode, equalsTo.OtherPredicateId, context, cancellationToken).ConfigureAwait(false);

        HashSet<TermId> valueSet = new(valueNodes);
        HashSet<TermId> compareSet = new(comparisonSet);

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        //Values in the shape's set but missing from the comparison set.
        foreach(TermId value in valueNodes)
        {
            if(!compareSet.Contains(value))
            {
                builder.Add(BuildResult(shape, constraint, focusNode, value, path));
            }
        }

        //Values in the comparison set but missing from the shape's set.
        foreach(TermId comparison in comparisonSet)
        {
            if(!valueSet.Contains(comparison))
            {
                builder.Add(BuildResult(shape, constraint, focusNode, comparison, path));
            }
        }

        return builder.ToImmutable();
    }

    private static ValidationResult BuildResult(
        Shape shape, ConstraintComponent constraint, TermId focusNode, TermId valueNode, PropertyPath? path)
        => new()
        {
            FocusNode = focusNode,
            ValueNode = valueNode,
            ResultPath = path,
            Severity = shape.Severity,
            SourceShape = shape.Id,
            SourceConstraintComponent = constraint.ConstraintComponentIri,
            Messages = shape.Messages,
        };
}
