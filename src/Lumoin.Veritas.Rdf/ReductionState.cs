using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// Driver-owned state for a single <see cref="GraphKFold"/> reduction pass.
/// </summary>
/// <remarks>
/// <para>
/// One instance exists per <c>FoldAsync</c> call. Its role is to hold the
/// per-node status and folded result, plus the mapping from encoded node
/// identifiers to local indices. The reduction driver and the algebra's
/// <see cref="ChildHandles{TResult}"/> view both read and write through
/// this type; algebras never see it directly.
/// </para>
/// <para>
/// Local node indices are assigned during the discovery pass, one per
/// reachable node, in discovery order. The root is always index zero. All
/// subsequent bookkeeping uses these indices so per-node lookups are
/// constant-time array reads.
/// </para>
/// <para>
/// The class is not thread-safe. One fold runs on one thread.
/// </para>
/// </remarks>
/// <typeparam name="TResult">The fold's result type.</typeparam>
internal sealed class ReductionState<TResult>
{
    //Parallel arrays indexed by local node id (assigned in discovery order).
    //
    //Node-level:
    //  _nodeStatus[i]   = status for node i (aggregate; becomes Computed when SetNodeResult runs)
    //  _nodeResult[i]   = folded result for node i (valid once _nodeStatus[i] == Computed)
    //
    //Per-node outgoing-children slice metadata:
    //  _childSliceStart[i] = index into the _childChildIdx array where node i's children begin
    //  _childSliceCount[i] = number of outgoing children for node i
    //
    //Child-lookup (per outgoing edge):
    //  _childChildIdx[k] = local node index of the k-th child across all slices
    //
    //The per-child status visible through ChildHandles is the status of the
    //underlying child node — i.e. _nodeStatus[_childChildIdx[baseIndex + i]].
    private readonly ChildStatus[] nodeStatus;
    private readonly TResult[] nodeResult;
    private readonly int[] childSliceStart;
    private readonly int[] childSliceCount;
    private readonly int[] childChildIdx;

    //Mapping from encoded node id to local node index.
    //Populated by GraphKFold discovery before this type is constructed.
    private readonly Dictionary<TermId, int> nodeIdToLocal;

    internal ReductionState(
        int nodeCount,
        Dictionary<TermId, int> nodeIdToLocal,
        int[] childSliceStart,
        int[] childSliceCount,
        int[] childChildIdx)
    {
        this.nodeStatus = new ChildStatus[nodeCount];
        this.nodeResult = new TResult[nodeCount];
        this.childSliceStart = childSliceStart;
        this.childSliceCount = childSliceCount;
        this.childChildIdx = childChildIdx;
        this.nodeIdToLocal = nodeIdToLocal;
    }

    /// <summary>
    /// Gets the local node index for the given encoded node id, or <c>-1</c>
    /// if the node was not reached during discovery.
    /// </summary>
    internal int GetLocalIndex(TermId encodedNodeId)
    {
        return nodeIdToLocal.TryGetValue(encodedNodeId, out int idx) ? idx : -1;
    }

    /// <summary>
    /// Marks a node as being computed. Used by the driver immediately before
    /// invoking the algebra for that node. A subsequent force request on
    /// this node while still in this state indicates recursion.
    /// </summary>
    internal void MarkComputing(int localNodeIndex)
    {
        nodeStatus[localNodeIndex] = ChildStatus.Computing;
    }

    /// <summary>
    /// Returns the local indices of the outgoing children of a node, as a
    /// slice <c>[start, start + count)</c> of the child index table.
    /// </summary>
    internal (int Start, int Count) GetChildSlice(int localNodeIndex)
    {
        return (childSliceStart[localNodeIndex], childSliceCount[localNodeIndex]);
    }

    /// <summary>
    /// Returns the local node index of the k-th entry in the global child
    /// index table. Used by the driver to find the child to force.
    /// </summary>
    internal int GetChildLocalIndex(int childSlotGlobalIndex)
    {
        return childChildIdx[childSlotGlobalIndex];
    }

    /// <summary>
    /// Returns the status of the child at the given global child-slot index.
    /// </summary>
    /// <remarks>
    /// The reported status is the status of the underlying child node. A
    /// child slot reports <see cref="ChildStatus.Computed"/> iff the node
    /// it refers to has its aggregate result written.
    /// </remarks>
    internal ChildStatus GetChildStatus(int childSlotGlobalIndex)
    {
        int childLocalIdx = childChildIdx[childSlotGlobalIndex];
        return nodeStatus[childLocalIdx];
    }

    /// <summary>
    /// Returns the folded value of the child at the given global child-slot index.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The underlying child node has not yet completed reduction.
    /// </exception>
    internal TResult GetChildValue(int childSlotGlobalIndex)
    {
        int childLocalIdx = childChildIdx[childSlotGlobalIndex];
        if(nodeStatus[childLocalIdx] != ChildStatus.Computed)
        {
            throw new InvalidOperationException(
                $"Child at slot {childSlotGlobalIndex} (node index {childLocalIdx}) has not been computed.");
        }

        return nodeResult[childLocalIdx];
    }

    /// <summary>
    /// Stores the folded result of a node and marks it computed.
    /// </summary>
    internal void SetNodeResult(int localNodeIndex, TResult value)
    {
        nodeResult[localNodeIndex] = value;
        nodeStatus[localNodeIndex] = ChildStatus.Computed;
    }

    /// <summary>
    /// Returns the folded result of a node. Valid only after
    /// <see cref="SetNodeResult"/> has been called for that node.
    /// </summary>
    internal TResult GetNodeResult(int localNodeIndex)
    {
        return nodeResult[localNodeIndex];
    }

    /// <summary>
    /// Returns the current status of a node.
    /// </summary>
    internal ChildStatus GetNodeStatus(int localNodeIndex)
    {
        return nodeStatus[localNodeIndex];
    }
}
