using System;
using System.Globalization;
using System.Numerics;

namespace Lumoin.Veritas.Cbor.Converters;

/// <summary>
/// Converts a <see cref="CborBigfloat"/> to and from a CBOR Tag 5 data
/// item (RFC 8949 §3.4.4): a two-element array carrying <c>[exponent,
/// mantissa]</c>. Identical structurally to Tag 4 but with a base-2
/// exponent.
/// </summary>
public sealed class BigfloatCborConverter: CborConverter<CborBigfloat>
{
    /// <inheritdoc/>
    public override void Write(CborWriter writer, CborBigfloat value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteTag(CborTag.Bigfloat);
        writer.WriteStartArray(2);
        writer.WriteInt32(value.Exponent);
        DecimalFractionCborConverter.WriteMantissa(writer, value.Mantissa);
        writer.WriteEndArray();
    }

    /// <inheritdoc/>
    public override CborBigfloat Read(CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        CborTag tag = reader.ReadTag();
        if(tag != CborTag.Bigfloat)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected CBOR Tag 5 (Bigfloat); got tag {tag.Value}."));
        }
        int? count = reader.ReadStartArray();
        if(count != 2)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"CBOR Tag 5 (Bigfloat) requires a 2-element array; got {count?.ToString(CultureInfo.InvariantCulture) ?? "indefinite"}."));
        }
        long exponent = reader.ReadInt64();
        if(exponent is > int.MaxValue or < int.MinValue)
        {
            throw new OverflowException("CBOR Tag 5 (Bigfloat) exponent does not fit in Int32.");
        }
        BigInteger mantissa = DecimalFractionCborConverter.ReadMantissa(reader);
        reader.ReadEndArray();
        return new CborBigfloat((int)exponent, mantissa);
    }
}
