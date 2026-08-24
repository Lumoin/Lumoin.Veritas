using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Numerics;
using Lumoin.Veritas.Geo.Dggs.Utils;

namespace Lumoin.Veritas.Geo.Dggs.Geometry;

/// <summary>
/// A polygon whose vertices lie on the unit sphere, stored as <see cref="Cartesian"/> points (rather
/// than spherical angles) for uniform precision in every direction. The instance owns its vertex list
/// — the constructor copies the caller's array — and its Area is computed once at construction and
/// cached in a readonly field rather than lazily on first use: an instance-level mutable cache would
/// be non-reentrant state, and eager computation from otherwise-immutable inputs is safe.
/// </summary>
internal class SphericalPolygonShape
{
    /// <summary>The owned backing array of the polygon's vertices.</summary>
    private Cartesian[] VertexArray { get; }

    /// <summary>The polygon's area in radians, computed once at construction.</summary>
    private double Area { get; }

    /// <summary>Builds a spherical polygon shape from its vertices, copying them into an owned array.</summary>
    public SphericalPolygonShape(Cartesian[] vertices)
    {
        VertexArray = new Cartesian[vertices.Length];
        Array.Copy(vertices, VertexArray, vertices.Length);
        Area = ComputeArea(VertexArray);
    }

    /// <summary>
    /// Returns a closed or open boundary of the polygon with <paramref name="segmentsPerEdge"/> points
    /// per edge.
    /// </summary>
    public Cartesian[] GetBoundary(int segmentsPerEdge = 1, bool closedRing = true)
    {
        int n = VertexArray.Length;
        int pointCount = n * segmentsPerEdge;
        Cartesian[] points = new Cartesian[closedRing ? pointCount + 1 : pointCount];
        for(int s = 0; s < pointCount; s++)
        {
            double t = (double)s / segmentsPerEdge;
            points[s] = Slerp(t);
        }

        if(closedRing)
        {
            points[pointCount] = points[0];
        }

        return points;
    }

    /// <summary>
    /// Interpolates along the boundary of the polygon. Passing <c>t = 1.5</c> gives the midpoint
    /// between the 2nd and 3rd vertices.
    /// </summary>
    public Cartesian Slerp(double t)
    {
        int n = VertexArray.Length;
        double fraction = t % 1;
        int index = (int)Math.Floor(t % n);
        int next = (index + 1) % n;
        Vector3d result = VectorUtilities.Slerp(CoordinateConversions.ToVector3d(VertexArray[index]), CoordinateConversions.ToVector3d(VertexArray[next]), fraction);

        return CoordinateConversions.ToCartesian(result);
    }

    /// <summary>
    /// Tests whether <paramref name="point"/> is inside the polygon, using the "necessary strike"
    /// condition from the locate-point-relative-to-spherical-polygon algorithm. This assumes a CONVEX
    /// polygon; concave rings need the free function
    /// <see cref="SphericalPolygonPrimitives.PointInSphericalPolygon"/> instead — the two are kept
    /// deliberately separate rather than merged.
    /// </summary>
    /// <returns>
    /// A positive value if the point is inside every arc, zero if it is exactly on an edge, and a
    /// negative value (growing with distance from the boundary) if it is outside.
    /// </returns>
    public double ContainsPoint(Cartesian point)
    {
        int n = VertexArray.Length;
        double thetaDeltaMin = double.PositiveInfinity;
        Vector3d pointVector = CoordinateConversions.ToVector3d(point);

        for(int index = 0; index < n; index++)
        {
            (Vector3d vertex, Vector3d towardNext, Vector3d towardPrevious) = GetTransformedVertices(index);

            Vector3d vectorToPoint = (pointVector - vertex).Normalize();
            Vector3d directionToNext = towardNext.Normalize();
            Vector3d directionToPrevious = towardPrevious.Normalize();

            Vector3d crossToNext = Vector3d.Cross(directionToNext, vectorToPoint);
            Vector3d crossToPrevious = Vector3d.Cross(vectorToPoint, directionToPrevious);

            double sinToNext = Vector3d.Dot(vertex, crossToNext);
            double sinToPrevious = Vector3d.Dot(vertex, crossToPrevious);

            thetaDeltaMin = Math.Min(thetaDeltaMin, Math.Min(sinToNext, sinToPrevious));
        }

        return thetaDeltaMin;
    }

    /// <summary>The Area of the spherical polygon in radians, decomposed into a fan of triangles.</summary>
    public double GetArea()
    {
        return Area;
    }

    /// <summary>
    /// Computes the fan-triangle Area: zero for fewer than 3 vertices, the direct triangle Area for
    /// exactly 3, and otherwise a fan around the normalized vertex centroid that SKIPS any NaN
    /// degenerate fan-triangle Area rather than propagating it into the sum.
    /// </summary>
    private static double ComputeArea(Cartesian[] vertices)
    {
        if(vertices.Length < 3)
        {
            return 0;
        }

        if(vertices.Length == 3)
        {
            return SphericalPolygonPrimitives.SphericalTriangleArea(vertices[0], vertices[1], vertices[2]);
        }

        Vector3d center = new(0, 0, 0);
        foreach(Cartesian vertex in vertices)
        {
            center += CoordinateConversions.ToVector3d(vertex);
        }

        center = center.Normalize();
        Cartesian centerCartesian = CoordinateConversions.ToCartesian(center);

        double totalArea = 0;
        int n = vertices.Length;
        for(int index = 0; index < n; index++)
        {
            Cartesian v1 = vertices[index];
            Cartesian v2 = vertices[(index + 1) % n];
            double triangleArea = SphericalPolygonPrimitives.SphericalTriangleArea(centerCartesian, v1, v2);
            if(!double.IsNaN(triangleArea))
            {
                totalArea += triangleArea;
            }
        }

        return totalArea;
    }

    /// <summary>
    /// Returns the vertex at <paramref name="index"/> together with the vectors toward its neighbors:
    /// toward the next vertex, and toward the previous one.
    /// </summary>
    private (Vector3d Vertex, Vector3d TowardNext, Vector3d TowardPrevious) GetTransformedVertices(int index)
    {
        int n = VertexArray.Length;
        int next = (index + 1) % n;
        int previous = (index + n - 1) % n;

        Vector3d vertex = CoordinateConversions.ToVector3d(VertexArray[index]);
        Vector3d towardNext = CoordinateConversions.ToVector3d(VertexArray[next]) - vertex;
        Vector3d towardPrevious = CoordinateConversions.ToVector3d(VertexArray[previous]) - vertex;

        return (vertex, towardNext, towardPrevious);
    }
}
