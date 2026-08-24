using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Columnar.Analytics;

/// <summary>The direction a triple's (subject, object) is read as a graph edge.</summary>
public enum GraphEdgeDirection
{
    /// <summary>Subject → object: a node's out-edges are the objects of the triples it is the subject of.</summary>
    Forward,

    /// <summary>Object → subject: a node's in-edges are the subjects of the triples it is the object of.</summary>
    Reverse,

    /// <summary>Both directions: every triple contributes an edge each way.</summary>
    Undirected,
}

/// <summary>How a vertex pair must be connected to count as a clique edge.</summary>
public enum CliqueConnectivity
{
    /// <summary>An edge either way connects the pair — the classical undirected clique, so a size-three clique is a triangle.</summary>
    Undirected,

    /// <summary>An edge both ways connects the pair — a reciprocal (mutual) clique, for directional relations such as value chains.</summary>
    Mutual,
}

/// <summary>
/// A graph projection over the multi-relational RDF index — which predicates count as edges, and the
/// direction a triple's (subject, object) is read as. RDF edges are typed by predicate, so every metric is
/// taken over a projection: the whole predicate set collapsed to one edge relation, a single predicate, or a
/// chosen subset (a cheap predicate slice off the index). Edge weights (attributed edges) are RDF 1.2
/// triple-term territory and are not expressed here yet.
/// </summary>
public sealed class GraphProjection
{
    /// <summary>The included predicate ids, or <see langword="null"/> when every predicate is an edge.</summary>
    private readonly ImmutableHashSet<TermId>? predicates;

    /// <summary>Constructs a projection over a predicate selection and a direction.</summary>
    /// <param name="predicates">The included predicate ids, or <see langword="null"/> for every predicate.</param>
    /// <param name="direction">The direction a triple is read as an edge.</param>
    private GraphProjection(ImmutableHashSet<TermId>? predicates, GraphEdgeDirection direction)
    {
        this.predicates = predicates;
        Direction = direction;
    }

    /// <summary>The direction a triple is read as an edge.</summary>
    public GraphEdgeDirection Direction { get; }

    /// <summary>Whether every predicate is an edge (no predicate filter) — the fast, offset-only degree path takes this.</summary>
    public bool IncludesEveryPredicate => predicates is null;

    /// <summary>A projection over every predicate — the whole graph collapsed to one edge relation.</summary>
    /// <param name="direction">The edge direction; <see cref="GraphEdgeDirection.Forward"/> by default.</param>
    /// <returns>The projection.</returns>
    public static GraphProjection AllPredicates(GraphEdgeDirection direction = GraphEdgeDirection.Forward)
    {
        return new GraphProjection(predicates: null, direction);
    }

    /// <summary>A projection over only the given predicates (a single predicate is a one-element set).</summary>
    /// <param name="predicates">The predicate ids that count as edges; must include at least one.</param>
    /// <param name="direction">The edge direction; <see cref="GraphEdgeDirection.Forward"/> by default.</param>
    /// <returns>The projection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicates"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="predicates"/> includes no predicate.</exception>
    public static GraphProjection ForPredicates(IEnumerable<TermId> predicates, GraphEdgeDirection direction = GraphEdgeDirection.Forward)
    {
        ArgumentNullException.ThrowIfNull(predicates);

        ImmutableHashSet<TermId> set = [.. predicates];
        if(set.IsEmpty)
        {
            throw new ArgumentException("A predicate projection must include at least one predicate.", nameof(predicates));
        }

        return new GraphProjection(set, direction);
    }

    /// <summary>Whether the predicate counts as an edge under this projection.</summary>
    /// <param name="predicate">The predicate id.</param>
    /// <returns><see langword="true"/> when the predicate is an edge.</returns>
    public bool IncludesPredicate(TermId predicate)
    {
        return predicates is null || predicates.Contains(predicate);
    }
}
