using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The dataset a query evaluates against (SPARQL 1.2 §13): a single default graph plus zero or more named
/// graphs, each a separate <see cref="HypertrieGraphStore"/> keyed by its graph-name <see cref="TermId"/>. Every
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
/// A <c>GRAPH</c> ranges over the named graphs only; the default graph is never one of them. An IRI that names
/// no named graph (including <see cref="TermId.None"/>, the value of an IRI absent from the dictionary)
/// contributes no solutions — never the default graph.
/// </para>
/// </remarks>
public sealed class SparqlDataset
{
    private readonly Dictionary<TermId, HypertrieGraphStore> namedGraphs;

    /// <summary>The default graph — the store queried outside any <c>GRAPH</c> form.</summary>
    public HypertrieGraphStore DefaultGraph { get; }

    /// <summary>
    /// The engine rendezvous default-graph basic graph patterns
    /// route through. Queries pin <see cref="DefaultGraph"/>, so a
    /// rendezvous that has advanced past this dataset's snapshot
    /// answers on the pinned store, preserving snapshot isolation.
    /// </summary>
    public QueryEngineRendezvous DefaultGraphRendezvous { get; }

    /// <summary>The graph names of the named graphs, in insertion order — the domain a <c>GRAPH ?g</c> form ranges over.</summary>
    public IReadOnlyList<TermId> GraphNames { get; }

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
        this.namedGraphs = new Dictionary<TermId, HypertrieGraphStore>(namedGraphs);
        GraphNames = [.. this.namedGraphs.Keys];
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

        return namedGraphs.TryGetValue(graph, out HypertrieGraphStore? store) ? store : null;
    }

    /// <summary>Looks up the named graph for a graph name.</summary>
    /// <param name="graph">The graph-name term id.</param>
    /// <param name="store">Receives the named graph's store on success.</param>
    /// <returns><see langword="true"/> when the dataset has a named graph for <paramref name="graph"/>.</returns>
    public bool TryGetNamedGraph(TermId graph, out HypertrieGraphStore store)
    {
        return namedGraphs.TryGetValue(graph, out store!);
    }
}
