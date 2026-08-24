using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Execution;
using Lumoin.Veritas.Jsonata.Formatting;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// The date/time built-in functions: <c>$now</c>, <c>$millis</c>, and <c>$toMillis</c> (the context-aware
/// trio — <c>$now</c> and <c>$millis</c> read the evaluation's captured instant, <c>$toMillis</c> reads it to
/// default the parts a picture leaves unspecified) and <c>$fromMillis</c> (the pure format function). Each
/// formats or parses against an XPath <c>fn:format-dateTime</c> / <c>fn:parse-dateTime</c> picture string; the
/// default (no-picture) mode is the ISO-8601 UTC form <c>yyyy-MM-ddTHH:mm:ss.fffZ</c>.
/// </summary>
/// <remarks>
/// <para>
/// A supplied <c>picture</c> (and <c>timezone</c>) is honoured through <see cref="DateTimePictureFormatter"/>:
/// <c>$fromMillis</c> and <c>$now</c> format through <see cref="DateTimePictureFormatter.FormatDateTime"/> and
/// <c>$toMillis</c> parses a picture through <see cref="DateTimePictureFormatter.ParseDateTime"/>. The
/// no-picture <c>$toMillis</c> keeps the fixed ISO-8601 validation-and-parse path (with the D3110 reject) for
/// the canonical timestamp form.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/date-time-functions">the JSONata date/time-functions reference</see>.</para>
/// </remarks>
internal static partial class JsonataDateFunctions
{
    /// <summary>The pure date built-ins (<c>$fromMillis</c>), exposed for the registry.</summary>
    public static IReadOnlyList<JsonataBuiltinFunction> All { get; } =
    [
        new JsonataBuiltinFunction(Utf8Strings.From("fromMillis"), InvokeFromMillis, JsonataSignature.Parse("<n-s?s?:s>"))
    ];

    /// <summary>The context-aware date built-ins (<c>$now</c>, <c>$millis</c>, <c>$toMillis</c>), exposed for the registry.</summary>
    public static IReadOnlyList<JsonataContextualBuiltinFunction> ContextualAll { get; } =
    [
        new JsonataContextualBuiltinFunction(Utf8Strings.From("now"), InvokeNow, JsonataSignature.Parse("<s?s?:s>")),
        new JsonataContextualBuiltinFunction(Utf8Strings.From("millis"), InvokeMillis, JsonataSignature.Parse("<:n>")),
        new JsonataContextualBuiltinFunction(Utf8Strings.From("toMillis"), InvokeToMillis, JsonataSignature.Parse("<s-s?:n>"))
    ];

    /// <summary>
    /// <c>$now([picture[, timezone]])</c>: the evaluation's fixed instant formatted as a string. With no
    /// picture it is the default ISO-8601 UTC form, equal to <c>$fromMillis($millis())</c>; with a picture (and
    /// optional timezone) it is formatted through the picture machinery.
    /// </summary>
    /// <param name="arguments">The argument list; the optional picture is the first argument, the optional timezone the second.</param>
    /// <param name="context">The evaluation context whose captured instant is formatted.</param>
    /// <returns>The instant formatted as a string.</returns>
    /// <exception cref="JsonataErrorException">The picture is malformed (D3132/D3133/D3134/D3135).</exception>
    private static JsonataValue InvokeNow(IReadOnlyList<JsonataValue> arguments, JsonataContext context)
    {
        string? picture = OptionalString(arguments, 0);
        string? timezone = OptionalString(arguments, 1);

        return JsonataValue.String(DateTimePictureFormatter.FormatDateTime(context.EvaluationMillis, picture, timezone));
    }

    /// <summary>
    /// <c>$millis()</c>: the evaluation's fixed instant as integer epoch-milliseconds (UTC). It reads the
    /// context's captured instant, so it is deterministic within one evaluation.
    /// </summary>
    /// <param name="arguments">The argument list; <c>$millis</c> takes no arguments.</param>
    /// <param name="context">The evaluation context whose captured instant is returned.</param>
    /// <returns>The captured instant as a number.</returns>
    private static JsonataValue InvokeMillis(IReadOnlyList<JsonataValue> arguments, JsonataContext context)
    {
        return JsonataValue.Number(context.EvaluationMillis);
    }

    /// <summary>
    /// <c>$fromMillis(number[, picture[, timezone]])</c>: formats epoch-milliseconds as a string. An undefined
    /// first argument yields undefined. With no picture it is the default ISO-8601 UTC form
    /// <c>yyyy-MM-ddTHH:mm:ss.fffZ</c>; with a picture (and optional timezone) it is formatted through the
    /// picture machinery.
    /// </summary>
    /// <param name="arguments">The argument list; the millis is the first argument, the optional picture the second and timezone the third.</param>
    /// <returns>The formatted string, or undefined for an undefined first argument.</returns>
    /// <exception cref="JsonataErrorException">The picture is malformed (D3132/D3133/D3134/D3135).</exception>
    private static JsonataValue InvokeFromMillis(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.Number)
        {
            return JsonataValue.Undefined;
        }

        long millis = (long)value.AsNumber;
        string? picture = OptionalString(arguments, 1);
        string? timezone = OptionalString(arguments, 2);

