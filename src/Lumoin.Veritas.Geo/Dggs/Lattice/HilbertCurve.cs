using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Geo.Dggs.Lattice;

/// <summary>
/// Conversions between a Hilbert curve index (an <see cref="Anchor"/>'s <c>s</c>-value) and its
/// <see cref="Anchor"/> or <see cref="IJ"/> representation, across all six curve
/// <see cref="Orientation"/>s. <c>s</c>-values fit in 64 bits at every valid resolution (two bits per
/// resolution level, at most 60 bits at resolution 30), so they are carried as <see cref="ulong"/>
/// throughout.
/// </summary>
internal static class HilbertCurve
{
    /// <summary>Whether digit shifting runs by default.</summary>
    private const bool DefaultShiftDigitsEnabled = true;

    /// <summary>
    /// The stack-allocation size for a quaternary digit buffer. <c>s</c> at the deepest Hilbert
    /// resolution (30, <c>hilbertResolution</c> 29) occupies at most 58 bits, so base-4 digit
    /// extraction never needs more than 29 digits. <see cref="ComputeAnchorFromS"/>'s loop also
    /// guarantees at least <c>resolution</c> digits (asserted &lt;= 30 throughout this class), and 32
    /// is the exact number of base-4 digits needed to exhaust any <see cref="ulong"/> whatsoever
    /// (64 bits / 2 bits per digit), so this bound is never a clamp — every digit a growable digit
    /// list would produce still fits.
    /// </summary>
    private const int MaximumDigitCount = 32;

    /// <summary>Compensates for the offset introduced when flipping I and J: the flips moved the origin of the cell, so the offset must shift to compensate.</summary>
    private static IJ FlipShift { get; } = new(-1, 1);

    /// <summary>Tangent-plane radius, in lattice units, of the fractional-offset probes in <see cref="ProbeOffsets"/>.</summary>
    private const double ProbeRadius = 0.1;

    /// <summary>
    /// Precomputed probe offsets for AnchorToS, indexed by flip combination:
    /// [No, No] -&gt; 0, [No, Yes] -&gt; 1, [Yes, No] -&gt; 2, [Yes, Yes] -&gt; 3.
    /// Angles are the midpoints of validated ranges (resolutions 3-9, all orientations):
    ///   [No, No]:  45deg (range 1-89deg)     [No, Yes]: 113deg (range 91-134deg)
    ///   [Yes, No]: 293deg (range 271-314deg) [Yes, Yes]: 225deg (range 181-269deg)
    /// Computed from the degree literals via degrees*PI/180 at static init, not hardcoded decimals.
    /// </summary>
    private static IJ[] ProbeOffsets { get; } =
    [
        new IJ(ProbeRadius * Math.Cos(45.0 * Math.PI / 180.0), ProbeRadius * Math.Sin(45.0 * Math.PI / 180.0)),
        new IJ(ProbeRadius * Math.Cos(113.0 * Math.PI / 180.0), ProbeRadius * Math.Sin(113.0 * Math.PI / 180.0)),
        new IJ(ProbeRadius * Math.Cos(293.0 * Math.PI / 180.0), ProbeRadius * Math.Sin(293.0 * Math.PI / 180.0)),
        new IJ(ProbeRadius * Math.Cos(225.0 * Math.PI / 180.0), ProbeRadius * Math.Sin(225.0 * Math.PI / 180.0))
    ];

    /// <summary>
    /// Converts a Hilbert curve index to its <see cref="Anchor"/> at the given resolution and
    /// orientation.
    /// </summary>
    public static Anchor SToAnchor(ulong s, int resolution, Orientation orientation, bool shiftDigitsEnabled = DefaultShiftDigitsEnabled)
    {
        Debug.Assert(resolution is >= 0 and <= 30, "Hilbert curve shifts assume a resolution between 0 and 30 inclusive.");

        ulong input = s;
        bool reverse = orientation is Orientation.VU or Orientation.WU or Orientation.VW;
        bool invertJ = orientation is Orientation.WV or Orientation.VW;
        bool flipIJ = orientation is Orientation.WU or Orientation.UW;

        if(reverse)
        {
            input = (1UL << (2 * resolution)) - input - 1UL;
        }

        Anchor anchor = ComputeAnchorFromS(input, resolution, invertJ, flipIJ, shiftDigitsEnabled);

        if(flipIJ)
        {
            double offsetI = anchor.Offset.J;
            double offsetJ = anchor.Offset.I;

            // The flips moved the origin of the cell, shift to compensate.
            if(anchor.Flips.FlipX == Flip.Yes)
            {
                offsetI += FlipShift.I;
                offsetJ += FlipShift.J;
            }

            if(anchor.Flips.FlipY == Flip.Yes)
            {
                offsetI -= FlipShift.I;
                offsetJ -= FlipShift.J;
            }

            anchor = anchor with { Offset = new IJ(offsetI, offsetJ) };
        }

        if(invertJ)
        {
            double invertedJ = (1 << resolution) - (anchor.Offset.I + anchor.Offset.J);
            FlipPair invertedFlips = new((Flip)(-(int)anchor.Flips.FlipX), anchor.Flips.FlipY);
            anchor = anchor with { Offset = new IJ(anchor.Offset.I, invertedJ), Flips = invertedFlips };
        }

        return anchor;
    }

