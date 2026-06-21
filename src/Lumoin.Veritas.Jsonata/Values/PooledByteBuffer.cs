using System;
using System.Buffers;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Jsonata.Values;

/// <summary>
/// A growable UTF-8 buffer backed by scratch rented from a <see cref="Utf8StringPool"/>, shared by the
/// engine's JSON writer and string serializer. The buffer doubles on growth and is passed by <c>ref</c>
/// (a mutable value type) so the byte sink stays allocation-free along the hot serialization path.
/// </summary>
internal struct PooledByteBuffer
{
    /// <summary>The pool the backing buffer is rented from.</summary>
    private readonly Utf8StringPool pool;

    /// <summary>The current backing buffer owner.</summary>
    private IMemoryOwner<byte> owner;

    /// <summary>The number of bytes written so far.</summary>
    private int length;

    /// <summary>Initializes a buffer renting from the given pool.</summary>
    /// <param name="pool">The pool to rent scratch from.</param>
    public PooledByteBuffer(Utf8StringPool pool)
    {
        this.pool = pool;
        owner = pool.RentScratch(64);
        length = 0;
    }

    /// <summary>Gets the bytes written so far.</summary>
    public readonly ReadOnlySpan<byte> WrittenSpan => owner.Memory.Span[..length];

    /// <summary>Appends a span of bytes, growing the backing buffer as needed.</summary>
    /// <param name="bytes">The bytes to append.</param>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        EnsureCapacity(length + bytes.Length);
        bytes.CopyTo(owner.Memory.Span[length..]);
        length += bytes.Length;
    }

    /// <summary>Appends one byte, growing the backing buffer as needed.</summary>
    /// <param name="value">The byte to append.</param>
    public void WriteByte(byte value)
    {
        EnsureCapacity(length + 1);
        owner.Memory.Span[length] = value;
        length++;
    }

    /// <summary>Returns the backing buffer to the pool.</summary>
    public readonly void Dispose()
    {
        owner.Dispose();
    }

    /// <summary>Ensures the backing buffer holds at least the requested number of bytes, doubling on growth.</summary>
    /// <param name="required">The required capacity in bytes.</param>
    private void EnsureCapacity(int required)
    {
        int capacity = owner.Memory.Length;
        if(required <= capacity)
        {
            return;
        }

        int grown = capacity;
        while(grown < required)
        {
            grown *= 2;
        }

        IMemoryOwner<byte> replacement = pool.RentScratch(grown);
        owner.Memory.Span[..length].CopyTo(replacement.Memory.Span);
        owner.Dispose();
        owner = replacement;
    }
}
