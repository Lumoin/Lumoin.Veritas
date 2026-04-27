using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Shared evaluation body for the four numeric-range constraints
/// (<c>sh:minInclusive</c>, <c>sh:maxInclusive</c>,
/// <c>sh:minExclusive</c>, <c>sh:maxExclusive</c>).
/// </summary>
/// <remarks>
/// <para>
/// All four constraints have the same structure: resolve the
/// constraint's bound to an <see cref="RdfTerm"/>, then for each
/// value node compare value vs bound and decide based on the
/// comparison result. The four differ only in which
/// <see cref="ComparisonResult"/> values they accept as conforming.
/// </para>
/// <para>
/// <see cref="ComparisonResult.Incomparable"/> always counts as
/// non-conformance. This covers ill-formed lexical forms, mismatched
/// datatypes, NaN, indeterminate datetime/duration comparisons, and
/// non-literal value nodes.
/// </para>
/// </remarks>
internal static class RangeEvaluatorCore
{
    /// <summary>
    /// Predicate: given the comparison of a value node to the
    /// constraint's bound, returns <c>true</c> iff the value
    /// conforms.
    /// </summary>
    public delegate bool ConformancePredicate(ComparisonResult comparisonResult);

    public static ValueTask<ImmutableArray<ValidationResult>> EvaluateAsync(
        Shape shape,
        ConstraintComponent constraint,
        TermId focusNode,
        ImmutableArray<TermId> valueNodes,
        PropertyPath? path,
        ValidationContext context,
        TermId boundTermId,
        ConformancePredicate isConforming,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(isConforming);
        cancellationToken.ThrowIfCancellationRequested();

        RdfTerm boundTerm = context.Dictionary.Resolve(boundTermId);

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            RdfTerm valueTerm = context.Dictionary.Resolve(value);
            ComparisonResult comparison = RdfValueComparer.Compare(valueTerm, boundTerm);
            if(isConforming(comparison))
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

        return ValueTask.FromResult(builder.ToImmutable());
    }
}
