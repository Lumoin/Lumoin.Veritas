using System;
using System.Globalization;

namespace Lumoin.Veritas.Cbor.Converters;

/// <summary>
/// Converts a byte sequence to and from a CBOR Tag 34 data item
/// (RFC 8949 §3.4.5.2): a tagged text string carrying base64-encoded
/// data per RFC 4648 §4 (with padding).
/// </summary>
public sealed class Base64CborConverter: CborConverter<byte[]>
{
    /// <inheritdoc/>
    public override void Write(CborWriter writer, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteTag(CborTag.Base64);
        writer.WriteTextString(Convert.ToBase64String(value));
    }

    /// <inheritdoc/>
    public override byte[] Read(CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        CborTag tag = reader.ReadTag();
        if(tag != CborTag.Base64)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected CBOR Tag 34 (Base64); got tag {tag.Value}."));
        }
        string text = reader.ReadTextString();
        return Convert.FromBase64String(text);
    }
}
