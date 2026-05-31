using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The mutable dataset SPARQL Update operates on: a default graph plus named graphs, each a current
/// <see cref="HypertrieGraphStore"/> over ONE shared <see cref="TermDictionary"/>, where every graph store is backed
/// by an <see cref="InMemoryJournal"/> so mutations go through real <see cref="EditSession"/> commits (each producing
/// a journal entry + content-addressed <c>EditCommitment</c>). An edit commits to a new immutable
/// <see cref="HypertrieSnapshot"/> (copy-on-write); <see cref="HypertrieGraphStore.FromSnapshot"/> re-wraps it and the
/// dataset swaps the new store in — the prior snapshot is untouched.
/// </summary>
/// <remarks>
/// The read-only <see cref="SparqlDataset"/> a query evaluates against is produced on demand by <see cref="Snapshot"/>;
/// the executor evaluates a modify's <c>WHERE</c> against that snapshot taken before the operation's own writes, then
/// applies the full delta (SPARQL Update §3.1.3 — the <c>WHERE</c> does not see the operation's writes).
/// </remarks>
public sealed class MutableSparqlDataset
{
    private readonly Dictionary<TermId, HypertrieGraphStore> namedGraphs;

    /// <summary>The shared term dictionary every graph encodes into; append-only, so inserts intern new terms.</summary>
    public TermDictionary Dictionary { get; }

    /// <summary>The current default-graph store.</summary>
    public HypertrieGraphStore DefaultGraph { get; private set; }

    /// <summary>Constructs a mutable dataset from a shared dictionary, a default-graph store, and named-graph stores.</summary>
    /// <param name="dictionary">The shared term dictionary all graphs encode into.</param>
    /// <param name="defaultGraph">The default-graph store (must be journal-backed to accept edits).</param>
    /// <param name="namedGraphs">The named-graph stores keyed by graph-name term id.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public MutableSparqlDataset(TermDictionary dictionary, HypertrieGraphStore defaultGraph, IReadOnlyDictionary<TermId, HypertrieGraphStore> namedGraphs)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(defaultGraph);
        ArgumentNullException.ThrowIfNull(namedGraphs);

        Dictionary = dictionary;
        DefaultGraph = defaultGraph;
        this.namedGraphs = new Dictionary<TermId, HypertrieGraphStore>(namedGraphs);
    }

    /// <summary>The graph-name term ids of the current named graphs.</summary>
    public IReadOnlyCollection<TermId> NamedGraphNames => namedGraphs.Keys;

    /// <summary>Produces a read-only <see cref="SparqlDataset"/> over the current graph stores, for building a query engine.</summary>
    /// <returns>The dataset snapshot.</returns>
    public SparqlDataset Snapshot()
    {
        return new SparqlDataset(DefaultGraph, namedGraphs);
    }

    /// <summary>Replaces the default-graph store.</summary>
    /// <param name="store">The new default-graph store.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public void SetDefaultGraph(HypertrieGraphStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        DefaultGraph = store;
    }

    /// <summary>Adds or replaces the named graph for a graph name.</summary>
    /// <param name="name">The graph-name term id.</param>
    /// <param name="store">The named graph's store.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public void SetNamedGraph(TermId name, HypertrieGraphStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        namedGraphs[name] = store;
    }

    /// <summary>Removes the named graph for a graph name.</summary>
    /// <param name="name">The graph-name term id.</param>
    /// <returns><see langword="true"/> when a named graph was removed.</returns>
    public bool RemoveNamedGraph(TermId name)
    {
        return namedGraphs.Remove(name);
    }

    /// <summary>Whether the dataset has a named graph for a graph name.</summary>
    /// <param name="name">The graph-name term id.</param>
    /// <returns><see langword="true"/> when the named graph exists.</returns>
    public bool ContainsNamedGraph(TermId name)
    {
        return namedGraphs.ContainsKey(name);
    }

    /// <summary>Looks up the current store for a named graph.</summary>
    /// <param name="name">The graph-name term id.</param>
    /// <param name="store">Receives the store on success.</param>
    /// <returns><see langword="true"/> when the named graph exists.</returns>
    public bool TryGetNamedGraph(TermId name, out HypertrieGraphStore? store)
    {
        return namedGraphs.TryGetValue(name, out store);
    }

    /// <summary>Builds a fresh journal-backed graph store from encoded triples (so it can accept later edits).</summary>
    /// <param name="triples">The encoded triples to index; empty for an empty graph.</param>
    /// <param name="cancellationToken">A token that aborts the build.</param>
    /// <returns>The new journal-backed store.</returns>
    public static async ValueTask<HypertrieGraphStore> BuildGraphAsync(IReadOnlyList<EncodedTriple> triples, CancellationToken cancellationToken = default)
    {
        //One journal instance backs the store's append and read delegates. Ownership of the NodeStore transfers to the
        //returned graph on success (its snapshot pins it); a build failure disposes the orphaned store.
        InMemoryJournal journal = new();
        NodeStore? store = null;
        try
        {
            store = new NodeStore(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
            HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(triples, store, cancellationToken).ConfigureAwait(false);
            store = null;

            return graph;
        }
        finally
        {
            store?.Dispose();
        }
    }
}
