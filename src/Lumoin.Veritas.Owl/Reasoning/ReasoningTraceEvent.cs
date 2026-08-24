using System;
using System.Diagnostics;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>The reasoning strategy a rendezvous selected for a request.</summary>
public enum ReasoningStrategy
{
    /// <summary>The RDFS streaming materialization — the schema-driven rule subset.</summary>
    Rdfs = 0,

    /// <summary>The OWL 2 RL/RDF rules closure.</summary>
    Rl = 1,

    /// <summary>SROIQ(D)-hard modules handed to the external description-logic delegate.</summary>
    DescriptionLogicDelegate = 2,

    /// <summary>The EL classifier — TBox preprocessing feeding planner statistics and query-time expansion, never materialized triples.</summary>
    ElClassification = 3,
}

/// <summary>Why the rendezvous selected the strategy it did — the expressiveness rungs.</summary>
public enum ReasoningSelectionReason
{
    /// <summary>The TBox stays within the RDFS vocabulary; the streaming pass answers.</summary>
    RdfsSufficient = 0,

    /// <summary>The TBox is within the RL profile; the RL closure answers completely.</summary>
    RlSufficient = 1,

    /// <summary>The TBox exceeds RL; the RL closure still ran (sound), and the exceeding axioms were handed to the description-logic delegate.</summary>
    BeyondRlDelegated = 2,

    /// <summary>The TBox exceeds RL and no description-logic delegate is wired; the exceeding axioms are reported, never silently dropped.</summary>
    BeyondRlReported = 3,

    /// <summary>The EL classification was computed for this store generation — the request paid the build, which consumers amortise.</summary>
    ElClassificationBuilt = 4,

    /// <summary>The EL classification for this store generation already existed and was reused at zero cost.</summary>
    ElClassificationReused = 5,
}

/// <summary>
/// One strategy-selection announcement on the reasoning trace stream,
/// mirroring the join layer's <c>EngineSelected</c> event: the strategy, the
/// reason, the detected profile floor's violation count, and the
/// materialization cost. Consumers joining these with
/// <see cref="InferenceTraceEvent"/> and the query events by
/// <see cref="ITraceEvent.CorrelationId"/> obtain (decision inputs →
/// observed cost) pairs — the feedback an adaptive reasoning policy learns
/// from. The bus observes; it never dictates.
/// </summary>
/// <param name="SequenceNumber">Monotonic sequence number within the rendezvous's trace stream.</param>
/// <param name="TimestampTicks">UTC timestamp in <see cref="DateTime.Ticks"/> units.</param>
/// <param name="CorrelationId">The reasoning request's correlation id.</param>
/// <param name="Strategy">The selected strategy.</param>
/// <param name="Reason">The expressiveness rung that selected it.</param>
/// <param name="BeyondRlAxiomCount">The number of axioms the RL grammar excluded; zero when the TBox is within profile.</param>
/// <param name="DerivedCount">The number of triples the materialization derived.</param>
/// <param name="ElapsedMilliseconds">The materialization cost in fractional milliseconds, measured on a monotonic clock.</param>
[DebuggerDisplay("ReasoningTraceEvent {Strategy} {Reason} Derived={DerivedCount}")]
public readonly record struct ReasoningTraceEvent(
    long SequenceNumber,
    long TimestampTicks,
    Guid CorrelationId,
    ReasoningStrategy Strategy,
    ReasoningSelectionReason Reason,
    int BeyondRlAxiomCount,
    int DerivedCount,
    double ElapsedMilliseconds): ITraceEvent;
