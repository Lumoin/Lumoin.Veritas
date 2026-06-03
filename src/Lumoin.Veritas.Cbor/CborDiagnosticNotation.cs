using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Lumoin.Veritas.Cbor;

/// <summary>
/// Renders a CBOR encoding as Extended Diagnostic Notation (EDN), the human-readable form of
/// <see href="https://www.rfc-editor.org/rfc/rfc8949#section-8">RFC 8949 §8</see>: integers and floats in
/// decimal, text strings quoted, byte strings as <c>h'…'</c>, arrays as <c>[…]</c>, maps as <c>{…}</c>,
/// tags as <c>n(…)</c>, and the simple values <c>true</c>/<c>false</c>/<c>null</c>/<c>undefined</c>.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="CborDiagnosticOptions.DecodeEmbeddedByteStrings"/> is set, a byte string whose content is
/// itself a single, fully-consumed CBOR item is shown as embedded CBOR — <c>&lt;&lt;inner EDN&gt;&gt;</c> —
/// rather than raw hex. This is the foundation for the diagnostic editor surface (hex ↔ decoded views).
/// </para>
/// <para>
/// The structural walk (arrays, maps, tags) is iterative over an explicit stack; only embedded-CBOR
/// decoding nests, and it is bounded by <see cref="MaxEmbeddedDepth"/> so adversarial nesting falls back to
/// hex instead of growing the call stack.
/// </para>
/// </remarks>
public static class CborDiagnosticNotation
{
    /// <summary>The maximum depth of nested embedded-CBOR (<c>&lt;&lt;…&gt;&gt;</c>) decoding; beyond it a byte string is shown as hex.</summary>
    public const int MaxEmbeddedDepth = 64;

    /// <summary>Renders a CBOR document as Extended Diagnostic Notation.</summary>
    /// <param name="cbor">The CBOR-encoded document (a single data item).</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The EDN text.</returns>
    public static string ToDiagnosticNotation(ReadOnlyMemory<byte> cbor, CborDiagnosticOptions? options = null)
    {
        CborDiagnosticOptions effective = options ?? CborDiagnosticOptions.Default;

        return Write(cbor, effective, depth: 0, out _);
    }

    /// <summary>Renders one CBOR document, reporting whether the reader consumed it completely (used to validate embedded CBOR).</summary>
    /// <param name="cbor">The CBOR bytes.</param>
    /// <param name="options">The rendering options.</param>
    /// <param name="depth">The current embedded-CBOR nesting depth.</param>
    /// <param name="fullyConsumed">On return, whether exactly one complete data item filled the whole input.</param>
    /// <returns>The EDN text.</returns>
    private static string Write(ReadOnlyMemory<byte> cbor, CborDiagnosticOptions options, int depth, out bool fullyConsumed)
    {
        CborReader reader = new(cbor, CborSerializerOptions.Default(options.Mode));
        StringBuilder builder = new();

        Stack<EdnFrame> stack = new();
        stack.Push(new EdnFrame(FrameKind.Root, count: 1, close: string.Empty));

        while(stack.Count > 0)
        {
            EdnFrame frame = stack.Peek();

            if(IsComplete(frame, reader))
            {
                builder.Append(frame.Close);
                stack.Pop();
                if(stack.Count > 0)
                {
                    //A closed container counts as one completed item of its parent.
                    stack.Peek().Emitted++;
                }

                continue;
            }

            WriteSeparator(builder, frame);

            if(!EmitItem(reader, builder, stack, options, depth))
            {
                frame.Emitted++;
            }
        }

        fullyConsumed = reader.PeekState() == CborReaderState.Finished;

        return builder.ToString();
    }

    /// <summary>Whether the container at <paramref name="frame"/> has emitted all its items (consuming an indefinite-length break when present).</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="reader">The reader (advanced past a break for a completed indefinite container).</param>
    /// <returns><see langword="true"/> when the container is complete.</returns>
    private static bool IsComplete(EdnFrame frame, CborReader reader)
    {
        switch(frame.Kind)
        {
            case FrameKind.Array:
            {
                bool done = frame.Count is int count ? frame.Emitted >= count : reader.PeekState() == CborReaderState.EndArray;
                if(done)
                {
                    //Close the container in the reader (definite or indefinite) so its nesting and end-of-data state stay in sync.
                    reader.ReadEndArray();
                }

                return done;
            }
            case FrameKind.Map:
            {
                bool done = frame.Count is int count ? frame.Emitted >= count * 2 : reader.PeekState() == CborReaderState.EndMap;
                if(done)
                {
                    reader.ReadEndMap();
                }

                return done;
            }
            default:
            {
                //Root and Tag wrap a single item and have no reader-level close.
                return frame.Emitted >= frame.Count!.Value;
            }
        }
    }

