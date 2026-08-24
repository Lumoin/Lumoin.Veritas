using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Persistence.Journal;

/// <summary>
/// The on-disk framing of one durable journal record: a length-prefixed payload followed by a
/// checksum over the length and payload together, so a torn or corrupt record is detected on replay
/// rather than mis-read. The payload serialises a <see cref="JournalEntry"/> through the shared
/// little-endian primitives. <see cref="TryReadRecord"/> is the replay boundary primitive — it returns
/// <see langword="false"/> for a record that is short or fails its checksum, which is exactly the
/// torn-tail / corruption boundary the journal recovers to.
/// </summary>
/// <remarks>
/// <para>
/// Record layout: <c>[u32 payloadLength][payload][checksum(algorithm width)]</c>, all little-endian.
/// The checksum covers the length prefix and the payload, so a corrupted length field is caught too.
/// The payload begins with a one-byte format version so a future layout change is distinguishable and
/// an incompatible record is refused rather than silently mis-decoded.
/// </para>
/// </remarks>
internal static class JournalRecordFormat
{
    /// <summary>The size of the record's leading little-endian payload-length prefix.</summary>
    private const int LengthPrefixSize = sizeof(uint);

    /// <summary>The payload layout version written as the payload's first byte; bumped when the payload byte layout changes.</summary>
    private const byte PayloadFormatVersion = 1;

    /// <summary>The number of bytes <see cref="WriteRecord"/> writes for an entry under the given checksum.</summary>
    /// <param name="entry">The entry to size.</param>
    /// <param name="checksum">The record checksum algorithm.</param>
    /// <returns>The record byte size.</returns>
    internal static int ComputeRecordSize(in JournalEntry entry, ChecksumAlgorithm checksum)
    {
        return LengthPrefixSize + ComputePayloadSize(entry) + checksum.ByteWidth;
    }

