using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Jsonata.Values;

/// <summary>
/// The structure serializer behind <c>$string</c> of an array or object. It mirrors the reference's
/// <c>JSON.stringify(arg, replacer, space)</c> in <c>functions.js</c>: every number is reduced through
/// <c>toPrecision(15)</c> and rendered with the ECMAScript <c>Number::toString</c> algorithm, every function
/// value (a lambda, built-in, regex, or other callable) renders as the empty string <c>""</c>, and a
/// non-finite number nested inside the structure raises <c>D1001</c> (the reference's <c>isNumeric</c> guard
/// inside the replacer), distinct from the <c>D3001</c> a bare top-level non-finite number raises in the
/// <c>$string</c> scalar branch.
/// </summary>
/// <remarks>
/// <para>
/// The walk is iterative — an explicit cursor stack per the no-recursion rule — and writes UTF-8 directly,
/// matching the <see cref="JsonataJsonWriter"/> idiom. The compact mode emits no insignificant whitespace; the
/// prettified mode (the reference's <c>space = 2</c>) indents each nesting level by two spaces, puts each
/// member or element on its own line, and writes <c>": "</c> after each key, while an empty object or array
/// stays on one line as <c>{}</c> or <c>[]</c>.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/string-functions#string">the JSONata <c>$string</c> reference</see>.</para>
/// </remarks>
internal static class JsonataStringSerializer
{
    /// <summary>The number of spaces one nesting level is indented in prettified output (the reference's <c>space = 2</c>).</summary>
    private const int PrettifyIndentWidth = 2;

    /// <summary>The UTF-8 bytes of the JSON <c>null</c> literal.</summary>
    private static ReadOnlySpan<byte> NullLiteral => "null"u8;

    /// <summary>The UTF-8 bytes of the JSON <c>true</c> literal.</summary>
    private static ReadOnlySpan<byte> TrueLiteral => "true"u8;

    /// <summary>The UTF-8 bytes of the JSON <c>false</c> literal.</summary>
    private static ReadOnlySpan<byte> FalseLiteral => "false"u8;

    /// <summary>Serializes an array or object value to the <c>$string</c> structure form, renting scratch from the supplied pool.</summary>
    /// <param name="value">The array or object value to serialize.</param>
    /// <param name="prettify">Whether to indent the output with two spaces per nesting level.</param>
    /// <param name="pool">The pool to rent scratch from.</param>
    /// <returns>The UTF-8 structure text.</returns>
    /// <exception cref="JsonataErrorException">A non-finite number was encountered inside the structure (code D1001).</exception>
    public static Utf8String Serialize(JsonataValue value, bool prettify, Utf8StringPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);

