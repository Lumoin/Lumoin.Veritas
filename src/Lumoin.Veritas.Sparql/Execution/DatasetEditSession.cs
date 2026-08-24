using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Threading;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// One open transaction over a <see cref="MutableSparqlDataset"/>:
/// stages per-graph deltas and structural changes (create, drop,
/// replace) against a private working state, then commits them as
/// ONE <see cref="DatasetJournalEntry"/> — every touched graph
/// transitions atomically or not at all. A SPARQL Update request
/// maps to one session, which is what makes the request a
/// transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Working state.</b> The session copies the base state's graph
/// DIRECTORY (cheap struct entries) and materializes stores only
/// for the graphs it touches; untouched graphs resolve through the
/// base state's memoized minting. Each mutating call applies
/// immediately to the working state (so a later operation in the
/// same request reads the earlier ones' effects through
/// <see cref="Snapshot"/>), while the published dataset stays at
/// the pre-request state until <see cref="CommitAsync"/>. The
/// journal entry records the NET per-graph delta between the base
/// and the final working state — a triple added and removed inside
/// one session does not appear.
/// </para>
/// <para>
/// <b>Optimistic concurrency.</b> The commit appends under the
/// dataset journal's head-CAS; when another session committed
/// first, <see cref="CommitAsync"/> raises
/// <see cref="EditSessionConcurrencyException"/> and the caller
/// retries the whole request against the new state. Intermediate
/// working stores the session abandons become sweepable through
/// the arena's weak registries.
/// </para>
/// <para>
/// <b>Lifecycle.</b> Open → Committed on successful commit;
/// Open → Disposed or Committed → Disposed on dispose. Disposing
/// an uncommitted session writes a best-effort
/// <see cref="EditSessionEntryKind.Abandoned"/> entry, mirroring
/// the per-store <see cref="EditSession"/> contract. The session
/// holds the arena's shared mutation gate for its lifetime, so
/// sweeps wait for open sessions.
/// </para>
/// </remarks>
[DebuggerDisplay("DatasetEditSession Id={Id} State={CurrentStateName}")]
public sealed class DatasetEditSession: IAsyncDisposable
{
    //State machine values; encoded as int so Interlocked.CompareExchange applies.
    private const int StateOpen = 0;

    private const int StateCommitted = 1;

    private const int StateDisposed = 2;

    //Naked field: Interlocked/Volatile primitives require ref semantics.
    private int state = StateOpen;

    /// <summary>Per-graph net-delta scratch: the additions and removals relative to the BASE state, folded across every operation of the session.</summary>
    private sealed class GraphScratch
    {
        /// <summary>Triples in the working graph that are not in the base graph.</summary>
        public HashSet<EncodedTriple> Added { get; } = [];

        /// <summary>Triples in the base graph that are not in the working graph.</summary>
        public HashSet<EncodedTriple> Removed { get; } = [];
    }

    /// <summary>The dataset the session commits into.</summary>
    private MutableSparqlDataset Dataset { get; }

    /// <summary>The committed state the session branched from.</summary>
    private MutableSparqlDataset.DatasetState BaseState { get; }

    /// <summary>The arena's shared mutation-gate scope, held for the session's lifetime.</summary>
    private SharedScope Scope { get; }

    /// <summary>The session's working default-graph store.</summary>
    public HypertrieGraphStore DefaultGraph { get; private set; }

    /// <summary>The session's working named-graph directory. A private copy of the base directory, mutated freely.</summary>
    private Dictionary<TermId, GraphDirectoryEntry> WorkingDirectory { get; }

    /// <summary>The stores the session has materialized for the graphs it touched; untouched graphs resolve through the base state.</summary>
    private Dictionary<TermId, HypertrieGraphStore> TouchedStores { get; } = [];

    /// <summary>The net deltas per touched graph, keyed by graph term id (<see cref="TermId.None"/> = the default graph).</summary>
    private Dictionary<TermId, GraphScratch> Scratch { get; } = [];

    /// <summary>The session's opaque identifier; identifies its lifecycle entries in the dataset journal.</summary>
    public SessionId Id { get; }

    /// <summary>Diagnostic projection of the state machine for the debugger display.</summary>
    private string CurrentStateName => Volatile.Read(ref state) switch
    {
        StateOpen => "Open",
        StateCommitted => "Committed",
        StateDisposed => "Disposed",
        _ => "Unknown",
    };

