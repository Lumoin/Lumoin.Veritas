using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// Catamorphism (fold) with selective child evaluation. Each node's algebra
/// decides which of its outgoing children to fold by yielding
/// <see cref="ForceRequest"/>s to the driver.
/// </summary>
/// <remarks>
/// <para>
/// <b>Relationship to <see cref="GraphFold"/>.</b> Plain <c>GraphFold</c>
/// evaluates every reachable child before invoking any node's algebra.
/// <c>GraphKFold</c> instead hands each algebra a
/// <see cref="ChildHandles{TResult}"/> and lets it pull child results on
/// demand. An algebra that always forces every child produces results
/// identical to plain <c>GraphFold</c>, but with higher per-call overhead
/// (one iterator state-machine allocation per node). An algebra that
/// short-circuits — stops forcing once a sufficient condition is known —
/// avoids evaluating whole subgraphs. SHACL boolean combinators
/// (<c>sh:or</c>, <c>sh:and</c>, <c>sh:not</c>, <c>sh:xone</c>) need this.
/// </para>
/// <para>
/// <b>Algebra contract.</b> An algebra is a method returning
/// <c>IEnumerator&lt;ForceRequest&gt;</c>, typically written as a C#
/// iterator method (<c>yield return</c>). The method receives its node
/// identifier, its outgoing triples, and a <see cref="ChildHandles{TResult}"/>
/// through which it observes child results. The algebra:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///       Reads the outgoing triples to decide what children to force and
///       in what order.
///     </description>
///   </item>
///   <item>
///     <description>
///       For each child it wants folded, issues
///       <c>yield return ForceRequest.Force(childIndex)</c>. When the
///       algebra resumes after the yield, that child's value is available
///       via <see cref="ChildHandles{TResult}.Get"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       When ready, writes its result via
///       <see cref="ChildHandles{TResult}.SetResult"/> and completes the
///       iterator (end of method or <c>yield break</c>).
///     </description>
///   </item>
/// </list>
/// <para>
/// <b>Execution shape.</b> Two passes:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Discovery</b> (identical to <see cref="GraphFold"/>): an
///       iterative BFS from the root node records every reachable node's
///       outgoing triples, assigns each a local index in discovery order,
///       and builds the parallel arrays used by
///       <see cref="ReductionState{TResult}"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Reduction</b>: the driver maintains an explicit
///       <see cref="Stack{T}"/> of active algebra frames. Each step:
///       peek the top frame, call <c>MoveNext</c>, act on the yielded
///       <see cref="ForceRequest"/>. Force requests push child frames;
///       completions pop. When the root frame completes, its result is
///       the fold result.
///     </description>
///   </item>
/// </list>
/// <para>
/// <b>Recursion handling.</b> A node marked <see cref="ChildStatus.Computing"/>
/// that is force-requested a second time indicates a cycle the algebra
/// traverses. The driver throws
/// <see cref="InvalidOperationException"/> naming the offending node.
/// Callers who expect cycles and want them broken should detect the cycle
/// in their algebra (via an auxiliary visited set carried in the result
/// type, for instance).
/// </para>
/// <para>
/// <b>Memory pool.</b> The optional <see cref="VeritasMemoryPool{T}"/>
/// parameter is currently reserved — future iterations may use it for
/// driver-side buffer allocations. Callers may pass <c>null</c> today
/// without loss.
/// </para>
/// <para>
/// <b>No recursion, no closures.</b> The driver uses only explicit
/// <see cref="Stack{T}"/> and <see cref="Queue{T}"/>. Algebras must not
/// capture outer-scope variables in lambdas; iterator methods with
/// straight-line bodies are the expected form.
/// </para>
/// </remarks>
public static class GraphKFold
{
    /// <summary>
    /// Folds the subgraph reachable from <paramref name="rootNodeId"/> using
    /// an iterator-based algebra that selectively forces its children.
    /// </summary>
    /// <typeparam name="TResult">The fold result type.</typeparam>
    /// <param name="rootNodeId">Encoded id of the root node.</param>
    /// <param name="algebra">The algebra factory. Invoked once per reached node.</param>
    /// <param name="match">The pattern match delegate over the graph.</param>
    /// <param name="pool">
    /// Optional memory pool reserved for future driver-side allocations.
    /// May be <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the whole operation.</param>
    /// <returns>The algebra's result for the root node.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="algebra"/> or <paramref name="match"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// An algebra forced a child that is itself currently computing — a cycle
    /// the algebra does not break on its own.
    /// </exception>
    public static async ValueTask<TResult> FoldAsync<TResult>(
        TermId rootNodeId,
        GraphAlgebras.GraphKAlgebra<TResult> algebra,
        StorageDelegates.MatchTriplesAsync match,
        VeritasMemoryPool<byte>? pool = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(algebra);
        ArgumentNullException.ThrowIfNull(match);
        _ = pool; //Reserved; not used in this session. See remarks.

        //Discovery pass. Build the local-index mapping, outgoing-triples
        //slices, and child index table. BFS from rootNodeId, identical in
        //shape to GraphFold's discovery.
        Dictionary<TermId, int> nodeIdToLocal = new() { [rootNodeId] = 0 };
        List<EncodedTriple[]> outgoingByLocal = [];
        Queue<TermId> discoveryQueue = new();
        discoveryQueue.Enqueue(rootNodeId);

        while(discoveryQueue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TermId currentNodeId = discoveryQueue.Dequeue();
            List<EncodedTriple> triples = [];
            await foreach(EncodedTriple triple in match(currentNodeId, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                triples.Add(triple);
                if(!nodeIdToLocal.ContainsKey(triple.Object))
                {
                    nodeIdToLocal[triple.Object] = nodeIdToLocal.Count;
                    discoveryQueue.Enqueue(triple.Object);
                }
            }

            outgoingByLocal.Add(triples.ToArray());
        }

        //Build the flat child-slice table. For each node, its slice lists
        //the local indices of its children, in the same order as its
        //outgoing triples (so child index k corresponds to outgoing[k]).
        int nodeCount = outgoingByLocal.Count;
        int[] childSliceStart = new int[nodeCount];
        int[] childSliceCount = new int[nodeCount];
        int totalChildSlots = 0;
        for(int i = 0; i < nodeCount; i++)
        {
            childSliceStart[i] = totalChildSlots;
            childSliceCount[i] = outgoingByLocal[i].Length;
            totalChildSlots += outgoingByLocal[i].Length;
        }

        int[] childChildIdx = new int[totalChildSlots];
        for(int i = 0; i < nodeCount; i++)
        {
            EncodedTriple[] triples = outgoingByLocal[i];
            int sliceStart = childSliceStart[i];
            for(int k = 0; k < triples.Length; k++)
            {
                //Every reached object must have a local index — the discovery
                //pass enumerates all outgoing triples and assigns indices to
                //their objects as it goes.
                childChildIdx[sliceStart + k] = nodeIdToLocal[triples[k].Object];
            }
        }

        ReductionState<TResult> state = new(nodeCount, nodeIdToLocal, childSliceStart, childSliceCount, childChildIdx);

        //Reduction pass. Explicit stack of frames; each frame holds an
        //active algebra iterator and the static info needed to build the
        //ChildHandles it observes.
        Stack<ReductionFrame<TResult>> stack = new();
        stack.Push(BuildFrame(rootNodeId, localIndex: 0, state, outgoingByLocal[0], algebra));
        state.MarkComputing(0);

        while(stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReductionFrame<TResult> top = stack.Peek();

            bool moved = top.Iterator.MoveNext();
            if(!moved)
            {
                //Algebra completed. Result has been written via SetResult.
                //We don't re-check here — if the algebra violates the contract
                //by completing without SetResult, the default zero value of
                //nodeResult[i] is returned, which surfaces as a programmer
                //error in consumer tests.
                stack.Pop();
                top.Iterator.Dispose();

                if(stack.Count == 0)
                {
                    //Root completed. Return its result.
                    return state.GetNodeResult(top.LocalIndex);
                }

                continue;
            }

            ForceRequest request = top.Iterator.Current;
            switch(request.Kind)
            {
                case ForceRequestKind.Skip:
                {
                    //Nothing to do; loop re-peeks and advances the algebra.
                    break;
                }

                case ForceRequestKind.Force:
                {
                    int childIdx = request.ChildIndex;
                    if((uint)childIdx >= (uint)top.OutgoingTriples.Length)
                    {
                        throw new InvalidOperationException(
                            $"Algebra for node {top.NodeId} requested child index {childIdx} out of range [0, {top.OutgoingTriples.Length}).");
                    }

                    int sliceStart = state.GetChildSlice(top.LocalIndex).Start;
                    int childLocalIdx = state.GetChildLocalIndex(sliceStart + childIdx);
                    ChildStatus childStatus = state.GetNodeStatus(childLocalIdx);

                    switch(childStatus)
                    {
                        case ChildStatus.Computed:
                        {
                            //Already available; algebra polls it on next resumption.
                            break;
                        }

                        case ChildStatus.Computing:
                        {
                            //Recursion. The algebra is asking for a node that is
                            //currently being reduced above it on the stack. We
                            //don't have fixpoint semantics; surface as error.
                            throw new InvalidOperationException(
                                $"Fold detected recursion at node {top.OutgoingTriples[childIdx].Object}. " +
                                "The algebra force-requested a node whose reduction is already in progress. " +
                                "Consumers that expect cycles must break them in the algebra itself.");
                        }

                        case ChildStatus.NotComputed:
                        {
                            TermId childEncodedId = top.OutgoingTriples[childIdx].Object;
                            stack.Push(BuildFrame(childEncodedId, childLocalIdx, state, outgoingByLocal[childLocalIdx], algebra));
                            state.MarkComputing(childLocalIdx);
                            break;
                        }

                        default:
                        {
                            throw new InvalidOperationException($"Unknown child status: {childStatus}.");
                        }
                    }

                    break;
                }

                default:
                {
                    throw new InvalidOperationException($"Unknown ForceRequest kind: {request.Kind}.");
                }
            }
        }

        //Unreachable: the loop returns from inside when the root pops.
        throw new InvalidOperationException("GraphKFold reduction ended without producing a root result.");
    }

    private static ReductionFrame<TResult> BuildFrame<TResult>(
        TermId nodeId,
        int localIndex,
        ReductionState<TResult> state,
        EncodedTriple[] outgoingTriples,
        GraphAlgebras.GraphKAlgebra<TResult> algebra)
    {
        (int sliceStart, int sliceCount) = state.GetChildSlice(localIndex);
        ChildHandles<TResult> handles = new(state, sliceStart, sliceCount, localIndex);
        IEnumerator<ForceRequest> iterator = algebra(nodeId, outgoingTriples, handles);
        return new ReductionFrame<TResult>(nodeId, localIndex, outgoingTriples, iterator);
    }
}