    /// <summary>
    /// Converts <see cref="IJ"/> coordinates to a Hilbert curve index at the given resolution and
    /// orientation.
    /// </summary>
    public static ulong IJToS(IJ input, int resolution, Orientation orientation = Orientation.UV, bool shiftDigitsEnabled = DefaultShiftDigitsEnabled)
    {
        Debug.Assert(resolution is >= 0 and <= 30, "Hilbert curve shifts assume a resolution between 0 and 30 inclusive.");

        bool reverse = orientation is Orientation.VU or Orientation.WU or Orientation.VW;
        bool invertJ = orientation is Orientation.WV or Orientation.VW;
        bool flipIJ = orientation is Orientation.WU or Orientation.UW;

        double i = input.I;
        double j = input.J;

        if(flipIJ)
        {
            i = input.J;
            j = input.I;
        }

        if(invertJ)
        {
            j = (1 << resolution) - (i + j);
        }

        ulong s = ComputeSFromIJ(new IJ(i, j), invertJ, flipIJ, resolution, shiftDigitsEnabled);

        if(reverse)
        {
            s = (1UL << (2 * resolution)) - s - 1UL;
        }

        return s;
    }

    /// <summary>
    /// Converts <see cref="IJ"/> coordinates to the flip pair the Hilbert curve descent to that
    /// point accumulates, without computing the full curve index. Shares its digit-descent loop with
    /// <see cref="ComputeSFromIJ"/> via <see cref="ComputeDigitsAndFlips"/>.
    /// </summary>
    public static FlipPair IJToFlips(IJ input, int resolution)
    {
        Debug.Assert(resolution is >= 0 and <= 30, "Hilbert curve shifts assume a resolution between 0 and 30 inclusive.");

        Span<int> digits = stackalloc int[MaximumDigitCount];

        return ComputeDigitsAndFlips(input, resolution, digits[..resolution]);
    }

    /// <summary>
    /// Converts an <see cref="Anchor"/> to a Hilbert curve index using a single targeted fractional
    /// offset probe. <see cref="IJToS"/> discretizes fractional offsets into Hilbert curve cells; at
    /// integer offsets (vertices of the triangular lattice) six triangular cells meet, and the flip
    /// values determine which triangular cell the anchor belongs to, allowing a single probe in the
    /// correct direction.
    /// </summary>
    public static ulong AnchorToS(Anchor anchor, int resolution, Orientation orientation = Orientation.UV)
    {
        int probeIndex = 1 - (int)anchor.Flips.FlipX + ((1 - (int)anchor.Flips.FlipY) / 2);
        IJ probeOffset = ProbeOffsets[probeIndex];
        IJ probed = new(anchor.Offset.I + probeOffset.I, anchor.Offset.J + probeOffset.J);

        return IJToS(probed, resolution, orientation);
    }

