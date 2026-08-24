using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Functions;

/// <summary>
/// The encoding built-in functions: <c>$base64encode</c> / <c>$base64decode</c> and the URI pair
/// <c>$encodeUrl</c> / <c>$decodeUrl</c> (whole-URI) and <c>$encodeUrlComponent</c> /
/// <c>$decodeUrlComponent</c> (component). Each returns undefined for an undefined argument.
/// </summary>
/// <remarks>
/// <para>
/// Base64 follows the reference's <c>btoa</c> / <c>atob</c>: the string is the Latin-1 (binary) byte
/// sequence, so each character is one byte (a character above U+00FF is outside the range these functions
/// model). The URI functions reproduce the ECMAScript <c>encodeURI</c> / <c>encodeURIComponent</c> and
/// <c>decodeURI</c> / <c>decodeURIComponent</c> unreserved sets exactly — a character outside the unreserved
/// set is percent-escaped as its UTF-8 bytes — rather than delegating to <see cref="System.Uri"/>, whose
/// RFC 3986 set escapes a different selection (for example <c>!*'()</c>). An unencodable input (a lone
/// surrogate) or a malformed percent-escape raises D3140.
/// </para>
/// <para>
/// <c>$decodeUrl</c> does not yet preserve the reserved set the ECMAScript <c>decodeURI</c> leaves escaped
/// (it decodes every percent-escape, like <c>decodeURIComponent</c>); no reserved-preservation case is in
/// the corpus. This is a fragment-relative divergence from the reference.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/string-functions">the JSONata string-functions reference</see>.</para>
/// </remarks>
internal static class JsonataEncodingFunctions
{
    /// <summary>The UTF-8 codec that throws on a lone surrogate (encode) or an invalid byte sequence (decode), so a malformed URI surfaces as D3140 rather than a replacement character.</summary>
    private static UTF8Encoding StrictUtf8 { get; } = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The encoding built-ins, exposed for the registry.</summary>
    public static IReadOnlyList<JsonataBuiltinFunction> All { get; } =
    [
        new JsonataBuiltinFunction(Utf8Strings.From("base64encode"), InvokeBase64Encode, JsonataSignature.Parse("<s-:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("base64decode"), InvokeBase64Decode, JsonataSignature.Parse("<s-:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("encodeUrlComponent"), InvokeEncodeUrlComponent, JsonataSignature.Parse("<s-:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("encodeUrl"), InvokeEncodeUrl, JsonataSignature.Parse("<s-:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("decodeUrlComponent"), InvokeDecodeUrlComponent, JsonataSignature.Parse("<s-:s>")),
        new JsonataBuiltinFunction(Utf8Strings.From("decodeUrl"), InvokeDecodeUrl, JsonataSignature.Parse("<s-:s>"))
    ];

    /// <summary><c>$base64encode(str)</c>: the Base64 of the string's Latin-1 byte sequence; undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the string is the first argument.</param>
    /// <returns>The Base64 text, or undefined.</returns>
    private static JsonataValue InvokeBase64Encode(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        return JsonataValue.String(System.Convert.ToBase64String(Encoding.Latin1.GetBytes(value.AsString)));
    }

    /// <summary><c>$base64decode(str)</c>: the Latin-1 string of the Base64-decoded bytes; undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the Base64 string is the first argument.</param>
    /// <returns>The decoded string, or undefined.</returns>
    private static JsonataValue InvokeBase64Decode(IReadOnlyList<JsonataValue> arguments)
    {
        JsonataValue value = First(arguments);
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        return JsonataValue.String(Encoding.Latin1.GetString(System.Convert.FromBase64String(value.AsString)));
    }

    /// <summary><c>$encodeUrlComponent(str)</c>: percent-encodes a URI component (the <c>encodeURIComponent</c> set); undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the string is the first argument.</param>
    /// <returns>The encoded component, or undefined.</returns>
    /// <exception cref="JsonataErrorException">The string cannot be UTF-8 encoded — a lone surrogate (code D3140).</exception>
    private static JsonataValue InvokeEncodeUrlComponent(IReadOnlyList<JsonataValue> arguments)
    {
        return EncodeUri(First(arguments), wholeUri: false);
    }

    /// <summary><c>$encodeUrl(str)</c>: percent-encodes a whole URI (the <c>encodeURI</c> set, leaving the reserved characters); undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the string is the first argument.</param>
    /// <returns>The encoded URI, or undefined.</returns>
    /// <exception cref="JsonataErrorException">The string cannot be UTF-8 encoded — a lone surrogate (code D3140).</exception>
    private static JsonataValue InvokeEncodeUrl(IReadOnlyList<JsonataValue> arguments)
    {
        return EncodeUri(First(arguments), wholeUri: true);
    }

    /// <summary><c>$decodeUrlComponent(str)</c>: decodes the percent-escapes of a URI component; undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the string is the first argument.</param>
    /// <returns>The decoded component, or undefined.</returns>
    /// <exception cref="JsonataErrorException">A percent-escape is malformed or decodes to invalid UTF-8 (code D3140).</exception>
    private static JsonataValue InvokeDecodeUrlComponent(IReadOnlyList<JsonataValue> arguments)
    {
        return DecodeUri(First(arguments));
    }

    /// <summary><c>$decodeUrl(str)</c>: decodes the percent-escapes of a whole URI; undefined yields undefined.</summary>
    /// <param name="arguments">The argument list; the string is the first argument.</param>
    /// <returns>The decoded URI, or undefined.</returns>
    /// <exception cref="JsonataErrorException">A percent-escape is malformed or decodes to invalid UTF-8 (code D3140).</exception>
    private static JsonataValue InvokeDecodeUrl(IReadOnlyList<JsonataValue> arguments)
    {
        return DecodeUri(First(arguments));
    }

    /// <summary>
    /// Percent-encodes a string against the requested unreserved set: the string is UTF-8 encoded (a lone
    /// surrogate raises D3140), then each byte that is in the unreserved set is kept as its character and every
    /// other byte is written as <c>%XX</c> with upper-case hexadecimal.
    /// </summary>
    /// <param name="value">The value to encode; a non-string yields undefined.</param>
    /// <param name="wholeUri"><see langword="true"/> for the whole-URI (<c>encodeURI</c>) set; <see langword="false"/> for the component (<c>encodeURIComponent</c>) set.</param>
    /// <returns>The percent-encoded string, or undefined.</returns>
    /// <exception cref="JsonataErrorException">The string cannot be UTF-8 encoded — a lone surrogate (code D3140).</exception>
    private static JsonataValue EncodeUri(JsonataValue value, bool wholeUri)
    {
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(value.AsString);
        }
        catch(EncoderFallbackException)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.MalformedUri, null, "Malformed URL passed to an encode function.");
        }

