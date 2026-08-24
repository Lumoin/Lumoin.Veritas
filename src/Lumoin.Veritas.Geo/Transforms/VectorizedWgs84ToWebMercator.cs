using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Lumoin.Veritas.Geo.Transforms;

/// <summary>
/// Vectorized WGS 84 (EPSG:4326) → Web Mercator (EPSG:3857) projection: the
/// same closed form as <see cref="ScalarWgs84ToWebMercator"/>, computed over
/// <see cref="Vector{T}"/> of <see cref="double"/> so a whole batch of
/// coordinates projects per instruction. Offered as a caller-selectable
/// alternative to the scalar kernel through
/// <see cref="CoordinateTransformKernelSelection"/> — the library user picks
/// exact-scalar or fast-vectorized per their accuracy and throughput needs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why portable <see cref="Vector{T}"/> rather than per-ISA intrinsics.</b>
/// The point-to-cell kernels hand-write per-ISA backends because their hot
/// instructions differ by instruction set. This projection uses only
/// multiply, add, FMA, and a few integer bit operations on doubles —
/// operations the JIT lowers optimally from <see cref="Vector{T}"/> to
/// AVX2, AVX-512, AArch64 NEON, <em>and</em> WebAssembly packed SIMD with
/// no per-ISA source. One implementation is therefore both the most
/// portable and the WASM-SIMD path the deployment targets care about;
/// <see cref="Vector.IsHardwareAccelerated"/> reports whether the host
/// lowered it to real SIMD or to a scalar shim.
/// </para>
/// <para>
/// <b>Both halves vectorized.</b> The longitude→x half is a single multiply
/// and exact. The latitude→y half is the transcendental
/// <c>R·ln(tan(π/4 + φ/2))</c>, computed here through the equivalent
/// <c>R·atanh(sin φ) = R·½·ln((1 + sin φ)/(1 − sin φ))</c>: a vectorized
/// <see cref="Sin"/> (degree-17 Taylor, exact to ~1e-14 over the clamped
/// ±85.05° domain that lies inside <c>[−π/2, π/2]</c>) feeds a vectorized
/// <see cref="Log"/> (mantissa-reduced atanh series). The <c>atanh(sin φ)</c>
/// form is chosen over <c>asinh(tan φ)</c> because <c>sin</c> is flat near
/// the pole clamp where <c>tan</c> diverges, so the input transcendental
/// stays well-conditioned and only Mercator's intrinsic vertical stretch
/// remains.
/// </para>
/// <para>
/// <b>Accuracy versus the scalar reference.</b> Agreement is within roughly
/// a micrometre away from the poles and stays below a millimetre at the
/// ±85.05° clamp, where Mercator's stretch amplifies the residual
/// <c>sin</c> error. For bit-exact answers right at the clamp the exact
/// scalar kernel remains the reference. The agreement sweep pins this
/// envelope.
/// </para>
/// <para>
/// <b>Lane layout.</b> Input is interleaved <c>(lon, lat, lon, lat, …)</c>,
/// so even lanes are longitudes and odd lanes latitudes for any even vector
/// width. Both the x-form and the y-form run on every lane and a precomputed
/// even/odd mask selects the right result per lane — cheaper than
/// deinterleaving, and the y-form evaluated on a longitude is simply
/// discarded. Lengths below one vector width and the trailing remainder are
/// delegated to the scalar kernel.
/// </para>
/// </remarks>
internal static class VectorizedWgs84ToWebMercator
{
    /// <summary>Degrees-to-radians conversion factor, <c>π / 180</c>.</summary>
    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>Longitude→x is linear: x = R · lon · π/180. Folded to one factor.</summary>
    private const double LongitudeToMeters = ScalarWgs84ToWebMercator.EarthRadiusMeters * DegreesToRadians;

    /// <summary>The poleward clamp limit, read from the scalar kernel so both kernels clamp identically.</summary>
    private const double LatitudeLimitDegrees = ScalarWgs84ToWebMercator.MercatorLatitudeLimitDegrees;

    /// <summary>Half the WGS 84 equatorial radius — the <c>R·½</c> factor of the atanh form.</summary>
    private const double HalfEarthRadiusMeters = ScalarWgs84ToWebMercator.EarthRadiusMeters * 0.5;

    /// <summary>√2, the mantissa-reduction threshold of <see cref="Log"/>.</summary>
    private const double Sqrt2 = 1.4142135623730951;

