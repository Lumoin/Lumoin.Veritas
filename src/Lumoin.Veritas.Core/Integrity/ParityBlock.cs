using System;
using System.Buffers;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// A fixed-length block of bytes in the capacity-1 parity code's domain — a data block being protected, the
/// encoded parity block, or a recovered block — as a pooled, owned buffer (<see cref="PooledBuffer{T}"/>). The
/// read view feeds <see cref="ParityCodec.Encode"/> and <see cref="ParityCodec.Restore"/> and compares recovered
/// bytes; the writable view is how a block's bytes are laid down; disposing returns the buffer to the pool the
/// caller threaded in. Being its own type keeps a parity block from being interchanged with another byte buffer
/// of a different purpose (a restored artifact image, say).
/// </summary>
public sealed class ParityBlock: PooledBuffer<byte>
{
    /// <summary>Wraps a rented buffer whose first <paramref name="length"/> bytes are the block.</summary>
    /// <param name="owner">The rented buffer owner; this block takes ownership and disposes it.</param>
    /// <param name="length">The block length in bytes.</param>
    private ParityBlock(IMemoryOwner<byte> owner, int length): base(owner, length)
    {
    }

    /// <summary>Rents a block of <paramref name="length"/> bytes from <paramref name="pool"/>; the bytes are uninitialized until written through <see cref="PooledBuffer{T}.WritableSpan"/>. The pool is threaded in by the caller rather than taken from a shared singleton, so the caller owns and disposes every block it rents.</summary>
    /// <param name="pool">The pool the block buffer is rented from.</param>
    /// <param name="length">The block length in bytes; positive, since a zero-width block has no parity-code meaning.</param>
    /// <returns>The pooled block.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is not positive.</exception>
    public static ParityBlock Rent(MemoryPool<byte> pool, int length)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        return new ParityBlock(pool.Rent(length), length);
    }
}
