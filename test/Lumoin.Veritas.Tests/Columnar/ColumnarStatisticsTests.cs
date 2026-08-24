using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The columnar statistics contract: cardinalities and the fan-out distribution
/// read off a built index match what a known corpus dictates, and the trace
/// event faithfully carries the summary. A corpus with a fixed objects-per-
/// subject fan-out is the oracle.
/// </summary>
[TestClass]
internal sealed class ColumnarStatisticsTests
{
    /// <summary>The single predicate every edge carries.</summary>
    private const uint Predicate = 1_000;

    /// <summary>Builds a corpus where every subject has exactly one predicate and <paramref name="fanOut"/> objects.</summary>
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

    [TestMethod]
    public void CardinalitiesAndFanOutMatchAKnownCorpus()
    {
        const int subjects = 1_000;
        const int fanOut = 8;
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(BuildFanOut(subjects, fanOut), ColumnarOrderSetMode.AllSixOrders);

        ColumnarStatistics stats = ColumnarStatistics.From(index.OrderAt(0), 0);

        Assert.AreEqual((long)subjects * fanOut, stats.TripleCount);
        Assert.AreEqual(subjects, stats.Level0Count);
        Assert.AreEqual(subjects, stats.Level1Count);
        Assert.AreEqual(subjects * fanOut, stats.Level2Count);

        //Each subject carries exactly one predicate: level-0 fan-out is 1.
        Assert.AreEqual(1, stats.Level0FanOut.Min);
        Assert.AreEqual(1, stats.Level0FanOut.Max);
        Assert.AreEqual(1.0, stats.Level0FanOut.Mean, 1e-9);

        //Each (subject, predicate) group has exactly fanOut objects.
        Assert.AreEqual(fanOut, stats.Level1FanOut.Min);
        Assert.AreEqual(fanOut, stats.Level1FanOut.Max);
        Assert.AreEqual((double)fanOut, stats.Level1FanOut.Mean, 1e-9);
    }

    [TestMethod]
    public void TraceEventCarriesTheSummary()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(BuildFanOut(500, 4), ColumnarOrderSetMode.ThreeRotations);
        ColumnarStatistics stats = ColumnarStatistics.From(index.OrderAt(0), 0);

        ColumnarStatisticsTraceEvent evt = ColumnarStatisticsTraceEvent.ForOrder(7, 123, Guid.Empty, stats);

        Assert.AreEqual(7, evt.SequenceNumber);
        Assert.AreEqual(123, evt.TimestampTicks);
        Assert.AreEqual(stats.TripleCount, evt.TripleCount);
        Assert.AreEqual(stats.Level0Count, evt.Level0Count);
        Assert.AreEqual(stats.Level1Count, evt.Level1Count);
        Assert.AreEqual(stats.Level1FanOut.Max, evt.Level1FanOutMax);
        Assert.AreEqual(stats.BitsPerTriple, evt.BitsPerTriple, 1e-9);
    }

    [TestMethod]
    public void EmitStatisticsEmitsOneEventPerMaterialisedOrder()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(BuildFanOut(200, 4), ColumnarOrderSetMode.ThreeRotations);
        Guid correlation = new("11111111-1111-1111-1111-111111111111");

        List<ColumnarStatisticsTraceEvent> collected = [];
        index.EmitStatistics((in ColumnarStatisticsTraceEvent traceEvent) => collected.Add(traceEvent), correlation, TimeProvider.System);

        //ThreeRotations materialises three orders.
        Assert.HasCount(3, collected);
        for(int i = 0; i < collected.Count; i++)
        {
            Assert.AreEqual(i, collected[i].SequenceNumber);
            Assert.AreEqual(correlation, collected[i].CorrelationId);
            Assert.AreEqual((long)(200 * 4), collected[i].TripleCount);
        }
    }
}
