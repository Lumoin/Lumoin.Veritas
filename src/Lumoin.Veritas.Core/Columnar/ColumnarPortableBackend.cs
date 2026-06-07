using System;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The portable scalar codec backend — always available, the
/// correctness reference every vector backend is differentially
/// tested against. Shift-or lane packing and a three-phase decode
/// (unpack, patch, fused un-zigzag prefix-sum), one lane at a time.
/// </summary>
public static class ColumnarPortableBackend
{
    /// <summary>This backend is supported everywhere.</summary>
    public static bool IsSupported => true;

    /// <summary>The portable backend bundle.</summary>
    public static ColumnarKernelBackend Backend { get; } = new(PackLanes, DecodeBlock, DecodeFrameBlock);

    /// <summary>The portable pack kernel: shift-or into at most two words per lane.</summary>
    /// <param name="values">The zigzag lane values to pack.</param>
    /// <param name="bitWidth">The lane width in bits.</param>
    /// <param name="payload">The zeroed destination words.</param>
    internal static void PackLanes(ReadOnlySpan<uint> values, int bitWidth, Span<ulong> payload)
    {
        if(bitWidth == 0)
        {
            return;
        }

        ulong mask = bitWidth == 32 ? uint.MaxValue : (1UL << bitWidth) - 1;
        long bitOffset = 0;

        for(int i = 0; i < values.Length; i++)
        {
            ulong lane = values[i] & mask;
            int word = (int)(bitOffset >> 6);
            int shift = (int)(bitOffset & 63);

            payload[word] |= lane << shift;
            if(shift + bitWidth > 64)
            {
                payload[word + 1] |= lane >> (64 - shift);
            }

            bitOffset += bitWidth;
        }
    }

    /// <summary>The portable whole-block decode kernel.</summary>
    /// <param name="payload">The packed words.</param>
    /// <param name="bitWidth">The lane width in bits.</param>
    /// <param name="anchor">The block's first decoded value.</param>
    /// <param name="exceptionPositions">The in-block positions of the exception lanes.</param>
    /// <param name="exceptionValues">The exception lanes' full zigzag values.</param>
    /// <param name="destination">Receives the decoded block values.</param>
    internal static void DecodeBlock(
        ReadOnlySpan<ulong> payload,
        int bitWidth,
        uint anchor,
        ReadOnlySpan<ushort> exceptionPositions,
        ReadOnlySpan<uint> exceptionValues,
        Span<uint> destination)
    {
        UnpackLanes(payload, bitWidth, destination);
        PatchExceptions(exceptionPositions, exceptionValues, destination);
        ReconstructFromDeltas(anchor, destination);
    }

    /// <summary>The portable frame-of-reference decode kernel: unpack, then add the frame base lane by lane.</summary>
    /// <param name="payload">The packed words.</param>
    /// <param name="bitWidth">The lane width in bits.</param>
    /// <param name="frameBase">The block's frame minimum.</param>
    /// <param name="destination">Receives the decoded block values.</param>
    internal static void DecodeFrameBlock(
        ReadOnlySpan<ulong> payload,
        int bitWidth,
        uint frameBase,
        Span<uint> destination)
    {
        UnpackLanes(payload, bitWidth, destination);
        for(int i = 0; i < destination.Length; i++)
        {
            destination[i] = unchecked(destination[i] + frameBase);
        }
    }

    /// <summary>Unpacks consecutive bit lanes; the inverse of <see cref="PackLanes"/>.</summary>
    /// <param name="payload">The packed words.</param>
    /// <param name="bitWidth">The lane width in bits.</param>
    /// <param name="destination">Receives the unpacked lane values.</param>
    internal static void UnpackLanes(ReadOnlySpan<ulong> payload, int bitWidth, Span<uint> destination)
    {
        if(bitWidth == 0)
        {
            destination.Clear();

            return;
        }

        ulong mask = bitWidth == 32 ? uint.MaxValue : (1UL << bitWidth) - 1;
        long bitOffset = 0;

        for(int i = 0; i < destination.Length; i++)
        {
            int word = (int)(bitOffset >> 6);
            int shift = (int)(bitOffset & 63);

            ulong lane = payload[word] >> shift;
            if(shift + bitWidth > 64)
            {
                lane |= payload[word + 1] << (64 - shift);
            }

            destination[i] = (uint)(lane & mask);
            bitOffset += bitWidth;
        }
    }

    /// <summary>Overwrites the exception lanes with their full zigzag values; the packed low bits at those positions are garbage by contract.</summary>
    /// <param name="exceptionPositions">The in-block positions of the exception lanes.</param>
    /// <param name="exceptionValues">The exception lanes' full zigzag values.</param>
    /// <param name="destination">The unpacked lanes to patch.</param>
    internal static void PatchExceptions(
        ReadOnlySpan<ushort> exceptionPositions,
        ReadOnlySpan<uint> exceptionValues,
        Span<uint> destination)
    {
        for(int e = 0; e < exceptionPositions.Length; e++)
        {
            destination[exceptionPositions[e]] = exceptionValues[e];
        }
    }

    /// <summary>Un-zigzags the lanes and prefix-sums from the anchor in wrapping 32-bit arithmetic, in place.</summary>
    /// <param name="anchor">The block's first decoded value.</param>
    /// <param name="destination">The patched zigzag lanes; becomes the decoded values.</param>
    internal static void ReconstructFromDeltas(uint anchor, Span<uint> destination)
    {
        uint running = anchor;
        destination[0] = running;
        for(int i = 1; i < destination.Length; i++)
        {
            uint zigzag = destination[i];
            int delta = (int)((zigzag >> 1) ^ (uint)-(int)(zigzag & 1));
            running = unchecked(running + (uint)delta);
            destination[i] = running;
        }
    }
}
