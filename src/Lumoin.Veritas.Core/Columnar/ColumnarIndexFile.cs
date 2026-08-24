using System;
using System.Buffers;
using System.IO;
using System.Runtime.Versioning;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Persistence;
using Microsoft.Win32.SafeHandles;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Reads and writes a standalone <see cref="ColumnarTripleIndex"/> as a self-describing sidecar
/// over the low-level buffer primitives — an <see cref="IBufferWriter{T}"/> to write, a
/// <see cref="ReadOnlySequence{T}"/> to read — rather than a <see cref="System.IO.Stream"/>. These
/// are the primitives <see cref="System.IO.Pipelines"/> is built on, so a pooled
/// <c>SlabBufferWriter</c>, a <see cref="System.IO.Pipelines.PipeWriter"/>/<see cref="System.IO.Pipelines.PipeReader"/>,
/// a memory-mapped view, or a <see cref="System.IO.RandomAccess"/> read all plug straight in with
/// no stream object. A contiguous sequence reads in place; a segmented one is linearised once
/// through the supplied pool.
/// </summary>
/// <remarks>
/// <para>
/// Where the image bytes come from is an open extension point: <see cref="Read(SegmentImageSource, MemoryPool{EncodedTriple}, ResolveChecksumAlgorithmDelegate?, ColumnPayloadBacking?, ColumnarKernelBackend?)"/>
/// decodes any <see cref="SegmentImageSource"/> — a memory-mapped file, a pooled buffer, or a
/// deployment's own decrypting or remote source — through the same verified path, so each blob is
/// checksum-verified as it is decoded regardless of the source. The
/// <see cref="Read(SafeFileHandle, ColumnAccessMode, MemoryPool{byte}, MemoryPool{EncodedTriple}, ResolveChecksumAlgorithmDelegate?, ColumnPayloadBacking?, ColumnarKernelBackend?)"/>
/// overload selects a built-in source from the resolved <see cref="ColumnAccessMode"/>. A reader that
/// serves columns directly from a mapped view for their whole lifetime — zero-copy rather than
/// decode-into-backing — is a later increment.
/// </para>
/// </remarks>
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

    /// <summary>Reads a columnar index image from any <paramref name="source"/>, warm — the base columns reload with no re-pack and each blob is verified against its checksum as it is decoded. The caller owns and disposes <paramref name="source"/>; this read borrows it.</summary>
    /// <param name="source">The image source — a memory-mapped file, a pooled buffer, or a deployment's own source.</param>
    /// <param name="deltaPool">The pool the transient delta triples are rented from while they are re-attached.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id on read; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <param name="backing">Where reloaded payloads live; <see langword="null"/> honors the persisted backing.</param>
    /// <param name="backend">The kernel bundle to decode with; <see langword="null"/> uses <see cref="ColumnarKernelBackend.Default"/>.</param>
    /// <returns>The reconstructed index.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The bytes are not a Veritas columnar index image, its directory is malformed, or a blob fails its checksum.</exception>
    /// <exception cref="NotSupportedException">The image's version, required features, or checksum algorithm are unsupported, the image is a graph view, or the host is big-endian.</exception>
    public static ColumnarTripleIndex Read(SegmentImageSource source, MemoryPool<EncodedTriple> deltaPool, ResolveChecksumAlgorithmDelegate? resolveChecksum = null, ColumnPayloadBacking? backing = null, ColumnarKernelBackend? backend = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(deltaPool);

        return ColumnarTripleIndex.ReadFrom(source.Image, deltaPool, resolveChecksum, backing, backend);
    }

    /// <summary>Reads a columnar index image from an open file handle, warm — selecting a memory-mapped or pooled-buffer source from <paramref name="mode"/>, then decoding through the verified path so each blob is checksum-verified. Host-only: a browser runtime reads bytes through the <see cref="ReadOnlySequence{T}"/> overload instead.</summary>
    /// <param name="handle">The open, readable file handle positioned at a columnar index image; left open (the caller keeps ownership).</param>
    /// <param name="mode">Whether to memory-map the file or read it through the pool; <see cref="ColumnAccessMode.Auto"/> and <see cref="ColumnAccessMode.MemoryMapped"/> memory-map, <see cref="ColumnAccessMode.Streamed"/> reads through the pool.</param>
    /// <param name="imagePool">The pool a streamed read rents the image buffer from; unused for a memory-mapped read.</param>
    /// <param name="deltaPool">The pool the transient delta triples are rented from while they are re-attached.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id on read; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <param name="backing">Where reloaded payloads live; <see langword="null"/> honors the persisted backing.</param>
    /// <param name="backend">The kernel bundle to decode with; <see langword="null"/> uses <see cref="ColumnarKernelBackend.Default"/>.</param>
    /// <returns>The reconstructed index.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The file is empty, exceeds the reader's size limit, is not a Veritas columnar index image, its directory is malformed, or a blob fails its checksum.</exception>
    /// <exception cref="NotSupportedException">The image's version, required features, or checksum algorithm are unsupported, the image is a graph view, or the host is big-endian.</exception>
    [UnsupportedOSPlatform("browser")]
    public static ColumnarTripleIndex Read(SafeFileHandle handle, ColumnAccessMode mode, MemoryPool<byte> imagePool, MemoryPool<EncodedTriple> deltaPool, ResolveChecksumAlgorithmDelegate? resolveChecksum = null, ColumnPayloadBacking? backing = null, ColumnarKernelBackend? backend = null)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(imagePool);
        ArgumentNullException.ThrowIfNull(deltaPool);

        long length = RandomAccess.GetLength(handle);
        if(length == 0)
        {
            throw new InvalidDataException("The file is empty and cannot be a Veritas columnar index image.");
        }

        if(length > int.MaxValue)
        {
            throw new InvalidDataException("The columnar index image exceeds the in-memory reader's size limit.");
        }

        using SegmentImageSource source = mode == ColumnAccessMode.Streamed
            ? OpenStreamed(handle, (int)length, imagePool)
            : MemoryMappedSegmentImageSource.Open(handle, length);

        return Read(source, deltaPool, resolveChecksum, backing, backend);
    }

    /// <summary>Reads the whole image from the file through a pooled buffer with positional reads.</summary>
    /// <param name="handle">The open file handle.</param>
    /// <param name="length">The image length in bytes.</param>
    /// <param name="imagePool">The pool the image buffer is rented from.</param>
    /// <returns>A pooled image source over the read bytes.</returns>
    private static PooledSegmentImageSource OpenStreamed(SafeFileHandle handle, int length, MemoryPool<byte> imagePool)
    {
        IMemoryOwner<byte> owner = imagePool.Rent(length);
        try
        {
            FillFromFile(handle, owner.Memory.Span[..length]);
        }
        catch
        {
            owner.Dispose();
            throw;
        }

        return new PooledSegmentImageSource(owner, length);
    }

    /// <summary>Reads exactly <paramref name="destination"/>.Length bytes from the start of the file, looping over partial positional reads.</summary>
    /// <param name="handle">The open file handle.</param>
    /// <param name="destination">The buffer to fill.</param>
    /// <exception cref="InvalidDataException">The file ends before the expected number of bytes is read.</exception>
    private static void FillFromFile(SafeFileHandle handle, Span<byte> destination)
    {
        int filled = 0;
        while(filled < destination.Length)
        {
            int read = RandomAccess.Read(handle, destination[filled..], filled);
            if(read == 0)
            {
                throw new InvalidDataException("The columnar index image file ended before the expected number of bytes was read.");
            }

            filled += read;
        }
    }
}
