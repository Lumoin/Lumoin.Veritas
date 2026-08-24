using System;

namespace Lumoin.Veritas.Geo.Transforms;

/// <summary>
/// Closed-form WGS 84 (EPSG:4326) to Web Mercator (EPSG:3857) projection.
/// Scalar reference implementation; the kernel slot exists so future SIMD
/// and GPGPU variants register through
/// <see cref="CoordinateTransformKernelSelection"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Formula.</b>
/// <code>
/// x = R · lon · π / 180
/// y = R · ln(tan(π/4 + (lat · π / 180) / 2))
/// </code>
/// where <c>R</c> is the WGS 84 equatorial radius (6 378 137 m).
/// </para>
/// <para>
/// <b>Latitude clamp.</b> The Mercator projection diverges at the poles.
/// Latitudes outside <c>±85.05112877980659°</c> are clamped to that
/// limit. Published datasets carry features outside this range (polar ice,
/// polar research stations); clamping is the conventional Web Mercator
/// handling in mapping toolchains.
/// </para>
/// </remarks>
internal static class ScalarWgs84ToWebMercator
{
    /// <summary>The WGS 84 equatorial radius, in metres — the <c>R</c> of both projection legs.</summary>
    public const double EarthRadiusMeters = 6_378_137.0;

    /// <summary>The latitude, in degrees, whose Web Mercator ordinate equals half the world extent; the projection's poleward clamp limit.</summary>
    public const double MercatorLatitudeLimitDegrees = 85.05112877980659;

    /// <summary>Degrees-to-radians conversion factor, <c>π / 180</c>.</summary>
    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>
    /// Returns the <see cref="CoordinateTransformKernel"/> delegate for this
    /// forward projection.
    /// </summary>
    /// <returns>The kernel delegate.</returns>
    public static CoordinateTransformKernel GetTransform() => Transform;

    /// <summary>
    /// Transforms interleaved geographic (longitude, latitude) pairs, in
    /// degrees, to interleaved Web Mercator (x, y) pairs, in metres.
    /// </summary>
    /// <param name="sourceLongitudeLatitude">Interleaved source (lon0, lat0, lon1, lat1, …) in degrees.</param>
    /// <param name="destinationXY">
    /// Interleaved destination (x0, y0, x1, y1, …) in metres; may alias
    /// <paramref name="sourceLongitudeLatitude"/> only when the two spans
    /// are identical.
    /// </param>
    private static void Transform(
        ReadOnlySpan<double> sourceLongitudeLatitude,
        Span<double> destinationXY)
    {
        if(sourceLongitudeLatitude.Length % 2 != 0)
        {
            throw new ArgumentException(
                "Source span length must be even (interleaved longitude/latitude pairs).",
                nameof(sourceLongitudeLatitude));
        }

        if(destinationXY.Length < sourceLongitudeLatitude.Length)
        {
            throw new ArgumentException(
                $"Destination span ({destinationXY.Length}) is shorter than source ({sourceLongitudeLatitude.Length}).",
                nameof(destinationXY));
        }

        for(int index = 0; index < sourceLongitudeLatitude.Length; index += 2)
        {
            double longitude = sourceLongitudeLatitude[index];
            double latitude = sourceLongitudeLatitude[index + 1];

            if(latitude > MercatorLatitudeLimitDegrees)
            {
                latitude = MercatorLatitudeLimitDegrees;
            }
            else if(latitude < -MercatorLatitudeLimitDegrees)
            {
                latitude = -MercatorLatitudeLimitDegrees;
            }

            double x = EarthRadiusMeters * longitude * DegreesToRadians;
            double y = EarthRadiusMeters * Math.Log(Math.Tan((Math.PI / 4.0) + (latitude * DegreesToRadians / 2.0)));

            destinationXY[index] = x;
            destinationXY[index + 1] = y;
        }
    }
}
