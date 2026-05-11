using System;
using System.Globalization;

namespace Lumoin.Veritas.Cbor.Converters;

/// <summary>
/// Converts a <see cref="DateTimeOffset"/> to and from a CBOR Tag 0 data
/// item (RFC 8949 §3.4.1): a tagged text string in standard RFC 3339 date/
/// time format.
/// </summary>
public sealed class DateTimeStringCborConverter: CborConverter<DateTimeOffset>
{
    //ISO 8601 / RFC 3339 round-trip format; "K" yields "Z" for UTC and "+HH:mm" otherwise.
    private const string Format = "yyyy-MM-ddTHH:mm:ss.FFFFFFFK";

    /// <inheritdoc/>
    public override void Write(CborWriter writer, DateTimeOffset value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteTag(CborTag.DateTimeString);
        writer.WriteTextString(value.ToString(Format, CultureInfo.InvariantCulture));
    }

    /// <inheritdoc/>
    public override DateTimeOffset Read(CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        CborTag tag = reader.ReadTag();
        if(tag != CborTag.DateTimeString)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected CBOR Tag 0 (DateTimeString); got tag {tag.Value}."));
        }
        string text = reader.ReadTextString();
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