    /// <summary>Appends the separator that precedes the next item of <paramref name="frame"/>: array commas, and map key/pair separators.</summary>
    /// <param name="builder">The output builder.</param>
    /// <param name="frame">The enclosing frame.</param>
    private static void WriteSeparator(StringBuilder builder, EdnFrame frame)
    {
        switch(frame.Kind)
        {
            case FrameKind.Array when frame.Emitted > 0:
            {
                builder.Append(", ");

                break;
            }
            case FrameKind.Map when frame.Emitted > 0:
            {
                builder.Append(frame.Emitted % 2 == 0 ? ", " : ": ");

                break;
            }
            default:
            {
                break;
            }
        }
    }

    /// <summary>Emits one data item: a scalar (the caller then advances the frame) or an opening bracket plus a pushed child frame.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="builder">The output builder.</param>
    /// <param name="stack">The frame stack.</param>
    /// <param name="options">The rendering options.</param>
    /// <param name="depth">The current embedded-CBOR depth.</param>
    /// <returns><see langword="true"/> when a container was pushed (the frame is advanced on its close), <see langword="false"/> for a scalar.</returns>
    private static bool EmitItem(CborReader reader, StringBuilder builder, Stack<EdnFrame> stack, CborDiagnosticOptions options, int depth)
    {
        switch(reader.PeekState())
        {
            case CborReaderState.UnsignedInteger:
            {
                builder.Append(reader.ReadUInt64().ToString(CultureInfo.InvariantCulture));

                return false;
            }
            case CborReaderState.NegativeInteger:
            {
                BigInteger value = BigInteger.MinusOne - new BigInteger(reader.ReadCborNegativeIntegerRepresentation());
                builder.Append(value.ToString(CultureInfo.InvariantCulture));

                return false;
            }
            case CborReaderState.ByteString:
            {
                EmitByteString(reader.ReadByteString(), builder, options, depth);

                return false;
            }
            case CborReaderState.TextString:
            {
                EmitTextString(reader.ReadTextString(), builder);

                return false;
            }
            case CborReaderState.StartArray:
            {
                int? count = reader.ReadStartArray();
                builder.Append('[');
                stack.Push(new EdnFrame(FrameKind.Array, count, "]"));

                return true;
            }
            case CborReaderState.StartMap:
            {
                int? count = reader.ReadStartMap();
                builder.Append('{');
                stack.Push(new EdnFrame(FrameKind.Map, count, "}"));

                return true;
            }
            case CborReaderState.Tag:
            {
                builder.Append(reader.ReadTag().Value.ToString(CultureInfo.InvariantCulture)).Append('(');
                stack.Push(new EdnFrame(FrameKind.Tag, count: 1, ")"));

                return true;
            }
            case CborReaderState.Boolean:
            {
                builder.Append(reader.ReadBoolean() ? "true" : "false");

                return false;
            }
            case CborReaderState.Null:
            {
                reader.ReadNull();
                builder.Append("null");

                return false;
            }
            case CborReaderState.Undefined:
            {
                reader.ReadUndefined();
                builder.Append("undefined");

                return false;
            }
            case CborReaderState.SimpleValue:
            {
                builder.Append("simple(").Append(reader.ReadSimpleValue().ToString(CultureInfo.InvariantCulture)).Append(')');

                return false;
            }
            case CborReaderState.HalfPrecisionFloat:
            {
                builder.Append(FormatFloat((double)reader.ReadHalf()));

                return false;
            }
            case CborReaderState.SinglePrecisionFloat:
            {
                builder.Append(FormatFloat(reader.ReadSingle()));

                return false;
            }
            case CborReaderState.DoublePrecisionFloat:
            {
                builder.Append(FormatFloat(reader.ReadDouble()));

                return false;
            }
            default:
            {
                throw new InvalidOperationException($"Unexpected CBOR reader state '{reader.PeekState()}' while rendering diagnostic notation.");
            }
        }
    }

