using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// One edge map of a <see cref="HypertrieNode"/>. Maps an encoded
/// term identifier (<see cref="uint"/>) to a child
/// <see cref="NodeHandle"/> for inner nodes, or carries the key
/// alone as a presence marker for depth-1 leaves.
/// </summary>
/// <remarks>
/// <para>
/// A value-typed container with a discriminated kind. Only one
/// representation is active at a time; <see cref="Kind"/> tells
/// operations how to interpret the value-bearing fields. Mutating
/// operations are static methods taking the map by
/// <see langword="ref"/>; read-only operations take it by
/// <see langword="in"/>. This avoids hidden boxing and keeps every
/// state transition explicit at the call site.
/// </para>
/// <para>
/// <b>Promotion ladder.</b>
/// <see cref="EdgeMapKind.Empty"/> →
/// <see cref="EdgeMapKind.Inline"/> on the first inserted entry;
/// <see cref="EdgeMapKind.Inline"/> grows up to
/// <see cref="InlineCapacity"/> entries stored in
/// <see cref="InlineKeys"/> / <see cref="InlineChildren"/>;
/// the next entry promotes the map to
/// <see cref="EdgeMapKind.SortedArray"/>, allocating parallel key
/// and child buffers from <see cref="BuildPools"/>'s pools.
/// SortedArray scales without further promotion: parallel arrays
/// grow by doubling as needed. Demotion on removal is not
/// implemented.
/// </para>
/// <para>
/// <b>Inline representation.</b> Entries are stored in the
/// struct's <see cref="InlineKeys"/> and
/// <see cref="InlineChildren"/> <c>[InlineArray(8)]</c> buffers.
/// Only the prefix <c>[0..InlineCount)</c> is valid. The Inline
/// tier maintains keys in ascending order so a flip to
/// <see cref="EdgeMapKind.SortedArray"/> can copy in-place
/// without resorting, and so consumers iterating an Inline map
/// see entries in the same order they would after promotion.
/// Lookup dispatches through an <see cref="InlineKeyLookup"/>
/// delegate, which permits future hardware-accelerated
/// implementations (AVX2, AVX-512, NEON) to land behind the same
/// boundary.
/// </para>
/// <para>
/// <b>SortedArray representation.</b> Two parallel pool-rented
/// buffers — <see cref="SortedKeysOwner"/> and
/// <see cref="SortedChildrenOwner"/> — hold entries in ascending
/// key order. Only the prefix <c>[0..SortedCount)</c> is valid.
/// Lookup is <see cref="MemoryExtensions.BinarySearch{T}(ReadOnlySpan{T}, IComparable{T})"/>
/// via the generic <see cref="Span{T}"/> overload; insertion
/// shifts entries up by one. The ascending-key contract is the
/// invariant downstream worst-case-optimal join algorithms depend
/// on.
/// </para>
/// <para>
/// <b>Pool rental ownership.</b> The SortedArray tier holds
/// <see cref="IMemoryOwner{T}"/> references; the rentals are
/// disposed on grow (in <c>GrowSortedArray</c>) and on overall
/// store disposal (in <c>NodeStore.Dispose</c>'s walk). Consumers
/// outside <c>NodeStore</c> never call <c>Dispose</c> on the
/// owners directly.
/// </para>
/// <para>
/// <b>Depth-1 leaf semantics.</b> The parent node's
/// <see cref="HypertrieNode.Depth"/> determines whether the edge
/// map represents leaves (Depth == 1, descent stops; triples are
/// emitted from the edge map's keys alone) or inner nodes (Depth
/// 2 or 3, descent continues through child handles). At depth-1
/// the child handles in this map are structurally
/// <see cref="NodeHandle.None"/> and semantically unused; at
/// depth-2 and depth-3 every child handle is non-None.
/// </para>
/// <para>
/// <b>Equality.</b> Two edge maps are equal when every field is
/// equal — the kind, the counts, the inline buffers element-wise
/// in the valid prefix, and the heap-owned buffers by
/// reference. Reference equality on the heap fields keeps
/// comparison O(1); structural equality across populated arrays
/// would be linear in entry count and is not the semantic any
/// current consumer needs.
/// </para>
/// </remarks>
[DebuggerDisplay("Kind={Kind} Count={DebuggerEntryCount,nq}")]
[SuppressMessage(
    "Design",
    "CA1051:Do not declare visible instance fields",
    Justification = "InlineKeys and InlineChildren are [InlineArray(8)] buffer fields. Consumers index them positionally via the InlineArray indexer; that access requires the field to be visible. Wrapping in properties would defeat the InlineArray optimisation.")]
