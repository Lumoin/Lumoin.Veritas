using System;
using System.Buffers;
using System.Threading;

namespace Lumoin.Veritas.Core.Memory;

/// <summary>
/// The shared mechanism for a pooled, owned, fixed-length buffer: storage rented from a caller-threaded
/// <see cref="MemoryPool{T}"/>, read and writable views clamped to the logical length, and an idempotent
/// return-to-pool on dispose. It is <see langword="abstract"/> so every owned buffer is a NAMED subtype (for
/// example a parity block, a restored artifact image, or a decoded item segment): two buffers of the same
/// element type but different use cases are DISTINCT compile-time types a caller cannot accidentally interchange,
/// while the rent/access/dispose logic is written here once rather than duplicated per buffer. Each subtype owns
/// its own <c>Rent</c> factory and length contract (some forbid an empty buffer, some allow one).
/// </summary>
/// <typeparam name="T">The buffer element type.</typeparam>
public abstract class PooledBuffer<T>: IDisposable
{
    /// <summary>The rented buffer owner; returned to the pool on dispose.</summary>
    private readonly IMemoryOwner<T> owner;

    /// <summary>The logical length, in elements, packed at the front of the buffer.</summary>
    private readonly int length;

    /// <summary>One once the buffer has been returned; guards a second return.</summary>
    private int disposed;

    /// <summary>Wraps a rented buffer whose first <paramref name="length"/> elements are the buffer.</summary>
    /// <param name="owner">The rented buffer owner; this takes ownership and returns it on <see cref="Dispose"/>.</param>
    /// <param name="length">The logical length, in elements.</param>
    protected PooledBuffer(IMemoryOwner<T> owner, int length)
    {
        this.owner = owner;
        this.length = length;
    }

    /// <summary>The logical length, in elements.</summary>
    public int Length => length;

    /// <summary>The buffer as owned memory, clamped to <see cref="Length"/>.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been returned to the pool.</exception>
    public ReadOnlyMemory<T> Memory
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

            return owner.Memory[..length];
        }
    }

    /// <summary>The buffer elements, clamped to <see cref="Length"/>.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been returned to the pool.</exception>
    public ReadOnlySpan<T> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

            return owner.Memory.Span[..length];
        }
    }

    /// <summary>The buffer elements as a writable view, clamped to <see cref="Length"/> — the surface the buffer is laid down into.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been returned to the pool.</exception>
    public Span<T> WritableSpan
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

            return owner.Memory.Span[..length];
        }
    }

    /// <summary>Returns the rented buffer to the pool; idempotent.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Returns the rented buffer to the pool on the first dispose; idempotent. A sealed subtype with no extra resources need not override this.</summary>
    /// <param name="disposing">Whether managed resources are being released (always <see langword="true"/> here — there is no finalizer, as the rented owner is itself managed).</param>
    protected virtual void Dispose(bool disposing)
    {
        if(disposing && Interlocked.Exchange(ref disposed, 1) == 0)
        {
            owner.Dispose();
        }
    }
}