        return JsonataValue.String(DateTimePictureFormatter.FormatDateTime(millis, picture, timezone));
    }

    /// <summary>
    /// <c>$toMillis(string[, picture])</c>: parses a timestamp string to integer epoch-milliseconds (UTC). An
    /// undefined first argument yields undefined. With no picture the string is validated against the ISO-8601
    /// pattern — a value that does not match throws D3110 — and then parsed to UTC epoch-milliseconds. With a
    /// picture the string is matched against the picture; a non-matching string yields undefined and the
    /// unspecified parts are defaulted from the evaluation's captured instant.
    /// </summary>
    /// <param name="arguments">The argument list; the timestamp string is the first argument, the optional picture the second.</param>
    /// <param name="context">The evaluation context whose captured instant defaults the picture's unspecified parts.</param>
    /// <returns>The epoch-milliseconds as a number, or undefined for an undefined first argument or a non-matching picture.</returns>
    /// <exception cref="JsonataErrorException">The no-picture input is not a valid ISO-8601 timestamp (D3110), or the picture/parse is malformed (D3132/D3133/D3135/D3136).</exception>
    private static JsonataValue InvokeToMillis(IReadOnlyList<JsonataValue> arguments, JsonataContext context)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        string? picture = OptionalString(arguments, 1);
        if(picture is null)
        {
            return JsonataValue.Number(ToUnixMillis(value.AsString));
        }

        long? millis = DateTimePictureFormatter.ParseDateTime(value.AsString, picture, context.EvaluationMillis);

        return millis is null ? JsonataValue.Undefined : JsonataValue.Number(millis.Value);
    }

    /// <summary>Reads the string argument at the given index, or <see langword="null"/> when the argument is absent or not a string.</summary>
    /// <param name="arguments">The argument list.</param>
    /// <param name="index">The argument index.</param>
    /// <returns>The string argument, or <see langword="null"/>.</returns>
    private static string? OptionalString(IReadOnlyList<JsonataValue> arguments, int index)
    {
        if(index >= arguments.Count)
        {
            return null;
        }

        JsonataValue value = arguments[index];

        return value.Kind == JsonataValueKind.String ? value.AsString : null;
    }

    /// <summary>
    /// Parses the default ISO-8601 timestamp string to UTC epoch-milliseconds: the string is validated against
    /// the ISO-8601 pattern (a value that does not match throws D3110), a bare-year string is completed to its
    /// first day, and the result is parsed as a universal instant.
    /// </summary>
    /// <param name="text">The timestamp string.</param>
    /// <returns>The UTC epoch-milliseconds.</returns>
    /// <exception cref="JsonataErrorException">The string is not a valid ISO-8601 timestamp (code D3110).</exception>
    private static long ToUnixMillis(string text)
    {
        if(!Iso8601Regex().IsMatch(text))
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.NotIso8601, null, "The supplied value could not be parsed as an ISO 8601 timestamp.");
        }

        string completed = CompleteBareYear(text);

        //The pattern is a necessary pre-filter, not a sufficient one: a value that matches the pattern but is
        //not a real calendar instant (an out-of-range month or day, or a year the universal-instant parser
        //rejects) is the same "not a valid ISO 8601 timestamp" condition, so a parse failure is also D3110
        //rather than a framework exception leaking from the public engine.
        try
        {
            DateTimeOffset instant = DateTimeOffset.Parse(completed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            return instant.ToUnixTimeMilliseconds();
        }
        catch(FormatException)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.NotIso8601, null, "The supplied value could not be parsed as an ISO 8601 timestamp.");
        }
        catch(ArgumentOutOfRangeException)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.NotIso8601, null, "The supplied value could not be parsed as an ISO 8601 timestamp.");
        }
    }

    /// <summary>
    /// Completes a bare-year ISO string (the date head carries no <c>-</c> separator, so it is only the
    /// 4-digit year) to its first day <c>year-01-01</c>, the form the universal-instant parser accepts; every
    /// other ISO form (already carrying a month, or a time) is returned unchanged.
    /// </summary>
    /// <param name="text">The ISO timestamp string, already validated against the pattern.</param>
    /// <returns>The string with a bare year completed to its first day, or the string unchanged.</returns>
    private static string CompleteBareYear(string text)
    {
        int timeSeparator = text.IndexOf('T', StringComparison.Ordinal);
        ReadOnlySpan<char> dateHead = timeSeparator < 0 ? text : text.AsSpan(0, timeSeparator);
        if(dateHead.IndexOf('-') >= 0)
        {
            //The date head already carries a month (and possibly a day), so the parser accepts it as is.
            return text;
        }

        return string.Concat(text.AsSpan(0, dateHead.Length), "-01-01", text.AsSpan(dateHead.Length));
    }

    /// <summary>Reads the first argument, or the undefined value when no argument was supplied.</summary>
    /// <param name="arguments">The argument list.</param>
    /// <returns>The first argument, or undefined.</returns>
    private static JsonataValue First(IReadOnlyList<JsonataValue> arguments)
    {
        return arguments.Count > 0 ? arguments[0] : JsonataValue.Undefined;
    }

    /// <summary>
    /// The fixed ISO-8601 validation pattern for the default-mode <c>$toMillis</c> parse, reproduced from the
    /// reference engine's <c>iso8601regex</c>: a 4-digit year, an optional month and day, an optional time, an
    /// optional fractional part, and an optional timezone offset or <c>Z</c>.
    /// </summary>
    /// <returns>The compiled ISO-8601 validation regular expression.</returns>
    [GeneratedRegex(@"^\d{4}(-[01]\d)?(-[0-3]\d)?(T[0-2]\d:[0-5]\d:[0-5]\d)?(\.\d+)?([+-][0-2]\d:?[0-5]\d|Z)?$")]
    private static partial Regex Iso8601Regex();
}
