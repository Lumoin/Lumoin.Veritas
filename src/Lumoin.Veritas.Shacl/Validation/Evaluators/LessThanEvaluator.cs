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
/// Evaluator for <c>sh:LessThanConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.8.3: every value node must be strictly less
/// than every value found at the constraint's other predicate, under
/// SPARQL ordering semantics.
/// </para>
/// <para>
/// Conforms when the comparison of value-vs-comparison-set-member is
/// <see cref="ComparisonResult.Less"/>. Incomparable pairs are
/// violations.
/// </para>
/// </remarks>
public static class LessThanEvaluator
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

        LessThanConstraint lessThan = (LessThanConstraint)constraint;

        return PairOrderingEvaluatorCore.EvaluateAsync(
            shape, constraint, focusNode, valueNodes, path, context,
            lessThan.OtherPredicateId, IsConforming, cancellationToken);
    }

    private static bool IsConforming(ComparisonResult comparison)
        => comparison == ComparisonResult.Less;
}
