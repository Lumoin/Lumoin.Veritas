using System;
using System.Buffers;

namespace Lumoin.Veritas.Cbor.Dcbor;

/// <summary>
/// Reads CBOR data items under the dCBOR profile
/// (<see href="https://datatracker.ietf.org/doc/draft-mcnally-deterministic-cbor/"/>).
/// Rejects indefinite-length items and the <c>undefined</c> simple value.
/// Accepts canonical NaN and infinity in their half-precision forms — per
/// draft-mcnally-deterministic-cbor §2.5 these are the only permitted
/// non-finite encodings; single-precision NaN / infinity is non-canonical
/// and remains permitted by the underlying reader so consumers can
/// inspect non-conforming input. Unlike DRISL, dCBOR allows integer-keyed
/// maps and arbitrary tag identifiers.
/// </summary>
public sealed class DcborReader
{
    private readonly CborReader inner;

    /// <summary>Initialises a new <see cref="DcborReader"/> reading from <paramref name="source"/>.</summary>
    /// <param name="source">The contiguous bytes to read.</param>
    public DcborReader(ReadOnlyMemory<byte> source)
    {
        inner = new CborReader(source, DcborDefaults.CreateOptions());
    }

    /// <summary>Initialises a new <see cref="DcborReader"/> reading from <paramref name="source"/>.</summary>
    /// <param name="source">The byte sequence to read.</param>
    public DcborReader(ReadOnlySequence<byte> source)
    {
        inner = new CborReader(source, DcborDefaults.CreateOptions());
    }

    /// <summary>Gets the total number of bytes consumed so far.</summary>
    public int BytesConsumed => inner.BytesConsumed;

    /// <summary>Gets the number of currently-open containers.</summary>
    public int CurrentDepth => inner.CurrentDepth;

    /// <summary>
    /// Returns the categorical state of the next data item, validating
    /// that the state is permitted under dCBOR.
    /// </summary>
    public CborReaderState PeekState()
    {
        CborReaderState state = inner.PeekState();
        return state switch
        {
            CborReaderState.Undefined => throw new FormatException("dCBOR forbids the undefined simple value."),
            _ => state
        };
    }

    /// <summary>
    /// Reads a half-precision (binary16) floating-point value. Accepted
    /// under dCBOR only for the canonical NaN (<c>0xF9 7E 00</c>) and the
    /// canonical positive and negative infinities (<c>0xF9 7C 00</c> /
    /// <c>0xF9 FC 00</c>); other half-precision encodings can be
    /// consumed but should be considered non-canonical and inspected by
    /// the caller as needed.
    /// </summary>
    public Half ReadHalf() => inner.ReadHalf();

    /// <summary>Reads a single-precision floating-point value.</summary>
    public float ReadSingle() => inner.ReadSingle();

    /// <summary>Reads an unsigned 64-bit integer.</summary>
    public ulong ReadUInt64() => inner.ReadUInt64();

    /// <summary>Reads a signed 64-bit integer.</summary>
    public long ReadInt64() => inner.ReadInt64();

    /// <summary>Reads a byte string.</summary>
    public byte[] ReadByteString() => inner.ReadByteString();

    /// <summary>Reads a UTF-8 text string.</summary>
    public string ReadTextString() => inner.ReadTextString();

    /// <summary>Reads a definite-length array introducer.</summary>
    /// <returns>The array's item count.</returns>
    public int ReadStartArray()
    {
        int? count = inner.ReadStartArray();
        if(count is null)
        {
            throw new FormatException("dCBOR forbids indefinite-length arrays.");
        }
        return count.Value;
    }

    /// <summary>Closes the topmost array.</summary>
    public void ReadEndArray() => inner.ReadEndArray();

    /// <summary>Reads a definite-length map introducer.</summary>
    /// <returns>The map's key/value pair count.</returns>
    public int ReadStartMap()
    {
        int? count = inner.ReadStartMap();
        if(count is null)
        {
            throw new FormatException("dCBOR forbids indefinite-length maps.");
        }
        return count.Value;
    }

    /// <summary>Closes the topmost map.</summary>
    public void ReadEndMap() => inner.ReadEndMap();

    /// <summary>Reads a Boolean.</summary>
    public bool ReadBoolean() => inner.ReadBoolean();

    /// <summary>Reads the CBOR null value.</summary>
    public void ReadNull() => inner.ReadNull();

    /// <summary>Reads a CBOR tag.</summary>
    public CborTag ReadTag() => inner.ReadTag();

    /// <summary>
    /// Reads a double-precision (binary64) floating-point value. Accepted
    /// for any finite value the producer chose to emit at this width;
    /// non-canonical NaN bit patterns are returned as-is so callers can
    /// detect and reject them if they enforce strict dCBOR conformance.
    /// </summary>
    public double ReadDouble() => inner.ReadDouble();
}
