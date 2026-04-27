using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// One edge map of a <see cref="HypertrieNode"/>. Maps an encoded
/// term identifier (<see cref="long"/>) to a child
/// <see cref="HypertrieNode"/> for inner nodes, or to a presence
/// marker (the child reference is <c>null</c>) for depth-1 leaves.
/// </summary>
/// <remarks>
/// <para>
/// A value-typed container with a discriminated kind. Only one
/// representation is active at a time and the kind tells operations
/// how to interpret the value-bearing properties. Operations are
/// static methods taking the map by <see langword="ref"/> for
/// mutating calls and by <see langword="in"/> for read-only calls;
/// this avoids hidden boxing and keeps every state transition
/// explicit at the call site.
/// </para>
/// <para>
/// <b>Promotion ladder.</b>
/// <see cref="EdgeMapKind.Empty"/> →
/// <see cref="EdgeMapKind.Inline"/> on the first inserted entry;
/// <see cref="EdgeMapKind.Inline"/> →
/// <see cref="EdgeMapKind.SortedArray"/> on the second distinct
/// entry. <see cref="EdgeMapKind.SortedArray"/> scales without
/// further promotion: the parallel arrays grow by doubling as
/// needed and lookup remains binary search throughout. Demotion on
/// removal is not implemented in this batch — the store is built
/// once and never mutated after, so demotion is unreachable.
/// </para>
/// <para>
/// <b>SortedArray representation.</b> Two parallel arrays —
/// <see cref="SortedKeys"/> and <see cref="SortedChildren"/> — hold
/// entries in ascending key order. Only the prefix
/// <c>[0..SortedCount)</c> is valid; the underlying arrays may be
/// over-allocated to amortise growth. Lookup is
/// <c>Array.BinarySearch</c>; insertion shifts entries up by one.
/// </para>
/// <para>
/// At depth-1 leaves, the child reference stored under each key is
/// <c>null</c>: the key's mere presence in the map represents the
/// triple's existence. At depth-2 and depth-3 nodes the child
/// reference is always non-<c>null</c>.
/// </para>
/// <para>
/// <b>Equality.</b> Two edge maps are equal when every field is
/// equal — the kind, the inline key, the sorted-array count, and
/// the heap fields by <see cref="object.ReferenceEquals(object?, object?)"/>.
/// Reference equality on the heap fields keeps comparison O(1);
/// structural equality across populated arrays would be linear in
/// entry count and is not the semantic any current consumer needs.
/// Because every promotion zeroes the fields belonging to the
/// previous representation, two maps in the same kind agree on
/// their inactive fields and a field-by-field comparison gives the
/// right answer in every kind.
/// </para>
/// </remarks>
[DebuggerDisplay("Kind={Kind}")]
public struct EdgeMap: IEquatable<EdgeMap>
{
    /// <summary>The initial capacity allocated when promoting from <see cref="EdgeMapKind.Inline"/> to <see cref="EdgeMapKind.SortedArray"/>.</summary>
    private const int SortedArrayInitialCapacity = 4;

    /// <summary>The active representation.</summary>
    public EdgeMapKind Kind { get; private set; }

    /// <summary>For <see cref="EdgeMapKind.Inline"/>: the single entry's key.</summary>
    public long InlineKey { get; private set; }

    /// <summary>For <see cref="EdgeMapKind.Inline"/>: the single entry's child reference, or <c>null</c> at depth-1 leaves.</summary>
    public HypertrieNode? InlineChild { get; private set; }

    /// <summary>For <see cref="EdgeMapKind.SortedArray"/>: the keys, ascending. Only the prefix <c>[0..SortedCount)</c> is valid.</summary>
    public long[]? SortedKeys { get; private set; }

    /// <summary>For <see cref="EdgeMapKind.SortedArray"/>: the parallel child references. Only the prefix <c>[0..SortedCount)</c> is valid.</summary>
    public HypertrieNode?[]? SortedChildren { get; private set; }

    /// <summary>For <see cref="EdgeMapKind.SortedArray"/>: the number of valid entries; the underlying arrays may be longer.</summary>
    public int SortedCount { get; private set; }

