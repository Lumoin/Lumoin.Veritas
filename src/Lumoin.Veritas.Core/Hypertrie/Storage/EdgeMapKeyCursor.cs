using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// A sorted-key cursor over one <see cref="EdgeMap"/>, or a
/// synthetic single-key cursor used at SEN descent. Exposes
/// uniform <see cref="MoveNext"/> / <see cref="SeekTo"/> /
/// <see cref="AtEnd"/> / <see cref="CurrentKey"/> /
/// <see cref="CurrentChild"/> operations regardless of the
/// underlying source.
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
/// what <see cref="EdgeMapKind.SortedArray"/> gives us natively,
/// what <see cref="EdgeMapKind.Inline"/> trivially satisfies, and
/// what the single-key synthetic cursor satisfies by carrying
/// exactly one element.
/// </para>
/// <para>
/// <b>State.</b> In edge-map mode the cursor holds the parent
/// <see cref="HypertrieNode"/> by value (a 16-byte struct copy
/// that shares the canonical <see cref="HypertrieNode.EdgeMaps"/>
/// array reference), the index identifying which of the node's
/// edge maps the cursor walks, a snapshot of the map's
/// <see cref="EdgeMapKind"/> and entry count, and a current
/// position. In synthetic mode the cursor instead carries a
/// single key value and a flag; descent code reads the key
/// directly without consulting any node.
/// </para>
/// <para>
/// <b>Lifetime.</b> A cursor must not outlive the underlying
/// <see cref="HypertrieNode"/>'s storage. Synthetic cursors hold
/// no node reference and are valid as long as their containing
/// frame is.
/// </para>
/// </remarks>
[DebuggerDisplay("EdgeMapKeyCursor IsSynthetic={isSynthetic} AtEnd={AtEnd}")]
public struct EdgeMapKeyCursor: IEquatable<EdgeMapKeyCursor>
{
    private readonly HypertrieNode node;
    private readonly byte edgeMapIndex;
    private readonly EdgeMapKind kind;
    private readonly int totalCount;
    private readonly uint syntheticKey;
    private readonly bool isSynthetic;
    private int position;

    /// <summary>
    /// Constructs a cursor over the edge map at
    /// <paramref name="edgeMapIndex"/> on <paramref name="node"/>,
    /// positioned at the first key (if any).
    /// </summary>
    /// <param name="node">The parent node holding the edge map.</param>
    /// <param name="edgeMapIndex">The index of the edge map to walk; in <c>[0, node.Depth)</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="node"/> is <c>default</c> (no edge maps) or <paramref name="edgeMapIndex"/> is outside <c>[0, node.Depth)</c>.</exception>
    public EdgeMapKeyCursor(HypertrieNode node, int edgeMapIndex)
    {
        if(node.EdgeMaps is null)
        {
            throw new ArgumentException("Cannot construct a cursor over a default HypertrieNode (no edge maps).", nameof(node));
        }

        if(edgeMapIndex < 0 || edgeMapIndex >= node.EdgeMaps.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(edgeMapIndex),
                edgeMapIndex,
                $"Edge-map index must be in [0, {node.EdgeMaps.Length}).");
        }

        this.node = node;
        this.edgeMapIndex = (byte)edgeMapIndex;
        syntheticKey = 0U;
        isSynthetic = false;

