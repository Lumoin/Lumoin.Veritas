using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Geo.Dggs.Utils;

/// <summary>
/// Precomputed <c>gamma</c> and <c>sin(gamma)</c> for a fixed slerp arc, so loops that slerp many times along
/// the same arc don't recompute them per step.
/// </summary>
internal readonly record struct SlerpContext(double Gamma, double SinGamma);

/// <summary>
/// Spherical vector primitives. All functions are pure and re-entrant, safe under parallel callers.
/// </summary>
internal static class VectorUtilities
{
    /// <summary>
    /// Difference measure between two unit vectors: <c>sqrt(1 - dot(a,b)) / sqrt(2)</c> rewritten via the
    /// half-angle identity as <c>|cross(A, normalize(midpoint(A, B)))|</c> for small-angle stability, with a
    /// 1e-8 fallback to half the chord length. For antipodal input the code path yields a finite value.
    /// </summary>
    public static double VectorDifference(Vector3d a, Vector3d b)
    {
        Vector3d midpoint = Vector3d.Lerp(a, b, 0.5).Normalize();
        double difference = Vector3d.Cross(a, midpoint).Length();

        if(difference < 1e-8)
        {
            double halfDistance = 0.5 * (a - b).Length();

            return halfDistance;
        }

        return difference;
    }

    /// <summary>Scalar triple product <c>dot(A, cross(B, C))</c>.</summary>
    public static double TripleProduct(Vector3d a, Vector3d b, Vector3d c)
    {
        return Vector3d.Dot(a, Vector3d.Cross(b, c));
    }

    /// <summary>
    /// Vector quadruple product <c>B·[A,C,D] - A·[B,C,D]</c>, evaluated in exactly this order:
    /// <c>(B * tripleACD) - (A * tripleBCD)</c>.
    /// </summary>
    public static Vector3d QuadrupleProduct(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
    {
        Vector3d crossCD = Vector3d.Cross(c, d);
        double tripleProductACD = Vector3d.Dot(a, crossCD);
        double tripleProductBCD = Vector3d.Dot(b, crossCD);
        Vector3d scaledA = a * tripleProductBCD;
        Vector3d scaledB = b * tripleProductACD;

        return scaledB - scaledA;
    }

    /// <summary>Builds the reusable slerp context for a fixed (A, B) arc.</summary>
    public static SlerpContext PrecomputeSlerp(Vector3d a, Vector3d b)
    {
        double gamma = Vector3d.Angle(a, b);

        return new SlerpContext(gamma, Math.Sin(gamma));
    }

    /// <summary>
    /// Spherical linear interpolation: angles below the 1e-12 threshold fall back to linear interpolation —
    /// one of four independent small-angle branches that must not be merged.
    /// </summary>
    public static Vector3d Slerp(Vector3d a, Vector3d b, double t)
    {
        double gamma = Vector3d.Angle(a, b);
        if(gamma < 1e-12)
        {
            return Vector3d.Lerp(a, b, t);
        }

        double sinGamma = Math.Sin(gamma);

        return SlerpWeighted(a, b, t, gamma, sinGamma);
    }

    /// <summary>Spherical linear interpolation with a precomputed arc context.</summary>
    public static Vector3d Slerp(Vector3d a, Vector3d b, double t, SlerpContext context)
    {
        if(context.Gamma < 1e-12)
        {
            return Vector3d.Lerp(a, b, t);
        }

        return SlerpWeighted(a, b, t, context.Gamma, context.SinGamma);
    }

    /// <summary>The shared weighted-sum tail of both slerp overloads, in the exact component order.</summary>
    private static Vector3d SlerpWeighted(Vector3d a, Vector3d b, double t, double gamma, double sinGamma)
    {
        double weightA = Math.Sin((1 - t) * gamma) / sinGamma;
        double weightB = Math.Sin(t * gamma) / sinGamma;

        return new Vector3d(
            (weightA * a.X) + (weightB * b.X),
            (weightA * a.Y) + (weightB * b.Y),
            (weightA * a.Z) + (weightB * b.Z));
    }
}
