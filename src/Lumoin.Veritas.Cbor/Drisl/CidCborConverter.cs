using System;
using System.Globalization;

namespace Lumoin.Veritas.Cbor.Drisl;

/// <summary>
/// Converts a <see cref="Lumoin.Veritas.Cid.Cid"/> to and from a CBOR Tag 42
/// data item per the DASL CID specification. The wire content is a byte
/// string carrying the historical <c>0x00</c> multibase prefix followed by
/// the CID's 36-byte canonical binary form, for a total of 37 content
/// bytes.
/// </summary>
public sealed class CidCborConverter: CborConverter<global::Lumoin.Veritas.Cid.Cid>
{
    private const byte MultibasePrefix = 0x00;
    private const int ContentLength = 37;

    /// <inheritdoc/>
    public override void Write(CborWriter writer, global::Lumoin.Veritas.Cid.Cid value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        byte[] cidBytes = global::Lumoin.Veritas.Cid.CidFormatter.ToBytes(value);
        byte[] content = new byte[ContentLength];
        content[0] = MultibasePrefix;
        cidBytes.CopyTo(content, 1);

        writer.WriteTag(CborTag.Cid);
        writer.WriteByteString(content);
    }

    /// <inheritdoc/>
    public override global::Lumoin.Veritas.Cid.Cid Read(CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        CborTag tag = reader.ReadTag();
        if(tag != CborTag.Cid)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected CBOR Tag 42 (CID); got tag {tag.Value}."));
        }

        byte[] content = reader.ReadByteString();
        if(content.Length != ContentLength)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"CBOR Tag 42 (CID) byte string must be exactly {ContentLength} bytes; got {content.Length}."));
        }
        if(content[0] != MultibasePrefix)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"CBOR Tag 42 (CID) byte string must start with 0x00 multibase prefix; got 0x{content[0]:X2}."));
        }

        return global::Lumoin.Veritas.Cid.CidParser.Parse(content.AsSpan(1));
    }
}
