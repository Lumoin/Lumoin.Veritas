using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// Paramorphism: a node-level bottom-up fold whose algebra sees, for each
/// outgoing edge, both the original triple and the folded result of its
/// object's subtree.
/// </summary>
/// <remarks>
/// <para>
/// Paramorphisms generalise catamorphisms. A catamorphism's algebra only sees
/// the already-folded child results; a paramorphism's algebra also sees the
/// unfolded original triples. This is required whenever the fold result must
/// refer back to the input structure — for example, when OWL inference emits
/// new triples that quote their premises, or when a SHACL validation result
/// needs to record which original triple caused a violation.
/// </para>
/// <para>
/// The implementation mirrors <see cref="GraphFold"/>: a two-pass iterative
/// algorithm with an explicit <see cref="Stack{T}"/> for the discovery pass
/// and a post-order walk for the reduction pass. The only difference is that
/// the per-node algebra call receives a list of <c>(triple, childResult)</c>
/// pairs instead of two parallel lists.
/// </para>
/// </remarks>
public static class GraphPara
{
    /// <summary>
    /// Folds the subgraph reachable from <paramref name="rootNodeId"/> through
    /// outgoing triples, returning the paramorphism algebra's result for the
    /// root node.
    /// </summary>
    /// <typeparam name="TResult">The type produced by the fold.</typeparam>
    /// <param name="rootNodeId">The encoded identifier of the root node.</param>
    /// <param name="algebra">The per-node paramorphism step.</param>
    /// <param name="match">The pattern match delegate over the graph.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The algebra's result for the root node.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="algebra"/> or <paramref name="match"/> is <c>null</c>.
    /// </exception>
    public static async ValueTask<TResult> ParaAsync<TResult>(
        TermId rootNodeId,
        GraphAlgebras.GraphParaAlgebra<TResult> algebra,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(algebra);
        ArgumentNullException.ThrowIfNull(match);

        Dictionary<TermId, IReadOnlyList<EncodedTriple>> outgoingByNode = [];
        List<TermId> postOrder = [];
        HashSet<TermId> discovered = [rootNodeId];

        Stack<DfsFrame> stack = new();
        DfsFrame initialFrame = await BuildFrameAsync(rootNodeId, match, cancellationToken).ConfigureAwait(false);
        stack.Push(initialFrame);
        outgoingByNode[rootNodeId] = initialFrame.Triples;

        while(stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DfsFrame top = stack.Peek();

            if(top.NextIndex >= top.Triples.Count)
            {
                postOrder.Add(top.NodeId);
                stack.Pop();
                continue;
            }

            EncodedTriple edge = top.Triples[top.NextIndex];
            top.NextIndex++;

            if(discovered.Add(edge.Object))
            {
                DfsFrame childFrame = await BuildFrameAsync(edge.Object, match, cancellationToken).ConfigureAwait(false);
                outgoingByNode[edge.Object] = childFrame.Triples;
                stack.Push(childFrame);
            }
        }

        Dictionary<TermId, TResult> foldedByNode = [];
        foreach(TermId nodeId in postOrder)
        {
            IReadOnlyList<EncodedTriple> outgoing = outgoingByNode[nodeId];
            List<(EncodedTriple Triple, TResult ChildResult)> edgesWithResults = new(outgoing.Count);
            foreach(EncodedTriple triple in outgoing)
            {
                if(foldedByNode.TryGetValue(triple.Object, out TResult? childResult))
                {
                    edgesWithResults.Add((triple, childResult));
                }
                else
                {
                    edgesWithResults.Add((triple, default!));
                }
            }

            foldedByNode[nodeId] = algebra(nodeId, edgesWithResults);
        }

        return foldedByNode[rootNodeId];
    }

    private static async ValueTask<DfsFrame> BuildFrameAsync(
        TermId nodeId,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken)
    {
        List<EncodedTriple> triples = [];
        await foreach(EncodedTriple triple in match(nodeId, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            triples.Add(triple);
        }

        return new DfsFrame(nodeId, triples, 0);
    }

    private sealed class DfsFrame(TermId nodeId, IReadOnlyList<EncodedTriple> triples, int nextIndex)
    {
        public TermId NodeId { get; } = nodeId;

        public IReadOnlyList<EncodedTriple> Triples { get; } = triples;

        public int NextIndex { get; set; } = nextIndex;
    }
}
