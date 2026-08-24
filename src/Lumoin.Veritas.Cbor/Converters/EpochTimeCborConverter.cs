using System;
using System.Globalization;

namespace Lumoin.Veritas.Cbor.Converters;

/// <summary>
/// Converts a <see cref="DateTimeOffset"/> to and from a CBOR Tag 1 data
/// item (RFC 8949 §3.4.2): a tagged numeric value representing seconds
/// since the POSIX epoch (1970-01-01T00:00:00Z).
/// </summary>
/// <remarks>
/// The writer always emits a double-precision (binary64) seconds value so
/// fractional seconds round-trip. The reader accepts integer, half-, single-,
/// and double-precision representations to match the spec.
/// </remarks>
public sealed class EpochTimeCborConverter: CborConverter<DateTimeOffset>
{
    /// <inheritdoc/>
    public override void Write(CborWriter writer, DateTimeOffset value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        double seconds = (value - DateTimeOffset.UnixEpoch).TotalSeconds;
        writer.WriteTag(CborTag.EpochTime);
        writer.WriteDouble(seconds);
    }

    /// <inheritdoc/>
    public override DateTimeOffset Read(CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        CborTag tag = reader.ReadTag();
        if(tag != CborTag.EpochTime)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected CBOR Tag 1 (EpochTime); got tag {tag.Value}."));
        }

        double seconds = reader.PeekState() switch
        {
            CborReaderState.UnsignedInteger => reader.ReadUInt64(),
            CborReaderState.NegativeInteger => reader.ReadInt64(),
            CborReaderState.HalfPrecisionFloat => (double)reader.ReadHalf(),
            CborReaderState.SinglePrecisionFloat => reader.ReadSingle(),
            CborReaderState.DoublePrecisionFloat => reader.ReadDouble(),
            CborReaderState s => throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"CBOR Tag 1 content must be an integer or float; got reader state {s}."))
        };

        return DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(seconds);
    }
}
