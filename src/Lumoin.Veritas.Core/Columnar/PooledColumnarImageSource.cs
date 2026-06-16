using System;
using System.Buffers;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// A <see cref="ColumnarImageSource"/> over a pooled buffer holding the whole image — the form a
/// streamed read or a linearised multi-segment sequence takes, and the buffer a decorating source
/// materialises into. Disposing returns the buffer to the pool it was rented from.
/// </summary>
public sealed class PooledColumnarImageSource : ColumnarImageSource
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
    public PooledColumnarImageSource(IMemoryOwner<byte> owner, int length)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, owner.Memory.Length);

        this.owner = owner;
        this.length = length;
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The buffer has been returned to the pool.</exception>
    public override ReadOnlySpan<byte> Image
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            return owner.Memory.Span[..length];
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
