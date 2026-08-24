using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:MaxCountConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.5.2: a violation is produced when the number
/// of value nodes is greater than
/// <see cref="MaxCountConstraint.MaxCount"/>.
/// </para>
/// <para>
/// The violation is set-level; the emitted result leaves
/// <see cref="ValidationResult.ValueNode"/> unset. Severity is
/// inherited from the owning shape.
/// </para>
/// </remarks>
public static class MaxCountEvaluator
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
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(constraint);
        _ = context;
        cancellationToken.ThrowIfCancellationRequested();

        MaxCountConstraint maxCount = (MaxCountConstraint)constraint;

        if(valueNodes.Length <= maxCount.MaxCount)
        {
            return ValueTask.FromResult(ImmutableArray<ValidationResult>.Empty);
        }

        ValidationResult result = new()
        {
            FocusNode = focusNode,
            ResultPath = path,
            Severity = shape.Severity,
            SourceShape = shape.Id,
            SourceConstraintComponent = constraint.ConstraintComponentIri,
            Messages = shape.Messages,
        };
        return ValueTask.FromResult(ImmutableArray.Create(result));
    }
}
