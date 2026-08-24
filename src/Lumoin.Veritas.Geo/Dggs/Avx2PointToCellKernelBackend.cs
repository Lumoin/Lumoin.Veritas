using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Geo.Dggs;

/// <summary>
/// AVX2 rung of the <see cref="A5PointToCellKernel"/> ladder: four <see cref="double"/> lanes via
/// <see cref="PointToCellBatchCore"/> at <see cref="PointToCellLanes256"/>. Bit-identical to the scalar
/// reference by the core's construction; gated by the ladder's bit-identity fixtures.
/// </summary>
internal static class Avx2PointToCellKernelBackend
{
    /// <summary>The stable delegate this backend hands out; created once per process.</summary>
    private static A5PointToCellKernel CachedKernel { get; } = PointToCell;

    /// <summary>Whether this host supports the AVX2 rung.</summary>
    public static bool IsSupported => Avx2.IsSupported && Vector256.IsHardwareAccelerated;

    /// <summary>
    /// Returns the AVX2 batch kernel, or throws <see cref="PlatformNotSupportedException"/> when the
    /// host lacks AVX2 — check <see cref="IsSupported"/> first. The throw happens here, at access, so an
    /// unsupported backend can never poison a selection class's type initializer.
    /// </summary>
    public static A5PointToCellKernel GetPointToCell()
    {
        if(!IsSupported)
        {
            throw new PlatformNotSupportedException(
                "Avx2PointToCellKernelBackend requires AVX2 with accelerated Vector256; check IsSupported before requesting the delegate.");
        }

        return CachedKernel;
    }

    /// <summary>The delegate body: the shared batch core at four lanes.</summary>
    private static void PointToCell(ReadOnlySpan<double> sourceLongitudeLatitude, int resolution, Span<A5CellId> destinationCellIds)
    {
        PointToCellBatchCore.Run<PointToCellLanes256>(sourceLongitudeLatitude, resolution, destinationCellIds);
    }
}
