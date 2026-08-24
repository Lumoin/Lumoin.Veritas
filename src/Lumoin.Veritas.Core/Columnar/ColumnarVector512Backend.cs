using System;
using System.Runtime.Intrinsics;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The 512-bit vector codec backend (AVX-512-class hardware): the decode's un-zigzag and prefix-sum phases run
/// sixteen lanes at a time with a Hillis–Steele in-register scan, and the frame-of-reference add broadcasts the
/// frame base sixteen lanes at a time; lane unpacking and exception patching stay on the portable kernels, as in
/// the 256-bit backend. Width-specialised vector unpacking is the recorded follow-up, gated by the soak ladder.
/// </summary>
/// <remarks>
/// <see cref="Vector512{T}"/> operations are correct everywhere — the runtime emulates them in narrower
/// arithmetic where 512-bit hardware is absent — so this backend produces the same bytes as
/// <see cref="ColumnarPortableBackend"/> on every machine; it is only <em>selected</em>
/// (<see cref="ColumnarKernelBackendSelection"/>) where it runs natively. The cross-backend differential test
/// pins it to the portable reference regardless of hardware via <see cref="BackendUnchecked"/>.
/// </remarks>
public static class ColumnarVector512Backend
{
    /// <summary>Whether 512-bit vector acceleration is available on this machine.</summary>
    public static bool IsSupported => Vector512.IsHardwareAccelerated;

    private static ColumnarKernelBackend Cached { get; } = new(ColumnarPortableBackend.PackLanes, DecodeBlock, DecodeFrameBlock);

    //Lane-shift permutations and their shifted-in-zero masks for the in-register scan: step k adds, to every lane
    //i >= k, the value of lane i - k. Sixteen lanes need four steps: 1, 2, 4, 8.
    private static Vector512<uint> ShiftOneIndices { get; } = Vector512.Create(0u, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14);

    private static Vector512<uint> ShiftOneMask { get; } = Vector512.Create(0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u);

    private static Vector512<uint> ShiftTwoIndices { get; } = Vector512.Create(0u, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);

    private static Vector512<uint> ShiftTwoMask { get; } = Vector512.Create(0u, 0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u);

    private static Vector512<uint> ShiftFourIndices { get; } = Vector512.Create(0u, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);

    private static Vector512<uint> ShiftFourMask { get; } = Vector512.Create(0u, 0u, 0u, 0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u);

    private static Vector512<uint> ShiftEightIndices { get; } = Vector512.Create(0u, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7);

    private static Vector512<uint> ShiftEightMask { get; } = Vector512.Create(0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u, ~0u);

    /// <summary>The 512-bit backend bundle.</summary>
    /// <exception cref="PlatformNotSupportedException">512-bit vectors are not accelerated here.</exception>
    public static ColumnarKernelBackend Backend =>
        IsSupported ? Cached : throw new PlatformNotSupportedException("512-bit vector acceleration is not available on this machine.");

    /// <summary>The bundle regardless of hardware support — for the cross-backend differential test, which pins every backend (including those the host does not natively accelerate) to the portable reference.</summary>
    internal static ColumnarKernelBackend BackendUnchecked => Cached;

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

        Vector512<uint> baseVector = Vector512.Create(frameBase);
        int i = 0;
        for(; i + Vector512<uint>.Count <= destination.Length; i += Vector512<uint>.Count)
        {
            (Vector512.Create(destination.Slice(i, Vector512<uint>.Count)) + baseVector).CopyTo(destination.Slice(i, Vector512<uint>.Count));
        }

        for(; i < destination.Length; i++)
        {
            destination[i] = unchecked(destination[i] + frameBase);
        }
    }

    /// <summary>
    /// Un-zigzags and prefix-sums the lanes from the anchor, sixteen at a time: an in-register Hillis–Steele scan
    /// per vector (shift steps 1, 2, 4, 8) plus a broadcast carry across vectors, with a scalar tail. All
    /// arithmetic wraps, matching the portable kernel bit for bit.
    /// </summary>
    /// <param name="anchor">The block's first decoded value.</param>
    /// <param name="destination">The patched zigzag lanes; becomes the decoded values.</param>
    private static void ReconstructFromDeltas(uint anchor, Span<uint> destination)
    {
        //The carry entering a vector is the running value of the last lane before it. Lane 0's zigzag is zero by
        //construction, so seeding the carry with the anchor makes the first vector's scan produce the anchor at lane 0.
        uint carry = anchor;
        int i = 0;
        Vector512<uint> one = Vector512.Create(1u);

        for(; i + Vector512<uint>.Count <= destination.Length; i += Vector512<uint>.Count)
        {
            Vector512<uint> zigzag = Vector512.Create(destination.Slice(i, Vector512<uint>.Count));
            Vector512<uint> delta = (zigzag >>> 1) ^ (Vector512<uint>.Zero - (zigzag & one));

            Vector512<uint> scan = delta;
            scan += Vector512.Shuffle(scan, ShiftOneIndices) & ShiftOneMask;
            scan += Vector512.Shuffle(scan, ShiftTwoIndices) & ShiftTwoMask;
            scan += Vector512.Shuffle(scan, ShiftFourIndices) & ShiftFourMask;
            scan += Vector512.Shuffle(scan, ShiftEightIndices) & ShiftEightMask;

            Vector512<uint> values = scan + Vector512.Create(carry);
            values.CopyTo(destination.Slice(i, Vector512<uint>.Count));
            carry = values.GetElement(Vector512<uint>.Count - 1);
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
