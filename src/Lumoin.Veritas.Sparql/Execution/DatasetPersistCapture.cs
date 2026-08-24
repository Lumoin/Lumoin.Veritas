using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// One committed dataset state captured as a single, self-consistent snapshot for durable persistence: the
/// default graph's triples, every named graph's triples keyed by graph-name term id, and the content-addressed
/// state identifier all three derive from. The three are always drawn from ONE committed dataset state, so a
/// generation assembled from a capture can never mix a default graph from one committed state with named graphs
/// from another - a cross-graph state that was never committed.
/// </summary>
/// <remarks>
/// The state identifier is the durable provenance binding a persisted generation records: it names the exact
/// committed dataset state the generation reflects, the affordance a recovery cross-checks a recovered
/// generation against a journal head with.
/// </remarks>
public sealed class DatasetPersistCapture
{
    /// <summary>The default graph's triples, encoded against the dataset's shared dictionary.</summary>
    public ReadOnlyMemory<EncodedTriple> DefaultGraph { get; }

    /// <summary>The named graphs, each its graph-name term id (interned in the shared dictionary) paired with that graph's triples; empty when the captured state has no named graphs.</summary>
    public IReadOnlyList<(TermId GraphName, ReadOnlyMemory<EncodedTriple> Triples)> NamedGraphs { get; }

    /// <summary>The content-addressed dataset state identifier the default graph and every named graph were captured from.</summary>
    public NodeIdentifier StateId { get; }

    /// <summary>Constructs a capture over one committed dataset state's graphs and identifier.</summary>
    /// <param name="defaultGraph">The default graph's triples.</param>
    /// <param name="namedGraphs">The named graphs keyed by graph-name term id.</param>
    /// <param name="stateId">The dataset state identifier the graphs were captured from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="namedGraphs"/> is <see langword="null"/>.</exception>
    public DatasetPersistCapture(
        ReadOnlyMemory<EncodedTriple> defaultGraph,
        IReadOnlyList<(TermId GraphName, ReadOnlyMemory<EncodedTriple> Triples)> namedGraphs,
        NodeIdentifier stateId)
    {
        ArgumentNullException.ThrowIfNull(namedGraphs);

        DefaultGraph = defaultGraph;
        NamedGraphs = namedGraphs;
        StateId = stateId;
    }
}
