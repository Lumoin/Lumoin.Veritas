using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Geo.Spatial3D;

/// <summary>
/// A double-precision vector in three-dimensional space: the coordinate and
/// direction carrier the plane-embedded certified geometry kernels operate over,
/// the three-ordinate sibling of <see cref="Spatial.Point2d"/>. A small immutable
/// value — algorithms stay in the kernels and the producers, never on the
/// coordinate carrier.
/// </summary>
/// <remarks>
/// Laid out as three sequential doubles (X, Y, Z) so a span of vectors
/// reinterprets as an interleaved coordinate buffer without copying. Every
/// member is plain unguarded double arithmetic; a consuming kernel states its
/// own magnitude walls and refuses operands outside them rather than relying on
/// guarded forms here.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("({X}, {Y}, {Z})")]
public readonly record struct Vector3d(double X, double Y, double Z)
{
    /// <summary>The zero vector.</summary>
    public static Vector3d Zero => new(0, 0, 0);

    /// <summary>The unit vector along the X axis.</summary>
    public static Vector3d UnitX => new(1, 0, 0);

    /// <summary>The unit vector along the Y axis.</summary>
    public static Vector3d UnitY => new(0, 1, 0);

    /// <summary>The unit vector along the Z axis.</summary>
    public static Vector3d UnitZ => new(0, 0, 1);

    /// <summary>Component-wise sum.</summary>
    public static Vector3d operator +(Vector3d left, Vector3d right)
    {
        return new Vector3d(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    /// <summary>Component-wise difference.</summary>
    public static Vector3d operator -(Vector3d left, Vector3d right)
    {
        return new Vector3d(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    /// <summary>Component-wise negation.</summary>
    public static Vector3d operator -(Vector3d value)
    {
        return new Vector3d(-value.X, -value.Y, -value.Z);
    }

    /// <summary>Scalar scaling.</summary>
    public static Vector3d operator *(Vector3d value, double scale)
    {
        return new Vector3d(value.X * scale, value.Y * scale, value.Z * scale);
    }

    /// <summary>Named alternate for <see cref="op_Addition(Vector3d, Vector3d)"/>.</summary>
    public static Vector3d Add(Vector3d left, Vector3d right)
    {
        return left + right;
    }

    /// <summary>Named alternate for <see cref="op_Subtraction(Vector3d, Vector3d)"/>.</summary>
    public static Vector3d Subtract(Vector3d left, Vector3d right)
    {
        return left - right;
    }

    /// <summary>Named alternate for <see cref="op_UnaryNegation(Vector3d)"/>.</summary>
    public static Vector3d Negate(Vector3d value)
    {
        return -value;
    }

    /// <summary>Named alternate for <see cref="op_Multiply(Vector3d, double)"/>.</summary>
    public static Vector3d Multiply(Vector3d value, double scale)
    {
        return value * scale;
    }

    /// <summary>Dot product.</summary>
    public static double Dot(Vector3d left, Vector3d right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    /// <summary>Cross product of <paramref name="left"/> with <paramref name="right"/>, right-handed.</summary>
    public static Vector3d Cross(Vector3d left, Vector3d right)
    {
        return new Vector3d(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    }

    /// <summary>The squared vector length — the comparison form that avoids the square root.</summary>
    public double LengthSquared()
    {
        return (X * X) + (Y * Y) + (Z * Z);
    }

    /// <summary>The vector length as the plain square root of <see cref="LengthSquared"/>.</summary>
    public double Length()
    {
        return Math.Sqrt(LengthSquared());
    }

    /// <summary>
    /// The unit vector in this vector's direction. A zero-magnitude vector has no
    /// direction to preserve, so normalizing one throws rather than inventing a
    /// silent default.
    /// </summary>
    /// <exception cref="InvalidOperationException">The vector has zero magnitude.</exception>
    public Vector3d Normalize()
    {
        double lengthSquared = LengthSquared();
        if(lengthSquared == 0)
        {
            throw new InvalidOperationException("Cannot normalize a zero-magnitude vector.");
        }

        double scale = 1 / Math.Sqrt(lengthSquared);

        return new Vector3d(X * scale, Y * scale, Z * scale);
    }

    /// <summary>Renders the vector with round-trip coordinate precision.</summary>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"({X:R}, {Y:R}, {Z:R})");
    }
}