        PooledByteBuffer buffer = new(pool);
        try
        {
            WriteValue(ref buffer, value, prettify);

            return new Utf8String(buffer.WrittenSpan.ToArray());
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>Drives the structure walk through an explicit cursor stack, threading the prettify indentation depth.</summary>
    /// <param name="buffer">The output buffer.</param>
    /// <param name="root">The value to write.</param>
    /// <param name="prettify">Whether to indent the output.</param>
    private static void WriteValue(ref PooledByteBuffer buffer, JsonataValue root, bool prettify)
    {
        Stack<ContainerCursor> stack = new();
        WriteScalarOrOpenContainer(ref buffer, root, prettify, stack);

        while(stack.Count > 0)
        {
            ContainerCursor cursor = stack.Peek();
            if(!cursor.TryAdvance(ref buffer, prettify, out JsonataValue child))
            {
                cursor.WriteClose(ref buffer, prettify);
                stack.Pop();

                continue;
            }

            WriteScalarOrOpenContainer(ref buffer, child, prettify, stack);
        }
    }

    /// <summary>Writes a scalar value directly (numbers via <c>toPrecision(15)</c>, functions as <c>""</c>), or opens a container and pushes its cursor.</summary>
    /// <param name="buffer">The output buffer.</param>
    /// <param name="value">The value to write or open.</param>
    /// <param name="prettify">Whether to indent the output.</param>
    /// <param name="stack">The container cursor stack.</param>
    /// <exception cref="JsonataErrorException">The value is a non-finite number (code D1001).</exception>
    private static void WriteScalarOrOpenContainer(ref PooledByteBuffer buffer, JsonataValue value, bool prettify, Stack<ContainerCursor> stack)
    {
        switch(value.Kind)
        {
            case JsonataValueKind.Null:
            {
                buffer.Write(NullLiteral);

                break;
            }
            case JsonataValueKind.Boolean:
            {
                buffer.Write(value.AsBoolean ? TrueLiteral : FalseLiteral);

                break;
            }
            case JsonataValueKind.Number:
            {
                WriteNumber(ref buffer, value.AsNumber);

                break;
            }
            case JsonataValueKind.String:
            {
                WriteString(ref buffer, value.AsString);

                break;
            }
            case JsonataValueKind.Function:
            {
                //A function value inside a structure renders as the empty string (the reference's replacer).
                WriteString(ref buffer, string.Empty);

                break;
            }
            case JsonataValueKind.Array:
            {
                buffer.WriteByte((byte)'[');
                stack.Push(ContainerCursor.ForArray(value.AsArray, stack.Count));

                break;
            }
            case JsonataValueKind.Object:
            {
                buffer.WriteByte((byte)'{');
                stack.Push(ContainerCursor.ForObject(value.AsObject, stack.Count));

                break;
            }
            default:
            {
                //Undefined cannot reach here (the cursor skips it) and the internal tuple-stream carrier never
                //escapes to a user-visible value, so reaching this arm is a serializer invariant breach.
                throw new InvalidOperationException("An undefined or tuple-stream value reached the $string structure serializer.");
            }
        }
    }

    /// <summary>Writes a number using the ECMAScript <c>Number::toString</c> form with the <c>toPrecision(15)</c> reduction, raising D1001 for a non-finite value.</summary>
    /// <param name="buffer">The output buffer.</param>
    /// <param name="number">The number to write.</param>
    /// <exception cref="JsonataErrorException">The number is not finite (code D1001).</exception>
    private static void WriteNumber(ref PooledByteBuffer buffer, double number)
    {
        if(!double.IsFinite(number))
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.NumberOutOfRange, null, "A non-finite number cannot appear inside a serialized structure.");
        }

