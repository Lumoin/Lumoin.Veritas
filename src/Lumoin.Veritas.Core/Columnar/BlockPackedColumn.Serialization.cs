using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Serialization of a block-packed column to and from a flat little-endian byte image — the
/// column codec the persistence container's blobs use. The block-packed modes
/// (<see cref="BlockPackedColumnMode.PrefixedDeltas"/> / <see cref="BlockPackedColumnMode.FrameOfReference"/>)
/// write their payload words and parallel block metadata; the Elias-Fano modes are not yet
/// serialized (a later increment). Reading wraps the payload bytes in a <see cref="ColumnSource"/>,
/// so a built column reloads without re-sorting or re-packing.
/// </summary>
public sealed partial class BlockPackedColumn
{
    /// <summary>The number of bytes <see cref="WriteTo"/> writes for this column.</summary>
    /// <exception cref="NotSupportedException">The column is Elias-Fano or partitioned-Elias-Fano encoded.</exception>
    internal int SerializedSize
    {
        get
        {
            EnsureBlockPacked();

            return sizeof(byte)
                + sizeof(int)
                + ArrayBytes(payloadStarts.Length, sizeof(int))
                + ArrayBytes(anchors.Length, sizeof(uint))
                + ArrayBytes(frameBases.Length, sizeof(uint))
                + ArrayBytes(widths.Length, sizeof(byte))
                + ArrayBytes(exceptionStarts.Length, sizeof(int))
                + ArrayBytes(exceptionPositions.Length, sizeof(ushort))
                + ArrayBytes(exceptionValues.Length, sizeof(uint))
                + ArrayBytes(Payload.Length, sizeof(ulong));
        }
    }

