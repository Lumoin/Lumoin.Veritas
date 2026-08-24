using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Columnar.Analytics;

/// <summary>
/// The undirected graph in compressed-sparse-row form over a dense vertex numbering — the substrate the leapfrog
/// clique enumerator intersects. Vertices are the edge-induced set, numbered 0..<see cref="Count"/> ascending by
/// encoded id, so a dense index's order matches its term's order. Each vertex's neighbor run is the sorted dense
/// indices it shares an edge with under the chosen connectivity (either-way for an undirected clique, both-ways
/// for a mutual one); parallel edges collapse and self-loops are dropped upstream, so a run holds distinct
/// vertices and never the vertex itself.
/// </summary>
internal sealed class SymmetricAdjacency
{
    /// <summary>The encoded term id of each dense vertex, ascending — dense index <c>i</c> is term <see cref="VertexAt"/>(i).</summary>
    private readonly uint[] vertices;

    /// <summary>The CSR row pointers: dense vertex <c>i</c>'s neighbor run is <c>neighbors[offsets[i] .. offsets[i + 1])</c>. Length is <see cref="Count"/> + 1.</summary>
    private readonly int[] offsets;

    /// <summary>The concatenated neighbor runs as dense indices, each run ascending.</summary>
    private readonly int[] neighbors;

    /// <summary>Builds the CSR adjacency from an undirected adjacency keyed by encoded term id.</summary>
    /// <param name="adjacency">Each vertex mapped to its distinct neighbors, ascending — as produced for the triangle and clustering metrics (either-way) or for mutual cliques (both-ways).</param>
    /// <exception cref="ArgumentNullException"><paramref name="adjacency"/> is <see langword="null"/>.</exception>
    public SymmetricAdjacency(Dictionary<uint, SortedSet<uint>> adjacency)
    {
        ArgumentNullException.ThrowIfNull(adjacency);

        vertices = new uint[adjacency.Count];
        int v = 0;
        foreach(uint vertex in adjacency.Keys)
        {
            vertices[v] = vertex;
            v++;
        }

        Array.Sort(vertices);

        Dictionary<uint, int> denseByVertex = new(vertices.Length);
        for(int i = 0; i < vertices.Length; i++)
        {
            denseByVertex[vertices[i]] = i;
        }

        offsets = new int[vertices.Length + 1];
        for(int i = 0; i < vertices.Length; i++)
        {
            offsets[i + 1] = offsets[i] + adjacency[vertices[i]].Count;
        }

        neighbors = new int[offsets[vertices.Length]];
        for(int i = 0; i < vertices.Length; i++)
        {
            int write = offsets[i];

            //The source vertices are sorted ascending and the dense numbering is that same order, so a neighbor
            //set already ascending by encoded id maps to a run already ascending by dense index — no resort.
            foreach(uint neighbor in adjacency[vertices[i]])
            {
                neighbors[write] = denseByVertex[neighbor];
                write++;
            }
        }
    }

    /// <summary>The number of vertices — the edge-induced vertex set under the chosen connectivity.</summary>
    public int Count => vertices.Length;

    /// <summary>The encoded term id of dense vertex <paramref name="dense"/>.</summary>
    /// <param name="dense">The dense vertex index.</param>
    /// <returns>The vertex's encoded term id.</returns>
    public uint VertexAt(int dense)
    {
        return vertices[dense];
    }

    /// <summary>The number of neighbors of dense vertex <paramref name="dense"/>.</summary>
    /// <param name="dense">The dense vertex index.</param>
    /// <returns>The vertex's neighbor count.</returns>
    public int NeighborCountOf(int dense)
    {
        return offsets[dense + 1] - offsets[dense];
    }

    /// <summary>A forward cursor over dense vertex <paramref name="dense"/>'s neighbor run.</summary>
    /// <param name="dense">The dense vertex index.</param>
    /// <returns>A cursor positioned at the run's first neighbor.</returns>
    public NeighborCursor CursorFor(int dense)
    {
        return new NeighborCursor(neighbors, offsets[dense], offsets[dense + 1]);
    }

    /// <summary>The neighbor run of dense vertex <paramref name="dense"/> as a span of dense indices, ascending — the direct view the centrality breadth-first traversals iterate.</summary>
    /// <param name="dense">The dense vertex index.</param>
    /// <returns>The vertex's neighbors as dense indices.</returns>
    public ReadOnlySpan<int> NeighborsOf(int dense)
    {
        return neighbors.AsSpan(offsets[dense], offsets[dense + 1] - offsets[dense]);
    }
}
