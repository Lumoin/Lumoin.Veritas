using System;

namespace Lumoin.Veritas.Geo.Spatial;

/// <summary>
/// The committed cross-adversary generator, shared by the unit tests and the
/// measurement harness: interleaved full-height vertical and full-width
/// horizontal slats over the square field [0, extent]², every slat centred on
/// the field centre. Both packings key on centres and every centre coincides,
/// so every packing key ties, leaf order falls entirely to the registration
/// tie-break, and the union over any child run that holds one slat of each
/// orientation is the full field — the shape that defeats the union bound in
/// every query mode while each stored box stays thin. Probe placement is part
/// of the committed protocol rather than an afterthought: an off-arm probe
/// lies strictly inside one of the four corner interstices the arms never
/// reach, so it has zero candidates in every mode by construction (the
/// interstice margin keeps the closed-interval algebra from ever counting a
/// touch), while an on-arm probe sits on a shared arm line, where candidate
/// counts grow with the slat count — valid evidence only through a
/// visits-minus-candidates reading, never through raw visits.
/// </summary>
internal static class CrossSlatFixture
{
    /// <summary>The fractional part of the golden ratio, the first lattice stride; with <see cref="PlasticNumberStride"/> it spreads probe positions without repeating along either axis.</summary>
    private const double GoldenRatioStride = 0.61803398874989485d;

    /// <summary>The reciprocal of the plastic number, the second lattice stride, incommensurate with <see cref="GoldenRatioStride"/>.</summary>
    private const double PlasticNumberStride = 0.75487766624669276d;

    /// <summary>
    /// The widest arm half-width: one eighth of the field, which leaves each
    /// corner interstice three eighths of the field on a side. Every slat the
    /// generator writes stays within this half-width of the centre line, so
    /// staying clear of it is what makes a probe off-arm.
    /// </summary>
    /// <param name="fieldExtent">The square field's side length.</param>
    /// <returns>The widest arm half-width.</returns>
    internal static double ArmHalfWidthMaximum(double fieldExtent)
    {
        return fieldExtent / 8d;
    }

    /// <summary>
    /// Writes one slat per destination element over the
    /// [0, <paramref name="fieldExtent"/>]² field. Even slat indices are
    /// full-height vertical slats, odd ones full-width horizontal; the pair
    /// index (slat divided by two) interpolates the half-width linearly from
    /// half of <paramref name="thickness"/> up to
    /// <see cref="ArmHalfWidthMaximum"/>, so same-orientation slats are
    /// pairwise distinct while every centre stays on the field centre.
    /// </summary>
    /// <param name="destination">The span that receives one slat per element.</param>
    /// <param name="fieldExtent">The square field's side length.</param>
    /// <param name="thickness">The thinnest slat's full width; must stay strictly below a quarter of the field.</param>
    internal static void WriteSlats(Span<BoundingBox> destination, double fieldExtent, double thickness)
    {
        GuardFieldExtent(fieldExtent);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thickness);

        if(!double.IsFinite(thickness))
        {
            throw new ArgumentOutOfRangeException(nameof(thickness), thickness, "The slat thickness must be finite.");
        }

        //Strictly below a quarter field: at exactly a quarter the interpolation range
        //collapses and same-orientation slats would duplicate, contradicting the documented
        //pairwise distinctness.
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(thickness, fieldExtent / 4d);

        int slatCount = destination.Length;
        int pairCount = (slatCount + 1) / 2;
        double center = fieldExtent / 2d;
        double halfWidthMinimum = thickness / 2d;
        double halfWidthStep = pairCount > 1
            ? (ArmHalfWidthMaximum(fieldExtent) - halfWidthMinimum) / (pairCount - 1)
            : 0d;

