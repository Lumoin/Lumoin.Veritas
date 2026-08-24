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
/// Evaluator for <c>sh:MinLengthConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.2.1: each value node's string form must have
/// length at least <see cref="MinLengthConstraint.MinLength"/>. The
/// string form is the lexical form for literals and the IRI string
/// for IRI nodes. Blank nodes have no string form per SHACL and
/// therefore always fail this constraint.
/// </para>
/// <para>
/// <b>Length semantics.</b> The SHACL spec talks about the length of
/// the string. This evaluator measures the length of the UTF-16
/// representation (the .NET <see cref="string.Length"/> property),
/// which counts surrogate-pair characters as two. For BMP-only
/// content this matches the intuitive notion of "characters"; for
/// content containing supplementary-plane characters the count is
/// the UTF-16 code-unit count, not the Unicode scalar count. This
/// matches the behaviour of common SHACL implementations.
/// </para>
/// <para>
/// Violations are per-value-node.
/// </para>
/// </remarks>
public static class MinLengthEvaluator
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

        MinLengthConstraint minLength = (MinLengthConstraint)constraint;
        int floor = minLength.MinLength;

        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            RdfTerm term = context.Dictionary.Resolve(value);
            string? stringForm = StringFormExtractor.Extract(term);
            bool conforms = stringForm is not null && stringForm.Length >= floor;
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
}