public struct EdgeMap: IEquatable<EdgeMap>
{
    /// <summary>The maximum number of entries stored in the Inline tier before promotion.</summary>
    public const int InlineCapacity = 8;

    /// <summary>The capacity allocated when promoting from <see cref="EdgeMapKind.Inline"/> to <see cref="EdgeMapKind.SortedArray"/>. Sized to comfortably absorb the Inline tier's contents plus a small headroom.</summary>
    private const int SortedArrayInitialCapacity = 16;

    /// <summary>The active representation.</summary>
    public EdgeMapKind Kind { get; private set; }

    /// <summary>For <see cref="EdgeMapKind.Inline"/>: the number of valid entries in <see cref="InlineKeys"/> / <see cref="InlineChildren"/>; in <c>[1, InlineCapacity]</c>.</summary>
    public byte InlineCount { get; private set; }

    /// <summary>For <see cref="EdgeMapKind.Inline"/>: inline storage for keys, ascending. Only the prefix <c>[0..InlineCount)</c> is valid.</summary>
    public InlineKeyBuffer InlineKeys;

    /// <summary>For <see cref="EdgeMapKind.Inline"/>: inline storage for child handles, parallel to <see cref="InlineKeys"/>. Only the prefix <c>[0..InlineCount)</c> is valid. At depth-1 leaves every slot is <see cref="NodeHandle.None"/>.</summary>
    public InlineChildBuffer InlineChildren;

    /// <summary>For <see cref="EdgeMapKind.SortedArray"/>: the rented keys buffer. Owns the rental; disposed on grow or on <see cref="NodeStore.Dispose"/>.</summary>
    public IMemoryOwner<uint>? SortedKeysOwner { get; private set; }

    /// <summary>For <see cref="EdgeMapKind.SortedArray"/>: the rented children buffer. Owns the rental; disposed on grow or on <see cref="NodeStore.Dispose"/>. At depth-1 leaves every slot is <see cref="NodeHandle.None"/>.</summary>
    public IMemoryOwner<NodeHandle>? SortedChildrenOwner { get; private set; }

    /// <summary>For <see cref="EdgeMapKind.SortedArray"/>: the number of valid entries; the rented buffers may be longer.</summary>
    public int SortedCount { get; private set; }

    private int DebuggerEntryCount => Count(in this);

