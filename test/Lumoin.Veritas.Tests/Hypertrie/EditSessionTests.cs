using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

/// <summary>
/// Lifecycle and semantics tests for <see cref="EditSession"/>.
/// Validates the state machine (open → committed → disposed,
/// or open → disposed-as-abandoned), the edit-buffer surface,
/// and the journal entries produced by each transition.
/// </summary>
[TestClass]
internal sealed class EditSessionTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task OpenSessionWritesStartedEntry()
    {
        InMemoryJournal journal = new();
        using NodeStore store = new(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
        EncodedTriple[] seed = [EncodedTriple.FromEncoded(1, 2, 3)];

        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(seed, store, TestContext.CancellationToken).ConfigureAwait(false);

        EditSession session = await graph.OpenEditSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            //Two entries: the Initial from BuildAsync and the
            //Started from the open. The Started entry's session
            //id must equal the session's id, and it must not move
            //the journal head.
            Assert.AreEqual(2, journal.Length);

            JournalEntry[] entries = await CollectAsync(journal, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(EditSessionEntryKind.Initial, entries[0].EntryKind);
            Assert.AreEqual(EditSessionEntryKind.Started, entries[1].EntryKind);
            Assert.AreEqual(session.Id, entries[1].SessionId);
            Assert.AreEqual(entries[1].ParentId, entries[1].ChildId);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task FromSnapshotReadsCommittedStateAndLeavesOriginalUnchanged()
    {
        InMemoryJournal journal = new();
        using NodeStore store = new(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
        EncodedTriple seed = EncodedTriple.FromEncoded(1, 2, 3);
        EncodedTriple added = EncodedTriple.FromEncoded(4, 5, 6);

        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync([seed], store, TestContext.CancellationToken).ConfigureAwait(false);

        EditSession session = await graph.OpenEditSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            session.Add(added);
            session.Remove(seed);
            using HypertrieSnapshot committed = await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            HypertrieGraphStore mutated = HypertrieGraphStore.FromSnapshot(committed);

            //The re-wrapped store reflects the commit: the added triple is present, the removed one gone.
            EncodedTriple[] mutatedTriples = [.. mutated.Match(TermId.None, TermId.None, TermId.None)];
            Assert.AreEqual(1, mutated.Count);
            Assert.Contains(added, mutatedTriples);
            Assert.DoesNotContain(seed, mutatedTriples);

            //Snapshot isolation: the original store still reads its pre-edit state.
            EncodedTriple[] originalTriples = [.. graph.Match(TermId.None, TermId.None, TermId.None)];
            Assert.AreEqual(1, graph.Count);
            Assert.Contains(seed, originalTriples);
            Assert.DoesNotContain(added, originalTriples);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task AddRecordsTripleInBuffer()
    {
        EditSessionScope scope = await OpenSessionWithSeedAsync([EncodedTriple.FromEncoded(1, 2, 3)]).ConfigureAwait(false);
        try
        {
            EncodedTriple t = EncodedTriple.FromEncoded(4, 5, 6);
            scope.Session.Add(t);

            Assert.AreEqual(1, scope.Session.Buffer.Count);
            Assert.IsTrue(scope.Session.Buffer.TryGetEdit(t, out EditKind kind));
            Assert.AreEqual(EditKind.Add, kind);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task RemoveRecordsTripleInBuffer()
    {
        EditSessionScope scope = await OpenSessionWithSeedAsync([EncodedTriple.FromEncoded(1, 2, 3)]).ConfigureAwait(false);
        try
        {
            EncodedTriple t = EncodedTriple.FromEncoded(1, 2, 3);
            scope.Session.Remove(t);

            Assert.IsTrue(scope.Session.Buffer.TryGetEdit(t, out EditKind kind));
            Assert.AreEqual(EditKind.Remove, kind);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task AddRangeRecordsEveryTriple()
    {
        EditSessionScope scope = await OpenSessionWithSeedAsync([EncodedTriple.FromEncoded(1, 2, 3)]).ConfigureAwait(false);
        try
        {
            EncodedTriple[] triples =
            [
                EncodedTriple.FromEncoded(10, 11, 12),
                EncodedTriple.FromEncoded(20, 21, 22),
                EncodedTriple.FromEncoded(30, 31, 32),
            ];
            scope.Session.AddRange(triples);

            Assert.AreEqual(3, scope.Session.Buffer.Count);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task RemoveRangeRecordsEveryTriple()
    {
        EncodedTriple[] seed =
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(20, 21, 22),
        ];
        EditSessionScope scope = await OpenSessionWithSeedAsync(seed).ConfigureAwait(false);
        try
        {
            scope.Session.RemoveRange(seed);

            Assert.AreEqual(2, scope.Session.Buffer.Count);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task CommitWithSingleAdditionProducesNewSnapshot()
    {
        EncodedTriple[] seed = [EncodedTriple.FromEncoded(1, 2, 3)];
        EditSessionScope scope = await OpenSessionWithSeedAsync(seed).ConfigureAwait(false);
        try
        {
            EncodedTriple added = EncodedTriple.FromEncoded(4, 5, 6);
            scope.Session.Add(added);

            using HypertrieSnapshot committed = await scope.Session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            //The new snapshot's id differs from the base, and reading
            //all triples from the committed root surfaces both the
            //seed and the added triple.
            Assert.AreNotEqual(scope.GraphStore.Snapshot.Id, committed.Id);
            HashSet<EncodedTriple> reachable = [.. HypertrieOps.Match(committed.Store.GetByHandle(committed.Root), committed.Store, TermId.None, TermId.None, TermId.None)];
            Assert.HasCount(2, reachable);
            Assert.Contains(added, reachable);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task CommitWithSingleRemovalProducesNewSnapshot()
    {
        EncodedTriple kept = EncodedTriple.FromEncoded(1, 2, 3);
        EncodedTriple toRemove = EncodedTriple.FromEncoded(4, 5, 6);
        EncodedTriple[] seed = [kept, toRemove];
        EditSessionScope scope = await OpenSessionWithSeedAsync(seed).ConfigureAwait(false);
        try
        {
            scope.Session.Remove(toRemove);

            using HypertrieSnapshot committed = await scope.Session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreNotEqual(scope.GraphStore.Snapshot.Id, committed.Id);
            HashSet<EncodedTriple> reachable = [.. HypertrieOps.Match(committed.Store.GetByHandle(committed.Root), committed.Store, TermId.None, TermId.None, TermId.None)];
            Assert.HasCount(1, reachable);
            Assert.Contains(kept, reachable);
            Assert.DoesNotContain(toRemove, reachable);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task EmptyCommitReturnsBaseAcquiredAndWritesNoEntry()
    {
        EncodedTriple[] seed = [EncodedTriple.FromEncoded(1, 2, 3)];
        EditSessionScope scope = await OpenSessionWithSeedAsync(seed).ConfigureAwait(false);
        try
        {
            int journalLengthBeforeCommit = scope.Journal.Length;

            using HypertrieSnapshot committed = await scope.Session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            //Same id as the base: the empty effective delta short-circuits to the base.
            Assert.AreEqual(scope.GraphStore.Snapshot.Id, committed.Id);

            //No new entry written.
            Assert.AreEqual(journalLengthBeforeCommit, scope.Journal.Length);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task EmptyCommitFollowedByDisposeWritesNoAbandonedEntry()
    {
        //An empty commit transitions the session to Committed
        //before dispose; the dispose path therefore does not
        //write an Abandoned entry.
        EncodedTriple[] seed = [EncodedTriple.FromEncoded(1, 2, 3)];
        EditSessionScope scope = await OpenSessionWithSeedAsync(seed).ConfigureAwait(false);
        try
        {
            using HypertrieSnapshot committedSnapshot = await scope.Session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
            int afterCommit = scope.Journal.Length;

            await scope.DisposeAsync().ConfigureAwait(false);

            Assert.AreEqual(afterCommit, scope.Journal.Length);
        }
        finally
        {
            //DisposeAsync is idempotent — safe to call again here even when the body already disposed.
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task CommitWithBothAdditionsAndRemovalsTransitions()
    {
        EncodedTriple kept = EncodedTriple.FromEncoded(1, 2, 3);
        EncodedTriple toRemove = EncodedTriple.FromEncoded(4, 5, 6);
        EncodedTriple toAdd = EncodedTriple.FromEncoded(7, 8, 9);
        EncodedTriple[] seed = [kept, toRemove];
        EditSessionScope scope = await OpenSessionWithSeedAsync(seed).ConfigureAwait(false);
        try
        {
            scope.Session.Remove(toRemove);
            scope.Session.Add(toAdd);

            using HypertrieSnapshot committed = await scope.Session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            HashSet<EncodedTriple> reachable = [.. HypertrieOps.Match(committed.Store.GetByHandle(committed.Root), committed.Store, TermId.None, TermId.None, TermId.None)];
            Assert.HasCount(2, reachable);
            Assert.Contains(kept, reachable);
            Assert.Contains(toAdd, reachable);
            Assert.DoesNotContain(toRemove, reachable);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task CommittedEntryRecordsSessionIdAndCommitment()
    {
        EncodedTriple[] seed = [EncodedTriple.FromEncoded(1, 2, 3)];
        EditSessionScope scope = await OpenSessionWithSeedAsync(seed).ConfigureAwait(false);
        try
        {
            scope.Session.Add(EncodedTriple.FromEncoded(4, 5, 6));

            using HypertrieSnapshot committedSnapshot = await scope.Session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            JournalEntry[] entries = await CollectAsync(scope.Journal, TestContext.CancellationToken).ConfigureAwait(false);
            JournalEntry committedEntry = entries[^1];
            Assert.AreEqual(EditSessionEntryKind.Committed, committedEntry.EntryKind);
            Assert.AreEqual(scope.Session.Id, committedEntry.SessionId);
            Assert.IsNotNull(committedEntry.EditCommitment);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task DoubleCommitThrows()
    {
        EditSessionScope scope = await OpenSessionWithSeedAsync([EncodedTriple.FromEncoded(1, 2, 3)]).ConfigureAwait(false);
        try
        {
            scope.Session.Add(EncodedTriple.FromEncoded(4, 5, 6));
            using HypertrieSnapshot committedSnapshot = await scope.Session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await scope.Session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.IsNotNull(thrown);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task EditAfterCommitThrows()
    {
        EditSessionScope scope = await OpenSessionWithSeedAsync([EncodedTriple.FromEncoded(1, 2, 3)]).ConfigureAwait(false);
        try
        {
            scope.Session.Add(EncodedTriple.FromEncoded(4, 5, 6));
            using HypertrieSnapshot committedSnapshot = await scope.Session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.Throws<InvalidOperationException>(() => scope.Session.Add(EncodedTriple.FromEncoded(7, 8, 9)));
            Assert.Throws<InvalidOperationException>(() => scope.Session.Remove(EncodedTriple.FromEncoded(1, 2, 3)));
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task DisposeWithoutCommitWritesAbandonedEntry()
    {
        InMemoryJournal journal = new();
        using NodeStore store = new(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
        EncodedTriple[] seed = [EncodedTriple.FromEncoded(1, 2, 3)];
        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(seed, store, TestContext.CancellationToken).ConfigureAwait(false);

        SessionId capturedId;
        EditSession session = await graph.OpenEditSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            capturedId = session.Id;
            session.Add(EncodedTriple.FromEncoded(4, 5, 6));
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        JournalEntry[] entries = await CollectAsync(journal, TestContext.CancellationToken).ConfigureAwait(false);
        JournalEntry last = entries[^1];
        Assert.AreEqual(EditSessionEntryKind.Abandoned, last.EntryKind);
        Assert.AreEqual(capturedId, last.SessionId);
    }

    [TestMethod]
    public async Task DisposeAfterCommitWritesNoExtraEntry()
    {
        InMemoryJournal journal = new();
        using NodeStore store = new(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
        EncodedTriple[] seed = [EncodedTriple.FromEncoded(1, 2, 3)];
        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(seed, store, TestContext.CancellationToken).ConfigureAwait(false);

        EditSession session = await graph.OpenEditSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        int beforeDispose;
        try
        {
            session.Add(EncodedTriple.FromEncoded(4, 5, 6));
            using HypertrieSnapshot committedSnapshot = await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
            beforeDispose = journal.Length;
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        Assert.AreEqual(beforeDispose, journal.Length);
    }

    [TestMethod]
    public async Task DoubleDisposeIsNoOp()
    {
        InMemoryJournal journal = new();
        using NodeStore store = new(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
        EncodedTriple[] seed = [EncodedTriple.FromEncoded(1, 2, 3)];
        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(seed, store, TestContext.CancellationToken).ConfigureAwait(false);

        EditSession session = await graph.OpenEditSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);

        await session.DisposeAsync().ConfigureAwait(false);
        int afterFirstDispose = journal.Length;

        await session.DisposeAsync().ConfigureAwait(false);
        Assert.AreEqual(afterFirstDispose, journal.Length);
    }

    [TestMethod]
    public async Task SessionWithoutJournalSkipsLifecycleEntries()
    {
        //A store constructed without journal delegates emits no
        //Started/Committed/Abandoned entries; the session still
        //functions for state changes, just unrecorded.
        using NodeStore store = new(VeritasHashing.Default);
        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(
            [EncodedTriple.FromEncoded(1, 2, 3)],
            store,
            TestContext.CancellationToken).ConfigureAwait(false);

        EditSession session = await graph.OpenEditSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            session.Add(EncodedTriple.FromEncoded(4, 5, 6));
            using HypertrieSnapshot committed = await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            HashSet<EncodedTriple> reachable = [.. HypertrieOps.Match(committed.Store.GetByHandle(committed.Root), committed.Store, TermId.None, TermId.None, TermId.None)];
            Assert.HasCount(2, reachable);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task BaseSnapshotIdMatchesSessionField()
    {
        EditSessionScope scope = await OpenSessionWithSeedAsync([EncodedTriple.FromEncoded(1, 2, 3)]).ConfigureAwait(false);
        try
        {
            Assert.AreEqual(scope.GraphStore.Snapshot.Id, scope.Session.BaseSnapshotId);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask<EditSessionScope> OpenSessionWithSeedAsync(EncodedTriple[] seed)
    {
        InMemoryJournal journal = new();
        NodeStore? store = null;
        bool ownershipTransferred = false;

        try
        {
            store = new NodeStore(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
            HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(seed, store, TestContext.CancellationToken).ConfigureAwait(false);
            EditSession session = await graph.OpenEditSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);

            EditSessionScope scope = new(journal, store, graph, session);
            ownershipTransferred = true;
            return scope;
        }
        finally
        {
            if(!ownershipTransferred)
            {
                store?.Dispose();
            }
        }
    }

    private static async Task<JournalEntry[]> CollectAsync(InMemoryJournal journal, CancellationToken cancellationToken)
    {
        List<JournalEntry> result = [];
        await foreach(JournalEntry entry in journal.ReadDelegate(0L, cancellationToken).ConfigureAwait(false))
        {
            result.Add(entry);
        }

        return [.. result];
    }

    //Bundles together the resources one EditSession test needs:
    //the journal, the store, the graph, and the session itself.
    //Disposes them in the correct order. Implemented as a private
    //sealed class rather than a record so DisposeAsync can be
    //async and run the session-and-store teardown sequence.
    private sealed class EditSessionScope: IAsyncDisposable
    {
        public InMemoryJournal Journal { get; }

        public NodeStore Store { get; }

        public HypertrieGraphStore GraphStore { get; }

        public EditSession Session { get; }

        private int disposed;

        public EditSessionScope(InMemoryJournal journal, NodeStore store, HypertrieGraphStore graphStore, EditSession session)
        {
            Journal = journal;
            Store = store;
            GraphStore = graphStore;
            Session = session;
        }

        public async ValueTask DisposeAsync()
        {
            if(Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            await Session.DisposeAsync().ConfigureAwait(false);
            Store.Dispose();
        }
    }
}
