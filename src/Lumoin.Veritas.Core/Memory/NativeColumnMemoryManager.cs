using System;
using System.Buffers;

namespace Lumoin.Veritas.Core.Memory;

/// <summary>
/// Surfaces an <see cref="AlignedNativeBuffer"/> as a <see cref="ReadOnlyMemory{T}"/> of
/// <c>ulong</c> — the storable whole-column handle the column seam holds. This manager
/// carries no finalizer: its unmanaged resource is the buffer, which reclaims itself, so a
/// memory derived here stays valid until the whole chain is unreachable, at which point the
/// buffer's finalizer frees the block. Keeping the finalizer on the buffer (not on this
/// manager) is what makes the off-GC payload both a <see cref="ReadOnlyMemory{T}"/> and
/// concurrency-safe to reclaim.
/// </summary>
public sealed class NativeColumnMemoryManager: MemoryManager<ulong>
{
    /// <summary>The owning native block this manager views.</summary>
    private AlignedNativeBuffer Buffer { get; }

    /// <summary>Views <paramref name="buffer"/> as managed memory.</summary>
    /// <param name="buffer">The native block to view.</param>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <see langword="null"/>.</exception>
    public NativeColumnMemoryManager(AlignedNativeBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        Buffer = buffer;
    }

    /// <inheritdoc/>
    public override Span<ulong> GetSpan()
    {
        return Buffer.Span;
    }

    /// <inheritdoc/>
    public override MemoryHandle Pin(int elementIndex = 0)
    {
        return Buffer.Pin(elementIndex);
    }

    /// <inheritdoc/>
    public override void Unpin()
    {
        //The native block is fixed for its lifetime; nothing to release.
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            Buffer.Dispose();
        }
    }
}