    /// <summary>Returns the number of entries in <paramref name="map"/>.</summary>
    /// <param name="map">The map to inspect.</param>
    /// <returns>The number of valid entries.</returns>
    public static int Count(in EdgeMap map)
    {
        return map.Kind switch
        {
            EdgeMapKind.Empty => 0,
            EdgeMapKind.Inline => map.InlineCount,
            EdgeMapKind.SortedArray => map.SortedCount,
            EdgeMapKind.SortedKeysOnly => map.SortedCount,
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>
    /// Looks up <paramref name="key"/> in <paramref name="map"/>.
    /// </summary>
    /// <param name="map">The map to query.</param>
    /// <param name="key">The key to search for.</param>
    /// <param name="inlineLookup">The inline-tier lookup implementation; obtain from <see cref="InlineKeyLookups.SelectBestAvailable"/> or a cached delegate.</param>
    /// <param name="child">On <c>true</c>: the child handle associated with <paramref name="key"/>, which is <see cref="NodeHandle.None"/> at depth-1 leaves. On <c>false</c>: <see cref="NodeHandle.None"/>.</param>
    /// <returns><c>true</c> when the key is present; otherwise <c>false</c>.</returns>
    public static bool TryGetChild(in EdgeMap map, uint key, InlineKeyLookup inlineLookup, out NodeHandle child)
    {
        ArgumentNullException.ThrowIfNull(inlineLookup);

        switch(map.Kind)
        {
            case EdgeMapKind.Empty:
            {
                child = NodeHandle.None;

                return false;
            }
            case EdgeMapKind.Inline:
            {
                Debug.Assert(map.InlineCount >= 1 && map.InlineCount <= InlineCapacity,
                    "Invariant violated: Inline tier has illegal entry count.");

                int index = inlineLookup(InlineKeysSpan(in map), key);
                if(index >= 0)
                {
                    child = InlineChildrenSpan(in map)[index];

                    return true;
                }

                child = NodeHandle.None;

                return false;
            }
            case EdgeMapKind.SortedArray:
            {
                ReadOnlySpan<uint> keys = SortedKeysSpan(in map);
                int index = keys.BinarySearch(key);
                if(index >= 0)
                {
                    child = SortedChildrenSpan(in map)[index];

                    return true;
                }

                child = NodeHandle.None;

                return false;
            }
            case EdgeMapKind.SortedKeysOnly:
            {
                ReadOnlySpan<uint> keys = SortedKeysSpan(in map);

                child = NodeHandle.None;

                return keys.BinarySearch(key) >= 0;
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
    /// Promotes the map to a denser representation when capacity is
    /// exceeded.
    /// </summary>
    /// <param name="map">The map to mutate.</param>
    /// <param name="key">The key to insert or replace.</param>
    /// <param name="child">The child handle to store. <see cref="NodeHandle.None"/> is meaningful at depth-1 leaves (the key alone is the answer) and structurally meaningless elsewhere; the depth-2/depth-3 invariant is enforced by callers.</param>
    /// <param name="pools">The pools used for SortedArray buffer rentals when this insert triggers a promotion or a grow.</param>
    /// <param name="inlineLookup">The inline-tier lookup implementation, used to detect duplicate keys inside the Inline tier.</param>
    public static void InsertOrReplace(ref EdgeMap map, uint key, NodeHandle child, BuildPools pools, InlineKeyLookup inlineLookup)
    {
        ArgumentNullException.ThrowIfNull(inlineLookup);

        switch(map.Kind)
        {
            case EdgeMapKind.Empty:
            {
                map.Kind = EdgeMapKind.Inline;
                map.InlineCount = 1;
                map.InlineKeys[0] = key;
                map.InlineChildren[0] = child;

                return;
            }
            case EdgeMapKind.Inline:
            {
                int existing = inlineLookup(InlineKeysSpan(in map), key);
                if(existing >= 0)
                {
                    map.InlineChildren[existing] = child;

                    return;
                }

                if(map.InlineCount < InlineCapacity)
                {
                    InsertIntoInline(ref map, key, child);

                    return;
                }

                //Inline buffer full — promote to a sorted form.
                //When every child slot (including the incoming one)
                //is the absence sentinel — the depth-1 leaf shape —
                //the child array is pure padding; promote to the
                //keys-only form and never allocate it.
                if(child == NodeHandle.None && AllInlineChildrenAreNone(in map))
                {
                    PromoteInlineToSortedKeysOnly(ref map, key, pools);

                    return;
                }

                PromoteInlineToSortedArray(ref map, key, child, pools);

                return;
            }
            case EdgeMapKind.SortedArray:
            {
                ReadOnlySpan<uint> keys = SortedKeysSpan(in map);
                int searchResult = keys.BinarySearch(key);
                if(searchResult >= 0)
                {
                    map.SortedChildrenOwner!.Memory.Span[searchResult] = child;

                    return;
                }

                int insertAt = ~searchResult;
                if(map.SortedCount == map.SortedKeysOwner!.Memory.Length)
                {
                    GrowSortedArray(ref map, pools);
                }

                Span<uint> writableKeys = map.SortedKeysOwner!.Memory.Span;
                Span<NodeHandle> writableChildren = map.SortedChildrenOwner!.Memory.Span;

                for(int i = map.SortedCount; i > insertAt; i--)
                {
                    writableKeys[i] = writableKeys[i - 1];
                    writableChildren[i] = writableChildren[i - 1];
                }
                writableKeys[insertAt] = key;
                writableChildren[insertAt] = child;
                map.SortedCount++;

                Debug.Assert(IsSortedAscending(SortedKeysSpan(in map)),
                    "Invariant violated: SortedArray must be ascending after insert.");

                return;
            }
            case EdgeMapKind.SortedKeysOnly:
            {
                //A real child arriving at a keys-only map means the
                //map is no longer the all-absent leaf shape; upgrade
                //to the parallel-array form first, then insert
                //through the SortedArray path.
                if(child != NodeHandle.None)
                {
                    UpgradeKeysOnlyToSortedArray(ref map, pools);
                    InsertOrReplace(ref map, key, child, pools, inlineLookup);

                    return;
                }

                ReadOnlySpan<uint> keys = SortedKeysSpan(in map);
                int searchResult = keys.BinarySearch(key);
                if(searchResult >= 0)
                {
                    //Present, and the child stays absent — no-op.
                    return;
                }

                int insertAt = ~searchResult;
                if(map.SortedCount == map.SortedKeysOwner!.Memory.Length)
                {
                    GrowSortedKeysOnly(ref map, pools);
                }

                Span<uint> writableKeys = map.SortedKeysOwner!.Memory.Span;

                for(int i = map.SortedCount; i > insertAt; i--)
                {
                    writableKeys[i] = writableKeys[i - 1];
                }
                writableKeys[insertAt] = key;
                map.SortedCount++;

                Debug.Assert(IsSortedAscending(SortedKeysSpan(in map)),
                    "Invariant violated: SortedKeysOnly must be ascending after insert.");

                return;
            }
            default:
            {
                throw new UnreachableException();
            }
        }
    }

    /// <summary>
    /// Enumerates every entry in <paramref name="map"/>. Entries
    /// are yielded in ascending key order for every kind — the
    /// Inline tier maintains its keys in ascending order, and the
    /// SortedArray tier is sorted by construction.
    /// </summary>
    /// <param name="map">The map to enumerate.</param>
    /// <returns>An enumerable of <c>(key, child)</c> pairs in ascending key order.</returns>
    public static IEnumerable<KeyValuePair<uint, NodeHandle>> Enumerate(EdgeMap map)
    {
        switch(map.Kind)
        {
            case EdgeMapKind.Empty:
            {
                yield break;
            }
            case EdgeMapKind.Inline:
            {
                for(int i = 0; i < map.InlineCount; i++)
                {
                    yield return new KeyValuePair<uint, NodeHandle>(map.InlineKeys[i], map.InlineChildren[i]);
                }

                yield break;
            }
            case EdgeMapKind.SortedArray:
            {
                Memory<uint> keys = map.SortedKeysOwner!.Memory;
                Memory<NodeHandle> children = map.SortedChildrenOwner!.Memory;
                int count = map.SortedCount;

                for(int i = 0; i < count; i++)
                {
                    yield return new KeyValuePair<uint, NodeHandle>(keys.Span[i], children.Span[i]);
                }

                yield break;
            }
            case EdgeMapKind.SortedKeysOnly:
            {
                Memory<uint> keys = map.SortedKeysOwner!.Memory;
                int count = map.SortedCount;

                for(int i = 0; i < count; i++)
                {
                    yield return new KeyValuePair<uint, NodeHandle>(keys.Span[i], NodeHandle.None);
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
    /// Returns the key at <paramref name="position"/> within
    /// <paramref name="map"/>. Position is a logical index into
    /// the map's sorted-ascending key sequence: for
    /// <see cref="EdgeMapKind.Inline"/>, the index into
    /// <see cref="InlineKeys"/> in <c>[0, InlineCount)</c>; for
    /// <see cref="EdgeMapKind.SortedArray"/>, the index into the
    /// rented keys buffer in <c>[0, SortedCount)</c>.
    /// </summary>
    /// <param name="map">The map to read.</param>
    /// <param name="position">The position index.</param>
    /// <returns>The key at <paramref name="position"/>.</returns>
    public static uint KeyAt(in EdgeMap map, int position)
    {
        return map.Kind switch
        {
            EdgeMapKind.Inline => map.InlineKeys[position],
            EdgeMapKind.SortedArray => map.SortedKeysOwner!.Memory.Span[position],
            EdgeMapKind.SortedKeysOnly => map.SortedKeysOwner!.Memory.Span[position],
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>
    /// Returns the child handle at <paramref name="position"/>
    /// within <paramref name="map"/>. At depth-1 leaves this is
    /// <see cref="NodeHandle.None"/>; at depth-2 and depth-3 nodes
    /// it is a real handle. Position is the logical index into the
    /// map's sorted-ascending key sequence (same semantics as
    /// <see cref="KeyAt"/>).
    /// </summary>
    /// <param name="map">The map to read.</param>
    /// <param name="position">The position index.</param>
    /// <returns>The child handle at <paramref name="position"/>.</returns>
    public static NodeHandle ChildAt(in EdgeMap map, int position)
    {
        return map.Kind switch
        {
            EdgeMapKind.Inline => map.InlineChildren[position],
            EdgeMapKind.SortedArray => map.SortedChildrenOwner!.Memory.Span[position],
            EdgeMapKind.SortedKeysOnly => NodeHandle.None,
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>
    /// Returns a read-only span over the Inline tier's valid key
    /// prefix.
    /// </summary>
    /// <param name="map">The map whose Inline keys to span.</param>
    /// <returns>A span covering <c>[0..InlineCount)</c>.</returns>
    public static ReadOnlySpan<uint> InlineKeysSpan(in EdgeMap map)
    {
        return MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.AsRef(in map.InlineKeys[0]),
            map.InlineCount);
    }

    /// <summary>
    /// Returns a read-only span over the Inline tier's valid child
    /// prefix.
    /// </summary>
    /// <param name="map">The map whose Inline children to span.</param>
    /// <returns>A span covering <c>[0..InlineCount)</c>.</returns>
    public static ReadOnlySpan<NodeHandle> InlineChildrenSpan(in EdgeMap map)
    {
        return MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.AsRef(in map.InlineChildren[0]),
            map.InlineCount);
    }

    /// <summary>
    /// Returns a read-only span over the SortedArray tier's valid
    /// key prefix.
    /// </summary>
    /// <param name="map">The map whose SortedArray keys to span.</param>
    /// <returns>A span covering <c>[0..SortedCount)</c>.</returns>
    public static ReadOnlySpan<uint> SortedKeysSpan(in EdgeMap map)
    {
        return map.SortedKeysOwner!.Memory.Span[..map.SortedCount];
    }

    /// <summary>
    /// Returns a read-only span over the SortedArray tier's valid
    /// child prefix.
    /// </summary>
    /// <param name="map">The map whose SortedArray children to span.</param>
    /// <returns>A span covering <c>[0..SortedCount)</c>.</returns>
    public static ReadOnlySpan<NodeHandle> SortedChildrenSpan(in EdgeMap map)
    {
        return map.SortedChildrenOwner!.Memory.Span[..map.SortedCount];
    }

    /// <summary>
    /// Disposes the SortedArray tier's rented buffers, if any,
    /// returning them to their pools. Called by
    /// <see cref="NodeStore.Dispose"/> during end-of-life cleanup.
    /// </summary>
    /// <param name="map">The map whose rentals to release.</param>
    public static void DisposeRentals(ref EdgeMap map)
    {
        if(map.Kind == EdgeMapKind.SortedArray)
        {
            Debug.Assert(
                map.SortedKeysOwner is not null && map.SortedChildrenOwner is not null,
                "SortedArray EdgeMap stored without owner.");

            map.SortedKeysOwner!.Dispose();
            map.SortedChildrenOwner!.Dispose();
            map.SortedKeysOwner = null;
            map.SortedChildrenOwner = null;

            return;
        }

        if(map.Kind == EdgeMapKind.SortedKeysOnly)
        {
            Debug.Assert(map.SortedKeysOwner is not null, "SortedKeysOnly EdgeMap stored without owner.");

            map.SortedKeysOwner!.Dispose();
            map.SortedKeysOwner = null;
        }
    }

    //Inserts (key, child) into the Inline tier at the position
    //preserving ascending key order. Capacity check is the
    //caller's responsibility.
    private static void InsertIntoInline(ref EdgeMap map, uint key, NodeHandle child)
    {
        Debug.Assert(map.InlineCount < InlineCapacity, "Inline tier is full; promotion should be handled by the caller.");

        //Linear scan for insertion point; small bounded count.
        int insertAt = map.InlineCount;
        for(int i = 0; i < map.InlineCount; i++)
        {
            if(map.InlineKeys[i] > key)
            {
                insertAt = i;
                break;
            }
        }

        //Shift entries from insertAt up by one.
        for(int i = map.InlineCount; i > insertAt; i--)
        {
            map.InlineKeys[i] = map.InlineKeys[i - 1];
            map.InlineChildren[i] = map.InlineChildren[i - 1];
        }

        map.InlineKeys[insertAt] = key;
        map.InlineChildren[insertAt] = child;
        map.InlineCount++;

        Debug.Assert(IsSortedAscending(InlineKeysSpan(in map)),
            "Invariant violated: Inline tier must be ascending after insert.");
    }

    //Promotes the Inline tier (currently at InlineCapacity entries)
    //plus one additional entry into a SortedArray representation.
    private static void PromoteInlineToSortedArray(ref EdgeMap map, uint newKey, NodeHandle newChild, BuildPools pools)
    {
        Debug.Assert(map.InlineCount == InlineCapacity, "Promotion called with Inline tier not full.");

        IMemoryOwner<uint> keysOwner = pools.KeyPool.Rent(SortedArrayInitialCapacity);
        IMemoryOwner<NodeHandle> childrenOwner = pools.ChildPool.Rent(SortedArrayInitialCapacity);

        Span<uint> keys = keysOwner.Memory.Span;
        Span<NodeHandle> children = childrenOwner.Memory.Span;

        //Inline tier is ascending; merge the new entry into its sorted position via insertion sort over 9 elements.
        int insertAt = InlineCapacity;
        for(int i = 0; i < InlineCapacity; i++)
        {
            if(map.InlineKeys[i] > newKey)
            {
                insertAt = i;
                break;
            }
        }

        for(int i = 0; i < insertAt; i++)
        {
            keys[i] = map.InlineKeys[i];
            children[i] = map.InlineChildren[i];
        }

        keys[insertAt] = newKey;
        children[insertAt] = newChild;

        for(int i = insertAt; i < InlineCapacity; i++)
        {
            keys[i + 1] = map.InlineKeys[i];
            children[i + 1] = map.InlineChildren[i];
        }

        map.Kind = EdgeMapKind.SortedArray;
        map.SortedKeysOwner = keysOwner;
        map.SortedChildrenOwner = childrenOwner;
        map.SortedCount = InlineCapacity + 1;
        map.InlineCount = 0;

        Debug.Assert(IsSortedAscending(SortedKeysSpan(in map)),
            "Invariant violated: SortedArray must be ascending after promotion.");
    }

    //Returns true when every valid Inline child slot holds the
    //absence sentinel — the depth-1 leaf shape.
    private static bool AllInlineChildrenAreNone(in EdgeMap map)
    {
        for(int i = 0; i < map.InlineCount; i++)
        {
            if(map.InlineChildren[i] != NodeHandle.None)
            {
                return false;
            }
        }

        return true;
    }

    //Promotes the Inline tier (currently at InlineCapacity entries,
    //all children absent) plus one additional absent-child entry
    //into the keys-only sorted representation. No child buffer is
    //rented.
    private static void PromoteInlineToSortedKeysOnly(ref EdgeMap map, uint newKey, BuildPools pools)
    {
        Debug.Assert(map.InlineCount == InlineCapacity, "Promotion called with Inline tier not full.");

        IMemoryOwner<uint> keysOwner = pools.KeyPool.Rent(SortedArrayInitialCapacity);
        Span<uint> keys = keysOwner.Memory.Span;

        //Inline tier is ascending; merge the new key into its sorted position via insertion over 9 elements.
        int insertAt = InlineCapacity;
        for(int i = 0; i < InlineCapacity; i++)
        {
            if(map.InlineKeys[i] > newKey)
            {
                insertAt = i;
                break;
            }
        }

        for(int i = 0; i < insertAt; i++)
        {
            keys[i] = map.InlineKeys[i];
        }

        keys[insertAt] = newKey;

        for(int i = insertAt; i < InlineCapacity; i++)
        {
            keys[i + 1] = map.InlineKeys[i];
        }

        map.Kind = EdgeMapKind.SortedKeysOnly;
        map.SortedKeysOwner = keysOwner;
        map.SortedChildrenOwner = null;
        map.SortedCount = InlineCapacity + 1;
        map.InlineCount = 0;

        Debug.Assert(IsSortedAscending(SortedKeysSpan(in map)),
            "Invariant violated: SortedKeysOnly must be ascending after promotion.");
    }

    //Converts a keys-only map into the parallel-array form by
    //renting a child buffer sized to the keys buffer and filling
    //the valid prefix with the absence sentinel. Used when a real
    //child arrives at a map that had only absent children.
    private static void UpgradeKeysOnlyToSortedArray(ref EdgeMap map, BuildPools pools)
    {
        IMemoryOwner<NodeHandle> childrenOwner = pools.ChildPool.Rent(map.SortedKeysOwner!.Memory.Length);
        Span<NodeHandle> children = childrenOwner.Memory.Span;

        for(int i = 0; i < map.SortedCount; i++)
        {
            children[i] = NodeHandle.None;
        }

        map.Kind = EdgeMapKind.SortedArray;
        map.SortedChildrenOwner = childrenOwner;
    }

    //Doubles the keys-only capacity by renting a larger keys
    //buffer, copying the valid prefix, then disposing the previous
    //rental.
    private static void GrowSortedKeysOnly(ref EdgeMap map, BuildPools pools)
    {
        IMemoryOwner<uint> oldKeysOwner = map.SortedKeysOwner!;

        int newCapacity = oldKeysOwner.Memory.Length * 2;
        IMemoryOwner<uint> newKeysOwner = pools.KeyPool.Rent(newCapacity);

        oldKeysOwner.Memory.Span[..map.SortedCount].CopyTo(newKeysOwner.Memory.Span);
        oldKeysOwner.Dispose();

        map.SortedKeysOwner = newKeysOwner;
    }

    //Doubles the SortedArray capacity by renting a larger pair of
    //buffers, copying the valid prefix, then disposing the previous
    //rentals.
    private static void GrowSortedArray(ref EdgeMap map, BuildPools pools)
    {
        IMemoryOwner<uint> oldKeysOwner = map.SortedKeysOwner!;
        IMemoryOwner<NodeHandle> oldChildrenOwner = map.SortedChildrenOwner!;

        int newCapacity = oldKeysOwner.Memory.Length * 2;
        IMemoryOwner<uint> newKeysOwner = pools.KeyPool.Rent(newCapacity);
        IMemoryOwner<NodeHandle> newChildrenOwner = pools.ChildPool.Rent(newCapacity);

        oldKeysOwner.Memory.Span[..map.SortedCount].CopyTo(newKeysOwner.Memory.Span);
        oldChildrenOwner.Memory.Span[..map.SortedCount].CopyTo(newChildrenOwner.Memory.Span);

        oldKeysOwner.Dispose();
        oldChildrenOwner.Dispose();

        map.SortedKeysOwner = newKeysOwner;
        map.SortedChildrenOwner = newChildrenOwner;
    }

    //Asserts ascending order across a key span. Used by
    //Debug.Assert after every mutation that produces a sorted
    //prefix.
    private static bool IsSortedAscending(ReadOnlySpan<uint> keys)
    {
        for(int i = 1; i < keys.Length; i++)
        {
            if(keys[i - 1] > keys[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="other"/> matches
    /// on every field — kind, counts, the inline buffers
    /// element-wise in the valid prefix, and the SortedArray
    /// owners by reference.
    /// </summary>
    /// <param name="other">The other map to compare.</param>
    /// <returns><c>true</c> when equal; otherwise <c>false</c>.</returns>
    public bool Equals(EdgeMap other)
    {
        if(Kind != other.Kind || InlineCount != other.InlineCount || SortedCount != other.SortedCount)
        {
            return false;
        }

        if(Kind == EdgeMapKind.Inline)
        {
            for(int i = 0; i < InlineCount; i++)
            {
                if(InlineKeys[i] != other.InlineKeys[i] || InlineChildren[i] != other.InlineChildren[i])
                {
                    return false;
                }
            }

            return true;
        }

        return ReferenceEquals(SortedKeysOwner, other.SortedKeysOwner)
            && ReferenceEquals(SortedChildrenOwner, other.SortedChildrenOwner);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EdgeMap other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(Kind, InlineCount, SortedCount, SortedKeysOwner, SortedChildrenOwner);
    }

    /// <summary>Returns <c>true</c> when the operands are equal.</summary>
    public static bool operator ==(EdgeMap left, EdgeMap right) => left.Equals(right);

    /// <summary>Returns <c>true</c> when the operands are not equal.</summary>
    public static bool operator !=(EdgeMap left, EdgeMap right) => !left.Equals(right);
}
