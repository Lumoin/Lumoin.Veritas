using System;
using System.Buffers;
using System.IO;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Integrity;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Reads and writes a standalone <see cref="ColumnarTripleIndex"/> as a self-describing sidecar
/// over the low-level buffer primitives — an <see cref="IBufferWriter{T}"/> to write, a
/// <see cref="ReadOnlySequence{T}"/> to read — rather than a <see cref="System.IO.Stream"/>. These
/// are the primitives <see cref="System.IO.Pipelines"/> is built on, so a pooled
/// <c>SlabBufferWriter</c>, a <see cref="System.IO.Pipelines.PipeWriter"/>/<see cref="System.IO.Pipelines.PipeReader"/>,
/// a memory-mapped view, or a <see cref="System.IO.RandomAccess"/> read all plug straight in with
/// no stream object. A contiguous sequence reads in place; a segmented one is linearised once
/// through the supplied pool. A memory-mapped, zero-copy reader is a later increment.
/// </summary>
public static class ColumnarIndexFile
{
    /// <summary>Writes <paramref name="index"/> as a columnar index image into <paramref name="writer"/>, with each blob checksummed under <paramref name="checksum"/>.</summary>
    /// <param name="index">The standalone index to persist.</param>
    /// <param name="writer">The buffer writer the image is written into.</param>
    /// <param name="checksum">The checksum algorithm stamped and computed per blob, or <see langword="null"/> for no checksums.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="index"/> is a graph view, or the host is big-endian.</exception>
    public static void Write(ColumnarTripleIndex index, IBufferWriter<byte> writer, ChecksumAlgorithm? checksum)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(writer);

        int size = index.ComputeSerializedSize(checksum);
        Span<byte> image = writer.GetSpan(size)[..size];
        index.WriteTo(image, checksum);
        writer.Advance(size);
    }

    /// <summary>Reads a columnar index image from <paramref name="source"/>, warm — the base columns reload with no re-pack, each blob verified against its checksum.</summary>
    /// <param name="source">The image bytes; contiguous sequences read in place, segmented ones are linearised through <paramref name="imagePool"/>.</param>
    /// <param name="imagePool">The pool a segmented image is linearised through; unused for a single-segment sequence.</param>
    /// <param name="deltaPool">The pool the transient delta triples are rented from while they are re-attached.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id on read; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <param name="backing">Where reloaded payloads live; <see langword="null"/> honors the persisted backing so a native index reloads native.</param>
    /// <param name="backend">The kernel bundle to decode with; <see langword="null"/> uses <see cref="ColumnarKernelBackend.Default"/>.</param>
    /// <returns>The reconstructed index.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The bytes are not a Veritas columnar index image, its directory is malformed, a blob fails its checksum, or the image exceeds the in-memory size limit.</exception>
    /// <exception cref="NotSupportedException">The image's version, required features, or checksum algorithm are unsupported, the image is a graph view, or the host is big-endian.</exception>
    public static ColumnarTripleIndex Read(ReadOnlySequence<byte> source, MemoryPool<byte> imagePool, MemoryPool<EncodedTriple> deltaPool, ResolveChecksumAlgorithmDelegate? resolveChecksum = null, ColumnPayloadBacking? backing = null, ColumnarKernelBackend? backend = null)
    {
        ArgumentNullException.ThrowIfNull(imagePool);
        ArgumentNullException.ThrowIfNull(deltaPool);

        if(source.IsEmpty)
        {
            throw new InvalidDataException("The bytes are too short to be a Veritas columnar index image.");
        }

        if(source.IsSingleSegment)
        {
            return ColumnarTripleIndex.ReadFrom(source.FirstSpan, deltaPool, resolveChecksum, backing, backend);
        }

        if(source.Length > int.MaxValue)
        {
            throw new InvalidDataException("The columnar index image exceeds the in-memory reader's size limit.");
        }

        int length = (int)source.Length;
        using IMemoryOwner<byte> owner = imagePool.Rent(length);
        Span<byte> image = owner.Memory.Span[..length];
        source.CopyTo(image);

        return ColumnarTripleIndex.ReadFrom(image, deltaPool, resolveChecksum, backing, backend);
    }
}
