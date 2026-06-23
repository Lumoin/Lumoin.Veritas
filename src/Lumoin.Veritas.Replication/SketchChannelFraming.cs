using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The per-message serializers the sketch-fetch transport binds to a Verisync message channel: a request carries
/// a four-byte symbol budget, a response carries the peer's sketch-image bytes verbatim. The channel itself adds
/// the length-prefix framing, so these only turn one message into bytes and back. They are pure functions of
/// their arguments — they capture nothing — so they bind as method groups without a closure, and the sketch
/// image is a self-describing, already-checksummed blob, so the response needs no encoding beyond the raw bytes.
/// </summary>
internal static class SketchChannelFraming
{
    /// <summary>Writes a symbol-budget request as a four-byte big-endian integer.</summary>
    /// <param name="symbolBudget">The requested symbol budget.</param>
    /// <param name="output">The channel buffer to write into.</param>
    internal static void WriteBudget(int symbolBudget, IBufferWriter<byte> output)
    {
        Span<byte> span = output.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(span, symbolBudget);
        output.Advance(sizeof(int));
    }

    /// <summary>Reads a symbol-budget request from a four-byte big-endian frame.</summary>
    /// <param name="payload">The framed request payload.</param>
    /// <returns>The requested symbol budget.</returns>
    /// <exception cref="InvalidDataException">The frame is shorter than the four-byte budget.</exception>
    internal static int ReadBudget(ReadOnlySequence<byte> payload)
    {
        if(payload.Length < sizeof(int))
        {
            throw new InvalidDataException("A sketch-fetch request frame is shorter than its four-byte symbol budget.");
        }

        Span<byte> span = stackalloc byte[sizeof(int)];
        payload.Slice(0, sizeof(int)).CopyTo(span);

        return BinaryPrimitives.ReadInt32BigEndian(span);
    }

    /// <summary>Writes a sketch-image response as its raw, self-describing bytes.</summary>
    /// <param name="image">The peer's sketch image.</param>
    /// <param name="output">The channel buffer to write into.</param>
    internal static void WriteImage(ReadOnlyMemory<byte> image, IBufferWriter<byte> output)
    {
        output.Write(image.Span);
    }

    /// <summary>Reads a sketch-image response — the frame's payload bytes are the image, copied into an owned array the session loads and verifies.</summary>
    /// <param name="payload">The framed response payload.</param>
    /// <returns>The peer's sketch image.</returns>
    internal static ReadOnlyMemory<byte> ReadImage(ReadOnlySequence<byte> payload)
    {
        return payload.ToArray();
    }
}
