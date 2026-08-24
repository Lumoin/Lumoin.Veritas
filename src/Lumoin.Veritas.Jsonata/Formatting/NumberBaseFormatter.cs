using System;

namespace Lumoin.Veritas.Jsonata.Formatting;

/// <summary>
/// Formats an integer-valued number into a positional base-N string for the <c>$formatBase</c> built-in,
/// using the lower-case digit alphabet <c>0-9a-z</c>. This is the first member of the reusable formatting
/// unit the numeric built-ins consume; the caller is responsible for coercing the value and the radix to
/// integers (the reference rounds both half to even) before formatting.
/// </summary>
/// <remarks>
/// <para>
/// The conversion runs as an explicit digit-extraction loop with no recursion, bounded by a fixed digit
/// buffer wide enough for the base-two expansion of any finite double. Magnitudes below 2^63 convert through
/// exact 64-bit integer arithmetic; a larger magnitude (which a double can no longer represent without gaps)
/// falls back to floating-point division, carrying the same precision as the value itself.
/// </para>
/// </remarks>
internal static class NumberBaseFormatter
{
    /// <summary>The smallest supported radix.</summary>
    private const int MinimumRadix = 2;

    /// <summary>The largest supported radix.</summary>
    private const int MaximumRadix = 36;

    /// <summary>The exclusive upper bound (2^63) below which an integer-valued double converts exactly to a 64-bit integer.</summary>
    private const double ExactIntegerLimit = 9223372036854775808.0;

    /// <summary>The digit-buffer width, wide enough for the base-two expansion of any finite double (under 1025 binary digits).</summary>
    private const int MaximumDigits = 1080;

    /// <summary>
    /// Formats an integer-valued number into its base-N representation, with a leading minus sign for a
    /// negative value and the lower-case alphabet for digits above nine.
    /// </summary>
    /// <param name="value">The integer-valued number to format (the caller has already rounded it).</param>
    /// <param name="radix">The radix; must be between 2 and 36 inclusive (the caller has already rounded it).</param>
    /// <returns>The base-N string.</returns>
    /// <exception cref="JsonataErrorException">The radix is outside the range 2 to 36 (code D3100).</exception>
    public static string Format(double value, double radix)
    {
        if(radix < MinimumRadix || radix > MaximumRadix)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.InvalidRadix, null, "The $formatBase radix must be between 2 and 36.");
        }

        if(value == 0)
        {
            return "0";
        }

        int baseN = (int)radix;
        bool negative = value < 0;
        double magnitude = Math.Abs(value);

        Span<char> digits = stackalloc char[MaximumDigits];
        int count = magnitude < ExactIntegerLimit ? ExtractDigitsExact((long)magnitude, baseN, digits) : ExtractDigitsApproximate(magnitude, baseN, digits);

        Span<char> result = stackalloc char[count + (negative ? 1 : 0)];
        int position = 0;
        if(negative)
        {
            result[position++] = '-';
        }

        for(int i = count - 1; i >= 0; i--)
        {
            result[position++] = digits[i];
        }

        return new string(result);
    }

    /// <summary>
    /// Extracts the base-N digits of an exact non-negative 64-bit magnitude, least significant first, into the
    /// buffer and returns the digit count.
    /// </summary>
    /// <param name="magnitude">The non-negative magnitude.</param>
    /// <param name="baseN">The radix.</param>
    /// <param name="digits">The buffer receiving the digits least significant first.</param>
    /// <returns>The number of digits written.</returns>
    private static int ExtractDigitsExact(long magnitude, int baseN, Span<char> digits)
    {
        int count = 0;
        long remaining = magnitude;
        while(remaining > 0)
        {
            digits[count++] = DigitCharacter((int)(remaining % baseN));
            remaining /= baseN;
        }

        return count;
    }

    /// <summary>
    /// Extracts the base-N digits of a magnitude too large for exact 64-bit conversion, least significant
    /// first, through floating-point division (carrying the value's own precision).
    /// </summary>
    /// <param name="magnitude">The non-negative magnitude.</param>
    /// <param name="baseN">The radix.</param>
    /// <param name="digits">The buffer receiving the digits least significant first.</param>
    /// <returns>The number of digits written.</returns>
    private static int ExtractDigitsApproximate(double magnitude, int baseN, Span<char> digits)
    {
        int count = 0;
        double remaining = magnitude;
        while(remaining >= 1)
        {
            double quotient = Math.Floor(remaining / baseN);
            digits[count++] = DigitCharacter((int)(remaining - (quotient * baseN)));
            remaining = quotient;
        }

        return count;
    }

    /// <summary>Maps a digit value 0-35 to its lower-case base-36 character.</summary>
    /// <param name="digit">The digit value, 0 to 35.</param>
    /// <returns>The digit character, <c>0-9</c> for 0-9 and <c>a-z</c> for 10-35.</returns>
    private static char DigitCharacter(int digit)
    {
        return digit < 10 ? (char)('0' + digit) : (char)('a' + (digit - 10));
    }
}
