using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Indexing;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The dataset a query evaluates against (SPARQL 1.2 §13): a single default graph plus zero or more named
/// graphs, each a <see cref="HypertrieGraphStore"/> keyed by its graph-name <see cref="TermId"/>. Every
/// graph is encoded by ONE shared <see cref="Lumoin.Veritas.Core.TermDictionary"/>, so a <see cref="TermId"/>
/// denotes the same term in every graph and the executor switches the active graph simply by re-keying which
/// store its leaf lookups hit.
/// </summary>
/// <remarks>
/// <para>
/// This composes the three-position <see cref="HypertrieGraphStore"/> rather than widening it to a fourth
/// (graph) position: a <c>GRAPH</c> form selects which store the enclosed pattern queries. The store's
/// worst-case-optimal join construction is untouched, so the large-graph build cost is unaffected.
/// </para>
/// <para>
/// <b>Resolution.</b> Named graphs resolve through a delegate, so a dataset over a graph DIRECTORY (many
/// logical graphs over one shared arena, stores minted on demand) and a dataset over materialized stores
/// share one shape; the materialized constructor wraps its dictionary as a resolver. Existence checks go
/// through the name set and never mint.
/// </para>
/// <para>
/// A <c>GRAPH</c> ranges over the named graphs only; the default graph is never one of them. An IRI that names
/// no named graph (including <see cref="TermId.None"/>, the value of an IRI absent from the dictionary)
/// contributes no solutions — never the default graph.
/// </para>
/// </remarks>
/// <summary>Resolves a named-graph term id to its store, or <see langword="null"/> when the dataset has no such graph.</summary>
/// <param name="graph">The graph-name term id.</param>
/// <returns>The named graph's store, or <see langword="null"/> when absent.</returns>
public delegate HypertrieGraphStore? ResolveNamedGraphDelegate(TermId graph);

public sealed class SparqlDataset
{
    private readonly HashSet<TermId> graphNameSet;

    private readonly ResolveNamedGraphDelegate resolveNamed;

    /// <summary>
    /// The default graph — the store queried outside any <c>GRAPH</c> form, or <see langword="null"/> for a
    /// deferred-residency dataset whose hypertrie has not been materialised yet (the warm serve-from-disk start).
    /// Use <see cref="RequireDefaultGraphAsync"/> to obtain it where the trie is genuinely needed; the basic-graph
    /// pattern path routes through <see cref="DefaultGraphRendezvous"/>, which serves columnar-capable shapes from
    /// the warm view without it.
    /// </summary>
    public HypertrieGraphStore? DefaultGraph { get; }

    /// <summary>
    /// The engine rendezvous default-graph basic graph patterns
    /// route through. Queries pin <see cref="DefaultGraph"/>, so a
    /// rendezvous that has advanced past this dataset's snapshot
    /// answers on the pinned store, preserving snapshot isolation.
    /// </summary>
    public QueryEngineRendezvous DefaultGraphRendezvous { get; }

    /// <summary>The graph names of the named graphs — the domain a <c>GRAPH ?g</c> form ranges over.</summary>
    public IReadOnlyList<TermId> GraphNames { get; }

    /// <summary>
    /// The rendezvous named-graph basic graph patterns route
    /// through — the shared columnar graph set's routing point.
    /// Self-owned (lazy, generation = this dataset) unless the
    /// long-lived one of a mutable dataset is supplied, whose
    /// derived set then persists across snapshots.
    /// </summary>
    public GraphSetRendezvous NamedGraphRendezvous { get; }

    /// <summary>The generation token this dataset's named-graph reads carry to <see cref="NamedGraphRendezvous"/>; a rendezvous that advanced past it answers on the pinned per-graph store.</summary>
    public object NamedGraphGeneration { get; }

