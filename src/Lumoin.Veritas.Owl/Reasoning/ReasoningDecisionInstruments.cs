using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// Records the <c>veritas.reasoning.*</c> metrics from the decision trace
/// stream: a decoupled consumer that turns each
/// <see cref="ReasoningDecisionTraceEvent"/> into measurements, so the
/// rendezvous stays free of any meter — it emits the event, and this adapter,
/// wired as the decision-trace handler, records the decision counter and the
/// solve-count and duration histograms, each tagged by outcome. The trace
/// event is the single source; the meter is one of its consumers, the same
/// shape as the compute-lane instruments observing the lane.
/// </summary>
/// <remarks>
/// The instruments live with the meter; disposing the meter retires them.
/// Wire <see cref="Handler"/> as the rendezvous's decision-trace handler — on
/// its own, or composed with another handler through
/// <c>TraceHandlers.Composite</c> — to feed the meter.
/// </remarks>
public sealed class ReasoningDecisionInstruments
{
    /// <summary>The total-decisions counter, tagged by outcome.</summary>
    private readonly Counter<long> decisions;

    /// <summary>The per-decision world-solve-count histogram, tagged by outcome.</summary>
    private readonly Histogram<int> solveCount;

    /// <summary>The per-decision duration histogram in fractional milliseconds, tagged by outcome.</summary>
    private readonly Histogram<double> duration;

    /// <summary>Creates the reasoning-decision instruments on a meter.</summary>
    /// <param name="meter">The meter the instruments are created on.</param>
    /// <exception cref="ArgumentNullException"><paramref name="meter"/> is <c>null</c>.</exception>
    public ReasoningDecisionInstruments(Meter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);

        decisions = meter.CreateCounter<long>(VeritasMetrics.ReasoningDecisionsTotal, "decisions", "Total description-logic decisions by outcome.");
        solveCount = meter.CreateHistogram<int>(VeritasMetrics.ReasoningDecisionSolveCount, "solves", "World solves per description-logic decision.");
        duration = meter.CreateHistogram<double>(VeritasMetrics.ReasoningDecisionDuration, "ms", "Description-logic decision duration in milliseconds.");
    }

    /// <summary>The trace handler that records each decision event to the instruments; wire it as the rendezvous's decision-trace handler.</summary>
    public TraceHandler<ReasoningDecisionTraceEvent> Handler => Record;

    /// <summary>Records one decision event as the counter increment and the two histogram measurements, tagged by outcome.</summary>
    /// <param name="decisionEvent">The decision event to record.</param>
    private void Record(in ReasoningDecisionTraceEvent decisionEvent)
    {
        KeyValuePair<string, object?> outcome = new("outcome", OutcomeTag(decisionEvent.Outcome));
        decisions.Add(1, outcome);
        solveCount.Record(decisionEvent.SolveCount, outcome);
        duration.Record(decisionEvent.ElapsedMilliseconds, outcome);
    }

    /// <summary>The metric tag for a decision outcome — lowercase and snake-cased per the OpenTelemetry conventions.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The tag value.</returns>
    private static string OutcomeTag(ReasoningDecisionOutcome outcome)
    {
        return outcome switch
        {
            ReasoningDecisionOutcome.Decided => "decided",
            ReasoningDecisionOutcome.AbstainedBudget => "abstained_budget",
            ReasoningDecisionOutcome.DecidedFragmentRelative => "decided_fragment_relative",
            _ => "unknown",
        };
    }
}
