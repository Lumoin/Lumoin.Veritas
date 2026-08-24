using System;
using System.Buffers;

namespace Lumoin.Veritas.Core.Persistence;

/// <summary>
/// A <see cref="SegmentImageSource"/> over a pooled buffer holding the whole image — the form a
/// streamed read or a linearised multi-segment sequence takes, and the buffer a decorating source
/// materialises into. Disposing returns the buffer to the pool it was rented from.
/// </summary>
public sealed class PooledSegmentImageSource : SegmentImageSource
{
    /// <summary>The rented buffer owner; disposed with this source.</summary>
    private readonly IMemoryOwner<byte> owner;

    /// <summary>The image length within the buffer.</summary>
    private readonly int length;

    /// <summary>Whether the buffer has been returned to the pool.</summary>
    private bool disposed;

    /// <summary>Wraps a rented buffer whose first <paramref name="length"/> bytes are the image.</summary>
    /// <param name="owner">The rented buffer owner; this source takes ownership and disposes it.</param>
    /// <param name="length">The image length in bytes; non-negative and within the buffer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative or exceeds the buffer.</exception>
    public PooledSegmentImageSource(IMemoryOwner<byte> owner, int length)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, owner.Memory.Length);

        this.owner = owner;
        this.length = length;
    }

    /// <inheritdoc/>
    /// <inheritdoc/>
    public override long Length => length;

    /// <inheritdoc/>
    /// <remarks>A pooled image lives in one rented buffer, so its whole range is span-addressable; the long offset only ever narrows within that buffer.</remarks>
    public override ReadOnlySpan<byte> Slice(long offset, int length)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if(offset > (long)this.length - length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, $"A window of {length} bytes at offset {offset} does not fit an image of {this.length} bytes.");
        }

        return owner.Memory.Span.Slice((int)offset, length);
    }

    /// <summary>
    /// The whole image as read-only memory, retainable for this source's lifetime — the form a consumer that
    /// holds the bytes past the open call needs (a sketch image returned for a peer to load, a system-of-record
    /// image a parity repair reads across the pass), which a memory-mapped source cannot safely back. The
    /// consumer keeps this source alive until it is done with the memory.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The buffer has been returned to the pool.</exception>
    public ReadOnlyMemory<byte> ImageMemory
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            return owner.Memory[..length];
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if(disposing && !disposed)
        {
            disposed = true;
            owner.Dispose();
        }
    }
}
