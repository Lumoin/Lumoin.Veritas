using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// A node in the hypertrie. Carries one <see cref="EdgeMap"/> per
/// remaining unresolved position; each edge map indexes the node's
/// data by that position.
/// </summary>
/// <remarks>
/// <para>
/// At depth 3 (the root) the node has three edge maps — one for
/// subject, one for predicate, one for object. At depth 2 it has
/// two; at depth 1, one. Slicing by any edge map produces a node of
/// depth-1 less.
/// </para>
/// <para>
/// <b>Position metadata is external.</b> The node does not record
/// which original RDF position (subject, predicate, or object) each
/// of its edge maps corresponds to — that mapping depends on the
/// descent path taken to reach the node. Consumers
/// (<see cref="HypertrieOps"/>, <see cref="HypertrieGraphStore"/>)
/// thread the position list alongside the node when they descend.
/// </para>
/// <para>
/// <b>No content-addressed deduplication in this batch.</b> Two
/// nodes that hold the same data are still distinct objects. A
/// follow-up batch will introduce hash-based deduplication via a
/// <c>NodeIdentifier</c> XOR-combined hash and a <c>NodeStore</c>,
/// turning the tree into a content-addressed DAG without changing
/// this type's public surface.
/// </para>
/// </remarks>
[DebuggerDisplay("HypertrieNode Depth={Depth}")]
public sealed class HypertrieNode
{
    /// <summary>
    /// The number of edge maps this node carries — one per
    /// unresolved position. Always 1, 2, or 3 in a depth-3 RDF
    /// hypertrie.
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// The edge maps. <c>EdgeMaps[i]</c> indexes the node's data by
    /// the i-th remaining position; the original RDF position
    /// (S/P/O) it represents is supplied by the descending consumer.
    /// </summary>
    public EdgeMap[] EdgeMaps { get; }

    /// <summary>
    /// Constructs an empty node of the given depth.
    /// </summary>
    /// <param name="depth">The number of edge maps to allocate; must be 1, 2, or 3.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is outside [1, 3].</exception>
    public HypertrieNode(int depth)
    {
        if(depth < 1 || depth > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(depth),
                depth,
                "Depth must be 1, 2, or 3 for an RDF triple hypertrie.");
        }

        Depth = depth;
        EdgeMaps = new EdgeMap[depth];
    }
}
