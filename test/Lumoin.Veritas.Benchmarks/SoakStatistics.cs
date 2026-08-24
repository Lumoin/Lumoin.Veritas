using System;
using System.Collections.Generic;
using System.Threading.Channels;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Statistics;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Shared soak helper for the trace-channel statistics. <see cref="ReportGraph"/>
/// reports the store-agnostic <see cref="GraphStatistics"/> any triple corpus
/// has; <see cref="Report"/> reports a built index's per-order
/// <see cref="ColumnarStatistics"/>. Both emit through the trace channel and
/// drain it, so a soak collects the data in one line.
/// </summary>
internal static class SoakStatistics
{
    /// <summary>Computes a triple corpus's store-agnostic statistics, emits them through the trace channel, then drains and prints them.</summary>
    /// <param name="triples">The triple corpus to summarise.</param>
    /// <param name="label">A label for the configuration.</param>
    public static void ReportGraph(IReadOnlyCollection<EncodedTriple> triples, string label)
    {
        GraphStatistics statistics = GraphStatistics.From(triples);
        Channel<GraphStatisticsTraceEvent> channel = Channel.CreateUnbounded<GraphStatisticsTraceEvent>();
        GraphStatisticsTraceEvent traceEvent = GraphStatisticsTraceEvent.From(0, TimeProvider.System.GetUtcNow().UtcTicks, Guid.Empty, statistics);
        TraceHandlers.ToChannel(channel.Writer)(in traceEvent);
        channel.Writer.Complete();

        while(channel.Reader.TryRead(out GraphStatisticsTraceEvent drained))
        {
            Console.WriteLine($"[graph] {label}: triples={drained.TripleCount:N0} distinctS={drained.DistinctSubjects:N0} distinctP={drained.DistinctPredicates:N0} distinctO={drained.DistinctObjects:N0} | subj out-deg min/mean/max={drained.MinSubjectOutDegree}/{drained.MeanSubjectOutDegree:F1}/{drained.MaxSubjectOutDegree} | maxPredFreq={drained.MaxPredicateFrequency:N0}");
        }
    }

    /// <summary>Emits the index's per-order statistics through a trace channel, then drains and prints them.</summary>
    /// <param name="index">The built index to summarise.</param>
    /// <param name="label">A label for the configuration.</param>
    public static void Report(ColumnarTripleIndex index, string label)
    {
        Channel<ColumnarStatisticsTraceEvent> channel = Channel.CreateUnbounded<ColumnarStatisticsTraceEvent>();
        index.EmitStatistics(TraceHandlers.ToChannel(channel.Writer), Guid.Empty, TimeProvider.System);
        channel.Writer.Complete();

        Console.WriteLine(
            $"[stats] {label}: per-order summary from the trace channel\n"
            + $"[stats]   {"perm",4} {"triples",12} {"L0 card",12} {"L1 card",12} {"fan0 mean/max",16} {"fan1 mean/max",16} {"bits/triple",12}");
        while(channel.Reader.TryRead(out ColumnarStatisticsTraceEvent traceEvent))
        {
            string fanOut0 = $"{traceEvent.Level0FanOutMean:F1}/{traceEvent.Level0FanOutMax}";
            string fanOut1 = $"{traceEvent.Level1FanOutMean:F1}/{traceEvent.Level1FanOutMax}";
            Console.WriteLine($"[stats]   {traceEvent.Permutation,4} {traceEvent.TripleCount,12:N0} {traceEvent.Level0Count,12:N0} {traceEvent.Level1Count,12:N0} {fanOut0,16} {fanOut1,16} {traceEvent.BitsPerTriple,12:F2}");
        }
    }
}
