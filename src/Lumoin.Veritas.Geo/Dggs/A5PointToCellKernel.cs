using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Geo.Dggs;

/// <summary>
/// Converts a batch of geographic points to cell ids at a single resolution. Input is interleaved
/// (lon0, lat0, lon1, lat1, …) geographic degrees; output is one <see cref="A5CellId"/> per input pair,
/// in the same order.
/// </summary>
/// <remarks>
/// <para>
/// The named-delegate slot is the kernel-pluggability seam: the scalar reference kernel loops
/// <see cref="A5.LonLatToCell(LonLat, int)"/> per point, and the SIMD batch backends behind
/// <see cref="A5PointToCellKernelSelection"/> implement the same contract.
/// </para>
/// <para>
/// The scalar kernel is the correctness reference every other backend is fixture-gated against, so it
/// stays the <see cref="A5PointToCellKernelSelection.Default"/> — never silently displaced by a faster
/// backend.
/// </para>
/// <para>
/// Span-length contract: <paramref name="destinationCellIds"/>'s length must equal
/// <paramref name="sourceLongitudeLatitude"/>'s length divided by two (one cell id per lon/lat pair). An
/// odd source length, or a destination length that does not match, is a caller error and every
/// implementation of this delegate throws <see cref="ArgumentException"/> for either.
/// </para>
/// </remarks>
public delegate void A5PointToCellKernel(
    ReadOnlySpan<double> sourceLongitudeLatitude,
    int resolution,
    Span<A5CellId> destinationCellIds);
