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
/// Evaluator for <c>sh:NodeKindConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.3.3: each value node must match the declared
/// <see cref="NodeKindConstraint.Kind"/>. The six kinds combine three
/// term categories (IRI, blank node, literal) as singletons or pairs:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="NodeKind.IRI"/> — the value must be a <see cref="NamedNode"/>.</description></item>
///   <item><description><see cref="NodeKind.BlankNode"/> — the value must be a <see cref="BlankNode"/>.</description></item>
///   <item><description><see cref="NodeKind.Literal"/> — the value must be a <see cref="Literal"/>.</description></item>
///   <item><description><see cref="NodeKind.BlankNodeOrIRI"/> / <see cref="NodeKind.BlankNodeOrLiteral"/> / <see cref="NodeKind.IRIOrLiteral"/> — unions of the above.</description></item>
/// </list>
/// <para>
/// Violations are per-value-node: each value that fails to match the
/// kind emits a separate result with
/// <see cref="ValidationResult.ValueNode"/> set.
/// </para>
/// </remarks>
public static class NodeKindEvaluator
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

        NodeKindConstraint nodeKind = (NodeKindConstraint)constraint;
        ImmutableArray<ValidationResult>.Builder builder = ImmutableArray.CreateBuilder<ValidationResult>();

        foreach(TermId value in valueNodes)
        {
            RdfTerm term = context.Dictionary.Resolve(value);
            if(Matches(term, nodeKind.Kind))
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

    //Maps each term kind to the set of NodeKind values that accept it.
    //Expressed as a pattern on (kind, term) so the compiler emits a
    //jump table; no per-call allocation.
    private static bool Matches(RdfTerm term, NodeKind kind)
        => (kind, term) switch
        {
            (NodeKind.IRI, NamedNode) => true,
            (NodeKind.BlankNode, BlankNode) => true,
            (NodeKind.Literal, Literal) => true,
            (NodeKind.BlankNodeOrIRI, NamedNode or BlankNode) => true,
            (NodeKind.BlankNodeOrLiteral, BlankNode or Literal) => true,
            (NodeKind.IRIOrLiteral, NamedNode or Literal) => true,
            _ => false,
        };
}
