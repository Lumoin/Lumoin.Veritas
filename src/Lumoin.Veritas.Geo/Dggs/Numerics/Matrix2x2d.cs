using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Geo.Dggs.Numerics;

/// <summary>
/// Double-precision 2×2 matrix in COLUMN-MAJOR layout: the four elements are <c>[M0, M1, M2, M3]</c> where
/// <c>(M0, M1)</c> is the FIRST column and <c>(M2, M3)</c> the second. <see cref="Transform"/> therefore
/// computes <c>x * column0 + y * column1</c> — never reinterpret as a row-major multiply.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[DebuggerDisplay("col0=({M0}, {M1}) col1=({M2}, {M3})")]
internal readonly record struct Matrix2x2d(double M0, double M1, double M2, double M3)
{
    /// <summary>Rotation matrix: columns <c>(cos, sin)</c> and <c>(-sin, cos)</c>.</summary>
    public static Matrix2x2d FromRotation(double radians)
    {
        double s = Math.Sin(radians);
        double c = Math.Cos(radians);

        return new Matrix2x2d(c, s, -s, c);
    }

    /// <summary>
    /// Matrix inverse. A5 never inverts a singular matrix, so this asserts instead of introducing a
    /// null/failure path that has no consumer.
    /// </summary>
    public Matrix2x2d Invert()
    {
        double determinant = (M0 * M3) - (M2 * M1);
        Debug.Assert(determinant != 0, "A 2x2 matrix inverse is only requested for non-singular A5 basis matrices.");
        determinant = 1.0 / determinant;

        return new Matrix2x2d(M3 * determinant, -M1 * determinant, -M2 * determinant, M0 * determinant);
    }

    /// <summary>Column-major matrix–vector product: <c>(M0*x + M2*y, M1*x + M3*y)</c>.</summary>
    public Vector2d Transform(Vector2d a)
    {
        return new Vector2d((M0 * a.X) + (M2 * a.Y), (M1 * a.X) + (M3 * a.Y));
    }
}
