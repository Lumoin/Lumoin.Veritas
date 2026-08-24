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
/// Evaluator for <c>sh:MaxInclusiveConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.1.4: each value node must be less than or
/// equal to <see cref="MaxInclusiveConstraint.Bound"/>.
/// </para>
/// <para>
/// Conforms when the comparison is
/// <see cref="ComparisonResult.Less"/> or
/// <see cref="ComparisonResult.Equal"/>. Violations per-value-node.
/// </para>
/// </remarks>
public static class MaxInclusiveEvaluator
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

        MaxInclusiveConstraint maxInclusive = (MaxInclusiveConstraint)constraint;

        return RangeEvaluatorCore.EvaluateAsync(
            shape, constraint, focusNode, valueNodes, path, context,
            maxInclusive.Bound, IsConforming, cancellationToken);
    }

    private static bool IsConforming(ComparisonResult comparison)
        => comparison is ComparisonResult.Less or ComparisonResult.Equal;
}
