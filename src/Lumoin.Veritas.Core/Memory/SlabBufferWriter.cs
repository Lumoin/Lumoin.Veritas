using System;
using System.Buffers;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Memory;

/// <summary>
/// An <see cref="IBufferWriter{T}"/> backed by slab-sized buffers rented
/// from <see cref="VeritasMemoryPool{T}"/>. Slabs grow as the codec writes
/// past the current slab's tail; on detach the slabs are concatenated into
/// a single owned buffer the caller disposes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why slabs.</b> A streaming CBOR encoder cannot predict total output
/// length up front, but it does not want to copy the buffer every time it
/// grows. The slab strategy amortises growth: each slab is sized to a
/// configurable allocation step, and slabs are linked rather than
/// reallocated. Detach concatenates the chain once into a buffer of exactly
/// the written length, returning the previous slabs to the pool.
/// </para>
/// <para>
/// <b>Lifecycle.</b> The writer rents its first slab eagerly. Each
/// <see cref="GetSpan"/> call ensures the active slab has at least the
/// requested headroom; if not, the writer commits the current slab and
/// rents another. The active slab and any committed slabs are returned to
/// the pool when <see cref="Reset"/>, <see cref="Detach"/>, or
/// <see cref="Dispose"/> is called.
/// </para>
/// </remarks>
public sealed class SlabBufferWriter: IBufferWriter<byte>, IDisposable
{
    private const int DefaultSlabSize = 4096;

    private readonly MemoryPool<byte> pool;
    private readonly int slabSize;
    private readonly List<IMemoryOwner<byte>> committed = [];
    private readonly List<int> committedLengths = [];

    private IMemoryOwner<byte>? activeSlab;
    private int activePosition;
    private int totalCommittedBytes;
    private bool disposed;

    /// <summary>
    /// Initialises a new <see cref="SlabBufferWriter"/> backed by the
    /// supplied <paramref name="pool"/>. Slabs are rented at
    /// <paramref name="slabSize"/> bytes each.
    /// </summary>
    /// <param name="pool">The pool to rent slabs from; threaded in by the caller (the abstract <see cref="MemoryPool{T}"/> so any pool — including the repair pass's injected one — flows down through the call chain).</param>
    /// <param name="slabSize">The byte length of each slab. Must be positive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slabSize"/> is not positive.</exception>
    public SlabBufferWriter(MemoryPool<byte> pool, int slabSize = DefaultSlabSize)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(slabSize, 0);

        this.pool = pool;
        this.slabSize = slabSize;
    }

    /// <summary>
    /// Gets the total number of bytes written so far, summed across all
    /// committed slabs and the active slab.
    /// </summary>
    public int BytesWritten => totalCommittedBytes + activePosition;

    /// <inheritdoc/>
    public void Advance(int count)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if(activeSlab is null)
        {
            if(count != 0)
            {
                throw new InvalidOperationException("Advance was called without a prior GetSpan/GetMemory.");
            }
            return;
        }

        if(activePosition + count > activeSlab.Memory.Length)
        {
            throw new InvalidOperationException("Advance moved past the active slab's tail.");
        }

        activePosition += count;
    }

    /// <inheritdoc/>
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return activeSlab!.Memory[activePosition..];
    }

    /// <inheritdoc/>
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return activeSlab!.Memory.Span[activePosition..];
    }

    /// <summary>
    /// Returns all rented slabs to the pool and resets the writer to its
    /// initial state. The instance can be reused after <see cref="Reset"/>.
    /// </summary>
    public void Reset()
    {
        ThrowIfDisposed();
        ReleaseAllSlabs();
    }

    /// <summary>
    /// Concatenates the written bytes into a fresh pooled buffer of exactly
    /// <see cref="BytesWritten"/> length and returns it as an
    /// <see cref="IMemoryOwner{T}"/>; when nothing has been written it returns an
    /// empty owner without renting. The writer is reset to its initial state and may
    /// be reused. The returned owner is the caller's responsibility to dispose.
    /// </summary>
    /// <returns>An owned buffer of exactly the written bytes; empty when nothing was written.</returns>
    public IMemoryOwner<byte> Detach()
    {
        ThrowIfDisposed();
        int total = BytesWritten;
        if(total == 0)
        {
            ReleaseAllSlabs();

            return EmptyMemoryOwner.Instance;
        }

        IMemoryOwner<byte> result = pool.Rent(total);
        Span<byte> output = result.Memory.Span[..total];
        int outIndex = 0;
        for(int i = 0; i < committed.Count; i++)
        {
            int length = committedLengths[i];
            committed[i].Memory.Span[..length].CopyTo(output[outIndex..]);
            outIndex += length;
        }
        if(activeSlab is not null && activePosition > 0)
        {
            activeSlab.Memory.Span[..activePosition].CopyTo(output[outIndex..]);
        }
        ReleaseAllSlabs();
        return result;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if(disposed)
        {
            return;
        }
        ReleaseAllSlabs();
        disposed = true;
    }

    private void EnsureCapacity(int sizeHint)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);

        int needed = sizeHint == 0 ? 1 : sizeHint;

        if(activeSlab is null)
        {
            int initialSize = needed > slabSize ? needed : slabSize;
            activeSlab = pool.Rent(initialSize);
            activePosition = 0;
            return;
        }

        int remaining = activeSlab.Memory.Length - activePosition;
        if(remaining >= needed)
        {
            return;
        }

        //Commit the current slab and rent a new one large enough for the request.
        committed.Add(activeSlab);
        committedLengths.Add(activePosition);
        totalCommittedBytes += activePosition;

        int newSize = needed > slabSize ? needed : slabSize;
        activeSlab = pool.Rent(newSize);
        activePosition = 0;
    }

    private void ReleaseAllSlabs()
    {
        for(int i = 0; i < committed.Count; i++)
        {
            committed[i].Dispose();
        }
        committed.Clear();
        committedLengths.Clear();
        totalCommittedBytes = 0;

        activeSlab?.Dispose();
        activeSlab = null;
        activePosition = 0;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    /// <summary>An <see cref="IMemoryOwner{T}"/> over no bytes, returned by <see cref="Detach"/> when nothing was written so no pool rental is needed.</summary>
    private sealed class EmptyMemoryOwner: IMemoryOwner<byte>
    {
        /// <summary>The shared empty owner; it holds no rented memory, so sharing and repeated disposal are safe.</summary>
        public static EmptyMemoryOwner Instance { get; } = new();

        /// <summary>Gets the empty backing memory.</summary>
        public Memory<byte> Memory => Memory<byte>.Empty;

        /// <summary>Does nothing; there is no rented memory to return.</summary>
        public void Dispose()
        {
        }
    }
}
