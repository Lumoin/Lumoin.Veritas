using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Evaluator for <c>sh:ReifierShapeConstraintComponent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Per SHACL 1.2 Core §6.11. The constraint applies when value nodes
/// are RDF 1.2 triple terms; reifier nodes of those triple terms
/// must conform to the shape referenced by
/// <see cref="ReifierShapeConstraint.ReifierShapeId"/>.
/// </para>
/// <para>
/// <b>Stub implementation.</b> Real evaluation requires RDF 1.2
/// triple-term support in the underlying graph store, which is not
/// yet present in this validator's data path. The evaluator emits a
/// single <see cref="Severity.Info"/> result per invocation flagging
/// the deferred status, mirroring the shape established by
/// <see cref="NotImplementedEvaluator"/>. The validator does not
/// throw, the run completes, and the report's
/// <see cref="ValidationReport.Conforms"/> flag is unaffected
/// (Info-severity results do not cause non-conformance).
/// </para>
/// <para>
/// <b>Why a dedicated evaluator and not the generic stub.</b>
/// <see cref="NotImplementedEvaluator"/>'s message is a generic
/// "not yet evaluated by this validator." This evaluator exists to
/// produce a more precise message identifying the specific blocker
/// (RDF 1.2 reification semantics), which is more actionable for
/// consumers reading the validation report.
/// </para>
/// </remarks>
public static class ReifierShapeEvaluator
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
        _ = valueNodes;
        _ = context;
        cancellationToken.ThrowIfCancellationRequested();

        ImmutableDictionary<string, string> messages = ImmutableDictionary<string, string>.Empty.Add(
            string.Empty,
            "sh:reifierShape evaluation is deferred until RDF 1.2 triple-term support reaches the data graph layer.");

        ValidationResult result = new()
        {
            FocusNode = focusNode,
            ResultPath = path,
            Severity = Severity.Info,
            SourceShape = shape.Id,
            SourceConstraintComponent = constraint.ConstraintComponentIri,
            Messages = messages,
        };

        return ValueTask.FromResult(ImmutableArray.Create(result));
    }
}
