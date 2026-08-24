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
/// Evaluator for <c>sh:PatternConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.2.3: each value node's lexical form must
/// match the constraint's regex pattern. The shape loader already
/// compiled the regex into <see cref="PatternConstraint.Compiled"/>
/// when the shape was loaded, so evaluation is a direct
/// <c>IsMatch</c> call with no per-run parse cost.
/// </para>
/// <para>
/// <b>Term kinds.</b> Non-literal value nodes (blank nodes, IRIs) fail
/// the match — the SHACL spec applies the pattern to the lexical form,
/// and blank nodes have no stable lexical form. IRIs are tested against
/// their IRI string. Literals are tested against their lexical value
/// component, ignoring any datatype or language tag.
/// </para>
/// <para>
/// Violations are per-value-node.
/// </para>
/// </remarks>
public static class PatternEvaluator
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

        PatternConstraint pattern = (PatternConstraint)constraint;
        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            RdfTerm term = context.Dictionary.Resolve(value);
            string? lexical = ExtractLexicalForm(term);
            bool matches = lexical is not null && pattern.Compiled.IsMatch(lexical);
            if(matches)
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

    //Lexical form of a term for pattern-matching purposes. Literals
    //contribute their value component; IRIs their IRI string; blank
    //nodes return null to signal "no matchable lexical form" and thus
    //a guaranteed mismatch.
    private static string? ExtractLexicalForm(RdfTerm term)
        => term switch
        {
            Literal literal => literal.Value.ToString(),
            NamedNode named => named.Iri.ToString(),
            _ => null,
        };
}
