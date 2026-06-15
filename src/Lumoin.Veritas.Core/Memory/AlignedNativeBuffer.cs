using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;

namespace Lumoin.Veritas.Core.Memory;

/// <summary>
/// A block of 64-byte-aligned unmanaged memory holding <c>ulong</c> words, off the
/// managed heap. Owns the allocation and frees it on <see cref="Dispose"/>, with a
/// finalizer backstop so the block is reclaimed even when the owner is dropped without
/// disposing. Freeing happens exactly once whichever of dispose or finalize runs first.
/// </summary>
/// <remarks>
/// <para>
/// The alignment matches the widest SIMD lane the column kernels use, so a payload
/// copied or mapped here needs no realignment. The block is fixed for its whole lifetime,
/// so a span over it never moves; a caller holds that span only for the duration of one
/// read, never across the point where the owner is released.
/// </para>
/// </remarks>
public sealed unsafe class AlignedNativeBuffer: IDisposable
{
    /// <summary>The alignment, in bytes, of the native block — the widest column-kernel SIMD lane.</summary>
    private const int Alignment = 64;

    /// <summary>The native block's base address, or 0 once freed (or for an empty block). A naked field: <see cref="Free"/> clears it via <see cref="Interlocked.Exchange(ref nint, nint)"/>, which needs a <c>ref</c> to the field.</summary>
    private nint pointer;

    /// <summary>Wraps an allocated base address; 0 for an empty block.</summary>
    /// <param name="pointer">The native base address, or 0 for an empty block.</param>
    /// <param name="length">The word count.</param>
    private AlignedNativeBuffer(nint pointer, int length)
    {
        this.pointer = pointer;
        Length = length;
    }

    /// <summary>Allocates a 64-byte-aligned native block of <paramref name="length"/> <c>ulong</c> words; the contents are uninitialized.</summary>
    /// <param name="length">The word count; 0 allocates nothing and yields an empty block.</param>
    /// <returns>The owning buffer.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    public static AlignedNativeBuffer Allocate(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if(length == 0)
        {
            return new AlignedNativeBuffer(0, 0);
        }

        nuint byteCount = (nuint)length * sizeof(ulong);
        nint allocated = (nint)NativeMemory.AlignedAlloc(byteCount, Alignment);

        return new AlignedNativeBuffer(allocated, length);
    }

    /// <summary>The number of <c>ulong</c> words the block holds.</summary>
    public int Length { get; }

    /// <summary>The native block as a writable span; empty for an empty block.</summary>
    /// <exception cref="ObjectDisposedException">The block was non-empty and has been freed.</exception>
    public Span<ulong> Span
    {
        get
        {
            if(Length == 0)
            {
                return Span<ulong>.Empty;
            }

            nint current = Volatile.Read(ref pointer);
            ObjectDisposedException.ThrowIf(current == 0, this);

            return new Span<ulong>((void*)current, Length);
        }
    }

    /// <summary>Hands a handle over the native address at <paramref name="elementIndex"/>; the block is already fixed, so there is nothing to unpin.</summary>
    /// <param name="elementIndex">The element offset to address; may equal <see cref="Length"/> for the one-past-end position.</param>
    /// <returns>A handle over the native address (no GC pin).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="elementIndex"/> is negative or past the block.</exception>
    /// <exception cref="ObjectDisposedException">The block was non-empty and has been freed.</exception>
    public MemoryHandle Pin(int elementIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elementIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(elementIndex, Length);

        if(Length == 0)
        {
            return default;
        }

        nint current = Volatile.Read(ref pointer);
        ObjectDisposedException.ThrowIf(current == 0, this);

        return new MemoryHandle((ulong*)current + elementIndex);
    }

    /// <summary>Frees the native block; idempotent and safe against the finalizer.</summary>
    public void Dispose()
    {
        Free();
        GC.SuppressFinalize(this);
    }

    /// <summary>Reclaims the native block when <see cref="Dispose"/> was not called.</summary>
    ~AlignedNativeBuffer()
    {
        Free();
    }

    /// <summary>Frees the block exactly once — whichever of dispose or finalize wins the exchange.</summary>
    private void Free()
    {
        nint current = Interlocked.Exchange(ref pointer, 0);
        if(current != 0)
        {
            NativeMemory.AlignedFree((void*)current);
        }
    }
}
