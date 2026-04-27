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
/// Evaluator for <c>sh:MaxExclusiveConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.1.2: each value node must be strictly less
/// than <see cref="MaxExclusiveConstraint.Bound"/>.
/// </para>
/// </remarks>
public static class MaxExclusiveEvaluator
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

        MaxExclusiveConstraint maxExclusive = (MaxExclusiveConstraint)constraint;

        return RangeEvaluatorCore.EvaluateAsync(
            shape, constraint, focusNode, valueNodes, path, context,
            maxExclusive.Bound, IsConforming, cancellationToken);
    }

    private static bool IsConforming(ComparisonResult comparison)
        => comparison == ComparisonResult.Less;
}
