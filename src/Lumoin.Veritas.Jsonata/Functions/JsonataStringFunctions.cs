using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// The string built-in functions: <c>$string</c>, <c>$length</c>, <c>$substring</c>,
/// <c>$substringBefore</c>, <c>$substringAfter</c>, <c>$uppercase</c>, <c>$lowercase</c>, <c>$trim</c>,
/// <c>$pad</c>, <c>$contains</c>, <c>$split</c>, <c>$join</c>, <c>$match</c>, and <c>$replace</c>. Each
/// returns undefined for an undefined primary argument.
/// </summary>
/// <remarks>
/// <para>
/// The codepoint-counting functions (<c>$length</c>, <c>$substring</c>, <c>$pad</c>) operate on Unicode
/// scalar values (runes), so a non-BMP character counts as one, whereas <c>$split</c> with an empty
/// separator splits into UTF-16 code units to match the reference's <c>String.split('')</c>.
/// </para>
/// <para>
/// <c>$contains</c>, <c>$split</c>, <c>$match</c>, and <c>$replace</c> accept a regular-expression matcher
/// (a <see cref="JsonataRegex"/> function value) as well as a string token: the matcher branch iterates the
/// matches directly, stepping the start index past each match and raising D1004 when a continuation match is
/// zero-length (the scan would not progress). The <c>$replace</c> function-replacement branch — where the
/// replacement applies a user function per match — is driven by the evaluator (it schedules a lambda body per
/// match), so this class handles only the string-replacement branch. The second <c>prettify</c> argument of
/// <c>$string</c> stays deferred — a fragment-relative divergence from the reference.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/string-functions">the JSONata string-functions reference</see>.</para>
/// </remarks>
internal static class JsonataStringFunctions
{
    /// <summary>The string built-ins, exposed for the registry.</summary>
    public static IReadOnlyList<JsonataBuiltinFunction> All { get; } =
    [
        new JsonataBuiltinFunction(Utf8Strings.From("string"), InvokeString, JsonataSignature.Parse("<x-b?:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("length"), InvokeLength, JsonataSignature.Parse("<s-:n>")),
        new JsonataBuiltinFunction(Utf8Strings.From("substring"), InvokeSubstring, JsonataSignature.Parse("<s-nn?:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("substringBefore"), InvokeSubstringBefore, JsonataSignature.Parse("<s-s:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("substringAfter"), InvokeSubstringAfter, JsonataSignature.Parse("<s-s:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("uppercase"), InvokeUppercase, JsonataSignature.Parse("<s-:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("lowercase"), InvokeLowercase, JsonataSignature.Parse("<s-:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("trim"), InvokeTrim, JsonataSignature.Parse("<s-:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("pad"), InvokePad, JsonataSignature.Parse("<s-ns?:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("contains"), InvokeContains, JsonataSignature.Parse("<s-(sf):b>")),
        new JsonataBuiltinFunction(Utf8Strings.From("split"), InvokeSplit, JsonataSignature.Parse("<s-(sf)n?:a<s>>")),
        new JsonataBuiltinFunction(Utf8Strings.From("join"), InvokeJoin, JsonataSignature.Parse("<a<s>s?:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("match"), InvokeMatch, JsonataSignature.Parse("<s-f<s>?n?:a<o>>")),
        new JsonataBuiltinFunction(Utf8Strings.From("replace"), InvokeReplace, JsonataSignature.Parse("<s-(sf)(sf)n?:s>"))
    ];

    /// <summary>
    /// <c>$string(arg [, prettify])</c>: casts a value to its string form. A string is returned verbatim; a
    /// function (lambda, built-in, or regex) is the empty string; a number is its ECMAScript
    /// <c>Number::toString</c> form after a <c>toPrecision(15)</c> reduction; a boolean is
    /// <c>true</c>/<c>false</c>; null is <c>null</c>; an array or object is its JSON (compact, or two-space
    /// prettified when the second argument is <see langword="true"/>); undefined yields undefined.
    /// </summary>
    /// <param name="arguments">The argument list; the value to cast is the first argument, an optional prettify boolean the second.</param>
    /// <returns>The string form, or undefined for an undefined argument.</returns>
    /// <exception cref="JsonataErrorException">A bare top-level non-finite number was cast (code D3001), or a non-finite number was nested inside a serialized structure (code D1001).</exception>
    private static JsonataValue InvokeString(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        bool prettify = arguments.Count > 1 && arguments[1].Kind == JsonataValueKind.Boolean && arguments[1].AsBoolean;

        return value.Kind switch
        {
            JsonataValueKind.Undefined => JsonataValue.Undefined,
            JsonataValueKind.String => value,
            JsonataValueKind.Function => JsonataValue.String(string.Empty),
            JsonataValueKind.Null => JsonataValue.String("null"),
            JsonataValueKind.Boolean => JsonataValue.String(value.AsBoolean ? "true" : "false"),
            JsonataValueKind.Number => JsonataValue.String(ScalarNumberToString(value.AsNumber)),
            _ => JsonataValue.String(SerializeStructure(value, prettify))
        };
    }

    /// <summary>Casts a bare top-level number to its <c>$string</c> form (the ECMAScript <c>Number::toString</c> after a <c>toPrecision(15)</c> reduction), rejecting a non-finite value with D3001.</summary>
    /// <param name="number">The number to cast.</param>
    /// <returns>The string form.</returns>
    /// <exception cref="JsonataErrorException">The number is not finite (code D3001).</exception>
    private static string ScalarNumberToString(double number)
    {
        if(!double.IsFinite(number))
        {
            //A bare top-level non-finite number is the reference's explicit D3001 branch in string(); a
            //non-finite number nested inside a structure raises D1001 from the structure serializer instead.
            throw new JsonataErrorException(WellKnownJsonataErrors.NonFiniteString, null, "A non-finite number cannot be cast to a string.");
        }

        return NumberToString(number);
    }

    /// <summary>Serializes an array or object to its <c>$string</c> form (numbers reduced through <c>toPrecision(15)</c>, functions rendered as the empty string), optionally two-space prettified.</summary>
    /// <param name="value">The array or object to serialize.</param>
    /// <param name="prettify">Whether to two-space prettify the output.</param>
    /// <returns>The serialized structure.</returns>
    /// <exception cref="JsonataErrorException">A non-finite number was nested inside the structure (code D1001).</exception>
    private static string SerializeStructure(JsonataValue value, bool prettify)
    {
        using Utf8StringPool pool = new();

        return JsonataStringSerializer.Serialize(value, prettify, pool).ToString();
    }

    /// <summary><c>$length(str)</c>: the number of Unicode codepoints in a string; undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the string is the first argument.</param>
    /// <returns>The codepoint count, or undefined.</returns>
    private static JsonataValue InvokeLength(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        return JsonataValue.Number(CountRunes(value.AsString));
    }

    /// <summary>
    /// <c>$substring(str, start[, length])</c>: a substring over the codepoint array, with JS-slice
    /// semantics — a negative start counts from the end, indices clamp to the bounds, and a non-positive
    /// length is the empty string. Undefined string yields undefined.
    /// </summary>
    /// <param name="arguments">The argument list; the string, the start, and an optional length.</param>
    /// <returns>The substring, or undefined for an undefined string.</returns>
    private static JsonataValue InvokeSubstring(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        List<Rune> runes = ToRunes(value.AsString);
        int start = arguments.Count > 1 && arguments[1].Kind == JsonataValueKind.Number ? (int)arguments[1].AsNumber : 0;
        if(runes.Count + start < 0)
        {
            start = 0;
        }

        if(arguments.Count > 2 && arguments[2].Kind == JsonataValueKind.Number)
        {
            return JsonataValue.String(SubstringWithLength(runes, start, (int)arguments[2].AsNumber));
        }

        return JsonataValue.String(SubstringToEnd(runes, start));
    }

    /// <summary><c>$substringBefore(str, chars)</c>: the prefix of a string before the first occurrence of a separator, or the whole string when the separator is absent. Undefined string yields undefined.</summary>
    /// <param name="arguments">The argument list; the string and the separator.</param>
    /// <returns>The prefix, or undefined for an undefined string.</returns>
    private static JsonataValue InvokeSubstringBefore(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        string subject = value.AsString;
        string chars = SecondString(arguments);
        int index = subject.IndexOf(chars, StringComparison.Ordinal);
        if(index < 0)
        {
            return JsonataValue.String(subject);
        }

        return JsonataValue.String(subject[..index]);
    }

    /// <summary><c>$substringAfter(str, chars)</c>: the suffix of a string after the first occurrence of a separator, or the whole string when the separator is absent. Undefined string yields undefined.</summary>
    /// <param name="arguments">The argument list; the string and the separator.</param>
    /// <returns>The suffix, or undefined for an undefined string.</returns>
    private static JsonataValue InvokeSubstringAfter(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        string subject = value.AsString;
        string chars = SecondString(arguments);
        int index = subject.IndexOf(chars, StringComparison.Ordinal);
        if(index < 0)
        {
            return JsonataValue.String(subject);
        }

        return JsonataValue.String(subject[(index + chars.Length)..]);
    }

    /// <summary><c>$uppercase(str)</c>: the culture-invariant upper-case of a string; undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the string is the first argument.</param>
    /// <returns>The upper-cased string, or undefined.</returns>
    private static JsonataValue InvokeUppercase(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        return JsonataValue.String(value.AsString.ToUpperInvariant());
    }

    /// <summary><c>$lowercase(str)</c>: the culture-invariant lower-case of a string; undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the string is the first argument.</param>
    /// <returns>The lower-cased string, or undefined.</returns>
    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "The JSONata $lowercase function is defined to produce a lower-case string; the invariant lower-casing is its contract, not a normalization.")]
    private static JsonataValue InvokeLowercase(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        return JsonataValue.String(value.AsString.ToLowerInvariant());
    }

    /// <summary>
    /// <c>$trim(str)</c>: collapses each run of space, tab, line feed, and carriage return to a single space,
    /// then strips one leading and one trailing space. Undefined yields undefined; an all-whitespace string
    /// yields the empty string.
    /// </summary>
    /// <param name="arguments">The argument list; the string is the first argument.</param>
    /// <returns>The trimmed string, or undefined.</returns>
    private static JsonataValue InvokeTrim(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        return JsonataValue.String(Trim(value.AsString));
    }

    /// <summary>
    /// <c>$pad(str, width[, char])</c>: pads a string to a target codepoint width with a pad character
    /// (a single space when the pad argument is absent or empty). A positive width right-pads, a negative
    /// width left-pads; a width whose magnitude is not greater than the string's codepoint length returns the
    /// string unchanged. Undefined string yields undefined.
    /// </summary>
    /// <param name="arguments">The argument list; the string, the signed width, and an optional pad character.</param>
    /// <returns>The padded string, or undefined for an undefined string.</returns>
    private static JsonataValue InvokePad(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        string subject = value.AsString;
        int width = arguments.Count > 1 && arguments[1].Kind == JsonataValueKind.Number ? (int)Math.Truncate(arguments[1].AsNumber) : 0;
        string padChar = arguments.Count > 2 && arguments[2].Kind == JsonataValueKind.String && arguments[2].AsString.Length > 0
            ? arguments[2].AsString
            : " ";

        int padLength = Math.Abs(width) - CountRunes(subject);
        if(padLength <= 0)
        {
            return JsonataValue.String(subject);
        }

        string padding = BuildPadding(padChar, padLength);

        return JsonataValue.String(width > 0 ? subject + padding : padding + subject);
    }

    /// <summary>
    /// <c>$contains(str, token)</c>: whether a string contains a string token (ordinal) or matches a
    /// regular-expression token anywhere. An empty string token is always contained; undefined string yields
    /// undefined.
    /// </summary>
    /// <param name="arguments">The argument list; the string and the token (a string or a regex).</param>
    /// <returns>Whether the token is contained / matches, or undefined.</returns>
    private static JsonataValue InvokeContains(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        if(arguments.Count > 1 && TryGetMatcher(arguments[1], out JsonataRegex? regex))
        {
            //A regex token matches anywhere; the result is whether any match exists.
            return JsonataValue.Boolean(regex.MatchAt(value.AsString, 0) is not null);
        }

        if(arguments.Count < 2 || arguments[1].Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        return JsonataValue.Boolean(value.AsString.Contains(arguments[1].AsString, StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>$match(str, pattern[, limit])</c>: returns an array of match objects, one per non-overlapping match
    /// of a regular-expression pattern in the string, keeping at most <c>limit</c> matches. Each object is
    /// <c>{ "match": &lt;whole match&gt;, "index": &lt;start&gt;, "groups": [&lt;group 1&gt;, …] }</c>. A
    /// negative limit throws D3040; a zero limit yields the empty array; undefined string yields undefined; a
    /// continuation match of length zero throws D1004.
    /// </summary>
    /// <param name="arguments">The argument list; the string, the regex pattern, and an optional limit.</param>
    /// <returns>The array of match objects, or undefined for an undefined string.</returns>
    /// <exception cref="JsonataErrorException">The limit is negative (code D3040) or a continuation match is zero-length (code D1004).</exception>
    private static JsonataValue InvokeMatch(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        int limit = ReadMatchLimit(arguments);
        if(arguments.Count < 2 || !TryGetMatcher(arguments[1], out JsonataRegex? regex))
        {
            return JsonataValue.Undefined;
        }

        if(limit == 0)
        {
            return JsonataValue.Undefined;
        }

        string subject = value.AsString;
        List<JsonataValue> results = [];
        foreach(JsonataRegexMatch match in JsonataRegexEngine.IterateMatches(regex, subject))
        {
            if(results.Count >= limit)
            {
                break;
            }

            results.Add(JsonataRegexEngine.BuildMatchObject(match));
        }

        //The reference builds a sequence, so no match is undefined and a single match is the bare object; only
        //two or more matches surface as an array.
        return results.Count switch
        {
            0 => JsonataValue.Undefined,
            1 => results[0],
            _ => JsonataValue.Array(results)
        };
    }

    /// <summary>
    /// <c>$split(str, separator[, limit])</c>: splits a string on a string or regular-expression separator
    /// into an array of strings, keeping at most <c>limit</c> pieces and dropping the remainder. An empty
    /// string separator splits into UTF-16 code units. A negative limit throws D3020; a zero limit yields the
    /// empty array; undefined string yields undefined. A regex separator whose continuation match is
    /// zero-length throws D1004.
    /// </summary>
    /// <param name="arguments">The argument list; the string, the separator (a string or a regex), and an optional limit.</param>
    /// <returns>The array of pieces, or undefined for an undefined string.</returns>
    /// <exception cref="JsonataErrorException">The limit is negative (code D3020) or a regex continuation match is zero-length (code D1004).</exception>
    private static JsonataValue InvokeSplit(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        int limit = ReadSplitLimit(arguments);
        if(limit == 0)
        {
            return JsonataValue.Array([]);
        }

        string subject = value.AsString;
        if(arguments.Count > 1 && TryGetMatcher(arguments[1], out JsonataRegex? regex))
        {
            return JsonataValue.Array(SplitOnRegex(subject, regex, limit));
        }

        string separator = SecondString(arguments);
        List<JsonataValue> pieces = separator.Length == 0
            ? SplitIntoCodeUnits(subject, limit)
            : SplitOnSeparator(subject, separator, limit);

        return JsonataValue.Array(pieces);
    }

    /// <summary>
    /// <c>$join(array[, separator])</c>: concatenates an array's string elements with a separator (the empty
    /// string when absent). A lone string is treated as a one-element array; the empty array joins to the
    /// empty string. Undefined argument yields undefined.
    /// </summary>
    /// <param name="arguments">The argument list; the array and an optional separator.</param>
    /// <returns>The joined string, or undefined for an undefined argument.</returns>
    private static JsonataValue InvokeJoin(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.IsUndefined)
        {
            return JsonataValue.Undefined;
        }

        IReadOnlyList<JsonataValue> items = value.Kind == JsonataValueKind.Array ? value.AsArray : [value];
        string separator = arguments.Count > 1 && arguments[1].Kind == JsonataValueKind.String ? arguments[1].AsString : string.Empty;
        StringBuilder builder = new();
        for(int i = 0; i < items.Count; i++)
        {
            if(i > 0)
            {
                builder.Append(separator);
            }

            builder.Append(items[i].Kind == JsonataValueKind.String ? items[i].AsString : string.Empty);
        }

        return JsonataValue.String(builder.ToString());
    }

    /// <summary>
    /// <c>$replace(str, pattern, replacement[, limit])</c>: replaces up to <c>limit</c> non-overlapping
    /// occurrences of a string or regular-expression pattern, left to right, with a string replacement. An
    /// empty string pattern throws D3010; a negative limit throws D3011; a zero limit returns the string
    /// unchanged. Undefined string yields undefined. A regex pattern whose continuation match is zero-length
    /// throws D1004. With a regex pattern the string replacement expands <c>$0</c> (the whole match),
    /// <c>$N</c> (group N), and <c>$$</c> (a literal <c>$</c>).
    /// </summary>
    /// <param name="arguments">The argument list; the string, the pattern (a string or a regex), the string replacement, and an optional limit.</param>
    /// <returns>The replaced string, or undefined for an undefined string.</returns>
    /// <exception cref="JsonataErrorException">The pattern is empty (code D3010), the limit is negative (code D3011), or a regex continuation match is zero-length (code D1004).</exception>
    /// <remarks>
    /// The function-replacement branch (a user function applied per match) is driven by the evaluator, which
    /// schedules a lambda body per match, so it never reaches this synchronous delegate; a string pattern
    /// inserts the replacement literally with no <c>$&lt;n&gt;</c> interpretation, matching the reference's
    /// string-pattern branch.
    /// </remarks>
    private static JsonataValue InvokeReplace(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        int limit = ReadReplaceLimit(arguments);
        string replacement = arguments.Count > 2 && arguments[2].Kind == JsonataValueKind.String ? arguments[2].AsString : string.Empty;

        if(arguments.Count > 1 && TryGetMatcher(arguments[1], out JsonataRegex? regex))
        {
            if(limit == 0)
            {
                return JsonataValue.String(value.AsString);
            }

            return JsonataValue.String(ReplaceRegex(value.AsString, regex, replacement, limit));
        }

        string pattern = SecondString(arguments);
        if(pattern.Length == 0)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.ReplaceEmptyPattern, null, "The pattern of the replace function must not be an empty string.");
        }

        if(limit == 0)
        {
            return JsonataValue.String(value.AsString);
        }

        return JsonataValue.String(ReplaceLiteral(value.AsString, pattern, replacement, limit));
    }

    /// <summary>Builds a substring over a codepoint array with an explicit length, applying JS-slice negative-index and clamping rules.</summary>
    /// <param name="runes">The string's codepoints.</param>
    /// <param name="start">The start index (negative counts from the end).</param>
    /// <param name="length">The substring length in codepoints (a non-positive length is the empty string).</param>
    /// <returns>The substring.</returns>
    private static string SubstringWithLength(List<Rune> runes, int start, int length)
    {
        if(length <= 0)
        {
            return string.Empty;
        }

        int end = start >= 0 ? start + length : runes.Count + start + length;
        int from = ClampIndex(start < 0 ? runes.Count + start : start, runes.Count);
        int to = ClampIndex(end, runes.Count);
        if(to <= from)
        {
            return string.Empty;
        }

        return RunesToString(runes, from, to);
    }

    /// <summary>Builds a substring over a codepoint array from a start index (negative counts from the end) to the end of the string.</summary>
    /// <param name="runes">The string's codepoints.</param>
    /// <param name="start">The start index (negative counts from the end).</param>
    /// <returns>The substring.</returns>
    private static string SubstringToEnd(List<Rune> runes, int start)
    {
        int from = ClampIndex(start < 0 ? runes.Count + start : start, runes.Count);

        return RunesToString(runes, from, runes.Count);
    }

    /// <summary>Clamps an index into the inclusive range <c>[0, count]</c>.</summary>
    /// <param name="index">The index to clamp.</param>
    /// <param name="count">The codepoint count.</param>
    /// <returns>The clamped index.</returns>
    private static int ClampIndex(int index, int count)
    {
        if(index < 0)
        {
            return 0;
        }

        if(index > count)
        {
            return count;
        }

        return index;
    }

    /// <summary>Reassembles a half-open codepoint range to a string.</summary>
    /// <param name="runes">The string's codepoints.</param>
    /// <param name="from">The inclusive start index.</param>
    /// <param name="to">The exclusive end index.</param>
    /// <returns>The reassembled string.</returns>
    private static string RunesToString(List<Rune> runes, int from, int to)
    {
        StringBuilder builder = new();
        for(int i = from; i < to; i++)
        {
            builder.Append(runes[i].ToString());
        }

        return builder.ToString();
    }

    /// <summary>Collapses each run of space, tab, line feed, and carriage return to a single space, then strips one leading and one trailing space.</summary>
    /// <param name="text">The string to trim.</param>
    /// <returns>The trimmed string.</returns>
    private static string Trim(string text)
    {
        StringBuilder builder = new();
        bool inWhitespace = false;
        foreach(char character in text)
        {
            if(IsTrimWhitespace(character))
            {
                inWhitespace = true;

                continue;
            }

            if(inWhitespace && builder.Length > 0)
            {
                //An interior whitespace run collapses to one space; a leading run produces no space at all.
                builder.Append(' ');
            }

            inWhitespace = false;
            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>Determines whether a character is one of the four whitespace characters <c>$trim</c> collapses (space, tab, line feed, carriage return).</summary>
    /// <param name="character">The character to test.</param>
    /// <returns><see langword="true"/> when the character is a trim whitespace character.</returns>
    private static bool IsTrimWhitespace(char character)
    {
        return character is ' ' or '\t' or '\n' or '\r';
    }

    /// <summary>Builds the pad string by repeating a pad sequence and clipping it to an exact codepoint length.</summary>
    /// <param name="padChar">The pad sequence (one or more codepoints).</param>
    /// <param name="padLength">The required pad length in codepoints.</param>
    /// <returns>The padding string of exactly <paramref name="padLength"/> codepoints.</returns>
    private static string BuildPadding(string padChar, int padLength)
    {
        List<Rune> padRunes = ToRunes(padChar);
        StringBuilder builder = new();
        int produced = 0;
        int index = 0;
        while(produced < padLength)
        {
            builder.Append(padRunes[index].ToString());
            produced++;
            index++;
            if(index >= padRunes.Count)
            {
                index = 0;
            }
        }

        return builder.ToString();
    }

    /// <summary>Splits a string into single UTF-16 code units, keeping at most <c>limit</c> pieces.</summary>
    /// <param name="subject">The string to split.</param>
    /// <param name="limit">The maximum number of pieces to keep.</param>
    /// <returns>The pieces as string values.</returns>
    private static List<JsonataValue> SplitIntoCodeUnits(string subject, int limit)
    {
        List<JsonataValue> pieces = [];
        for(int i = 0; i < subject.Length && pieces.Count < limit; i++)
        {
            pieces.Add(JsonataValue.String(subject[i].ToString()));
        }

        return pieces;
    }

    /// <summary>Splits a string on a non-empty separator, keeping at most <c>limit</c> pieces and dropping the remainder.</summary>
    /// <param name="subject">The string to split.</param>
    /// <param name="separator">The non-empty separator.</param>
    /// <param name="limit">The maximum number of pieces to keep.</param>
    /// <returns>The pieces as string values.</returns>
    private static List<JsonataValue> SplitOnSeparator(string subject, string separator, int limit)
    {
        List<JsonataValue> pieces = [];
        int position = 0;
        while(pieces.Count < limit)
        {
            int next = subject.IndexOf(separator, position, StringComparison.Ordinal);
            if(next < 0)
            {
                pieces.Add(JsonataValue.String(subject[position..]));

                return pieces;
            }

            pieces.Add(JsonataValue.String(subject[position..next]));
            position = next + separator.Length;
        }

        return pieces;
    }

    /// <summary>Replaces up to a limit of non-overlapping occurrences of a literal pattern with a literal replacement, left to right.</summary>
    /// <param name="subject">The string to scan.</param>
    /// <param name="pattern">The non-empty literal pattern.</param>
    /// <param name="replacement">The literal replacement.</param>
    /// <param name="limit">The maximum number of replacements.</param>
    /// <returns>The replaced string.</returns>
    private static string ReplaceLiteral(string subject, string pattern, string replacement, int limit)
    {
        StringBuilder builder = new();
        int position = 0;
        int replaced = 0;
        while(replaced < limit)
        {
            int next = subject.IndexOf(pattern, position, StringComparison.Ordinal);
            if(next < 0)
            {
                break;
            }

            builder.Append(subject, position, next - position);
            builder.Append(replacement);
            position = next + pattern.Length;
            replaced++;
        }

        builder.Append(subject, position, subject.Length - position);

        return builder.ToString();
    }

    /// <summary>Reads the optional <c>$split</c> limit, truncated to an integer; absent or non-numeric limit is unbounded.</summary>
    /// <param name="arguments">The argument list.</param>
    /// <returns>The limit, or <see cref="int.MaxValue"/> when absent.</returns>
    /// <exception cref="JsonataErrorException">The limit is negative (code D3020).</exception>
    private static int ReadSplitLimit(IReadOnlyList<JsonataValue> arguments)
    {
        if(arguments.Count < 3 || arguments[2].Kind != JsonataValueKind.Number)
        {
            return int.MaxValue;
        }

        double raw = arguments[2].AsNumber;
        if(raw < 0)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.SplitNegativeLimit, null, "The third argument of the split function must evaluate to a positive number.");
        }

        return (int)Math.Truncate(raw);
    }

    /// <summary>Reads the optional <c>$replace</c> limit, truncated to an integer; absent or non-numeric limit is unbounded.</summary>
    /// <param name="arguments">The argument list.</param>
    /// <returns>The limit, or <see cref="int.MaxValue"/> when absent.</returns>
    /// <exception cref="JsonataErrorException">The limit is negative (code D3011).</exception>
    private static int ReadReplaceLimit(IReadOnlyList<JsonataValue> arguments)
    {
        if(arguments.Count < 4 || arguments[3].Kind != JsonataValueKind.Number)
        {
            return int.MaxValue;
        }

        double raw = arguments[3].AsNumber;
        if(raw < 0)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.ReplaceNegativeLimit, null, "The fourth argument of the replace function must evaluate to a positive number.");
        }

        return (int)Math.Truncate(raw);
    }

    /// <summary>Reads the optional <c>$match</c> limit, truncated to an integer; absent or non-numeric limit is unbounded.</summary>
    /// <param name="arguments">The argument list.</param>
    /// <returns>The limit, or <see cref="int.MaxValue"/> when absent.</returns>
    /// <exception cref="JsonataErrorException">The limit is negative (code D3040).</exception>
    private static int ReadMatchLimit(IReadOnlyList<JsonataValue> arguments)
    {
        if(arguments.Count < 3 || arguments[2].Kind != JsonataValueKind.Number)
        {
            return int.MaxValue;
        }

        double raw = arguments[2].AsNumber;
        if(raw < 0)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.MatchNegativeLimit, null, "The third argument of the match function must evaluate to a positive number.");
        }

        return (int)Math.Truncate(raw);
    }

    /// <summary>
    /// Resolves the matcher argument of a matcher-accepting string function: a regular-expression function
    /// value yields its compiled regex; any other function value is a T1010 error (this engine models a
    /// matcher only as a regular expression, not a user matcher closure); a non-function value yields
    /// <see langword="false"/> so the caller takes its string branch.
    /// </summary>
    /// <param name="value">The matcher argument.</param>
    /// <param name="regex">On success, the carried regular expression; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the value is a regular-expression function value; <see langword="false"/> when it is not a function.</returns>
    /// <exception cref="JsonataErrorException">The value is a function that is not a regular expression (code T1010).</exception>
    private static bool TryGetMatcher(JsonataValue value, [NotNullWhen(true)] out JsonataRegex? regex)
    {
        if(value.Kind == JsonataValueKind.Function)
        {
            if(value.AsFunction is JsonataRegex carried)
            {
                regex = carried;

                return true;
            }

            throw new JsonataErrorException(WellKnownJsonataErrors.NotAMatcher, null, "The matcher argument must be a regular expression.");
        }

        regex = null;

        return false;
    }

    /// <summary>Splits a string on the matches of a regular expression, keeping at most <c>limit</c> pieces.</summary>
    /// <param name="subject">The string to split.</param>
    /// <param name="regex">The separator regular expression.</param>
    /// <param name="limit">The maximum number of pieces to keep.</param>
    /// <returns>The pieces as string values.</returns>
    /// <exception cref="JsonataErrorException">A continuation match is zero-length (code D1004).</exception>
    private static List<JsonataValue> SplitOnRegex(string subject, JsonataRegex regex, int limit)
    {
        List<JsonataValue> pieces = [];
        int start = 0;
        foreach(JsonataRegexMatch match in JsonataRegexEngine.IterateMatches(regex, subject))
        {
            if(pieces.Count >= limit)
            {
                return pieces;
            }

            pieces.Add(JsonataValue.String(subject[start..match.Start]));
            start = match.End;
        }

        if(pieces.Count < limit)
        {
            pieces.Add(JsonataValue.String(subject[start..]));
        }

        return pieces;
    }

    /// <summary>Replaces up to a limit of regular-expression matches with a string replacement that expands <c>$0</c>/<c>$N</c>/<c>$$</c>.</summary>
    /// <param name="subject">The string to scan.</param>
    /// <param name="regex">The pattern regular expression.</param>
    /// <param name="replacement">The replacement string, with <c>$0</c>/<c>$N</c>/<c>$$</c> substitution.</param>
    /// <param name="limit">The maximum number of replacements.</param>
    /// <returns>The replaced string.</returns>
    /// <exception cref="JsonataErrorException">A continuation match is zero-length (code D1004).</exception>
    private static string ReplaceRegex(string subject, JsonataRegex regex, string replacement, int limit)
    {
        StringBuilder builder = new();
        int start = 0;
        int replaced = 0;
        foreach(JsonataRegexMatch match in JsonataRegexEngine.IterateMatches(regex, subject))
        {
            if(replaced >= limit)
            {
                break;
            }

            builder.Append(subject, start, match.Start - start);
            builder.Append(ExpandReplacement(replacement, match));
            start = match.End;
            replaced++;
        }

        builder.Append(subject, start, subject.Length - start);

        return builder.ToString();
    }

    /// <summary>
    /// Expands a string replacement against one match, interpreting <c>$0</c> as the whole match, <c>$N</c> as
    /// captured group N, and <c>$$</c> as a literal <c>$</c>; a <c>$</c> followed by anything else is copied
    /// literally. The group-index reading mirrors the reference: it reads up to as many digits as the group
    /// count has, backing off one digit when the larger index would exceed the group count.
    /// </summary>
    /// <param name="replacement">The replacement string.</param>
    /// <param name="match">The match supplying the whole match and the captured groups.</param>
    /// <returns>The expanded replacement text for this match.</returns>
    private static string ExpandReplacement(string replacement, JsonataRegexMatch match)
    {
        StringBuilder substitute = new();
        int position = 0;
        int index = replacement.IndexOf('$', position);
        while(index != -1 && position < replacement.Length)
        {
            substitute.Append(replacement, position, index - position);
            position = index + 1;
            char dollarValue = position < replacement.Length ? replacement[position] : '\0';
            if(dollarValue == '$')
            {
                substitute.Append('$');
                position++;
            }
            else if(dollarValue == '0')
            {
                substitute.Append(match.Match);
                position++;
            }
            else
            {
                position = ExpandGroupReference(replacement, match, position, substitute);
            }

            index = replacement.IndexOf('$', position);
        }

        substitute.Append(replacement, position, replacement.Length - position);

        return substitute.ToString();
    }

    /// <summary>
    /// Expands a <c>$N</c> group reference at <paramref name="position"/> in the replacement, appending the
    /// captured group when the index parses and names a defined group, and returning the position past the
    /// consumed digits. A non-numeric reference copies a literal <c>$</c>. The digit count read mirrors the
    /// reference: at most as many digits as the group count has, backing off one digit when the wider index
    /// exceeds the group count.
    /// </summary>
    /// <param name="replacement">The replacement string.</param>
    /// <param name="match">The match supplying the captured groups.</param>
    /// <param name="position">The position immediately after the <c>$</c>.</param>
    /// <param name="substitute">The output buffer the group value or literal <c>$</c> is appended to.</param>
    /// <returns>The position past the consumed group reference.</returns>
    private static int ExpandGroupReference(string replacement, JsonataRegexMatch match, int position, StringBuilder substitute)
    {
        int groupCount = match.Groups.Count;
        int maxDigits = groupCount == 0 ? 1 : (int)Math.Floor(Math.Log10(groupCount)) + 1;
        if(!TryParseLeadingDigits(replacement, position, maxDigits, out int index))
        {
            //A '$' not followed by digits is a literal '$'.
            substitute.Append('$');

            return position;
        }

        if(maxDigits > 1 && index > groupCount && TryParseLeadingDigits(replacement, position, maxDigits - 1, out int narrower))
        {
            index = narrower;
        }

        if(groupCount > 0 && index >= 1 && index <= groupCount)
        {
            string? group = match.Groups[index - 1];
            if(group is not null)
            {
                substitute.Append(group);
            }
        }

        return position + index.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
    }

    /// <summary>Parses the leading run of up to <paramref name="maxDigits"/> ASCII digits at a position as a non-negative integer.</summary>
    /// <param name="text">The string to read from.</param>
    /// <param name="position">The position to begin reading at.</param>
    /// <param name="maxDigits">The maximum number of digits to read.</param>
    /// <param name="value">On success, the parsed integer.</param>
    /// <returns><see langword="true"/> when at least one digit was read.</returns>
    private static bool TryParseLeadingDigits(string text, int position, int maxDigits, out int value)
    {
        value = 0;
        int read = 0;
        while(read < maxDigits && position + read < text.Length && char.IsAsciiDigit(text[position + read]))
        {
            value = (value * 10) + (text[position + read] - '0');
            read++;
        }

        return read > 0;
    }

    /// <summary>Reads the second argument as a string, or the empty string when it is absent or not a string.</summary>
    /// <param name="arguments">The argument list.</param>
    /// <returns>The second argument's string, or the empty string.</returns>
    private static string SecondString(IReadOnlyList<JsonataValue> arguments)
    {
        if(arguments.Count > 1 && arguments[1].Kind == JsonataValueKind.String)
        {
            return arguments[1].AsString;
        }

        return string.Empty;
    }

    /// <summary>Counts the Unicode codepoints (runes) in a string over a bounded enumeration.</summary>
    /// <param name="text">The string to count.</param>
    /// <returns>The codepoint count.</returns>
    private static int CountRunes(string text)
    {
        int count = 0;
        foreach(Rune _ in text.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    /// <summary>Decomposes a string into its Unicode codepoints (runes), in order.</summary>
    /// <param name="text">The string to decompose.</param>
    /// <returns>The codepoint list.</returns>
    private static List<Rune> ToRunes(string text)
    {
        List<Rune> runes = [];
        foreach(Rune rune in text.EnumerateRunes())
        {
            runes.Add(rune);
        }

        return runes;
    }

    /// <summary>Formats a finite number to the <c>$string</c> scalar form: the ECMAScript <c>Number::toString</c> algorithm after the reference's <c>toPrecision(15)</c> reduction for a non-integer.</summary>
    /// <param name="number">The finite number to format.</param>
    /// <returns>The formatted decimal form.</returns>
    private static string NumberToString(double number)
    {
        Span<byte> scratch = stackalloc byte[EcmaScriptNumberFormatter.MaxFormattedLength];
        int written = EcmaScriptNumberFormatter.Format(number, applyToPrecision15: true, scratch);

        return Encoding.UTF8.GetString(scratch[..written]);
    }

    /// <summary>Reads the first argument, or the undefined value when no argument was supplied.</summary>
    /// <param name="arguments">The argument list.</param>
    /// <returns>The first argument, or undefined.</returns>
    private static JsonataValue First(IReadOnlyList<JsonataValue> arguments)
    {
        return arguments.Count > 0 ? arguments[0] : JsonataValue.Undefined;
    }
}