    /// <summary>Emits a byte string as <c>h'…'</c>, or as embedded CBOR (<c>&lt;&lt;…&gt;&gt;</c>) when enabled and the content is a single, fully-consumed item.</summary>
    /// <param name="bytes">The byte-string content.</param>
    /// <param name="builder">The output builder.</param>
    /// <param name="options">The rendering options.</param>
    /// <param name="depth">The current embedded-CBOR depth.</param>
    private static void EmitByteString(byte[] bytes, StringBuilder builder, CborDiagnosticOptions options, int depth)
    {
        if(options.DecodeEmbeddedByteStrings && bytes.Length > 0 && depth < MaxEmbeddedDepth)
        {
            try
            {
                string inner = Write(bytes, options, depth + 1, out bool fullyConsumed);
                if(fullyConsumed)
                {
                    builder.Append("<<").Append(inner).Append(">>");

                    return;
                }
            }
            catch(Exception exception) when(exception is FormatException or OverflowException or InvalidOperationException or CborSizeLimitExceededException)
            {
                //The content is not valid embedded CBOR; fall through to the hex rendering.
            }
        }

        builder.Append("h'").Append(Convert.ToHexStringLower(bytes)).Append('\'');
    }

    /// <summary>Emits a text string as a JSON-style quoted, escaped literal.</summary>
    /// <param name="text">The text-string content.</param>
    /// <param name="builder">The output builder.</param>
    private static void EmitTextString(string text, StringBuilder builder)
    {
        builder.Append('"');
        foreach(char character in text)
        {
            switch(character)
            {
                case '"':
                {
                    builder.Append("\\\"");

                    break;
                }
                case '\\':
                {
                    builder.Append("\\\\");

                    break;
                }
                case '\n':
                {
                    builder.Append("\\n");

                    break;
                }
                case '\r':
                {
                    builder.Append("\\r");

                    break;
                }
                case '\t':
                {
                    builder.Append("\\t");

                    break;
                }
                case < ' ':
                {
                    builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));

                    break;
                }
                default:
                {
                    builder.Append(character);

                    break;
                }
            }
        }

        builder.Append('"');
    }

    /// <summary>Formats a floating-point value the EDN way: special names for the non-finite values, and a decimal point forced onto integral values so they read as floats.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The EDN literal.</returns>
    private static string FormatFloat(double value)
    {
        if(double.IsNaN(value))
        {
            return "NaN";
        }

        if(double.IsPositiveInfinity(value))
        {
            return "Infinity";
        }

        if(double.IsNegativeInfinity(value))
        {
            return "-Infinity";
        }

        string text = value.ToString("R", CultureInfo.InvariantCulture);

        return text.Contains('.', StringComparison.Ordinal) || text.Contains('e', StringComparison.OrdinalIgnoreCase)
            ? text
            : text + ".0";
    }

    /// <summary>The kind of EDN container a frame represents.</summary>
    private enum FrameKind
    {
        /// <summary>The synthetic top-level frame holding the single document item.</summary>
        Root,

        /// <summary>An array <c>[…]</c>.</summary>
        Array,

        /// <summary>A map <c>{…}</c>.</summary>
        Map,

        /// <summary>A tag <c>n(…)</c> wrapping a single item.</summary>
        Tag
    }

    /// <summary>One open container on the rendering stack.</summary>
    private sealed class EdnFrame
    {
        /// <summary>Initializes a frame.</summary>
        /// <param name="kind">The container kind.</param>
        /// <param name="count">The definite item count (pairs for a map), or <see langword="null"/> for an indefinite-length container.</param>
        /// <param name="close">The closing token to emit.</param>
        public EdnFrame(FrameKind kind, int? count, string close)
        {
            Kind = kind;
            Count = count;
            Close = close;
        }

        /// <summary>Gets the container kind.</summary>
        public FrameKind Kind { get; }

        /// <summary>Gets the definite item count (map pairs), or <see langword="null"/> for indefinite length.</summary>
        public int? Count { get; }

        /// <summary>Gets the closing token.</summary>
        public string Close { get; }

        /// <summary>Gets or sets the number of items emitted so far (a map counts keys and values separately).</summary>
        public int Emitted { get; set; }
    }
}
