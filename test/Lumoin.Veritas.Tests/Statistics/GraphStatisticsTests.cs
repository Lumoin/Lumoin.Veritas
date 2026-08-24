using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Statistics;

namespace Lumoin.Veritas.Tests.Statistics;

/// <summary>
/// The store-agnostic graph statistics contract: counts and the subject
/// out-degree summary read from the triples alone match what a known corpus
/// dictates. A corpus with a fixed objects-per-subject fan-out is the oracle.
/// </summary>
[TestClass]
internal sealed class GraphStatisticsTests
{
    /// <summary>The single predicate every edge carries.</summary>
    private const uint Predicate = 1_000;

    /// <summary>Builds a corpus where every subject has one predicate and <paramref name="fanOut"/> distinct objects in a disjoint id range.</summary>
    /// <param name="subjects">The subject count.</param>
    /// <param name="fanOut">The objects per subject.</param>
    /// <returns>The triple corpus.</returns>
    private static List<EncodedTriple> BuildFanOut(int subjects, int fanOut)
    {
        List<EncodedTriple> triples = new(subjects * fanOut);
        for(int s = 0; s < subjects; s++)
        {
            long objectBase = (5_000_000L) + ((long)s * fanOut * 4);
            for(int k = 0; k < fanOut; k++)
            {
                triples.Add(EncodedTriple.FromEncoded((uint)s, Predicate, (uint)(objectBase + (k * 4))));
            }
        }

        return triples;
    }

    [TestMethod]
    public void CountsAndDegreeMatchAKnownCorpus()
    {
        const int subjects = 1_000;
        const int fanOut = 8;

        GraphStatistics statistics = GraphStatistics.From(BuildFanOut(subjects, fanOut));

        Assert.AreEqual((long)subjects * fanOut, statistics.TripleCount);
        Assert.AreEqual(subjects, statistics.DistinctSubjects);
        Assert.AreEqual(1, statistics.DistinctPredicates);
        Assert.AreEqual(subjects * fanOut, statistics.DistinctObjects);
        Assert.AreEqual(fanOut, statistics.MinSubjectOutDegree);
        Assert.AreEqual(fanOut, statistics.MaxSubjectOutDegree);
        Assert.AreEqual((double)fanOut, statistics.MeanSubjectOutDegree, 1e-9);
        Assert.AreEqual(subjects * fanOut, statistics.MaxPredicateFrequency);
    }

    [TestMethod]
    public void EmptyCorpusIsZeroed()
    {
        GraphStatistics statistics = GraphStatistics.From([]);

        Assert.AreEqual(0L, statistics.TripleCount);
        Assert.AreEqual(0, statistics.DistinctSubjects);
        Assert.AreEqual(0, statistics.MinSubjectOutDegree);
        Assert.AreEqual(0, statistics.MaxSubjectOutDegree);
        Assert.AreEqual(0.0, statistics.MeanSubjectOutDegree, 1e-9);
    }
}
