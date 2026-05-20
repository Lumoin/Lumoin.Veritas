using Lumoin.Veritas.Core.Hypertrie.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class EdgeMapTests
{
    public TestContext TestContext { get; set; } = null!;

    //A non-null stub child for tests that exercise EdgeMap mechanics
    //without caring about content equality of the child itself.
    private static HypertrieNode StubChild { get; } = new(1);

    [TestMethod]
    public void EmptyMapHasZeroCount()
    {
        EdgeMap map = default;

        Assert.AreEqual(EdgeMapKind.Empty, map.Kind);
        Assert.AreEqual(0, EdgeMap.Count(in map));
    }

    [TestMethod]
    public void FirstInsertPromotesEmptyToInline()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();

        EdgeMap.InsertOrReplace(ref map, 42U, StubChild, pools, InlineKeyLookups.Scalar);

        Assert.AreEqual(EdgeMapKind.Inline, map.Kind);
        Assert.AreEqual(1, EdgeMap.Count(in map));
        Assert.AreEqual(42U, EdgeMap.InlineKeysSpan(in map)[0]);
        Assert.AreSame(StubChild, EdgeMap.InlineChildrenSpan(in map)[0]);
    }

    [TestMethod]
    public void RepeatedInsertOnInlineReplacesChild()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        HypertrieNode first = new(1);
        HypertrieNode second = new(1);

        EdgeMap.InsertOrReplace(ref map, 42U, first, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 42U, second, pools, InlineKeyLookups.Scalar);

        Assert.AreEqual(EdgeMapKind.Inline, map.Kind);
        Assert.AreEqual(1, EdgeMap.Count(in map));
        Assert.AreSame(second, EdgeMap.InlineChildrenSpan(in map)[0]);
    }

    [TestMethod]
    public void SecondDistinctInsertKeepsMapInline()
    {
        //Inline tier now holds up to InlineCapacity (8) entries.
        //A second distinct insert no longer promotes to SortedArray.
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        HypertrieNode first = new(1);
        HypertrieNode second = new(1);

        EdgeMap.InsertOrReplace(ref map, 10U, first, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 5U, second, pools, InlineKeyLookups.Scalar);

        Assert.AreEqual(EdgeMapKind.Inline, map.Kind);
        Assert.AreEqual(2, EdgeMap.Count(in map));

        //Inline tier maintains ascending key order — 5 then 10.
        ReadOnlySpan<uint> keys = EdgeMap.InlineKeysSpan(in map);
        Assert.AreEqual(5U, keys[0]);
        Assert.AreEqual(10U, keys[1]);
    }

    [TestMethod]
    public void SortedArrayKeepsKeysInAscendingOrder()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        //Push past the 8-entry Inline boundary so the map is SortedArray.
        uint[] inputs = [50, 10, 30, 20, 40, 5, 60, 25, 15, 35];

        foreach(uint key in inputs)
        {
            EdgeMap.InsertOrReplace(ref map, key, child: null, pools, InlineKeyLookups.Scalar);
        }

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        Assert.AreEqual(inputs.Length, EdgeMap.Count(in map));

        uint[] enumerated = [.. EdgeMap.Enumerate(map).Select(kvp => kvp.Key)];
        uint[] expected = [.. inputs.OrderBy(x => x)];
        CollectionAssert.AreEqual(expected, enumerated);
    }

    [TestMethod]
    public void SortedArrayBinarySearchFindsAllInsertedKeys()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        uint[] inputs = [50, 10, 30, 20, 40, 5, 60, 25, 15, 35];

        foreach(uint key in inputs)
        {
            EdgeMap.InsertOrReplace(ref map, key, child: null, pools, InlineKeyLookups.Scalar);
        }

        foreach(uint key in inputs)
        {
            Assert.IsTrue(
                EdgeMap.TryGetChild(in map, key, InlineKeyLookups.Scalar, out _),
                $"Expected key {key} to be present in the SortedArray map.");
        }
    }

    [TestMethod]
    public void SortedArrayReplaceKeepsCountStable()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        HypertrieNode[] childs = new HypertrieNode[10];
        for(int i = 0; i < 10; i++)
        {
            childs[i] = new HypertrieNode(1);
        }

        //Push past the 8-entry Inline boundary so the map is SortedArray.
        for(uint k = 1; k <= 9; k++)
        {
            EdgeMap.InsertOrReplace(ref map, k, childs[k], pools, InlineKeyLookups.Scalar);
        }
        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        int countBefore = EdgeMap.Count(in map);

        HypertrieNode replacement = new(1);
        EdgeMap.InsertOrReplace(ref map, 5U, replacement, pools, InlineKeyLookups.Scalar);

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        Assert.AreEqual(countBefore, EdgeMap.Count(in map));
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 5U, InlineKeyLookups.Scalar, out HypertrieNode? actual));
        Assert.AreSame(replacement, actual);
    }

    [TestMethod]
    public void SortedArrayGrowsBeyondInitialCapacity()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();

        //Push past the 8-entry Inline boundary, then far enough to
        //force at least one SortedArray growth.
        for(uint key = 0; key < 32; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key, child: null, pools, InlineKeyLookups.Scalar);
        }

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        Assert.AreEqual(32, EdgeMap.Count(in map));
        Assert.AreEqual(32, map.SortedCount);
    }

    [TestMethod]
    public void SortedArrayScalesToHighCardinality()
    {
        //Verify the SortedArray representation handles cardinalities
        //well past where an earlier design would have promoted to a
        //hash-table representation.
        const int total = 4096;
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        for(uint key = 0; key < total; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key, child: null, pools, InlineKeyLookups.Scalar);
        }

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        Assert.AreEqual(total, EdgeMap.Count(in map));
    }

    [TestMethod]
    public void HighCardinalitySortedArrayPreservesAllKeys()
    {
        const int total = 4096;
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        for(uint key = 0; key < total; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key, child: null, pools, InlineKeyLookups.Scalar);
        }

        for(uint key = 0; key < total; key++)
        {
            Assert.IsTrue(
                EdgeMap.TryGetChild(in map, key, InlineKeyLookups.Scalar, out _),
                $"Expected key {key} to be present at high cardinality.");
        }
    }

    [TestMethod]
    public void HighCardinalitySortedArrayMaintainsAscendingOrder()
    {
        const int total = 1024;
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        for(uint key = total; key > 0; key--)
        {
            EdgeMap.InsertOrReplace(ref map, key - 1, child: null, pools, InlineKeyLookups.Scalar);
        }

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        bool hasPrevious = false;
        uint previousKey = 0;
        foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(map))
        {
            if(hasPrevious)
            {
                Assert.IsGreaterThan(previousKey, entry.Key, $"SortedArray order violated at key {entry.Key}; previous key was {previousKey}.");
            }

            hasPrevious = true;
            previousKey = entry.Key;
        }
    }

    [TestMethod]
    public void TryGetChildOnEmptyMapReturnsFalse()
    {
        EdgeMap map = default;

        bool found = EdgeMap.TryGetChild(in map, 0U, InlineKeyLookups.Scalar, out HypertrieNode? child);

        Assert.IsFalse(found);
        Assert.IsNull(child);
    }

    [TestMethod]
    public void TryGetChildOnInlineMapDistinguishesPresentFromAbsent()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        HypertrieNode node = new(1);
        EdgeMap.InsertOrReplace(ref map, 7U, node, pools, InlineKeyLookups.Scalar);

        Assert.IsTrue(EdgeMap.TryGetChild(in map, 7U, InlineKeyLookups.Scalar, out HypertrieNode? hit));
        Assert.AreSame(node, hit);
        Assert.IsFalse(EdgeMap.TryGetChild(in map, 8U, InlineKeyLookups.Scalar, out HypertrieNode? miss));
        Assert.IsNull(miss);
    }

    [TestMethod]
    public void EnumerateOnEmptyMapYieldsNothing()
    {
        EdgeMap map = default;

        Assert.IsEmpty(EdgeMap.Enumerate(map));
    }

    [TestMethod]
    public void DepthOneLeavesStoreNullChildAsPresenceMarker()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();

        EdgeMap.InsertOrReplace(ref map, 100U, child: null, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 200U, child: null, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 150U, child: null, pools, InlineKeyLookups.Scalar);

        //Three entries still fit in Inline (capacity 8).
        Assert.AreEqual(EdgeMapKind.Inline, map.Kind);
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 100U, InlineKeyLookups.Scalar, out HypertrieNode? c1));
        Assert.IsNull(c1);
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 150U, InlineKeyLookups.Scalar, out HypertrieNode? c2));
        Assert.IsNull(c2);
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 200U, InlineKeyLookups.Scalar, out HypertrieNode? c3));
        Assert.IsNull(c3);
    }

    [TestMethod]
    public void EqualMapsCompareEqualOnValueAndReferenceFields()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        HypertrieNode child = new(1);
        EdgeMap.InsertOrReplace(ref map, 1U, child, pools, InlineKeyLookups.Scalar);
        EdgeMap copy = map;

        Assert.IsTrue(map.Equals(copy));
        Assert.IsTrue(map == copy);
        Assert.AreEqual(map.GetHashCode(), copy.GetHashCode());
    }

    [TestMethod]
    public void DistinctSortedArrayInstancesCompareUnequal()
    {
        //Two structurally-identical maps built independently use
        //distinct backing arrays and therefore compare unequal — by
        //design, equality is reference-based on the heap fields.
        EdgeMap left = default;
        EdgeMap right = default;
        BuildPools leftPools = BuildPools.CreateDefault();
        BuildPools rightPools = BuildPools.CreateDefault();

        //Push past the 8-entry Inline boundary so both maps are SortedArray.
        for(uint k = 1; k <= 9; k++)
        {
            EdgeMap.InsertOrReplace(ref left, k, child: null, leftPools, InlineKeyLookups.Scalar);
            EdgeMap.InsertOrReplace(ref right, k, child: null, rightPools, InlineKeyLookups.Scalar);
        }
        Assert.AreEqual(EdgeMapKind.SortedArray, left.Kind);
        Assert.AreEqual(EdgeMapKind.SortedArray, right.Kind);

        Assert.IsFalse(left.Equals(right));
        Assert.IsTrue(left != right);
    }

    [TestMethod]
    public void InlineTierGrowsToEightThenPromotesToSortedArray()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();

        for(uint key = 1; key <= 8; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key, child: null, pools, InlineKeyLookups.Scalar);
            Assert.AreEqual(EdgeMapKind.Inline, map.Kind);
        }

        EdgeMap.InsertOrReplace(ref map, 9, child: null, pools, InlineKeyLookups.Scalar);
        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        Assert.AreEqual(9, EdgeMap.Count(in map));

        //All 9 keys present in ascending order.
        ReadOnlySpan<uint> keys = EdgeMap.SortedKeysSpan(in map);
        for(int i = 0; i < 9; i++)
        {
            Assert.AreEqual((uint)(i + 1), keys[i]);
        }
    }

    [TestMethod]
    public void InlineKeyLookupDelegateIsInvokedForInlineTier()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        int invocationCount = 0;
        int RecordingLookup(ReadOnlySpan<uint> keys, uint needle)
        {
            invocationCount++;
            return InlineKeyLookups.Scalar(keys, needle);
        }

        EdgeMap.InsertOrReplace(ref map, 42u, child: null, pools, RecordingLookup);
        int before = invocationCount;

        EdgeMap.TryGetChild(in map, 42u, RecordingLookup, out _);
        Assert.IsGreaterThan(before, invocationCount);
    }
}
