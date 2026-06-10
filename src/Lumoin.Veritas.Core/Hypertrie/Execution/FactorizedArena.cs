using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// A value-type view over a run of <see cref="uint"/>s the arena owns: a backing
/// array, a start offset into it, and a length. Reads and writes index the
/// backing directly — a value-type cursor over a flat buffer — so the slice
/// carries no <see cref="Memory{T}"/> or <see cref="IMemoryOwner{T}"/>; the
/// owning <see cref="FactorizedArena"/> holds the rental and frees it in bulk.
/// A slice is only valid until its arena is disposed.
/// </summary>
[DebuggerDisplay("ArenaSlice Length={Length}")]
public readonly struct ArenaSlice: IEquatable<ArenaSlice>
{
    /// <summary>The empty slice — zero length, no backing run.</summary>
    public static ArenaSlice Empty => new([], 0, 0);

    /// <summary>The backing array the slice runs over; owned by the arena.</summary>
    private readonly uint[] backing;

    /// <summary>The start offset of this slice's run within <see cref="backing"/>.</summary>
    private readonly int offset;

    /// <summary>Constructs a slice over a run of a backing array.</summary>
    /// <param name="backing">The backing array.</param>
    /// <param name="offset">The run's start offset.</param>
    /// <param name="length">The run's length.</param>
    internal ArenaSlice(uint[] backing, int offset, int length)
    {
        this.backing = backing;
        this.offset = offset;
        Length = length;
    }

    /// <summary>The slice's length in elements.</summary>
    public int Length { get; }

    /// <summary>The slice's run as a writable span, for bulk fill and read.</summary>
    public Span<uint> Span => backing.AsSpan(offset, Length);

    /// <summary>One element of the slice.</summary>
    /// <param name="index">The index, below <see cref="Length"/>.</param>
    /// <returns>The element.</returns>
    public uint this[int index] => backing[offset + index];

    /// <summary>Whether two slices view the same run — the same backing, offset, and length.</summary>
    /// <param name="other">The other slice.</param>
    /// <returns><see langword="true"/> when they view the same run.</returns>
    public bool Equals(ArenaSlice other)
    {
        return ReferenceEquals(backing, other.backing) && offset == other.offset && Length == other.Length;
    }

    /// <summary>Whether the object is a slice viewing the same run.</summary>
    /// <param name="obj">The object.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public override bool Equals(object? obj)
    {
        return obj is ArenaSlice other && Equals(other);
    }

    /// <summary>A hash over the viewed run's identity.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(RuntimeHelpers.GetHashCode(backing), offset, Length);
    }

    /// <summary>Whether two slices view the same run.</summary>
    /// <param name="left">The left slice.</param>
    /// <param name="right">The right slice.</param>
    /// <returns><see langword="true"/> when equal.</returns>
    public static bool operator ==(ArenaSlice left, ArenaSlice right)
    {
        return left.Equals(right);
    }

    /// <summary>Whether two slices view different runs.</summary>
    /// <param name="left">The left slice.</param>
    /// <param name="right">The right slice.</param>
    /// <returns><see langword="true"/> when not equal.</returns>
    public static bool operator !=(ArenaSlice left, ArenaSlice right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// A query-scoped bump arena over <see cref="uint"/> runs, layered on
/// <see cref="VeritasMemoryPool{T}"/>: it rents slab buffers from the pool,
/// hands out value-type <see cref="ArenaSlice"/>s by advancing a cursor within
/// the current slab, and returns every slab to the pool in one
/// <see cref="Dispose"/>. This is the <c>uint</c> counterpart of
/// <see cref="Utf8StringPool"/>'s byte arena, and the explicit single lifetime a
/// query's factorised buffers share — no per-allocation owner object, no
/// per-buffer disposal, no reliance on the garbage collector to reclaim the run.
/// </summary>
/// <remarks>
/// Not thread-safe: an arena is used by one query's build on one thread, then
/// disposed. A slice outlives nothing — reading it after <see cref="Dispose"/>
/// reads memory the pool may have handed to another caller.
/// </remarks>
[DebuggerDisplay("FactorizedArena Slabs={slabs.Count} SlabElements={slabElements} Position={position}")]
public sealed class FactorizedArena: IDisposable
{
    /// <summary>The default slab size in elements (16 KiB of <see cref="uint"/>s).</summary>
    public const int DefaultSlabElements = 4096;

    /// <summary>The pool slabs are rented from.</summary>
    private readonly MemoryPool<uint> pool;

    /// <summary>Whether this arena created and therefore disposes <see cref="pool"/>.</summary>
    private readonly bool ownsPool;

    /// <summary>The size, in elements, of a freshly rented slab.</summary>
    private readonly int slabElements;

    /// <summary>Every slab rented over the arena's lifetime, returned in bulk on disposal.</summary>
    private readonly List<IMemoryOwner<uint>> slabs = [];

    /// <summary>The current slab's backing array; allocations run within it.</summary>
    private uint[] current = [];

    /// <summary>The current slab's start offset within its backing array.</summary>
    private int currentBase;

    /// <summary>The current slab's length in elements.</summary>
    private int currentLength;

    /// <summary>The cursor within the current slab — the next free element.</summary>
    private int position;

    /// <summary>Whether the arena's slabs have been returned.</summary>
    private bool disposed;

    /// <summary>Constructs an arena over a pool.</summary>
    /// <param name="pool">The pool to rent slabs from, or <see langword="null"/> to create and own a <see cref="VeritasMemoryPool{T}"/>.</param>
    /// <param name="slabElements">The slab size in elements; allocations larger than this get a dedicated slab.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slabElements"/> is not positive.</exception>
    public FactorizedArena(MemoryPool<uint>? pool = null, int slabElements = DefaultSlabElements)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slabElements);

        ownsPool = pool is null;
        this.pool = pool ?? new VeritasMemoryPool<uint>();
        this.slabElements = slabElements;
    }

    /// <summary>
    /// Allocates a run of <paramref name="length"/> elements and returns a writable
    /// slice over it. The run lives in one slab; a request larger than the slab
    /// size, or one that would overflow the current slab, opens a fresh slab.
    /// </summary>
    /// <param name="length">The run length; zero yields <see cref="ArenaSlice.Empty"/>.</param>
    /// <returns>The slice, valid until <see cref="Dispose"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    /// <exception cref="ObjectDisposedException">The arena has been disposed.</exception>
    public ArenaSlice Allocate(int length)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if(length == 0)
        {
            return ArenaSlice.Empty;
        }

        if(position + length > currentLength)
        {
            GrowSlab(length);
        }

        ArenaSlice slice = new(current, currentBase + position, length);
        position += length;

        return slice;
    }

    /// <summary>Allocates a run holding a copy of the given values — the copying counterpart of <see cref="Allocate"/>, for small fixed tuples such as group keys.</summary>
    /// <param name="values">The values the run is filled with.</param>
    /// <returns>The slice over the copied values, valid until <see cref="Dispose"/>.</returns>
    /// <exception cref="ObjectDisposedException">The arena has been disposed.</exception>
    public ArenaSlice AllocateFrom(ReadOnlySpan<uint> values)
    {
        ArenaSlice slice = Allocate(values.Length);
        values.CopyTo(slice.Span);

        return slice;
    }

    /// <summary>Rents a slab large enough for the request and makes it current.</summary>
    /// <param name="required">The element count that must fit.</param>
    private void GrowSlab(int required)
    {
        IMemoryOwner<uint> owner = pool.Rent(Math.Max(slabElements, required));
        slabs.Add(owner);

        if(!MemoryMarshal.TryGetArray<uint>(owner.Memory, out ArraySegment<uint> segment) || segment.Array is null)
        {
            throw new InvalidOperationException("The arena's pool must rent array-backed memory.");
        }

        current = segment.Array;
        currentBase = segment.Offset;
        currentLength = segment.Count;
        position = 0;
    }

    /// <summary>Returns every slab to the pool — the arena's single lifetime end; idempotent.</summary>
    public void Dispose()
    {
        if(disposed)
        {
            return;
        }

        disposed = true;
        foreach(IMemoryOwner<uint> owner in slabs)
        {
            owner.Dispose();
        }

        slabs.Clear();
        current = [];
        currentLength = 0;
        position = 0;

        if(ownsPool)
        {
            pool.Dispose();
        }
    }
}
