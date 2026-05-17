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

        EdgeMap.InsertOrReplace(ref map, 42U, child);

        Assert.AreEqual(EdgeMapKind.Inline, map.Kind);
        Assert.AreEqual(1, EdgeMap.Count(in map));
        Assert.AreEqual(42U, map.InlineKey);
        Assert.AreSame(child, map.InlineChild);
    }

    [TestMethod]
    public void RepeatedInsertOnInlineReplacesChild()
    {
        EdgeMap map = default;
        HypertrieNode first = new(2);
        HypertrieNode second = new(2);

        EdgeMap.InsertOrReplace(ref map, 42U, first);
        EdgeMap.InsertOrReplace(ref map, 42U, second);

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

        EdgeMap.InsertOrReplace(ref map, 10U, first);
        EdgeMap.InsertOrReplace(ref map, 5U, second);

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        Assert.AreEqual(2, EdgeMap.Count(in map));
        //Inline fields are zeroed on promotion.
        Assert.AreEqual(0U, map.InlineKey);
        Assert.IsNull(map.InlineChild);
    }

    [TestMethod]
    public void SortedArrayKeepsKeysInAscendingOrder()
    {
        EdgeMap map = default;
        uint[] inputs = [50, 10, 30, 20, 40, 5, 60, 25, 15, 35];

        foreach(uint key in inputs)
        {
            EdgeMap.InsertOrReplace(ref map, key, null);
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
        uint[] inputs = [50, 10, 30, 20, 40, 5, 60, 25, 15, 35];

        foreach(uint key in inputs)
        {
            EdgeMap.InsertOrReplace(ref map, key, null);
        }

        foreach(uint key in inputs)
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
        EdgeMap.InsertOrReplace(ref map, 10U, new HypertrieNode(2));
        EdgeMap.InsertOrReplace(ref map, 20U, new HypertrieNode(2));
        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);

        HypertrieNode replacement = new(2);
        EdgeMap.InsertOrReplace(ref map, 10U, replacement);

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        Assert.AreEqual(2, EdgeMap.Count(in map));
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 10U, out HypertrieNode? actual));
        Assert.AreSame(replacement, actual);
    }

    [TestMethod]
    public void SortedArrayGrowsBeyondInitialCapacity()
    {
        EdgeMap map = default;

        //SortedArrayInitialCapacity is 4 internally; pushing 8 entries forces at least one growth.
        for(uint key = 0; key < 8; key++)
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
        for(uint key = 0; key < total; key++)
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
        for(uint key = 0; key < total; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key, null);
        }

        for(uint key = 0; key < total; key++)
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
        for(uint key = total; key > 0; key--)
        {
            EdgeMap.InsertOrReplace(ref map, key - 1, null);
        }

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        bool hasPrevious = false;
        uint previousKey = 0;
        foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(map))
        {
            if(hasPrevious)
            {
                Assert.IsLessThan(entry.Key, previousKey, $"SortedArray order violated at key {entry.Key}; previous key was {previousKey}.");
            }

            hasPrevious = true;
            previousKey = entry.Key;
        }
    }

    [TestMethod]
    public void TryGetChildOnEmptyMapReturnsFalse()
    {
        EdgeMap map = default;

        bool found = EdgeMap.TryGetChild(in map, 0U, out HypertrieNode? child);

        Assert.IsFalse(found);
        Assert.IsNull(child);
    }

    [TestMethod]
    public void TryGetChildOnInlineMapDistinguishesPresentFromAbsent()
    {
        EdgeMap map = default;
        HypertrieNode node = new(2);
        EdgeMap.InsertOrReplace(ref map, 7U, node);

        Assert.IsTrue(EdgeMap.TryGetChild(in map, 7U, out HypertrieNode? hit));
        Assert.AreSame(node, hit);
        Assert.IsFalse(EdgeMap.TryGetChild(in map, 8U, out HypertrieNode? miss));
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

        EdgeMap.InsertOrReplace(ref map, 100U, null);
        EdgeMap.InsertOrReplace(ref map, 200U, null);
        EdgeMap.InsertOrReplace(ref map, 150U, null);

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 100U, out HypertrieNode? c1));
        Assert.IsNull(c1);
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 150U, out HypertrieNode? c2));
        Assert.IsNull(c2);
        Assert.IsTrue(EdgeMap.TryGetChild(in map, 200U, out HypertrieNode? c3));
        Assert.IsNull(c3);
    }

    [TestMethod]
    public void EqualMapsCompareEqualOnValueAndReferenceFields()
    {
        EdgeMap map = default;
        HypertrieNode child = new(2);
        EdgeMap.InsertOrReplace(ref map, 1U, child);
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
        EdgeMap.InsertOrReplace(ref left, 1U, null);
        EdgeMap.InsertOrReplace(ref left, 2U, null);

        EdgeMap right = default;
        EdgeMap.InsertOrReplace(ref right, 1U, null);
        EdgeMap.InsertOrReplace(ref right, 2U, null);

        Assert.IsFalse(left.Equals(right));
        Assert.IsTrue(left != right);
    }
}
