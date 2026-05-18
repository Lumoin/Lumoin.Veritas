using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// Catamorphism (fold) over the subgraph reachable from a given root node.
/// </summary>
/// <remarks>
/// <para>
/// A catamorphism is a bottom-up fold: each node is reduced only after all of
/// its outward neighbours have been reduced. The caller supplies a
/// <see cref="GraphAlgebras.GraphAlgebra{TResult}"/> that defines the per-node
/// reduction.
/// </para>
/// <para>
/// The implementation uses two explicit stack-based passes and never recurses:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///       <b>Discovery pass:</b> an iterative depth-first search from the root
///       records each reachable node's outgoing triples. A visited set prevents
///       cycles and repeated work. Nodes are recorded in post-order — children
///       before parents.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Reduction pass:</b> process nodes in post-order, consulting a
///       dictionary of already-folded results for each outgoing edge's object.
///       A node appears in the reduction dictionary once; multiple subjects
///       sharing the same object see the same folded value for that object.
///     </description>
///   </item>
/// </list>
/// <para>
/// Objects that never appear as a subject in the traversal (pure leaves) are
/// not reduced — the algebra receives <c>default(TResult)</c> for their child
/// result slot.
/// </para>
/// </remarks>
public static class GraphFold
{
    /// <summary>
    /// Folds the subgraph reachable from <paramref name="rootNodeId"/> through
    /// outgoing triples, returning the algebra's result for the root node.
    /// </summary>
    /// <typeparam name="TResult">The type produced by the fold.</typeparam>
    /// <param name="rootNodeId">The encoded identifier of the root node.</param>
    /// <param name="algebra">The per-node reduction step.</param>
    /// <param name="match">The pattern match delegate over the graph.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The algebra's result for the root node. The root is always reduced (even
    /// if it has no outgoing triples), so this is always a genuine algebra
    /// result rather than <c>default(TResult)</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="algebra"/> or <paramref name="match"/> is <c>null</c>.
    /// </exception>
    public static async ValueTask<TResult> FoldAsync<TResult>(
        TermId rootNodeId,
        GraphAlgebras.GraphAlgebra<TResult> algebra,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(algebra);
        ArgumentNullException.ThrowIfNull(match);

        //Discovery: collect each reachable node's outgoing triples, keyed by node id.
        //Also record the post-order so the reduction pass sees children before parents.
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

        //Reduction: process each node in post-order. When reducing a node, its children
        //are already in the dictionary because they appeared earlier in post-order.
        Dictionary<TermId, TResult> foldedByNode = [];
        foreach(TermId nodeId in postOrder)
        {
            IReadOnlyList<EncodedTriple> outgoing = outgoingByNode[nodeId];
            List<TResult> childResults = new(outgoing.Count);
            foreach(EncodedTriple triple in outgoing)
            {
                //Leaf objects that were never discovered as subjects are not in
                //foldedByNode; the child-result slot is default(TResult).
                if(foldedByNode.TryGetValue(triple.Object, out TResult? childResult))
                {
                    childResults.Add(childResult);
                }
                else
                {
                    childResults.Add(default!);
                }
            }

            foldedByNode[nodeId] = algebra(nodeId, outgoing, childResults);
        }

        //The root is always in postOrder because we seeded the stack with it.
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
