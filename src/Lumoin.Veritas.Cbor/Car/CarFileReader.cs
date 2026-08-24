using System;
using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Cbor.DagCbor;
using CidValue = Lumoin.Veritas.Cid.Cid;
using CidParser = Lumoin.Veritas.Cid.CidParser;
using CidCodec = Lumoin.Veritas.Cid.CidCodec;
using Digest32 = Lumoin.Veritas.Cid.Digest32;

namespace Lumoin.Veritas.Cbor.Car;

/// <summary>
/// Reads CARv1 framing per the IPLD CARv1 specification. CARv1 wraps a
/// content-addressable DAG of DAG-CBOR blocks with a small varint-
/// framed header and per-section length prefixes; this reader walks
/// that framing without materialising block bytes — each section's
/// block is returned as a zero-copy <see cref="ReadOnlyMemory{T}"/>
/// slice of the source.
/// </summary>
/// <remarks>
/// <para>
/// Layout:
/// </para>
/// <code>
/// varint(headerLen) || header (DAG-CBOR { version, roots })
/// varint(sectionLen) || CID || DAG-CBOR-block
/// varint(sectionLen) || CID || DAG-CBOR-block
/// ...
/// </code>
/// <para>
/// where the section varint is an unsigned LEB128 covering the CID +
/// block bytes that follow it. CIDs in sections appear in their raw
/// binary form (no <c>0x00</c> multibase prefix), unlike CIDs encoded
/// inside DAG-CBOR blocks themselves (which use Tag 42 with a
/// 37-byte prefixed payload).
/// </para>
/// <para>
/// The reader is forward-only: <see cref="ReadHeader"/> must be called
/// once before <see cref="TryReadSection"/> is iterated to consume the
/// section stream. It does not cache or index sections; consumers that
/// need random access build their own index over the returned blocks.
/// </para>
/// </remarks>
/// <seealso href="https://ipld.io/specs/transport/car/carv1/"/>
public sealed class CarFileReader
{
    private readonly ReadOnlyMemory<byte> source;
    private int position;
    private bool headerConsumed;

    /// <summary>Initialises a new reader over the given CARv1 byte buffer.</summary>
    /// <param name="source">The CAR file bytes.</param>
    public CarFileReader(ReadOnlyMemory<byte> source)
    {
        this.source = source;
    }

    /// <summary>
    /// Reads the CARv1 header (version + root CID list). Must be called
    /// once before iterating sections via <see cref="TryReadSection"/>.
    /// </summary>
    /// <returns>The parsed header.</returns>
    /// <exception cref="InvalidOperationException">The header has already been consumed.</exception>
    /// <exception cref="FormatException">The header is malformed or exceeds the source length.</exception>
    public CarFileHeader ReadHeader()
    {
        if(headerConsumed)
        {
            throw new InvalidOperationException("CAR header already consumed.");
        }

        ulong headerLen = ReadVarint();
        if(headerLen > int.MaxValue || (int)headerLen > source.Length - position)
        {
            throw new FormatException("CAR header length exceeds source.");
        }

        ReadOnlyMemory<byte> headerBytes = source.Slice(position, (int)headerLen);
        position += (int)headerLen;
        headerConsumed = true;

        DagCborReader reader = new(headerBytes, strict: false);
        int mapCount = reader.ReadStartMap();
        long? version = null;
        List<CidValue> roots = [];
        for(int i = 0; i < mapCount; i++)
        {
            string key = reader.ReadTextString();
            if(key == "version")
            {
                version = reader.ReadInt64();
            }
            else if(key == "roots")
            {
                int rootCount = reader.ReadStartArray();
                for(int r = 0; r < rootCount; r++)
                {
                    roots.Add(reader.ReadCid());
                }
                reader.ReadEndArray();
            }
            else
            {
                //Skip unknown keys conservatively by walking the value.
                SkipValue(reader);
            }
        }
        reader.ReadEndMap();

        return new CarFileHeader(version ?? 1L, roots);
    }

