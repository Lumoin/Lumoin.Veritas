using System;

namespace Lumoin.Veritas.Geo.Transforms;

/// <summary>
/// Closed-form Web Mercator (EPSG:3857) to WGS 84 (EPSG:4326) inverse
/// projection. Scalar reference implementation; the kernel slot exists so
/// future SIMD and GPGPU variants register through
/// <see cref="CoordinateTransformKernelSelection"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Formula.</b>
/// <code>
/// lon = (x / R) · 180 / π
/// lat = (2 · atan(exp(y / R)) − π / 2) · 180 / π
/// </code>
/// where <c>R</c> is the WGS 84 equatorial radius (6 378 137 m), read from
/// <see cref="ScalarWgs84ToWebMercator.EarthRadiusMeters"/> rather than
/// redeclared. The <c>2 · atan(exp(·))</c> form is the algebraic mirror of
/// the forward kernel's <c>ln(tan(·))</c> form — the equivalent
/// <c>atan(sinh(·))</c> identity is not used — and is transcribed exactly
/// once.
/// </para>
/// <para>
/// <b>Totality and domain.</b> The kernel is total on finite input: it
/// never throws or refuses for a mathematically well-defined double pair.
/// Domain policy — which source coordinates are meaningful Web Mercator
/// values, which computed outputs are meaningful geographic coordinates —
/// is not this kernel's concern; it lives with the caller.
/// </para>
/// <para>
/// <b>Aliasing.</b> The destination may alias the source only when the two
/// spans are identical — same start, same length. Partial or offset
/// overlap is unsupported and its result is unspecified.
/// </para>
/// <para>
/// <b>Sign of zero.</b> The abscissa leg preserves the sign of zero; the
/// ordinate leg maps negative zero to positive zero
/// (<c>2 · atan(exp(−0.0)) − π/2</c> cancels exactly to <c>0.0</c>).
/// Sign-of-zero preservation is not claimed for this kernel.
/// </para>
/// </remarks>
internal static class ScalarWebMercatorToWgs84
{
    /// <summary>Radians-to-degrees conversion factor, <c>180 / π</c>.</summary>
    private const double RadiansToDegrees = 180.0 / Math.PI;

    /// <summary>
    /// Returns the <see cref="CoordinateTransformKernel"/> delegate for this
    /// inverse projection.
    /// </summary>
    /// <returns>The kernel delegate.</returns>
    public static CoordinateTransformKernel GetTransform() => Transform;

    /// <summary>
    /// Transforms interleaved Web Mercator (x, y) pairs, in metres, to
    /// interleaved geographic (longitude, latitude) pairs, in degrees.
    /// </summary>
    /// <param name="sourceXY">Interleaved source (x0, y0, x1, y1, …) in metres.</param>
    /// <param name="destinationLongitudeLatitude">
    /// Interleaved destination (lon0, lat0, lon1, lat1, …) in degrees; may
    /// alias <paramref name="sourceXY"/> only when the two spans are
    /// identical.
    /// </param>
    private static void Transform(
        ReadOnlySpan<double> sourceXY,
        Span<double> destinationLongitudeLatitude)
    {
        if(sourceXY.Length % 2 != 0)
        {
            throw new ArgumentException(
                "Source span length must be even (interleaved x/y pairs).",
                nameof(sourceXY));
        }

        if(destinationLongitudeLatitude.Length < sourceXY.Length)
        {
            throw new ArgumentException(
                $"Destination span ({destinationLongitudeLatitude.Length}) is shorter than source ({sourceXY.Length}).",
                nameof(destinationLongitudeLatitude));
        }

        for(int index = 0; index < sourceXY.Length; index += 2)
        {
            double longitude = sourceXY[index] / ScalarWgs84ToWebMercator.EarthRadiusMeters * RadiansToDegrees;
            double latitude = ((2.0 * Math.Atan(Math.Exp(sourceXY[index + 1] / ScalarWgs84ToWebMercator.EarthRadiusMeters))) - (Math.PI / 2.0)) * RadiansToDegrees;

            destinationLongitudeLatitude[index] = longitude;
            destinationLongitudeLatitude[index + 1] = latitude;
        }
    }
}
