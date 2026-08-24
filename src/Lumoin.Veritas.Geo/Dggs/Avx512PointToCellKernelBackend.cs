using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Geo.Dggs;

/// <summary>
/// AVX-512 rung of the <see cref="A5PointToCellKernel"/> ladder: eight <see cref="double"/> lanes via
/// <see cref="PointToCellBatchCore"/> at <see cref="PointToCellLanes512"/>. Bit-identical to the scalar
/// reference by the core's construction; gated by the ladder's bit-identity fixtures.
/// </summary>
internal static class Avx512PointToCellKernelBackend
{
    /// <summary>The stable delegate this backend hands out; created once per process.</summary>
    private static A5PointToCellKernel CachedKernel { get; } = PointToCell;

    /// <summary>Whether this host supports the AVX-512 rung.</summary>
    public static bool IsSupported => Avx512F.IsSupported && Vector512.IsHardwareAccelerated;

    /// <summary>
    /// Returns the AVX-512 batch kernel, or throws <see cref="PlatformNotSupportedException"/> when the
    /// host lacks AVX-512 — check <see cref="IsSupported"/> first. The throw happens here, at access, so an
    /// unsupported backend can never poison a selection class's type initializer.
    /// </summary>
    public static A5PointToCellKernel GetPointToCell()
    {
        if(!IsSupported)
        {
            throw new PlatformNotSupportedException(
                "Avx512PointToCellKernelBackend requires AVX-512F with accelerated Vector512; check IsSupported before requesting the delegate.");
        }

        return CachedKernel;
    }

    /// <summary>The delegate body: the shared batch core at eight lanes.</summary>
    private static void PointToCell(ReadOnlySpan<double> sourceLongitudeLatitude, int resolution, Span<A5CellId> destinationCellIds)
    {
        PointToCellBatchCore.Run<PointToCellLanes512>(sourceLongitudeLatitude, resolution, destinationCellIds);
    }
}
