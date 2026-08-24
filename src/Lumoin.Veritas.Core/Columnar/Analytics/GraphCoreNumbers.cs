using System;
using System.Buffers;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Core.Columnar.Analytics;

/// <summary>
/// The k-core decomposition (Batagelj–Zaversnik) over a <see cref="SymmetricAdjacency"/>: each vertex's core number,
/// the largest <c>k</c> for which the vertex belongs to the k-core — the maximal subgraph in which every vertex has
/// degree at least <c>k</c>. Computed iteratively in near-linear time by repeatedly peeling a minimum-degree vertex
/// with bin-sorted degree buckets (no sorting per step, no recursion); the maximum core number is the graph's
/// degeneracy. Undirected, over the dense vertex numbering.
/// </summary>
internal static class GraphCoreNumbers
{
    /// <summary>Fills <paramref name="core"/> with the core number of every dense vertex of <paramref name="adjacency"/>, indexed by dense vertex. The span doubles as the working degree array — each entry is initialised from the vertex's degree and decremented to its core number.</summary>
    /// <param name="adjacency">The dense undirected adjacency.</param>
    /// <param name="core">The per-dense-vertex core-number span to fill (one entry per vertex).</param>
    /// <exception cref="ArgumentNullException"><paramref name="adjacency"/> is <see langword="null"/>.</exception>
    public static void Compute(SymmetricAdjacency adjacency, Span<int> core)
    {
        ArgumentNullException.ThrowIfNull(adjacency);

        int count = adjacency.Count;
        if(count == 0)
        {
            return;
        }

        int maxDegree = 0;
        for(int vertex = 0; vertex < count; vertex++)
        {
            core[vertex] = adjacency.NeighborCountOf(vertex);
            if(core[vertex] > maxDegree)
            {
                maxDegree = core[vertex];
            }
        }

        using IMemoryOwner<int> binOwner = VeritasMemoryPool<int>.Shared.Rent(maxDegree + 1);
        using IMemoryOwner<int> positionOwner = VeritasMemoryPool<int>.Shared.Rent(count);
        using IMemoryOwner<int> vertexAtOwner = VeritasMemoryPool<int>.Shared.Rent(count);
        Span<int> bin = binOwner.Memory.Span;
        Span<int> position = positionOwner.Memory.Span;
        Span<int> vertexAt = vertexAtOwner.Memory.Span;

        //Bin-sort the vertices by degree. bin[d] is built up to the start offset of the degree-d class in vertexAt;
        //it is built by counting from zero, so the pooled span must start cleared.
        bin.Clear();
        for(int vertex = 0; vertex < count; vertex++)
        {
            bin[core[vertex]]++;
        }

        int start = 0;
        for(int d = 0; d <= maxDegree; d++)
        {
            int classCount = bin[d];
            bin[d] = start;
            start += classCount;
        }

        for(int vertex = 0; vertex < count; vertex++)
        {
            position[vertex] = bin[core[vertex]];
            vertexAt[position[vertex]] = vertex;
            bin[core[vertex]]++;
        }

        //Restore every bin to the start of its degree class, undoing the placement increments above.
        for(int d = maxDegree; d >= 1; d--)
        {
            bin[d] = bin[d - 1];
        }

        bin[0] = 0;

        //Peel vertices in nondecreasing current-degree order. A vertex's degree when it is peeled is its core
        //number, and a higher-degree neighbor is moved down one degree class, so core[] holds the core numbers
        //once every vertex is peeled.
        for(int i = 0; i < count; i++)
        {
            int vertex = vertexAt[i];
            foreach(int neighbor in adjacency.NeighborsOf(vertex))
            {
                if(core[neighbor] <= core[vertex])
                {
                    continue;
                }

                int neighborDegree = core[neighbor];
                int neighborPosition = position[neighbor];
                int classStart = bin[neighborDegree];
                int classStartVertex = vertexAt[classStart];
                if(neighbor != classStartVertex)
                {
                    //Swap the neighbor with the first vertex of its degree class.
                    position[neighbor] = classStart;
                    vertexAt[neighborPosition] = classStartVertex;
                    position[classStartVertex] = neighborPosition;
                    vertexAt[classStart] = neighbor;
                }

                bin[neighborDegree]++;
                core[neighbor]--;
            }
        }
    }
}
