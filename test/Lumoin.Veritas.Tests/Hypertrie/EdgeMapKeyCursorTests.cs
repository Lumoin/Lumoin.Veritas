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
        EdgeMap.InsertOrReplace(ref map, 42U, null);

        EdgeMapKeyCursor cursor = new(in map);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(42U, cursor.CurrentKey);

        cursor.MoveNext();

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void SortedArrayCursorVisitsKeysInAscendingOrder()
    {
        EdgeMap map = default;
        uint[] inputs = [50, 10, 30, 20, 40];

        foreach(uint key in inputs)
        {
            EdgeMap.InsertOrReplace(ref map, key, null);
        }

        EdgeMapKeyCursor cursor = new(in map);
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

        for(uint key = 0; key < 10; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key * 10, null);
        }

        EdgeMapKeyCursor cursor = new(in map);
        cursor.SeekTo(50U);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(50U, cursor.CurrentKey);
    }

    [TestMethod]
    public void SeekToFindsFirstKeyAtOrAfterTarget()
    {
        EdgeMap map = default;
        uint[] keys = [10, 20, 30, 40, 50];

        foreach(uint key in keys)
        {
            EdgeMap.InsertOrReplace(ref map, key, null);
        }

        EdgeMapKeyCursor cursor = new(in map);
        cursor.SeekTo(25U);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(30U, cursor.CurrentKey);
    }

    [TestMethod]
    public void SeekToBeyondLastKeyReachesAtEnd()
    {
        EdgeMap map = default;
        EdgeMap.InsertOrReplace(ref map, 10U, null);
        EdgeMap.InsertOrReplace(ref map, 20U, null);

        EdgeMapKeyCursor cursor = new(in map);
        cursor.SeekTo(99U);

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void SeekToWithTargetBelowCurrentIsNoOp()
    {
        EdgeMap map = default;
        EdgeMap.InsertOrReplace(ref map, 10U, null);
        EdgeMap.InsertOrReplace(ref map, 20U, null);
        EdgeMap.InsertOrReplace(ref map, 30U, null);

        EdgeMapKeyCursor cursor = new(in map);
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
        EdgeMap.InsertOrReplace(ref map, 7U, null);

        EdgeMapKeyCursor cursor = new(in map);
        cursor.SeekTo(7U);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(7U, cursor.CurrentKey);
    }

    [TestMethod]
    public void SeekToOnInlineMapWithLargerTargetReachesEnd()
    {
        EdgeMap map = default;
        EdgeMap.InsertOrReplace(ref map, 7U, null);

        EdgeMapKeyCursor cursor = new(in map);
        cursor.SeekTo(8U);

        Assert.IsTrue(cursor.AtEnd);
    }

    [TestMethod]
    public void SeekToOnInlineMapWithSmallerTargetIsNoOp()
    {
        EdgeMap map = default;
        EdgeMap.InsertOrReplace(ref map, 7U, null);

        EdgeMapKeyCursor cursor = new(in map);
        cursor.SeekTo(3U);

        Assert.IsFalse(cursor.AtEnd);
        Assert.AreEqual(7U, cursor.CurrentKey);
    }

    [TestMethod]
    public void HighCardinalitySeekToReachesCorrectKey()
    {
        const int total = 4096;
        EdgeMap map = default;

        for(uint key = 0; key < total; key++)
        {
            EdgeMap.InsertOrReplace(ref map, key * 2, null);
        }

        EdgeMapKeyCursor cursor = new(in map);
        cursor.SeekTo(2001U);

        Assert.IsFalse(cursor.AtEnd);
        //2001 is odd so not present; the cursor must land on 2002.
        Assert.AreEqual(2002U, cursor.CurrentKey);
    }

    [TestMethod]
    public void CurrentChildIsNullForDepthOneLeafEntries()
    {
        EdgeMap map = default;
        EdgeMap.InsertOrReplace(ref map, 1U, null);
        EdgeMap.InsertOrReplace(ref map, 2U, null);

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
        EdgeMap.InsertOrReplace(ref map, 1U, child1);
        EdgeMap.InsertOrReplace(ref map, 2U, child2);

        EdgeMapKeyCursor cursor = new(in map);

        Assert.AreSame(child1, cursor.CurrentChild);

        cursor.MoveNext();

        Assert.AreSame(child2, cursor.CurrentChild);
    }
}
