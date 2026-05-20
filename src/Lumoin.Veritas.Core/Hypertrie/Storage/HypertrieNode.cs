using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

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
/// node intern table. Nodes are content-addressed: two structurally-
/// equal nodes resolve to the same instance after interning.
/// </para>
/// <para>
/// <b>Storage shape.</b> Three <see cref="EdgeMap"/> fields are
/// embedded directly in the class — <see cref="Edge0"/> is always
/// valid; <see cref="Edge1"/> is valid when <see cref="Depth"/>
/// is at least <c>2</c>; <see cref="Edge2"/> is valid when
/// <see cref="Depth"/> equals <c>3</c>. Wasted space on depth-1
/// nodes (two embedded <see cref="EdgeMap"/> structs in the
/// <see cref="EdgeMapKind.Empty"/> state) is the cost of removing
/// the per-node <c>EdgeMap[]</c> outer array that previously held
/// these maps via indirection. Inline iteration through
/// <see cref="EdgeMapAt(int)"/> reaches all three fields directly.
/// </para>
/// <para>
/// <b>Reference-counted ownership not used.</b> The Tentris
/// reference (<c>cpp-ref/.../ReferenceCounted.hpp</c>) decorates
/// its <c>FullNode</c> with an inline ref-count and decrements
/// per release. We do not: our concurrency model uses a sweep over
/// reachable identifiers, not per-node refcounting. A well-meaning
/// future contributor reading the Tentris source and adding a
/// ref-count "to match" would break the sweep invariant — do not.
/// </para>
/// </remarks>
[DebuggerDisplay("HypertrieNode Depth={Depth}")]
[SuppressMessage(
    "Design",
    "CA1051:Do not declare visible instance fields",
    Justification = "Edge0, Edge1, and Edge2 are embedded EdgeMap struct fields. Consumers mutate them through static EdgeMap.InsertOrReplace(ref map, ...) and read them through static EdgeMap.* accessors that take `in EdgeMap`. Both require directly-addressable fields; wrapping in properties would force a copy on mutation and defeat the inline-storage shape.")]
public sealed class HypertrieNode
{
    /// <summary>The number of edge maps this node carries — one per unresolved position; <c>1</c>, <c>2</c>, or <c>3</c> in a depth-3 RDF hypertrie.</summary>
    public byte Depth { get; }

    /// <summary>The first edge map. Always valid for every node.</summary>
    public EdgeMap Edge0;

    /// <summary>The second edge map. Valid when <see cref="Depth"/> is at least <c>2</c>; otherwise <see cref="EdgeMapKind.Empty"/> by default.</summary>
    public EdgeMap Edge1;

    /// <summary>The third edge map. Valid when <see cref="Depth"/> equals <c>3</c>; otherwise <see cref="EdgeMapKind.Empty"/> by default.</summary>
    public EdgeMap Edge2;

    /// <summary>
    /// Constructs a node of the given depth with the embedded edge
    /// maps left in the <see cref="EdgeMapKind.Empty"/> state.
    /// </summary>
    /// <param name="depth">The number of valid edge maps; must be 1, 2, or 3.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is outside [1, 3].</exception>
    public HypertrieNode(byte depth)
    {
        if(depth < 1 || depth > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(depth),
                depth,
                "Depth must be 1, 2, or 3 for an RDF triple hypertrie.");
        }

        Depth = depth;
    }

    /// <summary>
    /// Returns a writable reference to the edge map at the given
    /// position. Position <c>0</c> maps to <see cref="Edge0"/>,
    /// <c>1</c> to <see cref="Edge1"/>, <c>2</c> to <see cref="Edge2"/>.
    /// </summary>
    /// <param name="position">The position index, in <c>[0, Depth)</c>.</param>
    /// <returns>A reference to the corresponding edge map.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is outside <c>[0, 3)</c>.</exception>
    public ref EdgeMap EdgeMapAt(int position)
    {
        switch(position)
        {
            case 0:
            {
                return ref Edge0;
            }
            case 1:
            {
                return ref Edge1;
            }
            case 2:
            {
                return ref Edge2;
            }
            default:
            {
                throw new ArgumentOutOfRangeException(
                    nameof(position),
                    position,
                    "Position must be 0, 1, or 2.");
            }
        }
    }
}