    /// <summary>
    /// Extracts quaternary digits from <paramref name="s"/> least-significant-first, then builds the
    /// <see cref="Anchor"/> by processing them most-significant-first. The digit count is growable
    /// rather than fixed to <paramref name="resolution"/>: an <paramref name="s"/>-value with more
    /// significant bits than the resolution implies still yields every digit it contains. The buffer
    /// is stack-allocated at <see cref="MaximumDigitCount"/>, the hard mathematical bound for that
    /// growth, rather than a growable heap list.
    /// </summary>
    private static Anchor ComputeAnchorFromS(ulong s, int resolution, bool invertJ, bool flipIJ, bool shiftDigitsEnabled)
    {
        ulong remaining = s;

        Span<int> digits = stackalloc int[MaximumDigitCount];
        int digitCount = 0;
        while(remaining > 0 || digitCount < resolution)
        {
            Debug.Assert(digitCount < MaximumDigitCount, "Digit extraction exceeded MaximumDigitCount, the hard bound for s's base-4 digit count.");
            digits[digitCount] = (int)(remaining % 4);
            digitCount++;
            remaining >>= 2;
        }

        Span<int> activeDigits = digits[..digitCount];
        int[] pattern = flipIJ ? DigitShifter.PatternFlipped : DigitShifter.Pattern;
        FlipPair flips = new(Flip.No, Flip.No);

        // Process digits from left to right (most significant first).
        for(int index = activeDigits.Length - 1; index >= 0; index--)
        {
            if(shiftDigitsEnabled)
            {
                DigitShifter.ShiftDigits(activeDigits, index, flips, invertJ, pattern);
            }

            flips = FlipPair.Multiply(flips, QuaternaryConversions.QuaternaryToFlips(activeDigits[index]));
        }

        double offsetK = 0;
        double offsetJ = 0;
        flips = new FlipPair(Flip.No, Flip.No); // Reset flips for the next loop.

        for(int index = activeDigits.Length - 1; index >= 0; index--)
        {
            // Scale up the existing anchor.
            offsetK *= 2;
            offsetJ *= 2;

            // Combine with the child anchor for this digit.
            KJ childOffset = QuaternaryConversions.QuaternaryToKJ(activeDigits[index], flips);
            offsetK += childOffset.K;
            offsetJ += childOffset.J;

            flips = FlipPair.Multiply(flips, QuaternaryConversions.QuaternaryToFlips(activeDigits[index]));
        }

        int q = activeDigits.Length > 0 ? activeDigits[0] : 0;
        IJ offset = LatticeBasis.KJToIJ(new KJ(offsetK, offsetJ));

        return new Anchor(q, offset, flips);
    }

    /// <summary>
    /// Descends the Hilbert curve one quaternary digit per resolution level, most-significant first,
    /// tracking the pivot offset and accumulated flips, and writing each digit into
    /// <paramref name="digits"/> (caller-supplied, length <paramref name="resolution"/>). Shared by
    /// <see cref="ComputeSFromIJ"/> (which also needs the digits afterward) and <see cref="IJToFlips"/>
    /// (which only needs the final flips), so the digit-descent loop exists in exactly one place.
    /// </summary>
    private static FlipPair ComputeDigitsAndFlips(IJ input, int resolution, Span<int> digits)
    {
        FlipPair flips = new(Flip.No, Flip.No);
        double pivotI = 0;
        double pivotJ = 0;

        // Process digits from left to right (most significant first).
        for(int index = resolution - 1; index >= 0; index--)
        {
            double relativeOffsetI = input.I - pivotI;
            double relativeOffsetJ = input.J - pivotJ;

            Debug.Assert(index <= 30, "A 1<<index shift here assumes index does not exceed 30 bits.");
            int scale = 1 << index;
            IJ scaledOffset = new(relativeOffsetI / scale, relativeOffsetJ / scale);

            int digit = QuaternaryConversions.IJToQuaternary(scaledOffset, flips);
            digits[index] = digit;

            KJ childOffsetKJ = QuaternaryConversions.QuaternaryToKJ(digit, flips);
            IJ childOffset = LatticeBasis.KJToIJ(childOffsetKJ);
            pivotI += childOffset.I * scale;
            pivotJ += childOffset.J * scale;

            flips = FlipPair.Multiply(flips, QuaternaryConversions.QuaternaryToFlips(digit));
        }

        return flips;
    }

    /// <summary>
    /// Converts <see cref="IJ"/> coordinates to a Hilbert curve index for the un-reversed, un-flipped
    /// case (the caller applies orientation transforms before and after calling this).
    /// </summary>
    private static ulong ComputeSFromIJ(IJ input, bool invertJ, bool flipIJ, int resolution, bool shiftDigitsEnabled)
    {
        Span<int> digits = stackalloc int[MaximumDigitCount];
        Span<int> activeDigits = digits[..resolution];
        FlipPair flips = ComputeDigitsAndFlips(input, resolution, activeDigits);

        int[] pattern = flipIJ ? DigitShifter.PatternFlippedReversed : DigitShifter.PatternReversed;

        for(int index = 0; index < activeDigits.Length; index++)
        {
            flips = FlipPair.Multiply(flips, QuaternaryConversions.QuaternaryToFlips(activeDigits[index]));
            if(shiftDigitsEnabled)
            {
                DigitShifter.ShiftDigits(activeDigits, index, flips, invertJ, pattern);
            }
        }

        ulong output = 0;
        for(int index = resolution - 1; index >= 0; index--)
        {
            ulong scale = 1UL << (2 * index);
            output += (ulong)activeDigits[index] * scale;
        }

        return output;
    }
}
