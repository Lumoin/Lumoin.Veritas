using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Numerics;
using Lumoin.Veritas.Geo.Dggs.Utils;

namespace Lumoin.Veritas.Geo.Dggs.Geometry;

/// <summary>
/// Free-function spherical-polygon primitives, kept outside <see cref="SphericalPolygonShape"/> so the
/// hot indexing paths that call them per-cell avoid the class's array/vertex-list allocation.
/// </summary>
internal static class SphericalPolygonPrimitives
{
    /// <summary>
    /// Area of the spherical triangle <paramref name="v1"/>, <paramref name="v2"/>, <paramref name="v3"/>
    /// on the unit sphere, in radians, via the midpoint triple-product method (well-conditioned for the
    /// tiny triangles produced by deep-resolution sub-triangles). The small-angle branch below
    /// (<c>|clamped| &lt; 1e-8 → 2·clamped</c>) is its own independent threshold, never merged with the
    /// other small-angle branches elsewhere in this codebase.
    /// </summary>
    public static double SphericalTriangleArea(Cartesian v1, Cartesian v2, Cartesian v3)
    {
        Vector3d vector1 = CoordinateConversions.ToVector3d(v1);
        Vector3d vector2 = CoordinateConversions.ToVector3d(v2);
        Vector3d vector3 = CoordinateConversions.ToVector3d(v3);

        Vector3d midpointOppositeV1 = Vector3d.Lerp(vector2, vector3, 0.5).Normalize();
        Vector3d midpointOppositeV2 = Vector3d.Lerp(vector3, vector1, 0.5).Normalize();
        Vector3d midpointOppositeV3 = Vector3d.Lerp(vector1, vector2, 0.5).Normalize();

        double scalarTripleProduct = VectorUtilities.TripleProduct(midpointOppositeV1, midpointOppositeV2, midpointOppositeV3);
        double clamped = Math.Max(-1.0, Math.Min(1.0, scalarTripleProduct));

        if(Math.Abs(clamped) < 1e-8)
        {
            return 2 * clamped;
        }

        return Math.Asin(clamped) * 2;
    }

    /// <summary>
    /// Spherical point-in-polygon test via signed-angle summation around <paramref name="point"/>.
    /// Works for concave rings, unlike <see cref="SphericalPolygonShape.ContainsPoint"/>, which assumes
    /// a convex polygon. The arithmetic is fully inlined (not routed through <see cref="Vector3d"/>)
    /// because this runs per-cell in polygon-fill hot paths.
    /// </summary>
    public static bool PointInSphericalPolygon(Cartesian point, Cartesian[] vertices)
    {
        double angleSum = 0;
        int n = vertices.Length;
        for(int index = 0; index < n; index++)
        {
            Cartesian a = vertices[index];
            Cartesian b = vertices[(index + 1) % n];

            double dotPointA = (point.X * a.X) + (point.Y * a.Y) + (point.Z * a.Z);
            double dotPointB = (point.X * b.X) + (point.Y * b.Y) + (point.Z * b.Z);

            double aPerpX = a.X - (dotPointA * point.X);
            double aPerpY = a.Y - (dotPointA * point.Y);
            double aPerpZ = a.Z - (dotPointA * point.Z);

            double bPerpX = b.X - (dotPointB * point.X);
            double bPerpY = b.Y - (dotPointB * point.Y);
            double bPerpZ = b.Z - (dotPointB * point.Z);

            double crossX = (aPerpY * bPerpZ) - (aPerpZ * bPerpY);
            double crossY = (aPerpZ * bPerpX) - (aPerpX * bPerpZ);
            double crossZ = (aPerpX * bPerpY) - (aPerpY * bPerpX);

            double sinComponent = (crossX * point.X) + (crossY * point.Y) + (crossZ * point.Z);
            double cosComponent = (aPerpX * bPerpX) + (aPerpY * bPerpY) + (aPerpZ * bPerpZ);
            angleSum += Math.Atan2(sinComponent, cosComponent);
        }

        return Math.Abs(angleSum) > Math.PI;
    }

    /// <summary>
    /// Ring winding direction: +1 for counter-clockwise (interior to the left of the edge direction),
    /// −1 for clockwise. Sums <c>(vᵢ × vᵢ₊₁) · centroid</c> across the ring.
    /// </summary>
    public static int RingWindingSign(Cartesian[] ringVertices)
    {
        Vector3d centroid = new(0, 0, 0);
        foreach(Cartesian vertex in ringVertices)
        {
            centroid += CoordinateConversions.ToVector3d(vertex);
        }

        centroid = centroid.Normalize();

        double sum = 0;
        int n = ringVertices.Length;
        for(int index = 0; index < n; index++)
        {
            Vector3d a = CoordinateConversions.ToVector3d(ringVertices[index]);
            Vector3d b = CoordinateConversions.ToVector3d(ringVertices[(index + 1) % n]);
            sum += VectorUtilities.TripleProduct(centroid, a, b);
        }

        return sum > 0 ? 1 : -1;
    }

    /// <summary>Great-circle plane normals for every segment of the ring.</summary>
    public static Cartesian[] RingSegmentNormals(Cartesian[] ringVertices)
    {
        int n = ringVertices.Length;
        Cartesian[] normals = new Cartesian[n];
        for(int index = 0; index < n; index++)
        {
            Vector3d a = CoordinateConversions.ToVector3d(ringVertices[index]);
            Vector3d b = CoordinateConversions.ToVector3d(ringVertices[(index + 1) % n]);
            normals[index] = CoordinateConversions.ToCartesian(Vector3d.Cross(a, b));
        }

        return normals;
    }
}
