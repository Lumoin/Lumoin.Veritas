using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:LessThanOrEqualsConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.8.4: every value node must be less than or
/// equal to every value found at the constraint's other predicate.
/// </para>
/// <para>
/// Conforms when the comparison is
/// <see cref="ComparisonResult.Less"/> or
/// <see cref="ComparisonResult.Equal"/>.
/// </para>
/// </remarks>
public static class LessThanOrEqualsEvaluator
{
    public static ValueTask<ImmutableArray<ValidationResult>> EvaluateAsync(
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

        LessThanOrEqualsConstraint lessThanOrEquals = (LessThanOrEqualsConstraint)constraint;

        return PairOrderingEvaluatorCore.EvaluateAsync(
            shape, constraint, focusNode, valueNodes, path, context,
            lessThanOrEquals.OtherPredicateId, IsConforming, cancellationToken);
    }

    private static bool IsConforming(ComparisonResult comparison)
        => comparison is ComparisonResult.Less or ComparisonResult.Equal;
}
