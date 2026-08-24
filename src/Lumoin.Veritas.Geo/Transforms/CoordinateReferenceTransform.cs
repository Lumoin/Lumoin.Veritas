using System;

namespace Lumoin.Veritas.Geo.Transforms;

/// <summary>
/// The coordinate-operation surface over the closed
/// <see cref="CoordinateReferenceSystem"/> roster: transforms interleaved 2D
/// coordinate spans between any ordered pair of recognized systems, or
/// refuses by value. Every ordered pair over the roster is supported —
/// recognition implies certification — so refusals are always about the
/// identifiers or the coordinates, never about the pair.
/// </summary>
/// <remarks>
/// <para>
/// <b>Refusals are values, exceptions are caller contract.</b> Unrecognized
/// systems, non-finite coordinates, and out-of-domain coordinates refuse by
/// value through <see cref="CoordinateTransformRefusal"/>; only span-shape
/// violations (odd source length, short destination) throw, exactly as the
/// underlying kernels do.
/// </para>
/// <para>
/// <b>The destination is written only after the whole span validates.</b>
/// Validation runs to completion before the first destination write, so a
/// refused call leaves the destination bit-unchanged — an in-place caller
/// keeps its operand. The refusal names the first offending double in
/// flattened index order; at each element, finiteness is checked before the
/// source domain, and the source domain before target representability.
/// Identifier checks precede all coordinate checks, source before target,
/// and fire on empty spans too.
/// </para>
/// <para>
/// <b>No fabrication anywhere.</b> Latitudes poleward of the Web Mercator
/// limit refuse rather than clamp; out-of-range longitudes refuse rather
/// than wrap; and the Web Mercator to geographic direction refuses the
/// ulp-scale boundary coordinates whose computed image the reverse
/// operation would not accept back — every geographic coordinate this
/// surface emits is a valid input to the geographic-to-Web-Mercator leg.
/// The converse does not hold at the projection square's edge: the
/// geographic-to-Web-Mercator leg accepts its declared boundary (±180°
/// longitude, ±the limit latitude) and may emit exactly ±π·R, which the
/// return leg then refuses — round-tripping is not total at the roster's
/// edges, and which edges bite is a per-sign fact of the platform's
/// arithmetic, pinned by test.
/// </para>
/// <para>
/// <b>Aliasing.</b> The destination may alias the source only when the two
/// spans are identical — same start, same length. Partial or offset overlap
/// is unsupported and its result is unspecified.
/// </para>
/// </remarks>
public static class CoordinateReferenceTransform
{
    /// <summary>Half the Web Mercator world extent in metres, π · R; the projection square spans ±this value on both axes.</summary>
    private const double HalfWorldMeters = Math.PI * ScalarWgs84ToWebMercator.EarthRadiusMeters;

    /// <summary>The exact scalar forward kernel — the exact scalar reference the selection's default is pinned to.</summary>
    private static CoordinateTransformKernel ForwardKernel { get; } = CoordinateTransformKernelSelection.Scalar;

    /// <summary>The exact scalar inverse kernel.</summary>
    private static CoordinateTransformKernel InverseKernel { get; } = ScalarWebMercatorToWgs84.GetTransform();