        Span<byte> scratch = stackalloc byte[EcmaScriptNumberFormatter.MaxFormattedLength];
        int written = EcmaScriptNumberFormatter.Format(number, applyToPrecision15: true, scratch);
        buffer.Write(scratch[..written]);
    }

    /// <summary>Writes a JSON string literal with RFC 8259 section 7 escaping, runs of ordinary characters encoded to UTF-8 in one pass so surrogate pairs survive.</summary>
    /// <param name="buffer">The output buffer.</param>
    /// <param name="text">The string to write.</param>
    private static void WriteString(ref PooledByteBuffer buffer, string text)
    {
        buffer.WriteByte((byte)'"');

        ReadOnlySpan<char> span = text;
        int runStart = 0;
        for(int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            if(!NeedsEscape(c))
            {
                continue;
            }

            FlushRun(ref buffer, span[runStart..i]);
            WriteEscape(ref buffer, c);
            runStart = i + 1;
        }

        FlushRun(ref buffer, span[runStart..]);
        buffer.WriteByte((byte)'"');
    }

    /// <summary>Determines whether a character requires a JSON escape (a short-form escape or a control character).</summary>
    /// <param name="c">The character to test.</param>
    /// <returns><see langword="true"/> when the character must be escaped.</returns>
    private static bool NeedsEscape(char c)
    {
        return c is '"' or '\\' || c < 0x20;
    }

    /// <summary>Encodes a run of ordinary characters to UTF-8 in one pass, keeping surrogate pairs intact.</summary>
    /// <param name="buffer">The output buffer.</param>
    /// <param name="run">The run of characters that need no escape.</param>
    private static void FlushRun(ref PooledByteBuffer buffer, ReadOnlySpan<char> run)
    {
        if(run.IsEmpty)
        {
            return;
        }

        int byteCount = Encoding.UTF8.GetByteCount(run);
        Span<byte> scratch = byteCount <= 256 ? stackalloc byte[256] : new byte[byteCount];
        int written = Encoding.UTF8.GetBytes(run, scratch);
        buffer.Write(scratch[..written]);
    }

    /// <summary>Writes the escape for a character that needs one: the short form for the seven escapes, otherwise a <c>\\uXXXX</c> control-character escape.</summary>
    /// <param name="buffer">The output buffer.</param>
    /// <param name="c">The character to escape.</param>
    private static void WriteEscape(ref PooledByteBuffer buffer, char c)
    {
        switch(c)
        {
            case '"':
            {
                buffer.Write("\\\""u8);

                break;
            }
            case '\\':
            {
                buffer.Write("\\\\"u8);

                break;
            }
            case '\b':
            {
                buffer.Write("\\b"u8);

                break;
            }
            case '\f':
            {
                buffer.Write("\\f"u8);

                break;
            }
            case '\n':
            {
                buffer.Write("\\n"u8);

                break;
            }
            case '\r':
            {
                buffer.Write("\\r"u8);

                break;
            }
            case '\t':
            {
                buffer.Write("\\t"u8);

                break;
            }
            default:
            {
                WriteUnicodeEscape(ref buffer, c);

                break;
            }
        }
    }

    /// <summary>Writes a <c>\\uXXXX</c> escape for a control character, lowercase hex.</summary>
    /// <param name="buffer">The output buffer.</param>
    /// <param name="c">The control character.</param>
    private static void WriteUnicodeEscape(ref PooledByteBuffer buffer, char c)
    {
        ReadOnlySpan<byte> hexDigits = "0123456789abcdef"u8;
        Span<byte> escape =
        [
            (byte)'\\',
            (byte)'u',
            hexDigits[(c >> 12) & 0xF],
            hexDigits[(c >> 8) & 0xF],
            hexDigits[(c >> 4) & 0xF],
            hexDigits[c & 0xF]
        ];
        buffer.Write(escape);
    }

    /// <summary>Writes a newline then a run of spaces to indent to the given nesting depth (two spaces per level).</summary>
    /// <param name="buffer">The output buffer.</param>
    /// <param name="depth">The nesting depth to indent to.</param>
    private static void WriteIndent(ref PooledByteBuffer buffer, int depth)
    {
        buffer.WriteByte((byte)'\n');
        int spaces = depth * PrettifyIndentWidth;
        for(int i = 0; i < spaces; i++)
        {
            buffer.WriteByte((byte)' ');
        }
    }

    /// <summary>A cursor over an array's or object's children, threading the comma separator and the prettify indentation.</summary>
    private sealed class ContainerCursor
    {
        /// <summary>The array items, or <see langword="null"/> for an object cursor.</summary>
        private readonly IReadOnlyList<JsonataValue>? items;

        /// <summary>The object entries, or <see langword="null"/> for an array cursor.</summary>
        private readonly IReadOnlyList<KeyValuePair<string, JsonataValue>>? entries;

        /// <summary>The container's own nesting depth: its closing token indents to this depth and its children to one deeper.</summary>
        private readonly int depth;

        /// <summary>The next child index to consider.</summary>
        private int index;

        /// <summary>Whether a value has already been written, so the next needs a leading comma.</summary>
        private bool wroteAny;

        /// <summary>Initializes a cursor over either an array or an object at a given nesting depth.</summary>
        /// <param name="items">The array items, or <see langword="null"/>.</param>
        /// <param name="entries">The object entries, or <see langword="null"/>.</param>
        /// <param name="depth">The container's own nesting depth.</param>
        private ContainerCursor(IReadOnlyList<JsonataValue>? items, IReadOnlyList<KeyValuePair<string, JsonataValue>>? entries, int depth)
        {
            this.items = items;
            this.entries = entries;
            this.depth = depth;
        }

        /// <summary>Creates a cursor over an array at a given nesting depth.</summary>
        /// <param name="items">The array items.</param>
        /// <param name="depth">The array's own nesting depth.</param>
        /// <returns>The array cursor.</returns>
        public static ContainerCursor ForArray(IReadOnlyList<JsonataValue> items, int depth)
        {
            return new ContainerCursor(items, entries: null, depth);
        }

        /// <summary>Creates a cursor over an object at a given nesting depth.</summary>
        /// <param name="entries">The object entries.</param>
        /// <param name="depth">The object's own nesting depth.</param>
        /// <returns>The object cursor.</returns>
        public static ContainerCursor ForObject(IReadOnlyList<KeyValuePair<string, JsonataValue>> entries, int depth)
        {
            return new ContainerCursor(items: null, entries, depth);
        }

        /// <summary>
        /// Advances to the next non-undefined child, writing the comma separator, the prettify indentation, and
        /// (for objects) the quoted key plus separator, then yields the child value to write.
        /// </summary>
        /// <param name="buffer">The output buffer.</param>
        /// <param name="prettify">Whether to indent the output.</param>
        /// <param name="child">On success, the next child value.</param>
        /// <returns><see langword="true"/> when a child was yielded; <see langword="false"/> at the end.</returns>
        public bool TryAdvance(ref PooledByteBuffer buffer, bool prettify, out JsonataValue child)
        {
            if(entries is not null)
            {
                return TryAdvanceObject(ref buffer, prettify, out child);
            }

            return TryAdvanceArray(ref buffer, prettify, out child);
        }

        /// <summary>Writes the closing bracket or brace, indenting it onto its own line in prettify mode when the container had any child.</summary>
        /// <param name="buffer">The output buffer.</param>
        /// <param name="prettify">Whether to indent the output.</param>
        public void WriteClose(ref PooledByteBuffer buffer, bool prettify)
        {
            if(prettify && wroteAny)
            {
                //A non-empty container in prettify mode closes on its own line at the container's own depth; an
                //empty container stays inline as {} / [].
                WriteIndent(ref buffer, depth);
            }

            buffer.WriteByte(entries is not null ? (byte)'}' : (byte)']');
        }

        /// <summary>Advances an array cursor, skipping undefined elements.</summary>
        /// <param name="buffer">The output buffer.</param>
        /// <param name="prettify">Whether to indent the output.</param>
        /// <param name="child">On success, the next element.</param>
        /// <returns><see langword="true"/> when an element was yielded.</returns>
        private bool TryAdvanceArray(ref PooledByteBuffer buffer, bool prettify, out JsonataValue child)
        {
            while(index < items!.Count)
            {
                JsonataValue candidate = items[index];
                index++;
                if(candidate.IsUndefined)
                {
                    continue;
                }

                WriteSeparator(ref buffer, prettify);
                child = candidate;

                return true;
            }

            child = JsonataValue.Undefined;

            return false;
        }

        /// <summary>Advances an object cursor, skipping members whose value is undefined.</summary>
        /// <param name="buffer">The output buffer.</param>
        /// <param name="prettify">Whether to indent the output.</param>
        /// <param name="child">On success, the next member's value.</param>
        /// <returns><see langword="true"/> when a member was yielded.</returns>
        private bool TryAdvanceObject(ref PooledByteBuffer buffer, bool prettify, out JsonataValue child)
        {
            while(index < entries!.Count)
            {
                KeyValuePair<string, JsonataValue> candidate = entries[index];
                index++;
                if(candidate.Value.IsUndefined)
                {
                    continue;
                }

                WriteSeparator(ref buffer, prettify);
                WriteString(ref buffer, candidate.Key);
                buffer.Write(prettify ? ": "u8 : ":"u8);
                child = candidate.Value;

                return true;
            }

            child = JsonataValue.Undefined;

            return false;
        }

        /// <summary>Writes the inter-child separator: a comma before any child but the first, and (in prettify mode) a newline-and-indent before every child.</summary>
        /// <param name="buffer">The output buffer.</param>
        /// <param name="prettify">Whether to indent the output.</param>
        private void WriteSeparator(ref PooledByteBuffer buffer, bool prettify)
        {
            if(wroteAny)
            {
                buffer.WriteByte((byte)',');
            }

            if(prettify)
            {
                //A child sits one nesting level deeper than its container.
                WriteIndent(ref buffer, depth + 1);
            }

            wroteAny = true;
        }
    }

}
