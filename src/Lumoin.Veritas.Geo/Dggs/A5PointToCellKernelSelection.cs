using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Geo.Dggs;

/// <summary>
/// Selector over the <see cref="A5PointToCellKernel"/> backends: each property resolves once at first
/// access and is a stable delegate reference for the process lifetime.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Default"/> and <see cref="Scalar"/> both resolve to the scalar reference kernel.
/// <see cref="Default"/> does not silently prefer a faster backend:
/// the scalar kernel is the exact reference every other backend is fixture-gated against, so it is what
/// <see cref="Default"/> stays pinned to even though the SIMD ladder below exists — callers opt into a
/// faster backend explicitly. That deliberate choice, not an oversight, is why both properties are
/// exposed rather than one.
/// </para>
/// <para>
/// The SIMD backends form a hardware-capability ladder: <see cref="Simd"/> resolves once to the highest-capability
/// rung the host supports — AVX-512 → AVX2 → AArch64 NEON → WebAssembly PackedSimd — falling back to
/// the scalar reference when none is, so it never throws. The per-ISA properties (<see cref="Avx512"/>,
/// <see cref="Avx2"/>, <see cref="Neon"/>, <see cref="WasmPackedSimd"/>) pin one rung explicitly and
/// throw <see cref="PlatformNotSupportedException"/> on hosts that lack it —
/// thrown at property access, never from a type initializer. Every backend is
/// bit-identical to <see cref="Scalar"/> by construction and by gate: vector stages use only IEEE-exact
/// operations in the scalar source's association order, transcendentals stay per-lane scalar, and any
/// lane leaving the estimate fast path falls back to the scalar search per point.
/// </para>
/// <para>
/// Measured on x64: the SIMD rungs land at parity with the scalar reference within
/// measurement noise single-threaded and slightly behind it at full parallelism — the per-lane scalar
/// transcendentals dominate the fast path, so the vectorized algebra buys no net speed there. The
/// scalar reference is therefore both <see cref="Default"/> and the recommended batch kernel on x64
/// today; the ladder is kept for the WASM-SIMD/NEON tier and as gated infrastructure.
/// </para>
/// <para>
/// The scalar implementation loops each interleaved longitude/latitude pair and calls
/// <see cref="Cell.LonLatToCell"/> directly — the same internal entry point <see cref="A5.LonLatToCell"/>
/// wraps — skipping the facade's per-point coordinate/resolution sanitation deliberately: this is the hot
/// batch path, and its caller-supplied span is expected to already hold valid data
/// (route through <see cref="A5.LonLatToCell"/> first if that is not guaranteed). Only the span-length
/// contract is checked, since violating it is a caller bug no valid input recovers from.
/// </para>
/// </remarks>
public static class A5PointToCellKernelSelection
{
    /// <summary>
    /// The default kernel — the scalar reference. Chosen as the default because it is the correctness
    /// reference every future backend is fixture-gated against.
    /// </summary>
    public static A5PointToCellKernel Default { get; } = CreateScalarKernel();

    /// <summary>The scalar kernel, by name; identical reference to <see cref="Default"/>.</summary>
    public static A5PointToCellKernel Scalar { get; } = CreateScalarKernel();

    /// <summary>
    /// The highest-capability SIMD rung the host supports (AVX-512 → AVX2 → NEON → WASM PackedSimd),
    /// or the scalar reference when no SIMD rung is available. Resolved once; never throws.
    /// </summary>
    public static A5PointToCellKernel Simd { get; } =
        SimdPointToCellKernel.IsSupported ? SimdPointToCellKernel.GetPointToCell() : CreateScalarKernel();

    /// <summary>
    /// The AVX-512 rung (eight lanes), pinned explicitly. Throws
    /// <see cref="PlatformNotSupportedException"/> on hosts without AVX-512F; the returned delegate
    /// reference is stable for the process lifetime.
    /// </summary>
    public static A5PointToCellKernel Avx512 => Avx512PointToCellKernelBackend.GetPointToCell();

    /// <summary>
    /// The AVX2 rung (four lanes), pinned explicitly. Throws
    /// <see cref="PlatformNotSupportedException"/> on hosts without AVX2; the returned delegate
    /// reference is stable for the process lifetime.
    /// </summary>
    public static A5PointToCellKernel Avx2 => Avx2PointToCellKernelBackend.GetPointToCell();

    /// <summary>
    /// The AArch64 NEON rung (two lanes), pinned explicitly. Throws
    /// <see cref="PlatformNotSupportedException"/> on hosts without AArch64 NEON; the returned delegate
    /// reference is stable for the process lifetime.
    /// </summary>
    public static A5PointToCellKernel Neon => NeonPointToCellKernelBackend.GetPointToCell();

    /// <summary>
    /// The WebAssembly SIMD128 (PackedSimd) rung (two lanes), pinned explicitly. Throws
    /// <see cref="PlatformNotSupportedException"/> on hosts without PackedSimd; the returned delegate
    /// reference is stable for the process lifetime.
    /// </summary>
    public static A5PointToCellKernel WasmPackedSimd => WasmPackedSimdPointToCellKernelBackend.GetPointToCell();

    /// <summary>Builds the scalar kernel delegate once; both <see cref="Default"/> and <see cref="Scalar"/> call this so each resolves independently but to equivalent, stable delegates.</summary>
    private static A5PointToCellKernel CreateScalarKernel()
    {
        return ScalarPointToCell;
    }

    /// <summary>
    /// The scalar reference kernel: validates the span-length contract, then converts each interleaved
    /// longitude/latitude pair to a cell id in order.
    /// </summary>
    private static void ScalarPointToCell(ReadOnlySpan<double> sourceLongitudeLatitude, int resolution, Span<A5CellId> destinationCellIds)
    {
        if(sourceLongitudeLatitude.Length % 2 != 0)
        {
            throw new ArgumentException(
                $"Source span length ({sourceLongitudeLatitude.Length}) must be even — interleaved longitude/latitude pairs.",
                nameof(sourceLongitudeLatitude));
        }

        int pointCount = sourceLongitudeLatitude.Length / 2;
        if(destinationCellIds.Length != pointCount)
        {
            throw new ArgumentException(
                $"Destination length ({destinationCellIds.Length}) must equal source length / 2 ({pointCount}).",
                nameof(destinationCellIds));
        }

        for(int index = 0; index < pointCount; index++)
        {
            double longitude = sourceLongitudeLatitude[2 * index];
            double latitude = sourceLongitudeLatitude[(2 * index) + 1];
            destinationCellIds[index] = new A5CellId(Cell.LonLatToCell(new LonLat(longitude, latitude), resolution));
        }
    }
}