        ref EdgeMap map = ref node.EdgeMaps[edgeMapIndex];
        kind = map.Kind;
        totalCount = EdgeMap.Count(in map);
        position = 0;
    }

    /// <summary>
    /// Constructs a synthetic single-key cursor that yields exactly
    /// one key (<paramref name="singleKey"/>) and exactly one child
    /// (<see cref="NodeHandle.None"/>). Used at SEN descent where
    /// the next variable level is a virtual depth-1 leaf with no
    /// real node to walk.
    /// </summary>
    /// <param name="singleKey">The single key this cursor yields.</param>
    public EdgeMapKeyCursor(uint singleKey)
    {
        node = default;
        edgeMapIndex = 0;
        kind = EdgeMapKind.Inline;
        totalCount = 1;
        syntheticKey = singleKey;
        isSynthetic = true;
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

            if(isSynthetic)
            {
                return syntheticKey;
            }

            ref EdgeMap map = ref node.EdgeMaps![edgeMapIndex];

            return EdgeMap.KeyAt(in map, position);
        }
    }

    /// <summary>
    /// The current child handle, which is <see cref="NodeHandle.None"/>
    /// at depth-1 leaves and a real handle at deeper nodes.
    /// Undefined when <see cref="AtEnd"/> is <c>true</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The cursor is past the end.</exception>
    public NodeHandle CurrentChild
    {
        get
        {
            if(AtEnd)
            {
                throw new InvalidOperationException("Cursor is past the end; CurrentChild is not defined.");
            }

            if(isSynthetic)
            {
                return NodeHandle.None;
            }

            ref EdgeMap map = ref node.EdgeMaps![edgeMapIndex];

            return EdgeMap.ChildAt(in map, position);
        }
    }

    /// <summary>
    /// Rewinds the cursor to its first key. After this call
    /// <see cref="CurrentKey"/> is the first key of the underlying
    /// edge map (or the synthetic single key), regardless of where
    /// the cursor had advanced to — including from
    /// <see cref="AtEnd"/>.
    /// </summary>
    /// <remarks>
    /// The underlying edge map (or synthetic key) is immutable, so
    /// resetting the position re-presents exactly the same key
    /// sequence. Used by the worst-case-optimal join driver to
    /// re-enumerate an independent variable's level when an
    /// unrelated variable re-binds.
    /// </remarks>
    public void Reset()
    {
        position = 0;
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
    /// </remarks>
    public void SeekTo(uint target)
    {
        if(AtEnd)
        {
            return;
        }

        if(isSynthetic)
        {
            //The synthetic cursor carries exactly one key; if the
            //target is past it, advance to end. Otherwise stay.
            if(target > syntheticKey)
            {
                position = totalCount;
            }

            return;
        }

        ref EdgeMap map = ref node.EdgeMaps![edgeMapIndex];
        ReadOnlySpan<uint> keys = kind switch
        {
            EdgeMapKind.Inline => EdgeMap.InlineKeysSpan(in map),
            EdgeMapKind.SortedArray => EdgeMap.SortedKeysSpan(in map),
            EdgeMapKind.SortedKeysOnly => EdgeMap.SortedKeysSpan(in map),
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

        //No exact match — bitwise complement is the insertion
        //point relative to the suffix, which is the first key
        //strictly greater than the target.
        int insertion = ~searchResult;
        position += insertion;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="other"/> walks the
    /// same edge map (same parent node by canonical array
    /// reference equality and same edge-map index) and is at the
    /// same position, or when both are synthetic cursors over the
    /// same key at the same position.
    /// </summary>
    public bool Equals(EdgeMapKeyCursor other)
    {
        if(isSynthetic != other.isSynthetic)
        {
            return false;
        }

        if(isSynthetic)
        {
            return syntheticKey == other.syntheticKey && position == other.position;
        }

        return ReferenceEquals(node.EdgeMaps, other.node.EdgeMaps)
            && edgeMapIndex == other.edgeMapIndex
            && position == other.position;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EdgeMapKeyCursor other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if(isSynthetic)
        {
            return HashCode.Combine(true, syntheticKey, position);
        }

        return HashCode.Combine(
            false,
            node.EdgeMaps is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(node.EdgeMaps),
            edgeMapIndex,
            position);
    }

    /// <summary>Returns <c>true</c> when the operands are equal.</summary>
    public static bool operator ==(EdgeMapKeyCursor left, EdgeMapKeyCursor right) => left.Equals(right);

    /// <summary>Returns <c>true</c> when the operands are not equal.</summary>
    public static bool operator !=(EdgeMapKeyCursor left, EdgeMapKeyCursor right) => !left.Equals(right);
}
