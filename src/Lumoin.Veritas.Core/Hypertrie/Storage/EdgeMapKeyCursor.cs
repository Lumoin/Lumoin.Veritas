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
/// holding a snapshot of the underlying <see cref="EdgeMap"/>'s
/// content (sorted-keys array reference for the array case, or
/// the inline key for the single-entry case) plus a current
/// position. Mutating operations take the cursor by
/// <see langword="ref"/>; read-only operations take it by
/// <see langword="in"/>.
/// </para>
/// <para>
/// <b>Lifetime.</b> A cursor must not outlive the underlying
/// <see cref="EdgeMap"/>'s storage. Because edge maps are owned by
/// hypertrie nodes that are immutable after interning, the cursor
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
/// <b>Equality.</b> Two cursors are equal when their snapshotted
/// representation matches and they are at the same position.
/// Reference equality is used on the heap-typed snapshot fields
/// (<see cref="SortedKeys"/>, <see cref="SortedChildren"/>);
/// structural equality across array contents would be linear in
/// entry count and is not the semantic any current consumer needs.
/// </para>
/// </remarks>
[DebuggerDisplay("EdgeMapKeyCursor Kind={Kind} AtEnd={AtEnd}")]
public struct EdgeMapKeyCursor: IEquatable<EdgeMapKeyCursor>
{
    /// <summary>
    /// The kind of the underlying map. Snapshotted at
    /// construction and never re-read because the underlying
    /// map's kind cannot change while the cursor lives — interned
    /// nodes are immutable.
    /// </summary>
    private EdgeMapKind Kind { get; }

    /// <summary>The sorted-ascending key snapshot for both <see cref="EdgeMapKind.Inline"/> and <see cref="EdgeMapKind.SortedArray"/>. <c>null</c> only for <see cref="EdgeMapKind.Empty"/>.</summary>
    private uint[]? SortedKeys { get; }

    /// <summary>The child-handle snapshot, parallel to <see cref="SortedKeys"/>. At depth-1 leaves every slot is <see cref="NodeHandle.None"/>.</summary>
    private NodeHandle[]? SortedChildren { get; }

    /// <summary>The count of valid entries in <see cref="SortedKeys"/> / <see cref="SortedChildren"/>.</summary>
    private int SortedCount { get; }

    /// <summary>
    /// Current position. For Inline: 0 = at the entry, 1 = past
    /// end. For SortedArray: index into [0..SortedCount). For
    /// Empty: always at end.
    /// </summary>
    private int Position { get; set; }

    /// <summary>
    /// Constructs a cursor over <paramref name="map"/>, positioned
    /// at the first key (if any).
    /// </summary>
    public EdgeMapKeyCursor(in EdgeMap map)
    {
        Kind = map.Kind;

        switch(map.Kind)
        {
            case EdgeMapKind.Empty:
            {
                SortedKeys = null;
                SortedChildren = null;
                SortedCount = 0;
                Position = 0;

                break;
            }
            case EdgeMapKind.Inline:
            {
                //Inline tier holds up to 8 entries in ascending key
                //order. Materialise the parallel arrays so the
                //cursor reads the same code path as SortedArray; the
                //per-cursor allocation is bounded by InlineCapacity.
                SortedKeys = EdgeMap.InlineKeysSpan(in map).ToArray();
                SortedChildren = EdgeMap.InlineChildrenSpan(in map).ToArray();
                SortedCount = SortedKeys.Length;
                Position = 0;

                break;
            }
            case EdgeMapKind.SortedArray:
            {
                SortedKeys = EdgeMap.SortedKeysSpan(in map).ToArray();
                SortedChildren = EdgeMap.SortedChildrenSpan(in map).ToArray();
                SortedCount = map.SortedCount;
                Position = 0;

                break;
            }
            default:
            {
                throw new UnreachableException();
            }
        }
    }

    /// <summary>
    /// <c>true</c> when the cursor has advanced past the last key
    /// (or the underlying map is empty).
    /// </summary>
    public bool AtEnd => Kind switch
    {
        EdgeMapKind.Empty => true,
        EdgeMapKind.Inline => Position >= SortedCount,
        EdgeMapKind.SortedArray => Position >= SortedCount,
        _ => throw new UnreachableException(),
    };

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

            return Kind switch
            {
                EdgeMapKind.Inline => SortedKeys![Position],
                EdgeMapKind.SortedArray => SortedKeys![Position],
                _ => throw new UnreachableException(),
            };
        }
    }

    /// <summary>
    /// The current child handle, which is <see cref="NodeHandle.None"/>
    /// at depth-1 leaves and a real handle at deeper nodes. Undefined
    /// when <see cref="AtEnd"/> is <c>true</c>.
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

            return Kind switch
            {
                EdgeMapKind.Inline => SortedChildren![Position],
                EdgeMapKind.SortedArray => SortedChildren![Position],
                _ => throw new UnreachableException(),
            };
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

        Position++;
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
    /// For <see cref="EdgeMapKind.SortedArray"/> the seek uses
    /// <see cref="Array.BinarySearch{T}(T[], int, int, T)"/> over
    /// the suffix <c>[Position..SortedCount)</c> so it remains
    /// O(log n) in the remaining entry count rather than O(log
    /// total).
    /// </para>
    /// </remarks>
    public void SeekTo(uint target)
    {
        if(AtEnd)
        {
            return;
        }

        switch(Kind)
        {
            case EdgeMapKind.Inline:
            case EdgeMapKind.SortedArray:
            {
                int remaining = SortedCount - Position;
                int searchResult = Array.BinarySearch(SortedKeys!, Position, remaining, target);

                if(searchResult >= 0)
                {
                    //Exact match — position the cursor at it (or
                    //leave it where it is if already past).
                    if(searchResult > Position)
                    {
                        Position = searchResult;
                    }

                    return;
                }

                //No exact match — bitwise complement is the
                //insertion point, which is the first key strictly
                //greater than the target.
                int insertion = ~searchResult;

                if(insertion > Position)
                {
                    Position = insertion;
                }

                return;
            }
            default:
            {
                throw new UnreachableException();
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="other"/> matches
    /// this cursor on every snapshotted field and on the current
    /// position. Heap-typed snapshot fields are compared by
    /// reference equality.
    /// </summary>
    public bool Equals(EdgeMapKeyCursor other)
    {
        return Kind == other.Kind
            && SortedCount == other.SortedCount
            && Position == other.Position
            && ReferenceEquals(SortedKeys, other.SortedKeys)
            && ReferenceEquals(SortedChildren, other.SortedChildren);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EdgeMapKeyCursor other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        //Heap-typed fields are hashed by reference identity (none
        //of those types overrides GetHashCode), which matches the
        //reference-equality semantics used in Equals.
        return HashCode.Combine(
            Kind,
            SortedKeys,
            SortedChildren,
            SortedCount,
            Position);
    }

    /// <summary>Returns <c>true</c> when the operands are equal.</summary>
    public static bool operator ==(EdgeMapKeyCursor left, EdgeMapKeyCursor right) => left.Equals(right);

    /// <summary>Returns <c>true</c> when the operands are not equal.</summary>
    public static bool operator !=(EdgeMapKeyCursor left, EdgeMapKeyCursor right) => !left.Equals(right);
}
