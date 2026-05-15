using System;
using System.Globalization;
using System.Numerics;

namespace Lumoin.Veritas.Cbor.Converters;

/// <summary>
/// Converts a negative <see cref="BigInteger"/> to and from a CBOR Tag 3
/// data item (RFC 8949 §3.4.3). Per the spec the wire byte string carries
/// the value of <c>-1 - n</c>, big-endian: a non-negative magnitude that
/// the reader negates and offsets to recover <c>n</c>.
/// </summary>
public sealed class NegativeBigIntegerCborConverter: CborConverter<BigInteger>
{
    /// <inheritdoc/>
    public override void Write(CborWriter writer, BigInteger value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if(value.Sign >= 0)
        {
            throw new ArgumentException("CBOR Tag 3 (NegativeBigInteger) requires a negative value.", nameof(value));
        }
        BigInteger encoded = -BigInteger.One - value;
        byte[] bytes = encoded.ToByteArray(isUnsigned: true, isBigEndian: true);
        writer.WriteTag(CborTag.NegativeBigInteger);
        writer.WriteByteString(bytes);
    }

    /// <inheritdoc/>
    public override BigInteger Read(CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        CborTag tag = reader.ReadTag();
        if(tag != CborTag.NegativeBigInteger)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected CBOR Tag 3 (NegativeBigInteger); got tag {tag.Value}."));
        }
        ReadOnlySpan<byte> bytes = reader.ReadByteStringSpan();
        BigInteger encoded = new(bytes, isUnsigned: true, isBigEndian: true);
        return -BigInteger.One - encoded;
    }
}
