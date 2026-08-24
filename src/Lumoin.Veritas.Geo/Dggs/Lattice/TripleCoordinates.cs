using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Geo.Dggs.Lattice;

/// <summary>
/// Conversions between the triangular-grid <see cref="Triple"/> coordinate system and the
/// pentagonal grid's <see cref="Anchor"/> / Hilbert curve index representation.
/// </summary>
internal static class TripleCoordinates
{
    /// <summary>The parity of a triple: <c>x + y + z</c>, always 0 or 1 for a valid triple.</summary>
    public static int TripleParity(Triple triple)
    {
        return triple.X + triple.Y + triple.Z;
    }

    /// <summary>Checks whether a triple lies within the valid quintant bounds for a given row limit.</summary>
    public static bool TripleInBounds(Triple triple, int maxRow)
    {
        int sum = triple.X + triple.Y + triple.Z;
        if(sum != 0 && sum != 1)
        {
            return false;
        }

        int limit = triple.Y - sum;

        return triple.X <= 0 && triple.Z <= 0 && triple.Y >= 0 && triple.Y <= maxRow && triple.X >= -limit && triple.Z >= -limit;
    }

    /// <summary>
    /// Converts triple coordinates to a Hilbert curve index, combining <see cref="TripleToAnchor"/>
    /// and <see cref="HilbertCurve.AnchorToS"/>. Returns <see langword="null"/> if the triple has
    /// invalid parity.
    /// </summary>
    public static ulong? TripleToS(Triple triple, int resolution, Orientation orientation = Orientation.UV)
    {
        Anchor? anchor = TripleToAnchor(triple, resolution, orientation);
        if(anchor is null)
        {
            return null;
        }

        return HilbertCurve.AnchorToS(anchor.Value, resolution, orientation);
    }

    /// <summary>
    /// Computes triple coordinates from an anchor. This maps the pentagonal A5 grid to a triangular
    /// grid coordinate system where neighbors differ by ±1 in exactly one coordinate while the other
    /// two stay constant.
    /// </summary>
    public static Triple AnchorToTriple(Anchor anchor)
    {
        (double shiftI, double shiftJ) = ComputeShift(anchor.Flips);

        // Compute the sub-cell center.
        double i = anchor.Offset.I + shiftI;
        double j = anchor.Offset.J + shiftJ;

        // Compute row and column in the triangular grid.
        double r = i + j - 0.5;
        double c = i - j + r;

        int x = (int)Math.Floor(((c + 1) / 2) - r);

        // Deliberately not rounded or floored, unlike x and z: r is exactly integral here by
        // construction (the shifts above are exact quarter-integer values), so a direct cast loses
        // nothing.
        int y = (int)r;

        int z = (int)Math.Floor((1 - c) / 2);

        return new Triple(x, y, z);
    }

    /// <summary>
    /// Converts triple coordinates to an <see cref="Anchor"/>, the inverse of
    /// <see cref="AnchorToTriple"/>. For the <see cref="Orientation.UV"/> and <see cref="Orientation.VU"/>
    /// orientations this uses a fast path via <see cref="HilbertCurve.IJToFlips"/>; every other
    /// orientation falls back to <see cref="HilbertCurve.IJToS"/> followed by
    /// <see cref="HilbertCurve.SToAnchor"/>, which handles all orientation transforms. Returns
    /// <see langword="null"/> if the triple has invalid parity.
    /// </summary>
    public static Anchor? TripleToAnchor(Triple triple, int resolution, Orientation orientation = Orientation.UV)
    {
        int sum = triple.X + triple.Y + triple.Z;
        if(sum != 0 && sum != 1)
        {
            return null;
        }

        double r = triple.Y;
        double cMin = Math.Max((2 * triple.X) + (2 * r) - 1, (-2 * triple.Z) - 1 + 0.0001);
        double cMax = Math.Min((2 * triple.X) + (2 * r) + 1 - 0.0001, 1 - (2 * triple.Z));
        double c = JsMath.Round((cMin + cMax) / 2);

        // Solved from the forward relationship r = centerI + centerJ - 0.5, c = centerI - centerJ + r.
        double centerI = (c + 0.5) / 2;
        double centerJ = r - (c / 2) + 0.25;

        if(orientation is Orientation.UV or Orientation.VU)
        {
            FlipPair flips = HilbertCurve.IJToFlips(new IJ(centerI, centerJ), resolution);
            (double shiftI, double shiftJ) = ComputeShift(flips);

            IJ offset = new(JsMath.Round(centerI - shiftI), JsMath.Round(centerJ - shiftJ));

            return AnchorFactory.OffsetFlipsToAnchor(offset, flips, orientation);
        }

        ulong s = HilbertCurve.IJToS(new IJ(centerI, centerJ), resolution, orientation);

        return HilbertCurve.SToAnchor(s, resolution, orientation);
    }

    /// <summary>
    /// Computes the sub-cell center shift for a flip pair, shared between <see cref="AnchorToTriple"/>
    /// and <see cref="TripleToAnchor"/>'s fast path.
    /// </summary>
    private static (double ShiftI, double ShiftJ) ComputeShift(FlipPair flips)
    {
        double shiftI = 0.25;
        double shiftJ = 0.25;

        // First check for the [No, Yes] rotation.
        if(flips.FlipX == Flip.No && flips.FlipY == Flip.Yes)
        {
            // Rotate 180 degrees.
            shiftI = -shiftI;
            shiftJ = -shiftJ;
        }

        // Then apply additional adjustments.
        if(flips.FlipX == Flip.Yes && flips.FlipY == Flip.Yes)
        {
            // Rotate 180 degrees.
            shiftI = -shiftI;
            shiftJ = -shiftJ;
        }
        else if(flips.FlipX == Flip.Yes)
        {
            // Shift left (subtract w = [0, 1]).
            shiftJ -= 1;
        }
        else if(flips.FlipY == Flip.Yes)
        {
            // Shift right (add w = [0, 1]).
            shiftJ += 1;
        }

        return (shiftI, shiftJ);
    }
}