    /// <summary>Writes this column's mode, length, payload words, and parallel block metadata into <paramref name="destination"/> (exactly <see cref="SerializedSize"/> bytes), little-endian.</summary>
    /// <param name="destination">The buffer to write into; at least <see cref="SerializedSize"/> long.</param>
    /// <exception cref="NotSupportedException">The column is Elias-Fano encoded, or the host is big-endian.</exception>
    internal void WriteTo(Span<byte> destination)
    {
        EnsureLittleEndian();
        EnsureBlockPacked();

        int offset = 0;
        destination[offset] = (byte)Mode;
        offset += sizeof(byte);

        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], Length);
        offset += sizeof(int);

        offset += WriteArray<int>(destination[offset..], payloadStarts);
        offset += WriteArray<uint>(destination[offset..], anchors);
        offset += WriteArray<uint>(destination[offset..], frameBases);
        offset += WriteArray<byte>(destination[offset..], widths);
        offset += WriteArray<int>(destination[offset..], exceptionStarts);
        offset += WriteArray<ushort>(destination[offset..], exceptionPositions);
        offset += WriteArray<uint>(destination[offset..], exceptionValues);
        WriteArray<ulong>(destination[offset..], Payload.Span);
    }

    /// <summary>Reconstructs a block-packed column from an image written by <see cref="WriteTo"/>.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="backing">Where the reconstructed payload words live; default managed.</param>
    /// <param name="backendOption">The kernel bundle to decode with; <see langword="null"/> uses <see cref="ColumnarKernelBackend.Default"/>.</param>
    /// <returns>The reconstructed column.</returns>
    /// <exception cref="NotSupportedException">The image's encoding is Elias-Fano or partitioned-Elias-Fano, or the host is big-endian.</exception>
    internal static BlockPackedColumn ReadFrom(ReadOnlySpan<byte> source, ColumnPayloadBacking backing = ColumnPayloadBacking.Managed, ColumnarKernelBackend? backendOption = null)
    {
        EnsureLittleEndian();

        int offset = 0;
        BlockPackedColumnMode mode = (BlockPackedColumnMode)source[offset];
        offset += sizeof(byte);

        if(mode is not (BlockPackedColumnMode.PrefixedDeltas or BlockPackedColumnMode.FrameOfReference))
        {
            throw new NotSupportedException("Only block-packed columns are serialized; Elias-Fano column serialization is a later increment.");
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
        offset += sizeof(int);

        int[] payloadStarts = ReadArray<int>(source[offset..], out int consumed);
        offset += consumed;
        uint[] anchors = ReadArray<uint>(source[offset..], out consumed);
        offset += consumed;
        uint[] frameBases = ReadArray<uint>(source[offset..], out consumed);
        offset += consumed;
        byte[] widths = ReadArray<byte>(source[offset..], out consumed);
        offset += consumed;
        int[] exceptionStarts = ReadArray<int>(source[offset..], out consumed);
        offset += consumed;
        ushort[] exceptionPositions = ReadArray<ushort>(source[offset..], out consumed);
        offset += consumed;
        uint[] exceptionValues = ReadArray<uint>(source[offset..], out consumed);
        offset += consumed;
        ulong[] payload = ReadArray<ulong>(source[offset..], out consumed);

        ColumnarKernelBackend backend = backendOption ?? ColumnarKernelBackend.Default;
        ColumnSource payloadSource = backing == ColumnPayloadBacking.NativeAligned
            ? InMemoryColumnSource.CreateNative(payload)
            : new InMemoryColumnSource(payload);

        return new BlockPackedColumn(backend, mode, length, payloadSource, payloadStarts, anchors, frameBases, widths, exceptionStarts, exceptionPositions, exceptionValues);
    }

    /// <summary>The serialized size of a length-prefixed primitive array.</summary>
    /// <param name="count">The element count.</param>
    /// <param name="elementSize">The element size in bytes.</param>
    /// <returns>The byte count: the length prefix plus the elements.</returns>
    private static int ArrayBytes(int count, int elementSize)
    {
        return sizeof(int) + (count * elementSize);
    }

    /// <summary>Writes a length-prefixed primitive array little-endian; returns the bytes written.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="destination">The buffer to write into.</param>
    /// <param name="values">The array to write.</param>
    /// <returns>The bytes written.</returns>
    private static int WriteArray<T>(Span<byte> destination, ReadOnlySpan<T> values) where T : unmanaged
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, values.Length);
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(values);
        bytes.CopyTo(destination[sizeof(int)..]);

        return sizeof(int) + bytes.Length;
    }

    /// <summary>Reads a length-prefixed primitive array; sets <paramref name="consumed"/> to the bytes read.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="source">The byte image positioned at the array.</param>
    /// <param name="consumed">Receives the bytes consumed.</param>
    /// <returns>The read array.</returns>
    private static T[] ReadArray<T>(ReadOnlySpan<byte> source, out int consumed) where T : unmanaged
    {
        int count = BinaryPrimitives.ReadInt32LittleEndian(source);
        T[] result = new T[count];
        int byteCount = count * Unsafe.SizeOf<T>();
        source.Slice(sizeof(int), byteCount).CopyTo(MemoryMarshal.AsBytes(result.AsSpan()));
        consumed = sizeof(int) + byteCount;

        return result;
    }

    /// <summary>Throws when the column holds no block payload (the Elias-Fano modes).</summary>
    /// <exception cref="NotSupportedException">The column is Elias-Fano or partitioned-Elias-Fano encoded.</exception>
    private void EnsureBlockPacked()
    {
        if(Mode is not (BlockPackedColumnMode.PrefixedDeltas or BlockPackedColumnMode.FrameOfReference))
        {
            throw new NotSupportedException("Only block-packed columns are serialized; Elias-Fano column serialization is a later increment.");
        }
    }

    /// <summary>Throws on a big-endian host — the column image is little-endian.</summary>
    /// <exception cref="NotSupportedException">The host is big-endian.</exception>
    private static void EnsureLittleEndian()
    {
        if(!BitConverter.IsLittleEndian)
        {
            throw new NotSupportedException("The column byte image is little-endian; big-endian hosts are not supported.");
        }
    }
}