        for(int slat = 0; slat < slatCount; slat++)
        {
            double halfWidth = halfWidthMinimum + (halfWidthStep * (slat / 2));

            destination[slat] = slat % 2 == 0
                ? new BoundingBox(center - halfWidth, 0d, center + halfWidth, fieldExtent)
                : new BoundingBox(0d, center - halfWidth, fieldExtent, center + halfWidth);
        }
    }

    /// <summary>
    /// Writes one off-arm probe per destination element. The corner
    /// interstice cycles by probe index modulo four, the extent class by
    /// probe index modulo three — a point, then three percent, then fourteen
    /// percent of the field, the same index convention every probe builder in
    /// the measurement harness uses — and positions spread over a
    /// two-stride low-discrepancy lattice advanced once per twelve probes,
    /// when both cycles have completed. Every probe box keeps a margin of one
    /// sixty-fourth of the field between itself and the widest possible arm,
    /// so no probe ever intersects, contains, or is contained by any slat:
    /// every mode answers the empty set on these probes, by construction.
    /// </summary>
    /// <param name="destination">The span that receives one probe per element.</param>
    /// <param name="fieldExtent">The square field's side length.</param>
    internal static void WriteOffArmProbes(Span<BoundingBox> destination, double fieldExtent)
    {
        GuardFieldExtent(fieldExtent);

        double interstitialSide = (fieldExtent / 2d) - ArmHalfWidthMaximum(fieldExtent);
        double margin = fieldExtent / 64d;

        for(int probe = 0; probe < destination.Length; probe++)
        {
            int quadrant = probe % 4;
            double extent = ProbeExtent(probe, fieldExtent);
            double originX = quadrant % 2 == 0 ? 0d : fieldExtent - interstitialSide;
            double originY = quadrant < 2 ? 0d : fieldExtent - interstitialSide;
            double usable = interstitialSide - (2d * margin) - extent;
            int lattice = probe / 12;
            double x = originX + margin + (Fraction(lattice * GoldenRatioStride) * usable);
            double y = originY + margin + (Fraction(lattice * PlasticNumberStride) * usable);
            destination[probe] = new BoundingBox(x, y, x + extent, y + extent);
        }
    }

    /// <summary>
    /// Writes one on-arm probe per destination element: the probe straddles
    /// the vertical arm line on even indices and the horizontal one on odd,
    /// the extent class cycles by probe index modulo three as everywhere, and
    /// the position along the arm advances on the golden-ratio lattice once
    /// per six probes, when both cycles have completed. A point probe lands
    /// exactly on its arm line and is contained by every slat of that
    /// orientation, so candidate counts on these probes scale with the slat
    /// count by design.
    /// </summary>
    /// <param name="destination">The span that receives one probe per element.</param>
    /// <param name="fieldExtent">The square field's side length.</param>
    internal static void WriteOnArmProbes(Span<BoundingBox> destination, double fieldExtent)
    {
        GuardFieldExtent(fieldExtent);

        double center = fieldExtent / 2d;
        double margin = fieldExtent / 64d;

        for(int probe = 0; probe < destination.Length; probe++)
        {
            double extent = ProbeExtent(probe, fieldExtent);
            double along = margin + (Fraction((probe / 6) * GoldenRatioStride) * (fieldExtent - (2d * margin) - extent));

            destination[probe] = probe % 2 == 0
                ? new BoundingBox(center - (extent / 2d), along, center + (extent / 2d), along + extent)
                : new BoundingBox(along, center - (extent / 2d), along + extent, center + (extent / 2d));
        }
    }

    /// <summary>The probe extent classes by index: a point, three percent of the field, fourteen percent of the field.</summary>
    /// <param name="probe">The probe index; the extent class is its value modulo three.</param>
    /// <param name="fieldExtent">The square field's side length.</param>
    /// <returns>The probe's extent.</returns>
    private static double ProbeExtent(int probe, double fieldExtent)
    {
        return (probe % 3) switch
        {
            0 => 0d,
            1 => fieldExtent * 0.03d,
            _ => fieldExtent * 0.14d
        };
    }

    /// <summary>The fractional part of a non-negative lattice position.</summary>
    /// <param name="value">The lattice position.</param>
    /// <returns>The fractional part.</returns>
    private static double Fraction(double value)
    {
        return value - Math.Floor(value);
    }

    /// <summary>The shared field guard: the extent must be a positive finite number.</summary>
    /// <param name="fieldExtent">The square field's side length.</param>
    private static void GuardFieldExtent(double fieldExtent)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fieldExtent);

        if(!double.IsFinite(fieldExtent))
        {
            throw new ArgumentOutOfRangeException(nameof(fieldExtent), fieldExtent, "The field extent must be finite.");
        }
    }
}
