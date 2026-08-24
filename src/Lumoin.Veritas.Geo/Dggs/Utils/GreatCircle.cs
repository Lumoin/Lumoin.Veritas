using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Geo.Dggs.Utils;

/// <summary>
/// Great-circle distance and arc sampling between unit vectors on the authalic sphere.
/// </summary>
internal static class GreatCircle
{
    /// <summary>
    /// Great-circle distance in meters between two unit vectors on the authalic sphere. The dot product
    /// is clamped to [-1, 1] via <see cref="Math.Min(double, double)"/> then <see cref="Math.Max(double, double)"/>
    /// directly — never a ternary clamp, so a NaN input propagates through <c>acos</c>.
    /// </summary>
    public static double GreatCircleDistance(Cartesian a, Cartesian b)
    {
        double rawDot = Vector3d.Dot(CoordinateConversions.ToVector3d(a), CoordinateConversions.ToVector3d(b));
        double dot = Math.Max(-1, Math.Min(1, rawDot));

        return Math.Acos(dot) * Constants.AuthalicRadiusEarth;
    }

    /// <summary>
    /// Samples interior points along the great-circle arc from <paramref name="a"/> to <paramref name="b"/>
    /// at roughly <paramref name="sampleInterval"/> meters spacing. Endpoints are NOT included — the
    /// caller already has them. Returned vectors live on the authalic unit sphere.
    /// </summary>
    /// <remarks>
    /// <paramref name="sampleInterval"/> must be positive: a non-positive interval would produce an
    /// effectively infinite sampling loop. The assert below only flags the violation in debug builds; it
    /// is not a behavior-changing guard.
    /// </remarks>
    public static Cartesian[] SampleGreatCircleArc(Cartesian a, Cartesian b, double sampleInterval)
    {
        Debug.Assert(sampleInterval > 0, "sampleGreatCircleArc's caller contract requires a positive sample interval.");

        double distance = GreatCircleDistance(a, b);
        double segmentCount = Math.Max(1, Math.Ceiling(distance / sampleInterval));
        if(segmentCount <= 1)
        {
            return [];
        }

        Vector3d vectorA = CoordinateConversions.ToVector3d(a);
        Vector3d vectorB = CoordinateConversions.ToVector3d(b);
        SlerpContext context = VectorUtilities.PrecomputeSlerp(vectorA, vectorB);

        int sampleCount = checked((int)segmentCount - 1);
        Cartesian[] samples = new Cartesian[sampleCount];
        for(int j = 1; j < segmentCount; j++)
        {
            Vector3d sample = VectorUtilities.Slerp(vectorA, vectorB, j / segmentCount, context);
            samples[j - 1] = CoordinateConversions.ToCartesian(sample);
        }

        return samples;
    }
}
