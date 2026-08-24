using System;
using System.Globalization;

namespace Lumoin.Veritas.Jsonata.Values;

/// <summary>
/// Formats a finite IEEE-754 double to its string form the way JSONata does: the ECMAScript
/// <c>Number::toString</c> algorithm (ECMA-262 section 6.1.6.1.20, identical to ES5.1 section 9.8.1), with an
/// optional <c>toPrecision(15)</c> reduction for non-integers that the reference applies inside <c>$string</c>
/// and the string-concatenation operator.
/// </summary>
/// <remarks>
/// <para>
/// The output is produced byte-native into a caller-supplied UTF-8 span (no intermediate
/// <see cref="string"/> for the final form), matching the writer idiom. The shortest round-trippable decimal
/// digits come from .NET's round-trip (<c>"R"</c>) format — .NET Core yields the shortest digit run that
/// re-parses to the same double — which is then decomposed into a sign, a significant-digit run <c>s</c> of
/// length <c>k</c> (no leading or trailing zeroes), and the decimal point exponent <c>n</c> such that the
/// value is <c>s · 10^(n − k)</c>. The ECMA-262 case analysis then selects fixed-point or exponential
/// notation:
/// </para>
/// <list type="bullet">
/// <item><description><c>k ≤ n ≤ 21</c>: the <c>k</c> digits followed by <c>n − k</c> zeroes (so <c>1e20</c> is <c>"100000000000000000000"</c>).</description></item>
/// <item><description><c>0 &lt; n ≤ 21</c>: the first <c>n</c> digits, a decimal point, then the remaining <c>k − n</c> digits.</description></item>
/// <item><description><c>−6 &lt; n ≤ 0</c>: <c>"0."</c>, then <c>−n</c> zeroes, then the <c>k</c> digits (so <c>1e-6</c> is <c>"0.000001"</c>).</description></item>
/// <item><description>otherwise (<c>n &gt; 21</c> or <c>n ≤ −6</c>): the first digit, a decimal point and the remaining <c>k − 1</c> digits when <c>k &gt; 1</c>, then <c>"e"</c>, a <c>"+"</c> or <c>"-"</c> sign, and the magnitude of <c>n − 1</c> (so <c>1e-7</c> is <c>"1e-7"</c> and <c>1e21</c> is <c>"1e+21"</c>).</description></item>
/// </list>
/// <para>
/// The exponent carries a leading <c>"+"</c> or <c>"-"</c> and no leading zeroes, the marker is lowercase
/// <c>e</c>, and only the exponent magnitude (never the digit run) is rendered. Callers must reject non-finite
/// values themselves; this formatter is defined only for finite inputs.
/// </para>
/// </remarks>
internal static class EcmaScriptNumberFormatter
{
    /// <summary>The largest decimal point exponent that still renders in fixed-point notation (the ECMA-262 <c>n ≤ 21</c> boundary).</summary>
    private const int MaxFixedPointExponent = 21;

    /// <summary>The exclusive lower bound on the decimal point exponent for fixed-point notation (the ECMA-262 <c>−6 &lt; n</c> boundary; at or below it the form is exponential). So <c>n = −5</c> is fixed-point and <c>n = −6</c> is exponential.</summary>
    private const int FixedPointExponentLowerBound = -6;

