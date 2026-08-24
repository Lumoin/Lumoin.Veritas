using System;
using System.Globalization;

namespace Lumoin.Veritas.Jsonata.Formatting;

/// <summary>
/// The half-to-even (banker's) decimal rounding the numeric and formatting built-ins share. This is the
/// reusable rounding member of the <c>Formatting</c> unit the numeric built-ins consume: <c>$round</c> and
/// <c>$formatBase</c> round through it directly, and the picture-string formatter rounds a mantissa to a
/// fractional-digit precision through it before rendering.
/// </summary>
/// <remarks>
/// <para>
/// Rounding to a fractional-digit precision scales by shifting the decimal point through exponent-string
/// manipulation rather than multiplying by a power of ten, so the scaling introduces no
/// binary-floating-point error of its own before the half-to-even step.
/// </para>
/// </remarks>
internal static class DecimalRounding
{
    /// <summary>
    /// Rounds a value half to even (banker's rounding) to the nearest integer: the half-shifted floor,
    /// stepped to the even neighbour when the value sat exactly on a half-boundary toward the odd one.
    /// </summary>
    /// <param name="value">The value to round.</param>
    /// <returns>The nearest integer, rounding a half-boundary to the even neighbour, with negative zero normalized.</returns>
    public static double RoundHalfToEven(double value)
    {
        //JS Math.round rounds half toward positive infinity, which is the floor of the half-shifted value.
        double result = Math.Floor(value + 0.5);
        double diff = result - value;
        if(Math.Abs(diff) == 0.5 && Math.Abs(result % 2) == 1)
        {
            //The half-shift rounded toward the odd neighbour; step to the even neighbour.
            result -= 1;
        }

        return NormalizeNegativeZero(result);
    }

    /// <summary>
    /// Rounds a value half to even (banker's rounding) to a fixed number of fractional digits: shift the
    /// decimal point right by that many places, round half to even to the nearest integer, then shift the
    /// point back left. A zero precision rounds to the nearest integer directly.
    /// </summary>
    /// <param name="value">The value to round.</param>
    /// <param name="fractionalDigits">The number of fractional digits to round to; zero rounds to an integer.</param>
    /// <returns>The rounded value, with negative zero normalized.</returns>
    public static double RoundHalfToEven(double value, int fractionalDigits)
    {
        bool scaled = fractionalDigits != 0;
        double arg = scaled ? ShiftDecimal(value, fractionalDigits) : value;
        double result = RoundHalfToEven(arg);
        if(scaled)
        {
            result = ShiftDecimal(result, -fractionalDigits);
        }

        return NormalizeNegativeZero(result);
    }

    /// <summary>
    /// Shifts a value's decimal point by a number of places through exponent-string manipulation rather than
    /// multiplying by a power of ten, so the scaling introduces no binary-floating-point error.
    /// </summary>
    /// <param name="value">The value to shift.</param>
    /// <param name="places">The number of decimal places to shift (positive shifts right, negative shifts left).</param>
    /// <returns>The shifted value.</returns>
    private static double ShiftDecimal(double value, int places)
    {
        //The value is formatted to its shortest round-trippable UTF-8 text — the same byte path the JSON
        //writer's number formatter takes — so adjusting the base-ten exponent and reparsing carries none of
        //the binary-rounding noise a fixed-width format would re-expose. The text is "<mantissa>" or
        //"<mantissa>E<exponent>" depending on the value's magnitude; the exponent marker is uppercase 'E'.
        Span<byte> formatted = stackalloc byte[32];
        value.TryFormat(formatted, out int written, "R", CultureInfo.InvariantCulture);
        ReadOnlySpan<byte> text = formatted[..written];

        int markerIndex = text.IndexOf((byte)'E');
        ReadOnlySpan<byte> mantissa = markerIndex < 0 ? text : text[..markerIndex];
        int exponent = markerIndex < 0 ? 0 : int.Parse(text[(markerIndex + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture);

        Span<byte> rebuilt = stackalloc byte[48];
        mantissa.CopyTo(rebuilt);
        rebuilt[mantissa.Length] = (byte)'E';
        (exponent + places).TryFormat(rebuilt[(mantissa.Length + 1)..], out int exponentWritten, provider: CultureInfo.InvariantCulture);
        int length = mantissa.Length + 1 + exponentWritten;

        return double.Parse(rebuilt[..length], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>Normalizes a negative-zero result to positive zero, so a rounded value never surfaces as <c>-0</c>.</summary>
    /// <param name="value">The value to normalize.</param>
    /// <returns>The value, with negative zero replaced by zero.</returns>
    private static double NormalizeNegativeZero(double value)
    {
        return value == 0 ? 0 : value;
    }
}
