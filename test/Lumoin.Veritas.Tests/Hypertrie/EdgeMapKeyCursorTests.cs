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
        HypertrieNode node = HypertrieNode.Create(1);
        node.EdgeMaps[0] = map;
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
        EdgeMap.InsertOrReplace(ref map, 42U, NodeHandle.None, pools, InlineKeyLookups.Scalar);

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
        //Push past the 8-entry Inline boundary to materialise the
        //sorted backing array (keys-only — children are absent).
        uint[] inputs = [50, 10, 30, 20, 40, 5, 60, 25, 15];

        foreach(uint key in inputs)
        {
            EdgeMap.InsertOrReplace(ref map, key, NodeHandle.None, pools, InlineKeyLookups.Scalar);
        }

        Assert.AreEqual(EdgeMapKind.SortedKeysOnly, map.Kind);

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
        Assert.AreSequenceEqual(expected, visited);
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
            EdgeMap.InsertOrReplace(ref map, key * 10, NodeHandle.None, pools, InlineKeyLookups.Scalar);
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
            EdgeMap.InsertOrReplace(ref map, key, NodeHandle.None, pools, InlineKeyLookups.Scalar);
        }

        EdgeMapKeyCursor cursor = CursorOver(in map);
        cursor.SeekTo(25U);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(30U, cursor.CurrentKey);
    }

    [TestMethod]
    public void SeekToBeyondLastKeyReachesAtEnd()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        EdgeMap.InsertOrReplace(ref map, 10U, NodeHandle.None, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 20U, NodeHandle.None, pools, InlineKeyLookups.Scalar);

        EdgeMapKeyCursor cursor = CursorOver(in map);
        cursor.SeekTo(99U);

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void SeekToWithTargetBelowCurrentIsNoOp()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        EdgeMap.InsertOrReplace(ref map, 10U, NodeHandle.None, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 20U, NodeHandle.None, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 30U, NodeHandle.None, pools, InlineKeyLookups.Scalar);

        EdgeMapKeyCursor cursor = CursorOver(in map);
        cursor.MoveNext();
        cursor.MoveNext();

        Assert.AreEqual(30U, cursor.CurrentKey);

        //Seeking back to 10 must not move the cursor backwards.
        cursor.SeekTo(10U);

        Assert.AreEqual(30U, cursor.CurrentKey);
    }

    [TestMethod]
    public void SeekToOnInlineMapWithMatchingKeyDoesNotAdvance()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        EdgeMap.InsertOrReplace(ref map, 7U, NodeHandle.None, pools, InlineKeyLookups.Scalar);

        EdgeMapKeyCursor cursor = CursorOver(in map);
        cursor.SeekTo(7U);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(7U, cursor.CurrentKey);
    }

    [TestMethod]
    public void SeekToOnInlineMapWithLargerTargetReachesEnd()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        EdgeMap.InsertOrReplace(ref map, 7U, NodeHandle.None, pools, InlineKeyLookups.Scalar);

        EdgeMapKeyCursor cursor = CursorOver(in map);
        cursor.SeekTo(8U);

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void SeekToOnInlineMapWithSmallerTargetIsNoOp()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        EdgeMap.InsertOrReplace(ref map, 7U, NodeHandle.None, pools, InlineKeyLookups.Scalar);

        EdgeMapKeyCursor cursor = CursorOver(in map);
        cursor.SeekTo(3U);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(7U, cursor.CurrentKey);
    }

    [TestMethod]
    public void HighCardinalitySeekToReachesCorrectKey()
    {
        const int total = 4096;
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();

        for(uint key = 0; key < total; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key * 2, NodeHandle.None, pools, InlineKeyLookups.Scalar);
        }

        EdgeMapKeyCursor cursor = CursorOver(in map);
        cursor.SeekTo(2001U);

        Assert.IsFalse(cursor.AtEnd);
        //2001 is odd so not present; the cursor must land on 2002.
        Assert.AreEqual(2002U, cursor.CurrentKey);
    }

    [TestMethod]
    public void CurrentChildIsNoneForDepthOneLeafEntries()
    {
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        EdgeMap.InsertOrReplace(ref map, 1U, NodeHandle.None, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 2U, NodeHandle.None, pools, InlineKeyLookups.Scalar);

        EdgeMapKeyCursor cursor = CursorOver(in map);

        Assert.IsTrue(cursor.CurrentChild.IsNone);

        cursor.MoveNext();

        Assert.IsTrue(cursor.CurrentChild.IsNone);
    }

    [TestMethod]
    public void CurrentChildReturnsHandleForInnerNodeEntries()
    {
        NodeHandle child1 = NodeHandle.FromEncoded(10);
        NodeHandle child2 = NodeHandle.FromEncoded(20);
        EdgeMap map = default;
        BuildPools pools = BuildPools.CreateDefault();
        EdgeMap.InsertOrReplace(ref map, 1U, child1, pools, InlineKeyLookups.Scalar);
        EdgeMap.InsertOrReplace(ref map, 2U, child2, pools, InlineKeyLookups.Scalar);

        EdgeMapKeyCursor cursor = CursorOver(in map);

        Assert.AreEqual(child1, cursor.CurrentChild);

        cursor.MoveNext();

        Assert.AreEqual(child2, cursor.CurrentChild);
    }
}
