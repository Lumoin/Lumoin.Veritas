using System;
using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Formatting;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// The numeric built-in functions: <c>$number</c>, <c>$abs</c>, <c>$floor</c>, <c>$ceil</c>,
/// <c>$round</c>, <c>$power</c>, <c>$sqrt</c>, <c>$formatNumber</c>, <c>$formatBase</c>,
/// <c>$formatInteger</c>, and <c>$parseInteger</c>. Each returns undefined for an undefined primary argument.
/// </summary>
/// <remarks>
/// <para>See <see href="https://docs.jsonata.org/numeric-functions">the JSONata numeric-functions reference</see>.</para>
/// </remarks>
internal static class JsonataNumericFunctions
{
    /// <summary>The numeric built-ins, exposed for the registry.</summary>
    public static IReadOnlyList<JsonataBuiltinFunction> All { get; } =
    [
        new JsonataBuiltinFunction(Utf8Strings.From("number"), InvokeNumber, JsonataSignature.Parse("<(nsb)-:n>")),
        new JsonataBuiltinFunction(Utf8Strings.From("abs"), InvokeAbs, JsonataSignature.Parse("<n-:n>")),
        new JsonataBuiltinFunction(Utf8Strings.From("floor"), InvokeFloor, JsonataSignature.Parse("<n-:n>")),
        new JsonataBuiltinFunction(Utf8Strings.From("ceil"), InvokeCeil, JsonataSignature.Parse("<n-:n>")),
        new JsonataBuiltinFunction(Utf8Strings.From("round"), InvokeRound, JsonataSignature.Parse("<n-n?:n>")),
        new JsonataBuiltinFunction(Utf8Strings.From("power"), InvokePower, JsonataSignature.Parse("<n-n:n>")),
        new JsonataBuiltinFunction(Utf8Strings.From("sqrt"), InvokeSqrt, JsonataSignature.Parse("<n-:n>")),
        new JsonataBuiltinFunction(Utf8Strings.From("formatNumber"), InvokeFormatNumber, JsonataSignature.Parse("<n-so?:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("formatBase"), InvokeFormatBase, JsonataSignature.Parse("<n-n?:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("formatInteger"), InvokeFormatInteger, JsonataSignature.Parse("<n-s:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("parseInteger"), InvokeParseInteger, JsonataSignature.Parse("<s-s:n>"))
    ];

    /// <summary>
    /// <c>$number(arg)</c>: casts a value to a number. A number is returned unchanged; a boolean true is
    /// <c>1</c> and false is <c>0</c>; a string is parsed as a JSON-style decimal or an explicit-prefix
    /// hex/octal/binary integer; an undefined argument yields undefined. Any other value, or an unparseable
    /// string, throws D3030.
    /// </summary>
    /// <param name="arguments">The argument list; the value to cast is the first argument.</param>
    /// <returns>The numeric value, or undefined for an undefined argument.</returns>
    /// <exception cref="JsonataErrorException">The value cannot be cast to a number (code D3030).</exception>
    private static JsonataValue InvokeNumber(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);

        return value.Kind switch
        {
            JsonataValueKind.Undefined => JsonataValue.Undefined,
            JsonataValueKind.Number => value,
            JsonataValueKind.Boolean => JsonataValue.Number(value.AsBoolean ? 1 : 0),
            JsonataValueKind.String => ParseStringNumber(value.AsString),
            _ => throw new JsonataErrorException(WellKnownJsonataErrors.NumberNotCastable, null, "The value could not be cast to a number.")
        };
    }

    /// <summary><c>$abs(arg)</c>: the absolute value; undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the number is the first argument.</param>
    /// <returns>The absolute value, or undefined.</returns>
    private static JsonataValue InvokeAbs(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.Number)
        {
            return JsonataValue.Undefined;
        }

        return JsonataValue.Number(Math.Abs(value.AsNumber));
    }

    /// <summary><c>$floor(arg)</c>: the largest integer not greater than the argument (toward negative infinity); undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the number is the first argument.</param>
    /// <returns>The floored value, or undefined.</returns>
    private static JsonataValue InvokeFloor(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.Number)
        {
            return JsonataValue.Undefined;
        }

        return JsonataValue.Number(NormalizeNegativeZero(Math.Floor(value.AsNumber)));
    }

