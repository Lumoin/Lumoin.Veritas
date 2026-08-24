using System;
using System.Buffers;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence.Manifest;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// A serialized persistence-artifact image the storage self-heal tests stage and damage: a pooled, owned
/// buffer (<see cref="PooledBuffer{T}"/>) carrying the artifact's manifest role alongside its bytes, the test
/// analog of <see cref="Lumoin.Veritas.Core.Persistence.PooledSegmentImageSource"/> and a sibling of the
/// production owned-image type <see cref="Lumoin.Veritas.Core.Integrity.PooledArtifactImage"/>. The read view
/// (<see cref="Bytes"/>) feeds the verify, checksum, and staging paths; the writable view
/// (<see cref="WritableBytes"/>) is how the fault injectors damage the image in place. Disposing returns the
/// buffer to the pool the caller threaded in. Being its own type keeps a staged image from being interchanged
/// with another byte buffer of a different purpose.
/// </summary>
internal sealed class ArtifactImage: PooledBuffer<byte>
{
    /// <summary>Wraps a rented buffer whose first <paramref name="length"/> bytes are the image.</summary>
    /// <param name="owner">The rented buffer owner; this image takes ownership and disposes it.</param>
    /// <param name="length">The image length in bytes.</param>
    /// <param name="role">The artifact's manifest role.</param>
    private ArtifactImage(IMemoryOwner<byte> owner, int length, ManifestFileRole role): base(owner, length)
    {
        Role = role;
    }

    /// <summary>The artifact's manifest role.</summary>
    internal ManifestFileRole Role { get; }

    /// <summary>The image bytes, for verifying, checksumming, and staging — the role-named alias of <see cref="PooledBuffer{T}.Span"/>.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been returned to the pool.</exception>
    internal ReadOnlySpan<byte> Bytes => Span;

    /// <summary>The image bytes as a writable view, the surface the fault injectors damage in place — the role-named alias of <see cref="PooledBuffer{T}.WritableSpan"/>.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been returned to the pool.</exception>
    internal Span<byte> WritableBytes => WritableSpan;

    /// <summary>Takes ownership of a pre-rented buffer a builder wrote a serialized image directly into.</summary>
    /// <param name="owner">The rented buffer owner; the image takes ownership and disposes it.</param>
    /// <param name="length">The image length in bytes (the written prefix of the rented buffer).</param>
    /// <param name="role">The artifact's manifest role.</param>
    /// <returns>The pooled image.</returns>
    internal static ArtifactImage Own(IMemoryOwner<byte> owner, int length, ManifestFileRole role)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, owner.Memory.Length);

        return new ArtifactImage(owner, length, role);
    }

    /// <summary>Rents a buffer from <paramref name="pool"/> and copies a freshly serialized image into it — for serializers that emit into a growable writer rather than a pre-sized span. The pool is threaded in by the caller rather than taken from a shared singleton, so a test owns and disposes every buffer it rents.</summary>
    /// <param name="serialized">The serialized artifact bytes to copy into pooled storage.</param>
    /// <param name="role">The artifact's manifest role.</param>
    /// <param name="pool">The pool the image buffer is rented from.</param>
    /// <returns>The pooled image.</returns>
    internal static ArtifactImage Copy(ReadOnlySpan<byte> serialized, ManifestFileRole role, MemoryPool<byte> pool)
    {
        ArgumentNullException.ThrowIfNull(pool);

        IMemoryOwner<byte> owner = pool.Rent(serialized.Length);
        serialized.CopyTo(owner.Memory.Span[..serialized.Length]);

        return new ArtifactImage(owner, serialized.Length, role);
    }

    /// <summary>A truncated copy: a fresh pooled image holding this image's leading bytes, dropping the trailing <paramref name="trailingBytesToDrop"/> so the declared geometry runs past the end and a decode-free verify refuses it as framing damage rather than mis-reading it.</summary>
    /// <param name="trailingBytesToDrop">The number of trailing bytes to drop; positive and within the image.</param>
    /// <param name="pool">The pool the truncated copy is rented from.</param>
    /// <returns>The truncated image.</returns>
    internal ArtifactImage Truncated(int trailingBytesToDrop, MemoryPool<byte> pool)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(trailingBytesToDrop);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(trailingBytesToDrop, Length);

        return Copy(Bytes[..(Length - trailingBytesToDrop)], Role, pool);
    }
}
