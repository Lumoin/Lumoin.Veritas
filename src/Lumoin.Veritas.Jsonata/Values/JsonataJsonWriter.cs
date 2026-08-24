using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Jsonata.Values;

/// <summary>
/// A hand-rolled RFC 8259 UTF-8 JSON writer over the <see cref="JsonataValue"/> union. The engine is
/// banned from <c>System.Text.Json</c>, so this writer is the self-contained serializer behind
/// <see cref="SerializeJsonataDelegate"/>.
/// </summary>
/// <remarks>
/// <para>
/// The walk is iterative — an explicit stack per the no-recursion rule. Scratch is rented from a
/// <see cref="Utf8StringPool"/> rather than <see cref="ArrayPool{T}.Shared"/>. A top-level
/// <see cref="JsonataValueKind.Undefined"/> serializes to no bytes; undefined members and elements are
/// omitted from objects and arrays; numbers use the shortest round-trip form with integers having no
/// decimal point; strings are escaped per RFC 8259 section 7; null is distinct from the omitted
/// undefined.
/// </para>
/// </remarks>
public static class JsonataJsonWriter
{
    /// <summary>The size, in bytes, of the on-stack scratch used to encode a run of ordinary string characters.</summary>
    private const int MaxStringRunScratch = 256;

    /// <summary>The UTF-8 bytes of the JSON <c>null</c> literal.</summary>
    private static ReadOnlySpan<byte> NullLiteral => "null"u8;

    /// <summary>The UTF-8 bytes of the JSON <c>true</c> literal.</summary>
    private static ReadOnlySpan<byte> TrueLiteral => "true"u8;

    /// <summary>The UTF-8 bytes of the JSON <c>false</c> literal.</summary>
    private static ReadOnlySpan<byte> FalseLiteral => "false"u8;

    /// <summary>
    /// Serializes a JSONata value to RFC 8259 UTF-8 JSON, using a private pool for scratch. Suitable as
    /// a <see cref="SerializeJsonataDelegate"/>.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The UTF-8 JSON text; empty for the undefined value.</returns>
    /// <exception cref="JsonataErrorException">A function value was encountered (no JSON representation), or a non-finite number was written (code D3001).</exception>
    public static Utf8String Serialize(JsonataValue value)
    {
        using Utf8StringPool pool = new();

        return Serialize(value, pool);
    }

    /// <summary>Serializes a JSONata value to RFC 8259 UTF-8 JSON, renting scratch from the supplied pool.</summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="pool">The pool to rent scratch from.</param>
    /// <returns>The UTF-8 JSON text; empty for the undefined value.</returns>
    /// <exception cref="JsonataErrorException">A function value was encountered (no JSON representation), or a non-finite number was written (code D3001).</exception>
    public static Utf8String Serialize(JsonataValue value, Utf8StringPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);

        if(value.IsUndefined)
        {
            return Utf8Strings.From(string.Empty);
        }

