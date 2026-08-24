using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:InConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.4.2: each value node must be term-equal to
/// some member of <see cref="InConstraint.AllowedValues"/>. Term
/// equality here is <see cref="TermId"/> equality — the shared term
/// dictionary guarantees that two <see cref="TermId"/> values are equal
/// iff they denote the same RDF term.
/// </para>
/// <para>
/// For speed the allowed-values array is rehashed into a
/// <see cref="HashSet{T}"/> once per evaluation. This pays off for
/// constraints with more than a handful of allowed members while
/// staying linear-equivalent for tiny sets; the alternative of a
/// nested loop would be <c>O(values × allowed)</c> and degrade on
/// larger <c>sh:in</c> lists.
/// </para>
/// <para>
/// Violations are per-value-node.
/// </para>
/// </remarks>
public static class InEvaluator
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

        InConstraint inConstraint = (InConstraint)constraint;

        HashSet<TermId> allowed = [.. inConstraint.AllowedValues];
        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            if(allowed.Contains(value))
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
