using System;
using System.Threading.Channels;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Shared soak helper that drains a built index's per-order
/// <see cref="ColumnarStatistics"/> off the trace channel and prints a per-order
/// summary. Any columnar soak calls <see cref="Report"/> to collect the same
/// statistics in one line.
/// </summary>
internal static class SoakStatistics
{
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
