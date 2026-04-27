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
/// Evaluator for <c>sh:MinExclusiveConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.1.1: each value node must be strictly
/// greater than <see cref="MinExclusiveConstraint.Bound"/>. Equality
/// fails this constraint, unlike <see cref="MinInclusiveEvaluator"/>.
/// </para>
/// </remarks>
public static class MinExclusiveEvaluator
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

        MinExclusiveConstraint minExclusive = (MinExclusiveConstraint)constraint;

        return RangeEvaluatorCore.EvaluateAsync(
            shape, constraint, focusNode, valueNodes, path, context,
            minExclusive.Bound, IsConforming, cancellationToken);
    }

    private static bool IsConforming(ComparisonResult comparison)
        => comparison == ComparisonResult.Greater;
}