    /// <summary>
    /// Formats a finite double per ECMA-262 <c>Number::toString</c> into a UTF-8 span, optionally applying the
    /// reference's <c>toPrecision(15)</c> reduction to a non-integer first.
    /// </summary>
    /// <param name="value">The finite value to format.</param>
    /// <param name="applyToPrecision15">When <see langword="true"/>, a non-integer value is reduced to its shortest 15-significant-figure form before formatting (the <c>$string</c> / concatenation behaviour); when <see langword="false"/>, the shortest round-trip digits are used (the JSON serializer behaviour).</param>
    /// <param name="destination">The UTF-8 destination span; it must be at least <see cref="MaxFormattedLength"/> bytes.</param>
    /// <returns>The number of bytes written.</returns>
    public static int Format(double value, bool applyToPrecision15, Span<byte> destination)
    {
        double reduced = applyToPrecision15 ? ReduceToPrecision15(value) : value;

        //The shortest round-trippable digits; .NET Core's "R" yields the minimal digit run that re-parses to
        //the same double, which is the s of the ECMA-262 algorithm (after stripping the sign, point, and any
        //exponent introduced by the format).
        Span<char> roundTrip = stackalloc char[32];
        if(!reduced.TryFormat(roundTrip, out int roundTripLength, "R", CultureInfo.InvariantCulture))
        {
            //"R" of a finite double never exceeds 32 chars, so this is defensive only.
            string fallback = reduced.ToString("R", CultureInfo.InvariantCulture);
            fallback.AsSpan().CopyTo(roundTrip);
            roundTripLength = fallback.Length;
        }

        return FormatRoundTrip(roundTrip[..roundTripLength], destination);
    }

    /// <summary>The maximum number of UTF-8 bytes <see cref="Format"/> can write, sufficient for the longest fixed-point form (a 21-digit integer with sign) and any exponential form.</summary>
    public static int MaxFormattedLength => 32;

    /// <summary>Reduces a non-integer to its shortest 15-significant-figure form (the reference's <c>Number(value.toPrecision(15))</c>); an integer-valued double is returned unchanged so it keeps its full digit run.</summary>
    /// <param name="value">The finite value to reduce.</param>
    /// <returns>The reduced value.</returns>
    private static double ReduceToPrecision15(double value)
    {
        if(value == Math.Truncate(value))
        {
            //An integer-valued double is emitted in full with no precision reduction (matching Number.isInteger).
            return value;
        }

        //"G15" rounds to 15 significant figures and re-parses, mirroring Number(value.toPrecision(15)): the
        //15-figure rounded text drops floating-point noise and the re-parse yields the shortest double for it.
        string rounded = value.ToString("G15", CultureInfo.InvariantCulture);

        return double.Parse(rounded, CultureInfo.InvariantCulture);
    }

    /// <summary>Decomposes a round-trip-formatted decimal string into sign, significant digits, and decimal point exponent, then renders the ECMA-262 case analysis into the destination.</summary>
    /// <param name="roundTrip">The round-trip-formatted decimal (a sign, integer/fraction digits, and an optional <c>E±exp</c>).</param>
    /// <param name="destination">The UTF-8 destination span.</param>
    /// <returns>The number of bytes written.</returns>
    private static int FormatRoundTrip(ReadOnlySpan<char> roundTrip, Span<byte> destination)
    {
        int cursor = 0;
        bool negative = roundTrip[0] == '-';
        if(negative)
        {
            cursor = 1;
        }

        //Accumulate every decimal digit (across the integer and fraction parts), tracking the decimal point's
        //position and any explicit exponent the "R" form introduced.
        Span<byte> digits = stackalloc byte[32];
        int digitCount = 0;
        int pointExponent = 0;
        bool sawPoint = false;
        int explicitExponent = 0;
        for(int i = cursor; i < roundTrip.Length; i++)
        {
            char c = roundTrip[i];
            if(c == '.')
            {
                sawPoint = true;

                continue;
            }

            if(c is 'e' or 'E')
            {
                explicitExponent = int.Parse(roundTrip[(i + 1)..], CultureInfo.InvariantCulture);

                break;
            }

            digits[digitCount] = (byte)c;
            digitCount++;
            if(!sawPoint)
            {
                pointExponent++;
            }
        }

        //pointExponent now counts the digits before the point in the raw run; fold in the explicit exponent so
        //the value is (all digits) with the point after pointExponent of them.
        pointExponent += explicitExponent;

        //Normalize: strip leading zeroes (advancing the start, each one lowering the point exponent) and
        //trailing zeroes (lowering the digit count), leaving the significant run s of length k.
        int start = 0;
        while(start < digitCount - 1 && digits[start] == (byte)'0')
        {
            start++;
            pointExponent--;
        }

        int end = digitCount;
        while(end - start > 1 && digits[end - 1] == (byte)'0')
        {
            end--;
        }

        ReadOnlySpan<byte> significant = digits[start..end];
        if(significant.Length == 1 && significant[0] == (byte)'0')
        {
            //A zero value renders as "0" (the +0 / -0 / 0 case), with no sign.
            destination[0] = (byte)'0';

            return 1;
        }

        return RenderEcmaScript(negative, significant, pointExponent, destination);
    }

