using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Core.Columnar.Analytics;

/// <summary>
/// Unweighted shortest-path centrality over a <see cref="SymmetricAdjacency"/> by breadth-first search: closeness
/// (how near a node is to the rest of its component) and betweenness (how many shortest paths run through a node,
/// by Brandes' accumulation). Both are all-sources traversals — the heavier analytics tier — computed directly over
/// the dense CSR; edges are unweighted (weighted shortest paths await an edge-weight model). The traversals are
/// iterative with an explicit queue and visitation-order array; there is no call recursion.
/// </summary>
internal static class GraphCentrality
{
    /// <summary>
    /// Fills <paramref name="closeness"/> with the closeness centrality of every dense vertex: the number of other
    /// vertices it reaches over its component divided by the total hop distance to them, or zero when it reaches
    /// none. One breadth-first search per source, over pooled scratch buffers.
    /// </summary>
    /// <param name="adjacency">The dense undirected adjacency.</param>
    /// <param name="closeness">The per-dense-vertex closeness span to fill (one entry per vertex); every entry is written.</param>
    /// <param name="cancellationToken">Cancellation token, checked once per source.</param>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static void Closeness(SymmetricAdjacency adjacency, Span<double> closeness, CancellationToken cancellationToken)
    {
        int count = adjacency.Count;
        if(count == 0)
        {
            return;
        }

        using IMemoryOwner<int> distanceOwner = VeritasMemoryPool<int>.Shared.Rent(count);
        using IMemoryOwner<int> queueOwner = VeritasMemoryPool<int>.Shared.Rent(count);
        Span<int> distance = distanceOwner.Memory.Span;
        Span<int> queue = queueOwner.Memory.Span;

        for(int source = 0; source < count; source++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            distance.Fill(-1);
            distance[source] = 0;

            int head = 0;
            int tail = 0;
            queue[tail++] = source;
            long total = 0;
            int reached = 0;

            while(head < tail)
            {
                int current = queue[head++];
                foreach(int neighbor in adjacency.NeighborsOf(current))
                {
                    if(distance[neighbor] < 0)
                    {
                        distance[neighbor] = distance[current] + 1;
                        total += distance[neighbor];
                        reached++;
                        queue[tail++] = neighbor;
                    }
                }
            }

            closeness[source] = total > 0 ? reached / (double)total : 0.0;
        }
    }

    /// <summary>
    /// Fills <paramref name="betweenness"/> with the betweenness centrality of every dense vertex by Brandes'
    /// algorithm over unweighted breadth-first searches: each source accumulates, in reverse breadth-first order, the
    /// dependency of all pairs whose shortest paths pass through each vertex. Undirected, so each unordered pair is
    /// discovered from both endpoints and the totals are halved. Raw shortest-path counts (not normalised). Over
    /// pooled scratch buffers.
    /// </summary>
    /// <param name="adjacency">The dense undirected adjacency.</param>
    /// <param name="betweenness">The per-dense-vertex betweenness span to fill (one entry per vertex); cleared first, since the algorithm accumulates into it.</param>
    /// <param name="cancellationToken">Cancellation token, checked once per source.</param>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static void Betweenness(SymmetricAdjacency adjacency, Span<double> betweenness, CancellationToken cancellationToken)
    {
        int count = adjacency.Count;
        if(count == 0)
        {
            return;
        }

        //The result span is accumulated into, so it must start at zero — pooled memory carries prior contents.
        betweenness.Clear();

        using IMemoryOwner<int> distanceOwner = VeritasMemoryPool<int>.Shared.Rent(count);
        using IMemoryOwner<double> pathCountOwner = VeritasMemoryPool<double>.Shared.Rent(count);
        using IMemoryOwner<double> dependencyOwner = VeritasMemoryPool<double>.Shared.Rent(count);
        using IMemoryOwner<int> orderOwner = VeritasMemoryPool<int>.Shared.Rent(count);
        using IMemoryOwner<int> queueOwner = VeritasMemoryPool<int>.Shared.Rent(count);
        Span<int> distance = distanceOwner.Memory.Span;
        Span<double> pathCount = pathCountOwner.Memory.Span;
        Span<double> dependency = dependencyOwner.Memory.Span;
        Span<int> order = orderOwner.Memory.Span;
        Span<int> queue = queueOwner.Memory.Span;

        List<int>[] predecessors = new List<int>[count];
        for(int i = 0; i < count; i++)
        {
            predecessors[i] = [];
        }

        for(int source = 0; source < count; source++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            distance.Fill(-1);
            pathCount.Clear();
            dependency.Clear();
            for(int i = 0; i < count; i++)
            {
                predecessors[i].Clear();
            }

            distance[source] = 0;
            pathCount[source] = 1.0;

            int head = 0;
            int tail = 0;
            int visited = 0;
            queue[tail++] = source;

            while(head < tail)
            {
                int current = queue[head++];
                order[visited++] = current;
                foreach(int neighbor in adjacency.NeighborsOf(current))
                {
                    if(distance[neighbor] < 0)
                    {
                        distance[neighbor] = distance[current] + 1;
                        queue[tail++] = neighbor;
                    }

                    if(distance[neighbor] == distance[current] + 1)
                    {
                        pathCount[neighbor] += pathCount[current];
                        predecessors[neighbor].Add(current);
                    }
                }
            }

            for(int i = visited - 1; i >= 0; i--)
            {
                int reached = order[i];
                foreach(int predecessor in predecessors[reached])
                {
                    dependency[predecessor] += (pathCount[predecessor] / pathCount[reached]) * (1.0 + dependency[reached]);
                }

                if(reached != source)
                {
                    betweenness[reached] += dependency[reached];
                }
            }
        }

        for(int i = 0; i < count; i++)
        {
            betweenness[i] /= 2.0;
        }
    }
}
