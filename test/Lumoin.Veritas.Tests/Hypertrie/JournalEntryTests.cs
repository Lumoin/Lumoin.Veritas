using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class JournalEntryTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void DefaultEntryHasEmptyIdsAndArrays()
    {
        JournalEntry entry = default;

        Assert.AreEqual(NodeIdentifier.Empty, entry.ParentId);
        Assert.AreEqual(NodeIdentifier.Empty, entry.ChildId);
        Assert.AreEqual(EditSessionEntryKind.Initial, entry.EntryKind);
        Assert.IsNull(entry.SessionId);
        Assert.IsNull(entry.EditCommitment);
        Assert.IsTrue(entry.Additions.IsDefaultOrEmpty);
        Assert.IsTrue(entry.Removals.IsDefaultOrEmpty);
        Assert.AreEqual(default, entry.Timestamp);
        Assert.AreEqual(0, entry.SequenceNumber);
    }

    [TestMethod]
    public void EntriesWithIdenticalFieldsAreEqual()
    {
        ImmutableArray<EncodedTriple> additions = [EncodedTriple.FromEncoded(1, 2, 3)];
        ImmutableArray<EncodedTriple> removals = [EncodedTriple.FromEncoded(4, 5, 6)];
        DateTimeOffset timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        NodeIdentifier parent = new(0xAAAA);
        NodeIdentifier child = new(0xBBBB);
        SessionId sessionId = SessionId.NewId();
        NodeIdentifier commitment = new(0xCCCC);

        JournalEntry left = new(parent, child, EditSessionEntryKind.Committed, sessionId, commitment, additions, removals, timestamp, 7);
        JournalEntry right = new(parent, child, EditSessionEntryKind.Committed, sessionId, commitment, additions, removals, timestamp, 7);

        Assert.AreEqual(left, right);
    }

    [TestMethod]
    public void EntriesDifferingInSequenceNumberAreDistinct()
    {
        ImmutableArray<EncodedTriple> additions = [EncodedTriple.FromEncoded(1, 2, 3)];
        ImmutableArray<EncodedTriple> removals = ImmutableArray<EncodedTriple>.Empty;

        JournalEntry left = new(
            NodeIdentifier.Empty,
            new NodeIdentifier(1UL),
            EditSessionEntryKind.Initial,
            SessionId: null,
            EditCommitment: null,
            additions,
            removals,
            DateTimeOffset.UnixEpoch,
            0);

        JournalEntry right = left with { SequenceNumber = 1 };

        Assert.AreNotEqual(left, right);
    }

    [TestMethod]
    public void EntryWithExpressionUpdatesOneField()
    {
        JournalEntry original = new(
            NodeIdentifier.Empty,
            new NodeIdentifier(0xCAFE),
            EditSessionEntryKind.Initial,
            SessionId: null,
            EditCommitment: null,
            ImmutableArray<EncodedTriple>.Empty,
            ImmutableArray<EncodedTriple>.Empty,
            DateTimeOffset.UnixEpoch,
            0);

        JournalEntry updated = original with { SequenceNumber = 42 };

        Assert.AreEqual(42, updated.SequenceNumber);
        Assert.AreEqual(original.ParentId, updated.ParentId);
        Assert.AreEqual(original.ChildId, updated.ChildId);
        Assert.AreEqual(original.EntryKind, updated.EntryKind);
    }

    [TestMethod]
    public void InitialFactoryProducesInitialKindWithCommitment()
    {
        ImmutableArray<EncodedTriple> additions = [EncodedTriple.FromEncoded(1, 2, 3), EncodedTriple.FromEncoded(4, 5, 6)];
        NodeIdentifier childId = new(0xCAFE);

        JournalEntry entry = JournalEntry.Initial(VeritasHashing.Default, childId, additions);

        Assert.AreEqual(EditSessionEntryKind.Initial, entry.EntryKind);
        Assert.AreEqual(NodeIdentifier.Empty, entry.ParentId);
        Assert.AreEqual(childId, entry.ChildId);
        Assert.IsNull(entry.SessionId);
        Assert.IsNotNull(entry.EditCommitment);
        Assert.AreSequenceEqual(additions, entry.Additions);
        Assert.IsTrue(entry.Removals.IsEmpty);
    }

    [TestMethod]
    public void StartedFactoryProducesNonMutatingEntryWithoutCommitment()
    {
        NodeIdentifier head = new(0xAAAA);
        SessionId sessionId = SessionId.NewId();

        JournalEntry entry = JournalEntry.Started(head, sessionId);

        Assert.AreEqual(EditSessionEntryKind.Started, entry.EntryKind);
        Assert.AreEqual(head, entry.ParentId);
        Assert.AreEqual(head, entry.ChildId);
        Assert.AreEqual(sessionId, entry.SessionId);
        Assert.IsNull(entry.EditCommitment);
        Assert.IsTrue(entry.Additions.IsEmpty);
        Assert.IsTrue(entry.Removals.IsEmpty);
    }

    [TestMethod]
    public void CommittedFactoryProducesCommittedEntryWithCommitment()
    {
        NodeIdentifier parent = new(0xAAAA);
        NodeIdentifier child = new(0xBBBB);
        SessionId sessionId = SessionId.NewId();
        ImmutableArray<EncodedTriple> additions = [EncodedTriple.FromEncoded(7, 8, 9)];
        ImmutableArray<EncodedTriple> removals = ImmutableArray<EncodedTriple>.Empty;

        JournalEntry entry = JournalEntry.Committed(VeritasHashing.Default, parent, child, sessionId, additions, removals);

        Assert.AreEqual(EditSessionEntryKind.Committed, entry.EntryKind);
        Assert.AreEqual(parent, entry.ParentId);
        Assert.AreEqual(child, entry.ChildId);
        Assert.AreEqual(sessionId, entry.SessionId);
        Assert.IsNotNull(entry.EditCommitment);
        Assert.AreSequenceEqual(additions, entry.Additions);
        Assert.AreSequenceEqual(removals, entry.Removals);
    }

    [TestMethod]
    public void AbandonedFactoryProducesNonMutatingEntryWithoutCommitment()
    {
        NodeIdentifier head = new(0xAAAA);
        SessionId sessionId = SessionId.NewId();

        JournalEntry entry = JournalEntry.Abandoned(head, sessionId);

        Assert.AreEqual(EditSessionEntryKind.Abandoned, entry.EntryKind);
        Assert.AreEqual(head, entry.ParentId);
        Assert.AreEqual(head, entry.ChildId);
        Assert.AreEqual(sessionId, entry.SessionId);
        Assert.IsNull(entry.EditCommitment);
        Assert.IsTrue(entry.Additions.IsEmpty);
        Assert.IsTrue(entry.Removals.IsEmpty);
    }

    [TestMethod]
    public void FactoryEntriesLeaveTimestampAndSequenceForJournal()
    {
        //The journal owns SequenceNumber and Timestamp; factory
        //methods leave them as default placeholders that the
        //journal will overwrite on append.
        SessionId sessionId = SessionId.NewId();
        JournalEntry started = JournalEntry.Started(new NodeIdentifier(1), sessionId);

        Assert.AreEqual(default, started.Timestamp);
        Assert.AreEqual(0L, started.SequenceNumber);
    }
}
