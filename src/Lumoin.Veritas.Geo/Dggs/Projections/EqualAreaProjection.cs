using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Geometry;
using Lumoin.Veritas.Geo.Dggs.Numerics;
using Lumoin.Veritas.Geo.Dggs.Utils;

namespace Lumoin.Veritas.Geo.Dggs.Projections;

/// <summary>
/// Shape-only invariants of a spherical triangle, sufficient to reconstruct
/// <see cref="EqualAreaProjection"/>'s per-point projection math without recomputing them for every
/// call: <paramref name="V"/> is the signed triple product <c>A · (B × C)</c>; <paramref name="C12"/>
/// is <c>B · C</c>; <paramref name="S12"/> is <c>|B × C|</c>; <paramref name="KQ"/> is
/// <c>2 / acos(C12)</c> (divides by zero for a degenerate triangle, left unguarded);
/// <paramref name="TriangleArea"/> is the spherical area of the
/// triangle <c>A, B, C</c>. A5 only ever projects the congruent face triangles of a single
/// dodecahedron, so these depend only on the triangle's SHAPE, not its position: <paramref name="V"/>
/// is signed, so this caching is only valid while every triangle shares the same winding —
/// <see cref="DodecahedronProjection"/> guarantees that by ordering vertices consistently across plain
/// and reflected faces.
/// </summary>
internal readonly record struct EqualAreaTriangleConstants(double V, double C12, double S12, double KQ, double TriangleArea);

/// <summary>
/// The IVEA (equal-area) projection between a spherical triangle on the unit sphere and its matching
/// triangle in <see cref="Face"/> coordinates: the mapping that gives every A5 cell equal area
/// regardless of resolution or position. An instance carries the <see cref="EqualAreaTriangleConstants"/>
/// derived once from a single canonical triangle at construction (every dodecahedron face triangle is
/// congruent with that canonical one, so one instance's constants serve every projection
/// <see cref="DodecahedronProjection"/> performs).
/// </summary>
internal sealed class EqualAreaProjection
{
    /// <summary>The vertex-snap threshold in <see cref="Inverse"/>: barycentric weights above this return the original triangle vertex exactly.</summary>
    private const double VertexSnapThreshold = 1 - 1e-14;

    /// <summary><see cref="SafeAcos"/>'s small-<c>x</c> branch threshold — its own branch, never merged with any other small-angle branch.</summary>
    private const double SafeAcosSmallThreshold = 1e-3;

    /// <summary>This instance's shape constants, derived once at construction from its canonical triangle.</summary>
    /// <summary>
    /// The instance's shape constants, exposed for the batch point-to-cell kernel core so its lane-wise
    /// mirror of <see cref="Forward"/> divides by the exact same <see cref="EqualAreaTriangleConstants.TriangleArea"/>.
    /// </summary>
    internal EqualAreaTriangleConstants Constants { get; }

    /// <summary>Constructs the projection from a canonical spherical triangle, computing its shape constants once.</summary>
    public EqualAreaProjection(SphericalTriangle canonicalTriangle)
    {
        Constants = ComputeConstants(canonicalTriangle);
    }

    /// <summary>Computes a spherical triangle's shape-only invariants, per <see cref="EqualAreaTriangleConstants"/>.</summary>
    public static EqualAreaTriangleConstants ComputeConstants(SphericalTriangle sphericalTriangle)
    {
        Vector3d vertexA = CoordinateConversions.ToVector3d(sphericalTriangle.A);
        Vector3d vertexB = CoordinateConversions.ToVector3d(sphericalTriangle.B);
        Vector3d vertexC = CoordinateConversions.ToVector3d(sphericalTriangle.C);

        Vector3d crossBC = Vector3d.Cross(vertexB, vertexC);
        double dotBC = Vector3d.Dot(vertexB, vertexC);

        return new EqualAreaTriangleConstants(
            Vector3d.Dot(vertexA, crossBC),
            dotBC,
            crossBC.Length(),
            2 / Math.Acos(dotBC),
            SphericalPolygonPrimitives.SphericalTriangleArea(sphericalTriangle.A, sphericalTriangle.B, sphericalTriangle.C));
    }

