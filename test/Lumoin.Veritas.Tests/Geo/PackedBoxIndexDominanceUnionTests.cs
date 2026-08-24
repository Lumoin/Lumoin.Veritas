using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The union canon for <see cref="PackedBoxIndex"/>'s embedded dominance tree: the
/// hand-derived literal anchor over a fully derivable fixture, the child-numbering
/// invariant the internal-union reverse sweep depends on, the union-consistency
/// property checked independently of the production fill, repeat-build identity over
/// the union columns, and the union accessor's range guards at the leaf/root boundary.
/// The dominance tree's other structural facts (ranges, split facts, order) are the
/// canon of <see cref="PackedBoxIndexDominanceTests"/>; this suite is the union
/// column's own.
/// </summary>
[TestClass]
internal sealed class PackedBoxIndexDominanceUnionTests
{
    /// <summary>Both packing families, swept wherever a row must hold for each.</summary>
    private static readonly BoxIndexPacking[] Packings = [BoxIndexPacking.SortTileRecursive, BoxIndexPacking.HilbertCurve];

    /// <summary>The twenty-item spaced row's dominance ranges, split facts, and unions equal the hand derivation, node by node.</summary>
    [TestMethod]
    public void SpacedRowUnionsMatchTheHandDerivation()
    {
        BoundingBox[] items = SpacedUnitBoxes(20);
        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 4));

        Assert.IsTrue(index.TryBuild(items), "The twenty-item spaced row must build under SortTileRecursive/capacity 4.");

        //The Sort-Tile-Recursive leaf pass sorts by center X first; centers here are
        //strictly increasing with registration index (box i spans [2i, 2i + 1]), so that
        //sort alone reproduces registration order. The within-slice center-Y re-sort that
        //follows ties on the constant Y = 0.5 for every item and closes on entry index,
        //which by then already equals registration order — so the whole leaf pass is the
        //identity permutation, and every literal below is derivable by hand straight off
        //the registration index.
        for(int slot = 0; slot < items.Length; slot++)
        {
            Assert.AreEqual(slot, index.ItemSlotRegistration(slot), $"Item slot {slot} must carry back to registration {slot}: the leaf pass is the identity permutation on this fixture.");
        }

        //Twenty items exceed the eight-item dominance leaf ceiling, so the root splits at
        //the item-count median into two tens; each ten again exceeds the ceiling and
        //splits into two fives, and five is a leaf. The split axis cycles by depth: depth
        //zero is MinX (axis 0), depth one is MaxX (axis 1).
        Assert.AreEqual(7, index.DominanceNodeCount, "Twenty items split 10+10 then 5+5 on each half: seven dominance nodes.");

        //Node 0, the root: MinX runs 0, 2, 4, …, 38 across all twenty slots, so its median
        //at position ten is slot 10's MinX = 20.
        AssertRange(index, node: 0, expectedLeft: 1, expectedItemStart: 0, expectedItemSpan: 20, context: "node 0 (root)");
        AssertSplitFacts(index, node: 0, expectedRight: 4, expectedAxis: 0, expectedSplit: 20, context: "node 0 (root)");
        AssertUnion(index, node: 0, minX: 0, minY: 0, maxX: 39, maxY: 1, context: "node 0 (root)");

        //Node 1, the root's left child over slots 0..9: MaxX runs 1, 3, …, 19, so the
        //median at position five (within this range) is slot 5's MaxX = 11.
        AssertRange(index, node: 1, expectedLeft: 2, expectedItemStart: 0, expectedItemSpan: 10, context: "node 1");
        AssertSplitFacts(index, node: 1, expectedRight: 3, expectedAxis: 1, expectedSplit: 11, context: "node 1");
        AssertUnion(index, node: 1, minX: 0, minY: 0, maxX: 19, maxY: 1, context: "node 1");

        //Node 2, a leaf over slots 0..4 (five items, at the leaf ceiling's floor half).
        AssertRange(index, node: 2, expectedLeft: -1, expectedItemStart: 0, expectedItemSpan: 5, context: "node 2 (leaf)");
        AssertUnion(index, node: 2, minX: 0, minY: 0, maxX: 9, maxY: 1, context: "node 2 (leaf)");

        //Node 3, a leaf over slots 5..9.
        AssertRange(index, node: 3, expectedLeft: -1, expectedItemStart: 5, expectedItemSpan: 5, context: "node 3 (leaf)");
        AssertUnion(index, node: 3, minX: 10, minY: 0, maxX: 19, maxY: 1, context: "node 3 (leaf)");

        //Node 4, the root's right child over slots 10..19: MaxX runs 21, 23, …, 39, so the
        //median at position five (within this range) is slot 15's MaxX = 31.
        AssertRange(index, node: 4, expectedLeft: 5, expectedItemStart: 10, expectedItemSpan: 10, context: "node 4");
        AssertSplitFacts(index, node: 4, expectedRight: 6, expectedAxis: 1, expectedSplit: 31, context: "node 4");
        AssertUnion(index, node: 4, minX: 20, minY: 0, maxX: 39, maxY: 1, context: "node 4");

        //Node 5, a leaf over slots 10..14.
        AssertRange(index, node: 5, expectedLeft: -1, expectedItemStart: 10, expectedItemSpan: 5, context: "node 5 (leaf)");
        AssertUnion(index, node: 5, minX: 20, minY: 0, maxX: 29, maxY: 1, context: "node 5 (leaf)");

        //Node 6, a leaf over slots 15..19.
        AssertRange(index, node: 6, expectedLeft: -1, expectedItemStart: 15, expectedItemSpan: 5, context: "node 6 (leaf)");
        AssertUnion(index, node: 6, minX: 30, minY: 0, maxX: 39, maxY: 1, context: "node 6 (leaf)");
    }

    /// <summary>Every dominance node's left and right child index strictly exceeds its own, across the named fixture family and a seeded-random fixture.</summary>
    [TestMethod]
    public void ChildIndicesExceedTheirParentEverywhere()
    {
        //The internal-union reverse sweep composes each internal node's union from its two
        //children in one descending-index pass over the node table; that composition is
        //sound only because the stack build's preorder numbering (right child pushed
        //first, so the pop order reproduces recursive preorder) guarantees every child's
        //index strictly exceeds its parent's. This row pins that invariant rather than
        //assuming it.
        BoundingBox[] randomItems = RandomBoxes(startState: 90210UL, size: 2000, originExtent: 10000d, maximumDimension: 50d);
        var fixtures = new List<(string Name, BoundingBox[] Items)>(PackedBoxIndexFixtureFamily.NamedFixtures());
        fixtures.Add(("seeded random (90210)", randomItems));

        foreach((string name, BoundingBox[] items) in fixtures)
        {
            foreach(BoxIndexPacking packing in Packings)
            {
                using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, 16));

                Assert.IsTrue(index.TryBuild(items), $"The '{name}' fixture must build under {packing}/capacity 16.");

                for(int node = 0; node < index.DominanceNodeCount; node++)
                {
                    (int left, _, _) = index.DominanceNodeRange(node);

                    if(left < 0)
                    {
                        continue;
                    }

                    (int right, _, _) = index.DominanceNodeSplitFacts(node);

                    Assert.IsGreaterThan(node, left, $"'{name}', {packing}: node {node}'s left child {left} must exceed its own index.");
                    Assert.IsGreaterThan(node, right, $"'{name}', {packing}: node {node}'s right child {right} must exceed its own index.");
                }
            }
        }
    }

    /// <summary>Every dominance node's stored union equals the independent fold over its item range, read through the public diagnostic accessors alone.</summary>
    [TestMethod]
    public void EveryNodeUnionEqualsTheFoldOverItsItemRange()
    {
        var fixtures = new List<(string Name, BoundingBox[] Items)>(PackedBoxIndexFixtureFamily.NamedFixtures());
        fixtures.Add(("extension fixture", PackedBoxIndexFixtureFamily.ExtensionFixture()));
        fixtures.Add(("seeded random (3)", RandomBoxes(startState: 3UL, size: 1000, originExtent: 10000d, maximumDimension: 50d)));
        fixtures.Add(("seeded random (77)", RandomBoxes(startState: 77UL, size: 1000, originExtent: 10000d, maximumDimension: 50d)));

        int[] capacities = [4, 16];

        foreach((string name, BoundingBox[] items) in fixtures)
        {
            foreach(BoxIndexPacking packing in Packings)
            {
                foreach(int capacity in capacities)
                {
                    using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, capacity));

                    Assert.IsTrue(index.TryBuild(items), $"The '{name}' fixture must build under {packing}/capacity {capacity}.");

                    for(int node = 0; node < index.DominanceNodeCount; node++)
                    {
                        (int left, int itemStart, int itemSpan) = index.DominanceNodeRange(node);
                        (double expectedMinX, double expectedMinY, double expectedMaxX, double expectedMaxY) = FoldUnionOverItemRange(index, items, itemStart, itemSpan);
                        (double actualMinX, double actualMinY, double actualMaxX, double actualMaxY) = index.DominanceNodeUnion(node);
                        string context = $"'{name}', {packing}, capacity {capacity}, node {node} (left {left})";

                        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expectedMinX), BitConverter.DoubleToInt64Bits(actualMinX), $"{context}: union MinX diverged from the independent fold.");
                        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expectedMinY), BitConverter.DoubleToInt64Bits(actualMinY), $"{context}: union MinY diverged from the independent fold.");
                        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expectedMaxX), BitConverter.DoubleToInt64Bits(actualMaxX), $"{context}: union MaxX diverged from the independent fold.");
                        Assert.AreEqual(BitConverter.DoubleToInt64Bits(expectedMaxY), BitConverter.DoubleToInt64Bits(actualMaxY), $"{context}: union MaxY diverged from the independent fold.");
                    }
                }
            }
        }
    }

    /// <summary>Two builds from one seeded-random sequence carry bitwise identical union columns, node by node.</summary>
    [TestMethod]
    public void RepeatBuildsCarryBitwiseIdenticalUnionColumns()
    {
        BoundingBox[] items = RandomBoxes(startState: 3UL, size: 1000, originExtent: 10000d, maximumDimension: 50d);

        foreach(BoxIndexPacking packing in Packings)
        {
            var options = new PackedBoxIndexOptions(packing, 16);
            using PackedBoxIndex first = PackedBoxIndex.Create(options);
            using PackedBoxIndex second = PackedBoxIndex.Create(options);

            Assert.IsTrue(first.TryBuild(items), $"The first build of the seed-3 random fixture must succeed under {packing}.");
            Assert.IsTrue(second.TryBuild(items), $"The second build of the seed-3 random fixture must succeed under {packing}.");
            Assert.AreEqual(first.DominanceNodeCount, second.DominanceNodeCount, $"Two builds from one sequence must carry the identical dominance node count under {packing}.");

            for(int node = 0; node < first.DominanceNodeCount; node++)
            {
                (double firstMinX, double firstMinY, double firstMaxX, double firstMaxY) = first.DominanceNodeUnion(node);
                (double secondMinX, double secondMinY, double secondMaxX, double secondMaxY) = second.DominanceNodeUnion(node);
                string context = $"{packing}, node {node}";

                Assert.AreEqual(BitConverter.DoubleToInt64Bits(firstMinX), BitConverter.DoubleToInt64Bits(secondMinX), $"{context}: union MinX must agree bitwise between the two builds.");
                Assert.AreEqual(BitConverter.DoubleToInt64Bits(firstMinY), BitConverter.DoubleToInt64Bits(secondMinY), $"{context}: union MinY must agree bitwise between the two builds.");
                Assert.AreEqual(BitConverter.DoubleToInt64Bits(firstMaxX), BitConverter.DoubleToInt64Bits(secondMaxX), $"{context}: union MaxX must agree bitwise between the two builds.");
                Assert.AreEqual(BitConverter.DoubleToInt64Bits(firstMaxY), BitConverter.DoubleToInt64Bits(secondMaxY), $"{context}: union MaxY must agree bitwise between the two builds.");
            }
        }
    }

    /// <summary>The union accessor answers finite, ordered bounds for every node across the root/leaf boundary sizes and rejects an out-of-range node.</summary>
    [TestMethod]
    public void UnionAccessorGuardsItsRangeAndAnswersEveryNodeAtBoundarySizes()
    {
        //The union columns are sized by the same dominance node bound as the rest of the
        //node table; answering every node across the root/leaf boundary — a single-leaf
        //build has no internal node at all, a nine-item build's root is internal over two
        //leaves — pins that sizing rather than assuming it holds past the boundary.
        int[] itemCounts = [1, 8, 9, 20];

        foreach(int itemCount in itemCounts)
        {
            BoundingBox[] items = SpacedUnitBoxes(itemCount);
            using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 4));

            Assert.IsTrue(index.TryBuild(items), $"The {itemCount}-item spaced row must build under SortTileRecursive/capacity 4.");

            for(int node = 0; node < index.DominanceNodeCount; node++)
            {
                (double minX, double minY, double maxX, double maxY) = index.DominanceNodeUnion(node);

                Assert.IsTrue(double.IsFinite(minX) && double.IsFinite(minY) && double.IsFinite(maxX) && double.IsFinite(maxY),
                    $"{itemCount} items, node {node}: every union ordinate must be finite.");
                Assert.IsLessThanOrEqualTo(maxX, minX, $"{itemCount} items, node {node}: union MinX must not exceed MaxX.");
                Assert.IsLessThanOrEqualTo(maxY, minY, $"{itemCount} items, node {node}: union MinY must not exceed MaxY.");
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => index.DominanceNodeUnion(-1),
                $"{itemCount} items: DominanceNodeUnion must reject a negative node.");
            Assert.Throws<ArgumentOutOfRangeException>(() => index.DominanceNodeUnion(index.DominanceNodeCount),
                $"{itemCount} items: DominanceNodeUnion must reject a node at the count bound.");
        }
    }

    /// <summary>Asserts one dominance node's always-written left/leaf slot and item range against a hand-derived expectation.</summary>
    /// <param name="index">The built index.</param>
    /// <param name="node">The dominance node.</param>
    /// <param name="expectedLeft">The hand-derived left/leaf slot.</param>
    /// <param name="expectedItemStart">The hand-derived item-range start.</param>
    /// <param name="expectedItemSpan">The hand-derived item-range span.</param>
    /// <param name="context">The failure-message context.</param>
    private static void AssertRange(PackedBoxIndex index, int node, int expectedLeft, int expectedItemStart, int expectedItemSpan, string context)
    {
        (int left, int itemStart, int itemSpan) = index.DominanceNodeRange(node);

        Assert.AreEqual(expectedLeft, left, $"{context}: left/leaf slot diverged from the hand derivation.");
        Assert.AreEqual(expectedItemStart, itemStart, $"{context}: item start diverged from the hand derivation.");
        Assert.AreEqual(expectedItemSpan, itemSpan, $"{context}: item span diverged from the hand derivation.");
    }

    /// <summary>Asserts one internal dominance node's right child, split axis, and split value against a hand-derived expectation.</summary>
    /// <param name="index">The built index.</param>
    /// <param name="node">The dominance node.</param>
    /// <param name="expectedRight">The hand-derived right child.</param>
    /// <param name="expectedAxis">The hand-derived split axis.</param>
    /// <param name="expectedSplit">The hand-derived split value.</param>
    /// <param name="context">The failure-message context.</param>
    private static void AssertSplitFacts(PackedBoxIndex index, int node, int expectedRight, int expectedAxis, double expectedSplit, string context)
    {
        (int right, int axis, double split) = index.DominanceNodeSplitFacts(node);

        Assert.AreEqual(expectedRight, right, $"{context}: right child diverged from the hand derivation.");
        Assert.AreEqual(expectedAxis, axis, $"{context}: split axis diverged from the hand derivation.");
        Assert.AreEqual(expectedSplit, split, $"{context}: split value diverged from the hand derivation.");
    }

    /// <summary>Asserts one dominance node's subtree union box against a hand-derived expectation, ordinate by ordinate.</summary>
    /// <param name="index">The built index.</param>
    /// <param name="node">The dominance node.</param>
    /// <param name="minX">The hand-derived union MinX.</param>
    /// <param name="minY">The hand-derived union MinY.</param>
    /// <param name="maxX">The hand-derived union MaxX.</param>
    /// <param name="maxY">The hand-derived union MaxY.</param>
    /// <param name="context">The failure-message context.</param>
    private static void AssertUnion(PackedBoxIndex index, int node, double minX, double minY, double maxX, double maxY, string context)
    {
        (double actualMinX, double actualMinY, double actualMaxX, double actualMaxY) = index.DominanceNodeUnion(node);

        Assert.AreEqual(minX, actualMinX, $"{context}: union MinX diverged from the hand derivation.");
        Assert.AreEqual(minY, actualMinY, $"{context}: union MinY diverged from the hand derivation.");
        Assert.AreEqual(maxX, actualMaxX, $"{context}: union MaxX diverged from the hand derivation.");
        Assert.AreEqual(maxY, actualMaxY, $"{context}: union MaxY diverged from the hand derivation.");
    }

    /// <summary>
    /// Folds min/max over items[index.ItemSlotRegistration(index.DominanceOrderSlot(position))]
    /// for position in [itemStart, itemStart + itemSpan) — the same fold
    /// <see cref="PackedBoxIndex.DominanceNodeUnion(int)"/> is contracted to answer, computed here
    /// from the public diagnostic accessors rather than trusted from the production fill.
    /// </summary>
    /// <param name="index">The built index.</param>
    /// <param name="items">The registered items.</param>
    /// <param name="itemStart">The node's item-range start.</param>
    /// <param name="itemSpan">The node's item-range span.</param>
    /// <returns>The folded union box.</returns>
    private static (double MinX, double MinY, double MaxX, double MaxY) FoldUnionOverItemRange(PackedBoxIndex index, BoundingBox[] items, int itemStart, int itemSpan)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;

        for(int position = itemStart; position < itemStart + itemSpan; position++)
        {
            int slot = index.DominanceOrderSlot(position);
            int registration = index.ItemSlotRegistration(slot);
            BoundingBox item = items[registration];
            minX = Math.Min(minX, item.MinX);
            minY = Math.Min(minY, item.MinY);
            maxX = Math.Max(maxX, item.MaxX);
            maxY = Math.Max(maxY, item.MaxY);
        }

        return (minX, minY, maxX, maxY);
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

    /// <summary>Deterministic mixed-bit boxes: origin ordinates in [0, originExtent), dimensions in [0, maximumDimension).</summary>
    /// <param name="startState">The mixing start value that makes the fixture reproducible.</param>
    /// <param name="size">The number of boxes.</param>
    /// <param name="originExtent">The exclusive upper bound of each origin ordinate.</param>
    /// <param name="maximumDimension">The exclusive upper bound of each box dimension.</param>
    /// <returns>The fixture items in registration order.</returns>
    private static BoundingBox[] RandomBoxes(ulong startState, int size, double originExtent, double maximumDimension)
    {
        ulong state = startState;
        var items = new BoundingBox[size];

        for(int index = 0; index < size; index++)
        {
            double x = DeterministicBitMixer.NextUnitDouble(ref state) * originExtent;
            double y = DeterministicBitMixer.NextUnitDouble(ref state) * originExtent;
            double width = DeterministicBitMixer.NextUnitDouble(ref state) * maximumDimension;
            double height = DeterministicBitMixer.NextUnitDouble(ref state) * maximumDimension;
            items[index] = new BoundingBox(x, y, x + width, y + height);
        }

        return items;
    }
}