    /// <summary>
    /// Returns the number of entries in <paramref name="map"/>.
    /// </summary>
    public static int Count(in EdgeMap map)
    {
        return map.Kind switch
        {
            EdgeMapKind.Empty => 0,
            EdgeMapKind.Inline => 1,
            EdgeMapKind.SortedArray => map.SortedCount,
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>
    /// Looks up <paramref name="key"/> in the map.
    /// </summary>
    /// <param name="map">The map to query.</param>
    /// <param name="key">The key to search for.</param>
    /// <param name="child">
    /// On <c>true</c>: the child reference associated with
    /// <paramref name="key"/>, which may legitimately be <c>null</c>
    /// at depth-1 leaves. On <c>false</c>: <c>null</c>.
    /// </param>
    /// <returns><c>true</c> when the key is present; otherwise <c>false</c>.</returns>
    public static bool TryGetChild(in EdgeMap map, long key, out HypertrieNode? child)
    {
        switch(map.Kind)
        {
            case EdgeMapKind.Empty:
            {
                child = null;

                return false;
            }
            case EdgeMapKind.Inline:
            {
                if(map.InlineKey == key)
                {
                    child = map.InlineChild;

                    return true;
                }

                child = null;

                return false;
            }
            case EdgeMapKind.SortedArray:
            {
                int index = Array.BinarySearch(map.SortedKeys!, 0, map.SortedCount, key);
                if(index >= 0)
                {
                    child = map.SortedChildren![index];

                    return true;
                }

                child = null;

                return false;
            }
            default:
            {
                throw new UnreachableException();
            }
        }
    }

    /// <summary>
    /// Inserts <paramref name="key"/> with <paramref name="child"/>,
    /// or replaces the existing child if the key is already present.
    /// Promotes the map to a denser representation when the entry
    /// count crosses a threshold. <see cref="EdgeMapKind.SortedArray"/>
    /// grows by doubling its parallel arrays as needed; no further
    /// promotion follows.
    /// </summary>
    /// <param name="map">The map to mutate.</param>
    /// <param name="key">The key to insert or replace.</param>
    /// <param name="child">
    /// The child reference to store. <c>null</c> is meaningful at
    /// depth-1 leaves (presence marker) and disallowed at higher
    /// depths.
    /// </param>
    public static void InsertOrReplace(ref EdgeMap map, long key, HypertrieNode? child)
    {
        switch(map.Kind)
        {
            case EdgeMapKind.Empty:
            {
                map.Kind = EdgeMapKind.Inline;
                map.InlineKey = key;
                map.InlineChild = child;

                return;
            }
            case EdgeMapKind.Inline:
            {
                if(map.InlineKey == key)
                {
                    map.InlineChild = child;

                    return;
                }

                //Second distinct entry — promote to SortedArray.
                long[] keys = new long[SortedArrayInitialCapacity];
                HypertrieNode?[] children = new HypertrieNode?[SortedArrayInitialCapacity];
                if(map.InlineKey < key)
                {
                    keys[0] = map.InlineKey;
                    children[0] = map.InlineChild;
                    keys[1] = key;
                    children[1] = child;
                }
                else
                {
                    keys[0] = key;
                    children[0] = child;
                    keys[1] = map.InlineKey;
                    children[1] = map.InlineChild;
                }
                map.Kind = EdgeMapKind.SortedArray;
                map.SortedKeys = keys;
                map.SortedChildren = children;
                map.SortedCount = 2;
                map.InlineKey = 0;
                map.InlineChild = null;

                return;
            }
            case EdgeMapKind.SortedArray:
            {
                int searchResult = Array.BinarySearch(map.SortedKeys!, 0, map.SortedCount, key);
                if(searchResult >= 0)
                {
                    //Key already present — replace child in place; ordering and count unchanged.
                    map.SortedChildren![searchResult] = child;

                    return;
                }

                //Not found — Array.BinarySearch returns the bitwise complement of the insertion point.
                int insertAt = ~searchResult;
                if(map.SortedCount == map.SortedKeys!.Length)
                {
                    //Capacity exhausted — double the parallel arrays and copy.
                    int newCapacity = map.SortedKeys.Length * 2;
                    long[] newKeys = new long[newCapacity];
                    HypertrieNode?[] newChildren = new HypertrieNode?[newCapacity];
                    Array.Copy(map.SortedKeys, newKeys, map.SortedCount);
                    Array.Copy(map.SortedChildren!, newChildren, map.SortedCount);
                    map.SortedKeys = newKeys;
                    map.SortedChildren = newChildren;
                }

                //Shift the suffix one slot right to make room at insertAt.
                for(int i = map.SortedCount; i > insertAt; i--)
                {
                    map.SortedKeys![i] = map.SortedKeys[i - 1];
                    map.SortedChildren![i] = map.SortedChildren[i - 1];
                }
                map.SortedKeys![insertAt] = key;
                map.SortedChildren![insertAt] = child;
                map.SortedCount++;

                return;
            }
            default:
            {
                throw new UnreachableException();
            }
        }
    }

    /// <summary>
    /// Enumerates every entry in <paramref name="map"/>. The iterator
    /// is read-only; the map must not be mutated during enumeration.
    /// For <see cref="EdgeMapKind.SortedArray"/> the entries are
    /// yielded in ascending key order, which the worst-case-optimal
    /// join algorithms downstream depend on.
    /// </summary>
    public static IEnumerable<KeyValuePair<long, HypertrieNode?>> Enumerate(EdgeMap map)
    {
        switch(map.Kind)
        {
            case EdgeMapKind.Empty:
            {
                yield break;
            }
            case EdgeMapKind.Inline:
            {
                yield return new KeyValuePair<long, HypertrieNode?>(map.InlineKey, map.InlineChild);
                yield break;
            }
            case EdgeMapKind.SortedArray:
            {
                long[] keys = map.SortedKeys!;
                HypertrieNode?[] children = map.SortedChildren!;
                int count = map.SortedCount;

                for(int i = 0; i < count; i++)
                {
                    yield return new KeyValuePair<long, HypertrieNode?>(keys[i], children[i]);
                }

                yield break;
            }
            default:
            {
                throw new UnreachableException();
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="other"/> matches on
    /// every field — kind, inline key, sorted-array count, and
    /// reference-equal heap fields.
    /// </summary>
    public bool Equals(EdgeMap other)
    {
        return Kind == other.Kind
            && InlineKey == other.InlineKey
            && SortedCount == other.SortedCount
            && ReferenceEquals(InlineChild, other.InlineChild)
            && ReferenceEquals(SortedKeys, other.SortedKeys)
            && ReferenceEquals(SortedChildren, other.SortedChildren);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EdgeMap other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        //InlineChild, SortedKeys, SortedChildren are reference-typed;
        //HashCode.Combine hashes references by identity (none of those
        //types overrides GetHashCode), which matches the
        //reference-equality semantics used in Equals.
        return HashCode.Combine(
            Kind,
            InlineKey,
            InlineChild,
            SortedKeys,
            SortedChildren,
            SortedCount);
    }

    /// <summary>Returns <c>true</c> when the operands are equal.</summary>
    public static bool operator ==(EdgeMap left, EdgeMap right) => left.Equals(right);

    /// <summary>Returns <c>true</c> when the operands are not equal.</summary>
    public static bool operator !=(EdgeMap left, EdgeMap right) => !left.Equals(right);
}
