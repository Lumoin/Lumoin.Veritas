using System;
using System.Runtime.Intrinsics;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The 256-bit vector codec backend (AVX2-class hardware): the
/// decode's un-zigzag and prefix-sum phases run eight lanes at a
/// time with a Hillis–Steele in-register scan; lane unpacking and
/// exception patching stay on the portable kernels. Width-
/// specialised vector unpacking is the recorded follow-up, gated
/// by the soak ladder.
/// </summary>
public static class ColumnarVector256Backend
{
    /// <summary>Whether 256-bit vector acceleration is available on this machine.</summary>
    public static bool IsSupported => Vector256.IsHardwareAccelerated;

    private static ColumnarKernelBackend Cached { get; } = new(ColumnarPortableBackend.PackLanes, DecodeBlock, DecodeFrameBlock);

    //Lane-shift permutations and their shifted-in-zero masks for
    //the in-register scan: step k adds, to every lane i ≥ k, the
    //value of lane i − k.
    private static Vector256<uint> ShiftOneIndices { get; } = Vector256.Create(0u, 0, 1, 2, 3, 4, 5, 6);

    private static Vector256<uint> ShiftOneMask { get; } = Vector256.Create(0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u);

    private static Vector256<uint> ShiftTwoIndices { get; } = Vector256.Create(0u, 0, 0, 1, 2, 3, 4, 5);

    private static Vector256<uint> ShiftTwoMask { get; } = Vector256.Create(0u, 0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u);

    private static Vector256<uint> ShiftFourIndices { get; } = Vector256.Create(0u, 0, 0, 0, 0, 1, 2, 3);

    private static Vector256<uint> ShiftFourMask { get; } = Vector256.Create(0u, 0u, 0u, 0u, ~0u, ~0u, ~0u, ~0u);

    /// <summary>The 256-bit backend bundle.</summary>
    /// <exception cref="PlatformNotSupportedException">256-bit vectors are not accelerated here.</exception>
    public static ColumnarKernelBackend Backend =>
        IsSupported ? Cached : throw new PlatformNotSupportedException("256-bit vector acceleration is not available on this machine.");

    /// <summary>The whole-block decode kernel: portable unpack and patch, vectorised reconstruction.</summary>
    /// <param name="payload">The packed words.</param>
    /// <param name="bitWidth">The lane width in bits.</param>
    /// <param name="anchor">The block's first decoded value.</param>
    /// <param name="exceptionPositions">The in-block positions of the exception lanes.</param>
    /// <param name="exceptionValues">The exception lanes' full zigzag values.</param>
    /// <param name="destination">Receives the decoded block values.</param>
    private static void DecodeBlock(
        ReadOnlySpan<ulong> payload,
        int bitWidth,
        uint anchor,
        ReadOnlySpan<ushort> exceptionPositions,
        ReadOnlySpan<uint> exceptionValues,
        Span<uint> destination)
    {
        ColumnarPortableBackend.UnpackLanes(payload, bitWidth, destination);
        ColumnarPortableBackend.PatchExceptions(exceptionPositions, exceptionValues, destination);
        ReconstructFromDeltas(anchor, destination);
    }

    /// <summary>The frame-of-reference decode kernel: portable unpack, then a vectorised broadcast add of the frame base.</summary>
    /// <param name="payload">The packed words.</param>
    /// <param name="bitWidth">The lane width in bits.</param>
    /// <param name="frameBase">The block's frame minimum.</param>
    /// <param name="destination">Receives the decoded block values.</param>
    private static void DecodeFrameBlock(
        ReadOnlySpan<ulong> payload,
        int bitWidth,
        uint frameBase,
        Span<uint> destination)
    {
        ColumnarPortableBackend.UnpackLanes(payload, bitWidth, destination);

        Vector256<uint> baseVector = Vector256.Create(frameBase);
        int i = 0;
        for(; i + Vector256<uint>.Count <= destination.Length; i += Vector256<uint>.Count)
        {
            (Vector256.Create(destination.Slice(i, Vector256<uint>.Count)) + baseVector).CopyTo(destination.Slice(i, Vector256<uint>.Count));
        }

        for(; i < destination.Length; i++)
        {
            destination[i] = unchecked(destination[i] + frameBase);
        }
    }

    /// <summary>
    /// Un-zigzags and prefix-sums the lanes from the anchor, eight
    /// at a time: an in-register Hillis–Steele scan per vector plus
    /// a broadcast carry across vectors, with a scalar tail. All
    /// arithmetic wraps, matching the portable kernel bit for bit.
    /// </summary>
    /// <param name="anchor">The block's first decoded value.</param>
    /// <param name="destination">The patched zigzag lanes; becomes the decoded values.</param>
    private static void ReconstructFromDeltas(uint anchor, Span<uint> destination)
    {
        //The carry entering a vector is the running value of the
        //last lane before it. Lane 0's zigzag is zero by
        //construction, so seeding the carry with the anchor makes
        //the first vector's scan produce the anchor at lane 0.
        uint carry = anchor;
        int i = 0;
        Vector256<uint> one = Vector256.Create(1u);

        for(; i + Vector256<uint>.Count <= destination.Length; i += Vector256<uint>.Count)
        {
            Vector256<uint> zigzag = Vector256.Create(destination.Slice(i, Vector256<uint>.Count));
            Vector256<uint> delta = (zigzag >>> 1) ^ (Vector256<uint>.Zero - (zigzag & one));

            Vector256<uint> scan = delta;
            scan += Vector256.Shuffle(scan, ShiftOneIndices) & ShiftOneMask;
            scan += Vector256.Shuffle(scan, ShiftTwoIndices) & ShiftTwoMask;
            scan += Vector256.Shuffle(scan, ShiftFourIndices) & ShiftFourMask;

            Vector256<uint> values = scan + Vector256.Create(carry);
            values.CopyTo(destination.Slice(i, Vector256<uint>.Count));
            carry = values.GetElement(Vector256<uint>.Count - 1);
        }

        for(; i < destination.Length; i++)
        {
            uint zigzag = destination[i];
            int delta = (int)((zigzag >> 1) ^ (uint)-(int)(zigzag & 1));
            carry = unchecked(carry + (uint)delta);
            destination[i] = carry;
        }
    }
}
