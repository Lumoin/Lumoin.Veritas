using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// One active algebra invocation on the <see cref="GraphKFold"/> reduction stack.
/// </summary>
/// <remarks>
/// <para>
/// Frames are created when the driver pushes a node for reduction and popped
/// when that node's algebra iterator completes. The frame holds the open
/// iterator (whose state machine carries the algebra's locals and position)
/// and the static inputs passed to the algebra: node id, local index, and
/// the outgoing-triples array the algebra references by child index.
/// </para>
/// <para>
/// Sealed class rather than struct: stack entries point to the same frame
/// instance until pop, iterator disposal requires definite ownership, and
/// the handful of allocations per fold is already dominated by iterator
/// state machines.
/// </para>
/// </remarks>
/// <typeparam name="TResult">The fold's result type.</typeparam>
internal sealed class ReductionFrame<TResult>
{
    internal ReductionFrame(
        TermId nodeId,
        int localIndex,
        EncodedTriple[] outgoingTriples,
        IEnumerator<ForceRequest> iterator)
    {
        NodeId = nodeId;
        LocalIndex = localIndex;
        OutgoingTriples = outgoingTriples;
        Iterator = iterator;
    }

    /// <summary>Encoded node identifier this frame is reducing.</summary>
    internal TermId NodeId { get; }

    /// <summary>Local index within the reduction state.</summary>
    internal int LocalIndex { get; }

    /// <summary>Outgoing triples of this node; child indices reference this array.</summary>
    internal EncodedTriple[] OutgoingTriples { get; }

    /// <summary>The open algebra iterator. Driver calls MoveNext and reads Current.</summary>
    internal IEnumerator<ForceRequest> Iterator { get; }
}
