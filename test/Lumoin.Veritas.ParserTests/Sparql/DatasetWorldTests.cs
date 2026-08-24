using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
/// The world-DAG contract: a fork is a new dataset over the SHARED
/// term dictionary and node arena with its own linear journal, so
/// worlds evolve independently under per-branch optimistic
/// concurrency, share unchanged content structurally, converge to
/// one content-addressed state identifier when their content
/// converges, and diff exactly by net per-graph transitions. The
/// journals form a DAG through <see cref="EditSessionEntryKind.Forked"/>
/// edges, and replay across a fork edge reconstructs the fork world
/// exactly.
/// </summary>
[TestClass]
internal sealed class DatasetWorldTests
{
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Mints an IRI term in the test namespace.</summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="local">The local name.</param>
    /// <returns>The minted identifier.</returns>
    private static TermId Mint(TermDictionary dictionary, string local)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/worlds/" + local)));
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
    /// <param name="journal">The journal to wire.</param>
    /// <param name="dictionary">The dictionary to encode against.</param>
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

    /// <summary>Reads a store's full triple set.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The triples.</returns>
    private static HashSet<EncodedTriple> Triples(HypertrieGraphStore store)
    {
        return [.. store.Match(TermId.None, TermId.None, TermId.None)];
    }

    /// <summary>Commits one delta against a world's default graph.</summary>
    /// <param name="world">The world.</param>
    /// <param name="additions">The triples to add.</param>
    /// <param name="removals">The triples to remove.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The asynchronous commit.</returns>
    private static async Task CommitDefaultDeltaAsync(
        MutableSparqlDataset world,
        IReadOnlyCollection<EncodedTriple> additions,
        IReadOnlyCollection<EncodedTriple> removals,
        CancellationToken cancellationToken)
    {
        DatasetEditSession session = await world.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await session.ApplyDeltaAsync(TermId.None, additions, removals, cancellationToken).ConfigureAwait(false);
            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task ForkOpensItsJournalWithTheForkEdgeAndSharesTheForkPointState()
    {
        InMemoryDatasetJournal sourceJournal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset source = await CreateEmptyAsync(sourceJournal, dictionary).ConfigureAwait(false);
        await CommitDefaultDeltaAsync(source, [Triple(dictionary, "d", "p", "1")], [], TestContext.CancellationToken).ConfigureAwait(false);
        NodeIdentifier forkPoint = source.StateId;

        InMemoryDatasetJournal forkJournal = new();
        MutableSparqlDataset fork = await source.ForkAsync(forkJournal.AppendDelegate, forkJournal.ReadDelegate, TestContext.CancellationToken).ConfigureAwait(false);

        //The fork sits at the fork point; its journal holds exactly
        //the fork edge, whose parent and child both name the
        //fork-point state and whose append moved the head there.
        Assert.AreEqual(forkPoint, fork.StateId);
        List<DatasetJournalEntry> entries = await ReadAllAsync(forkJournal, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(1, entries);
        Assert.AreEqual(EditSessionEntryKind.Forked, entries[0].EntryKind);
        Assert.AreEqual(forkPoint, entries[0].ParentId);
        Assert.AreEqual(forkPoint, entries[0].ChildId);
        Assert.IsNull(entries[0].SessionId);
        Assert.IsEmpty(entries[0].Transitions);
        Assert.AreEqual(forkPoint, forkJournal.Head);

        //Shared arena, shared content: the two worlds' default
        //graphs are the same content-addressed root.
        Assert.AreEqual(source.DefaultGraph.Snapshot.Id, fork.DefaultGraph.Snapshot.Id);
        Assert.IsTrue(Triples(fork.DefaultGraph).SetEquals(Triples(source.DefaultGraph)));

        //The source journal records nothing about the fork.
        List<DatasetJournalEntry> sourceEntries = await ReadAllAsync(sourceJournal, TestContext.CancellationToken).ConfigureAwait(false);
        foreach(DatasetJournalEntry entry in sourceEntries)
        {
            Assert.AreNotEqual(EditSessionEntryKind.Forked, entry.EntryKind);
        }
    }

    [TestMethod]
    public async Task CommitsAfterTheForkAreIsolatedInBothDirections()
    {
        InMemoryDatasetJournal sourceJournal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset source = await CreateEmptyAsync(sourceJournal, dictionary).ConfigureAwait(false);
        EncodedTriple shared = Triple(dictionary, "d", "p", "shared");
        await CommitDefaultDeltaAsync(source, [shared], [], TestContext.CancellationToken).ConfigureAwait(false);

        InMemoryDatasetJournal forkJournal = new();
        MutableSparqlDataset fork = await source.ForkAsync(forkJournal.AppendDelegate, forkJournal.ReadDelegate, TestContext.CancellationToken).ConfigureAwait(false);

        EncodedTriple sourceOnly = Triple(dictionary, "d", "p", "sourceOnly");
        EncodedTriple forkOnly = Triple(dictionary, "d", "p", "forkOnly");
        await CommitDefaultDeltaAsync(source, [sourceOnly], [], TestContext.CancellationToken).ConfigureAwait(false);
        await CommitDefaultDeltaAsync(fork, [forkOnly], [shared], TestContext.CancellationToken).ConfigureAwait(false);

        //Each world sees exactly its own history: the source keeps
        //the shared triple the fork removed, the fork never sees the
        //source's addition.
        HashSet<EncodedTriple> sourceTriples = Triples(source.DefaultGraph);
        HashSet<EncodedTriple> forkTriples = Triples(fork.DefaultGraph);
        Assert.IsTrue(sourceTriples.SetEquals([shared, sourceOnly]));
        Assert.IsTrue(forkTriples.SetEquals([forkOnly]));
        Assert.AreNotEqual(source.StateId, fork.StateId);

        //Each journal stays a linear chain over its own commits: the
        //fork's mutating entry parents on the fork point, not on the
        //source's newest state.
        List<DatasetJournalEntry> forkEntries = await ReadAllAsync(forkJournal, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.HasCount(3, forkEntries);
        Assert.AreEqual(EditSessionEntryKind.Forked, forkEntries[0].EntryKind);
        Assert.AreEqual(EditSessionEntryKind.Started, forkEntries[1].EntryKind);
        Assert.AreEqual(EditSessionEntryKind.Committed, forkEntries[2].EntryKind);
        Assert.AreEqual(forkEntries[0].ChildId, forkEntries[2].ParentId);
        Assert.AreEqual(fork.StateId, forkJournal.Head);
        Assert.AreEqual(source.StateId, sourceJournal.Head);
    }

    [TestMethod]
    public async Task ConvergentWorldsShareTheContentAddressedState()
    {
        InMemoryDatasetJournal sourceJournal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset source = await CreateEmptyAsync(sourceJournal, dictionary).ConfigureAwait(false);
        await CommitDefaultDeltaAsync(source, [Triple(dictionary, "d", "p", "base")], [], TestContext.CancellationToken).ConfigureAwait(false);

        MutableSparqlDataset fork = await source.ForkAsync(cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        //Both worlds apply the same delta through their own journals.
        EncodedTriple convergent = Triple(dictionary, "d", "p", "convergent");
        await CommitDefaultDeltaAsync(source, [convergent], [], TestContext.CancellationToken).ConfigureAwait(false);
        await CommitDefaultDeltaAsync(fork, [convergent], [], TestContext.CancellationToken).ConfigureAwait(false);

        //Convergent content converges to ONE state identifier and
        //ONE default-graph root — and really is the same triple set,
        //the content-equality backstop behind the fingerprint.
        Assert.AreEqual(source.StateId, fork.StateId);
        Assert.AreEqual(source.DefaultGraph.Snapshot.Id, fork.DefaultGraph.Snapshot.Id);
        Assert.IsTrue(Triples(source.DefaultGraph).SetEquals(Triples(fork.DefaultGraph)));
        Assert.IsEmpty(fork.DiffFrom(source));
    }

    [TestMethod]
    public async Task DiffBetweenDivergedWorldsReturnsTheNetTransitions()
    {
        InMemoryDatasetJournal sourceJournal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset source = await CreateEmptyAsync(sourceJournal, dictionary).ConfigureAwait(false);
        TermId keep = Mint(dictionary, "keep");
        TermId dropped = Mint(dictionary, "dropped");
        TermId fresh = Mint(dictionary, "fresh");
        EncodedTriple defaultBase = Triple(dictionary, "d", "p", "base");
        EncodedTriple keepTriple = Triple(dictionary, "k", "p", "1");
        EncodedTriple droppedTriple = Triple(dictionary, "x", "p", "1");

        DatasetEditSession seed = await source.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            await seed.ApplyDeltaAsync(TermId.None, [defaultBase], [], TestContext.CancellationToken).ConfigureAwait(false);
            await seed.ApplyDeltaAsync(keep, [keepTriple], [], TestContext.CancellationToken).ConfigureAwait(false);
            await seed.ApplyDeltaAsync(dropped, [droppedTriple], [], TestContext.CancellationToken).ConfigureAwait(false);
            await seed.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await seed.DisposeAsync().ConfigureAwait(false);
        }

        MutableSparqlDataset fork = await source.ForkAsync(cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        //Diverge the fork: mutate the default graph, drop one named
        //graph, create another, leave "keep" untouched.
        EncodedTriple defaultNew = Triple(dictionary, "d", "p", "new");
        EncodedTriple freshTriple = Triple(dictionary, "f", "p", "1");
        DatasetEditSession diverge = await fork.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            await diverge.ApplyDeltaAsync(TermId.None, [defaultNew], [defaultBase], TestContext.CancellationToken).ConfigureAwait(false);
            diverge.DropGraph(dropped);
            await diverge.ApplyDeltaAsync(fresh, [freshTriple], [], TestContext.CancellationToken).ConfigureAwait(false);
            await diverge.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await diverge.DisposeAsync().ConfigureAwait(false);
        }

        ImmutableArray<DatasetGraphTransition> diff = fork.DiffFrom(source);

        //Exactly three transitions: the default-graph mutate, the
        //drop, and the create; the untouched graph contributes none.
        Assert.HasCount(3, diff);
        Assert.AreEqual(TermId.None, diff[0].Graph);
        Assert.AreEqual(source.DefaultGraph.Snapshot.Id, diff[0].ParentRoot);
        Assert.AreEqual(fork.DefaultGraph.Snapshot.Id, diff[0].ChildRoot);
        Assert.IsTrue(new HashSet<EncodedTriple>(diff[0].Additions).SetEquals([defaultNew]));
        Assert.IsTrue(new HashSet<EncodedTriple>(diff[0].Removals).SetEquals([defaultBase]));

        DatasetGraphTransition dropTransition = default;
        DatasetGraphTransition createTransition = default;
        bool sawKeep = false;
        for(int i = 1; i < diff.Length; i++)
        {
            if(diff[i].Graph == dropped)
            {
                dropTransition = diff[i];
            }
            else if(diff[i].Graph == fresh)
            {
                createTransition = diff[i];
            }
            else if(diff[i].Graph == keep)
            {
                sawKeep = true;
            }
        }

        Assert.IsFalse(sawKeep);
        Assert.AreEqual(dropped, dropTransition.Graph);
        Assert.IsNull(dropTransition.ChildRoot);
        Assert.IsNotNull(dropTransition.ParentRoot);
        Assert.IsEmpty(dropTransition.Additions);
        Assert.IsEmpty(dropTransition.Removals);
        Assert.AreEqual(fresh, createTransition.Graph);
        Assert.IsNull(createTransition.ParentRoot);
        Assert.IsNotNull(createTransition.ChildRoot);
        Assert.IsTrue(new HashSet<EncodedTriple>(createTransition.Additions).SetEquals([freshTriple]));

        //The reverse diff mirrors: additions and removals swap and
        //the create becomes the drop.
        ImmutableArray<DatasetGraphTransition> reverse = source.DiffFrom(fork);
        Assert.HasCount(3, reverse);
        Assert.IsTrue(new HashSet<EncodedTriple>(reverse[0].Additions).SetEquals([defaultBase]));
        Assert.IsTrue(new HashSet<EncodedTriple>(reverse[0].Removals).SetEquals([defaultNew]));
    }

    [TestMethod]
    public async Task ForkOfAForkFormsADagOfIndependentLinearLogs()
    {
        InMemoryDatasetJournal sourceJournal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset source = await CreateEmptyAsync(sourceJournal, dictionary).ConfigureAwait(false);
        await CommitDefaultDeltaAsync(source, [Triple(dictionary, "d", "p", "1")], [], TestContext.CancellationToken).ConfigureAwait(false);

        InMemoryDatasetJournal childJournal = new();
        MutableSparqlDataset child = await source.ForkAsync(childJournal.AppendDelegate, childJournal.ReadDelegate, TestContext.CancellationToken).ConfigureAwait(false);
        await CommitDefaultDeltaAsync(child, [Triple(dictionary, "d", "p", "child")], [], TestContext.CancellationToken).ConfigureAwait(false);
        NodeIdentifier grandForkPoint = child.StateId;

        InMemoryDatasetJournal grandJournal = new();
        MutableSparqlDataset grandchild = await child.ForkAsync(grandJournal.AppendDelegate, grandJournal.ReadDelegate, TestContext.CancellationToken).ConfigureAwait(false);
        await CommitDefaultDeltaAsync(grandchild, [Triple(dictionary, "d", "p", "grandchild")], [], TestContext.CancellationToken).ConfigureAwait(false);

        //The grandchild's fork edge names the CHILD's state — the
        //DAG chains fork edges across journals.
        List<DatasetJournalEntry> grandEntries = await ReadAllAsync(grandJournal, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(EditSessionEntryKind.Forked, grandEntries[0].EntryKind);
        Assert.AreEqual(grandForkPoint, grandEntries[0].ParentId);

        //Three-way isolation.
        Assert.IsTrue(Triples(source.DefaultGraph).SetEquals([Triple(dictionary, "d", "p", "1")]));
        Assert.IsTrue(Triples(child.DefaultGraph).SetEquals([Triple(dictionary, "d", "p", "1"), Triple(dictionary, "d", "p", "child")]));
        Assert.IsTrue(Triples(grandchild.DefaultGraph).SetEquals([Triple(dictionary, "d", "p", "1"), Triple(dictionary, "d", "p", "child"), Triple(dictionary, "d", "p", "grandchild")]));
    }

    [TestMethod]
    public async Task CommitsToDifferentWorldsNeverConflict()
    {
        InMemoryDatasetJournal sourceJournal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset source = await CreateEmptyAsync(sourceJournal, dictionary).ConfigureAwait(false);
        MutableSparqlDataset fork = await source.ForkAsync(cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        //On ONE world this interleaving — open two sessions, commit
        //both — rejects the second commit at the head-CAS. Across
        //two worlds both commits land, because each world owns its
        //head.
        DatasetEditSession sourceSession = await source.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            DatasetEditSession forkSession = await fork.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
            try
            {
                await sourceSession.ApplyDeltaAsync(TermId.None, [Triple(dictionary, "d", "p", "source")], [], TestContext.CancellationToken).ConfigureAwait(false);
                await forkSession.ApplyDeltaAsync(TermId.None, [Triple(dictionary, "d", "p", "fork")], [], TestContext.CancellationToken).ConfigureAwait(false);
                await sourceSession.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
                await forkSession.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await forkSession.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await sourceSession.DisposeAsync().ConfigureAwait(false);
        }

        Assert.IsTrue(Triples(source.DefaultGraph).SetEquals([Triple(dictionary, "d", "p", "source")]));
        Assert.IsTrue(Triples(fork.DefaultGraph).SetEquals([Triple(dictionary, "d", "p", "fork")]));
    }

    [TestMethod]
    public async Task SweepRetainsEveryLiveWorldsRoots()
    {
        InMemoryDatasetJournal sourceJournal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset source = await CreateEmptyAsync(sourceJournal, dictionary).ConfigureAwait(false);
        TermId graphA = Mint(dictionary, "graphA");

        DatasetEditSession seed = await source.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            await seed.ApplyDeltaAsync(TermId.None, [Triple(dictionary, "d", "p", "1")], [], TestContext.CancellationToken).ConfigureAwait(false);
            await seed.ApplyDeltaAsync(graphA, [Triple(dictionary, "a", "p", "1")], [], TestContext.CancellationToken).ConfigureAwait(false);
            await seed.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await seed.DisposeAsync().ConfigureAwait(false);
        }

        MutableSparqlDataset fork = await source.ForkAsync(cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await CommitDefaultDeltaAsync(source, [Triple(dictionary, "d", "p", "2")], [Triple(dictionary, "d", "p", "1")], TestContext.CancellationToken).ConfigureAwait(false);
        await CommitDefaultDeltaAsync(fork, [Triple(dictionary, "d", "p", "3")], [], TestContext.CancellationToken).ConfigureAwait(false);

        //Capture the live roots BEFORE the sweep. Wrongful eviction
        //is only visible in the intern table — sweep never touches
        //node content behind handles — so the assertions below must
        //probe the table by identifier, not just re-read content.
        NodeIdentifier sourceRoot = source.DefaultGraph.Snapshot.Id;
        NodeIdentifier forkRoot = fork.DefaultGraph.Snapshot.Id;
        Assert.IsTrue(fork.TryGetNamedGraph(graphA, out HypertrieGraphStore? forkA));
        NodeIdentifier forkARoot = forkA!.Snapshot.Id;

        //An explicit sweep must retain every root reachable from a
        //live world — the fork's states are pinned by the fork's own
        //references, not by the source's history.
        _ = await source.Arena.SweepAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(source.Arena.Contains(sourceRoot));
        Assert.IsTrue(source.Arena.Contains(forkRoot));
        Assert.IsTrue(source.Arena.Contains(forkARoot));
        Assert.IsTrue(Triples(source.DefaultGraph).SetEquals([Triple(dictionary, "d", "p", "2")]));
        Assert.IsTrue(Triples(fork.DefaultGraph).SetEquals([Triple(dictionary, "d", "p", "1"), Triple(dictionary, "d", "p", "3")]));
        Assert.IsTrue(Triples(forkA!).SetEquals([Triple(dictionary, "a", "p", "1")]));
    }

    [TestMethod]
    public async Task ReplayAcrossTheForkEdgeReconstructsTheForkWorldExactly()
    {
        InMemoryDatasetJournal sourceJournal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset source = await CreateEmptyAsync(sourceJournal, dictionary).ConfigureAwait(false);
        TermId graphA = Mint(dictionary, "graphA");

        DatasetEditSession one = await source.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            await one.ApplyDeltaAsync(TermId.None, [Triple(dictionary, "d", "p", "1")], [], TestContext.CancellationToken).ConfigureAwait(false);
            await one.ApplyDeltaAsync(graphA, [Triple(dictionary, "a", "p", "1")], [], TestContext.CancellationToken).ConfigureAwait(false);
            await one.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await one.DisposeAsync().ConfigureAwait(false);
        }

        NodeIdentifier forkPoint = source.StateId;
        InMemoryDatasetJournal forkJournal = new();
        MutableSparqlDataset fork = await source.ForkAsync(forkJournal.AppendDelegate, forkJournal.ReadDelegate, TestContext.CancellationToken).ConfigureAwait(false);

        //The source diverges AFTER the fork; its later history must
        //not leak into the fork's replay.
        await CommitDefaultDeltaAsync(source, [Triple(dictionary, "d", "p", "sourceLater")], [], TestContext.CancellationToken).ConfigureAwait(false);

        DatasetEditSession two = await fork.OpenSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            await two.ApplyDeltaAsync(TermId.None, [Triple(dictionary, "d", "p", "fork")], [Triple(dictionary, "d", "p", "1")], TestContext.CancellationToken).ConfigureAwait(false);
            two.DropGraph(graphA);
            await two.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await two.DisposeAsync().ConfigureAwait(false);
        }

        //Replay the DAG path: fold the source journal's mutating
        //entries up to and including the fork point, then the fork
        //journal's mutating entries. The fork edge itself carries no
        //transitions; it names where the source replay must stand
        //when the fork journal takes over.
        Dictionary<TermId, HashSet<EncodedTriple>> replayed = [];
        bool reachedForkPoint = false;
        await foreach(DatasetJournalEntry entry in sourceJournal.ReadDelegate(0, TestContext.CancellationToken).ConfigureAwait(false))
        {
            if(entry.EntryKind is EditSessionEntryKind.Initial or EditSessionEntryKind.Committed)
            {
                ApplyTransitions(replayed, entry.Transitions);

                if(entry.ChildId == forkPoint)
                {
                    reachedForkPoint = true;

                    break;
                }
            }
        }

        Assert.IsTrue(reachedForkPoint, "the fork point must be a state the source journal's replay reaches");

        List<DatasetJournalEntry> forkEntries = await ReadAllAsync(forkJournal, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(EditSessionEntryKind.Forked, forkEntries[0].EntryKind);
        Assert.AreEqual(forkPoint, forkEntries[0].ParentId);
        foreach(DatasetJournalEntry entry in forkEntries)
        {
            if(entry.EntryKind is EditSessionEntryKind.Initial or EditSessionEntryKind.Committed)
            {
                ApplyTransitions(replayed, entry.Transitions);
            }
        }

        //Content addressing is the replay oracle: rebuilding each
        //replayed graph reproduces the fork world's live root
        //identifiers exactly.
        HypertrieGraphStore replayedDefault = await HypertrieGraphStore.BuildAsync(replayed[TermId.None], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(fork.DefaultGraph.Snapshot.Id, replayedDefault.Snapshot.Id);
        Assert.IsFalse(replayed.ContainsKey(graphA), "the graph the fork dropped must not survive its replay");
        Assert.IsFalse(fork.ContainsNamedGraph(graphA));
    }

    /// <summary>Folds one entry's per-graph transitions into replayed triple sets.</summary>
    /// <param name="replayed">The replayed graphs.</param>
    /// <param name="transitions">The transitions to fold.</param>
    private static void ApplyTransitions(Dictionary<TermId, HashSet<EncodedTriple>> replayed, ImmutableArray<DatasetGraphTransition> transitions)
    {
        foreach(DatasetGraphTransition transition in transitions)
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

    [TestMethod]
    public async Task ForkRequiresAnUnbornJournal()
    {
        InMemoryDatasetJournal journal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset dataset = await CreateEmptyAsync(journal, dictionary).ConfigureAwait(false);

        //The dataset's own journal already holds its Initial entry,
        //so the fork edge's empty-head expectation fails.
        await Assert.ThrowsExactlyAsync<EditSessionConcurrencyException>(
            async () => await dataset.ForkAsync(journal.AppendDelegate, journal.ReadDelegate, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ForkRequiresBothJournalDelegatesOrNeither()
    {
        InMemoryDatasetJournal journal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset dataset = await CreateEmptyAsync(journal, dictionary).ConfigureAwait(false);

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await dataset.ForkAsync(journalAppend: new InMemoryDatasetJournal().AppendDelegate, journalRead: null, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task DiffRejectsWorldsFromDifferentFamilies()
    {
        InMemoryDatasetJournal journalA = new();
        TermDictionary dictionaryA = new();
        MutableSparqlDataset datasetA = await CreateEmptyAsync(journalA, dictionaryA).ConfigureAwait(false);

        InMemoryDatasetJournal journalB = new();
        TermDictionary dictionaryB = new();
        MutableSparqlDataset datasetB = await CreateEmptyAsync(journalB, dictionaryB).ConfigureAwait(false);

        Assert.ThrowsExactly<ArgumentException>(() => datasetA.DiffFrom(datasetB));
    }

    [TestMethod]
    public async Task WorldRegistryForksDropsAndEnumeratesByName()
    {
        InMemoryDatasetJournal journal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset main = await CreateEmptyAsync(journal, dictionary).ConfigureAwait(false);
        await CommitDefaultDeltaAsync(main, [Triple(dictionary, "d", "p", "real")], [], TestContext.CancellationToken).ConfigureAwait(false);

        DatasetWorlds worlds = new("main", main);
        WorldFork forked = await worlds.TryForkAsync("main", "what-if", TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(WorldForkOutcome.Forked, forked.Outcome);
        MutableSparqlDataset whatIf = forked.World!;

        Assert.HasCount(2, worlds.Names);
        Assert.IsTrue(worlds.TryGet("what-if", out MutableSparqlDataset? found));
        Assert.AreSame(whatIf, found);

        //The what-if flow: apply a hypothetical to the fork, observe
        //the consequence as a diff, and leave the real world
        //untouched throughout.
        NodeIdentifier realState = main.StateId;
        await CommitDefaultDeltaAsync(whatIf, [Triple(dictionary, "d", "p", "hypothetical")], [], TestContext.CancellationToken).ConfigureAwait(false);
        ImmutableArray<DatasetGraphTransition> consequence = whatIf.DiffFrom(main);
        Assert.HasCount(1, consequence);
        Assert.IsTrue(new HashSet<EncodedTriple>(consequence[0].Additions).SetEquals([Triple(dictionary, "d", "p", "hypothetical")]));
        Assert.AreEqual(realState, main.StateId);

        //Duplicate and missing names answer as outcomes with nothing
        //registered; dropping removes the NAME while existing
        //holders keep the world usable.
        WorldFork duplicate = await worlds.TryForkAsync("main", "what-if", TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(WorldForkOutcome.DuplicateName, duplicate.Outcome);
        Assert.IsNull(duplicate.World);
        WorldFork missing = await worlds.TryForkAsync("missing", "other", TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(WorldForkOutcome.UnknownSource, missing.Outcome);
        Assert.IsNull(missing.World);
        Assert.HasCount(2, worlds.Names);

        Assert.IsTrue(worlds.Drop("what-if"));
        Assert.IsFalse(worlds.Drop("what-if"));
        Assert.IsFalse(worlds.TryGet("what-if", out _));
        Assert.IsTrue(Triples(whatIf.DefaultGraph).SetEquals([Triple(dictionary, "d", "p", "real"), Triple(dictionary, "d", "p", "hypothetical")]));
    }

    [TestMethod]
    public async Task WorldRegistryRecordsForkLineage()
    {
        InMemoryDatasetJournal journal = new();
        TermDictionary dictionary = new();
        MutableSparqlDataset main = await CreateEmptyAsync(journal, dictionary).ConfigureAwait(false);

        DatasetWorlds worlds = new("main", main);
        WorldFork whatIf = await worlds.TryForkAsync("main", "what-if", TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(WorldForkOutcome.Forked, whatIf.Outcome);
        WorldFork grandchild = await worlds.TryForkAsync("what-if", "grandchild", TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(WorldForkOutcome.Forked, grandchild.Outcome);

        //The snapshot carries each world's fork parent: none for the
        //seed, the source name for every fork.
        Dictionary<string, DatasetWorldEntry> described = [];
        foreach(DatasetWorldEntry entry in worlds.Describe())
        {
            described[entry.Name] = entry;
        }

        Assert.HasCount(3, described);
        Assert.IsNull(described["main"].Parent);
        Assert.AreSame(main, described["main"].World);
        Assert.AreEqual("main", described["what-if"].Parent);
        Assert.AreSame(whatIf.World, described["what-if"].World);
        Assert.AreEqual("what-if", described["grandchild"].Parent);

        //Lineage is history, not a live reference: the recorded
        //parent name stands after the parent's name is dropped.
        Assert.IsTrue(worlds.Drop("what-if"));
        Dictionary<string, DatasetWorldEntry> afterDrop = [];
        foreach(DatasetWorldEntry entry in worlds.Describe())
        {
            afterDrop[entry.Name] = entry;
        }

        Assert.HasCount(2, afterDrop);
        Assert.AreEqual("what-if", afterDrop["grandchild"].Parent);
    }
}
