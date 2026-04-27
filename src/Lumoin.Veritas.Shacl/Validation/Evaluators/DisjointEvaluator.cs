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
/// Evaluator for <c>sh:DisjointConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.8.2: the value-node set must be disjoint from
/// the value set found at the constraint's other predicate. As with
/// <see cref="EqualsEvaluator"/>, equality is RDF-term equality, not
/// SPARQL-ordering equality.
/// </para>
/// <para>
/// <b>Result shape.</b> Per-element. Each value present in both sets
/// produces one result, with that value as the
/// <see cref="ValidationResult.ValueNode"/>.
/// </para>
/// </remarks>
public static class DisjointEvaluator
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

        DisjointConstraint disjoint = (DisjointConstraint)constraint;
        ImmutableArray<TermId> comparisonSet = await PairPropertyComparisonSet.CollectAsync(
            focusNode, disjoint.OtherPredicateId, context, cancellationToken).ConfigureAwait(false);

        if(comparisonSet.Length == 0)
        {
            //Empty comparison set is trivially disjoint from anything.
            return [];
        }

        HashSet<TermId> compareSet = new(comparisonSet);
        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            if(compareSet.Contains(value))
            {
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
        }

        return builder.ToImmutable();
    }
}
