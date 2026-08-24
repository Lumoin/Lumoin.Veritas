using System;
using System.Runtime.CompilerServices;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The error-free transforms and expansion operations the folder's exact-sign
/// predicates ride on: every operation returns results whose components sum to
/// the mathematically exact value, so a sign read from the dominant component
/// is the exact sign. Only the bounded subset the predicates need lives here:
/// two-term transforms, zero-eliminating expansion summation, and — for the
/// incircle predicate's degree-four tail — expansion scaling and expansion
/// multiplication with the capacity algebra callers size their storage by.
/// The orientation-class callers fit in small stack allocations; the incircle
/// tail's large buffers ride <see cref="InCircleScratch"/>'s plain heap
/// arrays, never a pool.
/// </summary>
internal static class ExpansionArithmetic
{
    /// <summary>Components needed for <see cref="Sum"/> over expansions of the given lengths.</summary>
    /// <param name="eLength">The first expansion's component count.</param>
    /// <param name="fLength">The second expansion's component count.</param>
    /// <returns>The required result capacity.</returns>
    public static int SumCapacity(int eLength, int fLength) => eLength + fLength;

    /// <summary>Components needed for <see cref="Scale"/> over an expansion of the given length.</summary>
    /// <param name="eLength">The scaled expansion's component count.</param>
    /// <returns>The required result capacity.</returns>
    public static int ScaleCapacity(int eLength) => 2 * eLength;

    /// <summary>Components needed for the <see cref="Product"/> result over expansions of the given lengths.</summary>
    /// <param name="eLength">The first expansion's component count.</param>
    /// <param name="fLength">The second expansion's component count.</param>
    /// <returns>The required result capacity.</returns>
    public static int ProductCapacity(int eLength, int fLength) => 2 * eLength * fLength;

    /// <summary>
    /// Scratch components <see cref="Product"/> needs alongside its result:
    /// one <see cref="Scale"/> partial plus one accumulation buffer the
    /// running sum ping-pongs through.
    /// </summary>
    /// <param name="eLength">The first expansion's component count.</param>
    /// <param name="fLength">The second expansion's component count.</param>
    /// <returns>The required scratch capacity.</returns>
    public static int ProductScratchCapacity(int eLength, int fLength) => ScaleCapacity(eLength) + ProductCapacity(eLength, fLength);

    /// <summary>
    /// The Veltkamp splitter, <c>2^27 + 1</c> for IEEE 754 binary64: splits a
    /// double into two 26-bit halves whose products are exact.
    /// </summary>
    public const double Splitter = 134217729.0;

    /// <summary>
    /// Sums two doubles exactly: returns <c>(x, y)</c> with
    /// <c>x = fl(a + b)</c> and <c>x + y == a + b</c> exactly. No
    /// precondition on magnitudes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (double High, double Low) TwoSum(double a, double b)
    {
        double x = a + b;
        double bvirt = x - a;
        double avirt = x - bvirt;
        double bround = b - bvirt;
        double around = a - avirt;
        double y = around + bround;

        return (x, y);
    }

    /// <summary>
    /// Sums two doubles exactly under the precondition <c>|a| &gt;= |b|</c>
    /// (or <c>a == 0</c>): three operations instead of six.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (double High, double Low) FastTwoSum(double a, double b)
    {
        double x = a + b;
        double bvirt = x - a;
        double y = b - bvirt;

        return (x, y);
    }

    /// <summary>
    /// Subtracts two doubles exactly: returns <c>(x, y)</c> with
    /// <c>x = fl(a - b)</c> and <c>x + y == a - b</c> exactly.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (double High, double Low) TwoDiff(double a, double b)
    {
        double x = a - b;
        double bvirt = a - x;
        double avirt = x + bvirt;
        double bround = bvirt - b;
        double around = a - avirt;
        double y = around + bround;

        return (x, y);
    }

