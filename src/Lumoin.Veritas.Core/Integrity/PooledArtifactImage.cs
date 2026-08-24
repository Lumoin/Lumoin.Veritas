using System;
using System.Buffers;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// A pooled, owned artifact-image byte buffer (<see cref="PooledBuffer{T}"/>) — a self-describing,
/// block-checksummed image a repair pass produces (a system-of-record restored from local parity, or a
/// re-derived sidecar/sketch/parity). The bytes stay valid until disposed; a repair pass hands these to its
/// <see cref="RepairPassReport"/>, which owns and disposes them once the caller has staged the images into a
/// healed generation. Being its own type keeps a produced image from being interchanged with another byte buffer
/// of a different purpose (a parity block, say).
/// </summary>
public sealed class PooledArtifactImage: PooledBuffer<byte>
{
    /// <summary>Wraps a rented buffer whose first <paramref name="length"/> bytes are the image.</summary>
    /// <param name="owner">The rented buffer owner; this image takes ownership and disposes it.</param>
    /// <param name="length">The image byte length.</param>
    private PooledArtifactImage(IMemoryOwner<byte> owner, int length): base(owner, length)
    {
    }

    /// <summary>Rents an image buffer of <paramref name="length"/> bytes from <paramref name="pool"/>; the bytes are uninitialized until written through <see cref="PooledBuffer{T}.WritableSpan"/>. The pool is threaded in by the caller, who owns and disposes the image.</summary>
    /// <param name="pool">The pool the image buffer is rented from.</param>
    /// <param name="length">The image byte length; positive.</param>
    /// <returns>The pooled image.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is not positive.</exception>
    public static PooledArtifactImage Rent(MemoryPool<byte> pool, int length)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        return new PooledArtifactImage(pool.Rent(length), length);
    }

    /// <summary>Takes ownership of a pre-rented buffer an image was written directly into — for serializers that emit into a growable pooled writer (a <see cref="Lumoin.Veritas.Core.Memory.SlabBufferWriter"/>) and detach the concatenated result. The image disposes the owner.</summary>
    /// <param name="owner">The rented buffer owner; the image takes ownership and disposes it.</param>
    /// <param name="length">The image byte length (the written prefix of the rented buffer).</param>
    /// <returns>The pooled image.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative or longer than the rented buffer.</exception>
    public static PooledArtifactImage Own(IMemoryOwner<byte> owner, int length)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, owner.Memory.Length);

        return new PooledArtifactImage(owner, length);
    }
}
