using System;
using System.Collections.Generic;
namespace Lumoin.Veritas.Geo.Dggs.Numerics;

/// <summary>
/// ECMAScript numeric semantics, reproduced exactly where they differ from the .NET defaults.
/// </summary>
internal static class JsMath
{
    /// <summary>
    /// ECMAScript <c>Math.round</c>: round half toward positive infinity for ALL inputs (including negatives:
    /// <c>Math.round(-1.5) === -1</c>). .NET's <see cref="Math.Round(double)"/> is round-half-to-even and
    /// <c>MidpointRounding.AwayFromZero</c> rounds negative halves the other way — both diverge on half-integers.
    /// </summary>
    public static double Round(double value)
    {
        return Math.Floor(value + 0.5);
    }

    /// <summary>
    /// ECMAScript <c>Math.hypot(x, y)</c> as the fixture corpus was generated with: normalize by the largest
    /// magnitude, Kahan-compensated summation of the squares, then <c>sqrt(sum) * max</c>. Not equivalent to
    /// naive <c>Math.Sqrt(x*x + y*y)</c> in the last ulp; the vector length and distance operations in this
    /// namespace route through it.
    /// </summary>
    public static double Hypot(double x, double y)
    {
        double absX = Math.Abs(x);
        double absY = Math.Abs(y);
        if(double.IsNaN(x) || double.IsNaN(y))
        {
            return double.IsInfinity(absX) || double.IsInfinity(absY) ? double.PositiveInfinity : double.NaN;
        }

        double max = Math.Max(absX, absY);
        if(double.IsPositiveInfinity(max))
        {
            return double.PositiveInfinity;
        }

        if(max == 0)
        {
            return 0;
        }

        double sum = 0;
        double compensation = 0;
        AddScaledSquare(absX, max, ref sum, ref compensation);
        AddScaledSquare(absY, max, ref sum, ref compensation);

        return Math.Sqrt(sum) * max;
    }

    /// <summary>
    /// ECMAScript <c>Math.hypot(x, y, z)</c>, same algorithm as the two-argument form.
    /// </summary>
    public static double Hypot(double x, double y, double z)
    {
        double absX = Math.Abs(x);
        double absY = Math.Abs(y);
        double absZ = Math.Abs(z);
        if(double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z))
        {
            return double.IsInfinity(absX) || double.IsInfinity(absY) || double.IsInfinity(absZ)
                ? double.PositiveInfinity
                : double.NaN;
        }

        double max = Math.Max(Math.Max(absX, absY), absZ);
        if(double.IsPositiveInfinity(max))
        {
            return double.PositiveInfinity;
        }

        if(max == 0)
        {
            return 0;
        }

        double sum = 0;
        double compensation = 0;
        AddScaledSquare(absX, max, ref sum, ref compensation);
        AddScaledSquare(absY, max, ref sum, ref compensation);
        AddScaledSquare(absZ, max, ref sum, ref compensation);

        return Math.Sqrt(sum) * max;
    }

    /// <summary>
    /// One term of the compensated summation: <c>n = value / max; summand = n*n - compensation;
    /// preliminary = sum + summand; compensation = (preliminary - sum) - summand; sum = preliminary</c>.
    /// The operation order is load-bearing and must not be reassociated.
    /// </summary>
    private static void AddScaledSquare(double absValue, double max, ref double sum, ref double compensation)
    {
        double n = absValue / max;
        double summand = (n * n) - compensation;
        double preliminary = sum + summand;
        compensation = (preliminary - sum) - summand;
        sum = preliminary;
    }
}
