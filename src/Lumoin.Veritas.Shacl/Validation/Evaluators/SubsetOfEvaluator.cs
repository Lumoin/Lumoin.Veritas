using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:SubsetOfConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.8.5: the set of value nodes must be a
/// subset of the values of the focus node at the constraint's other
/// predicate. As with the other property-pair evaluators, equality
/// is RDF-term equality.
/// </para>
/// <para>
/// <b>Result shape.</b> Per-element. Each value present in the
/// value-node set but absent from the comparison set produces one
/// result, with that value as the
/// <see cref="ValidationResult.ValueNode"/>. The empty value-node
/// set is trivially a subset of anything and emits no results.
/// </para>
/// </remarks>
public static class SubsetOfEvaluator
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

        if(valueNodes.IsEmpty)
        {
            //Empty set is trivially a subset; nothing to check.
            return [];
        }

        SubsetOfConstraint subset = (SubsetOfConstraint)constraint;
        ImmutableArray<TermId> comparisonSet = await PairPropertyComparisonSet.CollectAsync(
            focusNode, subset.OtherPredicateId, context, cancellationToken).ConfigureAwait(false);

        HashSet<TermId> compareSet = new(comparisonSet);
        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            if(compareSet.Contains(value))
            {
                continue;
            }

            builder.Add(new ValidationResult
            {
                FocusNode = focusNode,
                ValueNode = value,
                ResultPath = path,
                Severity = shape.Severity,
                SourceShape = shape.Id,
                SourceConstraintComponent = constraint.ConstraintComponentIri,
                Messages = shape.Messages,
            });
        }

        return builder.ToImmutable();
    }
}
