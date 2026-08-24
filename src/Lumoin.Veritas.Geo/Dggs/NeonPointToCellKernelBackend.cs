using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics.Arm;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Geo.Dggs;

/// <summary>
/// AArch64 NEON rung of the <see cref="A5PointToCellKernel"/> ladder: two <see cref="double"/> lanes via
/// <see cref="PointToCellBatchCore"/> at <see cref="PointToCellLanes128"/> (NEON is the 128-bit tier of
/// the cross-platform vector APIs the shared core is written against). Bit-identical to the scalar
/// reference by the core's construction; gated by the ladder's bit-identity fixtures.
/// </summary>
internal static class NeonPointToCellKernelBackend
{
    /// <summary>The stable delegate this backend hands out; created once per process.</summary>
    private static A5PointToCellKernel CachedKernel { get; } = PointToCell;

    /// <summary>Whether this host supports the NEON rung.</summary>
    public static bool IsSupported => AdvSimd.Arm64.IsSupported;

    /// <summary>
    /// Returns the NEON batch kernel, or throws <see cref="PlatformNotSupportedException"/> when the
    /// host lacks AArch64 NEON — check <see cref="IsSupported"/> first. The throw happens here, at access,
    /// so an unsupported backend can never poison a selection class's type initializer.
    /// </summary>
    public static A5PointToCellKernel GetPointToCell()
    {
        if(!IsSupported)
        {
            throw new PlatformNotSupportedException(
                "NeonPointToCellKernelBackend requires AArch64 NEON; check IsSupported before requesting the delegate.");
        }

        return CachedKernel;
    }

    /// <summary>The delegate body: the shared batch core at two lanes.</summary>
    private static void PointToCell(ReadOnlySpan<double> sourceLongitudeLatitude, int resolution, Span<A5CellId> destinationCellIds)
    {
        PointToCellBatchCore.Run<PointToCellLanes128>(sourceLongitudeLatitude, resolution, destinationCellIds);
    }
}
