using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Geo.Dggs.Lattice;

/// <summary>
/// Conversions between a quaternary digit (valid range 0 to 3, selecting one of a Hilbert curve's
/// four congruent sub-triangles) and the <see cref="KJ"/> / <see cref="IJ"/> lattice coordinates it
/// corresponds to.
/// </summary>
internal static class QuaternaryConversions
{
    /// <summary>The unit <see cref="KJ"/> offset along the positive K axis.</summary>
    private static KJ KPositive { get; } = new(1, 0);

    /// <summary>The unit <see cref="KJ"/> offset along the positive J axis.</summary>
    private static KJ JPositive { get; } = new(0, 1);

    /// <summary>The unit <see cref="KJ"/> offset along the negative K axis.</summary>
    private static KJ KNegative { get; } = new(-1, 0);

    /// <summary>The unit <see cref="KJ"/> offset along the negative J axis.</summary>
    private static KJ JNegative { get; } = new(0, -1);

    /// <summary>The zero <see cref="KJ"/> offset.</summary>
    private static KJ Zero { get; } = new(0, 0);

    /// <summary>
    /// Converts a quaternary digit and its accumulated flips to the <see cref="KJ"/> offset of the
    /// corresponding sub-triangle. Composition of the axis vectors selected by <paramref name="flips"/>
    /// covers every combination the <see cref="Flip"/> type can hold; if none of the four flip-combo
    /// branches below matches (only reachable with an out-of-domain flip value), both intermediate
    /// vectors stay zero and the digit's length-1/√2/√5 scaling is applied to a zero vector, silently
    /// returning zero rather than throwing — this asymmetry with the digit-validation below is
    /// intentional.
    /// </summary>
    public static KJ QuaternaryToKJ(int quaternary, FlipPair flips)
    {
        KJ p = Zero;
        KJ q = Zero;

        if(flips.FlipX == Flip.No && flips.FlipY == Flip.No)
        {
            p = KPositive;
            q = JPositive;
        }
        else if(flips.FlipX == Flip.Yes && flips.FlipY == Flip.No)
        {
            // Swap and negate.
            p = JNegative;
            q = KNegative;
        }
        else if(flips.FlipX == Flip.No && flips.FlipY == Flip.Yes)
        {
            // Swap only.
            p = JPositive;
            q = KPositive;
        }
        else if(flips.FlipX == Flip.Yes && flips.FlipY == Flip.Yes)
        {
            // Negate only.
            p = KNegative;
            q = JNegative;
        }

        return quaternary switch
        {
            0 => Zero, // Length 0.
            1 => p, // Length 1.
            2 => new KJ(q.K + p.K, q.J + p.J), // Length √2.
            3 => new KJ(q.K + (p.K * 2), q.J + (p.J * 2)), // Length √5.
            _ => throw new ArgumentOutOfRangeException(nameof(quaternary), quaternary, "Quaternary digit must be in the range 0 to 3."),
        };
    }

    /// <summary>
    /// Returns the flip pair a quaternary digit applies to the running flip state. Unlike
    /// <see cref="QuaternaryToKJ"/>, an invalid digit here throws rather than returning a value.
    /// </summary>
    public static FlipPair QuaternaryToFlips(int quaternary)
    {
        return quaternary switch
        {
            0 => new FlipPair(Flip.No, Flip.No),
            1 => new FlipPair(Flip.No, Flip.Yes),
            2 => new FlipPair(Flip.No, Flip.No),
            3 => new FlipPair(Flip.Yes, Flip.No),
            _ => throw new ArgumentOutOfRangeException(nameof(quaternary), quaternary, "Quaternary digit must be in the range 0 to 3."),
        };
    }

    /// <summary>
    /// Converts <see cref="IJ"/> coordinates and the current flip state to the quaternary digit whose
    /// sub-triangle contains the point. Uses the <c>ij</c> basis directly, unlike its inverse
    /// <see cref="QuaternaryToKJ"/>. The boundary comparisons are strict <c>&lt;</c> / <c>&gt;</c>
    /// against exactly 1, never <c>&lt;=</c> / <c>&gt;=</c>.
    /// </summary>
    public static int IJToQuaternary(IJ ij, FlipPair flips)
    {
        double a = flips.FlipX == Flip.Yes ? -(ij.I + ij.J) : ij.I + ij.J;
        double b = flips.FlipY == Flip.Yes ? -ij.I : ij.I;
        double c = flips.FlipX == Flip.Yes ? -ij.J : ij.J;

        if((int)flips.FlipX + (int)flips.FlipY == 0)
        {
            // Only one flip.
            if(c < 1)
            {
                return 0;
            }

            if(b > 1)
            {
                return 3;
            }

            return a > 1 ? 2 : 1;
        }

        // No flips, or both.
        if(a < 1)
        {
            return 0;
        }

        if(b > 1)
        {
            return 3;
        }

        return c > 1 ? 2 : 1;
    }
}
