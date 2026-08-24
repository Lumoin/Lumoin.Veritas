using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// The dataset-journal contract: one <see cref="DatasetEditSession"/>
/// commits one atomic <see cref="DatasetJournalEntry"/> however many
/// graphs it touches; the journal is a linear OCC log whose replay
/// reconstructs the dataset; concurrent sessions conflict at the
/// head-CAS and retry; readers never observe half a commit.
/// </summary>
[TestClass]
internal sealed class DatasetEditSessionTests
{
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Mints an IRI term in the test namespace.</summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="local">The local name.</param>
    /// <returns>The minted identifier.</returns>
    private static TermId Mint(TermDictionary dictionary, string local)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/ds/" + local)));
    }

    /// <summary>Encodes an (s, p, o) triple of local names.</summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="s">The subject local name.</param>
    /// <param name="p">The predicate local name.</param>
    /// <param name="o">The object local name.</param>
    /// <returns>The encoded triple.</returns>
    private static EncodedTriple Triple(TermDictionary dictionary, string s, string p, string o)
    {
        return EncodedTriple.FromEncoded(Mint(dictionary, s).Encoded, Mint(dictionary, p).Encoded, Mint(dictionary, o).Encoded);
    }

    /// <summary>Builds an empty dataset over an inspectable journal.</summary>
    /// <param name="journal">Receives the journal.</param>
    /// <param name="dictionary">Receives the dictionary.</param>
    /// <returns>The dataset.</returns>
    private static async Task<MutableSparqlDataset> CreateEmptyAsync(InMemoryDatasetJournal journal, TermDictionary dictionary)
    {
        return await MutableSparqlDataset.CreateAsync(
            dictionary,
            [],
            namedGraphs: null,
            journalAppend: journal.AppendDelegate,
            journalRead: journal.ReadDelegate).ConfigureAwait(false);
    }

    /// <summary>Reads all entries from a journal.</summary>
    /// <param name="journal">The journal.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The entries in sequence order.</returns>
    private static async Task<List<DatasetJournalEntry>> ReadAllAsync(InMemoryDatasetJournal journal, CancellationToken cancellationToken)
    {
        List<DatasetJournalEntry> entries = [];
        await foreach(DatasetJournalEntry entry in journal.ReadDelegate(0, cancellationToken).ConfigureAwait(false))
        {
            entries.Add(entry);
        }

        return entries;
    }

    [TestMethod]
    public async Task MultiGraphCommitProducesOneEntryWithAllTransitions()
    {
        InMemoryDatasetJournal journal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset dataset = await CreateEmptyAsync(journal, dictionary).ConfigureAwait(false);
        TermId graphA = Mint(dictionary, "graphA");
        TermId graphB = Mint(dictionary, "graphB");

        DatasetEditSession session = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            await session.ApplyDeltaAsync(TermId.None, [Triple(dictionary, "s", "p", "o")], [], TestContext.CancellationToken).ConfigureAwait(false);
            await session.ApplyDeltaAsync(graphA, [Triple(dictionary, "a", "p", "1")], [], TestContext.CancellationToken).ConfigureAwait(false);
            await session.ApplyDeltaAsync(graphB, [Triple(dictionary, "b", "p", "2")], [], TestContext.CancellationToken).ConfigureAwait(false);
            await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        List<DatasetJournalEntry> entries = await ReadAllAsync(journal, TestContext.CancellationToken).ConfigureAwait(false);

        //Initial, Started, Committed — and the commit carries all
        //three graph transitions in one atomic entry.
        Assert.HasCount(3, entries);
        Assert.AreEqual(EditSessionEntryKind.Initial, entries[0].EntryKind);
        Assert.AreEqual(EditSessionEntryKind.Started, entries[1].EntryKind);
        Assert.AreEqual(EditSessionEntryKind.Committed, entries[2].EntryKind);
        Assert.HasCount(3, entries[2].Transitions);
        Assert.AreEqual(entries[0].ChildId, entries[2].ParentId);
        Assert.AreEqual(journal.Head, entries[2].ChildId);
        Assert.AreEqual(dataset.StateId, journal.Head);

        //The default graph's transition starts from the empty root;
        //the named graphs' transitions are creations.
        Assert.AreEqual(NodeIdentifier.Empty, entries[2].Transitions[0].ParentRoot);
        Assert.IsNull(entries[2].Transitions[1].ParentRoot);
        Assert.IsNull(entries[2].Transitions[2].ParentRoot);
    }

    /// <summary>
    /// The journal append is the linearisation point: once a session appends, a competing session based on the same
    /// state is rejected at its own append — even while the first session's publish is still pending. The interleaving
    /// hook makes this deterministic every run: it pauses the first commit between its append and its publish; the
    /// second commit then attempts and conflicts at the head-CAS; only then does the first publish.
    /// </summary>
    [TestMethod]
    public async Task CompetingCommitConflictsAtTheAppendWhileTheFirstPublishIsPending()
    {
        InMemoryDatasetJournal journal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset dataset = await CreateEmptyAsync(journal, dictionary).ConfigureAwait(false);

        DatasetEditSession first = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        DatasetEditSession second = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            await first.ApplyDeltaAsync(TermId.None, [Triple(dictionary, "s", "p", "first")], [], TestContext.CancellationToken).ConfigureAwait(false);
            await second.ApplyDeltaAsync(TermId.None, [Triple(dictionary, "s", "p", "second")], [], TestContext.CancellationToken).ConfigureAwait(false);

            //first pauses between its (linearising) append and its publish; the test releases it only after second has
            //attempted its commit, so the append-conflict-while-publish-pending interleaving happens every run.
            TaskCompletionSource firstReachedHook = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
            first.CommitInterleavingHook = async _ =>
            {
                firstReachedHook.SetResult();
                await releaseFirst.Task.ConfigureAwait(false);
            };

            Task firstCommit = first.CommitAsync(TestContext.CancellationToken).AsTask();
            await firstReachedHook.Task.ConfigureAwait(false);

            //first has appended (the head moved past the shared base) but has not yet published; second's append must
            //conflict at the head-CAS rather than producing a second commit on the same base.
            await Assert
                .ThrowsAsync<EditSessionConcurrencyException>(async () => await second.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false))
                .ConfigureAwait(false);

            releaseFirst.SetResult();
            await firstCommit.ConfigureAwait(false);
        }
        finally
        {
            await second.DisposeAsync().ConfigureAwait(false);
            await first.DisposeAsync().ConfigureAwait(false);
        }

        //Exactly one Committed entry — the first session's — reached the journal; the competing commit conflicted.
        List<DatasetJournalEntry> entries = await ReadAllAsync(journal, TestContext.CancellationToken).ConfigureAwait(false);
        int committed = 0;
        foreach(DatasetJournalEntry entry in entries)
        {
            if(entry.EntryKind == EditSessionEntryKind.Committed)
            {
                committed++;
            }
        }

        Assert.AreEqual(1, committed, "Exactly the first commit appended; the competing commit conflicted at the head-CAS.");
    }

    [TestMethod]
    public async Task NetDeltaFoldsToNothingAndWritesNoEntry()
    {
        InMemoryDatasetJournal journal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset dataset = await CreateEmptyAsync(journal, dictionary).ConfigureAwait(false);
        EncodedTriple triple = Triple(dictionary, "s", "p", "o");

        DatasetEditSession session = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            await session.ApplyDeltaAsync(TermId.None, [triple], [], TestContext.CancellationToken).ConfigureAwait(false);
            await session.ApplyDeltaAsync(TermId.None, [], [triple], TestContext.CancellationToken).ConfigureAwait(false);
            await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        List<DatasetJournalEntry> entries = await ReadAllAsync(journal, TestContext.CancellationToken).ConfigureAwait(false);

        //Add-then-remove folds to a no-op: Initial and Started only,
        //no Committed entry, state unchanged.
        Assert.HasCount(2, entries);
        Assert.AreEqual(entries[0].ChildId, dataset.StateId);
        Assert.AreEqual(0, dataset.DefaultGraph.Count);
    }

    [TestMethod]
    public async Task DropAndRecreateFoldsToOneNetTransition()
    {
        InMemoryDatasetJournal journal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset dataset = await CreateEmptyAsync(journal, dictionary).ConfigureAwait(false);
        TermId graph = Mint(dictionary, "graph");
        EncodedTriple kept = Triple(dictionary, "s", "p", "kept");
        EncodedTriple dropped = Triple(dictionary, "s", "p", "dropped");

        //Seed the graph, commit.
        DatasetEditSession seed = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            await seed.ApplyDeltaAsync(graph, [kept, dropped], [], TestContext.CancellationToken).ConfigureAwait(false);
            await seed.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await seed.DisposeAsync().ConfigureAwait(false);
        }

        //Drop the graph and re-create it with partially overlapping
        //content inside one session: the entry must carry one MUTATE
        //transition whose net delta is exactly the difference.
        DatasetEditSession session = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            session.DropGraph(graph);
            await session.ApplyDeltaAsync(graph, [kept, Triple(dictionary, "s", "p", "fresh")], [], TestContext.CancellationToken).ConfigureAwait(false);
            await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        List<DatasetJournalEntry> entries = await ReadAllAsync(journal, TestContext.CancellationToken).ConfigureAwait(false);
        DatasetJournalEntry last = entries[^1];

        Assert.AreEqual(EditSessionEntryKind.Committed, last.EntryKind);
        Assert.HasCount(1, last.Transitions);
        Assert.IsNotNull(last.Transitions[0].ParentRoot);
        Assert.IsNotNull(last.Transitions[0].ChildRoot);
        Assert.HasCount(1, last.Transitions[0].Additions);
        Assert.HasCount(1, last.Transitions[0].Removals);
        Assert.AreEqual(dropped, last.Transitions[0].Removals[0]);
    }

    [TestMethod]
    public async Task ConcurrentCommitConflictsAtTheHeadAndRetrySucceeds()
    {
        InMemoryDatasetJournal journal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset dataset = await CreateEmptyAsync(journal, dictionary).ConfigureAwait(false);

        DatasetEditSession first = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        DatasetEditSession second = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            await first.ApplyDeltaAsync(TermId.None, [Triple(dictionary, "s", "p", "first")], [], TestContext.CancellationToken).ConfigureAwait(false);
            await second.ApplyDeltaAsync(TermId.None, [Triple(dictionary, "s", "p", "second")], [], TestContext.CancellationToken).ConfigureAwait(false);

            await first.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            //The loser's base is no longer the head: the commit MUST
            //refuse, and the dataset must not contain its staging.
            await Assert.ThrowsAsync<EditSessionConcurrencyException>(async () =>
                await second.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        finally
        {
            await second.DisposeAsync().ConfigureAwait(false);
            await first.DisposeAsync().ConfigureAwait(false);
        }

        Assert.AreEqual(1, dataset.DefaultGraph.Count);

        //The retry against the new state lands.
        DatasetEditSession retry = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            await retry.ApplyDeltaAsync(TermId.None, [Triple(dictionary, "s", "p", "second")], [], TestContext.CancellationToken).ConfigureAwait(false);
            await retry.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await retry.DisposeAsync().ConfigureAwait(false);
        }

        Assert.AreEqual(2, dataset.DefaultGraph.Count);
        Assert.AreEqual(dataset.StateId, journal.Head);
    }

    [TestMethod]
    public async Task NamedGraphStoresMintLazilyAndMemoizePerState()
    {
        InMemoryDatasetJournal journal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset dataset = await CreateEmptyAsync(journal, dictionary).ConfigureAwait(false);
        TermId graph = Mint(dictionary, "graph");

        DatasetEditSession seed = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            await seed.ApplyDeltaAsync(graph, [Triple(dictionary, "s", "p", "o")], [], TestContext.CancellationToken).ConfigureAwait(false);
            await seed.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await seed.DisposeAsync().ConfigureAwait(false);
        }

        //Two resolutions of the same graph in the same committed
        //state return the same minted store instance — minting is
        //memoized per state, not per touch.
        Assert.IsTrue(dataset.TryGetNamedGraph(graph, out HypertrieGraphStore? first));
        Assert.IsTrue(dataset.TryGetNamedGraph(graph, out HypertrieGraphStore? second));
        Assert.AreSame(first, second);
        Assert.AreEqual(1, first!.Count);

        //The directory survives commits that do not touch the graph,
        //and the new state mints its own store over the same root.
        DatasetEditSession other = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            await other.ApplyDeltaAsync(TermId.None, [Triple(dictionary, "d", "p", "o")], [], TestContext.CancellationToken).ConfigureAwait(false);
            await other.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await other.DisposeAsync().ConfigureAwait(false);
        }

        Assert.IsTrue(dataset.TryGetNamedGraph(graph, out HypertrieGraphStore? afterCommit));
        Assert.AreEqual(first.Snapshot.Id, afterCommit!.Snapshot.Id);
    }

    [TestMethod]
    public async Task AbandonedSessionLeavesStateUntouchedAndRecordsAbandon()
    {
        InMemoryDatasetJournal journal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset dataset = await CreateEmptyAsync(journal, dictionary).ConfigureAwait(false);
        NodeIdentifier before = dataset.StateId;

        DatasetEditSession session = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        await session.ApplyDeltaAsync(TermId.None, [Triple(dictionary, "s", "p", "o")], [], TestContext.CancellationToken).ConfigureAwait(false);
        await session.DisposeAsync().ConfigureAwait(false);

        List<DatasetJournalEntry> entries = await ReadAllAsync(journal, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(before, dataset.StateId);
        Assert.AreEqual(0, dataset.DefaultGraph.Count);
        Assert.AreEqual(EditSessionEntryKind.Abandoned, entries[^1].EntryKind);
        Assert.AreEqual(before, entries[^1].ChildId);
    }

    [TestMethod]
    public async Task JournalReplayReconstructsTheDatasetExactly()
    {
        InMemoryDatasetJournal journal = new();
        TermDictionary dictionary = new();
        Dictionary<TermId, IReadOnlyList<EncodedTriple>> seededNamed = [];
        seededNamed[Mint(dictionary, "seeded")] = new List<EncodedTriple> { Triple(dictionary, "x", "p", "y") };
        MutableSparqlDataset dataset = await MutableSparqlDataset.CreateAsync(
            dictionary,
            [Triple(dictionary, "d", "p", "1")],
            seededNamed,
            journal.AppendDelegate,
            journal.ReadDelegate,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        TermId graphA = Mint(dictionary, "graphA");
        TermId seeded = Mint(dictionary, "seeded");

        //A few committed sessions: mutate, create, drop, replace.
        DatasetEditSession one = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            await one.ApplyDeltaAsync(TermId.None, [Triple(dictionary, "d", "p", "2")], [Triple(dictionary, "d", "p", "1")], TestContext.CancellationToken).ConfigureAwait(false);
            await one.ApplyDeltaAsync(graphA, [Triple(dictionary, "a", "p", "1"), Triple(dictionary, "a", "p", "2")], [], TestContext.CancellationToken).ConfigureAwait(false);
            await one.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await one.DisposeAsync().ConfigureAwait(false);
        }

        DatasetEditSession two = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            two.DropGraph(seeded);
            await two.ReplaceGraphAsync(graphA, [Triple(dictionary, "a", "p", "2"), Triple(dictionary, "a", "p", "3")], TestContext.CancellationToken).ConfigureAwait(false);
            await two.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await two.DisposeAsync().ConfigureAwait(false);
        }

        //Replay: fold every mutating entry's transitions over plain
        //triple sets.
        Dictionary<TermId, HashSet<EncodedTriple>> replayed = [];
        await foreach(DatasetJournalEntry entry in journal.ReadDelegate(0, TestContext.CancellationToken).ConfigureAwait(false))
        {
            if(entry.EntryKind is not (EditSessionEntryKind.Initial or EditSessionEntryKind.Committed))
            {
                continue;
            }

            foreach(DatasetGraphTransition transition in entry.Transitions)
            {
                if(transition.ChildRoot is null)
                {
                    Assert.IsTrue(replayed.Remove(transition.Graph), "a drop must target an existing graph");

                    continue;
                }

                if(!replayed.TryGetValue(transition.Graph, out HashSet<EncodedTriple>? triples))
                {
                    triples = [];
                    replayed[transition.Graph] = triples;
                }

                foreach(EncodedTriple removal in transition.Removals)
                {
                    triples.Remove(removal);
                }

                foreach(EncodedTriple addition in transition.Additions)
                {
                    triples.Add(addition);
                }
            }
        }

        //The replayed default graph and named graphs must match the
        //live dataset triple-for-triple, and — content addressing —
        //rebuilding each replayed graph must reproduce the very root
        //identifiers the live stores carry.
        Assert.IsTrue(replayed.ContainsKey(TermId.None));
        HypertrieGraphStore replayedDefault = await HypertrieGraphStore.BuildAsync(replayed[TermId.None], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(dataset.DefaultGraph.Snapshot.Id, replayedDefault.Snapshot.Id);

        Assert.IsFalse(replayed.ContainsKey(seeded), "the dropped graph must not survive replay");
        Assert.IsTrue(dataset.TryGetNamedGraph(graphA, out HypertrieGraphStore? liveA));
        HypertrieGraphStore replayedA = await HypertrieGraphStore.BuildAsync(replayed[graphA], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(liveA!.Snapshot.Id, replayedA.Snapshot.Id);
    }

    [TestMethod]
    public async Task ConcurrentWritersAndReadersKeepTheDatasetLinearisable()
    {
        InMemoryDatasetJournal journal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset dataset = await CreateEmptyAsync(journal, dictionary).ConfigureAwait(false);
        const int WriterCount = 4;
        const int CommitsPerWriter = 12;
        TermId graphA = Mint(dictionary, "invariantA");
        TermId graphB = Mint(dictionary, "invariantB");

        //Pre-mint every term the workers will use: TermDictionary
        //writes are the dataset's own concern, not this probe's.
        TermId[][] subjects = new TermId[WriterCount][];
        for(int w = 0; w < WriterCount; w++)
        {
            subjects[w] = new TermId[CommitsPerWriter];
            for(int i = 0; i < CommitsPerWriter; i++)
            {
                subjects[w][i] = Mint(dictionary, $"w{w}i{i}");
            }
        }

        TermId predicate = Mint(dictionary, "p");
        TermId value = Mint(dictionary, "v");

        //Writers: every commit adds the SAME subject to graph A and
        //graph B — the invariant a torn commit would break. The
        //writers start only after the reader has demonstrably begun
        //observing, so the probe always overlaps real commits.
        using CancellationTokenSource readers = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        TaskCompletionSource readerRunning = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> reader = Task.Run(async () =>
        {
            int observed = 0;
            while(!readers.Token.IsCancellationRequested)
            {
                SparqlDataset snapshot = dataset.Snapshot();
                int countA = snapshot.TryGetNamedGraph(graphA, out HypertrieGraphStore storeA) ? storeA.Count : 0;
                int countB = snapshot.TryGetNamedGraph(graphB, out HypertrieGraphStore storeB) ? storeB.Count : 0;
                Assert.AreEqual(countA, countB, "a reader observed half of a multi-graph commit");
                observed++;
                readerRunning.TrySetResult();

                await Task.Yield();
            }

            return observed;
        });

        await readerRunning.Task.ConfigureAwait(false);

        Task[] writers = new Task[WriterCount];
        for(int w = 0; w < WriterCount; w++)
        {
            int writer = w;
            writers[writer] = Task.Run(async () =>
            {
                for(int i = 0; i < CommitsPerWriter; i++)
                {
                    EncodedTriple triple = EncodedTriple.FromEncoded(subjects[writer][i].Encoded, predicate.Encoded, value.Encoded);

                    //Optimistic-concurrency loop: open against the
                    //current state, stage, commit; a head conflict
                    //(at open or commit) rebases by reopening.
                    while(true)
                    {
                        try
                        {
                            DatasetEditSession session = await dataset.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
                            try
                            {
                                await session.ApplyDeltaAsync(graphA, [triple], [], TestContext.CancellationToken).ConfigureAwait(false);
                                await session.ApplyDeltaAsync(graphB, [triple], [], TestContext.CancellationToken).ConfigureAwait(false);
                                await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

                                break;
                            }
                            finally
                            {
                                await session.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                        catch(EditSessionConcurrencyException)
                        {
                            //Another writer won the head; retry on
                            //the new state.
                        }
                    }
                }
            }, TestContext.CancellationToken);
        }

        await Task.WhenAll(writers).ConfigureAwait(false);
        await readers.CancelAsync().ConfigureAwait(false);
        int observations = await reader.ConfigureAwait(false);
        Assert.IsGreaterThan(0, observations);

        //No lost updates: every writer's every triple is present in
        //both graphs.
        Assert.IsTrue(dataset.TryGetNamedGraph(graphA, out HypertrieGraphStore? finalA));
        Assert.IsTrue(dataset.TryGetNamedGraph(graphB, out HypertrieGraphStore? finalB));
        Assert.AreEqual(WriterCount * CommitsPerWriter, finalA!.Count);
        Assert.AreEqual(WriterCount * CommitsPerWriter, finalB!.Count);

        //The journal is one linear chain: every mutating entry's
        //parent is the previous mutating entry's child, and the head
        //is the dataset's state.
        List<DatasetJournalEntry> entries = await ReadAllAsync(journal, TestContext.CancellationToken).ConfigureAwait(false);
        NodeIdentifier expectedParent = NodeIdentifier.Empty;
        int committedCount = 0;
        foreach(DatasetJournalEntry entry in entries)
        {
            if(entry.EntryKind is not (EditSessionEntryKind.Initial or EditSessionEntryKind.Committed))
            {
                continue;
            }

            Assert.AreEqual(expectedParent, entry.ParentId, "the mutating entries must form one linear chain");
            expectedParent = entry.ChildId;
            if(entry.EntryKind == EditSessionEntryKind.Committed)
            {
                committedCount++;
            }
        }

        Assert.AreEqual(WriterCount * CommitsPerWriter, committedCount);
        Assert.AreEqual(expectedParent, dataset.StateId);
        Assert.AreEqual(expectedParent, journal.Head);
    }
}
