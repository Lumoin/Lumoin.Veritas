namespace Lumoin.Veritas.Shacl.Diagnostics;

/// <summary>
/// Discriminator for the union cases of <see cref="ShaclTraceEvent"/>.
/// </summary>
/// <remarks>
/// <para>
/// Struct types cannot participate in inheritance-based closed unions,
/// so subsystem trace events use a single <c>readonly record struct</c>
/// carrying a discriminator plus all union-specific payload fields.
/// Consumers switch on this enum to interpret the event.
/// </para>
/// <para>
/// The <see cref="TraceHandler{TEvent}"/> contract accepts exactly one
/// struct type, so a subsystem's closed union must be represented as one
/// struct. Consumers wishing to observe only a subset of kinds filter
/// inside their handler.
/// </para>
/// </remarks>
public enum ShaclTraceEventKind
{
    /// <summary>
    /// A focus node has been yielded by a target's expansion. Useful for
    /// correlating validation results back to their originating focus
    /// nodes and for UI highlighting.
    /// </summary>
    FocusNodeSelected = 0,

    /// <summary>
    /// Evaluation of a single constraint on a specific focus node is
    /// about to begin. Useful for performance diagnostics.
    /// </summary>
    ConstraintEvaluationStarted = 1,

    /// <summary>
    /// Evaluation of a constraint has completed with a status (passed,
    /// failed, short-circuited). Paired with a prior Started event via
    /// <c>CorrelationId</c>.
    /// </summary>
    ConstraintEvaluationCompleted = 2,

    /// <summary>
    /// A <see cref="ValidationResult"/> has been produced and is about
    /// to be yielded to the caller. Lets UIs update before waiting for
    /// the next constraint's evaluation.
    /// </summary>
    ValidationResultProduced = 3,

    /// <summary>
    /// A constraint was encountered whose evaluator is not yet
    /// implemented. Emitted alongside the corresponding
    /// <c>NotImplementedException</c> thrown by the evaluator. Useful
    /// for tracking coverage gaps.
    /// </summary>
    ConstraintNotImplemented = 4
}
