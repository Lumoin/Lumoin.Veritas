using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The dominance-structure canon for <see cref="PackedBoxIndex"/>'s embedded containment
/// tree: hand-derived node counts across the median-split recursion, repeat-build identity,
/// the all-ties row, four single-axis rows that each isolate one dominance coordinate
/// (MinX, MaxX, MinY, MaxY), and the cross adversary's determinism and empty-answer rows.
/// The structural and parity gates over the union-bound tree live in
/// <see cref="PackedBoxIndexTests"/>; this suite is the containing route's own tree.
/// </summary>
[TestClass]
internal sealed class PackedBoxIndexDominanceTests
{
    /// <summary>Both packing families, swept wherever a row must hold for each.</summary>
    private static readonly BoxIndexPacking[] Packings = [BoxIndexPacking.SortTileRecursive, BoxIndexPacking.HilbertCurve];

    /// <summary>The node capacities the hand-derivation sweep runs against — the dominance tree is a structure of its own, independent of the packed tree's node capacity, down to the sanctioned floor.</summary>
    private static readonly int[] DominanceCapacities = [2, 4, 16];

    /// <summary>The dominance node count follows the item-count median split by hand, independent of packing and node capacity, including at the capacity floor.</summary>
    /// <param name="itemCount">The item count of the build.</param>
    /// <param name="expectedNodes">The hand-derived dominance node count.</param>
    [TestMethod]
    [DataRow(1, 1, DisplayName = "one item is a single leaf")]
    [DataRow(4, 1, DisplayName = "four items is a single leaf")]
    [DataRow(8, 1, DisplayName = "eight items is a single leaf, the leaf ceiling")]
    [DataRow(9, 3, DisplayName = "nine items split four and five under a root: three nodes")]
    [DataRow(17, 5, DisplayName = "seventeen items split eight and nine, the nine then splits four and five: five nodes")]
    [DataRow(20, 7, DisplayName = "twenty items split ten and ten, each half then splits five and five: seven nodes")]
    [DataRow(64, 15, DisplayName = "sixty-four items halve three times to eight eights: fifteen nodes")]
    public void DominanceNodeCountsFollowTheMedianSplitByHand(int itemCount, int expectedNodes)
    {
        //A range above the eight-item leaf ceiling splits at its item-count median into a
        //floor half and a ceiling half; at or below eight it refines in place as one leaf.
        //Counting splits by hand gives each row: nine splits 4+5 under a root for three
        //nodes; seventeen splits 8+9, the nine then splits 4+5, for five; twenty splits
        //10+10, each half then splits 5+5, for seven; sixty-four halves three times to
        //eight eights, for fifteen. The capacity sweep runs down to the sanctioned floor
        //of two: the dominance tree never reads the packed tree's node capacity, so every
        //row must hold there too, which is also the tiny-N boundary at capacity 2.
        BoundingBox[] items = SpacedUnitBoxes(itemCount);

        foreach(BoxIndexPacking packing in Packings)
        {
            foreach(int capacity in DominanceCapacities)
            {
                using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, capacity));

                Assert.IsTrue(index.TryBuild(items), "The spaced-unit-box fixture must build.");
                Assert.AreEqual(expectedNodes, index.DominanceNodeCount,
                    $"The dominance tree is independent of packing and node capacity; diverged under {packing}/capacity {capacity}.");
            }
        }
    }

    /// <summary>Two independent builds from one item sequence carry the same dominance node count and answer every containing probe identically.</summary>
    [TestMethod]
    public void RepeatDominanceBuildsEmitIdentically()
    {
        //The dominance tree is a pure function of (options, item sequence), same as the
        //union-bound tree: two independent builds from one sequence must carry the same
        //node count and answer every containing probe in the identical order.
        BoundingBox[] items = PackedBoxIndexFixtureFamily.ExtensionFixture();
        BoundingBox[] probes =
        [
            new BoundingBox(49, 49, 51, 51),
            new BoundingBox(25, 45, 28, 55),
            new BoundingBox(73, 73, 76, 76),
            new BoundingBox(50, 50, 50, 50)
        ];

        foreach(BoxIndexPacking packing in Packings)
        {
            var options = new PackedBoxIndexOptions(packing, 4);
            using PackedBoxIndex first = PackedBoxIndex.Create(options);
            using PackedBoxIndex second = PackedBoxIndex.Create(options);

            Assert.IsTrue(first.TryBuild(items), "The first build of the extension fixture must succeed.");
            Assert.IsTrue(second.TryBuild(items), "The second build of the extension fixture must succeed.");
            Assert.AreEqual(first.DominanceNodeCount, second.DominanceNodeCount,
                $"Two builds from one sequence must carry the identical dominance node count under {packing}.");

            foreach(BoundingBox probe in probes)
            {
                List<int> fromFirst = Collect(first, PackedBoxIndex.QueryMode.Containing, probe);
                List<int> fromSecond = Collect(second, PackedBoxIndex.QueryMode.Containing, probe);

                Assert.AreSequenceEqual(fromFirst, fromSecond,
                    $"Two builds from one sequence must emit the identical Containing order under {packing}.");
            }
        }
    }

    /// <summary>One thousand identical boxes emit ascending registration order under the slot tie-break alone.</summary>
    [TestMethod]
    public void AllTiedCoordinatesEmitAscendingRegistrationOrder()
    {
        //One thousand identical boxes: every dominance sort key ties on every axis, so the
        //slot tie-break — the unique slot value the median sort closes every axis tie with —
        //alone shapes the tree. Rank-sorted emission over an all-duplicates build is
        //ascending registration, derivable by hand for both packings.
        var items = new BoundingBox[1000];
        Array.Fill(items, new BoundingBox(5, 5, 6, 6));
        int[] expected = [.. Enumerable.Range(0, items.Length)];

        foreach(BoxIndexPacking packing in Packings)
        {
            using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, 16));

            Assert.IsTrue(index.TryBuild(items), "The all-duplicates fixture must build.");

            List<int> actual = Collect(index, PackedBoxIndex.QueryMode.Containing, new BoundingBox(5, 5, 6, 6));

            Assert.AreSequenceEqual(expected, actual,
                $"All-tied dominance coordinates must emit ascending registration order under {packing}.");
        }
    }

    //The four single-axis rows share one shape: seventy-two boxes, large enough that the
    //count-halving build splits at depths zero through three — so every one of the four
    //cycled coordinates is a genuine split axis somewhere in the tree, and the query's
    //half-space prune actually runs on the tested coordinate. Only the named coordinate
    //varies across the boxes; every other ordinate is constant and satisfies the probe
    //trivially, so membership is decided by that coordinate alone, boundary equality
    //included. A binding or direction error in the coordinate's build split or query
    //prune discards matching registrations and fails the row.

    /// <summary>MinX alone decides containment: the probe's MinX admits every registration at or below it, boundary included.</summary>
    [TestMethod]
    public void SingleAxisMinXDecidesContainment()
    {
        //MinX runs 0 through 71; the probe's MinX of 35 admits exactly registrations 0
        //through 35, the boundary box included by the non-strict algebra.
        var items = new BoundingBox[72];

        for(int index = 0; index < items.Length; index++)
        {
            items[index] = new BoundingBox(index, 0, 100, 100);
        }

        RunSingleAxisRow(items, new BoundingBox(35, 50, 90, 60), [.. Enumerable.Range(0, 36)], "MinX");
    }

    /// <summary>MaxX alone decides containment: the probe's MaxX admits every registration at or above it, boundary included.</summary>
    [TestMethod]
    public void SingleAxisMaxXDecidesContainment()
    {
        //MaxX runs 28 through 99; the probe's MaxX of 65 admits exactly registrations 37
        //through 71, the boundary box included by the non-strict algebra.
        var items = new BoundingBox[72];

        for(int index = 0; index < items.Length; index++)
        {
            items[index] = new BoundingBox(0, 0, 28 + index, 100);
        }

        RunSingleAxisRow(items, new BoundingBox(5, 50, 65, 60), [.. Enumerable.Range(37, 35)], "MaxX");
    }

    /// <summary>MinY alone decides containment: the probe's MinY admits every registration at or below it, boundary included.</summary>
    [TestMethod]
    public void SingleAxisMinYDecidesContainment()
    {
        //MinY runs 0 through 71; the probe's MinY of 35 admits exactly registrations 0
        //through 35, the boundary box included by the non-strict algebra.
        var items = new BoundingBox[72];

        for(int index = 0; index < items.Length; index++)
        {
            items[index] = new BoundingBox(0, index, 100, 100);
        }

        RunSingleAxisRow(items, new BoundingBox(5, 35, 90, 60), [.. Enumerable.Range(0, 36)], "MinY");
    }

    /// <summary>MaxY alone decides containment: the probe's MaxY admits every registration at or above it, boundary included.</summary>
    [TestMethod]
    public void SingleAxisMaxYDecidesContainment()
    {
        //MaxY runs 28 through 99; the probe's MaxY of 65 admits exactly registrations 37
        //through 71, the boundary box included by the non-strict algebra.
        var items = new BoundingBox[72];

        for(int index = 0; index < items.Length; index++)
        {
            items[index] = new BoundingBox(0, 0, 100, 28 + index);
        }

        RunSingleAxisRow(items, new BoundingBox(5, 5, 90, 65), [.. Enumerable.Range(37, 35)], "MaxY");
    }

    /// <summary>The cross adversary's builds are deterministic and its off-arm probes answer empty in every mode.</summary>
    [TestMethod]
    public void CrossSlatBuildsAreDeterministicAndOffArmProbesAnswerEmpty()
    {
        //The cross adversary: every node union is the full field, the shape that defeats
        //union-bound pruning in every query mode while each stored slat stays thin. Two
        //independent builds must answer the identical full traversal; the sixteen
        //off-arm probes sit strictly inside the quadrant interstices no slat reaches, so
        //every mode must answer empty on every one of them, by construction.
        var slats = new BoundingBox[40];
        CrossSlatFixture.WriteSlats(slats, fieldExtent: 1000d, thickness: 2d);

        var offArmProbes = new BoundingBox[16];
        CrossSlatFixture.WriteOffArmProbes(offArmProbes, 1000d);

        var everything = new BoundingBox(-1, -1, 1001, 1001);

        foreach(BoxIndexPacking packing in Packings)
        {
            var options = new PackedBoxIndexOptions(packing, 32);
            using PackedBoxIndex first = PackedBoxIndex.Create(options);
            using PackedBoxIndex second = PackedBoxIndex.Create(options);

            Assert.IsTrue(first.TryBuild(slats), "The first build of the cross-slat fixture must succeed.");
            Assert.IsTrue(second.TryBuild(slats), "The second build of the cross-slat fixture must succeed.");

            List<int> fromFirst = Collect(first, PackedBoxIndex.QueryMode.Intersecting, everything);
            List<int> fromSecond = Collect(second, PackedBoxIndex.QueryMode.Intersecting, everything);

            Assert.AreSequenceEqual(fromFirst, fromSecond,
                $"Two cross-slat builds from one sequence must emit the identical full traversal under {packing}.");

            foreach(BoundingBox probe in offArmProbes)
            {
                for(int mode = 0; mode < 3; mode++)
                {
                    List<int> actual = Collect(first, (PackedBoxIndex.QueryMode)mode, probe);

                    Assert.HasCount(0, actual,
                        $"Off-arm probes sit strictly inside the quadrant interstices no slat reaches; mode {(PackedBoxIndex.QueryMode)mode} under {packing} must answer empty.");
                }
            }
        }
    }

    /// <summary>A disjoint row of unit boxes spaced two apart: predictable structure, no overlap, every dominance split derivable by hand.</summary>
    /// <param name="count">The number of boxes.</param>
    /// <returns>The fixture items in registration order.</returns>
    private static BoundingBox[] SpacedUnitBoxes(int count)
    {
        var items = new BoundingBox[count];

        for(int index = 0; index < count; index++)
        {
            double offset = index * 2d;
            items[index] = new BoundingBox(offset, 0, offset + 1, 1);
        }

        return items;
    }

    /// <summary>
    /// Builds one single-axis fixture under both packings at capacity four, collects the
    /// Containing emission, sorts it, and asserts it against the hand-derived registration
    /// set — the shared body of the four single-axis dominance-coordinate rows.
    /// </summary>
    /// <param name="items">The single-axis fixture.</param>
    /// <param name="probe">The probe box.</param>
    /// <param name="expected">The hand-derived expected registration set, ascending.</param>
    /// <param name="axisName">The coordinate name under test, for the failure message.</param>
    private static void RunSingleAxisRow(BoundingBox[] items, BoundingBox probe, int[] expected, string axisName)
    {
        foreach(BoxIndexPacking packing in Packings)
        {
            using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, 4));

            Assert.IsTrue(index.TryBuild(items), "The single-axis fixture must build.");

            List<int> actual = Collect(index, PackedBoxIndex.QueryMode.Containing, probe);
            actual.Sort();

            Assert.AreSequenceEqual(expected, actual, $"{axisName} alone must decide containment under {packing}.");
        }
    }

    /// <summary>Enumerates one mode through the public foreach pattern into a list, order preserved.</summary>
    /// <param name="index">The index to query.</param>
    /// <param name="mode">The query mode.</param>
    /// <param name="query">The query box.</param>
    /// <returns>The answered registration indices in enumeration order.</returns>
    private static List<int> Collect(PackedBoxIndex index, PackedBoxIndex.QueryMode mode, BoundingBox query)
    {
        var results = new List<int>();
        PackedBoxIndex.Candidates candidates = mode switch
        {
            PackedBoxIndex.QueryMode.Intersecting => index.Intersecting(in query),
            PackedBoxIndex.QueryMode.ContainedIn => index.ContainedIn(in query),
            _ => index.Containing(in query),
        };

        foreach(int candidate in candidates)
        {
            results.Add(candidate);
        }

        return results;
    }
}
