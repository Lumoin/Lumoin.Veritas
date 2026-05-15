using System;
using System.Globalization;
using System.Numerics;

namespace Lumoin.Veritas.Cbor.Converters;

/// <summary>
/// Converts a non-negative <see cref="BigInteger"/> to and from a CBOR
/// Tag 2 data item (RFC 8949 §3.4.3): the absolute value as a big-endian
/// byte string.
/// </summary>
public sealed class UnsignedBigIntegerCborConverter: CborConverter<BigInteger>
{
    /// <inheritdoc/>
    public override void Write(CborWriter writer, BigInteger value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if(value.Sign < 0)
        {
            throw new ArgumentException("CBOR Tag 2 (UnsignedBigInteger) requires a non-negative value.", nameof(value));
        }
        byte[] bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        writer.WriteTag(CborTag.UnsignedBigInteger);
        writer.WriteByteString(bytes);
    }

    /// <inheritdoc/>
    public override BigInteger Read(CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        CborTag tag = reader.ReadTag();
        if(tag != CborTag.UnsignedBigInteger)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected CBOR Tag 2 (UnsignedBigInteger); got tag {tag.Value}."));
        }
        ReadOnlySpan<byte> bytes = reader.ReadByteStringSpan();
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
    }
}