    /// <summary>Renders the ECMA-262 fixed-point / exponential case analysis for a normalized significant-digit run.</summary>
    /// <param name="negative">Whether the value is negative.</param>
    /// <param name="significant">The significant digits <c>s</c> (no leading or trailing zeroes), of length <c>k</c>.</param>
    /// <param name="n">The decimal point exponent <c>n</c> such that the value is <c>s · 10^(n − k)</c>.</param>
    /// <param name="destination">The UTF-8 destination span.</param>
    /// <returns>The number of bytes written.</returns>
    private static int RenderEcmaScript(bool negative, ReadOnlySpan<byte> significant, int n, Span<byte> destination)
    {
        int written = 0;
        if(negative)
        {
            destination[written] = (byte)'-';
            written++;
        }

        int k = significant.Length;
        if(k <= n && n <= MaxFixedPointExponent)
        {
            //Step 6: the k digits followed by n − k zeroes.
            significant.CopyTo(destination[written..]);
            written += k;
            written += WriteZeroes(destination[written..], n - k);

            return written;
        }

        if(0 < n && n <= MaxFixedPointExponent)
        {
            //Step 7: the first n digits, a point, then the remaining k − n digits.
            significant[..n].CopyTo(destination[written..]);
            written += n;
            destination[written] = (byte)'.';
            written++;
            significant[n..].CopyTo(destination[written..]);
            written += k - n;

            return written;
        }

        if(FixedPointExponentLowerBound < n && n <= 0)
        {
            //Step 8: "0.", then −n zeroes, then the k digits.
            destination[written] = (byte)'0';
            written++;
            destination[written] = (byte)'.';
            written++;
            written += WriteZeroes(destination[written..], -n);
            significant.CopyTo(destination[written..]);
            written += k;

            return written;
        }

        //Steps 9 and 10: exponential form. The first digit, then (when k > 1) a point and the remaining
        //k − 1 digits, then "e", the sign of n − 1, and the magnitude of n − 1.
        destination[written] = significant[0];
        written++;
        if(k > 1)
        {
            destination[written] = (byte)'.';
            written++;
            significant[1..].CopyTo(destination[written..]);
            written += k - 1;
        }

        destination[written] = (byte)'e';
        written++;
        int exponent = n - 1;
        destination[written] = exponent < 0 ? (byte)'-' : (byte)'+';
        written++;
        written += WriteUnsignedInteger(destination[written..], Math.Abs(exponent));

        return written;
    }

    /// <summary>Writes a run of ASCII <c>'0'</c> bytes.</summary>
    /// <param name="destination">The UTF-8 destination span.</param>
    /// <param name="count">The number of zeroes to write (zero or more).</param>
    /// <returns>The number of bytes written.</returns>
    private static int WriteZeroes(Span<byte> destination, int count)
    {
        for(int i = 0; i < count; i++)
        {
            destination[i] = (byte)'0';
        }

        return count;
    }

    /// <summary>Writes a non-negative integer's decimal digits with no leading zeroes.</summary>
    /// <param name="destination">The UTF-8 destination span.</param>
    /// <param name="value">The non-negative value to write.</param>
    /// <returns>The number of bytes written.</returns>
    private static int WriteUnsignedInteger(Span<byte> destination, int value)
    {
        if(value == 0)
        {
            destination[0] = (byte)'0';

            return 1;
        }

        //Emit digits least-significant-first into a scratch, then copy them out in order.
        Span<byte> scratch = stackalloc byte[11];
        int count = 0;
        int remaining = value;
        while(remaining > 0)
        {
            scratch[count] = (byte)('0' + (remaining % 10));
            count++;
            remaining /= 10;
        }

        for(int i = 0; i < count; i++)
        {
            destination[i] = scratch[count - 1 - i];
        }

        return count;
    }
}