    /// <summary>Constructs a dataset from its default graph and its named graphs, with a fresh engine rendezvous for the default graph.</summary>
    /// <param name="defaultGraph">The default graph.</param>
    /// <param name="namedGraphs">The named graphs keyed by graph-name term id; encoded by the same dictionary as <paramref name="defaultGraph"/>.</param>
    /// <param name="computeLane">An optional compute lane; when supplied, the default graph's on-demand columnar view materialises as a lane turn off the serve path. <see langword="null"/> builds it inline on the first qualifying query.</param>
    /// <param name="initialColumnarView">A pre-built columnar view of <paramref name="defaultGraph"/>'s triples (a warm-loaded durable sidecar) the rendezvous serves from with no build, or <see langword="null"/> to build on demand.</param>
    /// <param name="valueIndexes">The composed value-index registry; <see langword="null"/> uses <see cref="ValueIndexRegistry.Empty"/>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SparqlDataset(HypertrieGraphStore defaultGraph, IReadOnlyDictionary<TermId, HypertrieGraphStore> namedGraphs, IComputeLane? computeLane = null, ColumnarTripleIndex? initialColumnarView = null, ValueIndexRegistry? valueIndexes = null)
        : this(defaultGraph, namedGraphs, new QueryEngineRendezvous(defaultGraph, QueryEnginePolicy.Default, computeLane, initialColumnarView, deferredStore: null, valueIndexes))
    {
    }

    /// <summary>Constructs a dataset from its default graph, its named graphs, and the long-lived rendezvous whose derived view outlives this snapshot.</summary>
    /// <param name="defaultGraph">The default graph.</param>
    /// <param name="namedGraphs">The named graphs keyed by graph-name term id; encoded by the same dictionary as <paramref name="defaultGraph"/>.</param>
    /// <param name="defaultGraphRendezvous">The rendezvous default-graph patterns route through; typically owned by the mutable dataset so its view persists across snapshots.</param>
    /// <exception cref="ArgumentNullException"><paramref name="namedGraphs"/> or <paramref name="defaultGraphRendezvous"/> is <see langword="null"/>.</exception>
    public SparqlDataset(
        HypertrieGraphStore? defaultGraph,
        IReadOnlyDictionary<TermId, HypertrieGraphStore> namedGraphs,
        QueryEngineRendezvous defaultGraphRendezvous)
    {
        ArgumentNullException.ThrowIfNull(namedGraphs);
        ArgumentNullException.ThrowIfNull(defaultGraphRendezvous);

        DefaultGraph = defaultGraph;
        DefaultGraphRendezvous = defaultGraphRendezvous;
        Dictionary<TermId, HypertrieGraphStore> materialized = new(namedGraphs);
        graphNameSet = [.. materialized.Keys];
        GraphNames = [.. graphNameSet];
        resolveNamed = new MaterializedGraphResolver(materialized).Resolve;
        NamedGraphGeneration = this;

        //One registry per dataset: the named-graph rendezvous carries the same composed registry the
        //default-graph rendezvous was built with.
        NamedGraphRendezvous = new GraphSetRendezvous(this, CollectNamedTriples, QueryEnginePolicy.Default, defaultGraphRendezvous.ValueIndexes);
    }

    /// <summary>Constructs a dataset whose named graphs resolve through a delegate — the graph-directory form: stores mint on demand, existence checks never mint.</summary>
    /// <param name="defaultGraph">The default graph.</param>
    /// <param name="graphNames">The named graphs' name term ids; copied.</param>
    /// <param name="resolveNamedGraph">Resolves a graph-name term id to its store, or <see langword="null"/> when the dataset has no such graph. Consulted only for names in <paramref name="graphNames"/>; must be consistent with it.</param>
    /// <param name="defaultGraphRendezvous">The rendezvous default-graph patterns route through.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SparqlDataset(
        HypertrieGraphStore defaultGraph,
        IReadOnlyCollection<TermId> graphNames,
        ResolveNamedGraphDelegate resolveNamedGraph,
        QueryEngineRendezvous defaultGraphRendezvous,
        GraphSetRendezvous? namedGraphRendezvous = null,
        object? namedGraphGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(defaultGraph);
        ArgumentNullException.ThrowIfNull(graphNames);
        ArgumentNullException.ThrowIfNull(resolveNamedGraph);
        ArgumentNullException.ThrowIfNull(defaultGraphRendezvous);

