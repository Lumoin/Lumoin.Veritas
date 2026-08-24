using System;
using System.Collections.Generic;
namespace Lumoin.Veritas.Geo.Dggs;

/// <summary>
/// Dispatch facade over the SIMD rungs of the <see cref="A5PointToCellKernel"/> ladder, walking the
/// capability tiers best-first: AVX-512 → AVX2 → AArch64 NEON → WebAssembly PackedSimd.
/// </summary>
internal static class SimdPointToCellKernel
{
    /// <summary>Inclusive OR over every SIMD rung's own capability check.</summary>
    public static bool IsSupported =>
        Avx512PointToCellKernelBackend.IsSupported
        || Avx2PointToCellKernelBackend.IsSupported
        || NeonPointToCellKernelBackend.IsSupported
        || WasmPackedSimdPointToCellKernelBackend.IsSupported;

    /// <summary>
    /// Returns the highest-capability SIMD batch kernel the host supports, or throws
    /// <see cref="PlatformNotSupportedException"/> when no SIMD rung is available — callers wanting a
    /// guaranteed kernel use <see cref="A5PointToCellKernelSelection.Simd"/>, which falls back to the
    /// scalar reference instead.
    /// </summary>
    public static A5PointToCellKernel GetPointToCell()
    {
        if(Avx512PointToCellKernelBackend.IsSupported)
        {
            return Avx512PointToCellKernelBackend.GetPointToCell();
        }

        if(Avx2PointToCellKernelBackend.IsSupported)
        {
            return Avx2PointToCellKernelBackend.GetPointToCell();
        }

        if(NeonPointToCellKernelBackend.IsSupported)
        {
            return NeonPointToCellKernelBackend.GetPointToCell();
        }

        if(WasmPackedSimdPointToCellKernelBackend.IsSupported)
        {
            return WasmPackedSimdPointToCellKernelBackend.GetPointToCell();
        }

        throw new PlatformNotSupportedException(
            "No SIMD A5 point-to-cell backend is supported on this host. AVX-512F, AVX2, AArch64 NEON, and WebAssembly PackedSimd are the supported sets. Use A5PointToCellKernelSelection.Default (or .Simd) for the scalar reference fallback.");
    }
}
