using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Numerics;
using Lumoin.Veritas.Geo.Dggs.Projections;

namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// Conversions between the coordinate systems the A5 grid is built from: polar and face coordinates,
/// the lattice's <see cref="IJ"/> basis, barycentric coordinates within a face triangle, spherical and
/// Cartesian points on the unit sphere, and geographic longitude/latitude. Every formula and its
/// operation order below is transcribed exactly.
/// </summary>
internal static class CoordinateTransforms
{
    /// <summary>
    /// Longitude offset, in degrees, applied before projecting a geographic point onto the sphere: the
    /// angle between the Greenwich meridian and the vector joining the first two dodecahedron face
    /// centers. Chosen so that the great majority of the world's population — and hence its land mass —
    /// falls within the first faces visited by the Hilbert curve. An exact literal, never a "rounder"
    /// substitute.
    /// </summary>
    internal const double LongitudeOffsetDegrees = 93;

    /// <summary>Converts an angle in degrees to radians.</summary>
    public static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180);
    }

    /// <summary>Converts an angle in radians to degrees.</summary>
    public static double RadiansToDegrees(double radians)
    {
        return radians * (180 / Math.PI);
    }

    /// <summary>Converts face coordinates to polar coordinates: radial distance via vector length, azimuth via <c>atan2</c>.</summary>
    public static Polar ToPolar(Face face)
    {
        double rho = CoordinateConversions.ToVector2d(face).Length();
        double gamma = Math.Atan2(face.Y, face.X);

        return new Polar(rho, gamma);
    }

    /// <summary>Converts polar coordinates back to face coordinates.</summary>
    public static Face ToFace(Polar polar)
    {
        double x = polar.Rho * Math.Cos(polar.Gamma);
        double y = polar.Rho * Math.Sin(polar.Gamma);

        return new Face(x, y);
    }

    /// <summary>Converts face coordinates to the lattice's <see cref="IJ"/> basis via <see cref="PentagonConstants.BasisInverse"/>.</summary>
    public static IJ FaceToIJ(Face face)
    {
        Vector2d ij = PentagonConstants.BasisInverse.Transform(CoordinateConversions.ToVector2d(face));

        return CoordinateConversions.ToIJ(ij);
    }

    /// <summary>Converts <see cref="IJ"/> lattice coordinates back to face coordinates via <see cref="PentagonConstants.Basis"/>.</summary>
    public static Face IJToFace(IJ ij)
    {
        Vector2d face = PentagonConstants.Basis.Transform(CoordinateConversions.ToVector2d(ij));

        return CoordinateConversions.ToFace(face);
    }

    /// <summary>
    /// Converts a face-coordinate point to barycentric coordinates relative to the triangle
    /// <paramref name="triangleVertexA"/>, <paramref name="triangleVertexB"/>,
    /// <paramref name="triangleVertexC"/>.
    /// </summary>
    public static Barycentric FaceToBarycentric(Face point, Face triangleVertexA, Face triangleVertexB, Face triangleVertexC)
    {
        double d31X = triangleVertexA.X - triangleVertexC.X;
        double d31Y = triangleVertexA.Y - triangleVertexC.Y;
        double d23X = triangleVertexC.X - triangleVertexB.X;
        double d23Y = triangleVertexC.Y - triangleVertexB.Y;
        double d3pX = point.X - triangleVertexC.X;
        double d3pY = point.Y - triangleVertexC.Y;

        double determinant = (d23X * d31Y) - (d23Y * d31X);
        double b0 = ((d23X * d3pY) - (d23Y * d3pX)) / determinant;
        double b1 = ((d31X * d3pY) - (d31Y * d3pX)) / determinant;
        double b2 = 1 - (b0 + b1);

        return new Barycentric(b0, b1, b2);
    }

    /// <summary>
    /// Converts barycentric coordinates back to a face-coordinate point relative to the triangle
    /// <paramref name="triangleVertexA"/>, <paramref name="triangleVertexB"/>,
    /// <paramref name="triangleVertexC"/>.
    /// </summary>
    public static Face BarycentricToFace(Barycentric barycentric, Face triangleVertexA, Face triangleVertexB, Face triangleVertexC)
    {
        double x = (barycentric.B0 * triangleVertexA.X) + (barycentric.B1 * triangleVertexB.X) + (barycentric.B2 * triangleVertexC.X);
        double y = (barycentric.B0 * triangleVertexA.Y) + (barycentric.B1 * triangleVertexB.Y) + (barycentric.B2 * triangleVertexC.Y);

        return new Face(x, y);
    }

    /// <summary>Converts a Cartesian unit-sphere point to spherical coordinates.</summary>
    public static Spherical ToSpherical(Cartesian cartesian)
    {
        double theta = Math.Atan2(cartesian.Y, cartesian.X);
        double r = Math.Sqrt((cartesian.X * cartesian.X) + (cartesian.Y * cartesian.Y) + (cartesian.Z * cartesian.Z));
        double phi = Math.Acos(cartesian.Z / r);

        return new Spherical(theta, phi);
    }

    /// <summary>Converts spherical coordinates to a Cartesian unit-sphere point.</summary>
    public static Cartesian ToCartesian(Spherical spherical)
    {
        double sinPhi = Math.Sin(spherical.Phi);
        double x = sinPhi * Math.Cos(spherical.Theta);
        double y = sinPhi * Math.Sin(spherical.Theta);
        double z = Math.Cos(spherical.Phi);

        return new Cartesian(x, y, z);
    }

    /// <summary>
    /// Converts longitude/latitude to spherical coordinates. The longitude is offset and converted to
    /// radians for <c>theta</c> but deliberately NOT wrapped to any range here — only
    /// <see cref="ToLonLat"/>'s output is normalized.
    /// </summary>
    public static Spherical FromLonLat(LonLat lonLat)
    {
        double theta = DegreesToRadians(lonLat.Longitude + LongitudeOffsetDegrees);

        double geodeticLatitude = DegreesToRadians(lonLat.Latitude);
        double authalicLatitude = AuthalicProjection.Forward(geodeticLatitude);
        double phi = (Math.PI / 2) - authalicLatitude;

        return new Spherical(theta, phi);
    }

    /// <summary>Normalizes a longitude value, in degrees, to the range [-180, 180).</summary>
    public static double NormalizeLongitude(double longitudeDegrees)
    {
        return ((((longitudeDegrees + 180) % 360) + 360) % 360) - 180;
    }

    /// <summary>
    /// Converts spherical coordinates back to longitude/latitude. Unlike <see cref="FromLonLat"/>'s
    /// input, the longitude IS normalized here, to the range [-180, 180).
    /// </summary>
    public static LonLat ToLonLat(Spherical spherical)
    {
        double longitude = NormalizeLongitude(RadiansToDegrees(spherical.Theta) - LongitudeOffsetDegrees);

        double authalicLatitude = (Math.PI / 2) - spherical.Phi;
        double geodeticLatitude = AuthalicProjection.Inverse(authalicLatitude);
        double latitude = RadiansToDegrees(geodeticLatitude);

        return new LonLat(longitude, latitude);
    }

    /// <summary>
    /// Normalizes the longitudes of a closed contour so every point's longitude lies within 180
    /// degrees of the contour's centroid longitude, resolving antimeridian-crossing ambiguity. The
    /// centroid is accumulated directly in Cartesian space (no intermediate point array) and, within
    /// 0.01 degrees of either pole, falls back to the first point's own longitude rather than the
    /// (poorly defined) centroid longitude. The per-point unwrap uses a literal
    /// <see langword="while"/> loop rather than a modulo rewrite — a modulo-equivalent could land
    /// exactly 360 degrees off in edge cases, a discrete branch difference rather than a rounding one.
    /// </summary>
    public static LonLat[] NormalizeLongitudes(ReadOnlySpan<LonLat> contour)
    {
        Vector3d center = new(0, 0, 0);
        for(int index = 0; index < contour.Length; index++)
        {
            center += CoordinateConversions.ToVector3d(ToCartesian(FromLonLat(contour[index])));
        }

        center = center.Normalize();
        LonLat centerLonLat = ToLonLat(ToSpherical(CoordinateConversions.ToCartesian(center)));
        double centerLongitude = centerLonLat.Longitude;

        if(centerLonLat.Latitude > 89.99 || centerLonLat.Latitude < -89.99)
        {
            // Near the poles the centroid longitude is poorly defined — use the first point's own.
            centerLongitude = contour[0].Longitude;
        }

        centerLongitude = NormalizeLongitude(centerLongitude);

        LonLat[] result = new LonLat[contour.Length];
        for(int index = 0; index < contour.Length; index++)
        {
            double longitude = contour[index].Longitude;
            double latitude = contour[index].Latitude;

            while(longitude - centerLongitude > 180)
            {
                longitude -= 360;
            }

            while(longitude - centerLongitude < -180)
            {
                longitude += 360;
            }

            result[index] = new LonLat(longitude, latitude);
        }

        return result;
    }
}