        DefaultGraph = defaultGraph;
        DefaultGraphRendezvous = defaultGraphRendezvous;
        graphNameSet = [.. graphNames];
        GraphNames = [.. graphNameSet];
        resolveNamed = resolveNamedGraph;
        NamedGraphGeneration = namedGraphGeneration ?? this;
        NamedGraphRendezvous = namedGraphRendezvous ?? new GraphSetRendezvous(NamedGraphGeneration, CollectNamedTriples, QueryEnginePolicy.Default, defaultGraphRendezvous.ValueIndexes);
    }

    /// <summary>
    /// Resolves named-graph stores from a materialized dictionary, carrying the dictionary as explicit state
    /// so the dataset's <see cref="ResolveNamedGraphDelegate"/> is a bound method group rather than a lambda
    /// closing over the enclosing dictionary.
    /// </summary>
    /// <param name="graphs">The materialized named graphs keyed by graph-name term id.</param>
    private sealed class MaterializedGraphResolver(Dictionary<TermId, HypertrieGraphStore> graphs)
    {
        /// <summary>The materialized named graphs keyed by graph-name term id.</summary>
        private Dictionary<TermId, HypertrieGraphStore> Graphs { get; } = graphs;

        /// <summary>Resolves a graph-name term id to its materialized store.</summary>
        /// <param name="graph">The graph-name term id.</param>
        /// <returns>The named graph's store, or <see langword="null"/> when absent.</returns>
        public HypertrieGraphStore? Resolve(TermId graph)
        {
            return Graphs.TryGetValue(graph, out HypertrieGraphStore? store) ? store : null;
        }
    }

    /// <summary>
    /// The self-owned rendezvous's lazy build source: every named
    /// graph's triples, enumerated through the resolver. Consulted
    /// at most once per dataset, on the first qualifying
    /// named-graph join.
    /// </summary>
    /// <returns>The named graphs' triples keyed by graph id.</returns>
    private Dictionary<TermId, IEnumerable<EncodedTriple>> CollectNamedTriples()
    {
        Dictionary<TermId, IEnumerable<EncodedTriple>> graphs = new(graphNameSet.Count);
        foreach(TermId graph in graphNameSet)
        {
            HypertrieGraphStore? store = resolveNamed(graph);
            if(store is not null)
            {
                graphs[graph] = store.Match(TermId.None, TermId.None, TermId.None);
            }
        }

        return graphs;
    }

    /// <summary>Builds a dataset with only a default graph and no named graphs — the common single-graph case.</summary>
    /// <param name="defaultGraph">The default graph.</param>
    /// <param name="computeLane">An optional compute lane materialising the default graph's view off the serve path; <see langword="null"/> builds it inline.</param>
    /// <param name="initialColumnarView">A pre-built columnar view of <paramref name="defaultGraph"/>'s triples (a warm-loaded durable sidecar), or <see langword="null"/> to build on demand.</param>
    /// <param name="valueIndexes">The composed value-index registry; <see langword="null"/> uses <see cref="ValueIndexRegistry.Empty"/>.</param>
    /// <returns>A named-graph-free dataset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="defaultGraph"/> is <see langword="null"/>.</exception>
    public static SparqlDataset FromDefaultGraph(HypertrieGraphStore defaultGraph, IComputeLane? computeLane = null, ColumnarTripleIndex? initialColumnarView = null, ValueIndexRegistry? valueIndexes = null)
    {
        return new SparqlDataset(defaultGraph, new Dictionary<TermId, HypertrieGraphStore>(), computeLane, initialColumnarView, valueIndexes);
    }

    /// <summary>
    /// Builds a deferred-residency dataset over a default graph the hypertrie is NOT yet materialised for: the
    /// rendezvous serves columnar-capable shapes from <paramref name="initialColumnarView"/> (a warm-loaded durable
    /// sidecar) and materialises the trie on demand only when a query genuinely needs it (an access-controlled
    /// query, a per-pattern self-join, a cyclic shape without a self-index). The warm serve-from-disk start. No
    /// named graphs — multi-graph persistence is a separate slice.
    /// </summary>
    /// <param name="deferredStore">The deferred build source the trie is materialised from on first demand; this dataset's rendezvous takes it over.</param>
    /// <param name="computeLane">An optional compute lane materialising the default graph's view off the serve path; <see langword="null"/> builds it inline.</param>
    /// <param name="initialColumnarView">A pre-built columnar view of the deferred triples (the warm sidecar), or <see langword="null"/> — without one a deferred query that qualifies for the view materialises the trie instead.</param>
    /// <param name="valueIndexes">The composed value-index registry; <see langword="null"/> uses <see cref="ValueIndexRegistry.Empty"/>.</param>
    /// <returns>A named-graph-free dataset whose default-graph trie is deferred.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="deferredStore"/> is <see langword="null"/>.</exception>
    public static SparqlDataset FromDeferredDefaultGraph(DeferredTrieSource deferredStore, IComputeLane? computeLane = null, ColumnarTripleIndex? initialColumnarView = null, ValueIndexRegistry? valueIndexes = null)
    {
        ArgumentNullException.ThrowIfNull(deferredStore);

        QueryEnginePolicy policy = QueryEnginePolicy.Default with { HypertrieResidency = HypertrieResidency.Deferred };
        QueryEngineRendezvous rendezvous = new(store: null, policy, computeLane, initialColumnarView, deferredStore, valueIndexes);

        return new SparqlDataset(defaultGraph: null, new Dictionary<TermId, HypertrieGraphStore>(), rendezvous);
    }

    /// <summary>
    /// The default graph's store, materialising the deferred-residency trie on demand when needed: returns the
    /// resident <see cref="DefaultGraph"/> immediately for an eager dataset, otherwise builds the trie through
    /// <see cref="DefaultGraphRendezvous"/> (at most once across callers) and returns it. The path for the consumers
    /// that genuinely need the trie's <see cref="HypertrieGraphStore.Match"/> ops — property paths, <c>DESCRIBE</c>,
    /// SHACL validation — rather than a basic-graph pattern the warm view can answer.
    /// </summary>
    /// <param name="cancellationToken">A token that aborts THIS caller's wait for a deferred materialisation; the shared build runs to completion regardless.</param>
    /// <returns>The default graph's system-of-record store.</returns>
    public ValueTask<HypertrieGraphStore> RequireDefaultGraphAsync(CancellationToken cancellationToken = default)
    {
        return DefaultGraph is { } graph
            ? new ValueTask<HypertrieGraphStore>(graph)
            : DefaultGraphRendezvous.MaterializeTrieAsync(cancellationToken);
    }

    /// <summary>Resolves the store for an active graph: the default graph for <see cref="TermId.None"/>, else the named graph for that term id.</summary>
    /// <param name="graph">The active-graph term id, or <see cref="TermId.None"/> for the default graph.</param>
    /// <returns>The store to query, or <see langword="null"/> when <paramref name="graph"/> names no graph in this dataset.</returns>
    public HypertrieGraphStore? Resolve(TermId graph)
    {
        if(graph.IsNone)
        {
            return DefaultGraph;
        }

        return graphNameSet.Contains(graph) ? resolveNamed(graph) : null;
    }

    /// <summary>Whether the dataset has a named graph for a graph name. Never mints a store.</summary>
    /// <param name="graph">The graph-name term id.</param>
    /// <returns><see langword="true"/> when the named graph exists.</returns>
    public bool ContainsNamedGraph(TermId graph)
    {
        return graphNameSet.Contains(graph);
    }

    /// <summary>Looks up the named graph for a graph name.</summary>
    /// <param name="graph">The graph-name term id.</param>
    /// <param name="store">Receives the named graph's store on success.</param>
    /// <returns><see langword="true"/> when the dataset has a named graph for <paramref name="graph"/>.</returns>
    public bool TryGetNamedGraph(TermId graph, out HypertrieGraphStore store)
    {
        store = (graphNameSet.Contains(graph) ? resolveNamed(graph) : null)!;

        return store is not null;
    }
}
