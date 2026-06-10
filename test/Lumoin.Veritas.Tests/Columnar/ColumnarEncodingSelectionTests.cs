using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The build-time value-column encoding selection: the default Elias-Fano
/// policy takes whole-column Elias-Fano on the globally-monotone top level and
/// partitioned Elias-Fano on a within-group column only when it is actually
/// smaller (large fan-out), keeping frame of reference otherwise — the
/// footprint-driven selector never regresses a column. The frame-of-reference
/// policy remains available and keeps every value column on that layout.
/// </summary>
[TestClass]
internal sealed class ColumnarEncodingSelectionTests
{
    /// <summary>The single predicate every edge carries.</summary>
    private const uint Predicate = 1_000;

    /// <summary>Builds a corpus where each subject has one predicate and <paramref name="fanOut"/> objects in its own contiguous id range.</summary>
    /// <param name="subjects">The subject count.</param>
    /// <param name="fanOut">The objects per subject (the level-2 group size).</param>
    /// <returns>The triple corpus.</returns>
    private static List<EncodedTriple> FanOut(int subjects, int fanOut)
    {
        List<EncodedTriple> corpus = new(subjects * fanOut);
        for(int s = 0; s < subjects; s++)
        {
            long objectBase = 5_000_000L + ((long)s * fanOut * 4);
            for(int k = 0; k < fanOut; k++)
            {
                corpus.Add(EncodedTriple.FromEncoded((uint)s, Predicate, (uint)(objectBase + (k * 4))));
            }
        }

        return corpus;
    }

    [TestMethod]
    public void DefaultPolicyIsTheSuccinctSelectionAndFrameOfReferenceStaysAvailable()
    {
        //The default build takes the Elias-Fano policy: the globally-monotone
        //top level goes succinct, and the constant predicate level stays frame
        //of reference (its packed width is already near zero — the selector
        //keeps the smaller).
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(FanOut(2_000, 16), ColumnarOrderSetMode.ThreeRotations);
        ColumnarOrder order = index.OrderAt(0);

        Assert.AreEqual(BlockPackedColumnMode.EliasFano, order.ValuesColumnAt(0).Mode);
        Assert.AreEqual(BlockPackedColumnMode.FrameOfReference, order.ValuesColumnAt(1).Mode);

        //The explicit frame-of-reference policy keeps every value column on
        //that layout — the differential baseline.
        ColumnarTripleIndex baseline = ColumnarTripleIndex.Build(FanOut(2_000, 16), ColumnarOrderSetMode.ThreeRotations, ColumnarValueColumnEncoding.FrameOfReference);
        ColumnarOrder baselineOrder = baseline.OrderAt(0);

        Assert.AreEqual(BlockPackedColumnMode.FrameOfReference, baselineOrder.ValuesColumnAt(0).Mode);
        Assert.AreEqual(BlockPackedColumnMode.FrameOfReference, baselineOrder.ValuesColumnAt(1).Mode);
        Assert.AreEqual(BlockPackedColumnMode.FrameOfReference, baselineOrder.ValuesColumnAt(2).Mode);
    }

    [TestMethod]
    public void LargeFanOutSelectsEliasFanoForTopLevelAndPartitionedForLevel2()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(FanOut(2_000, 64), ColumnarOrderSetMode.ThreeRotations, ColumnarValueColumnEncoding.EliasFanoWhenMonotone);
        ColumnarOrder order = index.OrderAt(0);

        //Level 0 (the globally-monotone subjects) takes whole-column Elias-Fano.
        Assert.AreEqual(BlockPackedColumnMode.EliasFano, order.ValuesColumnAt(0).Mode);

        //Level 2 (64 objects per group) is where partitioned Elias-Fano wins.
        Assert.AreEqual(BlockPackedColumnMode.PartitionedEliasFano, order.ValuesColumnAt(2).Mode);
    }

    [TestMethod]
    public void SmallFanOutKeepsFrameOfReferenceForLevel2()
    {
        //One object per subject: level-2 groups are size 1, so partitioned
        //Elias-Fano's per-segment overhead loses and frame of reference is kept.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(FanOut(20_000, 1), ColumnarOrderSetMode.ThreeRotations, ColumnarValueColumnEncoding.EliasFanoWhenMonotone);

        Assert.AreEqual(BlockPackedColumnMode.FrameOfReference, index.OrderAt(0).ValuesColumnAt(2).Mode);
    }
}
