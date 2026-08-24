using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Indexing;

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
/// <para>
/// <b>Worlds.</b> A dataset forks (<see cref="ForkAsync"/>) into an independently evolving WORLD: the
/// fork shares this dataset's term dictionary and node arena — unchanged content is physically shared
/// and convergent states deduplicate by content addressing — while carrying its own journal, head,
/// state lock, rendezvous pair, and observer seam, so per-world commits keep the per-branch
/// optimistic-concurrency contract and never conflict across worlds. The per-world linear journals
/// form a DAG through <see cref="EditSessionEntryKind.Forked"/> edges; <see cref="DiffFrom"/> computes
/// the net per-graph transitions between two worlds' committed states.
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

        /// <summary>The default-graph store. The default graph is hot on every query path, so it stays a materialized store. It is the ASSERTED default graph — the system of record the journal, replication, and persistence describe.</summary>
        public HypertrieGraphStore DefaultGraph { get; }

        /// <summary>
        /// The SERVED default graph — asserted ∪ derived when reasoning is wired, and reference-equal to
        /// <see cref="DefaultGraph"/> when it is not (the null path, zero cost). Queries read this store;
        /// everything journal/replication/persistence reads <see cref="DefaultGraph"/>. Like
        /// <see cref="DefaultGraph"/> it is a live store holding its own root reference, and on a reasoned engine
        /// its root also joins <see cref="Pin"/> so an arena sweep never collects the served lineage.
        /// </summary>
        public HypertrieGraphStore ServedDefaultGraph { get; }

        /// <summary>The opaque reasoning-state payload the Database layer supplies through a maintained commit and reads back for provenance; <see langword="null"/> when reasoning is unwired. Swapped atomically with the stores on publish, so the served store and its verdict can never tear.</summary>
        public object? ReasoningState { get; }

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
        /// <param name="servedDefaultGraph">The served default graph (asserted ∪ derived), or <see langword="null"/> to serve the asserted <paramref name="defaultGraph"/> — the unwired, zero-cost path.</param>
        /// <param name="reasoningState">The opaque reasoning-state payload for provenance, or <see langword="null"/> when reasoning is unwired.</param>
        public DatasetState(
            NodeStore arena,
            HypertrieGraphStore defaultGraph,
            Dictionary<TermId, GraphDirectoryEntry> directory,
            HypertrieRootSetPin? pin,
            NodeIdentifier stateId,
            IEnumerable<KeyValuePair<TermId, HypertrieGraphStore>>? minted = null,
            HypertrieGraphStore? servedDefaultGraph = null,
            object? reasoningState = null)
        {
            Arena = arena;
            DefaultGraph = defaultGraph;
            ServedDefaultGraph = servedDefaultGraph ?? defaultGraph;
            ReasoningState = reasoningState;
            Directory = directory;
            Pin = pin;
            StateId = stateId;
            Minted = minted is null ? new ConcurrentDictionary<TermId, HypertrieGraphStore>() : new ConcurrentDictionary<TermId, HypertrieGraphStore>(minted);
        }

        /// <summary>
        /// Produces a fork-time copy of this state serving asserted-only: the served default graph reset to the
        /// asserted <see cref="DefaultGraph"/> and the reasoning payload cleared, per the D-SCOPE bare-fork rule
        /// (a fork carries no maintenance delegate, so it behaves as a clean unwired engine rather than freezing
        /// the parent's overlay). Structural sharing keeps this free for an already-unwired state, which returns
        /// itself unchanged; a wired state copies only the served slot and reuses the directory, pin, and mint memo.
        /// </summary>
        /// <returns>This state when already serving asserted-only, otherwise an asserted-only copy.</returns>
        public DatasetState ForkedAssertedOnly()
        {
            return ReferenceEquals(ServedDefaultGraph, DefaultGraph)
                ? this
                : new DatasetState(Arena, DefaultGraph, Directory, Pin, StateId, Minted, servedDefaultGraph: DefaultGraph, reasoningState: null);
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
    /// <param name="valueIndexes">The composed value-index registry the commit path maintains; <see langword="null"/> uses <see cref="ValueIndexRegistry.Empty"/>.</param>
    private MutableSparqlDataset(
        TermDictionary dictionary,
        NodeStore arena,
        BuildPools pools,
        DatasetJournalDelegates.AppendDatasetJournalEntryAsync journalAppend,
        DatasetJournalDelegates.ReadDatasetJournalEntriesAsync journalRead,
        DatasetState initial,
        ValueIndexRegistry? valueIndexes = null)
    {
        Dictionary = dictionary;
        Arena = arena;
        Pools = pools;
        JournalAppend = journalAppend;
        JournalRead = journalRead;
        Current = initial;
        DefaultGraphRendezvous = new QueryEngineRendezvous(initial.ServedDefaultGraph, QueryEnginePolicy.Default, computeLane: null, initialView: null, deferredStore: null, valueIndexes);
        NamedGraphRendezvous = new GraphSetRendezvous(initial, new GraphSetBuildSource(initial).Build, QueryEnginePolicy.Default, DefaultGraphRendezvous.ValueIndexes);
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
    /// <param name="valueIndexes">The composed value-index registry the commit path maintains; <see langword="null"/> uses <see cref="ValueIndexRegistry.Empty"/>.</param>
    /// <param name="initialCausality">The baseline causality annotation the Initial entry carries when the store is created with a host identity — the Initial entry IS its baseline — or <see langword="null"/> for a store created without identity. The same annotation object seeds the ledger, one source of truth for both.</param>
    /// <param name="cancellationToken">A token that aborts the builds.</param>
    /// <returns>The dataset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> or <paramref name="defaultTriples"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Exactly one of <paramref name="journalAppend"/> and <paramref name="journalRead"/> was supplied.</exception>
    public static ValueTask<MutableSparqlDataset> CreateAsync(
        TermDictionary dictionary,
        IReadOnlyList<EncodedTriple> defaultTriples,
        IReadOnlyDictionary<TermId, IReadOnlyList<EncodedTriple>>? namedGraphs = null,
        DatasetJournalDelegates.AppendDatasetJournalEntryAsync? journalAppend = null,
        DatasetJournalDelegates.ReadDatasetJournalEntriesAsync? journalRead = null,
        ValueIndexRegistry? valueIndexes = null,
        CommitCausality? initialCausality = null,
        CancellationToken cancellationToken = default)
    {
        return CreateCoreAsync(dictionary, defaultTriples, initialServedTriples: null, initialReasoningState: null, namedGraphs, journalAppend, journalRead, valueIndexes, initialCausality, cancellationToken);
    }

    /// <summary>
    /// Builds a REASONED mutable dataset: the same shape as
    /// <see cref="CreateAsync(TermDictionary, IReadOnlyList{EncodedTriple}, IReadOnlyDictionary{TermId, IReadOnlyList{EncodedTriple}}, DatasetJournalDelegates.AppendDatasetJournalEntryAsync, DatasetJournalDelegates.ReadDatasetJournalEntriesAsync, CancellationToken)/>
    /// plus an initial served default graph (asserted ∪ derived) and its opaque reasoning-state payload, so a
    /// reasoned open serves entailments from the first query rather than from the first maintained commit. The
    /// served store is materialised in this dataset's arena over the asserted default graph, so structural
    /// sharing holds. Register the per-commit maintenance seam through <see cref="RegisterMaintenance"/> before
    /// any commit.
    /// </summary>
    /// <param name="dictionary">The shared term dictionary the triples are encoded against.</param>
    /// <param name="defaultTriples">The asserted default graph's encoded triples; empty for an empty default graph.</param>
    /// <param name="initialServedTriples">The initial served default graph's triples (asserted ∪ derived). A superset of the asserted set, so it applies as additions only over the asserted default graph.</param>
    /// <param name="initialReasoningState">The initial opaque reasoning-state payload for provenance, or <see langword="null"/>.</param>
    /// <param name="namedGraphs">The named graphs' encoded triples keyed by graph-name term id; <see langword="null"/> for none.</param>
    /// <param name="journalAppend">The dataset journal append seam; <see langword="null"/> (together with <paramref name="journalRead"/>) wires a fresh <see cref="InMemoryDatasetJournal"/>.</param>
    /// <param name="journalRead">The dataset journal read seam; <see langword="null"/> (together with <paramref name="journalAppend"/>) wires a fresh <see cref="InMemoryDatasetJournal"/>.</param>
    /// <param name="valueIndexes">The composed value-index registry the commit path maintains; <see langword="null"/> uses <see cref="ValueIndexRegistry.Empty"/>.</param>
    /// <param name="initialCausality">The baseline causality annotation the Initial entry carries when the store is created with a host identity — the Initial entry IS its baseline — or <see langword="null"/> for a store created without identity. The same annotation object seeds the ledger, one source of truth for both.</param>
    /// <param name="cancellationToken">A token that aborts the builds.</param>
    /// <returns>The reasoned dataset, serving the initial closure.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Exactly one of <paramref name="journalAppend"/> and <paramref name="journalRead"/> was supplied.</exception>
    public static ValueTask<MutableSparqlDataset> CreateAsync(
        TermDictionary dictionary,
        IReadOnlyList<EncodedTriple> defaultTriples,
        IReadOnlyList<EncodedTriple> initialServedTriples,
        object? initialReasoningState,
        IReadOnlyDictionary<TermId, IReadOnlyList<EncodedTriple>>? namedGraphs = null,
        DatasetJournalDelegates.AppendDatasetJournalEntryAsync? journalAppend = null,
        DatasetJournalDelegates.ReadDatasetJournalEntriesAsync? journalRead = null,
        ValueIndexRegistry? valueIndexes = null,
        CommitCausality? initialCausality = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initialServedTriples);

        return CreateCoreAsync(dictionary, defaultTriples, initialServedTriples, initialReasoningState, namedGraphs, journalAppend, journalRead, valueIndexes, initialCausality, cancellationToken);
    }

    /// <summary>The shared build core of both <c>CreateAsync</c> overloads: builds the arena, the asserted default graph, the named-graph directory, and (when reasoning is wired) the served default graph over the asserted graph's snapshot, then journals the initial state and returns the dataset.</summary>
    /// <param name="dictionary">The shared term dictionary the triples are encoded against.</param>
    /// <param name="defaultTriples">The asserted default graph's encoded triples.</param>
    /// <param name="initialServedTriples">The initial served default graph's triples (asserted ∪ derived), or <see langword="null"/> for an unwired engine serving the asserted graph.</param>
    /// <param name="initialReasoningState">The initial opaque reasoning-state payload, or <see langword="null"/>.</param>
    /// <param name="namedGraphs">The named graphs' triples keyed by graph-name term id, or <see langword="null"/>.</param>
    /// <param name="journalAppend">The dataset journal append seam, or <see langword="null"/> to wire a fresh in-memory journal.</param>
    /// <param name="journalRead">The dataset journal read seam, or <see langword="null"/> to wire a fresh in-memory journal.</param>
    /// <param name="valueIndexes">The composed value-index registry the commit path maintains; <see langword="null"/> uses <see cref="ValueIndexRegistry.Empty"/>.</param>
    /// <param name="initialCausality">The baseline causality annotation the Initial entry carries, or <see langword="null"/> for a store created without identity.</param>
    /// <param name="cancellationToken">A token that aborts the builds.</param>
    /// <returns>The dataset.</returns>
    private static async ValueTask<MutableSparqlDataset> CreateCoreAsync(
        TermDictionary dictionary,
        IReadOnlyList<EncodedTriple> defaultTriples,
        IReadOnlyList<EncodedTriple>? initialServedTriples,
        object? initialReasoningState,
        IReadOnlyDictionary<TermId, IReadOnlyList<EncodedTriple>>? namedGraphs,
        DatasetJournalDelegates.AppendDatasetJournalEntryAsync? journalAppend,
        DatasetJournalDelegates.ReadDatasetJournalEntriesAsync? journalRead,
        ValueIndexRegistry? valueIndexes,
        CommitCausality? initialCausality,
        CancellationToken cancellationToken)
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

            //The served store is the asserted graph plus the closure's derivations. It is materialised over the
            //asserted snapshot (the served set is a superset, so it applies as additions only), sharing structure
            //with the asserted graph, and its root joins the pin so the served lineage is sweep-reachable.
            HypertrieGraphStore servedGraph = defaultGraph;
            if(initialServedTriples is not null)
            {
                ApplyDeltaResult servedDelta = HypertrieOpsPatching.ApplyDelta(defaultGraph.Snapshot, initialServedTriples, [], arena, pools);
                using HypertrieSnapshot servedSnapshot = new(arena, servedDelta.Root, servedDelta.Id);
                servedGraph = HypertrieGraphStore.FromSnapshot(servedSnapshot, defaultGraph.Count + servedDelta.EffectiveAdditions.Count);
                roots.Add(servedGraph.Snapshot.Root);
            }

            HypertrieRootSetPin? pin = roots.Count > 0 ? arena.PinRoots(roots) : null;
            NodeIdentifier stateId = ComputeStateId(arena.Hash, defaultGraph, directory);
            DatasetJournalEntry initialEntry = DatasetJournalEntry.Initial(arena.Hash, stateId, [.. transitions], initialCausality);
            await journalAppend(initialEntry, NodeIdentifier.Empty, cancellationToken).ConfigureAwait(false);

            MutableSparqlDataset dataset = new(
                dictionary,
                arena,
                pools,
                journalAppend,
                journalRead!,
                new DatasetState(arena, defaultGraph, directory, pin, stateId, servedDefaultGraph: servedGraph, reasoningState: initialReasoningState),
                valueIndexes);
            arena = null;

            return dataset;
        }
        finally
        {
            arena?.Dispose();
        }
    }

    /// <summary>
    /// Rebuilds a mutable dataset that a durable journal already records the history of, attaching to that
    /// journal WITHOUT appending: resuming is not a new build, so no <see cref="EditSessionEntryKind.Initial"/>
    /// entry and no transitions are produced. The journal head already names this content; the caller has
    /// reconstructed the head state's per-graph content (from a recovered generation folded forward through the
    /// durable log), and this verifies the rebuild against that head before serving.
    /// </summary>
    /// <remarks>
    /// The rebuilt content-addressed state identifier MUST equal <paramref name="expectedHead"/>: dataset state
    /// identifiers are content addresses, so any divergence in the folded content — a lost commit, a mis-ordered
    /// transition, a term that resolved differently — moves the rebuilt identifier off the head the journal
    /// records, and the mismatch is refused loudly rather than served. This is the recovery oracle the durable
    /// dataset journal is built around.
    /// </remarks>
    /// <param name="dictionary">The shared term dictionary the triples are encoded against — the one the durable journal restored.</param>
    /// <param name="defaultTriples">The recovered default graph's encoded triples; empty for an empty default graph.</param>
    /// <param name="namedGraphs">The recovered named graphs' encoded triples keyed by graph-name term id; <see langword="null"/> for none. An empty triple list keeps the graph present but empty (existence and emptiness are distinct).</param>
    /// <param name="journalAppend">The durable dataset journal append seam; required — a resume attaches to an existing journal.</param>
    /// <param name="journalRead">The durable dataset journal read seam; required.</param>
    /// <param name="expectedHead">The journal head the rebuilt dataset state must reproduce; the recovery integrity oracle.</param>
    /// <param name="valueIndexes">The composed value-index registry the commit path maintains; <see langword="null"/> uses <see cref="ValueIndexRegistry.Empty"/>.</param>
    /// <param name="cancellationToken">A token that aborts the builds.</param>
    /// <returns>The resumed dataset, ready for sessions against its recovered head state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/>, <paramref name="defaultTriples"/>, <paramref name="journalAppend"/>, or <paramref name="journalRead"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The rebuilt dataset state identifier does not equal <paramref name="expectedHead"/> — the recovered content diverges from the journal's head state.</exception>
    public static ValueTask<MutableSparqlDataset> ResumeAsync(
        TermDictionary dictionary,
        IReadOnlyList<EncodedTriple> defaultTriples,
        IReadOnlyDictionary<TermId, IReadOnlyList<EncodedTriple>>? namedGraphs,
        DatasetJournalDelegates.AppendDatasetJournalEntryAsync journalAppend,
        DatasetJournalDelegates.ReadDatasetJournalEntriesAsync journalRead,
        NodeIdentifier expectedHead,
        ValueIndexRegistry? valueIndexes = null,
        CancellationToken cancellationToken = default)
    {
        return ResumeCoreAsync(dictionary, defaultTriples, initialServedTriples: null, initialReasoningState: null, namedGraphs, journalAppend, journalRead, expectedHead, valueIndexes, cancellationToken);
    }

    /// <summary>
    /// Resumes a REASONED mutable dataset from a durable journal: the same recovery shape as
    /// <see cref="ResumeAsync(TermDictionary, IReadOnlyList{EncodedTriple}, IReadOnlyDictionary{TermId, IReadOnlyList{EncodedTriple}}, DatasetJournalDelegates.AppendDatasetJournalEntryAsync, DatasetJournalDelegates.ReadDatasetJournalEntriesAsync, NodeIdentifier, CancellationToken)/>
    /// plus the served default graph (asserted ∪ derived) rebuilt at open, so a reasoned reopen serves
    /// entailments from the first query, not from the first maintained commit. The head oracle verifies the
    /// ASSERTED content only; the served store is materialised over the verified asserted graph. Register the
    /// per-commit maintenance seam through <see cref="RegisterMaintenance"/> before any commit.
    /// </summary>
    /// <param name="dictionary">The shared term dictionary the triples are encoded against.</param>
    /// <param name="defaultTriples">The recovered asserted default graph's encoded triples.</param>
    /// <param name="initialServedTriples">The initial served default graph's triples (asserted ∪ derived), rebuilt from the recovered asserted base. A superset of the asserted set.</param>
    /// <param name="initialReasoningState">The initial opaque reasoning-state payload for provenance, or <see langword="null"/>.</param>
    /// <param name="namedGraphs">The recovered named graphs' encoded triples keyed by graph-name term id; <see langword="null"/> for none.</param>
    /// <param name="journalAppend">The durable dataset journal append seam; required.</param>
    /// <param name="journalRead">The durable dataset journal read seam; required.</param>
    /// <param name="expectedHead">The journal head the rebuilt ASSERTED dataset state must reproduce; the recovery integrity oracle.</param>
    /// <param name="valueIndexes">The composed value-index registry the commit path maintains; <see langword="null"/> uses <see cref="ValueIndexRegistry.Empty"/>.</param>
    /// <param name="cancellationToken">A token that aborts the builds.</param>
    /// <returns>The resumed reasoned dataset, serving the recovered closure.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The rebuilt asserted dataset state identifier does not equal <paramref name="expectedHead"/>.</exception>
    public static ValueTask<MutableSparqlDataset> ResumeAsync(
        TermDictionary dictionary,
        IReadOnlyList<EncodedTriple> defaultTriples,
        IReadOnlyList<EncodedTriple> initialServedTriples,
        object? initialReasoningState,
        IReadOnlyDictionary<TermId, IReadOnlyList<EncodedTriple>>? namedGraphs,
        DatasetJournalDelegates.AppendDatasetJournalEntryAsync journalAppend,
        DatasetJournalDelegates.ReadDatasetJournalEntriesAsync journalRead,
        NodeIdentifier expectedHead,
        ValueIndexRegistry? valueIndexes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initialServedTriples);

        return ResumeCoreAsync(dictionary, defaultTriples, initialServedTriples, initialReasoningState, namedGraphs, journalAppend, journalRead, expectedHead, valueIndexes, cancellationToken);
    }

    /// <summary>The shared recovery core of both <c>ResumeAsync</c> overloads: rebuilds the asserted content, verifies it against the journal head, and (when reasoning is wired) materialises the served default graph over the verified asserted graph.</summary>
    /// <param name="dictionary">The shared term dictionary the triples are encoded against.</param>
    /// <param name="defaultTriples">The recovered asserted default graph's encoded triples.</param>
    /// <param name="initialServedTriples">The initial served default graph's triples (asserted ∪ derived), or <see langword="null"/> for an unwired resume.</param>
    /// <param name="initialReasoningState">The initial opaque reasoning-state payload, or <see langword="null"/>.</param>
    /// <param name="namedGraphs">The recovered named graphs' triples keyed by graph-name term id, or <see langword="null"/>.</param>
    /// <param name="journalAppend">The durable dataset journal append seam.</param>
    /// <param name="journalRead">The durable dataset journal read seam.</param>
    /// <param name="expectedHead">The journal head the rebuilt asserted state must reproduce.</param>
    /// <param name="valueIndexes">The composed value-index registry the commit path maintains; <see langword="null"/> uses <see cref="ValueIndexRegistry.Empty"/>.</param>
    /// <param name="cancellationToken">A token that aborts the builds.</param>
    /// <returns>The resumed dataset.</returns>
    private static async ValueTask<MutableSparqlDataset> ResumeCoreAsync(
        TermDictionary dictionary,
        IReadOnlyList<EncodedTriple> defaultTriples,
        IReadOnlyList<EncodedTriple>? initialServedTriples,
        object? initialReasoningState,
        IReadOnlyDictionary<TermId, IReadOnlyList<EncodedTriple>>? namedGraphs,
        DatasetJournalDelegates.AppendDatasetJournalEntryAsync journalAppend,
        DatasetJournalDelegates.ReadDatasetJournalEntriesAsync journalRead,
        NodeIdentifier expectedHead,
        ValueIndexRegistry? valueIndexes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(defaultTriples);
        ArgumentNullException.ThrowIfNull(journalAppend);
        ArgumentNullException.ThrowIfNull(journalRead);

        //Ownership-transfer pattern (CA2000), identical to CreateAsync: the arena is owned here until the returned
        //dataset's stores keep it alive; a failure midway — including the head-mismatch refusal — orphans it and
        //the finally disposes it.
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

            if(namedGraphs is not null)
            {
                foreach((TermId graphName, IReadOnlyList<EncodedTriple> triples) in namedGraphs)
                {
                    //The store object is transient: its root enters the directory and the pin below keeps the root
                    //reachable; the store itself is dropped. An empty graph enters the directory at the empty root,
                    //so a recovered empty-but-present graph stays present.
                    HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, arena, pools, cancellationToken).ConfigureAwait(false);
                    directory[graphName] = new GraphDirectoryEntry(store.Snapshot.Root, store.Snapshot.Id, store.Count);
                    roots.Add(store.Snapshot.Root);
                }
            }

            NodeIdentifier stateId = ComputeStateId(arena.Hash, defaultGraph, directory);
            if(stateId != expectedHead)
            {
                throw new InvalidDataException(
                    $"The rebuilt dataset state does not match the journal head: the recovery rebuilt state {stateId.Value:X16} but the durable journal head names {expectedHead.Value:X16}; the recovered content diverges from the journal's history.");
            }

            //The head oracle covers the ASSERTED content; the served store is rebuilt over the verified asserted
            //graph, sharing structure with it, and its root joins the pin.
            HypertrieGraphStore servedGraph = defaultGraph;
            if(initialServedTriples is not null)
            {
                ApplyDeltaResult servedDelta = HypertrieOpsPatching.ApplyDelta(defaultGraph.Snapshot, initialServedTriples, [], arena, pools);
                using HypertrieSnapshot servedSnapshot = new(arena, servedDelta.Root, servedDelta.Id);
                servedGraph = HypertrieGraphStore.FromSnapshot(servedSnapshot, defaultGraph.Count + servedDelta.EffectiveAdditions.Count);
                roots.Add(servedGraph.Snapshot.Root);
            }

            HypertrieRootSetPin? pin = roots.Count > 0 ? arena.PinRoots(roots) : null;

            MutableSparqlDataset dataset = new(
                dictionary,
                arena,
                pools,
                journalAppend,
                journalRead,
                new DatasetState(arena, defaultGraph, directory, pin, stateId, servedDefaultGraph: servedGraph, reasoningState: initialReasoningState),
                valueIndexes);
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

        //Queries read the SERVED default graph (== the asserted store when reasoning is unwired), while the
        //journal, replication, and persistence surfaces read the asserted store.
        return new SparqlDataset(state.ServedDefaultGraph, [.. state.Directory.Keys], state.ResolveNamed, DefaultGraphRendezvous, NamedGraphRendezvous, state);
    }

    /// <summary>
    /// Captures the current committed dataset state as one self-consistent snapshot for durable persistence: the
    /// default graph's triples, every named graph's triples keyed by graph-name term id, and the content-addressed
    /// state identifier all three derive from. The whole capture reads ONE committed <see cref="DatasetState"/> -
    /// an immutable, content-addressed object - so the default graph and the named graphs are always drawn from the
    /// same committed state. A commit racing the capture publishes a new state object and leaves the captured one
    /// untouched, so a persist can never freeze a cross-graph mixture that no commit ever produced, however many
    /// commits race it.
    /// </summary>
    /// <returns>The snapshot: the default-graph triples, the named graphs keyed by graph-name term id, and the state identifier the three share.</returns>
    public DatasetPersistCapture CaptureCommittedState()
    {
        DatasetState state = CurrentState();

        EncodedTriple[] defaultTriples = [.. CollectAllTriples(state.DefaultGraph)];

        List<(TermId GraphName, ReadOnlyMemory<EncodedTriple> Triples)> namedGraphs = new(state.Directory.Count);
        foreach(TermId graph in state.Directory.Keys)
        {
            HypertrieGraphStore? store = state.ResolveNamed(graph);
            if(store is null)
            {
                continue;
            }

            EncodedTriple[] triples = [.. CollectAllTriples(store)];
            namedGraphs.Add((graph, triples));
        }

        return new DatasetPersistCapture(defaultTriples, namedGraphs, state.StateId);
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

    /// <summary>
    /// Forks a new, independently evolving dataset — a WORLD — from
    /// the current committed state. The fork shares this dataset's
    /// term dictionary, node arena, and pools; it holds the SAME
    /// committed state object as its initial state, so nothing is
    /// copied, the fork-point roots stay sweep-reachable for as long
    /// as either world can serve them, and only divergence
    /// allocates. The fork carries its own journal (its own head —
    /// per-world commits never conflict across worlds), its own
    /// rendezvous pair, and its own observer seam. Its journal opens
    /// with an <see cref="EditSessionEntryKind.Forked"/> entry whose
    /// parent and child both name the fork-point state — the
    /// cross-journal edge replay follows to reconstruct the world.
    /// </summary>
    /// <param name="journalAppend">The fork's journal append seam; <see langword="null"/> (together with <paramref name="journalRead"/>) wires a fresh <see cref="InMemoryDatasetJournal"/>.</param>
    /// <param name="journalRead">The fork's journal read seam; <see langword="null"/> (together with <paramref name="journalAppend"/>) wires a fresh <see cref="InMemoryDatasetJournal"/>.</param>
    /// <param name="cancellationToken">A token that aborts the fork.</param>
    /// <returns>The forked dataset.</returns>
    /// <exception cref="ArgumentException">Exactly one of <paramref name="journalAppend"/> and <paramref name="journalRead"/> was supplied.</exception>
    /// <exception cref="EditSessionConcurrencyException">The supplied journal already has entries — the fork entry is only appendable to an unborn journal, whose head is the empty sentinel no real dataset state identifier equals.</exception>
    public async ValueTask<MutableSparqlDataset> ForkAsync(
        DatasetJournalDelegates.AppendDatasetJournalEntryAsync? journalAppend = null,
        DatasetJournalDelegates.ReadDatasetJournalEntriesAsync? journalRead = null,
        CancellationToken cancellationToken = default)
    {
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

        DatasetState forkPoint = CurrentState();

        DatasetJournalEntry forkedEntry = DatasetJournalEntry.Forked(forkPoint.StateId);
        await journalAppend(forkedEntry, NodeIdentifier.Empty, cancellationToken).ConfigureAwait(false);

        //A bare fork carries no maintenance delegate, so it is a clean unwired engine: its initial state serves the
        //asserted default graph, never the parent's frozen overlay (D-SCOPE). Structural sharing keeps this free.
        return new MutableSparqlDataset(Dictionary, Arena, Pools, journalAppend, journalRead!, forkPoint.ForkedAssertedOnly(), DefaultGraphRendezvous.ValueIndexes);
    }

    /// <summary>
    /// Computes the net per-graph transitions that carry
    /// <paramref name="baseline"/>'s current committed state to this
    /// dataset's current committed state — the same shape a
    /// committed journal entry records: a mutate transition where
    /// both worlds hold the graph at different roots, a create where
    /// only this world holds it (its additions are the graph's full
    /// content), a drop where only the baseline holds it.
    /// Content-based over the shared arena's stores, so the result
    /// is exact regardless of how the two histories diverged; each
    /// world's state is read atomically, but the pair is two reads —
    /// commits racing the diff land in one world's state or neither.
    /// </summary>
    /// <param name="baseline">The world to diff against.</param>
    /// <returns>The transitions, the default graph first and named graphs in ascending graph-id order; empty when the states are identical.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="baseline"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="baseline"/> does not share this dataset's term dictionary — term identifiers are dictionary-relative, so cross-family triples are not comparable.</exception>
    public ImmutableArray<DatasetGraphTransition> DiffFrom(MutableSparqlDataset baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        if(!ReferenceEquals(Dictionary, baseline.Dictionary))
        {
            throw new ArgumentException("Only worlds sharing one term dictionary can be diffed; term identifiers are dictionary-relative.", nameof(baseline));
        }

        DatasetState target = CurrentState();
        DatasetState source = baseline.CurrentState();

        //Equal state identifiers name equal content — the same
        //fingerprint trust every head comparison at this seam
        //relies on.
        if(target.StateId == source.StateId)
        {
            return [];
        }

        List<DatasetGraphTransition> transitions = [];

        if(target.DefaultGraph.Snapshot.Id != source.DefaultGraph.Snapshot.Id)
        {
            (ImmutableArray<EncodedTriple> additions, ImmutableArray<EncodedTriple> removals) = DiffStores(source.DefaultGraph, target.DefaultGraph);
            transitions.Add(new DatasetGraphTransition(
                TermId.None,
                ParentRoot: source.DefaultGraph.Snapshot.Id,
                ChildRoot: target.DefaultGraph.Snapshot.Id,
                Additions: additions,
                Removals: removals));
        }

        SortedSet<uint> graphIds = [];
        foreach(TermId graph in source.Directory.Keys)
        {
            graphIds.Add(graph.Encoded);
        }

        foreach(TermId graph in target.Directory.Keys)
        {
            graphIds.Add(graph.Encoded);
        }

        foreach(uint encoded in graphIds)
        {
            TermId graph = TermId.FromEncoded(encoded);
            bool inSource = source.Directory.TryGetValue(graph, out GraphDirectoryEntry sourceEntry);
            bool inTarget = target.Directory.TryGetValue(graph, out GraphDirectoryEntry targetEntry);

            if(inSource && inTarget)
            {
                if(sourceEntry.Id == targetEntry.Id)
                {
                    continue;
                }

                (ImmutableArray<EncodedTriple> additions, ImmutableArray<EncodedTriple> removals) = DiffStores(source.ResolveNamed(graph)!, target.ResolveNamed(graph)!);
                transitions.Add(new DatasetGraphTransition(
                    graph,
                    ParentRoot: sourceEntry.Id,
                    ChildRoot: targetEntry.Id,
                    Additions: additions,
                    Removals: removals));

                continue;
            }

            if(inSource)
            {
                //A drop discards the graph wholesale; the parent root
                //identifies what was discarded and the deltas stay
                //empty — the committed drop shape.
                transitions.Add(new DatasetGraphTransition(
                    graph,
                    ParentRoot: sourceEntry.Id,
                    ChildRoot: null,
                    Additions: [],
                    Removals: []));

                continue;
            }

            transitions.Add(new DatasetGraphTransition(
                graph,
                ParentRoot: null,
                ChildRoot: targetEntry.Id,
                Additions: [.. CollectAllTriples(target.ResolveNamed(graph)!)],
                Removals: []));
        }

        return [.. transitions];
    }

    /// <summary>Computes the triple-set difference between two stores over one arena.</summary>
    /// <param name="source">The baseline store.</param>
    /// <param name="target">The store the additions belong to.</param>
    /// <returns>The triples only in <paramref name="target"/> and the triples only in <paramref name="source"/>.</returns>
    private static (ImmutableArray<EncodedTriple> Additions, ImmutableArray<EncodedTriple> Removals) DiffStores(
        HypertrieGraphStore source,
        HypertrieGraphStore target)
    {
        HashSet<EncodedTriple> sourceTriples = [.. source.Match(TermId.None, TermId.None, TermId.None)];
        List<EncodedTriple> additions = [];

        //Removing shared triples as the target streams past leaves
        //exactly the source-only triples behind — one set, one pass
        //over each side.
        foreach(EncodedTriple triple in target.Match(TermId.None, TermId.None, TermId.None))
        {
            if(!sourceTriples.Remove(triple))
            {
                additions.Add(triple);
            }
        }

        return ([.. additions], [.. sourceTriples]);
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
    /// Reads the opaque reasoning-state payload of the CURRENT committed state — the per-landed-generation
    /// provenance a reasoned engine's facade surfaces. It is swapped atomically with the served store on publish,
    /// so it never tears against the served content: a refused or conflicted commit leaves it at the last landed
    /// generation. <see langword="null"/> when reasoning is unwired.
    /// </summary>
    /// <returns>The current committed state's opaque reasoning-state payload, or <see langword="null"/> when reasoning is unwired.</returns>
    public object? CurrentReasoningState()
    {
        return CurrentState().ReasoningState;
    }

    /// <summary>An observer notified of each committed default-graph delta, or <see langword="null"/> when none is subscribed; set once at wiring time before any commit.</summary>
    private DefaultGraphDeltaObserver? DefaultGraphObserver { get; set; }

    /// <summary>Subscribes an observer to committed default-graph deltas — the seam a replication feed binds its advance to so its reconciliation index tracks the committed default graph by the same delta the query store receives. Set once at wiring time, before commits begin; a second subscription is refused, so a subscriber can never be silently displaced — fan-out composes one observer at the wiring site.</summary>
    /// <param name="observer">The observer to notify on each committed default-graph delta.</param>
    /// <exception cref="ArgumentNullException"><paramref name="observer"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">An observer was already subscribed — the seam is set once.</exception>
    public void ObserveDefaultGraphDelta(DefaultGraphDeltaObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        if(DefaultGraphObserver is not null)
        {
            throw new InvalidOperationException("The default-graph delta observer seam is set once at wiring time and is already subscribed.");
        }

        DefaultGraphObserver = observer;
    }

    /// <summary>The causality-annotation builder of a remove-aware store, or <see langword="null"/> when the store is not remove-aware; set once at wiring time before any commit. A non-<see langword="null"/> value makes every locally-authored commit build its annotation between computing its transitions and the linearising journal append.</summary>
    internal BuildCommitCausalityDelegate? CausalityBuilder { get; private set; }

    /// <summary>
    /// The causality commit gate of a remove-aware store, created with the builder, or <see langword="null"/>
    /// on an add-only store (whose commit path stays byte-identical). The journal head compare-and-swap orders
    /// commits by the head VALUE, and a causality-only commit — child state equal to its parent — leaves that
    /// value unchanged, so the CAS alone can neither order such a commit's publish against a competitor's nor
    /// certify an annotation built while one was in flight. The gate restores both: on an unwired store every
    /// commit holds it from its annotation build (or adopted-plan basis) through its publish, so fold order is
    /// journal order and every annotation that reaches an append was built against the ledger state its
    /// publish extends. On a reasoned store the maintenance mutex already spans that stretch for every commit,
    /// so only the adopt write-back's plan-to-commit scope rides this gate there. Lock ordering: this gate is
    /// OUTERMOST — the maintenance mutex, <see cref="StateLock"/>, and the ledger's own gate all nest inside.
    /// </summary>
    internal SemaphoreSlim? CausalityCommitGate { get; private set; }

    /// <summary>Wires the causality-annotation builder of a remove-aware store — the dotted commit ledger's local-mint seam — and creates the causality commit gate. Set once at wiring time, before commits begin, mirroring <see cref="ObserveDefaultGraphDelta"/>.</summary>
    /// <param name="builder">The builder every locally-authored commit's annotation comes from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A builder was already registered — the seam is set once.</exception>
    public void RegisterCausalityBuilder(BuildCommitCausalityDelegate builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if(CausalityBuilder is not null)
        {
            throw new InvalidOperationException("The causality-builder seam is set once at wiring time and is already registered.");
        }

        CausalityBuilder = builder;
        CausalityCommitGate = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Enters the causality commit gate and returns the held scope — the adopt write-back's fence: it holds
    /// the scope across its whole plan-apply-commit attempt so the adopt plan's basis stays the live ledger
    /// state through the commit's publish, which the head compare-and-swap cannot guarantee for causality-only
    /// commits. The session commit it makes inside the scope passes its adopted annotation and does not
    /// re-enter the gate.
    /// </summary>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>The held scope; dispose to release.</returns>
    /// <exception cref="InvalidOperationException">The dataset is not remove-aware — no causality builder is registered.</exception>
    public async ValueTask<CausalityCommitScope> EnterCausalityCommitScopeAsync(CancellationToken cancellationToken = default)
    {
        if(CausalityCommitGate is not { } gate)
        {
            throw new InvalidOperationException("The causality commit gate exists only on a remove-aware dataset; register the causality builder first.");
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        return new CausalityCommitScope(gate);
    }

    /// <summary>The per-commit closure-maintenance seam of a reasoned mutable engine, or <see langword="null"/> when reasoning is unwired; set once at wiring time before any commit. A non-<see langword="null"/> value makes the dataset reasoning-wired: every <see cref="DatasetEditSession.CommitAsync"/> acquires <see cref="MaintenanceMutex"/> and maintains the served store.</summary>
    internal ClosureMaintenanceDelegate? MaintenanceDelegate { get; private set; }

    /// <summary>The single commit-outcome seam fired once per maintenance-delegate invocation, or <see langword="null"/> when reasoning is unwired.</summary>
    internal ClosureMaintenanceOutcomeDelegate? MaintenanceOutcome { get; private set; }

    /// <summary>
    /// The reasoned engine's maintenance mutex, or <see langword="null"/> when reasoning is unwired. A commit on a
    /// reasoned engine holds it across the whole tail — the staleness pre-check, the maintenance delegate, the
    /// served-snapshot build, the linearising journal append, <see cref="Publish"/>, and the outcome latch — so it
    /// serializes Apply-vs-COMMIT, not merely Apply-vs-Apply. Lock ordering is maintenance mutex OUTER,
    /// <see cref="StateLock"/> INNER: <see cref="Publish"/> takes <see cref="StateLock"/> while the mutex is held,
    /// and nothing takes the mutex under <see cref="StateLock"/>, so no deadlock is possible. Used only through
    /// <see cref="SemaphoreSlim.WaitAsync(CancellationToken)"/>/<see cref="SemaphoreSlim.Release()"/> (never the
    /// wait handle), so it allocates no OS handle and its lifetime rides with the dataset.
    /// </summary>
    internal SemaphoreSlim? MaintenanceMutex { get; private set; }

    /// <summary>
    /// Wires the per-commit closure-maintenance seam of a reasoned mutable engine — the maintenance delegate and
    /// its single outcome seam — and creates the maintenance mutex. Set once at wiring time, before commits begin,
    /// mirroring <see cref="ObserveDefaultGraphDelta"/>. The initial served store and reasoning-state payload are
    /// supplied at construction (the reasoned <c>CreateAsync</c>/<c>ResumeAsync</c> overloads), so a reasoned open
    /// serves entailments from the first query.
    /// </summary>
    /// <param name="maintenanceDelegate">The per-commit maintenance delegate.</param>
    /// <param name="maintenanceOutcome">The single commit-outcome seam.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Maintenance was already registered — the seam is set once.</exception>
    public void RegisterMaintenance(ClosureMaintenanceDelegate maintenanceDelegate, ClosureMaintenanceOutcomeDelegate maintenanceOutcome)
    {
        ArgumentNullException.ThrowIfNull(maintenanceDelegate);
        ArgumentNullException.ThrowIfNull(maintenanceOutcome);

        if(MaintenanceDelegate is not null)
        {
            throw new InvalidOperationException("The maintenance seam is set once at wiring time and is already registered.");
        }

        MaintenanceDelegate = maintenanceDelegate;
        MaintenanceOutcome = maintenanceOutcome;
        MaintenanceMutex = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Publishes a committed state: swaps it in atomically and
    /// advances the default-graph rendezvous by the commit's net
    /// default-graph delta. Called by
    /// <see cref="DatasetEditSession.CommitAsync"/> after its
    /// journal append succeeded — the append owns the transition,
    /// so the swap cannot race another publisher.
    /// </summary>
    /// <param name="state">The newly committed state; carries the served store and the reasoning payload, swapped atomically with the asserted store.</param>
    /// <param name="defaultGraphChanged">Whether the default graph moved in this commit.</param>
    /// <param name="defaultAdditions">The net ASSERTED default-graph additions — the delta the replication observer keeps.</param>
    /// <param name="defaultRemovals">The net ASSERTED default-graph removals — the delta the replication observer keeps.</param>
    /// <param name="namedGraphsChanged">Whether any named graph moved in this commit.</param>
    /// <param name="servedStore">The SERVED default graph the query rendezvous advances onto; equal to <paramref name="state"/>'s asserted store when reasoning is unwired.</param>
    /// <param name="servedAdditions">The net SERVED default-graph additions — the FULL closure delta the query rendezvous folds; equal to <paramref name="defaultAdditions"/> when reasoning is unwired.</param>
    /// <param name="servedRemovals">The net SERVED default-graph removals; equal to <paramref name="defaultRemovals"/> when reasoning is unwired.</param>
    /// <param name="causality">The commit's causality annotation, or <see langword="null"/> on a store that is not remove-aware or a commit that moves no default-graph content. A causality-only commit carries a non-<see langword="null"/> annotation with empty deltas and reaches the observer without a rendezvous advance.</param>
    internal void Publish(
        DatasetState state,
        bool defaultGraphChanged,
        IReadOnlyCollection<EncodedTriple> defaultAdditions,
        IReadOnlyCollection<EncodedTriple> defaultRemovals,
        bool namedGraphsChanged,
        HypertrieGraphStore servedStore,
        IReadOnlyCollection<EncodedTriple> servedAdditions,
        IReadOnlyCollection<EncodedTriple> servedRemovals,
        CommitCausality? causality)
    {
        //The whole publish — the state swap (asserted store, served store, and reasoning payload atomically), both
        //rendezvous advances, and the delta observer — runs under one lock, so it is atomic with respect to the next
        //commit. A competing commit reads Current to base itself, so until this publish completes the next one cannot
        //proceed; that orders the delta observers (and the rendezvous advances) in journal-commit order. Were the
        //advances outside the swap lock, two concurrent commits could deliver their deltas reversed, and the feed —
        //which evolves by delta fold, not by an absolute snapshot — would durably diverge from the committed state.
        //Every call here is synchronous and the rendezvous and feed hold their own distinct locks (no re-entrancy on
        //this one), so nothing awaits under the lock. This atomicity is the FeedMatchesStore invariant model-checked
        //in spec/CommitPublishFeed.tla: AtomicPublish = TRUE (this lock) proves it; AtomicPublish = FALSE reproduces
        //the reordered-observer bug. The argument extends to every subscriber behind the one observer seam — the
        //sketch maintainer's feed and the dotted commit ledger advance by the same delta under the same lock, one
        //more observer with no new proof shape. The query rendezvous advances by the SERVED delta (base ∪ derived)
        //while the replication observer keeps the ASSERTED delta — entailments serve but never replicate.
        lock(StateLock)
        {
            Current = state;

            if(defaultGraphChanged || causality is not null)
            {
                if(defaultGraphChanged)
                {
                    DefaultGraphRendezvous.Advance(servedStore, servedAdditions, servedRemovals);
                }

                //A subscribed replication feed advances its reconciliation index by the same committed ASSERTED
                //delta, so it tracks the asserted store exactly — derived triples never enter it. A causality-only
                //commit (empty delta, non-null annotation) reaches the observer so the ledger's durable knowledge
                //advances with the journal, without a rendezvous advance.
                DefaultGraphObserver?.Invoke(defaultAdditions, defaultRemovals, state.StateId, causality);
            }

            if(namedGraphsChanged)
            {
                //The shared graph set described the previous generation;
                //drop it and rebuild lazily from the new state on the
                //next qualifying named-graph join.
                NamedGraphRendezvous.Advance(state, new GraphSetBuildSource(state).Build);
            }
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
