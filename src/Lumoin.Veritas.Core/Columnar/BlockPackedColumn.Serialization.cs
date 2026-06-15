using System;
using System.Buffers.Binary;
using Lumoin.Veritas.Core.Collections;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Serialization of a column to and from a flat little-endian byte image — the column codec the
/// persistence container's blobs use. A one-byte mode tag leads, then the encoding-specific body:
/// the block-packed modes (<see cref="BlockPackedColumnMode.PrefixedDeltas"/> /
/// <see cref="BlockPackedColumnMode.FrameOfReference"/>) write their length, payload words, and
/// parallel block metadata; the Elias-Fano modes delegate to the succinct sequence's own codec.
/// Reading wraps the payload bytes in a <see cref="ColumnSource"/> (or recomputes the succinct
/// select samples), so a built column reloads without re-sorting or re-packing.
/// </summary>
public sealed partial class BlockPackedColumn
{
    /// <summary>The number of bytes <see cref="WriteTo"/> writes for this column.</summary>
    internal int SerializedSize => Mode switch
    {
        BlockPackedColumnMode.EliasFano => sizeof(byte) + eliasFano!.SerializedSize,
        BlockPackedColumnMode.PartitionedEliasFano => sizeof(byte) + partitionedEliasFano!.SerializedSize,
        _ => sizeof(byte) + BlockPackedSize(),
    };

    /// <summary>The serialized body size of a block-packed column: its length, payload words, and parallel block metadata.</summary>
    /// <returns>The byte count, excluding the leading mode tag.</returns>
    private int BlockPackedSize()
    {
        return sizeof(int)
            + LittleEndianBuffer.ArrayBytes<int>(payloadStarts.Length)
            + LittleEndianBuffer.ArrayBytes<uint>(anchors.Length)
            + LittleEndianBuffer.ArrayBytes<uint>(frameBases.Length)
            + LittleEndianBuffer.ArrayBytes<byte>(widths.Length)
            + LittleEndianBuffer.ArrayBytes<int>(exceptionStarts.Length)
            + LittleEndianBuffer.ArrayBytes<ushort>(exceptionPositions.Length)
            + LittleEndianBuffer.ArrayBytes<uint>(exceptionValues.Length)
            + LittleEndianBuffer.ArrayBytes<ulong>(Payload.Length);
    }

    /// <summary>Writes this column's mode tag and encoding-specific body into <paramref name="destination"/> (exactly <see cref="SerializedSize"/> bytes), little-endian.</summary>
    /// <param name="destination">The buffer to write into; at least <see cref="SerializedSize"/> long.</param>
    /// <exception cref="NotSupportedException">The host is big-endian.</exception>
    internal void WriteTo(Span<byte> destination)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        destination[0] = (byte)Mode;
        Span<byte> body = destination[sizeof(byte)..];

