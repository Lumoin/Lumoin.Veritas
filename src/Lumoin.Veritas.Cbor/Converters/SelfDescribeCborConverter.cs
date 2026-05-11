using System;
using System.Globalization;

namespace Lumoin.Veritas.Cbor.Converters;

/// <summary>
/// Helpers for the CBOR self-described tag (Tag 55799, RFC 8949 §3.4.6).
/// The tag carries no payload semantics: it announces that the bytes that
/// follow are CBOR. Producers prepend the tag to a top-level data item
/// when downstream consumers might be unsure whether the byte stream is
/// CBOR; consumers strip it transparently.
/// </summary>
/// <remarks>
/// The shape is not a typed converter because the self-described tag
/// wraps an arbitrary value rather than mapping to a single .NET type.
/// Use <see cref="WritePrefix"/> just before emitting the wrapped value
/// and <see cref="ConsumePrefix"/> just before reading it.
/// </remarks>
public static class SelfDescribeCborConverter
{
    /// <summary>
    /// Writes the Tag 55799 prefix to <paramref name="writer"/>. The next
    /// data item the writer emits becomes the tagged content.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    public static void WritePrefix(CborWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteTag(CborTag.SelfDescribe);
    }

    /// <summary>
    /// Consumes a Tag 55799 prefix from <paramref name="reader"/> if one is
    /// present at the current position, leaving the reader positioned on
    /// the wrapped data item. If no Tag 55799 prefix is present this is
    /// a no-op so callers can use it transparently regardless of whether
    /// the input was self-described.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <returns><c>true</c> if a self-describe prefix was consumed; <c>false</c> otherwise.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <c>null</c>.</exception>
    public static bool ConsumePrefix(CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if(reader.PeekState() != CborReaderState.Tag)
        {
            return false;
        }

        //Peeking the tag value is intrusive, so we read it and verify it matches.
        //If the tag is something else we cannot push it back into the reader; in
        //that case we throw, because the prefix was malformed for this consumer.
        int positionBefore = reader.BytesConsumed;
        CborTag tag = reader.ReadTag();
        if(tag == CborTag.SelfDescribe)
        {
            return true;
        }
        throw new FormatException(
            string.Create(CultureInfo.InvariantCulture, $"Expected CBOR Tag 55799 (SelfDescribe) or no tag at position {positionBefore}; got tag {tag.Value}."));
    }
}
