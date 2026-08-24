using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Reasoning;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The <c>veritas.reasoning.*</c> instruments record one decision counter and
/// the solve-count and duration histograms from the decision trace stream,
/// each tagged by outcome. Pulled deterministically through a
/// <see cref="MeterListener"/> after feeding the recorder two known events.
/// </summary>
[TestClass]
internal sealed class ReasoningDecisionInstrumentsTests
{
    [TestMethod]
    public void RecordsDecisionsAndHistogramsTaggedByOutcome()
    {
        using Meter meter = new(VeritasMetrics.MeterName, "1.0.0");
        ReasoningDecisionInstruments instruments = new(meter);
        TraceHandler<ReasoningDecisionTraceEvent> handler = instruments.Handler;

        long totalDecisions = 0;
        Dictionary<string, long> decisionsByOutcome = [];
        List<int> solveCounts = [];
        List<double> durations = [];

        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, activeListener) =>
        {
            if(instrument.Meter == meter)
            {
                activeListener.EnableMeasurementEvents(instrument);
            }
        };

        //The decision counter is a long instrument; the solve-count histogram is int; the duration histogram is fractional double.
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if(instrument.Name != VeritasMetrics.ReasoningDecisionsTotal)
            {
                return;
            }

            totalDecisions += measurement;
            foreach(KeyValuePair<string, object?> tag in tags)
            {
                if(tag.Key == "outcome" && tag.Value is string outcome)
                {
                    decisionsByOutcome[outcome] = decisionsByOutcome.GetValueOrDefault(outcome) + measurement;
                }
            }
        });
        listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
        {
            if(instrument.Name == VeritasMetrics.ReasoningDecisionSolveCount)
            {
                solveCounts.Add(measurement);
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if(instrument.Name == VeritasMetrics.ReasoningDecisionDuration)
            {
                durations.Add(measurement);
            }
        });
        listener.Start();

        ReasoningDecisionStatistics decidedStatistics = new(ModuleAxiomCount: 3, SolveCount: 7, SolverTotals: default);
        ReasoningDecisionTraceEvent decided = ReasoningDecisionTraceEvent.From(
            sequenceNumber: 1, timestampTicks: 0, correlationId: default, ReasoningDecisionOutcome.Decided, decidedStatistics, elapsedMilliseconds: 12);
        handler(in decided);

        ReasoningDecisionStatistics abstainedStatistics = new(ModuleAxiomCount: 9, SolveCount: 1, SolverTotals: default);
        ReasoningDecisionTraceEvent abstained = ReasoningDecisionTraceEvent.From(
            sequenceNumber: 2, timestampTicks: 0, correlationId: default, ReasoningDecisionOutcome.AbstainedBudget, abstainedStatistics, elapsedMilliseconds: 30);
        handler(in abstained);

        Assert.AreEqual(2L, totalDecisions);
        Assert.AreEqual(1L, decisionsByOutcome["decided"]);
        Assert.AreEqual(1L, decisionsByOutcome["abstained_budget"]);
        Assert.Contains(7, solveCounts);
        Assert.Contains(1, solveCounts);
        Assert.Contains(12.0, durations);
        Assert.Contains(30.0, durations);
    }

    /// <summary>A fragment-relative decision records its counter under the <c>decided_fragment_relative</c> tag.</summary>
    [TestMethod]
    public void FragmentRelativeDecisionRecordsUnderItsTag()
    {
        using Meter meter = new(VeritasMetrics.MeterName, "1.0.0");
        ReasoningDecisionInstruments instruments = new(meter);
        TraceHandler<ReasoningDecisionTraceEvent> handler = instruments.Handler;

        Dictionary<string, long> decisionsByOutcome = [];

        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, activeListener) =>
        {
            if(instrument.Meter == meter)
            {
                activeListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if(instrument.Name != VeritasMetrics.ReasoningDecisionsTotal)
            {
                return;
            }

            foreach(KeyValuePair<string, object?> tag in tags)
            {
                if(tag.Key == "outcome" && tag.Value is string outcome)
                {
                    decisionsByOutcome[outcome] = decisionsByOutcome.GetValueOrDefault(outcome) + measurement;
                }
            }
        });
        listener.Start();

        ReasoningDecisionStatistics statistics = new(ModuleAxiomCount: 5, SolveCount: 3, SolverTotals: default);
        ReasoningDecisionTraceEvent fragmentRelative = ReasoningDecisionTraceEvent.From(
            sequenceNumber: 1, timestampTicks: 0, correlationId: default, ReasoningDecisionOutcome.DecidedFragmentRelative, statistics, elapsedMilliseconds: 8);
        handler(in fragmentRelative);

        Assert.AreEqual(1L, decisionsByOutcome["decided_fragment_relative"]);
    }
}