    /// <summary>
    /// Forward projection: converts a spherical point to <see cref="Face"/> coordinates within
    /// <paramref name="faceTriangle"/>, given the matching <paramref name="sphericalTriangle"/> it was
    /// projected from.
    /// </summary>
    public Face Forward(Cartesian point, SphericalTriangle sphericalTriangle, FaceTriangle faceTriangle)
    {
        Vector3d vertexA = CoordinateConversions.ToVector3d(sphericalTriangle.A);
        Vector3d vertexB = CoordinateConversions.ToVector3d(sphericalTriangle.B);
        Vector3d vertexC = CoordinateConversions.ToVector3d(sphericalTriangle.C);
        Vector3d pointVector = CoordinateConversions.ToVector3d(point);

        // When the point is close to vertex A, the quadruple product below is unstable; the
        // difference (point - A) lies in the same plane as the great circle through A and the point,
        // so it stands in for the point itself.
        Vector3d differenceFromVertexA = (pointVector - vertexA).Normalize();
        Vector3d intersectionVector = VectorUtilities.QuadrupleProduct(vertexA, differenceFromVertexA, vertexB, vertexC).Normalize();
        Cartesian intersection = CoordinateConversions.ToCartesian(intersectionVector);

        double heightRatio = VectorUtilities.VectorDifference(vertexA, pointVector) / VectorUtilities.VectorDifference(vertexA, intersectionVector);
        double scaledArea = heightRatio / Constants.TriangleArea;

        // Barycentric weight 1 maps to triangle(A, intersection, C); weight 2 maps to triangle(A, B,
        // intersection) — cross-checked against Inverse's R = b2 / heightRatio relationship.
        Barycentric barycentric = new(
            1 - heightRatio,
            scaledArea * SphericalPolygonPrimitives.SphericalTriangleArea(sphericalTriangle.A, intersection, sphericalTriangle.C),
            scaledArea * SphericalPolygonPrimitives.SphericalTriangleArea(sphericalTriangle.A, sphericalTriangle.B, intersection));

        return CoordinateTransforms.BarycentricToFace(barycentric, faceTriangle.A, faceTriangle.B, faceTriangle.C);
    }

    /// <summary>
    /// Inverse projection: converts <see cref="Face"/> coordinates within <paramref name="faceTriangle"/>
    /// back to a spherical point, given the matching <paramref name="sphericalTriangle"/> to unproject onto.
    /// </summary>
    public Cartesian Inverse(Face facePoint, FaceTriangle faceTriangle, SphericalTriangle sphericalTriangle)
    {
        Barycentric barycentric = CoordinateTransforms.FaceToBarycentric(facePoint, faceTriangle.A, faceTriangle.B, faceTriangle.C);

        if(barycentric.B0 > VertexSnapThreshold)
        {
            return sphericalTriangle.A;
        }

        if(barycentric.B1 > VertexSnapThreshold)
        {
            return sphericalTriangle.B;
        }

        if(barycentric.B2 > VertexSnapThreshold)
        {
            return sphericalTriangle.C;
        }

        Vector3d vertexA = CoordinateConversions.ToVector3d(sphericalTriangle.A);
        Vector3d vertexB = CoordinateConversions.ToVector3d(sphericalTriangle.B);
        Vector3d vertexC = CoordinateConversions.ToVector3d(sphericalTriangle.C);

        double heightRatio = 1 - barycentric.B0;
        double radiusRatio = barycentric.B2 / heightRatio;
        double alpha = radiusRatio * Constants.TriangleArea;
        double sinAlpha = Math.Sin(alpha);
        double sinHalfAlpha = Math.Sin(alpha / 2);

        // The versine of alpha via the half-angle identity, not 1 - cos(alpha).
        double versineAlpha = 2 * sinHalfAlpha * sinHalfAlpha;

        // A·B and C·A swap between plain and reflected face triangles, so — unlike C12/S12/KQ/V/
        // TriangleArea — they cannot be cached on the triangle constants and are recomputed per call.
        double dotAB = Vector3d.Dot(vertexA, vertexB);
        double dotCA = Vector3d.Dot(vertexC, vertexA);

        double f = (sinAlpha * Constants.V) + (versineAlpha * ((dotAB * Constants.C12) - dotCA));
        double g = versineAlpha * Constants.S12 * (1 + dotAB);

        // atan2's argument order is (g, f), not (f, g).
        double arcInterpolationParameter = Constants.KQ * Math.Atan2(g, f);

        Vector3d pointOnArcBC = VectorUtilities.Slerp(vertexB, vertexC, arcInterpolationParameter);
        double differenceFromAToArcPoint = VectorUtilities.VectorDifference(vertexA, pointOnArcBC);
        double interpolationParameter = SafeAcos(heightRatio * differenceFromAToArcPoint) / SafeAcos(differenceFromAToArcPoint);

        Vector3d result = VectorUtilities.Slerp(vertexA, pointOnArcBC, interpolationParameter);

        return CoordinateConversions.ToCartesian(result);
    }

    /// <summary>
    /// Computes <c>acos(1 - 2x²)</c> without precision loss for small <paramref name="x"/>, via its own
    /// series branch — never merged with any other small-angle branch elsewhere in this codebase.
    /// </summary>
    private static double SafeAcos(double x)
    {
        if(x < SafeAcosSmallThreshold)
        {
            return (2 * x) + ((x * x * x) / 3);
        }

        return Math.Acos(1 - (2 * x * x));
    }
}