    /// <summary>
    /// Multiplies two doubles exactly: returns <c>(x, y)</c> with
    /// <c>x = fl(a * b)</c> and <c>x + y == a * b</c> exactly, through the
    /// fused multiply-add, whose single rounding makes the residual exact.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (double High, double Low) TwoProduct(double a, double b)
    {
        double x = a * b;
        double y = Math.FusedMultiplyAdd(a, b, -x);

        return (x, y);
    }

    /// <summary>
    /// Multiplies two doubles exactly through Veltkamp splitting — the
    /// fused-multiply-add-free formulation, functionally identical to
    /// <see cref="TwoProduct"/>. Retained as the differential reference the
    /// property tests compare against on every host.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (double High, double Low) TwoProductBySplit(double a, double b)
    {
        double x = a * b;
        (double aHigh, double aLow) = Split(a);
        (double bHigh, double bLow) = Split(b);
        double firstError = x - (aHigh * bHigh);
        double secondError = firstError - (aLow * bHigh);
        double thirdError = secondError - (aHigh * bLow);
        double y = (aLow * bLow) - thirdError;

        return (x, y);
    }

    /// <summary>
    /// Splits <paramref name="a"/> into a 26-bit high part and a 26-bit low
    /// part with <c>High + Low == a</c> exactly, such that products of halves
    /// are exact in double precision.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (double High, double Low) Split(double a)
    {
        double c = Splitter * a;
        double aBig = c - a;
        double high = c - aBig;
        double low = a - high;

        return (high, low);
    }

    /// <summary>
    /// Adds expansions <paramref name="e"/> and <paramref name="f"/>, writing
    /// a zero-eliminated expansion into <paramref name="result"/> and
    /// returning its component count. Requires both inputs to be valid
    /// expansions (nonoverlapping, increasing magnitude); the output is one.
    /// </summary>
    public static int Sum(ReadOnlySpan<double> e, ReadOnlySpan<double> f, Span<double> result)
    {
        int eIndex = 0;
        int fIndex = 0;
        double eNow = e[0];
        double fNow = f[0];
        double q;

        //Selects the smaller-magnitude head as the initial accumulator; the
        //comparison shape is the branchless-magnitude idiom.
        if((fNow > eNow) == (fNow > -eNow))
        {
            q = eNow;
            eIndex++;
        }
        else
        {
            q = fNow;
            fIndex++;
        }

        int written = 0;
        double high;
        double low;

        if(eIndex < e.Length && fIndex < f.Length)
        {
            eNow = e[eIndex];
            fNow = f[fIndex];

            if((fNow > eNow) == (fNow > -eNow))
            {
                (high, low) = FastTwoSum(eNow, q);
                eIndex++;
            }
            else
            {
                (high, low) = FastTwoSum(fNow, q);
                fIndex++;
            }

            q = high;

            if(low != 0.0)
            {
                result[written] = low;
                written++;
            }

            while(eIndex < e.Length && fIndex < f.Length)
            {
                eNow = e[eIndex];
                fNow = f[fIndex];

                if((fNow > eNow) == (fNow > -eNow))
                {
                    (high, low) = TwoSum(q, eNow);
                    eIndex++;
                }
                else
                {
                    (high, low) = TwoSum(q, fNow);
                    fIndex++;
                }

                q = high;

                if(low != 0.0)
                {
                    result[written] = low;
                    written++;
                }
            }
        }

        while(eIndex < e.Length)
        {
            (high, low) = TwoSum(q, e[eIndex]);
            eIndex++;
            q = high;

            if(low != 0.0)
            {
                result[written] = low;
                written++;
            }
        }

        while(fIndex < f.Length)
        {
            (high, low) = TwoSum(q, f[fIndex]);
            fIndex++;
            q = high;

            if(low != 0.0)
            {
                result[written] = low;
                written++;
            }
        }

        if(q != 0.0 || written == 0)
        {
            result[written] = q;
            written++;
        }

        return written;
    }