    /// <summary>Constructs an open session. Called by <see cref="MutableSparqlDataset.OpenSessionAsync"/>; consumers do not call this directly.</summary>
    /// <param name="dataset">The dataset the session commits into.</param>
    /// <param name="baseState">The committed state the session branches from.</param>
    /// <param name="scope">The arena's shared mutation-gate scope, owned by the session from here on.</param>
    /// <param name="id">The session's opaque identifier.</param>
    internal DatasetEditSession(
        MutableSparqlDataset dataset,
        MutableSparqlDataset.DatasetState baseState,
        SharedScope scope,
        SessionId id)
    {
        Dataset = dataset;
        BaseState = baseState;
        Scope = scope;
        Id = id;
        DefaultGraph = baseState.DefaultGraph;
        WorkingDirectory = new Dictionary<TermId, GraphDirectoryEntry>(baseState.Directory);
    }

    /// <summary>The dataset's shared term dictionary, for encoding update terms.</summary>
    public TermDictionary Dictionary => Dataset.Dictionary;

    /// <summary>The graph-name term ids of the working named graphs.</summary>
    public IReadOnlyCollection<TermId> NamedGraphNames => WorkingDirectory.Keys;

    /// <summary>Whether the working state has a named graph for a graph name. Never mints a store.</summary>
    /// <param name="name">The graph-name term id.</param>
    /// <returns><see langword="true"/> when the named graph exists.</returns>
    public bool ContainsNamedGraph(TermId name)
    {
        return WorkingDirectory.ContainsKey(name);
    }

    /// <summary>Looks up the working store for a named graph: the session's own store when touched, otherwise the base state's memoized mint.</summary>
    /// <param name="name">The graph-name term id.</param>
    /// <param name="store">Receives the store on success.</param>
    /// <returns><see langword="true"/> when the named graph exists.</returns>
    public bool TryGetNamedGraph(TermId name, out HypertrieGraphStore? store)
    {
        store = ResolveWorking(name);

        return store is not null;
    }

    /// <summary>
    /// Produces a read-only <see cref="SparqlDataset"/> over the
    /// session's WORKING state, so a later operation's
    /// <c>WHERE</c> sees the earlier operations' effects
    /// (SPARQL Update §3.1.3) while outside readers keep the
    /// pre-request state. The view resolves through the session
    /// live; it is meant for evaluation completed before the next
    /// mutating call, which is how the executor consumes it.
    /// </summary>
    /// <returns>The working-state dataset view.</returns>
    public SparqlDataset Snapshot()
    {
        ThrowIfNotOpen();

        //The session itself is the generation token: it never
        //matches the shared graph-set rendezvous's committed
        //generations, so mid-request named-graph reads always
        //answer on the WORKING stores — the set describes the base
        //state, which §3.1.3 forbids a later operation to see.
        return new SparqlDataset(DefaultGraph, [.. WorkingDirectory.Keys], ResolveWorking, Dataset.DefaultGraphRendezvous, Dataset.NamedGraphRendezvous, this);
    }

    /// <summary>Produces a working-state view with a substituted default graph (the <c>WITH</c> graph of a modify's <c>WHERE</c>).</summary>
    /// <param name="defaultGraph">The default-graph store the view uses.</param>
    /// <returns>The dataset view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="defaultGraph"/> is <see langword="null"/>.</exception>
    public SparqlDataset SnapshotWithDefault(HypertrieGraphStore defaultGraph)
    {
        ArgumentNullException.ThrowIfNull(defaultGraph);
        ThrowIfNotOpen();

        return new SparqlDataset(defaultGraph, [.. WorkingDirectory.Keys], ResolveWorking, Dataset.DefaultGraphRendezvous, Dataset.NamedGraphRendezvous, this);
    }

    /// <summary>
    /// Applies a triple delta to one graph of the working state. An
    /// absent named graph springs into existence when the delta
    /// carries additions (SPARQL Update §3.1.1); a delta of only
    /// removals against an absent graph is a no-op.
    /// </summary>
    /// <param name="graph">The graph to edit: a named graph's term id, or <see cref="TermId.None"/> for the default graph.</param>
    /// <param name="additions">The triples to add; already-present triples are tolerated.</param>
    /// <param name="removals">The triples to remove; already-absent triples are tolerated.</param>
    /// <param name="cancellationToken">A token that aborts the application.</param>
    /// <returns>The asynchronous application.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The session is not open.</exception>
    public async ValueTask ApplyDeltaAsync(
        TermId graph,
        IReadOnlyCollection<EncodedTriple> additions,
        IReadOnlyCollection<EncodedTriple> removals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(removals);
        ThrowIfNotOpen();
        cancellationToken.ThrowIfCancellationRequested();

        HypertrieGraphStore? store = graph.IsNone ? DefaultGraph : ResolveWorking(graph);
        if(store is null)
        {
            if(additions.Count == 0)
            {
                return;
            }

            store = await HypertrieGraphStore.BuildAsync([], Dataset.Arena, Dataset.Pools, cancellationToken).ConfigureAwait(false);
            RecordWorkingStore(graph, store);
        }

        ApplyDeltaResult delta = HypertrieOpsPatching.ApplyDelta(store.Snapshot, additions, removals, Dataset.Arena, Dataset.Pools);
        if(delta.EffectiveAdditions.Count == 0 && delta.EffectiveRemovals.Count == 0)
        {
            return;
        }

        //Constructing the snapshot registers it with the arena;
        //that happens under the mutation gate the session holds, so
        //a concurrent sweep cannot run between intern and
        //registration. The store acquires its own reference, so the
        //creator reference releases at the end of this scope.
        using HypertrieSnapshot snapshot = new(Dataset.Arena, delta.Root, delta.Id);
        HypertrieGraphStore updated = HypertrieGraphStore.FromSnapshot(snapshot, store.Count + delta.EffectiveAdditions.Count - delta.EffectiveRemovals.Count);
        if(graph.IsNone)
        {
            DefaultGraph = updated;
        }
        else
        {
            RecordWorkingStore(graph, updated);
        }

        FoldDelta(ScratchFor(graph), delta.EffectiveAdditions, delta.EffectiveRemovals);
    }

