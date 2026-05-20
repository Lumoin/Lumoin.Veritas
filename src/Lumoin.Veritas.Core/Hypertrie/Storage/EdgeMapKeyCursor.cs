using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// A sorted-key cursor over one <see cref="EdgeMap"/>. Exposes
/// uniform <see cref="MoveNext"/> / <see cref="SeekTo"/> /
/// <see cref="AtEnd"/> / <see cref="CurrentKey"/> /
/// <see cref="CurrentChild"/> operations regardless of which
/// <see cref="EdgeMapKind"/> the underlying map carries.
/// </summary>
/// <remarks>
/// <para>
/// This is the access primitive triejoin descent and leapfrog
/// intersection are built on. The leapfrog step needs to advance
/// one cursor to the first key greater than or equal to a target
/// (<see cref="SeekTo"/>); the descent step needs to walk every
/// key in sorted order (<see cref="MoveNext"/>) or jump to a
/// specific key (also <see cref="SeekTo"/>). Both algorithms
/// require the keys to be visited in ascending order — which is
/// what <see cref="EdgeMapKind.SortedArray"/> gives us natively
/// and what <see cref="EdgeMapKind.Inline"/> trivially satisfies.
/// </para>
/// <para>
/// <b>State.</b> The cursor is a value-typed mutable struct
/// holding a reference to the parent <see cref="HypertrieNode"/>,
/// the index identifying which of its three embedded
/// <see cref="EdgeMap"/> fields the cursor walks
/// (<see cref="HypertrieNode.EdgeMapAt(int)"/>), a snapshot of
/// the map's <see cref="EdgeMapKind"/> and entry count (cached
/// once at construction because interned nodes are immutable),
/// and a current position. The cursor does not materialise the
/// underlying key or child storage into per-cursor arrays — every
/// read goes through the static <see cref="EdgeMap.KeyAt"/> and
/// <see cref="EdgeMap.ChildAt"/> accessors on the live map.
/// Mutating operations take the cursor by <see langword="ref"/>;
/// read-only operations take it by <see langword="in"/>.
/// </para>
/// <para>
/// <b>Lifetime.</b> A cursor must not outlive the underlying
/// <see cref="HypertrieNode"/>. Because hypertrie nodes are
/// immutable after interning and pinned by snapshots, the cursor
/// is safe to hold as long as the consumer holds a reference to a
/// live snapshot.
/// </para>
/// <para>
/// <b>Initial state.</b> A freshly-constructed cursor is
/// positioned at the first key (if any) — equivalent to having
/// just called <see cref="MoveNext"/> from the "before-first"
/// virtual position. <see cref="AtEnd"/> is <c>true</c> only when
/// the underlying map is empty or the cursor has advanced past
/// the last key.
/// </para>
/// <para>
/// <b>Equality.</b> Two cursors are equal when they reference the
/// same parent node (by reference equality), the same edge-map
/// index, and the same current position. The cached kind and
/// count fields follow from the parent-and-index identity, so
/// they need not enter the equality check.
/// </para>
/// </remarks>
[DebuggerDisplay("EdgeMapKeyCursor Kind={kind} AtEnd={AtEnd}")]
public struct EdgeMapKeyCursor: IEquatable<EdgeMapKeyCursor>
{
    private readonly HypertrieNode? node;
    private readonly byte edgeMapIndex;
    private readonly EdgeMapKind kind;
    private readonly int totalCount;
    private int position;

    /// <summary>
    /// Constructs a cursor over the edge map at <paramref name="edgeMapIndex"/>
    /// on <paramref name="node"/>, positioned at the first key (if
    /// any).
    /// </summary>
    /// <param name="node">The parent node holding the edge map.</param>
    /// <param name="edgeMapIndex">The index of the edge map to walk; in <c>[0, node.Depth)</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeMapIndex"/> is outside <c>[0, node.Depth)</c>.</exception>
    public EdgeMapKeyCursor(HypertrieNode node, int edgeMapIndex)
    {
        ArgumentNullException.ThrowIfNull(node);
        if(edgeMapIndex < 0 || edgeMapIndex >= node.Depth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(edgeMapIndex),
                edgeMapIndex,
                $"Edge-map index must be in [0, {node.Depth}).");
        }

        this.node = node;
        this.edgeMapIndex = (byte)edgeMapIndex;

