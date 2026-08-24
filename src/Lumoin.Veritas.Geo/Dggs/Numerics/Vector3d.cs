using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Geo.Dggs.Numerics;

/// <summary>
/// Double-precision 3D vector for the A5 kernel's per-point geometry. See <see cref="Vector2d"/> for why this
/// is a custom type and where SIMD belongs instead.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("({X}, {Y}, {Z})")]
internal readonly record struct Vector3d(double X, double Y, double Z)
{
    /// <summary>Component-wise sum.</summary>
    public static Vector3d operator +(Vector3d a, Vector3d b)
    {
        return new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    /// <summary>Component-wise difference.</summary>
    public static Vector3d operator -(Vector3d a, Vector3d b)
    {
        return new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    /// <summary>Component-wise negation.</summary>
    public static Vector3d operator -(Vector3d a)
    {
        return new Vector3d(-a.X, -a.Y, -a.Z);
    }

    /// <summary>Scalar scaling.</summary>
    public static Vector3d operator *(Vector3d a, double scale)
    {
        return new Vector3d(a.X * scale, a.Y * scale, a.Z * scale);
    }

    /// <summary>Dot product.</summary>
    public static double Dot(Vector3d a, Vector3d b)
    {
        return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
    }

    /// <summary>Cross product, component order fixed by fixture parity.</summary>
    public static Vector3d Cross(Vector3d a, Vector3d b)
    {
        return new Vector3d(
            (a.Y * b.Z) - (a.Z * b.Y),
            (a.Z * b.X) - (a.X * b.Z),
            (a.X * b.Y) - (a.Y * b.X));
    }

    /// <summary>
    /// Linear interpolation <c>a + t * (b - a)</c> per component — exactly this form, not the algebraically
    /// equal <c>(1-t)*a + t*b</c>.
    /// </summary>
    public static Vector3d Lerp(Vector3d a, Vector3d b, double t)
    {
        return new Vector3d(a.X + (t * (b.X - a.X)), a.Y + (t * (b.Y - a.Y)), a.Z + (t * (b.Z - a.Z)));
    }

    /// <summary>Vector length via ECMAScript <c>Math.hypot</c> semantics (<see cref="JsMath.Hypot(double, double, double)"/>).</summary>
    public double Length()
    {
        return JsMath.Hypot(X, Y, Z);
    }

    /// <summary>Distance between two points: <c>hypot</c> of the component differences.</summary>
    public static double Distance(Vector3d a, Vector3d b)
    {
        return JsMath.Hypot(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
    }

    /// <summary>
    /// Normalization by multiplying with <c>1/sqrt(x²+y²+z²)</c> when the squared length is positive — NOT
    /// <c>hypot</c>-based, and a multiply rather than a divide; both choices are fixture-visible. A zero vector
    /// normalizes to zero.
    /// </summary>
    public Vector3d Normalize()
    {
        double length = (X * X) + (Y * Y) + (Z * Z);
        if(length > 0)
        {
            length = 1 / Math.Sqrt(length);
        }

        return new Vector3d(X * length, Y * length, Z * length);
    }

    /// <summary>
    /// Angle between two vectors: <c>acos</c> of the dot product over the product of naive (non-hypot)
    /// magnitudes, clamped to [-1, 1] via <c>min(max(cosine, -1), 1)</c>; zero magnitude short-circuits the
    /// cosine to 0.
    /// </summary>
    public static double Angle(Vector3d a, Vector3d b)
    {
        double magnitudeA = Math.Sqrt((a.X * a.X) + (a.Y * a.Y) + (a.Z * a.Z));
        double magnitudeB = Math.Sqrt((b.X * b.X) + (b.Y * b.Y) + (b.Z * b.Z));
        double magnitude = magnitudeA * magnitudeB;
        double cosine = magnitude == 0 ? 0 : Dot(a, b) / magnitude;

        return Math.Acos(Math.Min(Math.Max(cosine, -1), 1));
    }

    /// <summary>
    /// Rotates this vector by a quaternion via the optimized double-cross-product expansion
    /// (<c>uv = cross(q, v); uuv = cross(q, uv); v + uv * 2w + uuv * 2</c>) with exact operation order — not a
    /// generic <c>q·v·q⁻¹</c> expansion, which differs at the ulp level.
    /// </summary>
    public Vector3d Transform(QuaternionD q)
    {
        double uvx = (q.Y * Z) - (q.Z * Y);
        double uvy = (q.Z * X) - (q.X * Z);
        double uvz = (q.X * Y) - (q.Y * X);

        double uuvx = (q.Y * uvz) - (q.Z * uvy);
        double uuvy = (q.Z * uvx) - (q.X * uvz);
        double uuvz = (q.X * uvy) - (q.Y * uvx);

        double w2 = q.W * 2;
        uvx *= w2;
        uvy *= w2;
        uvz *= w2;

        uuvx *= 2;
        uuvy *= 2;
        uuvz *= 2;

        return new Vector3d(X + uvx + uuvx, Y + uvy + uuvy, Z + uvz + uuvz);
    }
}
