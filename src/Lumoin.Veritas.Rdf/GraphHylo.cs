using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// Hylomorphism: an unfold immediately consumed by a fold, without
/// materialising the intermediate graph.
/// </summary>
/// <remarks>
/// <para>
/// A hylomorphism is the fusion of a <see cref="GraphAlgebras.GraphCoalgebra{TSeed}"/>
/// (unfold step) and a <see cref="GraphAlgebras.GraphAlgebra{TResult}"/>
/// (node-level fold step) into a single operation. Each seed is expanded into
/// a triple plus neighbour seeds; the neighbours are expanded in turn; when a
/// node's outgoing expansions are fully processed, the algebra is applied with
/// the accumulated child results to produce the fold result for that node.
/// </para>
/// <para>
/// The canonical RDF use case: stream a domain object through a coalgebra that
/// turns it into expanded triples, and immediately fold those triples into a
/// SHACL validation result without ever storing the full expansion. The same
/// applies to JSON-LD streaming — expand JSON into quads, fold quads into a
/// canonicalisation hash — with no intermediate quad collection.
/// </para>
/// <para>
/// The implementation is non-recursive: a stack of frames tracks each seed's
/// expansion state and accumulated child results. When a frame's children are
/// fully processed, its algebra is applied and the result is fed back into the
/// parent frame's child-result list.
/// </para>
/// <para>
/// Each frame represents one expanded node. The node's identity for the
/// algebra call is the <c>Subject</c> of the triple that the coalgebra produced
/// when expanding this seed. A coalgebra that returns a triple whose subject
/// varies across invocations effectively produces a different node per
/// invocation; the algebra is then called once per such invocation.
/// </para>
/// <para>
/// Returning the overload result as a nullable is intentional: the coalgebra
/// may return an entirely empty expansion for the initial seed, in which case
/// there is no fold result to report.
/// </para>
/// </remarks>
public static class GraphHylo
{
    /// <summary>
    /// Runs a hylomorphism: expand <paramref name="seed"/> through
    /// <paramref name="coalgebra"/>, folding each produced triple through
    /// <paramref name="algebra"/> bottom-up.
    /// </summary>
    /// <typeparam name="TSeed">The seed value type.</typeparam>
    /// <typeparam name="TResult">The type produced by the fold.</typeparam>
    /// <param name="seed">The initial seed.</param>
    /// <param name="coalgebra">The expansion step.</param>
    /// <param name="algebra">The reduction step.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="HyloOutcome{TResult}"/> whose <see cref="HyloOutcome{TResult}.HasResult"/>
    /// is <c>false</c> if the seed produced no expansion at all, otherwise
    /// <c>true</c> and <see cref="HyloOutcome{TResult}.Result"/> holds the fold result.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="coalgebra"/> or <paramref name="algebra"/> is <c>null</c>.
    /// </exception>
    public static ValueTask<HyloOutcome<TResult>> HyloAsync<TSeed, TResult>(
        TSeed seed,
        GraphAlgebras.GraphCoalgebra<TSeed> coalgebra,
        GraphAlgebras.GraphAlgebra<TResult> algebra,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coalgebra);
        ArgumentNullException.ThrowIfNull(algebra);

        GraphExpansion<TSeed> rootExpansion = coalgebra(seed);
        if(!rootExpansion.Triple.HasValue && rootExpansion.Seeds.Count == 0)
        {
            return ValueTask.FromResult(HyloOutcome<TResult>.Empty);
        }

        Stack<HyloFrame<TSeed, TResult>> stack = new();
        stack.Push(BuildFrame<TSeed, TResult>(rootExpansion, coalgebra));

        TResult finalResult = default!;
        bool haveFinalResult = false;

        while(stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HyloFrame<TSeed, TResult> top = stack.Peek();

            if(top.NextChildIndex < top.PendingChildren.Count)
            {
                GraphExpansion<TSeed> childExpansion = top.PendingChildren[top.NextChildIndex];
                top.NextChildIndex++;

                if(!childExpansion.Triple.HasValue && childExpansion.Seeds.Count == 0)
                {
                    //Pruned: contributes nothing.
                    continue;
                }

                stack.Push(BuildFrame<TSeed, TResult>(childExpansion, coalgebra));
                continue;
            }

            stack.Pop();

            if(top.Triple.HasValue)
            {
                TermId nodeId = top.Triple.Value.Subject;
                TResult result = algebra(nodeId, top.OutgoingTriples, top.ChildResults);

                if(stack.Count > 0)
                {
                    HyloFrame<TSeed, TResult> parent = stack.Peek();
                    parent.OutgoingTriples.Add(top.Triple.Value);
                    parent.ChildResults.Add(result);
                }
                else
                {
                    finalResult = result;
                    haveFinalResult = true;
                }
            }
            else if(stack.Count > 0)
            {
                //Grouping seed with no triple of its own: propagate accumulated outgoing
                //triples and child results upward to the parent frame.
                HyloFrame<TSeed, TResult> parent = stack.Peek();
                for(int i = 0; i < top.OutgoingTriples.Count; i++)
                {
                    parent.OutgoingTriples.Add(top.OutgoingTriples[i]);
                    parent.ChildResults.Add(top.ChildResults[i]);
                }
            }
        }

        return ValueTask.FromResult(haveFinalResult
            ? new HyloOutcome<TResult>(true, finalResult)
            : HyloOutcome<TResult>.Empty);
    }

    private static HyloFrame<TSeed, TResult> BuildFrame<TSeed, TResult>(
        GraphExpansion<TSeed> expansion,
        GraphAlgebras.GraphCoalgebra<TSeed> coalgebra)
    {
        List<GraphExpansion<TSeed>> pendingChildren = new(expansion.Seeds.Count);
        foreach(TSeed seed in expansion.Seeds)
        {
            pendingChildren.Add(coalgebra(seed));
        }

        return new HyloFrame<TSeed, TResult>(expansion.Triple, pendingChildren);
    }

    private sealed class HyloFrame<TSeed, TResult>(
        EncodedTriple? triple,
        IReadOnlyList<GraphExpansion<TSeed>> pendingChildren)
    {
        public EncodedTriple? Triple { get; } = triple;

        public IReadOnlyList<GraphExpansion<TSeed>> PendingChildren { get; } = pendingChildren;

        public int NextChildIndex { get; set; }

        public List<EncodedTriple> OutgoingTriples { get; } = [];

        public List<TResult> ChildResults { get; } = [];
    }
}
