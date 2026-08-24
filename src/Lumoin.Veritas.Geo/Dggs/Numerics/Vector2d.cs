using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Geo.Dggs.Numerics;

/// <summary>
/// Double-precision 2D vector for the A5 kernel's per-point geometry.
/// </summary>
/// <remarks>
/// <para>
/// A custom type because the BCL offers no double-precision small vector: <see cref="System.Numerics.Vector2"/>
/// is float32-backed and cannot meet the fixture tolerances (down to 5e-16). Component arithmetic uses ordinary
/// operators; the formulas with fixture-visible operation order are documented per member and must not be
/// reassociated.
/// </para>
/// <para>
/// Per-point 3-component math does not benefit from SIMD lanes; batch throughput belongs to the
/// hardware-backend kernel seam, not inside this type.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("({X}, {Y})")]
internal readonly record struct Vector2d(double X, double Y)
{
    /// <summary>Component-wise sum.</summary>
    public static Vector2d operator +(Vector2d a, Vector2d b)
    {
        return new Vector2d(a.X + b.X, a.Y + b.Y);
    }

    /// <summary>Component-wise difference.</summary>
    public static Vector2d operator -(Vector2d a, Vector2d b)
    {
        return new Vector2d(a.X - b.X, a.Y - b.Y);
    }

    /// <summary>Component-wise negation.</summary>
    public static Vector2d operator -(Vector2d a)
    {
        return new Vector2d(-a.X, -a.Y);
    }

    /// <summary>Scalar scaling.</summary>
    public static Vector2d operator *(Vector2d a, double scale)
    {
        return new Vector2d(a.X * scale, a.Y * scale);
    }

    /// <summary>Component-wise (Hadamard) product; the lattice composes flip signs with this.</summary>
    public static Vector2d ComponentMultiply(Vector2d a, Vector2d b)
    {
        return new Vector2d(a.X * b.X, a.Y * b.Y);
    }

    /// <summary>Dot product.</summary>
    public static double Dot(Vector2d a, Vector2d b)
    {
        return (a.X * b.X) + (a.Y * b.Y);
    }

    /// <summary>
    /// Linear interpolation <c>a + t * (b - a)</c> per component — exactly this form, not the algebraically
    /// equal <c>(1-t)*a + t*b</c>; the difference is fixture-visible in the last ulp.
    /// </summary>
    public static Vector2d Lerp(Vector2d a, Vector2d b, double t)
    {
        return new Vector2d(a.X + (t * (b.X - a.X)), a.Y + (t * (b.Y - a.Y)));
    }

    /// <summary>Vector length via ECMAScript <c>Math.hypot</c> semantics (<see cref="JsMath.Hypot(double, double)"/>).</summary>
    public double Length()
    {
        return JsMath.Hypot(X, Y);
    }

    /// <summary>
    /// Rotates this point around <paramref name="origin"/> by <paramref name="radians"/>, counter-clockwise for
    /// positive angles: translate to origin, rotate, translate back — in exactly that operation order.
    /// </summary>
    public Vector2d RotateAround(Vector2d origin, double radians)
    {
        double p0 = X - origin.X;
        double p1 = Y - origin.Y;
        double sinC = Math.Sin(radians);
        double cosC = Math.Cos(radians);

        return new Vector2d(((p0 * cosC) - (p1 * sinC)) + origin.X, (p0 * sinC) + (p1 * cosC) + origin.Y);
    }
}
