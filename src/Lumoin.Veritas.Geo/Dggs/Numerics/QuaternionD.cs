using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Geo.Dggs.Numerics;

/// <summary>
/// Double-precision quaternion, component order <c>(X, Y, Z, W)</c>. Formulas transcribed exactly.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("({X}, {Y}, {Z}, {W})")]
internal readonly record struct QuaternionD(double X, double Y, double Z, double W)
{
    /// <summary>The identity quaternion <c>(0, 0, 0, 1)</c>.</summary>
    public static QuaternionD Identity { get; } = new(0, 0, 0, 1);

    /// <summary>Axis-angle construction; the axis is expected normalized.</summary>
    public static QuaternionD FromAxisAngle(Vector3d axis, double radians)
    {
        double half = radians * 0.5;
        double s = Math.Sin(half);

        return new QuaternionD(s * axis.X, s * axis.Y, s * axis.Z, Math.Cos(half));
    }

    /// <summary>
    /// Normalization by multiplying with <c>1/sqrt</c> of the squared length when positive; a zero quaternion
    /// normalizes to zero.
    /// </summary>
    public static QuaternionD Normalize(QuaternionD q)
    {
        double length = (q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z) + (q.W * q.W);
        if(length > 0)
        {
            length = 1 / Math.Sqrt(length);
        }

        return new QuaternionD(q.X * length, q.Y * length, q.Z * length, q.W * length);
    }

    /// <summary>Quaternion conjugate.</summary>
    public static QuaternionD Conjugate(QuaternionD q)
    {
        return new QuaternionD(-q.X, -q.Y, -q.Z, q.W);
    }

    /// <summary>
    /// Shortest-arc rotation taking unit vector <paramref name="a"/> to unit vector <paramref name="b"/>,
    /// including the exact antipodal fallback the spiral search relies on near the poles
    /// (dot &lt; -0.999999 → axis = cross(xUnit, a), falling back to cross(yUnit, a) when that axis has length
    /// &lt; 0.000001, then a π axis-angle rotation) and the near-identity fast path (dot &gt; 0.999999 →
    /// identity). The thresholds are fixture-visible; do not tidy them.
    /// </summary>
    public static QuaternionD RotationTo(Vector3d a, Vector3d b)
    {
        double dot = Vector3d.Dot(a, b);
        if(dot < -0.999999)
        {
            Vector3d axis = Vector3d.Cross(new Vector3d(1, 0, 0), a);
            if(axis.Length() < 0.000001)
            {
                axis = Vector3d.Cross(new Vector3d(0, 1, 0), a);
            }

            axis = axis.Normalize();

            return FromAxisAngle(axis, Math.PI);
        }

        if(dot > 0.999999)
        {
            return Identity;
        }

        Vector3d cross = Vector3d.Cross(a, b);

        return Normalize(new QuaternionD(cross.X, cross.Y, cross.Z, 1 + dot));
    }
}
