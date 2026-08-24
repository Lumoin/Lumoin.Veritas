using Lumoin.Veritas.Core.Hypertrie.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class EdgeMapTests
{
    public TestContext TestContext { get; set; } = null!;

    //A non-None stub child handle for tests that exercise EdgeMap
    //mechanics without caring about resolvability via a real store.
    private static NodeHandle StubChild { get; } = NodeHandle.FromEncoded(1);

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
        Assert.AreEqual(StubChild, EdgeMap.InlineChildrenSpan(in map)[0]);
    }

    [TestMethod]
    public void RepeatedInsertOnInlineReplacesChild()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        NodeHandle first = NodeHandle.FromEncoded(1);
        NodeHandle second = NodeHandle.FromEncoded(2);

        EdgeMap.InsertOrReplace(ref map, 42U, first, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 42U, second, pools, InlineKeyLookups.Scalar);

        Assert.AreEqual(EdgeMapKind.Inline, map.Kind);
        Assert.AreEqual(1, EdgeMap.Count(in map));
        Assert.AreEqual(second, EdgeMap.InlineChildrenSpan(in map)[0]);
    }

    [TestMethod]
    public void SecondDistinctInsertKeepsMapInline()
    {
        //Inline tier now holds up to InlineCapacity (8) entries.
        //A second distinct insert no longer promotes to SortedArray.
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        NodeHandle first = NodeHandle.FromEncoded(1);
        NodeHandle second = NodeHandle.FromEncoded(2);

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
        //Push past the 8-entry Inline boundary; absent children make
        //this the keys-only leaf shape.
        uint[] inputs = [50, 10, 30, 20, 40, 5, 60, 25, 15, 35];

        foreach(uint key in inputs)
        {
            EdgeMap.InsertOrReplace(ref map, key, NodeHandle.None, pools, InlineKeyLookups.Scalar);
        }

        Assert.AreEqual(EdgeMapKind.SortedKeysOnly, map.Kind);
        Assert.AreEqual(inputs.Length, EdgeMap.Count(in map));

        uint[] enumerated = [.. EdgeMap.Enumerate(map).Select(kvp => kvp.Key)];
        uint[] expected = [.. inputs.OrderBy(x => x)];
        Assert.AreSequenceEqual(expected, enumerated);
    }

    [TestMethod]
    public void SortedArrayBinarySearchFindsAllInsertedKeys()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        uint[] inputs = [50, 10, 30, 20, 40, 5, 60, 25, 15, 35];

        foreach(uint key in inputs)
        {
            EdgeMap.InsertOrReplace(ref map, key, NodeHandle.None, pools, InlineKeyLookups.Scalar);
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

        //Push past the 8-entry Inline boundary so the map is SortedArray.
        for(uint k = 1; k <= 9; k++)
        {
            EdgeMap.InsertOrReplace(ref map, k, NodeHandle.FromEncoded(k), pools, InlineKeyLookups.Scalar);
        }
        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        int countBefore = EdgeMap.Count(in map);

        NodeHandle replacement = NodeHandle.FromEncoded(99);
        EdgeMap.InsertOrReplace(ref map, 5U, replacement, pools, InlineKeyLookups.Scalar);

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        Assert.AreEqual(countBefore, EdgeMap.Count(in map));
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 5U, InlineKeyLookups.Scalar, out NodeHandle actual));
        Assert.AreEqual(replacement, actual);
    }

    [TestMethod]
    public void SortedArrayGrowsBeyondInitialCapacity()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();

        //Push past the 8-entry Inline boundary, then far enough to
        //force at least one keys-only growth.
        for(uint key = 0; key < 32; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key, NodeHandle.None, pools, InlineKeyLookups.Scalar);
        }

        Assert.AreEqual(EdgeMapKind.SortedKeysOnly, map.Kind);
        Assert.AreEqual(32, EdgeMap.Count(in map));
        Assert.AreEqual(32, map.SortedCount);
    }

    [TestMethod]
    public void SortedArrayScalesToHighCardinality()
    {
        //Verify the sorted representation handles cardinalities well
        //past the inline boundary without further promotion; the
        //backing array simply grows by doubling. Absent children
        //keep this in the keys-only leaf shape.
        const int total = 4096;
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        for(uint key = 0; key < total; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key, NodeHandle.None, pools, InlineKeyLookups.Scalar);
        }

        Assert.AreEqual(EdgeMapKind.SortedKeysOnly, map.Kind);
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
            EdgeMap.InsertOrReplace(ref map, key, NodeHandle.None, pools, InlineKeyLookups.Scalar);
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
        //Inserting in reverse order is a stronger ordering test than
        //the small-input version above because every insert hits
        //insertAt = 0 and shifts the entire current contents up.
        const int total = 1024;
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        for(uint key = total; key > 0; key--)
        {
            EdgeMap.InsertOrReplace(ref map, key - 1, NodeHandle.None, pools, InlineKeyLookups.Scalar);
        }

        Assert.AreEqual(EdgeMapKind.SortedKeysOnly, map.Kind);
        bool hasPrevious = false;
        uint previousKey = 0;
        foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(map))
        {
            if(hasPrevious)
            {
                Assert.IsGreaterThan(previousKey, entry.Key, $"Sorted order violated at key {entry.Key}; previous key was {previousKey}.");
            }

            hasPrevious = true;
            previousKey = entry.Key;
        }
    }

    [TestMethod]
    public void TryGetChildOnEmptyMapReturnsFalse()
    {
        EdgeMap map = default;

        bool found = EdgeMap.TryGetChild(in map, 0U, InlineKeyLookups.Scalar, out NodeHandle child);

        Assert.IsFalse(found);
        Assert.IsTrue(child.IsNone);
    }

    [TestMethod]
    public void TryGetChildOnInlineMapDistinguishesPresentFromAbsent()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        NodeHandle node = NodeHandle.FromEncoded(42);
        EdgeMap.InsertOrReplace(ref map, 7U, node, pools, InlineKeyLookups.Scalar);

        Assert.IsTrue(EdgeMap.TryGetChild(in map, 7U, InlineKeyLookups.Scalar, out NodeHandle hit));
        Assert.AreEqual(node, hit);
        Assert.IsFalse(EdgeMap.TryGetChild(in map, 8U, InlineKeyLookups.Scalar, out NodeHandle miss));
        Assert.IsTrue(miss.IsNone);
    }

    [TestMethod]
    public void EnumerateOnEmptyMapYieldsNothing()
    {
        EdgeMap map = default;

        Assert.IsEmpty(EdgeMap.Enumerate(map));
    }

    [TestMethod]
    public void DepthOneLeavesStoreNoneChildAsPresenceMarker()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();

        EdgeMap.InsertOrReplace(ref map, 100U, NodeHandle.None, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 200U, NodeHandle.None, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 150U, NodeHandle.None, pools, InlineKeyLookups.Scalar);

        //Three entries still fit in Inline (capacity 8).
        Assert.AreEqual(EdgeMapKind.Inline, map.Kind);
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 100U, InlineKeyLookups.Scalar, out NodeHandle c1));
        Assert.IsTrue(c1.IsNone);
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 150U, InlineKeyLookups.Scalar, out NodeHandle c2));
        Assert.IsTrue(c2.IsNone);
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 200U, InlineKeyLookups.Scalar, out NodeHandle c3));
        Assert.IsTrue(c3.IsNone);
    }

    [TestMethod]
    public void EqualMapsCompareEqualOnValueAndReferenceFields()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        NodeHandle child = NodeHandle.FromEncoded(7);
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

        //Push past the 8-entry Inline boundary so both maps use a
        //sorted backing array (keys-only — children are absent).
        for(uint k = 1; k <= 9; k++)
        {
            EdgeMap.InsertOrReplace(ref left, k, NodeHandle.None, leftPools, InlineKeyLookups.Scalar);
            EdgeMap.InsertOrReplace(ref right, k, NodeHandle.None, rightPools, InlineKeyLookups.Scalar);
        }
        Assert.AreEqual(EdgeMapKind.SortedKeysOnly, left.Kind);
        Assert.AreEqual(EdgeMapKind.SortedKeysOnly, right.Kind);

        Assert.IsFalse(left.Equals(right));
        Assert.IsTrue(left != right);
    }

    [TestMethod]
    public void InlineTierGrowsToEightThenPromotesToSortedKeysOnly()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();

        for(uint key = 1; key <= 8; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key, NodeHandle.None, pools, InlineKeyLookups.Scalar);
            Assert.AreEqual(EdgeMapKind.Inline, map.Kind);
        }

        EdgeMap.InsertOrReplace(ref map, 9, NodeHandle.None, pools, InlineKeyLookups.Scalar);
        Assert.AreEqual(EdgeMapKind.SortedKeysOnly, map.Kind);
        Assert.AreEqual(9, EdgeMap.Count(in map));

        //All 9 keys present in ascending order.
        ReadOnlySpan<uint> keys = EdgeMap.SortedKeysSpan(in map);
        for(int i = 0; i < 9; i++)
        {
            Assert.AreEqual((uint)(i + 1), keys[i]);
        }
    }

    [TestMethod]
    public void RealChildArrivingAtKeysOnlyMapUpgradesToSortedArray()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();

        //Promote into the keys-only shape first.
        for(uint key = 1; key <= 9; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key, NodeHandle.None, pools, InlineKeyLookups.Scalar);
        }

        Assert.AreEqual(EdgeMapKind.SortedKeysOnly, map.Kind);

        //A real child forces the parallel-array form; prior keys
        //keep their absent children and the new entry carries its
        //handle.
        NodeHandle child = NodeHandle.FromEncoded(77);
        EdgeMap.InsertOrReplace(ref map, 10U, child, pools, InlineKeyLookups.Scalar);

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        Assert.AreEqual(10, EdgeMap.Count(in map));
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 10U, InlineKeyLookups.Scalar, out NodeHandle stored));
        Assert.AreEqual(child, stored);
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 5U, InlineKeyLookups.Scalar, out NodeHandle absent));
        Assert.IsTrue(absent.IsNone);
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

        EdgeMap.InsertOrReplace(ref map, 42u, NodeHandle.None, pools, RecordingLookup);
        int before = invocationCount;

        EdgeMap.TryGetChild(in map, 42u, RecordingLookup, out _);
        Assert.IsGreaterThan(before, invocationCount);
    }
}
