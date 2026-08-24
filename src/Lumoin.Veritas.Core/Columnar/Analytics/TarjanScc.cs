using System;
using System.Buffers;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Core.Columnar.Analytics;

/// <summary>
/// Tarjan's strongly-connected-components algorithm over a dense directed adjacency, computed iteratively with an
/// explicit work stack in place of call recursion: each maximal set of vertices all mutually reachable along
/// directed edges forms one component. Vertices are <c>0..n</c> dense indices; <see cref="Compute"/> returns each
/// component as the dense indices it holds. The partition a directed graph induces is unique, so the caller orders
/// the components and their members for a deterministic result.
/// </summary>
internal static class TarjanScc
{
    /// <summary>
    /// The strongly connected components of the directed graph <paramref name="adjacency"/>. Each returned list is one
    /// component's dense vertex indices in the order Tarjan emits them (a component root last); the caller sorts for
    /// determinism.
    /// </summary>
    /// <param name="adjacency">The directed CSR adjacency.</param>
    /// <returns>The components, each a list of dense vertex indices.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="adjacency"/> is <see langword="null"/>.</exception>
    public static List<List<int>> Compute(DirectedAdjacency adjacency)
    {
        ArgumentNullException.ThrowIfNull(adjacency);

        int count = adjacency.Count;
        List<List<int>> components = [];
        if(count == 0)
        {
            return components;
        }

        using IMemoryOwner<int> indexOwner = VeritasMemoryPool<int>.Shared.Rent(count);
        using IMemoryOwner<int> lowLinkOwner = VeritasMemoryPool<int>.Shared.Rent(count);
        using IMemoryOwner<bool> onStackOwner = VeritasMemoryPool<bool>.Shared.Rent(count);
        using IMemoryOwner<int> tarjanStackOwner = VeritasMemoryPool<int>.Shared.Rent(count);
        using IMemoryOwner<int> frameVertexOwner = VeritasMemoryPool<int>.Shared.Rent(count);
        using IMemoryOwner<int> frameChildOwner = VeritasMemoryPool<int>.Shared.Rent(count);
        Span<int> index = indexOwner.Memory.Span;
        Span<int> lowLink = lowLinkOwner.Memory.Span;
        Span<bool> onStack = onStackOwner.Memory.Span;
        index.Fill(-1);
        onStack.Clear();

        //The Tarjan stack: vertices reached on the current search whose component is not yet closed.
        Span<int> tarjanStack = tarjanStackOwner.Memory.Span;
        int tarjanTop = 0;

        //The explicit depth-first work stack — parallel vertex and next-child-cursor arrays standing in for call
        //frames, so the search never recurses. Its depth never exceeds the vertex count.
        Span<int> frameVertex = frameVertexOwner.Memory.Span;
        Span<int> frameChild = frameChildOwner.Memory.Span;
        int frameTop = 0;

        int nextIndex = 0;

        for(int root = 0; root < count; root++)
        {
            if(index[root] != -1)
            {
                continue;
            }

            frameVertex[frameTop] = root;
            frameChild[frameTop] = 0;
            frameTop++;

            while(frameTop > 0)
            {
                int vertex = frameVertex[frameTop - 1];
                int child = frameChild[frameTop - 1];

                if(child == 0)
                {
                    //First visit of this vertex: number it and push it on the Tarjan stack.
                    index[vertex] = nextIndex;
                    lowLink[vertex] = nextIndex;
                    nextIndex++;
                    tarjanStack[tarjanTop] = vertex;
                    tarjanTop++;
                    onStack[vertex] = true;
                }

                ReadOnlySpan<int> neighbors = adjacency.NeighborsOf(vertex);
                int descendInto = -1;
                while(child < neighbors.Length)
                {
                    int next = neighbors[child];
                    if(index[next] == -1)
                    {
                        descendInto = next;

                        break;
                    }

                    if(onStack[next] && index[next] < lowLink[vertex])
                    {
                        lowLink[vertex] = index[next];
                    }

                    child++;
                }

                if(descendInto != -1)
                {
                    //Resume this vertex after the descended edge, then push the child frame.
                    frameChild[frameTop - 1] = child + 1;
                    frameVertex[frameTop] = descendInto;
                    frameChild[frameTop] = 0;
                    frameTop++;

                    continue;
                }

                //Every edge scanned: a vertex whose low-link equals its index is a component root — pop the Tarjan
                //stack down to it to close the component.
                if(lowLink[vertex] == index[vertex])
                {
                    List<int> component = [];
                    int popped;
                    do
                    {
                        tarjanTop--;
                        popped = tarjanStack[tarjanTop];
                        onStack[popped] = false;
                        component.Add(popped);
                    }
                    while(popped != vertex);

                    components.Add(component);
                }

                //Pop this frame and fold its low-link into its parent's.
                frameTop--;
                if(frameTop > 0)
                {
                    int parent = frameVertex[frameTop - 1];
                    if(lowLink[vertex] < lowLink[parent])
                    {
                        lowLink[parent] = lowLink[vertex];
                    }
                }
            }
        }

        return components;
    }
}
