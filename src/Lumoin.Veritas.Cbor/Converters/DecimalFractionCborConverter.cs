using System;
using System.Globalization;
using System.Numerics;

namespace Lumoin.Veritas.Cbor.Converters;

/// <summary>
/// Converts a <see cref="CborDecimalFraction"/> to and from a CBOR Tag 4
/// data item (RFC 8949 §3.4.4): a two-element array carrying
/// <c>[exponent, mantissa]</c>. The mantissa is encoded as an integer,
/// or as a Tag 2/Tag 3 big integer when its magnitude exceeds the 64-bit
/// integer range.
/// </summary>
public sealed class DecimalFractionCborConverter: CborConverter<CborDecimalFraction>
{
    /// <inheritdoc/>
    public override void Write(CborWriter writer, CborDecimalFraction value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteTag(CborTag.DecimalFraction);
        writer.WriteStartArray(2);
        writer.WriteInt32(value.Exponent);
        WriteMantissa(writer, value.Mantissa);
        writer.WriteEndArray();
    }

    /// <inheritdoc/>
    public override CborDecimalFraction Read(CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        CborTag tag = reader.ReadTag();
        if(tag != CborTag.DecimalFraction)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected CBOR Tag 4 (DecimalFraction); got tag {tag.Value}."));
        }
        int? count = reader.ReadStartArray();
        if(count != 2)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"CBOR Tag 4 (DecimalFraction) requires a 2-element array; got {count?.ToString(CultureInfo.InvariantCulture) ?? "indefinite"}."));
        }
        long exponent = reader.ReadInt64();
        if(exponent is > int.MaxValue or < int.MinValue)
        {
            throw new OverflowException("CBOR Tag 4 (DecimalFraction) exponent does not fit in Int32.");
        }
        BigInteger mantissa = ReadMantissa(reader);
        reader.ReadEndArray();
        return new CborDecimalFraction((int)exponent, mantissa);
    }

    internal static void WriteMantissa(CborWriter writer, BigInteger mantissa)
    {
        if(mantissa >= long.MinValue && mantissa <= long.MaxValue)
        {
            writer.WriteInt64((long)mantissa);
            return;
        }
        if(mantissa.Sign >= 0)
        {
            byte[] bytes = mantissa.ToByteArray(isUnsigned: true, isBigEndian: true);
            writer.WriteTag(CborTag.UnsignedBigInteger);
            writer.WriteByteString(bytes);
        }
        else
        {
            BigInteger encoded = -BigInteger.One - mantissa;
            byte[] bytes = encoded.ToByteArray(isUnsigned: true, isBigEndian: true);
            writer.WriteTag(CborTag.NegativeBigInteger);
            writer.WriteByteString(bytes);
        }
    }

    internal static BigInteger ReadMantissa(CborReader reader)
    {
        return reader.PeekState() switch
        {
            CborReaderState.UnsignedInteger => new BigInteger(reader.ReadUInt64()),
            CborReaderState.NegativeInteger => new BigInteger(reader.ReadInt64()),
            CborReaderState.Tag => ReadTaggedBigMantissa(reader),
            CborReaderState s => throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"CBOR mantissa must be an integer or tagged big integer; got reader state {s}."))
        };
    }

    private static BigInteger ReadTaggedBigMantissa(CborReader reader)
    {
        CborTag tag = reader.ReadTag();
        ReadOnlySpan<byte> bytes = reader.ReadByteStringSpan();
        if(tag == CborTag.UnsignedBigInteger)
        {
            return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        }
        if(tag == CborTag.NegativeBigInteger)
        {
            BigInteger encoded = new(bytes, isUnsigned: true, isBigEndian: true);
            return -BigInteger.One - encoded;
        }
        throw new FormatException(
            string.Create(CultureInfo.InvariantCulture, $"CBOR mantissa tag must be 2 or 3; got {tag.Value}."));
    }
}