    /// <summary>ln 2, the per-exponent-bit contribution recombined in <see cref="Log"/>.</summary>
    private const double Ln2 = 0.6931471805599453;

    /// <summary>The exact scalar kernel: the sub-width, remainder, and no-SIMD fallback.</summary>
    private static CoordinateTransformKernel Scalar { get; } = ScalarWgs84ToWebMercator.GetTransform();

    /// <summary>Even lanes (longitudes) all-bits, odd lanes (latitudes) zero; selects x over y per lane.</summary>
    private static Vector<double> EvenLaneSelectMask { get; } = BuildEvenLaneMask();

    /// <summary>Returns the vectorized transform as a <see cref="CoordinateTransformKernel"/> delegate.</summary>
    /// <returns>The kernel delegate.</returns>
    public static CoordinateTransformKernel GetTransform() => Transform;

    /// <summary>
    /// Transforms interleaved geographic (longitude, latitude) pairs, in
    /// degrees, to interleaved Web Mercator (x, y) pairs, in metres, a
    /// vector width per iteration.
    /// </summary>
    /// <param name="sourceLongitudeLatitude">Interleaved source (lon0, lat0, lon1, lat1, …) in degrees.</param>
    /// <param name="destinationXY">
    /// Interleaved destination (x0, y0, x1, y1, …) in metres; may alias
    /// <paramref name="sourceLongitudeLatitude"/> only when the two spans
    /// are identical.
    /// </param>
    private static void Transform(
        ReadOnlySpan<double> sourceLongitudeLatitude,
        Span<double> destinationXY)
    {
        if(sourceLongitudeLatitude.Length % 2 != 0)
        {
            throw new ArgumentException(
                "Source span length must be even (interleaved longitude/latitude pairs).",
                nameof(sourceLongitudeLatitude));
        }

        if(destinationXY.Length < sourceLongitudeLatitude.Length)
        {
            throw new ArgumentException(
                $"Destination span ({destinationXY.Length}) is shorter than source ({sourceLongitudeLatitude.Length}).",
                nameof(destinationXY));
        }

        int width = Vector<double>.Count;

        //No SIMD lowering, or too little data to fill one vector: the scalar kernel is the whole answer.
        if(!Vector.IsHardwareAccelerated || sourceLongitudeLatitude.Length < width)
        {
            Scalar(sourceLongitudeLatitude, destinationXY);

            return;
        }

        var longitudeFactor = new Vector<double>(LongitudeToMeters);
        var degreesToRadians = new Vector<double>(DegreesToRadians);
        var positiveLimit = new Vector<double>(LatitudeLimitDegrees);
        var negativeLimit = new Vector<double>(-LatitudeLimitDegrees);
        var one = Vector<double>.One;
        var halfRadius = new Vector<double>(HalfEarthRadiusMeters);

        int index = 0;

        for(; index + width <= sourceLongitudeLatitude.Length; index += width)
        {
            var lane = new Vector<double>(sourceLongitudeLatitude.Slice(index, width));

            //x on every lane; valid where the lane is a longitude.
            Vector<double> projectedX = lane * longitudeFactor;

            //y on every lane; valid where the lane is a latitude. Clamp first so the
            //transcendental stays inside its accurate domain even for the discarded lon lanes.
            Vector<double> clampedDegrees = Vector.Min(Vector.Max(lane, negativeLimit), positiveLimit);
            Vector<double> radians = clampedDegrees * degreesToRadians;
            Vector<double> sine = Sin(radians);
            Vector<double> ratio = (one + sine) / (one - sine);
            Vector<double> projectedY = halfRadius * Log(ratio);

            Vector<double> result = Vector.ConditionalSelect(EvenLaneSelectMask, projectedX, projectedY);
            result.CopyTo(destinationXY.Slice(index, width));
        }

        //Even-length remainder (source length and width are both even) goes to the scalar kernel.
        if(index < sourceLongitudeLatitude.Length)
        {
            Scalar(sourceLongitudeLatitude[index..], destinationXY[index..]);
        }
    }

