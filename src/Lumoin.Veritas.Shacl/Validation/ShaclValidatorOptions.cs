using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Shacl.Diagnostics;

namespace Lumoin.Veritas.Shacl.Validation;

/// <summary>
/// Tuning options for <see cref="ShaclValidator.ValidateAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Options are immutable; use <c>with</c>-expressions to derive a
/// modified instance from <see cref="Default"/>.
/// </para>
/// <para>
/// Additional options may be added as the validator grows — for example
/// an <c>EnableRdfsInference</c> flag once
/// <see cref="Shacl.Constraints.ClassConstraint"/> has an evaluator. This
/// record exists now so the option-passing shape of
/// <see cref="ShaclValidator.ValidateAsync"/> does not change when later
/// options are introduced.
/// </para>
/// </remarks>
public sealed record ShaclValidatorOptions
{
    /// <summary>
    /// Maximum number of results to collect before the validator stops
    /// eagerly. <c>null</c> means no limit. When the cap is reached the
    /// run halts and the partial report is returned.
    /// </summary>
    public int? MaxResults { get; init; }

    /// <summary>
    /// When <c>true</c>, the validator halts as soon as the first
    /// <see cref="Severity.Violation"/> result is produced. Useful for
    /// CI gates that only care whether the data conforms at all.
    /// </summary>
    public bool FailFast { get; init; }

    /// <summary>
    /// Optional structured-trace sink. When non-<c>null</c>, the
    /// orchestrator emits <see cref="ShaclTraceEvent"/>s at focus-node
    /// selection, constraint start / completion / not-implemented, and
    /// per-result production points. When <c>null</c>, tracing is
    /// zero-cost — the orchestrator skips event construction entirely.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler is invoked synchronously on the validator's thread.
    /// Handlers must not block; if persistence or cross-thread dispatch
    /// is needed, forward into a <c>System.Threading.Channels.Channel</c>
    /// or equivalent via <see cref="TraceHandlers"/> adapters.
    /// </para>
    /// <para>
    /// All events in a single validation run share the same
    /// <see cref="Core.Diagnostics.ITraceEvent.CorrelationId"/>. Sequence
    /// numbers are monotonically increasing within the run.
    /// </para>
    /// </remarks>
    public TraceHandler<ShaclTraceEvent>? TraceHandler { get; init; }

    /// <summary>The default options — no limit, no fail-fast, no tracing.</summary>
    public static ShaclValidatorOptions Default { get; } = new();
}