    /// <summary>
    /// Transforms interleaved 2D coordinates from <paramref name="source"/>
    /// to <paramref name="target"/>, writing into
    /// <paramref name="destination"/>, or refuses by value. Spans are read
    /// and written in each system's declared axis order. Returns
    /// <see langword="true"/> with <see cref="CoordinateTransformRefusal.None"/>
    /// on success; returns <see langword="false"/> with the reason and first
    /// offending element index on refusal, leaving the destination
    /// untouched.
    /// </summary>
    /// <param name="source">The source coordinate reference system.</param>
    /// <param name="target">The target coordinate reference system.</param>
    /// <param name="sourceCoordinates">Interleaved source coordinates in the source system's declared axis order.</param>
    /// <param name="destination">
    /// Receives the interleaved transformed coordinates in the target
    /// system's declared axis order; may alias
    /// <paramref name="sourceCoordinates"/> only when the two spans are
    /// identical.
    /// </param>
    /// <param name="refusal">The typed refusal, or <see cref="CoordinateTransformRefusal.None"/> on success.</param>
    /// <returns><see langword="true"/> when the whole span transformed; <see langword="false"/> on refusal.</returns>
    /// <exception cref="ArgumentException">
    /// The source span length is odd, or the destination is shorter than the
    /// source — caller contract violations, never domain refusals.
    /// </exception>
    public static bool TryTransform(
        CoordinateReferenceSystem source,
        CoordinateReferenceSystem target,
        ReadOnlySpan<double> sourceCoordinates,
        Span<double> destination,
        out CoordinateTransformRefusal refusal)
    {
        if(sourceCoordinates.Length % 2 != 0)
        {
            throw new ArgumentException(
                "Source span length must be even (interleaved coordinate pairs).",
                nameof(sourceCoordinates));
        }

        if(destination.Length < sourceCoordinates.Length)
        {
            throw new ArgumentException(
                $"Destination span ({destination.Length}) is shorter than source ({sourceCoordinates.Length}).",
                nameof(destination));
        }

        if(source.Kind == CoordinateReferenceSystemKind.Unspecified)
        {
            refusal = new CoordinateTransformRefusal(CoordinateTransformRefusalKind.SourceCrsUnrecognized, -1);

            return false;
        }

        if(target.Kind == CoordinateReferenceSystemKind.Unspecified)
        {
            refusal = new CoordinateTransformRefusal(CoordinateTransformRefusalKind.TargetCrsUnrecognized, -1);

            return false;
        }

        if(!TryValidate(source, target, sourceCoordinates, out refusal))
        {
            return false;
        }

        Execute(source, target, sourceCoordinates, destination);
        refusal = CoordinateTransformRefusal.None;

        return true;
    }

    /// <summary>
    /// The complete validation pass: walks the flattened element indices in
    /// increasing order and reports the first offending double, checking
    /// finiteness, then the source domain, then target representability at
    /// each element. Never writes anywhere — the transform pass runs only
    /// after this pass accepts the whole span.
    /// </summary>
    /// <param name="source">The source coordinate reference system.</param>
    /// <param name="target">The target coordinate reference system.</param>
    /// <param name="sourceCoordinates">Interleaved source coordinates in the source system's declared axis order.</param>
    /// <param name="refusal">The first refusal, or <see cref="CoordinateTransformRefusal.None"/>.</param>
    /// <returns><see langword="true"/> when every element validates.</returns>
    private static bool TryValidate(
        CoordinateReferenceSystem source,
        CoordinateReferenceSystem target,
        ReadOnlySpan<double> sourceCoordinates,
        out CoordinateTransformRefusal refusal)
    {
        bool sourceIsGeographic = source.Kind != CoordinateReferenceSystemKind.WebMercator;
        bool targetIsGeographic = target.Kind != CoordinateReferenceSystemKind.WebMercator;
        bool latitudeFirst = source.AxisOrder == CoordinateAxisOrder.LatitudeLongitude;

        for(int index = 0; index < sourceCoordinates.Length; index++)
        {
            double value = sourceCoordinates[index];

            if(!double.IsFinite(value))
            {
                refusal = new CoordinateTransformRefusal(CoordinateTransformRefusalKind.NonFiniteCoordinate, index);

                return false;
            }

            if(sourceIsGeographic)
            {
                bool isLatitude = latitudeFirst == (index % 2 == 0);
                double bound = isLatitude ? 90.0 : 180.0;

                if(value > bound || value < -bound)
                {
                    refusal = new CoordinateTransformRefusal(CoordinateTransformRefusalKind.CoordinateOutsideSourceDomain, index);

                    return false;
                }

                if(isLatitude && !targetIsGeographic
                    && (value > ScalarWgs84ToWebMercator.MercatorLatitudeLimitDegrees
                        || value < -ScalarWgs84ToWebMercator.MercatorLatitudeLimitDegrees))
                {
                    refusal = new CoordinateTransformRefusal(CoordinateTransformRefusalKind.CoordinateOutsideTargetDomain, index);

                    return false;
                }
            }
            else
            {
                if(value > HalfWorldMeters || value < -HalfWorldMeters)
                {
                    refusal = new CoordinateTransformRefusal(CoordinateTransformRefusalKind.CoordinateOutsideSourceDomain, index);

                    return false;
                }

                if(targetIsGeographic && !ImageIsReacceptable(index, value))
                {
                    refusal = new CoordinateTransformRefusal(CoordinateTransformRefusalKind.CoordinateOutsideTargetDomain, index);

                    return false;
                }
            }
        }

        refusal = CoordinateTransformRefusal.None;

        return true;
    }

