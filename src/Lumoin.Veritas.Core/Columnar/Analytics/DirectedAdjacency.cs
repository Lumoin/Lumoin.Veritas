using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Columnar.Analytics;

/// <summary>
/// The directed graph in compressed-sparse-row form over a dense vertex numbering — the flat substrate the directed
/// traversals (strongly connected components, single-source shortest paths) walk. Like <see cref="SymmetricAdjacency"/>
/// it holds the whole out-adjacency in three contiguous one-dimensional arrays rather than a jagged
/// <c>int[][]</c>: <see cref="VertexAt"/> maps a dense index to its encoded term id, and each vertex's out-neighbor
/// run is a slice of one shared <c>neighbors</c> array. The contiguous layout keeps a neighbor scan sequential and
/// cache-resident — no per-vertex array to chase a pointer to. Vertices are numbered in first-sight order; each
/// run holds its out-neighbors' dense indices, ascending, distinct (parallel edges collapsed upstream).
/// </summary>
internal sealed class DirectedAdjacency
{
    /// <summary>The encoded term id of each dense vertex — dense index <c>i</c> is term <see cref="VertexAt"/>(i).</summary>
    private readonly uint[] vertices;

    /// <summary>The CSR row pointers: dense vertex <c>i</c>'s out-neighbor run is <c>neighbors[offsets[i] .. offsets[i + 1])</c>. Length is <see cref="Count"/> + 1.</summary>
    private readonly int[] offsets;

    /// <summary>The concatenated out-neighbor runs as dense indices, each run ascending — the one contiguous block a traversal scans.</summary>
    private readonly int[] neighbors;

    /// <summary>The encoded-term-id to dense-index map, for resolving a query vertex (a shortest-path source) to its dense index.</summary>
    private readonly Dictionary<uint, int> denseByVertex;

    /// <summary>
    /// Flattens a dense-indexed collection of out-neighbor sets into the CSR arrays. The inputs are the build
    /// scaffolding (the same first-sight dense numbering the PageRank build uses); this materialises them as the flat
    /// contiguous form the traversals read.
    /// </summary>
    /// <param name="outNeighbors">Each dense vertex's out-neighbor set (dense indices), indexed by dense vertex.</param>
    /// <param name="nodeByDense">The dense-index to encoded-term-id list.</param>
    /// <param name="denseByVertex">The encoded-term-id to dense-index map; retained for <see cref="TryGetDense"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public DirectedAdjacency(IReadOnlyList<HashSet<int>> outNeighbors, IReadOnlyList<uint> nodeByDense, Dictionary<uint, int> denseByVertex)
    {
        ArgumentNullException.ThrowIfNull(outNeighbors);
        ArgumentNullException.ThrowIfNull(nodeByDense);
        ArgumentNullException.ThrowIfNull(denseByVertex);

        int count = nodeByDense.Count;
        vertices = new uint[count];
        for(int i = 0; i < count; i++)
        {
            vertices[i] = nodeByDense[i];
        }

        offsets = new int[count + 1];
        for(int i = 0; i < count; i++)
        {
            offsets[i + 1] = offsets[i] + outNeighbors[i].Count;
        }

        neighbors = new int[offsets[count]];
        for(int i = 0; i < count; i++)
        {
            int write = offsets[i];
            foreach(int neighbor in outNeighbors[i])
            {
                neighbors[write] = neighbor;
                write++;
            }

            //Each run is sorted so a vertex's out-neighbors are scanned in a stable, ascending order.
            neighbors.AsSpan(offsets[i], outNeighbors[i].Count).Sort();
        }

        this.denseByVertex = denseByVertex;
    }

    /// <summary>The number of vertices — the edge-induced vertex set.</summary>
    public int Count => vertices.Length;

    /// <summary>The encoded term id of dense vertex <paramref name="dense"/>.</summary>
    /// <param name="dense">The dense vertex index.</param>
    /// <returns>The vertex's encoded term id.</returns>
    public uint VertexAt(int dense)
    {
        return vertices[dense];
    }

    /// <summary>Resolves an encoded term id to its dense index.</summary>
    /// <param name="vertex">The encoded term id.</param>
    /// <param name="dense">On success, the vertex's dense index.</param>
    /// <returns><see langword="true"/> when the vertex is in the edge-induced set.</returns>
    public bool TryGetDense(uint vertex, out int dense)
    {
        return denseByVertex.TryGetValue(vertex, out dense);
    }

    /// <summary>The out-neighbor run of dense vertex <paramref name="dense"/> as a span of dense indices, ascending — the contiguous view a traversal iterates.</summary>
    /// <param name="dense">The dense vertex index.</param>
    /// <returns>The vertex's out-neighbors as dense indices.</returns>
    public ReadOnlySpan<int> NeighborsOf(int dense)
    {
        return neighbors.AsSpan(offsets[dense], offsets[dense + 1] - offsets[dense]);
    }
}
