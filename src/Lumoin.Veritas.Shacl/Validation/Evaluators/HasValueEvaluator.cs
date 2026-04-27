using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:HasValueConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.4.1: at least one value node must be term-equal
/// to the constraint's <see cref="HasValueConstraint.RequiredValueId"/>.
/// </para>
/// <para>
/// <b>Term equality, not value equality.</b> The required-value match
/// is by <see cref="TermId"/> identity — i.e., same encoded term in
/// the dictionary. Two literals with different lexical forms or
/// different datatype IRIs are not equal even when they represent the
/// same SPARQL value (e.g., <c>"5"^^xsd:integer</c> and
/// <c>"5.0"^^xsd:decimal</c> are not <c>sh:hasValue</c>-equivalent).
/// This is the SHACL spec semantics, not a simplification.
/// </para>
/// <para>
/// <b>Result shape.</b> Set-level. One outer result is emitted if no
/// value node matches; the result has no <see cref="ValidationResult.ValueNode"/>
/// because the violation is about the absence of a value, not about
/// any specific value node.
/// </para>
/// </remarks>
public static class HasValueEvaluator
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

        HasValueConstraint hasValue = (HasValueConstraint)constraint;
        TermId required = hasValue.RequiredValueId;

        foreach(TermId value in valueNodes)
        {
            if(value.Equals(required))
            {
                return ValueTask.FromResult(ImmutableArray<ValidationResult>.Empty);
            }
        }

        ImmutableArray<ValidationResult> results = ImmutableArray.Create(new ValidationResult
        {
            FocusNode = focusNode,
            ResultPath = path,
            Severity = shape.Severity,
            SourceShape = shape.Id,
            SourceConstraintComponent = constraint.ConstraintComponentIri,
            Messages = shape.Messages,
        });

        return ValueTask.FromResult(results);
    }
}
