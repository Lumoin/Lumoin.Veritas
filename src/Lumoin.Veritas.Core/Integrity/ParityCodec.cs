using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The capacity-1 single-parity erasure code over equal-stride block payloads — the detection-complement of
/// <see cref="ChecksumAlgorithm"/>: a checksum names which block is lost, and this code restores it. One parity
/// block is the byte-wise XOR of every data block, each implicitly zero-extended to the parity stride (so a
/// shorter final block contributes only its leading bytes). Because XOR is self-inverse, the one lost block of a
/// set is exactly the parity XORed with all the surviving blocks; a single erasure is recovered, and the
/// recovered bytes beyond the lost block's true length fall out as zero. This is the restoring source the
/// local-parity repair rung peels: it recovers a lost system-of-record block rather than only naming the loss.
/// </summary>
/// <remarks>
/// <para>
/// The fold is byte-wise XOR and therefore endian-agnostic — the word- and vector-width paths read and write the
/// same machine byte order, so the per-byte result is identical on either endianness. The portable word-parallel
/// path and the Vector128, Vector256, and Vector512 paths all compute the same accumulation; each is exposed so
/// the differential oracle proves they agree, per the keep-measured-alternatives discipline, and the public entry
/// dispatches to the widest hardware-accelerated path. The wider paths are built even where this host cannot
/// accelerate them — their <see cref="Vector256.IsHardwareAccelerated"/>-style guards gate the dispatch, and a
/// host that supports them exercises the fast path while the others verify correctness by software emulation.
/// </para>
/// <para>
/// The code is capacity-1: it produces one parity block and recovers one lost block. Recovering more than one
/// lost block from a single parity block is not possible and is the caller's responsibility to detect before
/// calling <see cref="Restore"/> (the repair ladder descends past the local-parity rung when more blocks are lost
/// than the parity's capacity).
/// </para>
/// </remarks>
public static class ParityCodec
{
    /// <summary>The byte width of one word in the portable word-parallel path.</summary>
    private const int WordByteWidth = sizeof(ulong);

    /// <summary>Encodes the capacity-1 parity block: <paramref name="parity"/> becomes the byte-wise XOR of every block in <paramref name="blocks"/>, each implicitly zero-extended to <paramref name="parity"/>'s length (the stride). No block may be longer than the stride.</summary>
    /// <param name="blocks">The data block payloads to protect; each at most <paramref name="parity"/>'s length, a shorter block contributing only its leading bytes.</param>
    /// <param name="parity">The destination parity block, exactly the stride wide; it is fully overwritten.</param>
    /// <exception cref="ArgumentOutOfRangeException">A block is longer than <paramref name="parity"/>.</exception>
    public static void Encode(ReadOnlySpan<ReadOnlyMemory<byte>> blocks, Span<byte> parity)
    {
        parity.Clear();
        for(int i = 0; i < blocks.Length; i++)
        {
            AccumulateXor(parity, blocks[i].Span);
        }
    }

