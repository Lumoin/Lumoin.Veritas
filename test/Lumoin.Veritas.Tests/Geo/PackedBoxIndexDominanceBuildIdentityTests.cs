using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The pre-sorted dominance build's primary identity gate: an independent
/// test-side per-node re-sorting construction, checked
/// column for column against <see cref="PackedBoxIndex"/>'s internal
/// diagnostic accessors. The pre-sorted construction sorts each axis order
/// once up front and partitions it stably at every split instead of
/// re-sorting each node's range; this suite exists to prove that shortcut
/// never changes a single stored value — the reference builder below
/// reproduces the original per-node sort verbatim, iteratively over an
/// explicit work stack, and every row asserts leaf-masked equality of the
/// node table and the final dominance order.
/// </summary>
[TestClass]
internal sealed class PackedBoxIndexDominanceBuildIdentityTests
{
    /// <summary>The dominance tree's leaf ceiling, mirroring the production constant: a range at or below this size refines in place instead of splitting.</summary>
    private const int DominanceLeafSize = 8;

    /// <summary>Both packing families, swept wherever a row must hold for each.</summary>
    private static readonly BoxIndexPacking[] Packings = [BoxIndexPacking.SortTileRecursive, BoxIndexPacking.HilbertCurve];

    /// <summary>The node capacities this suite's fixture and small-count rows sweep.</summary>
    private static readonly int[] Capacities = [4, 16];

