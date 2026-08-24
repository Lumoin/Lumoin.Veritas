using System;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// Transforms coordinates from a source CRS to a target CRS: interleaved
/// 2D pairs in the source system's units and axis order in, interleaved 2D
/// pairs in the destination system's units and axis order out. The
/// direction a given instance runs is defined by the concrete kernel it
/// was obtained from — the parameter names reflect the original forward
/// (geographic to Web Mercator) instance, and an inverse kernel reads them
/// in its own direction. The destination may alias the source span for
/// in-place transforms only when the two spans are identical — same start,
/// same length; partial or offset overlap is unsupported and its result is
/// unspecified.
/// </summary>
/// <param name="sourceLongitudeLatitude">Interleaved source pairs in the kernel's source system's units and axis order.</param>
/// <param name="destinationXY">Interleaved destination pairs in the kernel's destination system's units and axis order; may alias the source only when the spans are identical.</param>
/// <remarks>
/// First concrete instance: WGS 84 → Web Mercator, scalar closed-form.
/// The named-delegate slot is the substrate for future SIMD, AVX, and
/// GPGPU variants following the house kernel-pluggability pattern:
/// implementations are selectable kernels behind a selection class, never
/// intrinsics branched inline in common code.
/// </remarks>
public delegate void CoordinateTransformKernel(
    ReadOnlySpan<double> sourceLongitudeLatitude,
    Span<double> destinationXY);
