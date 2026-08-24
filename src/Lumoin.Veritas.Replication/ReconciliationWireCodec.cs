using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The primitive read and write helpers every reconciliation channel framing shares: big-endian scalars,
/// length-prefixed byte fields, the truncation-refusing exact reads, and the hostile-count guard. The channel
/// framings own their header layouts and the envelope framing owns the envelope legs; this codec is the one
/// byte-level vocabulary underneath both, so the two channels cannot drift on a primitive's encoding.
/// </summary>
internal static class ReconciliationWireCodec
{
    /// <summary>Writes a single byte.</summary>
    /// <param name="output">The channel buffer to write into.</param>
    /// <param name="value">The byte.</param>
    public static void WriteByte(IBufferWriter<byte> output, byte value)
    {
        Span<byte> span = output.GetSpan(1);
        span[0] = value;
        output.Advance(1);
    }

    /// <summary>Writes a four-byte big-endian integer.</summary>
    /// <param name="output">The channel buffer to write into.</param>
    /// <param name="value">The value.</param>
    public static void WriteInt(IBufferWriter<byte> output, int value)
    {
        Span<byte> span = output.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        output.Advance(sizeof(int));
    }

    /// <summary>Writes an eight-byte big-endian unsigned integer.</summary>
    /// <param name="output">The channel buffer to write into.</param>
    /// <param name="value">The value.</param>
    public static void WriteUlong(IBufferWriter<byte> output, ulong value)
    {
        Span<byte> span = output.GetSpan(sizeof(ulong));
        BinaryPrimitives.WriteUInt64BigEndian(span, value);
        output.Advance(sizeof(ulong));
    }

    /// <summary>Writes a length-prefixed byte field.</summary>
    /// <param name="output">The channel buffer to write into.</param>
    /// <param name="bytes">The field content.</param>
    public static void WritePrefixedBytes(IBufferWriter<byte> output, ImmutableArray<byte> bytes)
    {
        WriteInt(output, bytes.Length);
        output.Write(bytes.AsSpan());
    }

    /// <summary>Reads a four-byte big-endian integer, refusing a truncated frame.</summary>
    /// <param name="reader">The frame cursor, advanced past the value.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated.</exception>
    public static int ReadInt(ref SequenceReader<byte> reader)
    {
        if(!reader.TryReadBigEndian(out int value))
        {
            throw new InvalidDataException("A reconciliation channel frame is truncated.");
        }

        return value;
    }

    /// <summary>Reads an eight-byte big-endian unsigned integer, refusing a truncated frame.</summary>
    /// <param name="reader">The frame cursor, advanced past the value.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated.</exception>
    public static ulong ReadUlong(ref SequenceReader<byte> reader)
    {
        if(!reader.TryReadBigEndian(out long value))
        {
            throw new InvalidDataException("A reconciliation channel frame is truncated.");
        }

        return unchecked((ulong)value);
    }

    /// <summary>Reads a single byte, refusing a truncated frame.</summary>
    /// <param name="reader">The frame cursor, advanced past the byte.</param>
    /// <returns>The byte.</returns>
    /// <exception cref="InvalidDataException">The frame holds no more bytes.</exception>
    public static byte ReadByteOrThrow(ref SequenceReader<byte> reader)
    {
        if(!reader.TryRead(out byte value))
        {
            throw new InvalidDataException("A reconciliation channel frame is truncated.");
        }

        return value;
    }

    /// <summary>Fills the destination exactly from the cursor, refusing a truncated frame.</summary>
    /// <param name="reader">The frame cursor, advanced past the copied bytes.</param>
    /// <param name="destination">The buffer to fill whole.</param>
    /// <exception cref="InvalidDataException">The frame holds fewer bytes than the destination.</exception>
    public static void ReadExactly(ref SequenceReader<byte> reader, scoped Span<byte> destination)
    {
        if(!reader.TryCopyTo(destination))
        {
            throw new InvalidDataException("A reconciliation channel frame is truncated.");
        }

        reader.Advance(destination.Length);
    }

    /// <summary>Reads a length-prefixed byte field into an owned immutable array.</summary>
    /// <param name="reader">The frame cursor, advanced past the field.</param>
    /// <returns>The field content.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated or the declared length is negative or exceeds the remaining bytes.</exception>
    public static ImmutableArray<byte> ReadPrefixedBytes(ref SequenceReader<byte> reader)
    {
        int length = ReadInt(ref reader);
        if(length < 0 || length > reader.Remaining)
        {
            throw new InvalidDataException("A reconciliation channel frame declared a byte field longer than the frame.");
        }

        byte[] bytes = new byte[length];
        ReadExactly(ref reader, bytes);

        return ImmutableCollectionsMarshal.AsImmutableArray(bytes);
    }

    /// <summary>Refuses a declared repetition count that is negative or whose minimum wire size exceeds the remaining bytes — the hostile-count guard every repeated leg runs before allocating.</summary>
    /// <param name="count">The declared count.</param>
    /// <param name="minimumItemBytes">The fewest bytes one repetition can occupy.</param>
    /// <param name="remaining">The frame bytes remaining.</param>
    /// <exception cref="InvalidDataException">The count is negative or cannot fit the frame.</exception>
    public static void EnsureCountFits(int count, int minimumItemBytes, long remaining)
    {
        if(count < 0 || (long)count * minimumItemBytes > remaining)
        {
            throw new InvalidDataException("A reconciliation channel frame declared a count its bytes cannot hold.");
        }
    }
}
