using System;
using System.Buffers;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// A fixed-length block of bytes in the capacity-1 parity code's domain — a data block being protected, the
/// encoded parity block, or a recovered block — held in storage rented from a caller-threaded
/// <see cref="MemoryPool{T}"/> rather than a loose array, so a block is a semantic value with pooled, owned
/// storage rather than a raw buffer owner. The read view feeds <see cref="ParityCodec.Encode"/> and
/// <see cref="ParityCodec.Restore"/> and compares recovered bytes; the writable view is how a block's bytes are
/// laid down — a data block staged, a parity block encoded, or a lost block recovered in place. Disposing returns
/// the buffer to the pool the caller threaded in.
/// </summary>
public sealed class ParityBlock: IDisposable
{
    /// <summary>The rented buffer owner; returned to the pool on dispose.</summary>
    private readonly IMemoryOwner<byte> owner;

    /// <summary>Whether the buffer has been returned to the pool.</summary>
    private bool disposed;

    /// <summary>Wraps a rented buffer whose first <paramref name="length"/> bytes are the block.</summary>
    /// <param name="owner">The rented buffer owner; this block takes ownership and disposes it.</param>
    /// <param name="length">The block length in bytes.</param>
    private ParityBlock(IMemoryOwner<byte> owner, int length)
    {
        this.owner = owner;
        Length = length;
    }

    /// <summary>The block length in bytes.</summary>
    public int Length { get; }

    /// <summary>The block bytes, for folding into the parity, restoring from it, or comparing.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been returned to the pool.</exception>
    public ReadOnlySpan<byte> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            return owner.Memory.Span[..Length];
        }
    }

    /// <summary>The block bytes as a writable view, the surface a data block is staged, a parity block is encoded, or a lost block is recovered into.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been returned to the pool.</exception>
    public Span<byte> WritableSpan
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            return owner.Memory.Span[..Length];
        }
    }

    /// <summary>The block bytes as owned memory, for passing as a codec block handle to <see cref="ParityCodec.Encode"/> and <see cref="ParityCodec.Restore"/>.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been returned to the pool.</exception>
    public ReadOnlyMemory<byte> Memory
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            return owner.Memory[..Length];
        }
    }

    /// <summary>Rents a block of <paramref name="length"/> bytes from <paramref name="pool"/>; the bytes are uninitialized until written through <see cref="WritableSpan"/>. The pool is threaded in by the caller rather than taken from a shared singleton, so the caller owns and disposes every block it rents.</summary>
    /// <param name="pool">The pool the block buffer is rented from.</param>
    /// <param name="length">The block length in bytes; positive, since a zero-width block has no parity-code meaning.</param>
    /// <returns>The pooled block.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is not positive.</exception>
    public static ParityBlock Rent(MemoryPool<byte> pool, int length)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        IMemoryOwner<byte> owner = pool.Rent(length);

        return new ParityBlock(owner, length);
    }

    /// <summary>Returns the rented buffer to the pool.</summary>
    public void Dispose()
    {
        if(!disposed)
        {
            disposed = true;
            owner.Dispose();
        }
    }
}
