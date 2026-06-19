using System;
using System.Buffers;
using Lumoin.Veritas.Core.Persistence.Manifest;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// A serialized persistence-artifact image the storage self-heal tests stage and damage: the artifact's
/// manifest role together with its bytes held in a buffer rented from a caller-supplied
/// <see cref="System.Buffers.MemoryPool{T}"/> — threaded in by the test, never a shared singleton — rather than
/// a loose array, so an image is a semantic value with pooled, owned storage, the test analog of
/// <see cref="Lumoin.Veritas.Core.Columnar.PooledColumnarImageSource"/>. The read view feeds the verify,
/// checksum, and staging paths; the writable view is how the fault injectors damage the image in place.
/// Disposing returns the buffer to the pool the caller threaded in.
/// </summary>
internal sealed class ArtifactImage: IDisposable
{
    /// <summary>The rented buffer owner; returned to the pool on dispose.</summary>
    private readonly IMemoryOwner<byte> owner;

    /// <summary>Whether the buffer has been returned to the pool.</summary>
    private bool disposed;

    /// <summary>Wraps a rented buffer whose first <paramref name="length"/> bytes are the image.</summary>
    /// <param name="owner">The rented buffer owner; this image takes ownership and disposes it.</param>
    /// <param name="length">The image length in bytes.</param>
    /// <param name="role">The artifact's manifest role.</param>
    private ArtifactImage(IMemoryOwner<byte> owner, int length, ManifestFileRole role)
    {
        this.owner = owner;
        Length = length;
        Role = role;
    }

    /// <summary>The artifact's manifest role.</summary>
    internal ManifestFileRole Role { get; }

    /// <summary>The image length in bytes.</summary>
    internal int Length { get; }

    /// <summary>The image bytes, for verifying, checksumming, and staging.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been returned to the pool.</exception>
    internal ReadOnlySpan<byte> Bytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            return owner.Memory.Span[..Length];
        }
    }

    /// <summary>The image bytes as a writable view, the surface the fault injectors damage in place.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been returned to the pool.</exception>
    internal Span<byte> WritableBytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            return owner.Memory.Span[..Length];
        }
    }

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
