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
/// Evaluator for <c>sh:MinInclusiveConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.1.3: each value node must be greater than
/// or equal to the constraint's <see cref="MinInclusiveConstraint.Bound"/>
/// under SPARQL ordering semantics.
/// </para>
/// <para>
/// Conforms when the comparison result is
/// <see cref="ComparisonResult.Equal"/> or
/// <see cref="ComparisonResult.Greater"/>. Rejects
/// <see cref="ComparisonResult.Less"/> and
/// <see cref="ComparisonResult.Incomparable"/> — the latter covers
/// ill-formed lexical forms, NaN, datatype mismatches, and
/// indeterminate datetime/duration comparisons.
/// </para>
/// </remarks>
public static class MinInclusiveEvaluator
{
    /// <summary>
    /// The evaluator function. Matches the
    /// <see cref="ConstraintEvaluator"/> delegate shape.
    /// </summary>
    public static ValueTask<ImmutableArray<ValidationResult>> EvaluateAsync(
        Shape shape,
        ConstraintComponent constraint,
        TermId focusNode,
        ImmutableArray<TermId> valueNodes,
        PropertyPath? path,
        ValidationContext context,
        CancellationToken cancellationToken)
    {
        //CA1062: argument-null checks must be at every public entry
        //point. RangeEvaluatorCore also checks, but the analyzer
        //tracks per-method without inter-procedural reasoning.
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(context);

        MinInclusiveConstraint minInclusive = (MinInclusiveConstraint)constraint;

        return RangeEvaluatorCore.EvaluateAsync(
            shape, constraint, focusNode, valueNodes, path, context,
            minInclusive.Bound, IsConforming, cancellationToken);
    }

    private static bool IsConforming(ComparisonResult comparison)
        => comparison is ComparisonResult.Equal or ComparisonResult.Greater;
}