        PooledByteBuffer buffer = new(pool);
        try
        {
            WriteValue(ref buffer, value);

            return new Utf8String(buffer.WrittenSpan.ToArray());
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>Writes one value, driving nested arrays/objects through an explicit stack of cursors.</summary>
    /// <param name="buffer">The output buffer.</param>
    /// <param name="root">The value to write.</param>
    private static void WriteValue(ref PooledByteBuffer buffer, JsonataValue root)
    {
        //An explicit cursor stack replaces recursion: each pending container holds its remaining
        //children and whether the next child needs a leading comma.
        Stack<ContainerCursor> stack = new();
        WriteScalarOrOpenContainer(ref buffer, root, stack);

        while(stack.Count > 0)
        {
            ContainerCursor cursor = stack.Peek();
            if(!cursor.TryAdvance(ref buffer, out JsonataValue child))
            {
                buffer.WriteByte(cursor.IsObject ? (byte)'}' : (byte)']');
                stack.Pop();

                continue;
            }

            WriteScalarOrOpenContainer(ref buffer, child, stack);
        }
    }

    /// <summary>Writes a scalar value directly, or opens a container and pushes its cursor.</summary>
    /// <param name="buffer">The output buffer.</param>
    /// <param name="value">The value to write or open.</param>
    /// <param name="stack">The container cursor stack.</param>
    private static void WriteScalarOrOpenContainer(ref PooledByteBuffer buffer, JsonataValue value, Stack<ContainerCursor> stack)
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
            case JsonataValueKind.Array:
            {
                buffer.WriteByte((byte)'[');
                stack.Push(ContainerCursor.ForArray(value.AsArray));

                break;
            }
            case JsonataValueKind.Object:
            {
                buffer.WriteByte((byte)'{');
                stack.Push(ContainerCursor.ForObject(value.AsObject));

                break;
            }
            case JsonataValueKind.Undefined:
            {
                //An undefined element/member is dropped by the cursor before it reaches here; a bare
                //undefined value is handled by the caller. Reaching here is a writer invariant breach.
                throw new InvalidOperationException("An undefined JSONata value reached the JSON writer.");
            }
            case JsonataValueKind.TupleStream:
            {
                //The internal tuple-stream carrier is consumed by its enclosing path step; reaching the writer
                //means it escaped — a writer invariant breach, not a user-facing function-value rejection.
                throw new InvalidOperationException("The internal tuple-stream carrier reached the JSON writer; it must be consumed by its enclosing path step.");
            }
            default:
            {
                throw new JsonataErrorException("A JSONata function value has no JSON representation.");
            }
        }
    }

    /// <summary>Writes a number in the ECMAScript <c>Number::toString</c> form (the JSON.stringify number form, no <c>toPrecision</c> reduction); rejects non-finite values.</summary>
    /// <param name="buffer">The output buffer.</param>
    /// <param name="number">The number to write.</param>
    /// <exception cref="JsonataErrorException">The number is not finite (code D3001).</exception>
    private static void WriteNumber(ref PooledByteBuffer buffer, double number)
    {
        if(!double.IsFinite(number))
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.NonFiniteString, null, "A non-finite number cannot be serialized to JSON.");
        }

        Span<byte> scratch = stackalloc byte[EcmaScriptNumberFormatter.MaxFormattedLength];
        int written = EcmaScriptNumberFormatter.Format(number, applyToPrecision15: false, scratch);
        buffer.Write(scratch[..written]);
    }

    /// <summary>
    /// Writes a JSON string literal with RFC 8259 section 7 escaping. Runs of ordinary characters are
    /// encoded to UTF-8 in one pass so surrogate pairs stay paired (non-BMP characters survive); the run
    /// breaks only for the seven short-form escapes and control characters.
    /// </summary>
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
        Span<byte> scratch = byteCount <= MaxStringRunScratch ? stackalloc byte[MaxStringRunScratch] : new byte[byteCount];
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

    /// <summary>A cursor over an array's or object's remaining children, threading the comma separator.</summary>
    private sealed class ContainerCursor
    {
        /// <summary>The array items, or <see langword="null"/> for an object cursor.</summary>
        private readonly IReadOnlyList<JsonataValue>? items;

        /// <summary>The object entries, or <see langword="null"/> for an array cursor.</summary>
        private readonly IReadOnlyList<KeyValuePair<string, JsonataValue>>? entries;

        /// <summary>The next child index to consider.</summary>
        private int index;

        /// <summary>Whether a value has already been written, so the next needs a leading comma.</summary>
        private bool wroteAny;

        /// <summary>Initializes a cursor over either an array or an object.</summary>
        /// <param name="items">The array items, or <see langword="null"/>.</param>
        /// <param name="entries">The object entries, or <see langword="null"/>.</param>
        private ContainerCursor(IReadOnlyList<JsonataValue>? items, IReadOnlyList<KeyValuePair<string, JsonataValue>>? entries)
        {
            this.items = items;
            this.entries = entries;
        }

        /// <summary>Gets a value indicating whether this cursor is over an object.</summary>
        public bool IsObject => entries is not null;

        /// <summary>Creates a cursor over an array.</summary>
        /// <param name="items">The array items.</param>
        /// <returns>The array cursor.</returns>
        public static ContainerCursor ForArray(IReadOnlyList<JsonataValue> items)
        {
            return new ContainerCursor(items, entries: null);
        }

        /// <summary>Creates a cursor over an object.</summary>
        /// <param name="entries">The object entries.</param>
        /// <returns>The object cursor.</returns>
        public static ContainerCursor ForObject(IReadOnlyList<KeyValuePair<string, JsonataValue>> entries)
        {
            return new ContainerCursor(items: null, entries);
        }

        /// <summary>
        /// Advances to the next non-undefined child, writing the comma separator and (for objects) the
        /// quoted key plus colon, and yields the child value to write.
        /// </summary>
        /// <param name="buffer">The output buffer.</param>
        /// <param name="child">On success, the next child value.</param>
        /// <returns><see langword="true"/> when a child was yielded; <see langword="false"/> at the end.</returns>
        public bool TryAdvance(ref PooledByteBuffer buffer, out JsonataValue child)
        {
            if(entries is not null)
            {
                return TryAdvanceObject(ref buffer, out child);
            }

            return TryAdvanceArray(ref buffer, out child);
        }

        /// <summary>Advances an array cursor, skipping undefined elements.</summary>
        /// <param name="buffer">The output buffer.</param>
        /// <param name="child">On success, the next element.</param>
        /// <returns><see langword="true"/> when an element was yielded.</returns>
        private bool TryAdvanceArray(ref PooledByteBuffer buffer, out JsonataValue child)
        {
            while(index < items!.Count)
            {
                JsonataValue candidate = items[index];
                index++;
                if(candidate.IsUndefined)
                {
                    continue;
                }

                if(wroteAny)
                {
                    buffer.WriteByte((byte)',');
                }

                wroteAny = true;
                child = candidate;

                return true;
            }

            child = JsonataValue.Undefined;

            return false;
        }

        /// <summary>Advances an object cursor, skipping members whose value is undefined.</summary>
        /// <param name="buffer">The output buffer.</param>
        /// <param name="child">On success, the next member's value.</param>
        /// <returns><see langword="true"/> when a member was yielded.</returns>
        private bool TryAdvanceObject(ref PooledByteBuffer buffer, out JsonataValue child)
        {
            while(index < entries!.Count)
            {
                KeyValuePair<string, JsonataValue> candidate = entries[index];
                index++;
                if(candidate.Value.IsUndefined)
                {
                    continue;
                }

                if(wroteAny)
                {
                    buffer.WriteByte((byte)',');
                }

                wroteAny = true;
                WriteString(ref buffer, candidate.Key);
                buffer.WriteByte((byte)':');
                child = candidate.Value;

                return true;
            }

            child = JsonataValue.Undefined;

            return false;
        }
    }

}
