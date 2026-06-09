using System;
using System.Collections.Generic;
using System.Threading.Channels;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak that builds a columnar index, computes each materialised order's
/// <see cref="ColumnarStatistics"/>, emits them as
/// <see cref="ColumnarStatisticsTraceEvent"/> through a
/// <see cref="TraceHandler{TEvent}"/> channel, then drains the channel and
/// prints the per-order summary — exercising statistics computation, the trace
/// channel, and a consumer end to end over a few corpus shapes.
/// </summary>
/// <remarks>Line-oriented output, the same shape as the other <c>--profile-*</c> soaks.</remarks>
internal static class StatisticsSoak
{
    /// <summary>The single predicate every edge carries.</summary>
    private const uint Predicate = 1_000;

    /// <summary>Runs the statistics soak over a spread of corpus shapes.</summary>
    public static void RunStatisticsSoak()
    {
        RunConfiguration("triangle", BuildTriangle(1_000_000));
        RunConfiguration("fan-out 32", BuildFanOut(100_000, 32));
        RunConfiguration("fan-out 2", BuildFanOut(1_500_000, 2));
    }

    /// <summary>Builds the index, emits each order's statistics through a trace channel, then drains and prints them.</summary>
    /// <param name="name">A label for the corpus shape.</param>
    /// <param name="corpus">The triple corpus.</param>
    private static void RunConfiguration(string name, List<EncodedTriple> corpus)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(corpus, ColumnarOrderSetMode.ThreeRotations);

        Channel<ColumnarStatisticsTraceEvent> channel = Channel.CreateUnbounded<ColumnarStatisticsTraceEvent>();
        TraceHandler<ColumnarStatisticsTraceEvent> handler = TraceHandlers.ToChannel(channel.Writer);

        long sequence = 0;
        long ticks = TimeProvider.System.GetUtcNow().UtcTicks;
        Guid correlation = Guid.Empty;
        for(int permutation = 0; permutation < 6; permutation++)
        {
            if(index.IsPermutationAvailable(permutation))
            {
                ColumnarStatistics statistics = ColumnarStatistics.From(index.OrderAt(permutation), permutation);
                ColumnarStatisticsTraceEvent evt = ColumnarStatisticsTraceEvent.ForOrder(sequence++, ticks, correlation, statistics);
                handler(in evt);
            }
        }

        channel.Writer.Complete();

        Console.WriteLine(
            $"[stats] {name}: {corpus.Count:N0} triples — per-order summary drained from the trace channel\n"
            + $"[stats]   {"perm",4} {"triples",12} {"L0 card",12} {"L1 card",12} {"fan0 mean/max",16} {"fan1 mean/max",16} {"bits/triple",12}");
        while(channel.Reader.TryRead(out ColumnarStatisticsTraceEvent evt))
        {
            string fanOut0 = $"{evt.Level0FanOutMean:F1}/{evt.Level0FanOutMax}";
            string fanOut1 = $"{evt.Level1FanOutMean:F1}/{evt.Level1FanOutMax}";
            Console.WriteLine($"[stats]   {evt.Permutation,4} {evt.TripleCount,12:N0} {evt.Level0Count,12:N0} {evt.Level1Count,12:N0} {fanOut0,16} {fanOut1,16} {evt.BitsPerTriple,12:F2}");
        }
    }

    /// <summary>Builds disjoint directed triangles over sequential node ids — one object per (subject, predicate) group.</summary>
    /// <param name="groups">The group count.</param>
    /// <returns>The triple corpus, three edges per group.</returns>
    private static List<EncodedTriple> BuildTriangle(int groups)
    {
        List<EncodedTriple> corpus = new(groups * 3);
        for(int i = 0; i < groups; i++)
        {
            uint a = (uint)(i * 3);
            uint b = a + 1;
            uint c = a + 2;
            corpus.Add(EncodedTriple.FromEncoded(a, Predicate, b));
            corpus.Add(EncodedTriple.FromEncoded(b, Predicate, c));
            corpus.Add(EncodedTriple.FromEncoded(c, Predicate, a));
        }

        return corpus;
    }

    /// <summary>Builds a corpus where every subject has one predicate and <paramref name="fanOut"/> objects.</summary>
    /// <param name="subjects">The subject count.</param>
    /// <param name="fanOut">The objects per subject.</param>
    /// <returns>The triple corpus.</returns>
    private static List<EncodedTriple> BuildFanOut(int subjects, int fanOut)
    {
        List<EncodedTriple> corpus = new(subjects * fanOut);
        for(int s = 0; s < subjects; s++)
        {
            long objectBase = (long)s * fanOut * 4;
            for(int k = 0; k < fanOut; k++)
            {
                corpus.Add(EncodedTriple.FromEncoded((uint)s, Predicate, (uint)(objectBase + (k * 4))));
            }
        }

        return corpus;
    }
}