    /// <summary>
    /// The image-validity rule for the Web Mercator to geographic direction:
    /// the geographic image the inverse kernel would actually emit for a
    /// source element must itself be accepted back by the reverse
    /// geographic-to-Web-Mercator operation. The image is computed by
    /// invoking the same kernel delegate the transform pass runs — never a
    /// second transcription of the formula — so the accept/reject decision
    /// and the emitted value cannot drift apart. Only ulp-scale slivers at
    /// the projection square's edge fail this — the abscissa at exactly
    /// ±π·R inverts a hair beyond ±180°, and the topmost representable
    /// ordinates invert a hair poleward of the Mercator latitude limit;
    /// both bands are environment-measured facts of the platform's
    /// arithmetic, not design choices.
    /// </summary>
    /// <param name="index">The flattened element index; even = abscissa, odd = ordinate.</param>
    /// <param name="value">The Web Mercator coordinate under test, in metres.</param>
    /// <returns><see langword="true"/> when the computed image is re-accepted by the forward leg.</returns>
    private static bool ImageIsReacceptable(int index, double value)
    {
        Span<double> scratch = stackalloc double[2];
        bool isAbscissa = index % 2 == 0;

        scratch[0] = isAbscissa ? value : 0.0;
        scratch[1] = isAbscissa ? 0.0 : value;
        InverseKernel(scratch, scratch);

        if(isAbscissa)
        {
            return scratch[0] <= 180.0 && scratch[0] >= -180.0;
        }

        return scratch[1] <= ScalarWgs84ToWebMercator.MercatorLatitudeLimitDegrees
            && scratch[1] >= -ScalarWgs84ToWebMercator.MercatorLatitudeLimitDegrees;
    }

    /// <summary>
    /// The transform pass over an already-validated span: identity copy,
    /// per-point swap (both ordinates read into locals before either write,
    /// which is what makes identical-span in-place calls correct), or the
    /// projection legs with the axis reorder on the geographic side —
    /// reorder then project toward Web Mercator, unproject then reorder from
    /// it.
    /// </summary>
    /// <param name="source">The source coordinate reference system.</param>
    /// <param name="target">The target coordinate reference system.</param>
    /// <param name="sourceCoordinates">Interleaved, already-validated source coordinates.</param>
    /// <param name="destination">Receives the transformed coordinates; at least as long as the source.</param>
    private static void Execute(
        CoordinateReferenceSystem source,
        CoordinateReferenceSystem target,
        ReadOnlySpan<double> sourceCoordinates,
        Span<double> destination)
    {
        Span<double> written = destination[..sourceCoordinates.Length];

        if(source.Kind == target.Kind)
        {
            sourceCoordinates.CopyTo(written);

            return;
        }

        bool sourceIsGeographic = source.Kind != CoordinateReferenceSystemKind.WebMercator;
        bool targetIsGeographic = target.Kind != CoordinateReferenceSystemKind.WebMercator;

        if(sourceIsGeographic && targetIsGeographic)
        {
            SwapPairs(sourceCoordinates, written);

            return;
        }

        if(sourceIsGeographic)
        {
            if(source.AxisOrder == CoordinateAxisOrder.LatitudeLongitude)
            {
                SwapPairs(sourceCoordinates, written);
                ForwardKernel(written, written);

                return;
            }

            ForwardKernel(sourceCoordinates, written);

            return;
        }

        InverseKernel(sourceCoordinates, written);

        if(target.AxisOrder == CoordinateAxisOrder.LatitudeLongitude)
        {
            SwapPairs(written, written);
        }
    }

    /// <summary>
    /// Swaps each interleaved pair's ordinates. Both source elements are
    /// read into locals before either destination element is written, so the
    /// swap is correct when the destination is the identical span.
    /// </summary>
    /// <param name="sourcePairs">The interleaved source pairs.</param>
    /// <param name="destinationPairs">Receives the swapped pairs; may be the identical span.</param>
    private static void SwapPairs(ReadOnlySpan<double> sourcePairs, Span<double> destinationPairs)
    {
        for(int index = 0; index < sourcePairs.Length; index += 2)
        {
            double first = sourcePairs[index];
            double second = sourcePairs[index + 1];

            destinationPairs[index] = second;
            destinationPairs[index + 1] = first;
        }
    }
}