        StringBuilder builder = new(bytes.Length);
        foreach(byte b in bytes)
        {
            if(IsUnreserved(b, wholeUri))
            {
                builder.Append((char)b);

                continue;
            }

            builder.Append('%');
            builder.Append(ToHex(b >> 4));
            builder.Append(ToHex(b & 0xF));
        }

        return JsonataValue.String(builder.ToString());
    }

    /// <summary>
    /// Decodes the percent-escapes of a string: each <c>%XX</c> is parsed to a byte and each other character
    /// contributes its own UTF-8 bytes, then the whole byte sequence is decoded as UTF-8. A percent sign not
    /// followed by two hexadecimal digits, or a byte sequence that is not valid UTF-8, raises D3140.
    /// </summary>
    /// <param name="value">The value to decode; a non-string yields undefined.</param>
    /// <returns>The decoded string, or undefined.</returns>
    /// <exception cref="JsonataErrorException">A percent-escape is malformed or decodes to invalid UTF-8 (code D3140).</exception>
    private static JsonataValue DecodeUri(JsonataValue value)
    {
        if(value.Kind != JsonataValueKind.String)
        {
            return JsonataValue.Undefined;
        }

        string text = value.AsString;
        List<byte> bytes = new(text.Length);
        int i = 0;
        while(i < text.Length)
        {
            char c = text[i];
            if(c == '%')
            {
                if(i + 2 >= text.Length || !TryHex(text[i + 1], out int high) || !TryHex(text[i + 2], out int low))
                {
                    throw new JsonataErrorException(WellKnownJsonataErrors.MalformedUri, null, "Malformed URL passed to a decode function.");
                }

                bytes.Add((byte)((high << 4) | low));
                i += 3;

                continue;
            }

            i = AppendLiteral(bytes, text, i);
        }

        try
        {
            return JsonataValue.String(StrictUtf8.GetString([.. bytes]));
        }
        catch(DecoderFallbackException)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.MalformedUri, null, "Malformed URL passed to a decode function.");
        }
    }

    /// <summary>
    /// Appends the UTF-8 bytes of the literal (non-escaped) character at the cursor: an ASCII character is one
    /// byte, and a non-ASCII character (a whole surrogate pair when present) is encoded through the strict
    /// codec. Returns the cursor past the consumed character(s).
    /// </summary>
    /// <param name="bytes">The byte accumulator.</param>
    /// <param name="text">The source text.</param>
    /// <param name="index">The cursor at the literal character.</param>
    /// <returns>The cursor past the consumed character(s).</returns>
    private static int AppendLiteral(List<byte> bytes, string text, int index)
    {
        char c = text[index];
        if(c < (char)128)
        {
            bytes.Add((byte)c);

            return index + 1;
        }

        int charCount = char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
        bytes.AddRange(StrictUtf8.GetBytes(text.Substring(index, charCount)));

        return index + charCount;
    }

    /// <summary>Determines whether a byte is in the requested unreserved set (alphanumerics, the shared marks, and — for a whole URI — the reserved characters).</summary>
    /// <param name="b">The byte to test.</param>
    /// <param name="wholeUri"><see langword="true"/> to also admit the whole-URI reserved characters.</param>
    /// <returns><see langword="true"/> when the byte should be kept rather than percent-escaped.</returns>
    private static bool IsUnreserved(byte b, bool wholeUri)
    {
        //The ECMAScript unreserved marks (beyond the alphanumerics) that neither encodeURI nor
        //encodeURIComponent escapes, then the extra characters encodeURI additionally leaves (the URI reserved
        //set plus '#') which encodeURIComponent escapes.
        ReadOnlySpan<byte> componentMarks = "-_.!~*'()"u8;
        ReadOnlySpan<byte> wholeUriMarks = ";,/?:@&=+$#"u8;
        bool basic = b is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9')
            || componentMarks.IndexOf(b) >= 0;

        return basic || (wholeUri && wholeUriMarks.IndexOf(b) >= 0);
    }

    /// <summary>Maps a 0-15 nibble to its upper-case hexadecimal character.</summary>
    /// <param name="nibble">The nibble value (0-15).</param>
    /// <returns>The hexadecimal character.</returns>
    private static char ToHex(int nibble)
    {
        return (char)(nibble < 10 ? '0' + nibble : 'A' + (nibble - 10));
    }

    /// <summary>Parses a hexadecimal digit to its 0-15 value.</summary>
    /// <param name="c">The candidate hexadecimal digit.</param>
    /// <param name="value">The parsed value on success; zero otherwise.</param>
    /// <returns><see langword="true"/> when the character is a hexadecimal digit.</returns>
    private static bool TryHex(char c, out int value)
    {
        if(c is >= '0' and <= '9')
        {
            value = c - '0';

            return true;
        }

        if(c is >= 'A' and <= 'F')
        {
            value = c - 'A' + 10;

            return true;
        }

        if(c is >= 'a' and <= 'f')
        {
            value = c - 'a' + 10;

            return true;
        }

        value = 0;

        return false;
    }

    /// <summary>Reads the first argument, or the undefined value when no argument was supplied.</summary>
    /// <param name="arguments">The argument list.</param>
    /// <returns>The first argument, or undefined.</returns>
    private static JsonataValue First(IReadOnlyList<JsonataValue> arguments)
    {
        return arguments.Count > 0 ? arguments[0] : JsonataValue.Undefined;
    }
}
