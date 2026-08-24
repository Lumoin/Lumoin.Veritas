using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie.Editing;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class EditBufferTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void NewBufferIsEmpty()
    {
        EditBuffer buffer = new();

        Assert.AreEqual(0, buffer.Count);
        Assert.IsEmpty(buffer.PendingAdditions);
        Assert.IsEmpty(buffer.PendingRemovals);
    }

    [TestMethod]
    public void AddRecordsPendingAddition()
    {
        EditBuffer buffer = new();
        EncodedTriple triple = EncodedTriple.FromEncoded(1, 2, 3);

        buffer.Add(triple);

        Assert.AreEqual(1, buffer.Count);
        Assert.IsTrue(buffer.TryGetEdit(triple, out EditKind kind));
        Assert.AreEqual(EditKind.Add, kind);
        Assert.Contains(triple, buffer.PendingAdditions);
        Assert.IsEmpty(buffer.PendingRemovals);
    }

    [TestMethod]
    public void RemoveRecordsPendingRemoval()
    {
        EditBuffer buffer = new();
        EncodedTriple triple = EncodedTriple.FromEncoded(1, 2, 3);

        buffer.Remove(triple);

        Assert.AreEqual(1, buffer.Count);
        Assert.IsTrue(buffer.TryGetEdit(triple, out EditKind kind));
        Assert.AreEqual(EditKind.Remove, kind);
        Assert.Contains(triple, buffer.PendingRemovals);
        Assert.IsEmpty(buffer.PendingAdditions);
    }

    [TestMethod]
    public void AddAfterRemoveLastWriteWins()
    {
        EditBuffer buffer = new();
        EncodedTriple triple = EncodedTriple.FromEncoded(1, 2, 3);

        buffer.Remove(triple);
        buffer.Add(triple);

        Assert.AreEqual(1, buffer.Count);
        Assert.IsTrue(buffer.TryGetEdit(triple, out EditKind kind));
        Assert.AreEqual(EditKind.Add, kind);
    }

    [TestMethod]
    public void RemoveAfterAddLastWriteWins()
    {
        EditBuffer buffer = new();
        EncodedTriple triple = EncodedTriple.FromEncoded(1, 2, 3);

        buffer.Add(triple);
        buffer.Remove(triple);

        Assert.AreEqual(1, buffer.Count);
        Assert.IsTrue(buffer.TryGetEdit(triple, out EditKind kind));
        Assert.AreEqual(EditKind.Remove, kind);
    }

    [TestMethod]
    public void AddOfDuplicateTripleStillCountsAsOne()
    {
        EditBuffer buffer = new();
        EncodedTriple triple = EncodedTriple.FromEncoded(1, 2, 3);

        buffer.Add(triple);
        buffer.Add(triple);
        buffer.Add(triple);

        Assert.AreEqual(1, buffer.Count);
    }

    [TestMethod]
    public void EditsAcrossDistinctTriplesAccumulate()
    {
        EditBuffer buffer = new();
        EncodedTriple a = EncodedTriple.FromEncoded(1, 2, 3);
        EncodedTriple b = EncodedTriple.FromEncoded(4, 5, 6);
        EncodedTriple c = EncodedTriple.FromEncoded(7, 8, 9);

        buffer.Add(a);
        buffer.Add(b);
        buffer.Remove(c);

        Assert.AreEqual(3, buffer.Count);
        Assert.HasCount(2, buffer.PendingAdditions);
        Assert.HasCount(1, buffer.PendingRemovals);
        Assert.Contains(a, buffer.PendingAdditions);
        Assert.Contains(b, buffer.PendingAdditions);
        Assert.Contains(c, buffer.PendingRemovals);
    }

    [TestMethod]
    public void ClearEditRemovesSingleEntry()
    {
        EditBuffer buffer = new();
        EncodedTriple a = EncodedTriple.FromEncoded(1, 2, 3);
        EncodedTriple b = EncodedTriple.FromEncoded(4, 5, 6);
        buffer.Add(a);
        buffer.Add(b);

        bool removed = buffer.ClearEdit(a);

        Assert.IsTrue(removed);
        Assert.AreEqual(1, buffer.Count);
        Assert.IsFalse(buffer.TryGetEdit(a, out _));
        Assert.IsTrue(buffer.TryGetEdit(b, out _));
    }

    [TestMethod]
    public void ClearEditOnUnknownTripleReturnsFalse()
    {
        EditBuffer buffer = new();
        EncodedTriple t = EncodedTriple.FromEncoded(1, 2, 3);

        bool removed = buffer.ClearEdit(t);

        Assert.IsFalse(removed);
    }

    [TestMethod]
    public void ClearRemovesAllEdits()
    {
        EditBuffer buffer = new();
        buffer.Add(EncodedTriple.FromEncoded(1, 2, 3));
        buffer.Add(EncodedTriple.FromEncoded(4, 5, 6));
        buffer.Remove(EncodedTriple.FromEncoded(7, 8, 9));

        buffer.Clear();

        Assert.AreEqual(0, buffer.Count);
        Assert.IsEmpty(buffer.PendingAdditions);
        Assert.IsEmpty(buffer.PendingRemovals);
    }

    [TestMethod]
    public void EnumerateEditsYieldsEveryRecordedEdit()
    {
        EditBuffer buffer = new();
        EncodedTriple a = EncodedTriple.FromEncoded(1, 2, 3);
        EncodedTriple b = EncodedTriple.FromEncoded(4, 5, 6);
        buffer.Add(a);
        buffer.Remove(b);

        Dictionary<EncodedTriple, EditKind> seen = [];
        foreach(KeyValuePair<EncodedTriple, EditKind> entry in buffer.EnumerateEdits())
        {
            seen[entry.Key] = entry.Value;
        }

        Assert.HasCount(2, seen);
        Assert.AreEqual(EditKind.Add, seen[a]);
        Assert.AreEqual(EditKind.Remove, seen[b]);
    }
}
