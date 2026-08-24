using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Tests.MemoryPool;

/// <summary>
/// A test memory pool that makes a buffer's lifetime end <em>visible</em>: it rents exact-size buffers from an
/// inner <see cref="VeritasMemoryPool{T}"/> and, the moment a rented buffer is disposed, overwrites its bytes
/// with a poison sentinel before returning it. Any code that reads a <see cref="ReadOnlyMemory{T}"/> view of a
/// buffer <em>after</em> its owner was disposed reads poison rather than the still-intact bytes the garbage
/// collector would have left behind — so a disposal-order slip in an owned-buffer pipeline fails an assertion
/// instead of silently yielding a "verified-but-wrong" result. It also counts outstanding rentals so a test can
/// assert every buffer it rented was returned (a leak, the opposite failure, is caught the same turn).
/// </summary>
/// <typeparam name="T">The unmanaged element type, so the buffer's raw bytes can be poisoned regardless of <typeparamref name="T"/>.</typeparam>
internal sealed class PoisoningMemoryPool<T>: System.Buffers.MemoryPool<T> where T : unmanaged
{
    /// <summary>The byte the buffer is filled with on return; a recognizable non-zero "dead" marker.</summary>
    private const byte PoisonByte = 0xDD;

    /// <summary>The inner exact-size pool the buffers are actually rented from and returned to.</summary>
    private readonly VeritasMemoryPool<T> inner = new();

    /// <summary>The number of rented buffers not yet returned; read through <see cref="OutstandingRentals"/>.</summary>
    private int outstanding;

    /// <summary>The number of buffers rented from this pool that have not yet been disposed; a test asserts this is zero once its owners are released.</summary>
    internal int OutstandingRentals => Volatile.Read(ref outstanding);

    /// <summary>The largest buffer the inner pool serves.</summary>
    public override int MaxBufferSize => inner.MaxBufferSize;

    /// <summary>Rents an exact-size buffer whose disposal poisons its bytes and decrements the outstanding count.</summary>
    /// <param name="minBufferSize">The minimum buffer size, or -1 for the pool default.</param>
    /// <returns>The rented buffer owner.</returns>
    public override IMemoryOwner<T> Rent(int minBufferSize = -1)
    {
        IMemoryOwner<T> innerOwner = inner.Rent(minBufferSize);
        Interlocked.Increment(ref outstanding);

        return new PoisoningOwner(this, innerOwner);
    }

    /// <summary>Disposes the inner pool.</summary>
    /// <param name="disposing">Whether managed resources are being released.</param>
    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            inner.Dispose();
        }
    }

    /// <summary>Records that one rented buffer was returned.</summary>
    private void OnReturned()
    {
        Interlocked.Decrement(ref outstanding);
    }

    /// <summary>An owner that poisons its buffer's bytes on dispose before returning it to the inner pool, so a read of a view taken over it after disposal sees the sentinel rather than the original bytes.</summary>
    private sealed class PoisoningOwner: IMemoryOwner<T>
    {
        /// <summary>The pool to notify on return.</summary>
        private readonly PoisoningMemoryPool<T> pool;

        /// <summary>The inner owner whose buffer is poisoned then returned.</summary>
        private readonly IMemoryOwner<T> inner;

        /// <summary>Whether this owner has already been disposed; a second dispose is a no-op.</summary>
        private bool disposed;

        /// <summary>Wraps an inner owner rented from <paramref name="pool"/>'s inner pool.</summary>
        /// <param name="pool">The owning pool, notified on return.</param>
        /// <param name="inner">The inner buffer owner.</param>
        internal PoisoningOwner(PoisoningMemoryPool<T> pool, IMemoryOwner<T> inner)
        {
            this.pool = pool;
            this.inner = inner;
        }

        /// <summary>The rented buffer.</summary>
        public Memory<T> Memory => inner.Memory;

        /// <summary>Poisons the buffer's bytes, records the return, and disposes the inner owner.</summary>
        public void Dispose()
        {
            if(!disposed)
            {
                disposed = true;
                MemoryMarshal.AsBytes(inner.Memory.Span).Fill(PoisonByte);
                pool.OnReturned();
                inner.Dispose();
            }
        }
    }
}
