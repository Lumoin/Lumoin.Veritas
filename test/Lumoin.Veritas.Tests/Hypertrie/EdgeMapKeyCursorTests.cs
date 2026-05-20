using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class EdgeMapKeyCursorTests
{
    public TestContext TestContext { get; set; } = null!;

    //Wraps an EdgeMap in a depth-1 HypertrieNode and constructs a
    //cursor over its first edge map. The cursor's constructor takes
    //(HypertrieNode, edgeMapIndex) and reads through EdgeMap
    //accessors on the live node; the helper isolates that bit of
    //boilerplate so every test reads cleanly.
    private static EdgeMapKeyCursor CursorOver(in EdgeMap map)
    {
        HypertrieNode node = new(1);
        node.Edge0 = map;
        return new EdgeMapKeyCursor(node, 0);
    }

    [TestMethod]
    public void EmptyMapCursorIsImmediatelyAtEnd()
    {
        EdgeMap map = default;

        EdgeMapKeyCursor cursor = CursorOver(in map);

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void InlineMapCursorVisitsTheSingleKeyThenEnds()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        EdgeMap.InsertOrReplace(ref map, 42U, child: null, pools, InlineKeyLookups.Scalar);

        EdgeMapKeyCursor cursor = CursorOver(in map);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(42U, cursor.CurrentKey);

        cursor.MoveNext();

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void SortedArrayCursorVisitsKeysInAscendingOrder()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        //Push past the 8-entry Inline boundary to materialise SortedArray.
        uint[] inputs = [50, 10, 30, 20, 40, 5, 60, 25, 15];

        foreach(uint key in inputs)
        {
            EdgeMap.InsertOrReplace(ref map, key, child: null, pools, InlineKeyLookups.Scalar);
        }

        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);

        EdgeMapKeyCursor cursor = CursorOver(in map);
        uint[] visited = new uint[inputs.Length];

        for(int i = 0; i < inputs.Length; i++)
        {
            Assert.IsFalse(cursor.AtEnd);

            visited[i] = cursor.CurrentKey;

            cursor.MoveNext();
        }

        Assert.IsTrue(cursor.AtEnd);

        uint[] expected = [.. inputs.OrderBy(x => x)];
        CollectionAssert.AreEqual(expected, visited);
    }

    [TestMethod]
    public void CurrentKeyThrowsWhenAtEnd()
    {
        EdgeMap map = default;
        EdgeMapKeyCursor cursor = CursorOver(in map);

        Assert.Throws<InvalidOperationException>(() => _ = cursor.CurrentKey);
    }

    [TestMethod]
    public void CurrentChildThrowsWhenAtEnd()
    {
        EdgeMap map = default;
        EdgeMapKeyCursor cursor = CursorOver(in map);

        Assert.Throws<InvalidOperationException>(() => _ = cursor.CurrentChild);
    }

    [TestMethod]
    public void MoveNextOnEmptyCursorIsNoOp()
    {
        EdgeMap map = default;
        EdgeMapKeyCursor cursor = CursorOver(in map);

        cursor.MoveNext();
        cursor.MoveNext();

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void SeekToFindsExactKey()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();

        for(uint key = 0; key < 10; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key * 10, child: null, pools, InlineKeyLookups.Scalar);
        }

        EdgeMapKeyCursor cursor = CursorOver(in map);
        cursor.SeekTo(50U);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(50U, cursor.CurrentKey);
    }

    [TestMethod]
    public void SeekToFindsFirstKeyAtOrAfterTarget()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        uint[] keys = [10, 20, 30, 40, 50];

        foreach(uint key in keys)
        {
            EdgeMap.InsertOrReplace(ref map, key, child: null, pools, InlineKeyLookups.Scalar);
        }

        EdgeMapKeyCursor cursor = CursorOver(in map);
        cursor.SeekTo(25U);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(30U, cursor.CurrentKey);
    }

    [TestMethod]
    public void SeekToPastLastKeyReachesEnd()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        EdgeMap.InsertOrReplace(ref map, 10U, child: null, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 20U, child: null, pools, InlineKeyLookups.Scalar);

        EdgeMapKeyCursor cursor = CursorOver(in map);
        cursor.SeekTo(30U);

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void SeekToTargetLessThanCurrentDoesNotMoveBackwards()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        EdgeMap.InsertOrReplace(ref map, 10U, child: null, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 20U, child: null, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 30U, child: null, pools, InlineKeyLookups.Scalar);

        EdgeMapKeyCursor cursor = CursorOver(in map);
        cursor.MoveNext();
        cursor.MoveNext();
        Assert.AreEqual(30U, cursor.CurrentKey);

        cursor.SeekTo(5U);

        Assert.AreEqual(30U, cursor.CurrentKey);
    }

    [TestMethod]
    public void SeekToOnEmptyCursorIsNoOp()
    {
        EdgeMap map = default;
        EdgeMapKeyCursor cursor = CursorOver(in map);

        cursor.SeekTo(42U);

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void SeekToOnInlineSingleEntryMapHandlesMatchAndMiss()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        EdgeMap.InsertOrReplace(ref map, 7U, child: null, pools, InlineKeyLookups.Scalar);

        EdgeMapKeyCursor matchCursor = CursorOver(in map);
        matchCursor.SeekTo(7U);
        Assert.IsFalse(matchCursor.AtEnd);
        Assert.AreEqual(7U, matchCursor.CurrentKey);
    }

    [TestMethod]
    public void SeekToOnInlineSingleEntryMapMissBeforeReachesEnd()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        EdgeMap.InsertOrReplace(ref map, 7U, child: null, pools, InlineKeyLookups.Scalar);

        EdgeMapKeyCursor cursor = CursorOver(in map);
        cursor.SeekTo(10U);

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void SeekToOnInlineSingleEntryMapTargetEqualToCurrentStays()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        EdgeMap.InsertOrReplace(ref map, 7U, child: null, pools, InlineKeyLookups.Scalar);

        EdgeMapKeyCursor cursor = CursorOver(in map);
        cursor.SeekTo(7U);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(7U, cursor.CurrentKey);
    }

    [TestMethod]
    public void SortedArraySeekToReachesEndIfPastLast()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        for(uint key = 1; key <= 9; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key * 2, child: null, pools, InlineKeyLookups.Scalar);
        }
        Assert.AreEqual(EdgeMapKind.SortedArray, map.Kind);

        EdgeMapKeyCursor cursor = CursorOver(in map);
        cursor.SeekTo(100U);

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void CurrentChildIsNullForDepth1LeafEntries()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        EdgeMap.InsertOrReplace(ref map, 1U, child: null, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 2U, child: null, pools, InlineKeyLookups.Scalar);

        EdgeMapKeyCursor cursor = CursorOver(in map);

        Assert.IsNull(cursor.CurrentChild);

        cursor.MoveNext();

        Assert.IsNull(cursor.CurrentChild);
    }

    [TestMethod]
    public void CurrentChildReturnsReferenceForInnerNodeEntries()
    {
        HypertrieNode child1 = new(1);
        HypertrieNode child2 = new(1);
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        EdgeMap.InsertOrReplace(ref map, 1U, child1, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 2U, child2, pools, InlineKeyLookups.Scalar);

        EdgeMapKeyCursor cursor = CursorOver(in map);

        Assert.AreSame(child1, cursor.CurrentChild);

        cursor.MoveNext();

        Assert.AreSame(child2, cursor.CurrentChild);
    }
}