    /// <summary>
    /// Vectorized sine for arguments in <c>[−π/2, π/2]</c>: a degree-17 odd
    /// Taylor polynomial, whose remainder is below <c>2e-14</c> across the
    /// clamped Mercator latitude domain. Horner form in <c>x²</c>.
    /// </summary>
    /// <param name="x">The argument vector, in radians.</param>
    /// <returns>The per-lane sine.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<double> Sin(Vector<double> x)
    {
        Vector<double> x2 = x * x;

        //Coefficients are (-1)^k / (2k+1)! for the x^(2k+1) terms, applied highest-order first.
        Vector<double> series = new Vector<double>(1.0 / 355687428096000.0);                 //+x^17 / 17!
        series = (series * x2) - new Vector<double>(1.0 / 1307674368000.0);                  //-x^15 / 15!
        series = (series * x2) + new Vector<double>(1.0 / 6227020800.0);                     //+x^13 / 13!
        series = (series * x2) - new Vector<double>(1.0 / 39916800.0);                       //-x^11 / 11!
        series = (series * x2) + new Vector<double>(1.0 / 362880.0);                         //+x^9 / 9!
        series = (series * x2) - new Vector<double>(1.0 / 5040.0);                           //-x^7 / 7!
        series = (series * x2) + new Vector<double>(1.0 / 120.0);                            //+x^5 / 5!
        series = (series * x2) - new Vector<double>(1.0 / 6.0);                              //-x^3 / 3!
        series = (series * x2) + Vector<double>.One;                                         //+x^1

        return x * series;
    }

    /// <summary>
    /// Vectorized natural logarithm for positive arguments. Decomposes
    /// <c>x = m · 2^e</c> by bit manipulation, reduces the mantissa to
    /// <c>[√½, √2)</c>, and evaluates <c>ln(m) = 2·atanh(w)</c> with
    /// <c>w = (m−1)/(m+1)</c> through a degree-19 odd series; <c>|w| ≤ 0.172</c>
    /// over the reduced range, so the series reaches full double precision.
    /// </summary>
    /// <param name="x">The argument vector; every lane must be positive and finite.</param>
    /// <returns>The per-lane natural logarithm.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<double> Log(Vector<double> x)
    {
        Vector<long> bits = Vector.AsVectorInt64(x);

        //Unbiased exponent e and the mantissa reassembled into [1, 2).
        Vector<long> exponentField = Vector.ShiftRightLogical(bits, 52) & new Vector<long>(0x7FF);
        Vector<double> exponent = Vector.ConvertToDouble(exponentField - new Vector<long>(1023));

        Vector<long> mantissaBits = (bits & new Vector<long>(0x000FFFFFFFFFFFFF)) | new Vector<long>(0x3FF0000000000000);
        Vector<double> mantissa = Vector.AsVectorDouble(mantissaBits);

        //Reduce [1, 2) to [√½, √2): values at or above √2 halve and carry one into the exponent.
        Vector<double> needsHalving = Vector.AsVectorDouble(Vector.GreaterThanOrEqual(mantissa, new Vector<double>(Sqrt2)));
        mantissa = Vector.ConditionalSelect(needsHalving, mantissa * new Vector<double>(0.5), mantissa);
        exponent += Vector.ConditionalSelect(needsHalving, Vector<double>.One, Vector<double>.Zero);

        Vector<double> one = Vector<double>.One;
        Vector<double> w = (mantissa - one) / (mantissa + one);
        Vector<double> w2 = w * w;

        //ln(m) = 2·(w + w³/3 + w⁵/5 + … + w^19/19); Horner in w² from the highest term.
        Vector<double> series = new Vector<double>(1.0 / 19.0);
        series = (series * w2) + new Vector<double>(1.0 / 17.0);
        series = (series * w2) + new Vector<double>(1.0 / 15.0);
        series = (series * w2) + new Vector<double>(1.0 / 13.0);
        series = (series * w2) + new Vector<double>(1.0 / 11.0);
        series = (series * w2) + new Vector<double>(1.0 / 9.0);
        series = (series * w2) + new Vector<double>(1.0 / 7.0);
        series = (series * w2) + new Vector<double>(1.0 / 5.0);
        series = (series * w2) + new Vector<double>(1.0 / 3.0);
        series = (series * w2) + one;

        Vector<double> logMantissa = (w + w) * series;

        return logMantissa + (exponent * new Vector<double>(Ln2));
    }

    /// <summary>Builds the lane mask that is all-bits on even (longitude) lanes and zero on odd (latitude) lanes.</summary>
    /// <returns>The lane-select mask.</returns>
    private static Vector<double> BuildEvenLaneMask()
    {
        Span<long> lanes = stackalloc long[Vector<long>.Count];

        for(int index = 0; index < lanes.Length; index++)
        {
            lanes[index] = (index % 2 == 0) ? -1L : 0L;
        }

        return Vector.AsVectorDouble(new Vector<long>(lanes));
    }
}
