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
/// two; at depth 1, one. Slicing by any edge map produces a node
/// of depth-1 less.
/// </para>
/// <para>
/// <b>Position metadata is external.</b> The node does not record
/// which original RDF position (subject, predicate, or object)
/// each of its edge maps corresponds to — that mapping depends on
/// the descent path taken to reach the node. Consumers
/// (<see cref="HypertrieOps"/>, <see cref="HypertrieGraphStore"/>)
/// thread the position list alongside the node when they descend.
/// </para>
/// <para>
/// <b>Identity and storage.</b> A node carries data only; its
/// canonical identity is the <see cref="NodeIdentifier"/> hash
/// stored separately in <see cref="NodeStore"/>'s identifier-to-
/// handle map. Nodes are content-addressed: two structurally-equal
/// nodes resolve to the same <see cref="NodeHandle"/> after
/// interning.
/// </para>
/// <para>
/// <b>Default value.</b> <c>default(HypertrieNode)</c> has
/// <see cref="Depth"/> equal to zero and a <c>null</c>
/// <see cref="EdgeMaps"/> reference. The store reserves handle
/// index <c>0</c> for this sentinel; no real node has depth zero.
/// </para>
/// </remarks>
/// <param name="Depth">The number of edge maps this node carries — one per unresolved position; <c>1</c>, <c>2</c>, or <c>3</c> in a depth-3 RDF hypertrie. Zero only on <c>default(HypertrieNode)</c>.</param>
/// <param name="EdgeMaps">The edge maps. <c>EdgeMaps[i]</c> indexes the node's data by the i-th remaining position; the original RDF position (S/P/O) it represents is supplied by the descending consumer.</param>
[DebuggerDisplay("HypertrieNode Depth={Depth}")]
public readonly record struct HypertrieNode(byte Depth, EdgeMap[] EdgeMaps)
{
    /// <summary>
    /// Constructs a node of the given depth with freshly-allocated
    /// edge maps in the Empty state.
    /// </summary>
    /// <param name="depth">The number of edge maps to allocate; must be 1, 2, or 3.</param>
    /// <returns>A new node with <see cref="EdgeMaps"/> of the requested length.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is outside [1, 3].</exception>
    public static HypertrieNode Create(byte depth)
    {
        if(depth < 1 || depth > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(depth),
                depth,
                "Depth must be 1, 2, or 3 for an RDF triple hypertrie.");
        }

        return new HypertrieNode(depth, new EdgeMap[depth]);
    }
}