    /// <summary>Writes one framed record (length, payload, checksum) into <paramref name="destination"/>.</summary>
    /// <param name="destination">The buffer to write into; at least <see cref="ComputeRecordSize"/> bytes long.</param>
    /// <param name="entry">The entry to write.</param>
    /// <param name="checksum">The record checksum algorithm.</param>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="ArgumentException">The entry carries a kind the record format does not accept; rejecting it at the write boundary keeps the durable log readable, since <see cref="ReadPayload"/> refuses the same kinds.</exception>
    /// <exception cref="NotSupportedException">The host is big-endian.</exception>
    internal static int WriteRecord(Span<byte> destination, in JournalEntry entry, ChecksumAlgorithm checksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();

        if(entry.EntryKind > EditSessionEntryKind.Abandoned)
        {
            throw new ArgumentException($"The per-store journal record format does not accept the entry kind '{entry.EntryKind}'.", nameof(entry));
        }

        int payloadSize = ComputePayloadSize(entry);
        BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)payloadSize);
        WritePayload(destination.Slice(LengthPrefixSize, payloadSize), entry);

        int checksummedLength = LengthPrefixSize + payloadSize;
        checksum.Compute(destination[..checksummedLength], destination.Slice(checksummedLength, checksum.ByteWidth));

        return checksummedLength + checksum.ByteWidth;
    }

    /// <summary>Attempts to read one record at the start of <paramref name="source"/>, verifying its checksum.</summary>
    /// <param name="source">The byte image positioned at a record.</param>
    /// <param name="checksum">The record checksum algorithm.</param>
    /// <param name="entry">The decoded entry when the record is complete and verifies.</param>
    /// <param name="recordLength">The total bytes the record occupies when it verifies.</param>
    /// <returns><see langword="true"/> when a complete, checksum-valid record was read; <see langword="false"/> when the record is short or fails its checksum (the recovery boundary).</returns>
    /// <exception cref="InvalidDataException">A checksum-valid record carries a malformed payload (a codec error rather than at-rest corruption).</exception>
    /// <exception cref="NotSupportedException">A checksum-valid record carries an unsupported payload format version, or the host is big-endian.</exception>
    internal static bool TryReadRecord(ReadOnlySpan<byte> source, ChecksumAlgorithm checksum, out JournalEntry entry, out int recordLength)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        entry = default;
        recordLength = 0;

        if(source.Length < LengthPrefixSize)
        {
            return false;
        }

        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(source);
        int width = checksum.ByteWidth;
        long total = (long)LengthPrefixSize + payloadLength + width;
        if(total > source.Length)
        {
            return false;
        }

        int checksummedLength = LengthPrefixSize + (int)payloadLength;
        Span<byte> computed = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
        checksum.Compute(source[..checksummedLength], computed[..width]);
        if(!computed[..width].SequenceEqual(source.Slice(checksummedLength, width)))
        {
            return false;
        }

        entry = ReadPayload(source.Slice(LengthPrefixSize, (int)payloadLength));
        recordLength = (int)total;

        return true;
    }

    /// <summary>The serialized payload size for an entry.</summary>
    /// <param name="entry">The entry to size.</param>
    /// <returns>The payload byte size.</returns>
    private static int ComputePayloadSize(in JournalEntry entry)
    {
        int commitmentBytes = entry.EditCommitment.HasValue ? sizeof(ulong) : 0;
        int sessionBytes = entry.SessionId.HasValue ? GuidByteCount : 0;

        return FixedPayloadFieldsSize
            + commitmentBytes
            + sessionBytes
            + LittleEndianBuffer.ArrayBytes<EncodedTriple>(entry.Additions.AsSpan().Length)
            + LittleEndianBuffer.ArrayBytes<EncodedTriple>(entry.Removals.AsSpan().Length);
    }

    /// <summary>Writes the entry payload into <paramref name="destination"/> (exactly <see cref="ComputePayloadSize"/> bytes).</summary>
    /// <param name="destination">The payload-sized destination.</param>
    /// <param name="entry">The entry to write.</param>
    private static void WritePayload(Span<byte> destination, in JournalEntry entry)
    {
        int p = 0;
        destination[p++] = PayloadFormatVersion;
        BinaryPrimitives.WriteInt64LittleEndian(destination[p..], entry.SequenceNumber);
        p += sizeof(long);
        destination[p++] = (byte)entry.EntryKind;
        BinaryPrimitives.WriteUInt64LittleEndian(destination[p..], entry.ParentId.Value);
        p += sizeof(ulong);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[p..], entry.ChildId.Value);
        p += sizeof(ulong);

        if(entry.EditCommitment.HasValue)
        {
            destination[p++] = 1;
            BinaryPrimitives.WriteUInt64LittleEndian(destination[p..], entry.EditCommitment.Value.Value);
            p += sizeof(ulong);
        }
        else
        {
            destination[p++] = 0;
        }

        if(entry.SessionId.HasValue)
        {
            destination[p++] = 1;
            _ = entry.SessionId.Value.Value.TryWriteBytes(destination.Slice(p, GuidByteCount), bigEndian: false, out _);
            p += GuidByteCount;
        }
        else
        {
            destination[p++] = 0;
        }

        BinaryPrimitives.WriteInt64LittleEndian(destination[p..], entry.Timestamp.UtcTicks);
        p += sizeof(long);

        p += LittleEndianBuffer.WriteArray(destination[p..], entry.Additions.AsSpan());
        _ = LittleEndianBuffer.WriteArray(destination[p..], entry.Removals.AsSpan());
    }

    /// <summary>Reads an entry payload written by <see cref="WritePayload"/>.</summary>
    /// <param name="source">The payload bytes (exactly one payload).</param>
    /// <returns>The decoded entry.</returns>
    /// <exception cref="InvalidDataException">The payload is truncated, has a bad presence flag or kind, an out-of-range timestamp, or unexpected trailing bytes.</exception>
    /// <exception cref="NotSupportedException">The payload format version is unsupported.</exception>
    private static JournalEntry ReadPayload(ReadOnlySpan<byte> source)
    {
        int p = 0;
        EnsureRemaining(source, p, sizeof(byte));
        byte version = source[p++];
        if(version != PayloadFormatVersion)
        {
            throw new NotSupportedException($"Journal record payload format version {version} is not supported; this build reads version {PayloadFormatVersion}.");
        }

        EnsureRemaining(source, p, sizeof(long));
        long sequence = BinaryPrimitives.ReadInt64LittleEndian(source[p..]);
        p += sizeof(long);

        EnsureRemaining(source, p, sizeof(byte));
        byte kindByte = source[p++];
        if(kindByte > (byte)EditSessionEntryKind.Abandoned)
        {
            throw new InvalidDataException("A journal record names an unknown entry kind.");
        }

        EditSessionEntryKind kind = (EditSessionEntryKind)kindByte;

        EnsureRemaining(source, p, sizeof(ulong) + sizeof(ulong));
        ulong parent = BinaryPrimitives.ReadUInt64LittleEndian(source[p..]);
        p += sizeof(ulong);
        ulong child = BinaryPrimitives.ReadUInt64LittleEndian(source[p..]);
        p += sizeof(ulong);

        EnsureRemaining(source, p, sizeof(byte));
        byte commitmentPresence = source[p++];
        NodeIdentifier? commitment = null;
        if(commitmentPresence == 1)
        {
            EnsureRemaining(source, p, sizeof(ulong));
            commitment = new NodeIdentifier(BinaryPrimitives.ReadUInt64LittleEndian(source[p..]));
            p += sizeof(ulong);
        }
        else if(commitmentPresence != 0)
        {
            throw new InvalidDataException("A journal record has an invalid edit-commitment presence flag.");
        }

        EnsureRemaining(source, p, sizeof(byte));
        byte sessionPresence = source[p++];
        SessionId? session = null;
        if(sessionPresence == 1)
        {
            EnsureRemaining(source, p, GuidByteCount);
            session = new SessionId(new Guid(source.Slice(p, GuidByteCount), bigEndian: false));
            p += GuidByteCount;
        }
        else if(sessionPresence != 0)
        {
            throw new InvalidDataException("A journal record has an invalid session-id presence flag.");
        }

        EnsureRemaining(source, p, sizeof(long));
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(source[p..]);
        p += sizeof(long);
        DateTimeOffset timestamp = ReadTimestamp(ticks);

        EncodedTriple[] additions = LittleEndianBuffer.ReadArray<EncodedTriple>(source[p..], out int additionsConsumed);
        p += additionsConsumed;
        EncodedTriple[] removals = LittleEndianBuffer.ReadArray<EncodedTriple>(source[p..], out int removalsConsumed);
        p += removalsConsumed;

        if(p != source.Length)
        {
            throw new InvalidDataException("A journal record payload has unexpected trailing bytes.");
        }

        ImmutableArray<EncodedTriple> additionsImmutable = [.. additions];
        ImmutableArray<EncodedTriple> removalsImmutable = [.. removals];

        return new JournalEntry(new NodeIdentifier(parent), new NodeIdentifier(child), kind, session, commitment, additionsImmutable, removalsImmutable, timestamp, sequence);
    }

    /// <summary>Reconstructs the entry timestamp from its stored UTC ticks, refusing an out-of-range value.</summary>
    /// <param name="ticks">The stored UTC ticks.</param>
    /// <returns>The timestamp at zero offset.</returns>
    /// <exception cref="InvalidDataException">The ticks are outside the representable range.</exception>
    private static DateTimeOffset ReadTimestamp(long ticks)
    {
        try
        {
            return new DateTimeOffset(ticks, TimeSpan.Zero);
        }
        catch(ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("A journal record carries an out-of-range timestamp.");
        }
    }

    /// <summary>Throws when fewer than <paramref name="needed"/> bytes remain in <paramref name="source"/> from <paramref name="position"/>.</summary>
    /// <param name="source">The payload span.</param>
    /// <param name="position">The current read position.</param>
    /// <param name="needed">The bytes the next read needs.</param>
    /// <exception cref="InvalidDataException">The payload is truncated.</exception>
    private static void EnsureRemaining(ReadOnlySpan<byte> source, int position, int needed)
    {
        if(source.Length - position < needed)
        {
            throw new InvalidDataException("A journal record payload is truncated.");
        }
    }

    /// <summary>The number of bytes a <see cref="Guid"/> serialises to.</summary>
    private const int GuidByteCount = 16;

    /// <summary>The fixed-field payload size before the optional commitment and session and the two triple arrays: version (1) + sequence (8) + kind (1) + parent (8) + child (8) + commitment presence (1) + session presence (1) + timestamp (8).</summary>
    private const int FixedPayloadFieldsSize = sizeof(byte) + sizeof(long) + sizeof(byte) + sizeof(ulong) + sizeof(ulong) + sizeof(byte) + sizeof(byte) + sizeof(long);
}
