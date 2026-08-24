using System;
using System.Buffers;
using System.Globalization;
using CidValue = Lumoin.Veritas.Cid.Cid;

namespace Lumoin.Veritas.Cbor.Drisl;

/// <summary>
/// Converts a <see cref="CidValue"/> to and from a CBOR Tag 42 data item
/// per the DASL CID specification. The wire content is a byte string
/// carrying the historical <c>0x00</c> multibase prefix followed by the
/// CID's 36-byte canonical binary form, for a total of 37 content bytes.
/// </summary>
public sealed class CidCborConverter: CborConverter<CidValue>
{
    private const byte MultibasePrefix = 0x00;
    private const int ContentLength = 37;

    /// <inheritdoc/>
    public override void Write(CborWriter writer, CidValue value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        byte[] cidBytes = Lumoin.Veritas.Cid.CidFormatter.ToBytes(value);
        byte[] content = new byte[ContentLength];
        content[0] = MultibasePrefix;
        cidBytes.CopyTo(content, 1);

        writer.WriteTag(CborTag.Cid);
        writer.WriteByteString(content);
    }

    /// <inheritdoc/>
    public override CidValue Read(CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        CborTag tag = reader.ReadTag();
        if(tag != CborTag.Cid)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected CBOR Tag 42 (CID); got tag {tag.Value}."));
        }

        //Use pool-rented memory rather than fresh-allocating per call:
        //CIDs occur many times per CAR file and the 37-byte buffer comes
        //from a small-buckets pool, so per-call overhead is a rent/return
        //pair rather than a managed-heap allocation. ReadByteStringPooled
        //also handles multi-segment input transparently (the reader copies
        //into the rented slab when the byte string straddles a sequence
        //segment boundary). The reader wraps the rent so Memory.Length is
        //exactly the byte-string length, not the pool bucket size.
        using IMemoryOwner<byte> owner = reader.ReadByteStringPooled();
        ReadOnlySpan<byte> content = owner.Memory.Span;
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

        return Lumoin.Veritas.Cid.CidParser.Parse(content[1..]);
    }
}
