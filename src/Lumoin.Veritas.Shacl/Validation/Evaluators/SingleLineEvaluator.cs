using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:SingleLineConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.2.4: when
/// <see cref="SingleLineConstraint.SingleLine"/> is <c>true</c>, each
/// value node's string form must contain no line break (LF or CR).
/// When the flag is <c>false</c> the constraint is trivially
/// satisfied and the evaluator emits no results — the loader still
/// includes the constraint, the evaluator simply no-ops.
/// </para>
/// <para>
/// <b>Line-break definition.</b> SHACL refers to "line breaks"; this
/// evaluator treats the presence of any of <c>U+000A</c> (LF),
/// <c>U+000D</c> (CR), or the combination <c>CR LF</c> as a line
/// break. Other line-separator characters
/// (<c>U+2028</c>, <c>U+2029</c>) are not treated as line breaks
/// here, matching the behaviour of common SHACL implementations.
/// </para>
/// <para>
/// Blank nodes have no string form and therefore fail the constraint
/// when active. Violations are per-value-node.
/// </para>
/// </remarks>
public static class SingleLineEvaluator
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
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        SingleLineConstraint singleLine = (SingleLineConstraint)constraint;
        if(!singleLine.SingleLine)
        {
            //Constraint declared with sh:singleLine false — passes
            //unconditionally. No allocations, no per-value work.
            return ValueTask.FromResult(ImmutableArray<ValidationResult>.Empty);
        }

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            RdfTerm term = context.Dictionary.Resolve(value);
            string? stringForm = StringFormExtractor.Extract(term);
            bool conforms = stringForm is not null && !ContainsLineBreak(stringForm);
            if(conforms)
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

    //Test for any LF or CR. AsSpan().IndexOfAny is the JIT-vectorised
    //path on modern .NET; faster than IndexOf('\n') | IndexOf('\r')
    //and handles either-order CR/LF without a second scan.
    private static bool ContainsLineBreak(string value)
        => value.AsSpan().IndexOfAny('\n', '\r') >= 0;
}