    /// <summary>
    /// Attempts to read the next CAR section: a CID identifying the
    /// content followed by a zero-copy <see cref="ReadOnlyMemory{T}"/>
    /// slice over the DAG-CBOR block bytes. Returns <c>false</c> at
    /// end-of-stream.
    /// </summary>
    /// <param name="cid">The CID of the block; the multihash carries the SHA-256 digest of <paramref name="blockBytes"/>.</param>
    /// <param name="blockBytes">A zero-copy view of the block content; valid as long as the source buffer is.</param>
    /// <returns><c>true</c> when a section was read; <c>false</c> at end-of-stream.</returns>
    /// <exception cref="InvalidOperationException"><see cref="ReadHeader"/> has not been called.</exception>
    /// <exception cref="FormatException">A section length exceeds the source.</exception>
    public bool TryReadSection(out CidValue cid, out ReadOnlyMemory<byte> blockBytes)
    {
        cid = default!;
        if(!TryReadSection(out ReadOnlyMemory<byte> cidBytes, out blockBytes))
        {
            return false;
        }

        //Real AT Protocol CIDs use sha2-256 with 32-byte digests; the canonical 36-byte form matches our CidParser
        //contract. CIDs of an unexpected width are passed through as a null Cid; consumers that care about strict
        //validation can inspect blockBytes / the multihash externally, or use the raw-slice overload below.
        ReadOnlySpan<byte> cidSpan = cidBytes.Span;
        if(cidSpan.Length == 36)
        {
            cid = CidParser.Parse(cidSpan);
        }

        return true;
    }

    /// <summary>
    /// Attempts to read the next CAR section, yielding the block's CID as its <see cref="CidCodec"/> and 32-byte
    /// <see cref="Digest32"/> without materialising a <see cref="CidValue"/> — the zero-allocation counterpart for
    /// hot paths (a firehose CAR walk) that only need the digest to compare or index by. This parses the digest
    /// internally so the consumer needs no CID-binary decoding of its own. <paramref name="codec"/> and
    /// <paramref name="digest"/> are <see langword="default"/> when the section's CID is not a canonical 36-byte
    /// binary CID. Returns <c>false</c> at end-of-stream.
    /// </summary>
    /// <param name="codec">The block CID's codec, or <see langword="default"/> for a non-canonical CID.</param>
    /// <param name="digest">The block CID's 32-byte digest, or <see langword="default"/> for a non-canonical CID.</param>
    /// <param name="blockBytes">A zero-copy view of the block content; valid as long as the source buffer is.</param>
    /// <returns><c>true</c> when a section was read; <c>false</c> at end-of-stream.</returns>
    /// <exception cref="InvalidOperationException"><see cref="ReadHeader"/> has not been called.</exception>
    /// <exception cref="FormatException">A section length exceeds the source.</exception>
    public bool TryReadSection(out CidCodec codec, out Digest32 digest, out ReadOnlyMemory<byte> blockBytes)
    {
        codec = default;
        digest = default;
        if(!TryReadSection(out ReadOnlyMemory<byte> cidBytes, out blockBytes))
        {
            return false;
        }

        //A non-canonical / unexpected-width CID leaves codec+digest at default (TryParseDigest returns false).
        _ = CidParser.TryParseDigest(cidBytes.Span, out codec, out digest);

        return true;
    }