        ref EdgeMap map = ref node.EdgeMapAt(edgeMapIndex);
        kind = map.Kind;
        totalCount = EdgeMap.Count(in map);
        position = 0;
    }

    /// <summary>
    /// <c>true</c> when the cursor has advanced past the last key
    /// (or the underlying map is empty).
    /// </summary>
    public bool AtEnd => position >= totalCount;

    /// <summary>
    /// The current key. Undefined when <see cref="AtEnd"/> is
    /// <c>true</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The cursor is past the end.</exception>
    public uint CurrentKey
    {
        get
        {
            if(AtEnd)
            {
                throw new InvalidOperationException("Cursor is past the end; CurrentKey is not defined.");
            }

            Debug.Assert(node is not null, "Cursor with positive count must have a non-null node.");

            ref EdgeMap map = ref node!.EdgeMapAt(edgeMapIndex);
            return EdgeMap.KeyAt(in map, position);
        }
    }

    /// <summary>
    /// The current child reference, which is <c>null</c> at
    /// depth-1 leaves and a real <see cref="HypertrieNode"/>
    /// reference at deeper nodes. Undefined when
    /// <see cref="AtEnd"/> is <c>true</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The cursor is past the end.</exception>
    public HypertrieNode? CurrentChild
    {
        get
        {
            if(AtEnd)
            {
                throw new InvalidOperationException("Cursor is past the end; CurrentChild is not defined.");
            }

            Debug.Assert(node is not null, "Cursor with positive count must have a non-null node.");

            ref EdgeMap map = ref node!.EdgeMapAt(edgeMapIndex);
            return EdgeMap.ChildAt(in map, position);
        }
    }

    /// <summary>
    /// Advances to the next key. The cursor reaches
    /// <see cref="AtEnd"/> when called from the last key.
    /// </summary>
    public void MoveNext()
    {
        if(AtEnd)
        {
            return;
        }

        position++;
    }

    /// <summary>
    /// Advances the cursor to the first key greater than or equal
    /// to <paramref name="target"/>. If no such key exists the
    /// cursor reaches <see cref="AtEnd"/>. If the cursor is
    /// already past <paramref name="target"/>, this is a no-op.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SeekTo"/> never moves the cursor backwards. This
    /// matches the leapfrog triejoin contract: cursors advance
    /// monotonically through a single descent, so a seek with a
    /// target less than <see cref="CurrentKey"/> is a logical
    /// no-op.
    /// </para>
    /// <para>
    /// The seek uses
    /// <see cref="MemoryExtensions.BinarySearch{T}(ReadOnlySpan{T}, T)"/>
    /// over the suffix <c>[position..totalCount)</c> of the
    /// underlying map's key span so it remains O(log n) in the
    /// remaining entry count rather than O(log total).
    /// </para>
    /// </remarks>
    public void SeekTo(uint target)
    {
        if(AtEnd)
        {
            return;
        }

        Debug.Assert(node is not null, "Cursor with positive count must have a non-null node.");

        ref EdgeMap map = ref node!.EdgeMapAt(edgeMapIndex);
        ReadOnlySpan<uint> keys = kind switch
        {
            EdgeMapKind.Inline => EdgeMap.InlineKeysSpan(in map),
            EdgeMapKind.SortedArray => EdgeMap.SortedKeysSpan(in map),
            _ => throw new UnreachableException(),
        };

        ReadOnlySpan<uint> suffix = keys[position..];
        int searchResult = suffix.BinarySearch(target);

        if(searchResult >= 0)
        {
            //Exact match — position the cursor at it.
            position += searchResult;

            return;
        }

        //No exact match — bitwise complement is the insertion point
        //relative to the suffix, which is the first key strictly
        //greater than the target.
        int insertion = ~searchResult;
        position += insertion;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="other"/> walks the
    /// same edge map (same parent node by reference and same edge-
    /// map index) and is at the same position.
    /// </summary>
    public bool Equals(EdgeMapKeyCursor other)
    {
        return ReferenceEquals(node, other.node)
            && edgeMapIndex == other.edgeMapIndex
            && position == other.position;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EdgeMapKeyCursor other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            node is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(node),
            edgeMapIndex,
            position);
    }

    /// <summary>Returns <c>true</c> when the operands are equal.</summary>
    public static bool operator ==(EdgeMapKeyCursor left, EdgeMapKeyCursor right) => left.Equals(right);

    /// <summary>Returns <c>true</c> when the operands are not equal.</summary>
    public static bool operator !=(EdgeMapKeyCursor left, EdgeMapKeyCursor right) => !left.Equals(right);
}