    /// <summary>Every named fixture, packing, and capacity combination matches the reference build.</summary>
    [TestMethod]
    public void FixtureFamilyBuildsMatchTheReferenceAcrossPackingsAndCapacities()
    {
        foreach((string name, BoundingBox[] items) in PackedBoxIndexFixtureFamily.NamedFixtures())
        {
            foreach(BoxIndexPacking packing in Packings)
            {
                foreach(int capacity in Capacities)
                {
                    using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, capacity));

                    Assert.IsTrue(index.TryBuild(items), $"The '{name}' fixture must build under {packing}/capacity {capacity}.");

                    AssertTreeMatchesReference(index, items, $"fixture '{name}', {packing}, capacity {capacity}");
                }
            }
        }
    }

    /// <summary>The coincident-center cross fixture matches the reference build at two slat counts.</summary>
    [TestMethod]
    public void CrossSlatFixturesMatchTheReferenceUnderCoincidentCenterTies()
    {
        //The cross adversary's every centre coincides on the field centre, so every packing
        //key ties: this is the hardest tie-break stress the reference builder faces, since
        //almost no comparison is settled by the ordinate itself.
        int[] slatCounts = [40, 200];

        foreach(int slatCount in slatCounts)
        {
            var slats = new BoundingBox[slatCount];
            CrossSlatFixture.WriteSlats(slats, fieldExtent: 1000d, thickness: 2d);

            foreach(BoxIndexPacking packing in Packings)
            {
                using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, 16));

                Assert.IsTrue(index.TryBuild(slats), $"The {slatCount}-slat cross fixture must build under {packing}.");

                AssertTreeMatchesReference(index, slats, $"cross slats ({slatCount}), {packing}");
            }
        }
    }

    /// <summary>Deterministic pseudo-random box sets match the reference build across seeds and sizes.</summary>
    [TestMethod]
    public void SeededRandomBoxesMatchTheReference()
    {
        ulong[] seeds = [17UL, 4242UL, 987651UL];
        int[] sizes = [100, 1000];

        foreach(ulong seed in seeds)
        {
            foreach(int size in sizes)
            {
                BoundingBox[] items = RandomBoxes(seed, size, ordinateExtent: 10000d, maximumDimension: 50d);

                foreach(BoxIndexPacking packing in Packings)
                {
                    using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, 16));

                    Assert.IsTrue(index.TryBuild(items), $"The seed {seed} size {size} random fixture must build under {packing}.");

                    AssertTreeMatchesReference(index, items, $"random seed {seed}, size {size}, {packing}");
                }
            }
        }
    }

    /// <summary>A small-integer-grid fixture, where exact ties dominate every comparison, matches the reference build.</summary>
    [TestMethod]
    public void DuplicateHeavySmallIntegerGridMatchesTheReference()
    {
        //The highest-risk identity surface: ordinates drawn from a tiny integer grid make
        //exact coordinate ties pervasive, so nearly every dominance comparison falls through
        //to the slot tie-break rather than the ordinate itself — precisely the condition most
        //likely to expose any divergence between the pre-sorted stable-partition build and
        //the reference per-node re-sort it must reproduce exactly.
        ulong[] seeds = [7UL, 91UL];

        foreach(ulong seed in seeds)
        {
            BoundingBox[] items = SmallIntegerGridBoxes(seed, 500);

            foreach(BoxIndexPacking packing in Packings)
            {
                using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, 16));

                Assert.IsTrue(index.TryBuild(items), $"The small-integer-grid fixture (seed {seed}) must build under {packing}.");

                AssertTreeMatchesReference(index, items, $"small-integer-grid seed {seed}, {packing}");
            }
        }
    }

    /// <summary>Item counts at or below the leaf ceiling produce one dominance node; nine items force the depth-zero MinX split.</summary>
    [TestMethod]
    public void SmallItemCountsProduceOneLeafAndNineProducesTheDepthZeroMinXSplit()
    {
        int[] singleLeafCounts = [1, 4, 8];

        foreach(int count in singleLeafCounts)
        {
            BoundingBox[] items = SpacedUnitBoxes(count);

            foreach(BoxIndexPacking packing in Packings)
            {
                foreach(int capacity in Capacities)
                {
                    using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, capacity));

                    Assert.IsTrue(index.TryBuild(items), $"The {count}-item spaced row must build under {packing}/capacity {capacity}.");
                    Assert.AreEqual(1, index.DominanceNodeCount,
                        $"{count} items at or below the leaf ceiling must produce exactly one dominance node under {packing}/capacity {capacity}.");

                    AssertTreeMatchesReference(index, items, $"spaced row of {count}, {packing}, capacity {capacity}");
                }
            }
        }

        BoundingBox[] nineItems = SpacedUnitBoxes(9);

        foreach(BoxIndexPacking packing in Packings)
        {
            foreach(int capacity in Capacities)
            {
                using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, capacity));

                Assert.IsTrue(index.TryBuild(nineItems), $"The nine-item spaced row must build under {packing}/capacity {capacity}.");
                Assert.AreEqual(3, index.DominanceNodeCount,
                    $"Nine items must split into three dominance nodes under {packing}/capacity {capacity}.");

                (_, int axis, _) = index.DominanceNodeSplitFacts(0);
                Assert.AreEqual(0, axis, $"The root's depth-zero split must cycle to axis 0 (MinX) under {packing}/capacity {capacity}.");

                AssertTreeMatchesReference(index, nineItems, $"spaced row of nine, {packing}, capacity {capacity}");
            }
        }
    }

    /// <summary>Two builds from the duplicate-heavy fixture agree bitwise, index versus index.</summary>
    [TestMethod]
    public void RepeatBuildsFromTheDuplicateHeavyFixtureAreBitwiseIdenticalIndexVersusIndex()
    {
        BoundingBox[] items = SmallIntegerGridBoxes(startState: 7UL, size: 500);

        foreach(BoxIndexPacking packing in Packings)
        {
            var options = new PackedBoxIndexOptions(packing, 16);
            using PackedBoxIndex first = PackedBoxIndex.Create(options);
            using PackedBoxIndex second = PackedBoxIndex.Create(options);

            Assert.IsTrue(first.TryBuild(items), $"The first build of the duplicate-heavy fixture must succeed under {packing}.");
            Assert.IsTrue(second.TryBuild(items), $"The second build of the duplicate-heavy fixture must succeed under {packing}.");
            Assert.AreEqual(first.DominanceNodeCount, second.DominanceNodeCount,
                $"Two builds from one item sequence must carry the identical dominance node count under {packing}.");

            for(int node = 0; node < first.DominanceNodeCount; node++)
            {
                (int firstLeft, int firstItemStart, int firstItemSpan) = first.DominanceNodeRange(node);
                (int secondLeft, int secondItemStart, int secondItemSpan) = second.DominanceNodeRange(node);

                Assert.AreEqual(firstLeft, secondLeft, $"Node {node} left/leaf must agree between the two builds under {packing}.");
                Assert.AreEqual(firstItemStart, secondItemStart, $"Node {node} item start must agree between the two builds under {packing}.");
                Assert.AreEqual(firstItemSpan, secondItemSpan, $"Node {node} item span must agree between the two builds under {packing}.");

                if(firstLeft >= 0)
                {
                    (int firstRight, int firstAxis, double firstSplit) = first.DominanceNodeSplitFacts(node);
                    (int secondRight, int secondAxis, double secondSplit) = second.DominanceNodeSplitFacts(node);

                    Assert.AreEqual(firstRight, secondRight, $"Node {node} right child must agree between the two builds under {packing}.");
                    Assert.AreEqual(firstAxis, secondAxis, $"Node {node} split axis must agree between the two builds under {packing}.");
                    Assert.AreEqual(
                        BitConverter.DoubleToInt64Bits(firstSplit),
                        BitConverter.DoubleToInt64Bits(secondSplit),
                        $"Node {node} split value must agree bitwise between the two builds under {packing}.");
                }
            }

            for(int position = 0; position < first.Count; position++)
            {
                Assert.AreEqual(first.DominanceOrderSlot(position), second.DominanceOrderSlot(position),
                    $"Dominance order position {position} must agree between the two builds under {packing}.");
            }
        }
    }

    /// <summary>The dominance accessors guard a leaf node's never-written split facts and reject every out-of-range argument.</summary>
    [TestMethod]
    public void AccessorsGuardLeafSplitFactsAndOutOfRangeArguments()
    {
        //Nine items split under a root into two leaves, so this one build carries both an
        //internal node (the root) and at least one leaf node to exercise every guard.
        BoundingBox[] items = SpacedUnitBoxes(9);
        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16));

        Assert.IsTrue(index.TryBuild(items), "The nine-item spaced row must build.");

        int leafNode = -1;

        for(int node = 0; node < index.DominanceNodeCount; node++)
        {
            (int left, _, _) = index.DominanceNodeRange(node);

            if(left < 0)
            {
                leafNode = node;

                break;
            }
        }

        Assert.IsGreaterThanOrEqualTo(0, leafNode, "The fixture must contain at least one leaf dominance node to exercise the guard.");
        Assert.Throws<ArgumentException>(() => index.DominanceNodeSplitFacts(leafNode),
            "A leaf dominance node must refuse to answer its never-written split facts.");

        Assert.Throws<ArgumentOutOfRangeException>(() => index.DominanceNodeRange(-1),
            "DominanceNodeRange must reject a negative node.");
        Assert.Throws<ArgumentOutOfRangeException>(() => index.DominanceNodeRange(index.DominanceNodeCount),
            "DominanceNodeRange must reject a node at the count bound.");
        Assert.Throws<ArgumentOutOfRangeException>(() => index.DominanceNodeSplitFacts(-1),
            "DominanceNodeSplitFacts must reject a negative node.");
        Assert.Throws<ArgumentOutOfRangeException>(() => index.DominanceNodeSplitFacts(index.DominanceNodeCount),
            "DominanceNodeSplitFacts must reject a node at the count bound.");
        Assert.Throws<ArgumentOutOfRangeException>(() => index.DominanceOrderSlot(-1),
            "DominanceOrderSlot must reject a negative position.");
        Assert.Throws<ArgumentOutOfRangeException>(() => index.DominanceOrderSlot(index.Count),
            "DominanceOrderSlot must reject a position at the count bound.");
        Assert.Throws<ArgumentOutOfRangeException>(() => index.ItemSlotRegistration(-1),
            "ItemSlotRegistration must reject a negative item slot.");
        Assert.Throws<ArgumentOutOfRangeException>(() => index.ItemSlotRegistration(index.Count),
            "ItemSlotRegistration must reject an item slot at the count bound.");
    }

    /// <summary>
    /// Derives the built index's slot boxes from <paramref name="items"/>
    /// through <see cref="PackedBoxIndex.ItemSlotRegistration"/>, builds the
    /// reference dominance tree over them, and asserts the leaf-masked column
    /// equality contract: node count, root, every node's range, every
    /// internal node's split facts (the split value compared bitwise), and
    /// every position of the dominance order.
    /// </summary>
    /// <param name="index">The built index under test.</param>
    /// <param name="items">The items the index was built from, in registration order.</param>
    /// <param name="context">The failure-message context identifying the fixture and configuration.</param>
    internal static void AssertTreeMatchesReference(PackedBoxIndex index, BoundingBox[] items, string context)
    {
        int itemCount = index.Count;
        var slotBoxes = new BoundingBox[itemCount];

        for(int slot = 0; slot < itemCount; slot++)
        {
            int registration = index.ItemSlotRegistration(slot);
            slotBoxes[slot] = items[registration];
        }

        ReferenceDominanceTree reference = BuildReferenceDominanceTree(slotBoxes);

        Assert.AreEqual(reference.NodeCount, index.DominanceNodeCount, $"{context}: dominance node count diverged from the reference build.");

        if(itemCount > 0)
        {
            Assert.AreEqual(0, reference.Root, $"{context}: the reference build's own root must be node 0 by its preorder numbering.");
        }

        for(int node = 0; node < reference.NodeCount; node++)
        {
            (int left, int itemStart, int itemSpan) = index.DominanceNodeRange(node);

            Assert.AreEqual(reference.Left[node], left, $"{context}: node {node} left/leaf slot diverged from the reference build.");
            Assert.AreEqual(reference.ItemStart[node], itemStart, $"{context}: node {node} item start diverged from the reference build.");
            Assert.AreEqual(reference.ItemSpan[node], itemSpan, $"{context}: node {node} item span diverged from the reference build.");

            if(reference.Left[node] >= 0)
            {
                (int right, int axis, double split) = index.DominanceNodeSplitFacts(node);

                Assert.AreEqual(reference.Right[node], right, $"{context}: node {node} right child diverged from the reference build.");
                Assert.AreEqual(reference.Axis[node], axis, $"{context}: node {node} split axis diverged from the reference build.");
                Assert.AreEqual(
                    BitConverter.DoubleToInt64Bits(reference.Split[node]),
                    BitConverter.DoubleToInt64Bits(split),
                    $"{context}: node {node} split value diverged from the reference build (bitwise comparison).");
            }
        }

        for(int position = 0; position < itemCount; position++)
        {
            Assert.AreEqual(reference.DominanceOrder[position], index.DominanceOrderSlot(position),
                $"{context}: dominance order position {position} diverged from the reference build.");
        }
    }

    /// <summary>
    /// Reimplements the retired per-node re-sorting dominance build verbatim,
    /// over slot boxes already in the production build's leaf packing order.
    /// Iterative over an explicit work-item stack — the depth bound for the
    /// item counts these tests use never approaches the fixed sixty-four-slot
    /// stack below.
    /// </summary>
    /// <param name="slotBoxes">The item boxes in production leaf packing order, indexed by slot.</param>
    /// <returns>The reference build's node table and dominance order.</returns>
    private static ReferenceDominanceTree BuildReferenceDominanceTree(BoundingBox[] slotBoxes)
    {
        int itemCount = slotBoxes.Length;
        int nodeBound = itemCount <= 0 ? 0 : (2 * ((itemCount + 3) / 4)) - 1;

        var left = new int[nodeBound];
        var right = new int[nodeBound];
        var axis = new int[nodeBound];
        var split = new double[nodeBound];
        var itemStart = new int[nodeBound];
        var itemSpan = new int[nodeBound];
        var dominanceOrder = new int[itemCount];

        if(itemCount == 0)
        {
            return new ReferenceDominanceTree(left, right, axis, split, itemStart, itemSpan, dominanceOrder, NodeCount: 0, Root: -1);
        }

        for(int slot = 0; slot < itemCount; slot++)
        {
            dominanceOrder[slot] = slot;
        }

        //The median split halves the range each level down to eight-item leaves; N stays at
        //or below two thousand across this suite, so a fixed sixty-four-entry stack is far
        //more headroom than the deepest build ever needs.
        var buildStack = new ReferenceBuildWorkItem[64];
        int nodeCount = 0;
        int root = -1;
        int buildTop = 0;
        buildStack[buildTop] = new ReferenceBuildWorkItem(0, itemCount, Depth: 0, ParentNode: -1, IsLeftChild: false);
        buildTop++;

        while(buildTop > 0)
        {
            buildTop--;
            ReferenceBuildWorkItem work = buildStack[buildTop];
            int node = nodeCount;
            nodeCount++;

            if(work.ParentNode < 0)
            {
                root = node;
            }
            else if(work.IsLeftChild)
            {
                left[work.ParentNode] = node;
            }
            else
            {
                right[work.ParentNode] = node;
            }

            itemStart[node] = work.ItemStart;
            itemSpan[node] = work.ItemCount;

            if(work.ItemCount <= DominanceLeafSize)
            {
                left[node] = -1;

                continue;
            }

            int splitAxis = work.Depth % 4;

            SortRangeByAxis(slotBoxes, dominanceOrder, work.ItemStart, work.ItemCount, splitAxis);

            int half = work.ItemCount / 2;
            int medianSlot = dominanceOrder[work.ItemStart + half];
            axis[node] = splitAxis;
            split[node] = AxisOrdinate(slotBoxes[medianSlot], splitAxis);

            buildStack[buildTop] = new ReferenceBuildWorkItem(work.ItemStart + half, work.ItemCount - half, work.Depth + 1, node, IsLeftChild: false);
            buildTop++;
            buildStack[buildTop] = new ReferenceBuildWorkItem(work.ItemStart, half, work.Depth + 1, node, IsLeftChild: true);
            buildTop++;
        }

        return new ReferenceDominanceTree(left, right, axis, split, itemStart, itemSpan, dominanceOrder, nodeCount, root);
    }

    /// <summary>
    /// Sorts <paramref name="dominanceOrder"/>'s range
    /// [<paramref name="itemStart"/>, <paramref name="itemStart"/> + <paramref name="itemCount"/>)
    /// by the composite (ordinate, slot) key on <paramref name="axis"/>,
    /// writing the sorted slot sequence back into that same range — the
    /// per-node re-sort the retired construction ran at every internal node.
    /// </summary>
    /// <param name="slotBoxes">The item boxes indexed by slot.</param>
    /// <param name="dominanceOrder">The dominance order being refined in place.</param>
    /// <param name="itemStart">The range's first position.</param>
    /// <param name="itemCount">The range's length.</param>
    /// <param name="axis">The sort axis: 0 MinX, 1 MaxX, 2 MinY, 3 MaxY.</param>
    private static void SortRangeByAxis(BoundingBox[] slotBoxes, int[] dominanceOrder, int itemStart, int itemCount, int axis)
    {
        var keys = new ReferenceSortKey[itemCount];

        for(int position = 0; position < itemCount; position++)
        {
            int slot = dominanceOrder[itemStart + position];
            keys[position] = new ReferenceSortKey(AxisOrdinate(slotBoxes[slot], axis), slot);
        }

        Array.Sort(keys);

        for(int position = 0; position < itemCount; position++)
        {
            dominanceOrder[itemStart + position] = keys[position].Slot;
        }
    }

    /// <summary>The fixed axis binding: 0 MinX, 1 MaxX, 2 MinY, 3 MaxY.</summary>
    /// <param name="box">The box to read.</param>
    /// <param name="axis">The axis to read.</param>
    /// <returns>The box's ordinate on the axis.</returns>
    private static double AxisOrdinate(BoundingBox box, int axis)
    {
        return axis switch
        {
            0 => box.MinX,
            1 => box.MaxX,
            2 => box.MinY,
            _ => box.MaxY,
        };
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

    /// <summary>Deterministic pseudo-random boxes: origin ordinates in [0, ordinateExtent), dimensions in [0, maximumDimension).</summary>
    /// <param name="startState">The mixing start value that makes the fixture reproducible.</param>
    /// <param name="size">The number of boxes.</param>
    /// <param name="ordinateExtent">The exclusive upper bound of each origin ordinate.</param>
    /// <param name="maximumDimension">The exclusive upper bound of each box dimension.</param>
    /// <returns>The fixture items in registration order.</returns>
    private static BoundingBox[] RandomBoxes(ulong startState, int size, double ordinateExtent, double maximumDimension)
    {
        ulong state = startState;
        var items = new BoundingBox[size];

        for(int index = 0; index < size; index++)
        {
            double x = DeterministicBitMixer.NextUnitDouble(ref state) * ordinateExtent;
            double y = DeterministicBitMixer.NextUnitDouble(ref state) * ordinateExtent;
            double width = DeterministicBitMixer.NextUnitDouble(ref state) * maximumDimension;
            double height = DeterministicBitMixer.NextUnitDouble(ref state) * maximumDimension;
            items[index] = new BoundingBox(x, y, x + width, y + height);
        }

        return items;
    }

    /// <summary>
    /// Deterministic pseudo-random boxes whose ordinates are drawn from a
    /// small integer grid, so exact coordinate ties are pervasive and the
    /// slot tie-break decides nearly every dominance comparison.
    /// </summary>
    /// <param name="startState">The mixing start value that makes the fixture reproducible.</param>
    /// <param name="size">The number of boxes.</param>
    /// <returns>The fixture items in registration order.</returns>
    private static BoundingBox[] SmallIntegerGridBoxes(ulong startState, int size)
    {
        ulong state = startState;
        var items = new BoundingBox[size];

        for(int index = 0; index < size; index++)
        {
            double x = DeterministicBitMixer.NextBelow(ref state, 8);
            double y = DeterministicBitMixer.NextBelow(ref state, 8);
            double width = DeterministicBitMixer.NextBelow(ref state, 4);
            double height = DeterministicBitMixer.NextBelow(ref state, 4);
            items[index] = new BoundingBox(x, y, x + width, y + height);
        }

        return items;
    }

    /// <summary>One pending subtree of the reference dominance build's explicit work stack, mirroring the production materialization pass's work item shape.</summary>
    /// <param name="ItemStart">The first position of the dominance-order range this subtree owns.</param>
    /// <param name="ItemCount">The number of positions in the range.</param>
    /// <param name="Depth">The subtree's depth from the root; the split axis is the depth modulo four.</param>
    /// <param name="ParentNode">The parent node whose child link this subtree fills once created; −1 for the root.</param>
    /// <param name="IsLeftChild">Whether this subtree fills the parent's left link.</param>
    private readonly record struct ReferenceBuildWorkItem(int ItemStart, int ItemCount, int Depth, int ParentNode, bool IsLeftChild);

    /// <summary>The reference build's per-node sort key: the axis ordinate first, the slot second — the same composite the production build's median split closes every tie with.</summary>
    /// <param name="Value">The entry's ordinate on the sort axis.</param>
    /// <param name="Slot">The entry's unique slot, closing every ordinate tie.</param>
    private readonly record struct ReferenceSortKey(double Value, int Slot): IComparable<ReferenceSortKey>
    {
        /// <summary>Ordinate first via <see cref="double.CompareTo(double)"/>, mirroring the production key's total-order semantics; the unique slot closes every tie.</summary>
        /// <param name="other">The key to compare against.</param>
        /// <returns>A negative value, zero, or a positive value as this key sorts before, with, or after <paramref name="other"/>.</returns>
        public int CompareTo(ReferenceSortKey other)
        {
            int byValue = Value.CompareTo(other.Value);

            return byValue != 0 ? byValue : Slot.CompareTo(other.Slot);
        }
    }

    /// <summary>The reference build's full node table plus the final dominance order, in the same column shape the production accessors expose.</summary>
    /// <param name="Left">Per node, the left child; −1 for a leaf.</param>
    /// <param name="Right">Per internal node, the right child.</param>
    /// <param name="Axis">Per internal node, the split axis.</param>
    /// <param name="Split">Per internal node, the split value.</param>
    /// <param name="ItemStart">Per node, the first position of its dominance-order range.</param>
    /// <param name="ItemSpan">Per node, the length of its dominance-order range.</param>
    /// <param name="DominanceOrder">The final dominance order: position to item slot.</param>
    /// <param name="NodeCount">The node count of the reference build.</param>
    /// <param name="Root">The root node's index; −1 for an empty build.</param>
    private readonly record struct ReferenceDominanceTree(
        int[] Left,
        int[] Right,
        int[] Axis,
        double[] Split,
        int[] ItemStart,
        int[] ItemSpan,
        int[] DominanceOrder,
        int NodeCount,
        int Root);
}
