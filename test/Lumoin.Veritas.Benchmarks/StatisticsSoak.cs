using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak that builds a columnar index over a few corpus shapes and reports each
/// materialised order's <see cref="ColumnarStatistics"/> through
/// <see cref="SoakStatistics.Report"/> — the per-order cardinalities, fan-out
/// distribution, and bits per triple, drained from the trace channel.
/// </summary>
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

    /// <summary>Builds the index and reports its per-order statistics.</summary>
    /// <param name="name">A label for the corpus shape.</param>
    /// <param name="corpus">The triple corpus.</param>
    private static void RunConfiguration(string name, List<EncodedTriple> corpus)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(corpus, ColumnarOrderSetMode.ThreeRotations);
        SoakStatistics.ReportGraph(corpus, name);
        SoakStatistics.Report(index, $"{name} ({corpus.Count:N0} triples)");
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
