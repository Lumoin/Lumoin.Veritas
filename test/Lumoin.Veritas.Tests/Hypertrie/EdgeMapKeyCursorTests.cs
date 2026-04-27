using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class EdgeMapKeyCursorTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void EmptyMapCursorIsImmediatelyAtEnd()
    {
        EdgeMap map = default;

        EdgeMapKeyCursor cursor = new(in map);

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void InlineMapCursorVisitsTheSingleKeyThenEnds()
    {
        EdgeMap map = default;
        EdgeMap.InsertOrReplace(ref map, 42L, null);

        EdgeMapKeyCursor cursor = new(in map);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(42L, cursor.CurrentKey);

        cursor.MoveNext();

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void SortedArrayCursorVisitsKeysInAscendingOrder()
    {
        EdgeMap map = default;
        long[] inputs = [50, 10, 30, 20, 40];

        foreach(long key in inputs)
        {
            EdgeMap.InsertOrReplace(ref map, key, null);
        }

        EdgeMapKeyCursor cursor = new(in map);
        long[] visited = new long[inputs.Length];

        for(int i = 0; i < inputs.Length; i++)
        {
            Assert.IsFalse(cursor.AtEnd);

            visited[i] = cursor.CurrentKey;

            cursor.MoveNext();
        }

        Assert.IsTrue(cursor.AtEnd);

        long[] expected = [.. inputs.OrderBy(x => x)];
        CollectionAssert.AreEqual(expected, visited);
    }

    [TestMethod]
    public void CurrentKeyThrowsWhenAtEnd()
    {
        EdgeMap map = default;
        EdgeMapKeyCursor cursor = new(in map);

        Assert.Throws<InvalidOperationException>(() => _ = cursor.CurrentKey);
    }

    [TestMethod]
    public void CurrentChildThrowsWhenAtEnd()
    {
        EdgeMap map = default;
        EdgeMapKeyCursor cursor = new(in map);

        Assert.Throws<InvalidOperationException>(() => _ = cursor.CurrentChild);
    }

    [TestMethod]
    public void MoveNextOnEmptyCursorIsNoOp()
    {
        EdgeMap map = default;
        EdgeMapKeyCursor cursor = new(in map);

        cursor.MoveNext();
        cursor.MoveNext();

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void SeekToFindsExactKey()
    {
        EdgeMap map = default;

        for(long key = 0; key < 10; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key * 10, null);
        }

        EdgeMapKeyCursor cursor = new(in map);
        cursor.SeekTo(50L);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(50L, cursor.CurrentKey);
    }

    [TestMethod]
    public void SeekToFindsFirstKeyAtOrAfterTarget()
    {
        EdgeMap map = default;
        long[] keys = [10, 20, 30, 40, 50];

        foreach(long key in keys)
        {
            EdgeMap.InsertOrReplace(ref map, key, null);
        }

        EdgeMapKeyCursor cursor = new(in map);
        cursor.SeekTo(25L);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(30L, cursor.CurrentKey);
    }

    [TestMethod]
    public void SeekToBeyondLastKeyReachesAtEnd()
    {
        EdgeMap map = default;
        EdgeMap.InsertOrReplace(ref map, 10L, null);
        EdgeMap.InsertOrReplace(ref map, 20L, null);

        EdgeMapKeyCursor cursor = new(in map);
        cursor.SeekTo(99L);

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void SeekToWithTargetBelowCurrentIsNoOp()
    {
        EdgeMap map = default;
        EdgeMap.InsertOrReplace(ref map, 10L, null);
        EdgeMap.InsertOrReplace(ref map, 20L, null);
        EdgeMap.InsertOrReplace(ref map, 30L, null);

        EdgeMapKeyCursor cursor = new(in map);
        cursor.MoveNext();
        cursor.MoveNext();

        Assert.AreEqual(30L, cursor.CurrentKey);

        //Seeking back to 10 must not move the cursor backwards.
        cursor.SeekTo(10L);

        Assert.AreEqual(30L, cursor.CurrentKey);
    }

    [TestMethod]
    public void SeekToOnInlineMapWithMatchingKeyDoesNotAdvance()
    {
        EdgeMap map = default;
        EdgeMap.InsertOrReplace(ref map, 7L, null);

        EdgeMapKeyCursor cursor = new(in map);
        cursor.SeekTo(7L);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(7L, cursor.CurrentKey);
    }

    [TestMethod]
    public void SeekToOnInlineMapWithLargerTargetReachesEnd()
    {
        EdgeMap map = default;
        EdgeMap.InsertOrReplace(ref map, 7L, null);

        EdgeMapKeyCursor cursor = new(in map);
        cursor.SeekTo(8L);

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void SeekToOnInlineMapWithSmallerTargetIsNoOp()
    {
        EdgeMap map = default;
        EdgeMap.InsertOrReplace(ref map, 7L, null);

        EdgeMapKeyCursor cursor = new(in map);
        cursor.SeekTo(3L);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(7L, cursor.CurrentKey);
    }

    [TestMethod]
    public void HighCardinalitySeekToReachesCorrectKey()
    {
        const int total = 4096;
        EdgeMap map = default;

        for(long key = 0; key < total; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key * 2, null);
        }

        EdgeMapKeyCursor cursor = new(in map);
        cursor.SeekTo(2001L);

        Assert.IsFalse(cursor.AtEnd);
        //2001 is odd so not present; the cursor must land on 2002.
        Assert.AreEqual(2002L, cursor.CurrentKey);
    }

    [TestMethod]
    public void CurrentChildIsNullForDepthOneLeafEntries()
    {
        EdgeMap map = default;
        EdgeMap.InsertOrReplace(ref map, 1L, null);
        EdgeMap.InsertOrReplace(ref map, 2L, null);

        EdgeMapKeyCursor cursor = new(in map);

        Assert.IsNull(cursor.CurrentChild);

        cursor.MoveNext();

        Assert.IsNull(cursor.CurrentChild);
    }

    [TestMethod]
    public void CurrentChildReturnsReferenceForInnerNodeEntries()
    {
        HypertrieNode child1 = new(2);
        HypertrieNode child2 = new(2);
        EdgeMap map = default;
        EdgeMap.InsertOrReplace(ref map, 1L, child1);
        EdgeMap.InsertOrReplace(ref map, 2L, child2);

        EdgeMapKeyCursor cursor = new(in map);

        Assert.AreSame(child1, cursor.CurrentChild);

        cursor.MoveNext();

        Assert.AreSame(child2, cursor.CurrentChild);
    }
}