    /// <summary><c>$ceil(arg)</c>: the smallest integer not less than the argument (toward positive infinity); undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the number is the first argument.</param>
    /// <returns>The ceiling value, or undefined.</returns>
    private static JsonataValue InvokeCeil(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.Number)
        {
            return JsonataValue.Undefined;
        }

        return JsonataValue.Number(NormalizeNegativeZero(Math.Ceiling(value.AsNumber)));
    }

    /// <summary>
    /// <c>$round(arg[, precision])</c>: rounds half to even (banker's rounding) to an optional number of
    /// decimal places (default 0, with a precision of 0 behaving as omitted). An undefined first argument
    /// yields undefined.
    /// </summary>
    /// <param name="arguments">The argument list; the number is the first argument and an optional precision the second.</param>
    /// <returns>The rounded value, or undefined for an undefined first argument.</returns>
    private static JsonataValue InvokeRound(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.Number)
        {
            return JsonataValue.Undefined;
        }

        int precision = ReadPrecision(arguments);

        //DecimalRounding normalizes negative zero on the value it returns.
        return JsonataValue.Number(DecimalRounding.RoundHalfToEven(value.AsNumber, precision));
    }

    /// <summary>
    /// <c>$power(base, exponent)</c>: the base raised to the exponent. An undefined base yields undefined; a
    /// non-finite result (overflow, or a negative base with a fractional exponent) throws D3061.
    /// </summary>
    /// <param name="arguments">The argument list; the base is the first argument and the exponent the second.</param>
    /// <returns>The power, or undefined for an undefined base.</returns>
    /// <exception cref="JsonataErrorException">The result is not a finite number (code D3061).</exception>
    private static JsonataValue InvokePower(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.Number)
        {
            return JsonataValue.Undefined;
        }

        double exponent = arguments.Count > 1 && arguments[1].Kind == JsonataValueKind.Number ? arguments[1].AsNumber : 0;
        double result = Math.Pow(value.AsNumber, exponent);
        if(!double.IsFinite(result))
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.PowerNotFinite, null, "The power function result is out of range.");
        }

        return JsonataValue.Number(result);
    }

    /// <summary><c>$sqrt(arg)</c>: the non-negative square root; undefined yields undefined; a negative argument throws D3060.</summary>
    /// <param name="arguments">The argument list; the number is the first argument.</param>
    /// <returns>The square root, or undefined.</returns>
    /// <exception cref="JsonataErrorException">The argument is negative (code D3060).</exception>
    private static JsonataValue InvokeSqrt(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.Number)
        {
            return JsonataValue.Undefined;
        }

        double number = value.AsNumber;
        if(number < 0)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.SqrtNegative, null, "The sqrt function cannot be applied to a negative number.");
        }

        return JsonataValue.Number(NormalizeNegativeZero(Math.Sqrt(number)));
    }

    /// <summary>
    /// <c>$formatNumber(number, picture[, options])</c>: formats a number into a decimal picture string,
    /// implementing the XPath/XQuery <c>fn:format-number</c> decimal-format DSL. The optional third argument is
    /// an object of symbol overrides keyed by the F&amp;O property name. An undefined number yields undefined.
    /// </summary>
    /// <param name="arguments">The argument list; the number is the first argument, the picture string the second, and an optional options object the third.</param>
    /// <returns>The formatted string, or undefined for an undefined number.</returns>
    /// <exception cref="JsonataErrorException">The picture string is invalid (a <c>D308x</c> code).</exception>
    private static JsonataValue InvokeFormatNumber(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.Number)
        {
            return JsonataValue.Undefined;
        }

        string picture = arguments.Count > 1 && arguments[1].Kind == JsonataValueKind.String ? arguments[1].AsString : string.Empty;
        IReadOnlyList<KeyValuePair<string, JsonataValue>>? options = arguments.Count > 2 && arguments[2].Kind == JsonataValueKind.Object ? arguments[2].AsObject : null;

        return JsonataValue.String(NumberPictureFormatter.Format(value.AsNumber, picture, options));
    }

    /// <summary>
    /// <c>$formatBase(number[, radix])</c>: formats a number as a base-N string using the lower-case digit
    /// alphabet. The number and the radix are each rounded half to even before formatting; the radix defaults
    /// to 10 and must be between 2 and 36. An undefined number yields undefined.
    /// </summary>
    /// <param name="arguments">The argument list; the number is the first argument and an optional radix the second.</param>
    /// <returns>The base-N string, or undefined for an undefined number.</returns>
    /// <exception cref="JsonataErrorException">The radix is outside the range 2 to 36 (code D3100).</exception>
    private static JsonataValue InvokeFormatBase(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.Number)
        {
            return JsonataValue.Undefined;
        }

        double number = DecimalRounding.RoundHalfToEven(value.AsNumber);
        double radix = arguments.Count > 1 && arguments[1].Kind == JsonataValueKind.Number ? DecimalRounding.RoundHalfToEven(arguments[1].AsNumber) : 10;

        return JsonataValue.String(NumberBaseFormatter.Format(number, radix));
    }

    /// <summary>
    /// <c>$formatInteger(number, picture)</c>: formats an integer-valued number into a picture string,
    /// implementing the XPath/XQuery <c>fn:format-integer</c> DSL (decimal digits, spreadsheet-column letters,
    /// Roman numerals, and English number words, with an optional ordinal modifier). The number is floored
    /// toward negative infinity before rendering. An undefined number yields undefined.
    /// </summary>
    /// <param name="arguments">The argument list; the number is the first argument and the picture string the second.</param>
    /// <returns>The formatted string, or undefined for an undefined number.</returns>
    /// <exception cref="JsonataErrorException">The picture is an unsupported sequence (D3130) or mixes digit families (D3131).</exception>
    private static JsonataValue InvokeFormatInteger(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.Number)
        {
            return JsonataValue.Undefined;
        }

        string picture = arguments.Count > 1 && arguments[1].Kind == JsonataValueKind.String ? arguments[1].AsString : string.Empty;

        return JsonataValue.String(IntegerPictureFormatter.Format(value.AsNumber, picture));
    }

    /// <summary>
    /// <c>$parseInteger(value, picture)</c>: parses a string formatted by the <c>fn:format-integer</c> DSL back
    /// to a number, transforming the input directly for the integer path (no regex validation). An undefined
    /// value yields undefined.
    /// </summary>
    /// <param name="arguments">The argument list; the string is the first argument and the picture string the second.</param>
    /// <returns>The parsed number, or undefined for an undefined value.</returns>
    /// <exception cref="JsonataErrorException">The picture is an unsupported sequence (D3130) or mixes digit families (D3131).</exception>
    private static JsonataValue InvokeParseInteger(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        string picture = arguments.Count > 1 && arguments[1].Kind == JsonataValueKind.String ? arguments[1].AsString : string.Empty;

        return JsonataValue.Number(IntegerPictureFormatter.Parse(value.AsString, picture));
    }

    /// <summary>Reads the optional second precision argument, truncated to an integer; absent or non-numeric precision is 0 (no scaling).</summary>
    /// <param name="arguments">The argument list.</param>
    /// <returns>The truncated precision, or 0 when absent.</returns>
    private static int ReadPrecision(IReadOnlyList<JsonataValue> arguments)
    {
        if(arguments.Count < 2 || arguments[1].Kind != JsonataValueKind.Number)
        {
            return 0;
        }

        return (int)Math.Truncate(arguments[1].AsNumber);
    }

    /// <summary>
    /// Parses a string to a number: a JSON-style decimal (a digit before and after any decimal point, with an
    /// optional sign and exponent) or an explicit-prefix hexadecimal, octal, or binary integer. An
    /// unparseable string throws D3030.
    /// </summary>
    /// <param name="text">The string to parse.</param>
    /// <returns>The parsed number.</returns>
    /// <exception cref="JsonataErrorException">The string is not a valid number (code D3030).</exception>
    private static JsonataValue ParseStringNumber(string text)
    {
        if(IsJsonDecimal(text))
        {
            double value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

            //A magnitude beyond the double range parses to an infinity; the reference rejects a non-finite
            //cast rather than carrying an infinity that only fails later at serialization.
            if(!double.IsFinite(value))
            {
                throw new JsonataErrorException(WellKnownJsonataErrors.NumberNotCastable, null, "The value could not be cast to a number.");
            }

            return JsonataValue.Number(value);
        }

        if(TryParsePrefixedInteger(text, out double prefixed))
        {
            return JsonataValue.Number(prefixed);
        }

        throw new JsonataErrorException(WellKnownJsonataErrors.NumberNotCastable, null, "The value could not be cast to a number.");
    }

    /// <summary>
    /// Determines whether a string matches the JSON decimal grammar
    /// <c>^-?[0-9]+(\.[0-9]+)?([Ee][-+]?[0-9]+)?$</c>: an optional leading minus, one or more integer
    /// digits, an optional fractional part with a digit on each side of the point, and an optional
    /// signed exponent.
    /// </summary>
    /// <param name="text">The string to test.</param>
    /// <returns><see langword="true"/> when the string is a JSON decimal.</returns>
    private static bool IsJsonDecimal(string text)
    {
        ReadOnlySpan<char> span = text;
        int index = 0;
        if(index < span.Length && span[index] == '-')
        {
            index++;
        }

        int integerDigits = ConsumeDigits(span, ref index);
        if(integerDigits == 0)
        {
            return false;
        }

        if(index < span.Length && span[index] == '.')
        {
            index++;
            int fractionDigits = ConsumeDigits(span, ref index);
            if(fractionDigits == 0)
            {
                return false;
            }
        }

        if(index < span.Length && (span[index] == 'e' || span[index] == 'E'))
        {
            index++;
            if(index < span.Length && (span[index] == '+' || span[index] == '-'))
            {
                index++;
            }

            int exponentDigits = ConsumeDigits(span, ref index);
            if(exponentDigits == 0)
            {
                return false;
            }
        }

        return index == span.Length;
    }

    /// <summary>Consumes a run of ASCII decimal digits from a span, advancing the cursor, and returns how many were consumed.</summary>
    /// <param name="span">The span being scanned.</param>
    /// <param name="index">The cursor, advanced past the consumed digits.</param>
    /// <returns>The number of digits consumed.</returns>
    private static int ConsumeDigits(ReadOnlySpan<char> span, ref int index)
    {
        int start = index;
        while(index < span.Length && span[index] is >= '0' and <= '9')
        {
            index++;
        }

        return index - start;
    }

    /// <summary>
    /// Parses an explicit-prefix integer in base 16 (<c>0x</c>), base 8 (<c>0o</c>), or base 2 (<c>0b</c>),
    /// each with at least one digit of the corresponding alphabet and no other characters.
    /// </summary>
    /// <param name="text">The string to parse.</param>
    /// <param name="result">On success, the parsed value as a double.</param>
    /// <returns><see langword="true"/> when the string is a valid prefixed integer.</returns>
    private static bool TryParsePrefixedInteger(string text, out double result)
    {
        result = 0;
        if(text.Length < 3 || text[0] != '0')
        {
            return false;
        }

        (int radix, int alphabet) = text[1] switch
        {
            'x' or 'X' => (16, 16),
            'o' or 'O' => (8, 8),
            'b' or 'B' => (2, 2),
            _ => (0, 0)
        };

        if(radix == 0)
        {
            return false;
        }

        double value = 0;
        for(int i = 2; i < text.Length; i++)
        {
            int digit = DigitValue(text[i]);
            if(digit < 0 || digit >= alphabet)
            {
                return false;
            }

            value = (value * radix) + digit;
        }

        result = value;

        return true;
    }

    /// <summary>Maps an ASCII hexadecimal digit character to its value, or a negative number when it is not a hexadecimal digit.</summary>
    /// <param name="character">The character to map.</param>
    /// <returns>The digit value 0-15, or -1 when the character is not a hexadecimal digit.</returns>
    private static int DigitValue(char character)
    {
        return character switch
        {
            >= '0' and <= '9' => character - '0',
            >= 'a' and <= 'f' => (character - 'a') + 10,
            >= 'A' and <= 'F' => (character - 'A') + 10,
            _ => -1
        };
    }

    /// <summary>Normalizes a negative-zero result to positive zero, so a rounded or floored value never surfaces as <c>-0</c>.</summary>
    /// <param name="value">The value to normalize.</param>
    /// <returns>The value, with negative zero replaced by zero.</returns>
    private static double NormalizeNegativeZero(double value)
    {
        return value == 0 ? 0 : value;
    }

    /// <summary>Reads the first argument, or the undefined value when no argument was supplied.</summary>
    /// <param name="arguments">The argument list.</param>
    /// <returns>The first argument, or undefined.</returns>
    private static JsonataValue First(IReadOnlyList<JsonataValue> arguments)
    {
        return arguments.Count > 0 ? arguments[0] : JsonataValue.Undefined;
    }
}