        switch(Mode)
        {
            case BlockPackedColumnMode.EliasFano:
                eliasFano!.WriteTo(body);
                break;
            case BlockPackedColumnMode.PartitionedEliasFano:
                partitionedEliasFano!.WriteTo(body);
                break;
            default:
                WriteBlockPacked(body);
                break;
        }
    }

    /// <summary>Writes the block-packed body — length, payload words, and parallel block metadata — into <paramref name="destination"/>.</summary>
    /// <param name="destination">The buffer to write the body into.</param>
    private void WriteBlockPacked(Span<byte> destination)
    {
        int offset = 0;
        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], Length);
        offset += sizeof(int);

        offset += LittleEndianBuffer.WriteArray<int>(destination[offset..], payloadStarts);
        offset += LittleEndianBuffer.WriteArray<uint>(destination[offset..], anchors);
        offset += LittleEndianBuffer.WriteArray<uint>(destination[offset..], frameBases);
        offset += LittleEndianBuffer.WriteArray<byte>(destination[offset..], widths);
        offset += LittleEndianBuffer.WriteArray<int>(destination[offset..], exceptionStarts);
        offset += LittleEndianBuffer.WriteArray<ushort>(destination[offset..], exceptionPositions);
        offset += LittleEndianBuffer.WriteArray<uint>(destination[offset..], exceptionValues);
        LittleEndianBuffer.WriteArray<ulong>(destination[offset..], Payload.Span);
    }

    /// <summary>Reconstructs a column from an image written by <see cref="WriteTo"/>.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="backing">Where a reconstructed block-packed payload lives; default managed (ignored by the succinct modes).</param>
    /// <param name="backendOption">The kernel bundle to decode with; <see langword="null"/> uses <see cref="ColumnarKernelBackend.Default"/>.</param>
    /// <returns>The reconstructed column.</returns>
    /// <exception cref="NotSupportedException">The host is big-endian.</exception>
    internal static BlockPackedColumn ReadFrom(ReadOnlySpan<byte> source, ColumnPayloadBacking backing = ColumnPayloadBacking.Managed, ColumnarKernelBackend? backendOption = null)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        ColumnarKernelBackend backend = backendOption ?? ColumnarKernelBackend.Default;
        BlockPackedColumnMode mode = (BlockPackedColumnMode)source[0];
        ReadOnlySpan<byte> body = source[sizeof(byte)..];

        switch(mode)
        {
            case BlockPackedColumnMode.EliasFano:
                EliasFanoSequence eliasFano = EliasFanoSequence.ReadFrom(body, backend.DecodeFrame.Invoke, out _);

                return new BlockPackedColumn(backend, eliasFano.Count, eliasFano);
            case BlockPackedColumnMode.PartitionedEliasFano:
                PartitionedEliasFanoSequence partitionedEliasFano = PartitionedEliasFanoSequence.ReadFrom(body, out _);

                return new BlockPackedColumn(backend, partitionedEliasFano.Count, partitionedEliasFano);
            default:
                return ReadBlockPacked(body, mode, backing, backend);
        }
    }

    /// <summary>Reconstructs a block-packed column from its body — length, payload words, and parallel block metadata.</summary>
    /// <param name="source">The byte image positioned past the mode tag.</param>
    /// <param name="mode">The block-packed mode.</param>
    /// <param name="backing">Where the reconstructed payload words live.</param>
    /// <param name="backend">The kernel bundle to decode with.</param>
    /// <returns>The reconstructed column.</returns>
    private static BlockPackedColumn ReadBlockPacked(ReadOnlySpan<byte> source, BlockPackedColumnMode mode, ColumnPayloadBacking backing, ColumnarKernelBackend backend)
    {
        int offset = 0;
        int length = BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
        offset += sizeof(int);

        int[] payloadStarts = LittleEndianBuffer.ReadArray<int>(source[offset..], out int consumed);
        offset += consumed;
        uint[] anchors = LittleEndianBuffer.ReadArray<uint>(source[offset..], out consumed);
        offset += consumed;
        uint[] frameBases = LittleEndianBuffer.ReadArray<uint>(source[offset..], out consumed);
        offset += consumed;
        byte[] widths = LittleEndianBuffer.ReadArray<byte>(source[offset..], out consumed);
        offset += consumed;
        int[] exceptionStarts = LittleEndianBuffer.ReadArray<int>(source[offset..], out consumed);
        offset += consumed;
        ushort[] exceptionPositions = LittleEndianBuffer.ReadArray<ushort>(source[offset..], out consumed);
        offset += consumed;
        uint[] exceptionValues = LittleEndianBuffer.ReadArray<uint>(source[offset..], out consumed);
        offset += consumed;
        ulong[] payload = LittleEndianBuffer.ReadArray<ulong>(source[offset..], out consumed);

        ColumnSource payloadSource = backing == ColumnPayloadBacking.NativeAligned
            ? InMemoryColumnSource.CreateNative(payload)
            : new InMemoryColumnSource(payload);

        return new BlockPackedColumn(backend, mode, length, payloadSource, payloadStarts, anchors, frameBases, widths, exceptionStarts, exceptionPositions, exceptionValues);
    }
}
