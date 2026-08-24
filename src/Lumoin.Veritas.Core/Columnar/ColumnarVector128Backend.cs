using System;
using System.Runtime.Intrinsics;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The 128-bit vector codec backend (SSE- and NEON-class hardware):
/// the decode's un-zigzag and prefix-sum phases run four lanes at a
/// time; lane unpacking and exception patching stay on the portable
/// kernels. The same kernels serve
/// <see cref="ColumnarWasmPackedSimdBackend"/> — the portable
/// 128-bit vector API lowers to PackedSimd on WebAssembly hosts.
/// </summary>
public static class ColumnarVector128Backend
{
    /// <summary>Whether 128-bit vector acceleration is available on this machine.</summary>
    public static bool IsSupported => Vector128.IsHardwareAccelerated;

    private static ColumnarKernelBackend Cached { get; } = new(ColumnarPortableBackend.PackLanes, DecodeBlock, DecodeFrameBlock);

    //Lane-shift permutations and their shifted-in-zero masks for
    //the in-register scan.
    private static Vector128<uint> ShiftOneIndices { get; } = Vector128.Create(0u, 0, 1, 2);

    private static Vector128<uint> ShiftOneMask { get; } = Vector128.Create(0u, ~0u, ~0u, ~0u);

    private static Vector128<uint> ShiftTwoIndices { get; } = Vector128.Create(0u, 0, 0, 1);

    private static Vector128<uint> ShiftTwoMask { get; } = Vector128.Create(0u, 0u, ~0u, ~0u);

    /// <summary>The 128-bit backend bundle.</summary>
    /// <exception cref="PlatformNotSupportedException">128-bit vectors are not accelerated here.</exception>
    public static ColumnarKernelBackend Backend =>
        IsSupported ? Cached : throw new PlatformNotSupportedException("128-bit vector acceleration is not available on this machine.");

    /// <summary>The bundle without the acceleration check — for <see cref="ColumnarWasmPackedSimdBackend"/>, whose support gate is PackedSimd's own.</summary>
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

        Vector128<uint> baseVector = Vector128.Create(frameBase);
        int i = 0;
        for(; i + Vector128<uint>.Count <= destination.Length; i += Vector128<uint>.Count)
        {
            (Vector128.Create(destination.Slice(i, Vector128<uint>.Count)) + baseVector).CopyTo(destination.Slice(i, Vector128<uint>.Count));
        }

        for(; i < destination.Length; i++)
        {
            destination[i] = unchecked(destination[i] + frameBase);
        }
    }

    /// <summary>
    /// Un-zigzags and prefix-sums the lanes from the anchor, four
    /// at a time: an in-register Hillis–Steele scan per vector plus
    /// a broadcast carry across vectors, with a scalar tail. All
    /// arithmetic wraps, matching the portable kernel bit for bit.
    /// </summary>
    /// <param name="anchor">The block's first decoded value.</param>
    /// <param name="destination">The patched zigzag lanes; becomes the decoded values.</param>
    private static void ReconstructFromDeltas(uint anchor, Span<uint> destination)
    {
        uint carry = anchor;
        int i = 0;
        Vector128<uint> one = Vector128.Create(1u);

        for(; i + Vector128<uint>.Count <= destination.Length; i += Vector128<uint>.Count)
        {
            Vector128<uint> zigzag = Vector128.Create(destination.Slice(i, Vector128<uint>.Count));
            Vector128<uint> delta = (zigzag >>> 1) ^ (Vector128<uint>.Zero - (zigzag & one));

            Vector128<uint> scan = delta;
            scan += Vector128.Shuffle(scan, ShiftOneIndices) & ShiftOneMask;
            scan += Vector128.Shuffle(scan, ShiftTwoIndices) & ShiftTwoMask;

            Vector128<uint> values = scan + Vector128.Create(carry);
            values.CopyTo(destination.Slice(i, Vector128<uint>.Count));
            carry = values.GetElement(Vector128<uint>.Count - 1);
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