    /// <summary>
    /// Multiplies expansion <paramref name="e"/> by the single double
    /// <paramref name="b"/>, writing a zero-eliminated expansion into
    /// <paramref name="result"/> (at least <see cref="ScaleCapacity"/>
    /// components) and returning its component count — the scale-expansion
    /// step of the incircle tail, exact by the two-term transforms it chains.
    /// </summary>
    /// <param name="e">The scaled expansion.</param>
    /// <param name="b">The scalar factor.</param>
    /// <param name="result">The destination buffer.</param>
    /// <returns>The written component count.</returns>
    public static int Scale(ReadOnlySpan<double> e, double b, Span<double> result)
    {
        int written = 0;

        //The exact tail of the first product is the lowest-magnitude
        //component of the whole scaled expansion; the rounded product
        //continues as the accumulator.
        (double q, double firstRoundoff) = TwoProduct(e[0], b);

        if(firstRoundoff != 0.0)
        {
            result[written] = firstRoundoff;
            written++;
        }

        for(int index = 1; index < e.Length; index++)
        {
            (double productHigh, double productLow) = TwoProduct(e[index], b);
            (double sum, double sumRoundoff) = TwoSum(q, productLow);

            if(sumRoundoff != 0.0)
            {
                result[written] = sumRoundoff;
                written++;
            }

            (q, sumRoundoff) = FastTwoSum(productHigh, sum);

            if(sumRoundoff != 0.0)
            {
                result[written] = sumRoundoff;
                written++;
            }
        }

        if(q != 0.0 || written == 0)
        {
            result[written] = q;
            written++;
        }

        return written;
    }

    /// <summary>
    /// Multiplies expansions <paramref name="e"/> and <paramref name="f"/>,
    /// writing a zero-eliminated expansion into <paramref name="result"/> and
    /// returning its component count. The product is formed as the exact sum
    /// of <paramref name="e"/> scaled by each component of <paramref name="f"/>,
    /// every step one of the already-exact <see cref="Scale"/> and
    /// <see cref="Sum"/> operations, so the product carries no new rounding.
    /// <paramref name="scratch"/> must hold at least
    /// <see cref="ProductScratchCapacity"/> components and
    /// <paramref name="result"/> at least <see cref="ProductCapacity"/>, both
    /// for the input lengths.
    /// </summary>
    /// <param name="e">The first expansion.</param>
    /// <param name="f">The second expansion.</param>
    /// <param name="scratch">The working buffer.</param>
    /// <param name="result">The destination buffer.</param>
    /// <returns>The written component count.</returns>
    public static int Product(ReadOnlySpan<double> e, ReadOnlySpan<double> f, Span<double> scratch, Span<double> result)
    {
        int accumulationCapacity = ProductCapacity(e.Length, f.Length);
        Span<double> scalePartial = scratch[..ScaleCapacity(e.Length)];
        Span<double> accumulation = scratch.Slice(ScaleCapacity(e.Length), accumulationCapacity);

        //The running product alternates between result and the accumulation
        //scratch on each Sum; inResult tracks where the live components are.
        int length = Scale(e, f[0], result);
        bool inResult = true;

        for(int index = 1; index < f.Length; index++)
        {
            int partialLength = Scale(e, f[index], scalePartial);

            if(inResult)
            {
                length = Sum(result[..length], scalePartial[..partialLength], accumulation);
            }
            else
            {
                length = Sum(accumulation[..length], scalePartial[..partialLength], result);
            }

            inResult = !inResult;
        }

        if(!inResult)
        {
            accumulation[..length].CopyTo(result);
        }

        return length;
    }

    /// <summary>
    /// The exact sign of the value a zero-eliminated expansion represents:
    /// the components are nonoverlapping and increase in magnitude, so the
    /// last component dominates the sum and carries its sign.
    /// </summary>
    public static int Sign(ReadOnlySpan<double> e)
    {
        double dominant = e[^1];

        if(dominant > 0.0)
        {
            return 1;
        }

        if(dominant < 0.0)
        {
            return -1;
        }

        return 0;
    }
}
