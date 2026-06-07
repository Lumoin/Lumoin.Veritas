using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The mutable dataset SPARQL Update operates on: a default graph plus named graphs, every graph a logical
/// <see cref="HypertrieGraphStore"/> over ONE shared <see cref="NodeStore"/> arena and ONE shared
/// <see cref="TermDictionary"/>, with all mutation flowing through a single dataset-scoped journal. A
/// <see cref="DatasetEditSession"/> stages per-graph deltas and commits them as ONE
/// <see cref="DatasetJournalEntry"/> — an atomic, linearisable transition of the whole dataset, however many
/// graphs it touches.
/// </summary>
/// <remarks>
/// <para>
/// <b>Consistency model.</b> The dataset journal is the linearisation point: appends are optimistic
/// (head-CAS over the dataset state identifier), so concurrent sessions conflict at commit and the loser
/// retries against the new state. The published state swaps under a lock only after a successful append,
/// so a reader's <see cref="Snapshot"/> always observes a committed dataset state — never half of a
/// multi-graph update.
/// </para>
/// <para>
/// <b>Memory model.</b> One arena and one pools bundle serve every graph: the per-store fixed cost is paid
/// once, identical subtrees intern once across graphs, and superseded logical stores become sweepable by
/// unreachability (the arena's weak snapshot registry).
/// </para>
/// <para>
/// The read-only <see cref="SparqlDataset"/> a query evaluates against is produced on demand by
/// <see cref="Snapshot"/>; the update executor evaluates a modify's <c>WHERE</c> against the open session's
/// working state instead, so a later operation in the same request sees the earlier ones' effects
/// (SPARQL Update §3.1.3) while readers keep seeing the pre-request state until the request commits.
/// </para>
/// </remarks>
public sealed class MutableSparqlDataset
{
    /// <summary>One committed dataset state: the stores and the content-addressed identifier the journal head points at.</summary>
    /// <param name="DefaultGraph">The default-graph store.</param>
    /// <param name="NamedGraphs">The named-graph stores keyed by graph-name term id. Treated as immutable once published; sessions copy before mutating.</param>
    /// <param name="StateId">The dataset state identifier (<see cref="DatasetStateHashing.ComputeStateId"/>).</param>
    internal sealed record DatasetState(
        HypertrieGraphStore DefaultGraph,
        Dictionary<TermId, HypertrieGraphStore> NamedGraphs,
        NodeIdentifier StateId);

    /// <summary>The shared term dictionary every graph encodes into; append-only, so inserts intern new terms.</summary>
    public TermDictionary Dictionary { get; }

    /// <summary>The shared node arena interning every graph's nodes.</summary>
    internal NodeStore Arena { get; }

    /// <summary>The shared pools bundle serving every build and delta application.</summary>
    internal BuildPools Pools { get; }

    /// <summary>The journal append seam — the dataset-level consensus point.</summary>
    internal DatasetJournalDelegates.AppendDatasetJournalEntryAsync JournalAppend { get; }

    /// <summary>The journal read seam, for replay and audit.</summary>
    public DatasetJournalDelegates.ReadDatasetJournalEntriesAsync JournalRead { get; }

    /// <summary>Guards <see cref="Current"/>; held only for the read or the swap, never across awaits.</summary>
    private Lock StateLock { get; } = new();

    /// <summary>The current committed state. Swapped atomically under <see cref="StateLock"/> on publish.</summary>
    private DatasetState Current { get; set; }

    /// <summary>
    /// The long-lived engine rendezvous for the default graph. Its
    /// derived view persists across query snapshots and evolves
    /// with every committed default-graph delta.
    /// </summary>
    public QueryEngineRendezvous DefaultGraphRendezvous { get; }

    /// <summary>Constructs the dataset over its initial committed state. Called by <see cref="CreateAsync"/>; consumers do not call this directly.</summary>
    /// <param name="dictionary">The shared term dictionary.</param>
    /// <param name="arena">The shared node arena.</param>
    /// <param name="pools">The shared pools bundle.</param>
    /// <param name="journalAppend">The journal append seam.</param>
    /// <param name="journalRead">The journal read seam.</param>
    /// <param name="initial">The initial committed state.</param>
    private MutableSparqlDataset(
        TermDictionary dictionary,
        NodeStore arena,
        BuildPools pools,
        DatasetJournalDelegates.AppendDatasetJournalEntryAsync journalAppend,
        DatasetJournalDelegates.ReadDatasetJournalEntriesAsync journalRead,
        DatasetState initial)
    {
        Dictionary = dictionary;
        Arena = arena;
        Pools = pools;
        JournalAppend = journalAppend;
        JournalRead = journalRead;
        Current = initial;
        DefaultGraphRendezvous = new QueryEngineRendezvous(initial.DefaultGraph, QueryEnginePolicy.Default);
    }

