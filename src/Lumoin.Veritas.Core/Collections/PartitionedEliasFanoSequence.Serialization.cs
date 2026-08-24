using System;
using System.Buffers.Binary;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// Serialization of a partitioned Elias-Fano sequence to and from a little-endian byte image:
/// the shared low and high payloads, the per-segment base and low-bit width, the per-segment
/// bit-offset prefix sums, and the boundaries. The boundaries are stored here so a column
/// round-trips standalone; the container can later share them with the offset column they came
/// from.
/// </summary>
public sealed partial class PartitionedEliasFanoSequence
{
    /// <summary>The number of bytes <see cref="WriteTo"/> writes.</summary>
    internal int SerializedSize =>
        sizeof(int)
        + LittleEndianBuffer.ArrayBytes<ulong>(lower.Length)
        + LittleEndianBuffer.ArrayBytes<ulong>(upper.Length)
        + LittleEndianBuffer.ArrayBytes<uint>(segmentBase.Length)
        + LittleEndianBuffer.ArrayBytes<byte>(segmentLowBits.Length)
        + LittleEndianBuffer.ArrayBytes<long>(segmentLowerStart.Length)
        + LittleEndianBuffer.ArrayBytes<long>(segmentUpperStart.Length)
        + LittleEndianBuffer.ArrayBytes<int>(boundaries.Length);

    /// <summary>Writes the sequence into <paramref name="destination"/> (exactly <see cref="SerializedSize"/> bytes).</summary>
    /// <param name="destination">The buffer to write into.</param>
    internal void WriteTo(Span<byte> destination)
    {
        int offset = 0;
        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], Count);
        offset += sizeof(int);
        offset += LittleEndianBuffer.WriteArray<ulong>(destination[offset..], lower);
        offset += LittleEndianBuffer.WriteArray<ulong>(destination[offset..], upper);
        offset += LittleEndianBuffer.WriteArray<uint>(destination[offset..], segmentBase);
        offset += LittleEndianBuffer.WriteArray<byte>(destination[offset..], segmentLowBits);
        offset += LittleEndianBuffer.WriteArray<long>(destination[offset..], segmentLowerStart);
        offset += LittleEndianBuffer.WriteArray<long>(destination[offset..], segmentUpperStart);
        LittleEndianBuffer.WriteArray<int>(destination[offset..], boundaries);
    }

    /// <summary>Reconstructs a sequence from an image written by <see cref="WriteTo"/>.</summary>
    /// <param name="source">The byte image positioned at the sequence.</param>
    /// <param name="consumed">Receives the bytes consumed.</param>
    /// <returns>The reconstructed sequence.</returns>
    internal static PartitionedEliasFanoSequence ReadFrom(ReadOnlySpan<byte> source, out int consumed)
    {
        int offset = 0;
        int count = BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
        offset += sizeof(int);
        ulong[] lower = LittleEndianBuffer.ReadArray<ulong>(source[offset..], out int read);
        offset += read;
        ulong[] upper = LittleEndianBuffer.ReadArray<ulong>(source[offset..], out read);
        offset += read;
        uint[] segmentBase = LittleEndianBuffer.ReadArray<uint>(source[offset..], out read);
        offset += read;
        byte[] segmentLowBits = LittleEndianBuffer.ReadArray<byte>(source[offset..], out read);
        offset += read;
        long[] segmentLowerStart = LittleEndianBuffer.ReadArray<long>(source[offset..], out read);
        offset += read;
        long[] segmentUpperStart = LittleEndianBuffer.ReadArray<long>(source[offset..], out read);
        offset += read;
        int[] boundaries = LittleEndianBuffer.ReadArray<int>(source[offset..], out read);
        offset += read;

        consumed = offset;

        return new PartitionedEliasFanoSequence(lower, upper, segmentBase, segmentLowBits, segmentLowerStart, segmentUpperStart, boundaries, count);
    }
}
