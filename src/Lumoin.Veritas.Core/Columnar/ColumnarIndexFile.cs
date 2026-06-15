using System;
using System.Buffers;
using System.IO;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Reads and writes a standalone <see cref="ColumnarTripleIndex"/> as a self-describing sidecar
/// over a <see cref="Stream"/> — the copy-based file surface for the columnar index container.
/// The image and the transient delta are staged through caller-injected pools, so a deployment
/// can route persistence through a tagged (for example secure or native) pool; a memory-mapped,
/// zero-copy reader that addresses the same on-disk layout in place is a later increment.
/// </summary>
public static class ColumnarIndexFile
{
    /// <summary>Writes <paramref name="index"/> as a columnar index image to <paramref name="destination"/>.</summary>
    /// <param name="index">The standalone index to persist.</param>
    /// <param name="destination">The stream to write the image to.</param>
    /// <param name="imagePool">The pool the staging image buffer is rented from.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="index"/> is a graph view, or the host is big-endian.</exception>
    public static void Write(ColumnarTripleIndex index, Stream destination, MemoryPool<byte> imagePool)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(imagePool);

        int size = index.SerializedSize;
        using IMemoryOwner<byte> owner = imagePool.Rent(size);
        Span<byte> image = owner.Memory.Span[..size];
        index.WriteTo(image);
        destination.Write(image);
    }

    /// <summary>Reads a columnar index image from <paramref name="source"/>, warm — the base columns reload with no re-pack.</summary>
    /// <param name="source">The seekable stream positioned at the image; the image runs to the stream end.</param>
    /// <param name="imagePool">The pool the staging image buffer is rented from.</param>
    /// <param name="deltaPool">The pool the transient delta triples are rented from while they are re-attached.</param>
    /// <param name="backing">Where reloaded payloads live; <see langword="null"/> honors the persisted backing so a native index reloads native.</param>
    /// <param name="backend">The kernel bundle to decode with; <see langword="null"/> uses <see cref="ColumnarKernelBackend.Default"/>.</param>
    /// <returns>The reconstructed index.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The bytes are not a Veritas columnar index image, or its directory is malformed.</exception>
    /// <exception cref="NotSupportedException">The image's version or required features are unsupported, the image is a graph view, or the host is big-endian.</exception>
    public static ColumnarTripleIndex Read(Stream source, MemoryPool<byte> imagePool, MemoryPool<EncodedTriple> deltaPool, ColumnPayloadBacking? backing = null, ColumnarKernelBackend? backend = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(imagePool);
        ArgumentNullException.ThrowIfNull(deltaPool);

        int size = checked((int)(source.Length - source.Position));
        if(size <= 0)
        {
            throw new InvalidDataException("The stream contains no columnar index image.");
        }

        using IMemoryOwner<byte> owner = imagePool.Rent(size);
        Span<byte> image = owner.Memory.Span[..size];
        source.ReadExactly(image);

        return ColumnarTripleIndex.ReadFrom(image, deltaPool, backing, backend);
    }
}