    /// <summary>
    /// Builds a mutable dataset over one shared arena, journals the
    /// initial state as an <see cref="EditSessionEntryKind.Initial"/>
    /// dataset entry, and returns the dataset ready for sessions.
    /// </summary>
    /// <param name="dictionary">The shared term dictionary the triples are encoded against.</param>
    /// <param name="defaultTriples">The default graph's encoded triples; empty for an empty default graph.</param>
    /// <param name="namedGraphs">The named graphs' encoded triples keyed by graph-name term id; <see langword="null"/> for none.</param>
    /// <param name="journalAppend">The dataset journal append seam; <see langword="null"/> (together with <paramref name="journalRead"/>) wires a fresh <see cref="InMemoryDatasetJournal"/>.</param>
    /// <param name="journalRead">The dataset journal read seam; <see langword="null"/> (together with <paramref name="journalAppend"/>) wires a fresh <see cref="InMemoryDatasetJournal"/>.</param>
    /// <param name="cancellationToken">A token that aborts the builds.</param>
    /// <returns>The dataset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> or <paramref name="defaultTriples"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Exactly one of <paramref name="journalAppend"/> and <paramref name="journalRead"/> was supplied.</exception>
    public static async ValueTask<MutableSparqlDataset> CreateAsync(
        TermDictionary dictionary,
        IReadOnlyList<EncodedTriple> defaultTriples,
        IReadOnlyDictionary<TermId, IReadOnlyList<EncodedTriple>>? namedGraphs = null,
        DatasetJournalDelegates.AppendDatasetJournalEntryAsync? journalAppend = null,
        DatasetJournalDelegates.ReadDatasetJournalEntriesAsync? journalRead = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(defaultTriples);

        if(journalAppend is null != journalRead is null)
        {
            throw new ArgumentException("Supply both journal delegates or neither; a dataset journal is one append/read pair.");
        }

        if(journalAppend is null)
        {
            InMemoryDatasetJournal journal = new();
            journalAppend = journal.AppendDelegate;
            journalRead = journal.ReadDelegate;
        }

        //Ownership-transfer pattern (CA2000): the arena is owned
        //here until construction completes; the returned dataset's
        //stores keep it alive afterwards. A failure midway orphans
        //it and the finally block disposes it.
        BuildPools pools = BuildPools.CreateDefault();
        NodeStore? arena = null;

        try
        {
            arena = new NodeStore(VeritasHashing.Default, pools.NodePool);
            arena.AttachToLifetime(pools.NodePool);
            arena.AttachToLifetime(pools.KeyPool);
            arena.AttachToLifetime(pools.ChildPool);
            arena.AttachToLifetime(pools.PermutationPool);

            HypertrieGraphStore defaultGraph = await HypertrieGraphStore.BuildAsync(defaultTriples, arena, pools, cancellationToken).ConfigureAwait(false);
            Dictionary<TermId, HypertrieGraphStore> named = [];
            List<DatasetGraphTransition> transitions =
            [
                //The default graph conceptually always exists: its
                //initial transition starts from the empty root
                //rather than from absence.
                new DatasetGraphTransition(
                    TermId.None,
                    ParentRoot: NodeIdentifier.Empty,
                    ChildRoot: defaultGraph.Snapshot.Id,
                    Additions: [.. CollectAllTriples(defaultGraph)],
                    Removals: []),
            ];

            if(namedGraphs is not null)
            {
                foreach((TermId graphName, IReadOnlyList<EncodedTriple> triples) in namedGraphs)
                {
                    HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, arena, pools, cancellationToken).ConfigureAwait(false);
                    named[graphName] = store;
                    transitions.Add(new DatasetGraphTransition(
                        graphName,
                        ParentRoot: null,
                        ChildRoot: store.Snapshot.Id,
                        Additions: [.. CollectAllTriples(store)],
                        Removals: []));
                }
            }

            NodeIdentifier stateId = ComputeStateId(arena.Hash, defaultGraph, named);
            DatasetJournalEntry initialEntry = DatasetJournalEntry.Initial(arena.Hash, stateId, [.. transitions]);
            await journalAppend(initialEntry, NodeIdentifier.Empty, cancellationToken).ConfigureAwait(false);

            MutableSparqlDataset dataset = new(dictionary, arena, pools, journalAppend, journalRead!, new DatasetState(defaultGraph, named, stateId));
            arena = null;

            return dataset;
        }
        finally
        {
            arena?.Dispose();
        }
    }

    /// <summary>The graph-name term ids of the current committed named graphs.</summary>
    public IReadOnlyCollection<TermId> NamedGraphNames
    {
        get
        {
            lock(StateLock)
            {
                return [.. Current.NamedGraphs.Keys];
            }
        }
    }

    /// <summary>The current committed default-graph store.</summary>
    public HypertrieGraphStore DefaultGraph
    {
        get
        {
            lock(StateLock)
            {
                return Current.DefaultGraph;
            }
        }
    }

    /// <summary>The current committed dataset state identifier — what the journal head points at.</summary>
    public NodeIdentifier StateId
    {
        get
        {
            lock(StateLock)
            {
                return Current.StateId;
            }
        }
    }

    /// <summary>
    /// Produces a read-only <see cref="SparqlDataset"/> over the
    /// current committed state, for building a query engine. The
    /// snapshot is consistent: it can never observe half of a
    /// multi-graph commit. Default-graph patterns route through
    /// the long-lived rendezvous, pinning the snapshot's store.
    /// </summary>
    /// <returns>The dataset snapshot.</returns>
    public SparqlDataset Snapshot()
    {
        DatasetState state = CurrentState();

        return new SparqlDataset(state.DefaultGraph, state.NamedGraphs, DefaultGraphRendezvous);
    }

    /// <summary>Whether the committed state has a named graph for a graph name.</summary>
    /// <param name="name">The graph-name term id.</param>
    /// <returns><see langword="true"/> when the named graph exists.</returns>
    public bool ContainsNamedGraph(TermId name)
    {
        lock(StateLock)
        {
            return Current.NamedGraphs.ContainsKey(name);
        }
    }

    /// <summary>Looks up the committed store for a named graph.</summary>
    /// <param name="name">The graph-name term id.</param>
    /// <param name="store">Receives the store on success.</param>
    /// <returns><see langword="true"/> when the named graph exists.</returns>
    public bool TryGetNamedGraph(TermId name, out HypertrieGraphStore? store)
    {
        lock(StateLock)
        {
            return Current.NamedGraphs.TryGetValue(name, out store);
        }
    }

    /// <summary>
    /// Opens a <see cref="DatasetEditSession"/> against the current
    /// committed state. The session stages per-graph deltas and
    /// commits them as one atomic dataset transition; an
    /// <see cref="EditSessionEntryKind.Started"/> entry records the
    /// open under the journal's optimistic-concurrency contract.
    /// </summary>
    /// <param name="cancellationToken">A token that aborts the open.</param>
    /// <returns>The opened session.</returns>
    /// <exception cref="EditSessionConcurrencyException">The journal head moved between reading the state and recording the open — a commit was in flight; retry.</exception>
    public async ValueTask<DatasetEditSession> OpenSessionAsync(CancellationToken cancellationToken = default)
    {
        SessionId sessionId = SessionId.NewId();
        DatasetState baseState = CurrentState();

        Core.Threading.SharedScope scope = await Arena.EnterSharedScopeAsync(cancellationToken).ConfigureAwait(false);
        bool ownershipTransferred = false;

        try
        {
            DatasetJournalEntry startedEntry = DatasetJournalEntry.Started(baseState.StateId, sessionId);
            await JournalAppend(startedEntry, baseState.StateId, cancellationToken).ConfigureAwait(false);

            DatasetEditSession session = new(this, baseState, scope, sessionId);
            ownershipTransferred = true;

            return session;
        }
        finally
        {
            if(!ownershipTransferred)
            {
                await scope.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Reads the current committed state under the lock.</summary>
    /// <returns>The state.</returns>
    internal DatasetState CurrentState()
    {
        lock(StateLock)
        {
            return Current;
        }
    }

    /// <summary>
    /// Publishes a committed state: swaps it in atomically and
    /// advances the default-graph rendezvous by the commit's net
    /// default-graph delta. Called by
    /// <see cref="DatasetEditSession.CommitAsync"/> after its
    /// journal append succeeded — the append owns the transition,
    /// so the swap cannot race another publisher.
    /// </summary>
    /// <param name="state">The newly committed state.</param>
    /// <param name="defaultGraphChanged">Whether the default graph moved in this commit.</param>
    /// <param name="defaultAdditions">The net default-graph additions.</param>
    /// <param name="defaultRemovals">The net default-graph removals.</param>
    internal void Publish(
        DatasetState state,
        bool defaultGraphChanged,
        IReadOnlyCollection<EncodedTriple> defaultAdditions,
        IReadOnlyCollection<EncodedTriple> defaultRemovals)
    {
        lock(StateLock)
        {
            Current = state;
        }

        if(defaultGraphChanged)
        {
            DefaultGraphRendezvous.Advance(state.DefaultGraph, defaultAdditions, defaultRemovals);
        }
    }

    /// <summary>Computes the dataset state identifier for a default store and named map.</summary>
    /// <param name="hash">The arena's hash function.</param>
    /// <param name="defaultGraph">The default-graph store.</param>
    /// <param name="namedGraphs">The named-graph stores.</param>
    /// <returns>The state identifier.</returns>
    internal static NodeIdentifier ComputeStateId(
        VeritasHash hash,
        HypertrieGraphStore defaultGraph,
        Dictionary<TermId, HypertrieGraphStore> namedGraphs)
    {
        List<KeyValuePair<TermId, NodeIdentifier>> roots = new(namedGraphs.Count);
        foreach((TermId graph, HypertrieGraphStore store) in namedGraphs)
        {
            roots.Add(new KeyValuePair<TermId, NodeIdentifier>(graph, store.Snapshot.Id));
        }

        return DatasetStateHashing.ComputeStateId(hash, defaultGraph.Snapshot.Id, roots);
    }

    /// <summary>Reads all of a store's encoded triples.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The store's encoded triples.</returns>
    internal static List<EncodedTriple> CollectAllTriples(HypertrieGraphStore store)
    {
        List<EncodedTriple> triples = [];
        foreach(EncodedTriple triple in store.Match(TermId.None, TermId.None, TermId.None))
        {
            triples.Add(triple);
        }

        return triples;
    }
}
