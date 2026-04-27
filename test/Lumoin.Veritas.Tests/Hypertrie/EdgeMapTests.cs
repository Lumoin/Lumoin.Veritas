using Lumoin.Veritas.Core.Hypertrie.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class EdgeMapTests
{
    public TestContext TestContext { get; set; } = null!;

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
        HypertrieNode child = new(2);

        EdgeMap.InsertOrReplace(ref map, 42L, child);

        Assert.AreEqual(EdgeMapKind.Inline, map.Kind);
        Assert.AreEqual(1, EdgeMap.Count(in map));
        Assert.AreEqual(42L, map.InlineKey);
        Assert.AreSame(child, map.InlineChild);
    }

    [TestMethod]
    public void RepeatedInsertOnInlineReplacesChild()
    {
        EdgeMap map = default;
        HypertrieNode first = new(2);
        HypertrieNode second = new(2);

        EdgeMap.InsertOrReplace(ref map, 42L, first);
        EdgeMap.InsertOrReplace(ref map, 42L, second);

        Assert.AreEqual(EdgeMapKind.Inline, map.Kind);
        Assert.AreEqual(1, EdgeMap.Count(in map));
        Assert.AreSame(second, map.InlineChild);
    }

    [TestMethod]
    public void SecondDistinctInsertPromotesInlineToSortedArray()
    {
        EdgeMap map = default;
        HypertrieNode first = new(2);
        HypertrieNode second = new(2);

        EdgeMap.InsertOrReplace(ref map, 10L, first);
        EdgeMap.InsertOrReplace(ref map, 5L, second);

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        Assert.AreEqual(2, EdgeMap.Count(in map));
        //Inline fields are zeroed on promotion.
        Assert.AreEqual(0L, map.InlineKey);
        Assert.IsNull(map.InlineChild);
    }

    [TestMethod]
    public void SortedArrayKeepsKeysInAscendingOrder()
    {
        EdgeMap map = default;
        long[] inputs = [50, 10, 30, 20, 40, 5, 60, 25, 15, 35];

        foreach(long key in inputs)
        {
            EdgeMap.InsertOrReplace(ref map, key, null);
        }

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        Assert.AreEqual(inputs.Length, EdgeMap.Count(in map));

        long[] enumerated = [.. EdgeMap.Enumerate(map).Select(kvp => kvp.Key)];
        long[] expected = [.. inputs.OrderBy(x => x)];
        CollectionAssert.AreEqual(expected, enumerated);
    }

    [TestMethod]
    public void SortedArrayBinarySearchFindsAllInsertedKeys()
    {
        EdgeMap map = default;
        long[] inputs = [50, 10, 30, 20, 40, 5, 60, 25, 15, 35];

        foreach(long key in inputs)
        {
            EdgeMap.InsertOrReplace(ref map, key, null);
        }

        foreach(long key in inputs)
        {
            Assert.IsTrue(
                EdgeMap.TryGetChild(in map, key, out _),
                $"Expected key {key} to be present in the SortedArray map.");
        }
    }

    [TestMethod]
    public void SortedArrayReplaceKeepsCountStable()
    {
        EdgeMap map = default;
        EdgeMap.InsertOrReplace(ref map, 10L, new HypertrieNode(2));
        EdgeMap.InsertOrReplace(ref map, 20L, new HypertrieNode(2));
        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);

        HypertrieNode replacement = new(2);
        EdgeMap.InsertOrReplace(ref map, 10L, replacement);

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        Assert.AreEqual(2, EdgeMap.Count(in map));
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 10L, out HypertrieNode? actual));
        Assert.AreSame(replacement, actual);
    }

    [TestMethod]
    public void SortedArrayGrowsBeyondInitialCapacity()
    {
        EdgeMap map = default;

        //SortedArrayInitialCapacity is 4 internally; pushing 8 entries forces at least one growth.
        for(long key = 0; key < 8; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key, null);
        }

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        Assert.AreEqual(8, EdgeMap.Count(in map));
        Assert.IsLessThanOrEqualTo(map.SortedKeys!.Length, map.SortedCount);
    }

    [TestMethod]
    public void SortedArrayScalesToHighCardinality()
    {
        //Verify the SortedArray representation handles cardinalities
        //well past where an earlier design would have promoted to a
        //hash-table representation. SortedArray scales without
        //further promotion; the parallel arrays simply grow by
        //doubling.
        const int total = 4096;
        EdgeMap map = default;
        for(long key = 0; key < total; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key, null);
        }

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        Assert.AreEqual(total, EdgeMap.Count(in map));
    }

    [TestMethod]
    public void HighCardinalitySortedArrayPreservesAllKeys()
    {
        const int total = 4096;
        EdgeMap map = default;
        for(long key = 0; key < total; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key, null);
        }

        for(long key = 0; key < total; key++)
        {
            Assert.IsTrue(
                EdgeMap.TryGetChild(in map, key, out _),
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
        for(long key = total - 1; key >= 0; key--)
        {
            EdgeMap.InsertOrReplace(ref map, key, null);
        }

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        long previousKey = long.MinValue;
        foreach(KeyValuePair<long, HypertrieNode?> entry in EdgeMap.Enumerate(map))
        {
            Assert.IsLessThan(entry.Key, previousKey, $"SortedArray order violated at key {entry.Key}; previous key was {previousKey}.");
            previousKey = entry.Key;
        }
    }

    [TestMethod]
    public void TryGetChildOnEmptyMapReturnsFalse()
    {
        EdgeMap map = default;

        bool found = EdgeMap.TryGetChild(in map, 0L, out HypertrieNode? child);

        Assert.IsFalse(found);
        Assert.IsNull(child);
    }

    [TestMethod]
    public void TryGetChildOnInlineMapDistinguishesPresentFromAbsent()
    {
        EdgeMap map = default;
        HypertrieNode node = new(2);
        EdgeMap.InsertOrReplace(ref map, 7L, node);

        Assert.IsTrue(EdgeMap.TryGetChild(in map, 7L, out HypertrieNode? hit));
        Assert.AreSame(node, hit);
        Assert.IsFalse(EdgeMap.TryGetChild(in map, 8L, out HypertrieNode? miss));
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

        EdgeMap.InsertOrReplace(ref map, 100L, null);
        EdgeMap.InsertOrReplace(ref map, 200L, null);
        EdgeMap.InsertOrReplace(ref map, 150L, null);

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 100L, out HypertrieNode? c1));
        Assert.IsNull(c1);
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 150L, out HypertrieNode? c2));
        Assert.IsNull(c2);
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 200L, out HypertrieNode? c3));
        Assert.IsNull(c3);
    }

    [TestMethod]
    public void EqualMapsCompareEqualOnValueAndReferenceFields()
    {
        EdgeMap map = default;
        HypertrieNode child = new(2);
        EdgeMap.InsertOrReplace(ref map, 1L, child);
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
        EdgeMap.InsertOrReplace(ref left, 1L, null);
        EdgeMap.InsertOrReplace(ref left, 2L, null);

        EdgeMap right = default;
        EdgeMap.InsertOrReplace(ref right, 1L, null);
        EdgeMap.InsertOrReplace(ref right, 2L, null);

        Assert.IsFalse(left.Equals(right));
        Assert.IsTrue(left != right);
    }
}
