using System;
using System.Collections.Concurrent;
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
/// The mutable dataset SPARQL Update operates on: a default graph plus named graphs over ONE shared
/// <see cref="NodeStore"/> arena and ONE shared <see cref="TermDictionary"/>, with all mutation flowing
/// through a single dataset-scoped journal. A <see cref="DatasetEditSession"/> stages per-graph deltas and
/// commits them as ONE <see cref="DatasetJournalEntry"/> — an atomic, linearisable transition of the whole
/// dataset, however many graphs it touches.
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
/// <b>Memory model.</b> Named graphs live as a GRAPH DIRECTORY — root handle, root identifier, and count
/// per graph — pinned by one <see cref="HypertrieRootSetPin"/> per committed state (the dataset-level
/// snapshot). Store objects mint on demand, memoized per state, so an untouched graph costs one directory
/// entry rather than a store and snapshot object. Superseded states become sweepable by unreachability
/// (the arena's weak registries).
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
    /// <summary>
    /// One committed dataset state: the default-graph store, the
    /// named-graph directory with its root-set pin, the
    /// content-addressed state identifier the journal head points
    /// at, and the per-state memo of on-demand-minted stores.
    /// </summary>
    internal sealed class DatasetState
    {
        /// <summary>The arena the directory's roots live in; minting resolves through it.</summary>
        private NodeStore Arena { get; }

        /// <summary>The default-graph store. The default graph is hot on every query path, so it stays a materialized store.</summary>
        public HypertrieGraphStore DefaultGraph { get; }

        /// <summary>The named-graph directory. Immutable once published; sessions copy before mutating.</summary>
        public Dictionary<TermId, GraphDirectoryEntry> Directory { get; }

        /// <summary>The root-set pin keeping every directory root sweep-reachable for this state's lifetime; <see langword="null"/> when the directory is empty.</summary>
        public HypertrieRootSetPin? Pin { get; }

        /// <summary>The dataset state identifier (<see cref="DatasetStateHashing.ComputeStateId"/>).</summary>
        public NodeIdentifier StateId { get; }

        /// <summary>The stores minted on demand for this state, memoized so a hot graph mints once per commit generation.</summary>
        private ConcurrentDictionary<TermId, HypertrieGraphStore> Minted { get; }

        /// <summary>Constructs a committed state.</summary>
        /// <param name="arena">The shared arena.</param>
        /// <param name="defaultGraph">The default-graph store.</param>
        /// <param name="directory">The named-graph directory; owned by the state from here on.</param>
        /// <param name="pin">The root-set pin over the directory's roots, or <see langword="null"/> for an empty directory.</param>
        /// <param name="stateId">The state identifier.</param>
        /// <param name="minted">Stores already materialized for this state (a committing session's touched stores), or <see langword="null"/> for none.</param>
        public DatasetState(
            NodeStore arena,
            HypertrieGraphStore defaultGraph,
            Dictionary<TermId, GraphDirectoryEntry> directory,
            HypertrieRootSetPin? pin,
            NodeIdentifier stateId,
            IEnumerable<KeyValuePair<TermId, HypertrieGraphStore>>? minted = null)
        {
            Arena = arena;
            DefaultGraph = defaultGraph;
            Directory = directory;
            Pin = pin;
            StateId = stateId;
            Minted = minted is null ? new ConcurrentDictionary<TermId, HypertrieGraphStore>() : new ConcurrentDictionary<TermId, HypertrieGraphStore>(minted);
        }

        /// <summary>
        /// Resolves a named graph to a queryable store, minting one
        /// from the directory entry on first touch. The pin keeps
        /// the root's nodes reachable regardless of when the minted
        /// snapshot registers, so minting is sweep-safe without the
        /// mutation gate.
        /// </summary>
        /// <param name="graph">The graph-name term id.</param>
        /// <returns>The store, or <see langword="null"/> when the state has no such graph.</returns>
        public HypertrieGraphStore? ResolveNamed(TermId graph)
        {
            if(!Directory.TryGetValue(graph, out GraphDirectoryEntry entry))
            {
                return null;
            }

            return Minted.GetOrAdd(graph, static (_, state) =>
            {
                //The store acquires its own reference; the creator
                //reference releases here. A racing duplicate mint
                //loses the GetOrAdd and its store becomes sweepable
                //by unreachability.
                using HypertrieSnapshot snapshot = new(state.Arena, state.Entry.Root, state.Entry.Id);

                return HypertrieGraphStore.FromSnapshot(snapshot, state.Entry.Count);
            }, (Arena, Entry: entry));
        }
    }

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

    /// <summary>
    /// The long-lived rendezvous for the named graphs: one shared
    /// columnar graph set per committed generation, materialised
    /// lazily and dropped when a commit changes any named graph.
    /// Snapshots carry their <see cref="DatasetState"/> as the
    /// generation token; open sessions carry a never-matching token
    /// so mid-request reads always answer on the working stores.
    /// </summary>
    public GraphSetRendezvous NamedGraphRendezvous { get; }

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
        NamedGraphRendezvous = new GraphSetRendezvous(initial, new GraphSetBuildSource(initial).Build, QueryEnginePolicy.Default);
    }

    /// <summary>
    /// A generation's lazy graph-set build source: every named
    /// graph's triples, read through the state's minted views.
    /// </summary>
    /// <param name="state">The committed state the set describes.</param>
    /// <returns>The named graphs' triples keyed by graph id.</returns>
    private static Dictionary<TermId, IEnumerable<EncodedTriple>> CollectGraphSetSource(DatasetState state)
    {
        Dictionary<TermId, IEnumerable<EncodedTriple>> graphs = new(state.Directory.Count);
        foreach(TermId graph in state.Directory.Keys)
        {
            HypertrieGraphStore? store = state.ResolveNamed(graph);
            if(store is not null)
            {
                graphs[graph] = store.Match(TermId.None, TermId.None, TermId.None);
            }
        }

        return graphs;
    }

    /// <summary>
    /// A generation's deferred graph-set build, carrying the committed state as explicit state so the
    /// rendezvous build source is a bound method group rather than a lambda closing over the enclosing state.
    /// </summary>
    /// <param name="state">The committed state the set describes.</param>
    private sealed class GraphSetBuildSource(DatasetState state)
    {
        /// <summary>The committed state the set describes.</summary>
        private DatasetState State { get; } = state;

        /// <summary>Builds the named graphs' triples for the state.</summary>
        /// <returns>The named graphs' triples keyed by graph id.</returns>
        public Dictionary<TermId, IEnumerable<EncodedTriple>> Build()
        {
            return CollectGraphSetSource(State);
        }
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
            Dictionary<TermId, GraphDirectoryEntry> directory = [];
            List<NodeHandle> roots = [];
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
                    //The store object is transient: its root enters
                    //the directory and the pin below keeps the root
                    //reachable; the store itself is dropped.
                    HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, arena, pools, cancellationToken).ConfigureAwait(false);
                    directory[graphName] = new GraphDirectoryEntry(store.Snapshot.Root, store.Snapshot.Id, store.Count);
                    roots.Add(store.Snapshot.Root);
                    transitions.Add(new DatasetGraphTransition(
                        graphName,
                        ParentRoot: null,
                        ChildRoot: store.Snapshot.Id,
                        Additions: [.. CollectAllTriples(store)],
                        Removals: []));
                }
            }

            HypertrieRootSetPin? pin = roots.Count > 0 ? arena.PinRoots(roots) : null;
            NodeIdentifier stateId = ComputeStateId(arena.Hash, defaultGraph, directory);
            DatasetJournalEntry initialEntry = DatasetJournalEntry.Initial(arena.Hash, stateId, [.. transitions]);
            await journalAppend(initialEntry, NodeIdentifier.Empty, cancellationToken).ConfigureAwait(false);

            MutableSparqlDataset dataset = new(dictionary, arena, pools, journalAppend, journalRead!, new DatasetState(arena, defaultGraph, directory, pin, stateId));
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
                return [.. Current.Directory.Keys];
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
    /// multi-graph commit. Named graphs mint on demand from the
    /// state's directory; default-graph patterns route through the
    /// long-lived rendezvous, pinning the snapshot's store.
    /// </summary>
    /// <returns>The dataset snapshot.</returns>
    public SparqlDataset Snapshot()
    {
        DatasetState state = CurrentState();

        return new SparqlDataset(state.DefaultGraph, [.. state.Directory.Keys], state.ResolveNamed, DefaultGraphRendezvous, NamedGraphRendezvous, state);
    }

    /// <summary>Whether the committed state has a named graph for a graph name. Never mints a store.</summary>
    /// <param name="name">The graph-name term id.</param>
    /// <returns><see langword="true"/> when the named graph exists.</returns>
    public bool ContainsNamedGraph(TermId name)
    {
        lock(StateLock)
        {
            return Current.Directory.ContainsKey(name);
        }
    }

    /// <summary>Looks up (minting on first touch) the committed store for a named graph.</summary>
    /// <param name="name">The graph-name term id.</param>
    /// <param name="store">Receives the store on success.</param>
    /// <returns><see langword="true"/> when the named graph exists.</returns>
    public bool TryGetNamedGraph(TermId name, out HypertrieGraphStore? store)
    {
        store = CurrentState().ResolveNamed(name);

        return store is not null;
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

    /// <summary>An observer notified of each committed default-graph delta, or <see langword="null"/> when none is subscribed; set once at wiring time before any commit.</summary>
    private DefaultGraphDeltaObserver? DefaultGraphObserver { get; set; }

    /// <summary>Subscribes an observer to committed default-graph deltas — the seam a replication feed binds its advance to so its reconciliation index tracks the committed default graph by the same delta the query store receives. Set once at wiring time, before commits begin.</summary>
    /// <param name="observer">The observer to notify on each committed default-graph delta.</param>
    /// <exception cref="ArgumentNullException"><paramref name="observer"/> is <see langword="null"/>.</exception>
    public void ObserveDefaultGraphDelta(DefaultGraphDeltaObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        DefaultGraphObserver = observer;
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
        IReadOnlyCollection<EncodedTriple> defaultRemovals,
        bool namedGraphsChanged)
    {
        lock(StateLock)
        {
            Current = state;
        }

        if(defaultGraphChanged)
        {
            DefaultGraphRendezvous.Advance(state.DefaultGraph, defaultAdditions, defaultRemovals);

            //A subscribed replication feed advances its reconciliation index by the same committed delta, so it
            //tracks the query store exactly. The observer runs after the rendezvous advance and outside the state
            //swap lock, matching the rendezvous's own threading; the feed serializes its own state.
            DefaultGraphObserver?.Invoke(defaultAdditions, defaultRemovals, state.StateId);
        }

        if(namedGraphsChanged)
        {
            //The shared graph set described the previous generation;
            //drop it and rebuild lazily from the new state on the
            //next qualifying named-graph join.
            NamedGraphRendezvous.Advance(state, new GraphSetBuildSource(state).Build);
        }
    }

    /// <summary>Computes the dataset state identifier for a default store and a named-graph directory.</summary>
    /// <param name="hash">The arena's hash function.</param>
    /// <param name="defaultGraph">The default-graph store.</param>
    /// <param name="directory">The named-graph directory.</param>
    /// <returns>The state identifier.</returns>
    internal static NodeIdentifier ComputeStateId(
        VeritasHash hash,
        HypertrieGraphStore defaultGraph,
        Dictionary<TermId, GraphDirectoryEntry> directory)
    {
        List<KeyValuePair<TermId, NodeIdentifier>> roots = new(directory.Count);
        foreach((TermId graph, GraphDirectoryEntry entry) in directory)
        {
            roots.Add(new KeyValuePair<TermId, NodeIdentifier>(graph, entry.Id));
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
