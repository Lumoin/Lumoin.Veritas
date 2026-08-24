using System;
using System.Collections.Generic;
using System.Numerics;

namespace Lumoin.Veritas.Geo.Dggs.Core;

/// <summary>
/// Cell counts and areas as pure functions of resolution — no cell id is needed. Provides
/// input-type-driven dispatch for cell counts (a <see cref="int"/> resolution returns a
/// <see cref="double"/>, a <see cref="BigInteger"/> resolution returns an exact
/// <see cref="BigInteger"/>) rather than picking one return type.
/// </summary>
internal static class CellInfo
{
    /// <summary>
    /// Number of cells at <paramref name="resolution"/>, as a <see cref="double"/>: exact for every
    /// valid resolution (0-30) — <c>60·4^(r−1) = 15·2^(2r)</c> has a 4-bit mantissa and loses no
    /// precision through r = 30, so this is never an approximation of the
    /// <see cref="GetNumCells(BigInteger)"/> overload, only a same-value alternate representation.
    /// Negative resolutions — including the world cell's −1 — have zero cells.
    /// </summary>
    public static double GetNumCells(int resolution)
    {
        if(resolution < 0)
        {
            return 0;
        }

        if(resolution == 0)
        {
            return 12;
        }

        return 60 * Math.Pow(4, resolution - 1);
    }

    /// <summary>
    /// Number of cells at <paramref name="resolution"/>, as an exact <see cref="BigInteger"/>: kept only
    /// for input-type-driven API parity with the <see cref="int"/> overload above, never a "more
    /// precise" alternative — the two are mathematically identical at every valid resolution.
    /// </summary>
    public static BigInteger GetNumCells(BigInteger resolution)
    {
        if(resolution < BigInteger.Zero)
        {
            return BigInteger.Zero;
        }

        if(resolution == BigInteger.Zero)
        {
            return new BigInteger(12);
        }

        return new BigInteger(60) * BigInteger.Pow(4, (int)(resolution - BigInteger.One));
    }

    /// <summary>
    /// Number of <paramref name="childResolution"/>-cells inside one <paramref name="parentResolution"/>-cell.
    /// </summary>
    public static double GetNumChildren(int parentResolution, int childResolution)
    {
        if(childResolution < parentResolution)
        {
            return 0;
        }

        if(childResolution == parentResolution)
        {
            return 1;
        }

        if(parentResolution >= Serialization.FirstHilbertResolution)
        {
            // Between Hilbert-range levels the aperture is constant (4), so the relation simplifies to a
            // plain power, computed as Math.Pow rather than an integer shift.
            return Math.Pow(4, childResolution - parentResolution);
        }

        // Explicit zero-check guarding against division by zero: only reachable when parentResolution
        // is negative (the world cell), where GetNumCells returns 0.
        double parentCount = GetNumCells(parentResolution);
        if(parentCount == 0)
        {
            parentCount = 1;
        }

        double childCount = GetNumCells(childResolution);

        return childCount / parentCount;
    }

    /// <summary>
    /// Area of a cell at <paramref name="resolution"/> in square meters. Negative resolutions (the
    /// world cell) return the whole authalic sphere's area directly — a distinct early return, not
    /// equivalent to dividing by <see cref="GetNumCells(int)"/> at a negative resolution (which would
    /// divide by zero) — a load-bearing branch.
    /// </summary>
    public static double CellArea(int resolution)
    {
        if(resolution < 0)
        {
            return Constants.AuthalicAreaEarth;
        }

        return Constants.AuthalicAreaEarth / GetNumCells(resolution);
    }
}
