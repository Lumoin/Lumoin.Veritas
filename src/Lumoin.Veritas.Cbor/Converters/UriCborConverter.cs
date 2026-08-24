using System;
using System.Globalization;

namespace Lumoin.Veritas.Cbor.Converters;

/// <summary>
/// Converts a <see cref="Uri"/> to and from a CBOR Tag 32 data item
/// (RFC 8949 §3.4.5.3): the URI as a text string per RFC 3986.
/// </summary>
public sealed class UriCborConverter: CborConverter<Uri>
{
    /// <inheritdoc/>
    public override void Write(CborWriter writer, Uri value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteTag(CborTag.Uri);
        writer.WriteTextString(value.ToString());
    }

    /// <inheritdoc/>
    public override Uri Read(CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        CborTag tag = reader.ReadTag();
        if(tag != CborTag.Uri)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected CBOR Tag 32 (URI); got tag {tag.Value}."));
        }
        string text = reader.ReadTextString();
        return new Uri(text, UriKind.RelativeOrAbsolute);
    }
}