    /// <summary>Restores the one lost block from the parity and the surviving blocks: <paramref name="restored"/> becomes <paramref name="parity"/> XORed with every block in <paramref name="survivingBlocks"/>, which is the lost block zero-extended to the stride. The lost block's true payload is the leading bytes of <paramref name="restored"/>; the bytes beyond it are zero.</summary>
    /// <param name="parity">The parity block written by <see cref="Encode"/>, exactly the stride wide.</param>
    /// <param name="survivingBlocks">Every block of the original set except the one lost block; each at most the stride wide.</param>
    /// <param name="restored">The destination for the recovered block, exactly the stride wide (the same length as <paramref name="parity"/>); it is fully overwritten.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="restored"/> is not the same length as <paramref name="parity"/>, or a surviving block is longer than the stride.</exception>
    public static void Restore(ReadOnlySpan<byte> parity, ReadOnlySpan<ReadOnlyMemory<byte>> survivingBlocks, Span<byte> restored)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(restored.Length, parity.Length);
        parity.CopyTo(restored);
        for(int i = 0; i < survivingBlocks.Length; i++)
        {
            AccumulateXor(restored, survivingBlocks[i].Span);
        }
    }

    /// <summary>Folds one block payload into the accumulator by byte-wise XOR over <paramref name="blockPayload"/>'s leading bytes — the step shared by <see cref="Encode"/> and <see cref="Restore"/>. The accumulator's bytes beyond <paramref name="blockPayload"/>'s length are the block's implicit zero padding and are left unchanged. Dispatches to the widest hardware-accelerated path.</summary>
    /// <param name="accumulator">The accumulator to XOR into; at least as long as <paramref name="blockPayload"/>.</param>
    /// <param name="blockPayload">The block payload to fold in.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockPayload"/> is longer than <paramref name="accumulator"/>.</exception>
    public static void AccumulateXor(Span<byte> accumulator, ReadOnlySpan<byte> blockPayload)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(blockPayload.Length, accumulator.Length);
        if(Vector512.IsHardwareAccelerated)
        {
            AccumulateXorVector512(accumulator, blockPayload);
        }
        else if(Vector256.IsHardwareAccelerated)
        {
            AccumulateXorVector256(accumulator, blockPayload);
        }
        else if(Vector128.IsHardwareAccelerated)
        {
            AccumulateXorVector128(accumulator, blockPayload);
        }
        else
        {
            AccumulateXorPortable(accumulator, blockPayload);
        }
    }

    /// <summary>The word-parallel fallback: XOR <paramref name="blockPayload"/> into <paramref name="accumulator"/> a 64-bit word at a time, then the trailing bytes. Native word order cancels in the XOR, so the per-byte result is endian-agnostic.</summary>
    /// <param name="accumulator">The accumulator to XOR into; at least as long as <paramref name="blockPayload"/>.</param>
    /// <param name="blockPayload">The block payload to fold in.</param>
    internal static void AccumulateXorPortable(Span<byte> accumulator, ReadOnlySpan<byte> blockPayload)
    {
        int length = blockPayload.Length;
        int wordCount = length / WordByteWidth;
        int wordBytes = wordCount * WordByteWidth;
        Span<ulong> accumulatorWords = MemoryMarshal.Cast<byte, ulong>(accumulator[..wordBytes]);
        ReadOnlySpan<ulong> blockWords = MemoryMarshal.Cast<byte, ulong>(blockPayload[..wordBytes]);
        for(int w = 0; w < wordCount; w++)
        {
            accumulatorWords[w] ^= blockWords[w];
        }

        for(int b = wordBytes; b < length; b++)
        {
            accumulator[b] ^= blockPayload[b];
        }
    }

    /// <summary>The 128-bit-vector path: XOR a <see cref="Vector128{T}"/> of bytes at a time, then the sub-vector tail by the portable path.</summary>
    /// <param name="accumulator">The accumulator to XOR into; at least as long as <paramref name="blockPayload"/>.</param>
    /// <param name="blockPayload">The block payload to fold in.</param>
    internal static void AccumulateXorVector128(Span<byte> accumulator, ReadOnlySpan<byte> blockPayload)
    {
        int length = blockPayload.Length;
        int i = 0;
        for(; i + Vector128<byte>.Count <= length; i += Vector128<byte>.Count)
        {
            Vector128<byte> folded = Vector128.Create(accumulator.Slice(i, Vector128<byte>.Count)) ^ Vector128.Create(blockPayload.Slice(i, Vector128<byte>.Count));
            folded.CopyTo(accumulator.Slice(i, Vector128<byte>.Count));
        }

        AccumulateXorPortable(accumulator[i..length], blockPayload[i..]);
    }

    /// <summary>The 256-bit-vector path: XOR a <see cref="Vector256{T}"/> of bytes at a time, then the sub-vector tail by the portable path.</summary>
    /// <param name="accumulator">The accumulator to XOR into; at least as long as <paramref name="blockPayload"/>.</param>
    /// <param name="blockPayload">The block payload to fold in.</param>
    internal static void AccumulateXorVector256(Span<byte> accumulator, ReadOnlySpan<byte> blockPayload)
    {
        int length = blockPayload.Length;
        int i = 0;
        for(; i + Vector256<byte>.Count <= length; i += Vector256<byte>.Count)
        {
            Vector256<byte> folded = Vector256.Create(accumulator.Slice(i, Vector256<byte>.Count)) ^ Vector256.Create(blockPayload.Slice(i, Vector256<byte>.Count));
            folded.CopyTo(accumulator.Slice(i, Vector256<byte>.Count));
        }

        AccumulateXorPortable(accumulator[i..length], blockPayload[i..]);
    }

    /// <summary>The 512-bit-vector path: XOR a <see cref="Vector512{T}"/> of bytes at a time, then the sub-vector tail by the portable path.</summary>
    /// <param name="accumulator">The accumulator to XOR into; at least as long as <paramref name="blockPayload"/>.</param>
    /// <param name="blockPayload">The block payload to fold in.</param>
    internal static void AccumulateXorVector512(Span<byte> accumulator, ReadOnlySpan<byte> blockPayload)
    {
        int length = blockPayload.Length;
        int i = 0;
        for(; i + Vector512<byte>.Count <= length; i += Vector512<byte>.Count)
        {
            Vector512<byte> folded = Vector512.Create(accumulator.Slice(i, Vector512<byte>.Count)) ^ Vector512.Create(blockPayload.Slice(i, Vector512<byte>.Count));
            folded.CopyTo(accumulator.Slice(i, Vector512<byte>.Count));
        }

        AccumulateXorPortable(accumulator[i..length], blockPayload[i..]);
    }
}
