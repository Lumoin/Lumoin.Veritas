using System;
using System.Buffers.Binary;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// Serialization of an Elias-Fano sequence to and from a little-endian byte image: the low and
/// high payloads plus the scalar parameters. The select samples are NOT stored — they recompute
/// from the high bit-vector on read (a linear scan, far cheaper than re-encoding the values), so
/// the image carries only the succinct payload.
/// </summary>
public sealed partial class EliasFanoSequence
{
    /// <summary>The number of bytes <see cref="WriteTo"/> writes.</summary>
    internal int SerializedSize =>
        sizeof(int)
        + sizeof(int)
        + sizeof(long)
        + sizeof(uint)
        + sizeof(int)
        + LittleEndianBuffer.ArrayBytes<ulong>(lower.Length)
        + LittleEndianBuffer.ArrayBytes<ulong>(upper.Length);

    /// <summary>Writes the sequence's payload and parameters into <paramref name="destination"/> (exactly <see cref="SerializedSize"/> bytes).</summary>
    /// <param name="destination">The buffer to write into.</param>
    internal void WriteTo(Span<byte> destination)
    {
        int offset = 0;
        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], LowBits);
        offset += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], Count);
        offset += sizeof(int);
        BinaryPrimitives.WriteInt64LittleEndian(destination[offset..], upperBits);
        offset += sizeof(long);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], maxHigh);
        offset += sizeof(uint);
        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], selectSampleRate);
        offset += sizeof(int);
        offset += LittleEndianBuffer.WriteArray<ulong>(destination[offset..], lower);
        LittleEndianBuffer.WriteArray<ulong>(destination[offset..], upper);
    }

    /// <summary>Reconstructs a sequence from an image written by <see cref="WriteTo"/>, recomputing the select samples from the high bit-vector.</summary>
    /// <param name="source">The byte image positioned at the sequence.</param>
    /// <param name="laneUnpacker">The bulk lane unpacker retained for <see cref="Decode"/>, or <see langword="null"/>.</param>
    /// <param name="consumed">Receives the bytes consumed.</param>
    /// <returns>The reconstructed sequence.</returns>
    internal static EliasFanoSequence ReadFrom(ReadOnlySpan<byte> source, BitLaneUnpacker? laneUnpacker, out int consumed)
    {
        int offset = 0;
        int lowBits = BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
        offset += sizeof(int);
        int count = BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
        offset += sizeof(int);
        long upperBits = BinaryPrimitives.ReadInt64LittleEndian(source[offset..]);
        offset += sizeof(long);
        uint maxHigh = BinaryPrimitives.ReadUInt32LittleEndian(source[offset..]);
        offset += sizeof(uint);
        int selectSampleRate = BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
        offset += sizeof(int);
        ulong[] lower = LittleEndianBuffer.ReadArray<ulong>(source[offset..], out int read);
        offset += read;
        ulong[] upper = LittleEndianBuffer.ReadArray<ulong>(source[offset..], out read);
        offset += read;

        int upperWords = upper.Length;
        (int[] oneSampleWord, int[] oneSampleOnesBefore) = BuildSamples(upper, upperWords, count, upperBits, selectSampleRate, ones: true);
        (int[] zeroSampleWord, int[] zeroSampleZerosBefore) = BuildSamples(upper, upperWords, (int)(maxHigh + 1), upperBits, selectSampleRate, ones: false);

        consumed = offset;

        return new EliasFanoSequence(lower, upper, oneSampleWord, oneSampleOnesBefore, zeroSampleWord, zeroSampleZerosBefore, upperBits, maxHigh, lowBits, count, selectSampleRate, laneUnpacker);
    }
}