    /// <summary>
    /// Attempts to read the next CAR section, surfacing the section's CID as a raw zero-copy byte slice rather
    /// than a materialised CID, alongside the block. This is the allocation-free section walk: a consumer that
    /// only needs the CID's digest — keying blocks by digest, or skipping MST nodes it does not want — passes
    /// <paramref name="cidBytes"/> to <c>CidParser.TryParseDigest</c> and never allocates a CID per section, the
    /// per-section cost that dominates a firehose frame. Returns <c>false</c> at end-of-stream.
    /// </summary>
    /// <param name="cidBytes">A zero-copy view of the section's raw binary CID (no <c>0x00</c> multibase prefix); valid as long as the source buffer is.</param>
    /// <param name="blockBytes">A zero-copy view of the block content; valid as long as the source buffer is.</param>
    /// <returns><c>true</c> when a section was read; <c>false</c> at end-of-stream.</returns>
    /// <exception cref="InvalidOperationException"><see cref="ReadHeader"/> has not been called.</exception>
    /// <exception cref="FormatException">A section length exceeds the source.</exception>
    public bool TryReadSection(out ReadOnlyMemory<byte> cidBytes, out ReadOnlyMemory<byte> blockBytes)
    {
        cidBytes = default;
        blockBytes = default;
        if(!headerConsumed)
        {
            throw new InvalidOperationException("Call ReadHeader before TryReadSection.");
        }

        if(position >= source.Length)
        {
            return false;
        }

        ulong sectionLen = ReadVarint();
        if(sectionLen > int.MaxValue || (int)sectionLen > source.Length - position)
        {
            throw new FormatException("CAR section length exceeds source.");
        }

        int sectionStart = position;
        int sectionEnd = position + (int)sectionLen;

        //Each section starts with a CID in raw binary form (no 0x00 multibase prefix): the CID version (varint,
        //expected 1), the codec (varint), the multihash code (varint), the digest length (varint), then the
        //digest bytes. The slice over those bytes is the raw CID, returned as-is so a consumer comparing only
        //the digest never materialises a Cid.
        ReadOnlyMemory<byte> sectionMem = source.Slice(sectionStart, (int)sectionLen);
        ReadOnlySpan<byte> sectionSpan = sectionMem.Span;
        int cursor = 0;
        cursor += ReadVarintInto(sectionSpan[cursor..], out _);   //CID version
        cursor += ReadVarintInto(sectionSpan[cursor..], out _);   //codec
        cursor += ReadVarintInto(sectionSpan[cursor..], out _);   //multihash code
        cursor += ReadVarintInto(sectionSpan[cursor..], out ulong digestLen);
        cursor += (int)digestLen;

        cidBytes = sectionMem[..cursor];
        blockBytes = sectionMem.Slice(cursor, (int)sectionLen - cursor);
        position = sectionEnd;

        return true;
    }

    private ulong ReadVarint()
    {
        ulong value = 0;
        int shift = 0;
        while(position < source.Length)
        {
            byte b = source.Span[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if((b & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
            if(shift > 63)
            {
                throw new FormatException("CAR varint overflows 64 bits.");
            }
        }

        throw new FormatException("CAR varint truncated.");
    }

    private static int ReadVarintInto(ReadOnlySpan<byte> bytes, out ulong value)
    {
        value = 0;
        int shift = 0;
        for(int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            value |= (ulong)(b & 0x7F) << shift;
            if((b & 0x80) == 0)
            {
                return i + 1;
            }

            shift += 7;
            if(shift > 63)
            {
                throw new FormatException("CAR varint overflows 64 bits.");
            }
        }

        throw new FormatException("CAR varint truncated.");
    }

    private static void SkipValue(DagCborReader reader)
    {
        CborReaderState state = reader.PeekState();
        switch(state)
        {
            case CborReaderState.UnsignedInteger:
            case CborReaderState.NegativeInteger:
            {
                _ = reader.ReadInt64();
                break;
            }
            case CborReaderState.TextString:
            {
                _ = reader.ReadTextString();
                break;
            }
            case CborReaderState.ByteString:
            {
                _ = reader.ReadByteStringSpan();
                break;
            }
            case CborReaderState.Boolean:
            {
                _ = reader.ReadBoolean();
                break;
            }
            case CborReaderState.Null:
            {
                reader.ReadNull();
                break;
            }
            case CborReaderState.DoublePrecisionFloat:
            case CborReaderState.SinglePrecisionFloat:
            case CborReaderState.HalfPrecisionFloat:
            {
                _ = reader.ReadDouble();
                break;
            }
            case CborReaderState.StartArray:
            {
                int count = reader.ReadStartArray();
                for(int i = 0; i < count; i++)
                {
                    SkipValue(reader);
                }
                reader.ReadEndArray();
                break;
            }
            case CborReaderState.StartMap:
            {
                int count = reader.ReadStartMap();
                for(int i = 0; i < count; i++)
                {
                    _ = reader.ReadTextString();
                    SkipValue(reader);
                }
                reader.ReadEndMap();
                break;
            }
            case CborReaderState.Tag:
            {
                _ = reader.ReadCid();
                break;
            }
            default:
            {
                throw new FormatException(
                    string.Create(CultureInfo.InvariantCulture, $"CAR header skipper encountered unhandled state {state}."));
            }
        }
    }
}
