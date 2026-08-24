using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Database;

/// <summary>
/// One graph's part of a world diff, decoded to terms: which graph differs and the net triple delta
/// that carries the baseline world's graph to the diffed world's graph. The shape mirrors the commit
/// path's per-graph transition record with the term identifiers resolved through the shared dictionary,
/// so no caller handles encoded triples.
/// </summary>
public sealed class WorldGraphTransition
{
    /// <summary>The graph the transition applies to: a named graph's name term, or <see langword="null"/> for the default graph.</summary>
    public RdfTerm? Graph { get; }

    /// <summary>The triples present in the diffed world's graph and absent from the baseline's. Net-effective: a triple added and removed across the divergence does not appear.</summary>
    public IReadOnlyList<DataTriple> Additions { get; }

    /// <summary>The triples present in the baseline world's graph and absent from the diffed world's. Net-effective, like <see cref="Additions"/>.</summary>
    public IReadOnlyList<DataTriple> Removals { get; }

    /// <summary>
    /// Constructs a decoded per-graph transition.
    /// </summary>
    /// <param name="graph">The graph's name term, or <see langword="null"/> for the default graph.</param>
    /// <param name="additions">The decoded additions.</param>
    /// <param name="removals">The decoded removals.</param>
    public WorldGraphTransition(RdfTerm? graph, IReadOnlyList<DataTriple> additions, IReadOnlyList<DataTriple> removals)
    {
        Graph = graph;
        Additions = additions;
        Removals = removals;
    }
}
