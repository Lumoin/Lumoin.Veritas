using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;

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
public sealed class SparqlDataset
{
    private readonly HashSet<TermId> graphNameSet;

    private readonly Func<TermId, HypertrieGraphStore?> resolveNamed;

    /// <summary>The default graph — the store queried outside any <c>GRAPH</c> form.</summary>
    public HypertrieGraphStore DefaultGraph { get; }

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
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SparqlDataset(HypertrieGraphStore defaultGraph, IReadOnlyDictionary<TermId, HypertrieGraphStore> namedGraphs)
        : this(defaultGraph, namedGraphs, new QueryEngineRendezvous(defaultGraph, QueryEnginePolicy.Default))
    {
    }

    /// <summary>Constructs a dataset from its default graph, its named graphs, and the long-lived rendezvous whose derived view outlives this snapshot.</summary>
    /// <param name="defaultGraph">The default graph.</param>
    /// <param name="namedGraphs">The named graphs keyed by graph-name term id; encoded by the same dictionary as <paramref name="defaultGraph"/>.</param>
    /// <param name="defaultGraphRendezvous">The rendezvous default-graph patterns route through; typically owned by the mutable dataset so its view persists across snapshots.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public SparqlDataset(
        HypertrieGraphStore defaultGraph,
        IReadOnlyDictionary<TermId, HypertrieGraphStore> namedGraphs,
        QueryEngineRendezvous defaultGraphRendezvous)
    {
        ArgumentNullException.ThrowIfNull(defaultGraph);
        ArgumentNullException.ThrowIfNull(namedGraphs);
        ArgumentNullException.ThrowIfNull(defaultGraphRendezvous);

        DefaultGraph = defaultGraph;
        DefaultGraphRendezvous = defaultGraphRendezvous;
        Dictionary<TermId, HypertrieGraphStore> materialized = new(namedGraphs);
        graphNameSet = [.. materialized.Keys];
        GraphNames = [.. materialized.Keys];
        resolveNamed = graph => materialized.TryGetValue(graph, out HypertrieGraphStore? store) ? store : null;
        NamedGraphGeneration = this;
        NamedGraphRendezvous = new GraphSetRendezvous(this, CollectNamedTriples, QueryEnginePolicy.Default);
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
        Func<TermId, HypertrieGraphStore?> resolveNamedGraph,
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
        NamedGraphRendezvous = namedGraphRendezvous ?? new GraphSetRendezvous(NamedGraphGeneration, CollectNamedTriples, QueryEnginePolicy.Default);
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
    /// <returns>A named-graph-free dataset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="defaultGraph"/> is <see langword="null"/>.</exception>
    public static SparqlDataset FromDefaultGraph(HypertrieGraphStore defaultGraph)
    {
        return new SparqlDataset(defaultGraph, new Dictionary<TermId, HypertrieGraphStore>());
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
