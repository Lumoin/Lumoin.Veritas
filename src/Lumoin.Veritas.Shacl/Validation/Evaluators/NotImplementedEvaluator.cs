using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Constraints;

namespace Lumoin.Veritas.Shacl.Validation.Evaluators;

/// <summary>
/// Default evaluator returned by
/// <see cref="ConstraintEvaluatorRegistry.GetOrDefault"/> when no real
/// evaluator is registered for a constraint component. Emits a single
/// <see cref="Severity.Info"/> result flagging the constraint as
/// unimplemented, without halting validation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why Info rather than Violation.</b> An unimplemented evaluator
/// tells us nothing about the data — the constraint could be satisfied,
/// could be violated, the validator simply did not check. Emitting a
/// <see cref="Severity.Violation"/> would mislead consumers into
/// thinking real data failures were present; emitting no result at all
/// would silently skip the constraint. <see cref="Severity.Info"/>
/// makes the omission visible. Note that under SHACL §3.6 any result —
/// including this Info — makes the report non-conforming, so an
/// unimplemented constraint correctly forces <c>sh:conforms = false</c>
/// (the validator did not establish conformance) rather than silently
/// passing. With the Core component set now fully implemented this
/// evaluator is reached only for genuinely unregistered components.
/// </para>
/// <para>
/// <b>Not a conformance failure.</b> SHACL 1.2 §4.8 says a conforming
/// validator must produce a failure for unsupported constructs. This
/// evaluator does not meet that strict requirement; it is a pragmatic
/// compromise that lets partial validator implementations produce
/// useful reports. Once all 38 constraint components have real
/// evaluators, this type becomes a dead-code sentinel that
/// <see cref="ConstraintEvaluatorRegistry"/> never actually returns.
/// </para>
/// </remarks>
public static class NotImplementedEvaluator
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
            $"Constraint component '{constraint.ConstraintComponentIri}' is not yet evaluated by this validator.");

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
