using System;
using System.Buffers;

namespace Lumoin.Veritas.Core.Encoding;

/// <summary>
/// Byte-native UTF-8 text-writing extensions over <see cref="IBufferWriter{T}"/>, the
/// format-layer replacement for a <see cref="System.IO.StreamWriter"/> wrapped around a
/// <see cref="System.IO.Stream"/>. Each method emits directly into the writer's own buffer
/// via <see cref="IBufferWriter{T}.GetSpan(int)"/> and <see cref="IBufferWriter{T}.Advance(int)"/>,
/// so any sink — a <see cref="System.IO.Pipelines.PipeWriter"/>, an
/// <see cref="ArrayBufferWriter{T}"/>, or a pooled buffer — works with no intermediate stream.
/// </summary>
/// <remarks>
/// The enclosing namespace name shadows <see cref="System.Text.Encoding"/>, so the
/// <c>System.Text.Encoding.UTF8</c> encoder is named fully qualified throughout.
/// </remarks>
public static class Utf8BufferWriter
{
    /// <summary>
    /// Copies a pre-encoded UTF-8 literal — typically a <c>u8</c> span such as <c>"&lt;"u8</c> —
    /// into <paramref name="writer"/> verbatim, with no re-encoding.
    /// </summary>
    /// <param name="writer">The destination buffer writer.</param>
    /// <param name="literal">The pre-encoded UTF-8 bytes to copy; an empty span writes nothing.</param>
    public static void WriteUtf8Literal(this IBufferWriter<byte> writer, ReadOnlySpan<byte> literal)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if(literal.IsEmpty)
        {
            return;
        }

        Span<byte> destination = writer.GetSpan(literal.Length);
        literal.CopyTo(destination);
        writer.Advance(literal.Length);
    }

    /// <summary>
    /// Writes one byte — intended for a single ASCII delimiter such as a quote, colon, or
    /// newline — into <paramref name="writer"/>.
    /// </summary>
    /// <param name="writer">The destination buffer writer.</param>
    /// <param name="value">The byte to write.</param>
    public static void WriteByte(this IBufferWriter<byte> writer, byte value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> destination = writer.GetSpan(1);
        destination[0] = value;
        writer.Advance(1);
    }

    /// <summary>
    /// Encodes a UTF-16 character run to UTF-8 in a single pass and writes it into
    /// <paramref name="writer"/>. Encoding the whole run at once keeps surrogate pairs paired,
    /// so non-BMP characters survive; a run that may contain a surrogate pair must not be split
    /// across calls.
    /// </summary>
    /// <param name="writer">The destination buffer writer.</param>
    /// <param name="text">The characters to encode; an empty span writes nothing.</param>
    public static void WriteUtf8(this IBufferWriter<byte> writer, ReadOnlySpan<char> text)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if(text.IsEmpty)
        {
            return;
        }

        int byteCount = System.Text.Encoding.UTF8.GetByteCount(text);
        Span<byte> destination = writer.GetSpan(byteCount);
        int written = System.Text.Encoding.UTF8.GetBytes(text, destination);
        writer.Advance(written);
    }

    /// <summary>
    /// Encodes a UTF-16 string to UTF-8 and writes it into <paramref name="writer"/>; a
    /// convenience overload of <see cref="WriteUtf8(IBufferWriter{byte}, ReadOnlySpan{char})"/>.
    /// </summary>
    /// <param name="writer">The destination buffer writer.</param>
    /// <param name="text">The string to encode; <see langword="null"/> or empty writes nothing.</param>
    public static void WriteUtf8(this IBufferWriter<byte> writer, string? text)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if(string.IsNullOrEmpty(text))
        {
            return;
        }

        WriteUtf8(writer, text.AsSpan());
    }

    /// <summary>
    /// Copies the already-UTF-8 bytes of a <see cref="Utf8String"/> into <paramref name="writer"/>
    /// verbatim, with no re-encoding.
    /// </summary>
    /// <param name="writer">The destination buffer writer.</param>
    /// <param name="value">The UTF-8 string whose <see cref="Utf8String.Span"/> is copied.</param>
    public static void WriteUtf8String(this IBufferWriter<byte> writer, Utf8String value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        WriteUtf8Literal(writer, value.Span);
    }
}