    /// <summary>Replaces a graph's content wholesale (<c>COPY</c>/<c>MOVE</c>/<c>CLEAR</c> semantics): the graph's working content becomes exactly <paramref name="triples"/>. An absent named graph is created, even for empty replacement content.</summary>
    /// <param name="graph">The graph to replace: a named graph's term id (created when absent), or <see cref="TermId.None"/> for the default graph.</param>
    /// <param name="triples">The replacement content; empty clears the graph.</param>
    /// <param name="cancellationToken">A token that aborts the application.</param>
    /// <returns>The asynchronous application.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The session is not open.</exception>
    public async ValueTask ReplaceGraphAsync(
        TermId graph,
        IReadOnlyCollection<EncodedTriple> triples,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triples);
        ThrowIfNotOpen();

        if(!graph.IsNone && !WorkingDirectory.ContainsKey(graph))
        {
            await CreateGraphAsync(graph, cancellationToken).ConfigureAwait(false);
        }

        HypertrieGraphStore store = graph.IsNone ? DefaultGraph : ResolveWorking(graph)!;
        List<EncodedTriple> currentTriples = MutableSparqlDataset.CollectAllTriples(store);

        await ApplyDeltaAsync(graph, triples, currentTriples, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates an empty named graph in the working state (<c>CREATE GRAPH</c>); an existing graph is left unchanged.</summary>
    /// <param name="graph">The graph-name term id.</param>
    /// <param name="cancellationToken">A token that aborts the build.</param>
    /// <returns>The asynchronous application.</returns>
    /// <exception cref="ArgumentException"><paramref name="graph"/> is <see cref="TermId.None"/> — the default graph always exists.</exception>
    /// <exception cref="InvalidOperationException">The session is not open.</exception>
    public async ValueTask CreateGraphAsync(TermId graph, CancellationToken cancellationToken = default)
    {
        ThrowIfNotOpen();

        if(graph.IsNone)
        {
            throw new ArgumentException("The default graph always exists and cannot be created.", nameof(graph));
        }

        if(WorkingDirectory.ContainsKey(graph))
        {
            return;
        }

        RecordWorkingStore(graph, await HypertrieGraphStore.BuildAsync([], Dataset.Arena, Dataset.Pools, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Drops a named graph from the working state (<c>DROP
    /// GRAPH</c>); an absent graph is a no-op. The net-delta
    /// scratch first folds in the removal of the graph's working
    /// content, so a later re-creation in the same session yields a
    /// correct net transition against the base.
    /// </summary>
    /// <param name="graph">The graph-name term id.</param>
    /// <exception cref="ArgumentException"><paramref name="graph"/> is <see cref="TermId.None"/> — clear the default graph via <see cref="ReplaceGraphAsync"/> instead.</exception>
    /// <exception cref="InvalidOperationException">The session is not open.</exception>
    public void DropGraph(TermId graph)
    {
        ThrowIfNotOpen();

        if(graph.IsNone)
        {
            throw new ArgumentException("The default graph always exists and cannot be dropped.", nameof(graph));
        }

        HypertrieGraphStore? store = ResolveWorking(graph);
        if(store is null)
        {
            return;
        }

        FoldDelta(ScratchFor(graph), [], MutableSparqlDataset.CollectAllTriples(store));
        WorkingDirectory.Remove(graph);
        TouchedStores.Remove(graph);
    }

    /// <summary>
    /// A test-only seam that runs between this commit's journal append (its linearisation point) and its publish,
    /// letting a test pause here while a competing commit attempts its own append — so a concurrency interleaving
    /// becomes deterministic instead of timing-dependent. It forces, in process, the same interleaving the TLA+
    /// model <c>spec/CommitPublishFeed.tla</c> checks exhaustively. <see langword="null"/> in production: the commit
    /// path is then byte-identical and pays only a null check, and nothing here ever runs.
    /// </summary>
    internal Func<CancellationToken, ValueTask>? CommitInterleavingHook { get; set; }

    /// <summary>
    /// Commits the session's accumulated transitions as one
    /// <see cref="DatasetJournalEntry"/> and publishes the new
    /// dataset state — directory, root-set pin, and the touched
    /// stores seeding the new state's mint memo. Transitions the
    /// session to Committed; subsequent mutating calls throw.
    /// </summary>
    /// <param name="cancellationToken">A token that aborts the commit.</param>
    /// <returns>The committed read-only dataset snapshot. When nothing effectively changed, the pre-request snapshot — no journal entry is written.</returns>
    /// <exception cref="InvalidOperationException">The session is not open.</exception>
    /// <exception cref="EditSessionConcurrencyException">Another session committed first; the dataset journal's head moved past the session's base. Retry the whole request against the new state.</exception>
    public ValueTask<SparqlDataset> CommitAsync(CancellationToken cancellationToken = default)
    {
        return CommitAsync(adoptedCausality: null, cancellationToken);
    }

    /// <summary>
    /// Commits the session's accumulated transitions with an explicitly supplied causality annotation — the
    /// reconcile write-back's entrance. The adopted annotation rides the journal entry and the publish verbatim;
    /// the locally-wired causality builder is not consulted. A non-<see langword="null"/> annotation with no
    /// staged transitions commits as a causality-only entry: the committed triple set, StateId, and journal head
    /// value are unchanged while the annotation lands durably and atomically under the same head compare-and-swap
    /// as any commit.
    /// </summary>
    /// <param name="adoptedCausality">The pre-built, adopt-guarded annotation the commit carries, or <see langword="null"/> for a locally-authored commit whose annotation comes from the wired builder.</param>
    /// <param name="cancellationToken">A token that aborts the commit.</param>
    /// <returns>The committed read-only dataset snapshot. When nothing effectively changed and no annotation was supplied, the pre-request snapshot — no journal entry is written.</returns>
    /// <exception cref="InvalidOperationException">The session is not open.</exception>
    /// <exception cref="EditSessionConcurrencyException">Another session committed first; the dataset journal's head moved past the session's base. Retry the whole request against the new state.</exception>
    public async ValueTask<SparqlDataset> CommitAsync(CommitCausality? adoptedCausality, CancellationToken cancellationToken = default)
    {
        ThrowIfNotOpen();
        cancellationToken.ThrowIfCancellationRequested();

        ImmutableArray<DatasetGraphTransition> transitions = ComputeTransitions();
        if(transitions.IsEmpty && adoptedCausality is null)
        {
            TransitionToCommitted();

            return Dataset.Snapshot();
        }

        //A reasoned mutable engine maintains the served store under the maintenance mutex; an unwired engine takes
        //the byte-identical path below with no mutex and no new work.
        if(Dataset.MaintenanceDelegate is { } maintenanceDelegate)
        {
            return await CommitMaintainedAsync(transitions, adoptedCausality, maintenanceDelegate, cancellationToken).ConfigureAwait(false);
        }

        bool defaultChanged = Scratch.TryGetValue(TermId.None, out GraphScratch? defaultScratch)
            && (defaultScratch.Added.Count > 0 || defaultScratch.Removed.Count > 0);

        //A remove-aware store's LOCAL commit holds the causality commit gate from its annotation build through
        //its publish. The head compare-and-swap orders commits by the head VALUE, and a causality-only
        //competitor (an adopt commit with no dataset delta) leaves that value unchanged — so without the gate
        //an annotation could be built against a ledger state such a competitor then moves without failing this
        //commit's append, and two appended commits could publish out of journal order. An adopted commit's
        //caller already holds the gate across its plan and this commit; an add-only store has no gate and this
        //path is byte-identical to before the remove-aware lane existed.
        SemaphoreSlim? causalityGate = adoptedCausality is null ? Dataset.CausalityCommitGate : null;
        if(causalityGate is not null)
        {
            await causalityGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            //The commit's annotation: the adopt entrance supplies it pre-built and guarded; a locally-authored
            //commit builds it against the live ledger through the wired builder. Built BEFORE the linearising
            //append — the head compare-and-swap (with the gate above) certifies its basis, and a commit that
            //loses the race rebuilds it against the new head on retry.
            CommitCausality? causality = adoptedCausality;
            if(causality is null && defaultChanged && Dataset.CausalityBuilder is { } causalityBuilder)
            {
                causality = causalityBuilder(defaultScratch!.Added, defaultScratch.Removed);
            }

            NodeIdentifier childStateId = MutableSparqlDataset.ComputeStateId(Dataset.Arena.Hash, DefaultGraph, WorkingDirectory);
            DatasetJournalEntry committedEntry = DatasetJournalEntry.Committed(
                Dataset.Arena.Hash,
                parentId: BaseState.StateId,
                childId: childStateId,
                sessionId: Id,
                transitions: transitions,
                causality: causality);

            //The append is the linearisation point: it succeeds only
            //when this session's base is still the head, so the
            //subsequent publish cannot race another publisher. This is the
            //AppendOk / AppendConflict head-CAS modelled in spec/CommitPublishFeed.tla.
            await Dataset.JournalAppend(committedEntry, BaseState.StateId, cancellationToken).ConfigureAwait(false);

            //Test-only interleaving seam (null in production): between the linearising append and the publish, so a test
            //can pause here to force a deterministic interleaving with a competing commit.
            if(CommitInterleavingHook is { } interleavingHook)
            {
                await interleavingHook(cancellationToken).ConfigureAwait(false);
            }

            bool namedChanged = false;
            foreach(DatasetGraphTransition transition in transitions)
            {
                if(!transition.Graph.IsNone)
                {
                    namedChanged = true;

                    break;
                }
            }

            List<NodeHandle> roots = new(WorkingDirectory.Count);
            foreach(GraphDirectoryEntry entry in WorkingDirectory.Values)
            {
                roots.Add(entry.Root);
            }

            MutableSparqlDataset.DatasetState newState = new(
                Dataset.Arena,
                DefaultGraph,
                WorkingDirectory,
                roots.Count > 0 ? Dataset.Arena.PinRoots(roots) : null,
                childStateId,
                TouchedStores);
            //Unwired: the served store is the asserted store and the served delta is the asserted delta, so the
            //rendezvous advance is byte-identical to before the reasoned lane existed.
            Dataset.Publish(
                newState,
                defaultChanged,
                defaultChanged ? defaultScratch!.Added : [],
                defaultChanged ? defaultScratch!.Removed : [],
                namedChanged,
                newState.DefaultGraph,
                defaultChanged ? defaultScratch!.Added : [],
                defaultChanged ? defaultScratch!.Removed : [],
                causality);
            TransitionToCommitted();

            return new SparqlDataset(newState.DefaultGraph, [.. WorkingDirectory.Keys], newState.ResolveNamed, Dataset.DefaultGraphRendezvous);
        }
        finally
        {
            causalityGate?.Release();
        }
    }

    /// <summary>
    /// Commits the session on a REASONED mutable engine: the same linearising append and publish as the unwired
    /// path, wrapped in the maintenance mutex and interleaved with the per-commit closure maintenance. The mutex
    /// spans the staleness pre-check, the maintenance delegate, the served-snapshot build, the append,
    /// <see cref="MutableSparqlDataset.Publish"/>, and the outcome latch — serializing Apply-vs-COMMIT. Under
    /// it: a stale session skips the delegate and lets its append fail naturally (firing no outcome); a
    /// default-graph commit invokes the delegate, builds the new served snapshot from the returned delta, and
    /// publishes both stores atomically; a named-graph-only or empty-default commit carries the served store and
    /// reasoning payload forward from the base state by reference. The single outcome seam fires exactly once per
    /// delegate invocation, with the value latched immediately after publish, before the mutex releases.
    /// </summary>
    /// <param name="transitions">The session's computed per-graph transitions; empty only on a causality-only commit.</param>
    /// <param name="adoptedCausality">The pre-built, adopt-guarded annotation the commit carries, or <see langword="null"/> for a locally-authored commit whose annotation comes from the wired builder.</param>
    /// <param name="maintenanceDelegate">The reasoned engine's per-commit maintenance delegate.</param>
    /// <param name="cancellationToken">A token that aborts the commit; observed pre-append.</param>
    /// <returns>The committed read-only dataset snapshot, its default graph routed through the served store.</returns>
    /// <exception cref="EditSessionConcurrencyException">Another session committed first; the head-CAS fails. Retry the whole request against the new state.</exception>
    private async ValueTask<SparqlDataset> CommitMaintainedAsync(
        ImmutableArray<DatasetGraphTransition> transitions,
        CommitCausality? adoptedCausality,
        ClosureMaintenanceDelegate maintenanceDelegate,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim mutex = Dataset.MaintenanceMutex!;
        bool delegateInvoked = false;
        bool landed = false;

        await mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            //Staleness pre-check under the mutex: after a competing commit published, Current has advanced past this
            //session's base. Under the mutex Current cannot move, so the pre-check reliably predicts the append —
            //a stale session skips the delegate (no Apply, no closure cost) and lets its append fail naturally into
            //the facade retry, and a skipped invocation fires no outcome notification.
            bool stale = BaseState.StateId != Dataset.CurrentState().StateId;

            GraphScratch? defaultScratch = Scratch.TryGetValue(TermId.None, out GraphScratch? scratch) ? scratch : null;
            bool defaultChanged = defaultScratch is not null && (defaultScratch.Added.Count > 0 || defaultScratch.Removed.Count > 0);

            //The commit's annotation, built BEFORE the linearising append exactly as on the unwired path; a
            //stale session skips the build the way it skips the maintenance delegate — its append fails anyway
            //and the facade retry rebuilds against the new head.
            CommitCausality? causality = adoptedCausality;
            if(causality is null && !stale && defaultChanged && Dataset.CausalityBuilder is { } causalityBuilder)
            {
                causality = causalityBuilder(defaultScratch!.Added, defaultScratch.Removed);
            }

            HypertrieGraphStore servedStore;
            object? reasoningState;
            IReadOnlyCollection<EncodedTriple> servedAdditions;
            IReadOnlyCollection<EncodedTriple> servedRemovals;

            if(!stale && defaultChanged)
            {
                //Wholesale-replace detection: the net retract set covers the entire pre-commit asserted default
                //graph (equivalently, every asserted default triple is retracted — a CLEAR/REPLACE of the default
                //graph), so maintenance rebuilds from the tentative base rather than feeding a degenerate apply.
                bool wholesaleReplace = defaultScratch!.Removed.Count >= BaseState.DefaultGraph.Count;

                //Mark the invocation BEFORE awaiting: a delegate/apply throw is an invoked, not-landed outcome,
                //so it must ride the finally's notification exactly like a post-delegate append conflict does.
                delegateInvoked = true;
                MaintainedCommitDelta delta = await maintenanceDelegate(
                    defaultScratch.Added,
                    defaultScratch.Removed,
                    DefaultGraph,
                    wholesaleReplace,
                    cancellationToken).ConfigureAwait(false);

                //Build the new served snapshot by ONE edit over the CURRENT served store, applying the returned
                //served delta. Same arena, so the new served root shares structure with the base served store; the
                //session holds the mutation gate, so the snapshot registers sweep-safely.
                ApplyDeltaResult servedDelta = HypertrieOpsPatching.ApplyDelta(
                    BaseState.ServedDefaultGraph.Snapshot,
                    delta.ServedAdditions,
                    delta.ServedRemovals,
                    Dataset.Arena,
                    Dataset.Pools);
                using HypertrieSnapshot servedSnapshot = new(Dataset.Arena, servedDelta.Root, servedDelta.Id);
                servedStore = HypertrieGraphStore.FromSnapshot(
                    servedSnapshot,
                    BaseState.ServedDefaultGraph.Count + servedDelta.EffectiveAdditions.Count - servedDelta.EffectiveRemovals.Count);
                servedAdditions = delta.ServedAdditions;
                servedRemovals = delta.ServedRemovals;
                reasoningState = delta.ReasoningState;
            }
            else
            {
                //A named-graph-only commit, an empty default-graph net delta, or a stale session: the delegate is
                //not invoked. The served store and reasoning payload carry forward from the base state BY REFERENCE
                //— never reset to the asserted graph, which would orphan the overlay and serve asserted-only until
                //the next default-graph commit.
                servedStore = BaseState.ServedDefaultGraph;
                reasoningState = BaseState.ReasoningState;
                servedAdditions = [];
                servedRemovals = [];
            }

            NodeIdentifier childStateId = MutableSparqlDataset.ComputeStateId(Dataset.Arena.Hash, DefaultGraph, WorkingDirectory);
            DatasetJournalEntry committedEntry = DatasetJournalEntry.Committed(
                Dataset.Arena.Hash,
                parentId: BaseState.StateId,
                childId: childStateId,
                sessionId: Id,
                transitions: transitions,
                causality: causality);

            //The linearising append. A stale session fails the head-CAS here having invoked no delegate, so the
            //finally reports nothing. A post-delegate append conflict (the out-of-process race, or a fault-injected
            //failure) throws here having invoked the delegate, so the finally fires landed=false and the Database
            //binding discards the maintenance instance.
            await Dataset.JournalAppend(committedEntry, BaseState.StateId, cancellationToken).ConfigureAwait(false);

            //Test-only interleaving seam (null in production): between the linearising append and the publish.
            if(CommitInterleavingHook is { } interleavingHook)
            {
                await interleavingHook(cancellationToken).ConfigureAwait(false);
            }

            bool namedChanged = false;
            foreach(DatasetGraphTransition transition in transitions)
            {
                if(!transition.Graph.IsNone)
                {
                    namedChanged = true;

                    break;
                }
            }

            List<NodeHandle> roots = new(WorkingDirectory.Count + 1);
            foreach(GraphDirectoryEntry entry in WorkingDirectory.Values)
            {
                roots.Add(entry.Root);
            }

            //The served lineage joins the state pin alongside the asserted roots, so an arena sweep never collects it.
            roots.Add(servedStore.Snapshot.Root);

            MutableSparqlDataset.DatasetState newState = new(
                Dataset.Arena,
                DefaultGraph,
                WorkingDirectory,
                Dataset.Arena.PinRoots(roots),
                childStateId,
                TouchedStores,
                servedDefaultGraph: servedStore,
                reasoningState: reasoningState);

            Dataset.Publish(
                newState,
                defaultChanged,
                defaultChanged ? defaultScratch!.Added : [],
                defaultChanged ? defaultScratch!.Removed : [],
                namedChanged,
                servedStore,
                servedAdditions,
                servedRemovals,
                causality);

            //Latch landed IMMEDIATELY after the atomic publish — before TransitionToCommitted, whose
            //concurrent-dispose throw must not spuriously invalidate a correctly-landed commit.
            landed = true;
            TransitionToCommitted();

            //The returned dataset routes its default graph through the served store, matching Snapshot().
            return new SparqlDataset(newState.ServedDefaultGraph, [.. WorkingDirectory.Keys], newState.ResolveNamed, Dataset.DefaultGraphRendezvous);
        }
        finally
        {
            //One outcome notification per delegate invocation, fired with the latched landed value before the mutex
            //releases. A skipped delegate (stale session, named-graph-only, or empty default delta) fires nothing.
            if(delegateInvoked)
            {
                Dataset.MaintenanceOutcome?.Invoke(landed);
            }

            mutex.Release();
        }
    }

    /// <summary>
    /// Releases the session's resources. An uncommitted session
    /// records a best-effort
    /// <see cref="EditSessionEntryKind.Abandoned"/> entry — a
    /// failure there is swallowed, mirroring the per-store
    /// <see cref="EditSession"/> contract. Always releases the
    /// arena's shared scope.
    /// </summary>
    /// <returns>The asynchronous disposal.</returns>
    public async ValueTask DisposeAsync()
    {
        int previous = Interlocked.Exchange(ref state, StateDisposed);
        if(previous == StateDisposed)
        {
            return;
        }

        try
        {
            if(previous == StateOpen)
            {
                DatasetJournalEntry abandonedEntry = DatasetJournalEntry.Abandoned(BaseState.StateId, Id);

                try
                {
                    await Dataset.JournalAppend(abandonedEntry, BaseState.StateId, CancellationToken.None).ConfigureAwait(false);
                }
                catch(EditSessionConcurrencyException)
                {
                    //Head moved before the abandon could be
                    //recorded. The Started entry written at open
                    //plus the absence of a Committed entry against
                    //this SessionId is the recoverable signal.
                }
                catch(InvalidOperationException)
                {
                    //Defensive against journal implementations that
                    //surface state-violation errors during shutdown.
                }
            }
        }
        finally
        {
            await Scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Resolves a named graph in the working state: a touched store, or the base state's memoized mint for an inherited entry.</summary>
    /// <param name="graph">The graph-name term id.</param>
    /// <returns>The store, or <see langword="null"/> when the working state has no such graph.</returns>
    private HypertrieGraphStore? ResolveWorking(TermId graph)
    {
        if(TouchedStores.TryGetValue(graph, out HypertrieGraphStore? touched))
        {
            return touched;
        }

        //An untouched directory entry is byte-identical to the base
        //state's, so the base's mint memo serves it — shared with
        //published readers of the same state.
        return WorkingDirectory.ContainsKey(graph) ? BaseState.ResolveNamed(graph) : null;
    }

    /// <summary>Records a touched graph's new working store and directory entry.</summary>
    /// <param name="graph">The graph-name term id.</param>
    /// <param name="store">The new working store.</param>
    private void RecordWorkingStore(TermId graph, HypertrieGraphStore store)
    {
        TouchedStores[graph] = store;
        WorkingDirectory[graph] = new GraphDirectoryEntry(store.Snapshot.Root, store.Snapshot.Id, store.Count);
    }

    /// <summary>Looks up or creates the net-delta scratch for a graph.</summary>
    /// <param name="graph">The graph term id; <see cref="TermId.None"/> for the default graph.</param>
    /// <returns>The scratch.</returns>
    private GraphScratch ScratchFor(TermId graph)
    {
        if(!Scratch.TryGetValue(graph, out GraphScratch? scratch))
        {
            scratch = new GraphScratch();
            Scratch[graph] = scratch;
        }

        return scratch;
    }

    /// <summary>
    /// Folds one operation's EFFECTIVE delta into a graph's net
    /// scratch, maintaining the invariants added = working∖base and
    /// removed = base∖working: re-adding a removed triple cancels
    /// the removal, removing an added triple cancels the addition.
    /// </summary>
    /// <param name="scratch">The graph's scratch.</param>
    /// <param name="effectiveAdditions">Triples this operation actually added to the working graph.</param>
    /// <param name="effectiveRemovals">Triples this operation actually removed from the working graph.</param>
    private static void FoldDelta(
        GraphScratch scratch,
        IReadOnlyList<EncodedTriple> effectiveAdditions,
        IReadOnlyList<EncodedTriple> effectiveRemovals)
    {
        foreach(EncodedTriple triple in effectiveAdditions)
        {
            if(!scratch.Removed.Remove(triple))
            {
                scratch.Added.Add(triple);
            }
        }

        foreach(EncodedTriple triple in effectiveRemovals)
        {
            if(!scratch.Added.Remove(triple))
            {
                scratch.Removed.Add(triple);
            }
        }
    }

    /// <summary>
    /// Computes the per-graph transitions between the base state
    /// and the working state: a mutate transition where both sides
    /// exist and the root moved, a create where only the working
    /// side exists, a drop where only the base side exists.
    /// Deterministically ordered by graph id, the default graph
    /// first.
    /// </summary>
    /// <returns>The transitions; empty when nothing effectively changed.</returns>
    private ImmutableArray<DatasetGraphTransition> ComputeTransitions()
    {
        List<DatasetGraphTransition> transitions = [];

        if(DefaultGraph.Snapshot.Id != BaseState.DefaultGraph.Snapshot.Id)
        {
            GraphScratch scratch = ScratchFor(TermId.None);
            transitions.Add(new DatasetGraphTransition(
                TermId.None,
                ParentRoot: BaseState.DefaultGraph.Snapshot.Id,
                ChildRoot: DefaultGraph.Snapshot.Id,
                Additions: [.. scratch.Added],
                Removals: [.. scratch.Removed]));
        }

        SortedSet<uint> graphIds = [];
        foreach(TermId graph in BaseState.Directory.Keys)
        {
            graphIds.Add(graph.Encoded);
        }

        foreach(TermId graph in WorkingDirectory.Keys)
        {
            graphIds.Add(graph.Encoded);
        }

        foreach(uint encoded in graphIds)
        {
            TermId graph = TermId.FromEncoded(encoded);
            bool inBase = BaseState.Directory.TryGetValue(graph, out GraphDirectoryEntry baseEntry);
            bool inWorking = WorkingDirectory.TryGetValue(graph, out GraphDirectoryEntry workingEntry);

            if(inBase && inWorking)
            {
                if(baseEntry.Id == workingEntry.Id)
                {
                    continue;
                }

                GraphScratch scratch = ScratchFor(graph);
                transitions.Add(new DatasetGraphTransition(
                    graph,
                    ParentRoot: baseEntry.Id,
                    ChildRoot: workingEntry.Id,
                    Additions: [.. scratch.Added],
                    Removals: [.. scratch.Removed]));

                continue;
            }

            if(inBase)
            {
                //A drop discards the graph wholesale; the parent
                //root identifies what was discarded and the deltas
                //stay empty.
                transitions.Add(new DatasetGraphTransition(
                    graph,
                    ParentRoot: baseEntry.Id,
                    ChildRoot: null,
                    Additions: [],
                    Removals: []));

                continue;
            }

            if(inWorking)
            {
                GraphScratch scratch = ScratchFor(graph);
                transitions.Add(new DatasetGraphTransition(
                    graph,
                    ParentRoot: null,
                    ChildRoot: workingEntry.Id,
                    Additions: [.. scratch.Added],
                    Removals: []));
            }
        }

        return [.. transitions];
    }

    /// <summary>Throws when the session is not open.</summary>
    private void ThrowIfNotOpen()
    {
        int observed = Volatile.Read(ref state);
        if(observed != StateOpen)
        {
            throw new InvalidOperationException(
                observed switch
                {
                    StateCommitted => "The dataset edit session has already been committed.",
                    StateDisposed => "The dataset edit session has been disposed.",
                    _ => "The dataset edit session is not in an open state.",
                });
        }
    }

    /// <summary>Moves the state machine from Open to Committed, exactly once.</summary>
    /// <exception cref="InvalidOperationException">The session was disposed concurrently with the commit.</exception>
    private void TransitionToCommitted()
    {
        int previous = Interlocked.CompareExchange(ref state, StateCommitted, StateOpen);
        if(previous != StateOpen)
        {
            throw new InvalidOperationException("The dataset edit session was disposed concurrently with the commit.");
        }
    }
}
