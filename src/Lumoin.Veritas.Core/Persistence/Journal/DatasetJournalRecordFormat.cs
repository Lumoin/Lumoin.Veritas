using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Persistence.Journal;

/// <summary>
/// The on-disk framing of one durable DATASET journal record: a length-prefixed payload followed by a
/// checksum over the length and payload together, so a torn or corrupt record is detected on replay rather
/// than mis-read. It mirrors <see cref="JournalRecordFormat"/>'s framing byte for byte and extends the
/// payload with the things a dataset record needs to be self-contained — a <b>term section</b> carrying
/// every dictionary term minted since the previous durable record (through the shared
/// <see cref="TermRecordCodec"/>), the entry's per-graph <b>transitions</b>, and the entry's
/// presence-flagged <b>causality annotation</b> (the dotted observed-remove knowledge replay reads back
/// verbatim rather than re-deriving).
/// </summary>
/// <remarks>
/// <para>
/// Record layout: <c>[u32 payloadLength][payload][checksum(algorithm width)]</c>, all little-endian, the
/// checksum covering the length prefix and the payload. The payload begins with a one-byte format version so
/// an incompatible record is refused rather than silently mis-decoded.
/// </para>
/// <para>
/// <b>Forked is rejected at both gates.</b> A durable dataset log is self-contained and replays from empty,
/// so the cross-journal <see cref="EditSessionEntryKind.Forked"/> edge has no meaning here: it is refused at
/// the write boundary before any bytes are produced and at the read boundary so a hand-crafted record can
/// never be mis-decoded. A forked world keeps an in-memory journal in this cut; cross-journal anchoring of a
/// durable log is the named follow-up.
/// </para>
/// </remarks>
internal static class DatasetJournalRecordFormat
{
    /// <summary>The size of the record's leading little-endian payload-length prefix.</summary>
    private const int LengthPrefixSize = sizeof(uint);

    /// <summary>The payload layout version written as the payload's first byte; bumped when the payload byte layout changes.</summary>
    private const byte PayloadFormatVersion = 1;

    /// <summary>The number of bytes a <see cref="Guid"/> serialises to.</summary>
    private const int GuidByteCount = 16;

    /// <summary>The fixed-field payload size before the optional commitment and session, the term section, and the transitions: version (1) + sequence (8) + kind (1) + parent (8) + child (8) + commitment presence (1) + session presence (1) + timestamp (8).</summary>
    private const int FixedPayloadFieldsSize = sizeof(byte) + sizeof(long) + sizeof(byte) + sizeof(ulong) + sizeof(ulong) + sizeof(byte) + sizeof(byte) + sizeof(long);

    /// <summary>The number of bytes <see cref="WriteRecord"/> writes for an entry and its new terms under the given checksum.</summary>
    /// <param name="entry">The entry to size.</param>
    /// <param name="termWatermark">The count captured by the previous durable record; the exclusive lower bound of this record's term identifier range.</param>
    /// <param name="newTerms">The terms minted since the previous durable record, one record each.</param>
    /// <param name="checksum">The record checksum algorithm.</param>
    /// <returns>The record byte size.</returns>
    /// <exception cref="ArgumentException">The record would exceed the single-record byte bound.</exception>
    internal static int ComputeRecordSize(in DatasetJournalEntry entry, int termWatermark, ReadOnlySpan<RdfTerm> newTerms, ChecksumAlgorithm checksum)
    {
        long payloadSize = ComputePayloadSize(entry, newTerms);

        return EnsureRecordFits(payloadSize, checksum, entry);
    }

    /// <summary>Writes one framed record (length, payload, checksum) into <paramref name="destination"/>.</summary>
    /// <param name="destination">The buffer to write into; at least <see cref="ComputeRecordSize"/> bytes long.</param>
    /// <param name="entry">The entry to write.</param>
    /// <param name="termWatermark">The count captured by the previous durable record; the exclusive lower bound of this record's term identifier range.</param>
    /// <param name="newTerms">The terms minted since the previous durable record, written in identifier order.</param>
    /// <param name="checksum">The record checksum algorithm.</param>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="ArgumentException">The entry carries the <see cref="EditSessionEntryKind.Forked"/> kind the durable dataset log does not accept, or the record would exceed the single-record byte bound. Rejecting at the write boundary keeps the durable log readable, since the read gate refuses the same kind.</exception>
    /// <exception cref="NotSupportedException">The host is big-endian.</exception>
    internal static int WriteRecord(Span<byte> destination, in DatasetJournalEntry entry, int termWatermark, ReadOnlySpan<RdfTerm> newTerms, ChecksumAlgorithm checksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();

        if(entry.EntryKind > EditSessionEntryKind.Abandoned)
        {
            throw new ArgumentException($"The durable dataset journal record format does not accept the entry kind '{entry.EntryKind}'.", nameof(entry));
        }

        long payloadSize = ComputePayloadSize(entry, newTerms);
        int recordSize = EnsureRecordFits(payloadSize, checksum, entry);
        int payloadSizeInt = (int)payloadSize;

        BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)payloadSizeInt);
        WritePayload(destination.Slice(LengthPrefixSize, payloadSizeInt), entry, termWatermark, newTerms);

        int checksummedLength = LengthPrefixSize + payloadSizeInt;
        checksum.Compute(destination[..checksummedLength], destination.Slice(checksummedLength, checksum.ByteWidth));

        return recordSize;
    }

    /// <summary>Attempts to read one record at the start of <paramref name="source"/>, verifying its checksum and decoding the entry, its term section, and its transitions.</summary>
    /// <param name="source">The byte image positioned at a record.</param>
    /// <param name="checksum">The record checksum algorithm.</param>
    /// <param name="pool">The pool the decoded term bytes are interned into.</param>
    /// <param name="record">The decoded entry, term watermark, and new terms when the record is complete and verifies.</param>
    /// <param name="recordLength">The total bytes the record occupies when it verifies.</param>
    /// <returns><see langword="true"/> when a complete, checksum-valid record was read; <see langword="false"/> when the record is short or fails its checksum (the recovery boundary).</returns>
    /// <exception cref="InvalidDataException">A checksum-valid record carries a malformed payload (a codec error rather than at-rest corruption).</exception>
    /// <exception cref="NotSupportedException">A checksum-valid record carries an unsupported payload format version, or the host is big-endian.</exception>
    internal static bool TryReadRecord(ReadOnlySpan<byte> source, ChecksumAlgorithm checksum, Utf8StringPool pool, out DatasetJournalRecord record, out int recordLength)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        record = default;
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

        record = ReadPayload(source.Slice(LengthPrefixSize, (int)payloadLength), pool);
        recordLength = (int)total;

        return true;
    }

    /// <summary>The serialized payload size for an entry and its new terms.</summary>
    /// <param name="entry">The entry to size.</param>
    /// <param name="newTerms">The terms minted since the previous durable record.</param>
    /// <returns>The payload byte size.</returns>
    private static long ComputePayloadSize(in DatasetJournalEntry entry, ReadOnlySpan<RdfTerm> newTerms)
    {
        long size = FixedPayloadFieldsSize;
        size += entry.EditCommitment.HasValue ? sizeof(ulong) : 0;
        size += entry.SessionId.HasValue ? GuidByteCount : 0;

        //Term section: an i32 watermark and an i32 count, then one record per new term.
        size += sizeof(int) + sizeof(int);
        Stack<RdfTerm> work = new();
        foreach(RdfTerm term in newTerms)
        {
            size += TermRecordCodec.ComputeSize(term, work);
        }

        //Transitions: an i32 count, then per transition a graph id, two presence-flagged roots, and two triple arrays.
        size += sizeof(int);
        foreach(DatasetGraphTransition transition in entry.Transitions)
        {
            size += sizeof(uint);
            size += sizeof(byte) + (transition.ParentRoot.HasValue ? sizeof(ulong) : 0);
            size += sizeof(byte) + (transition.ChildRoot.HasValue ? sizeof(ulong) : 0);
            size += LittleEndianBuffer.ArrayBytes<EncodedTriple>(transition.Additions.AsSpan().Length);
            size += LittleEndianBuffer.ArrayBytes<EncodedTriple>(transition.Removals.AsSpan().Length);
        }

        //Causality: a presence byte, then the annotation when present.
        size += sizeof(byte);
        if(entry.Causality is { } causality)
        {
            size += causality.ComputeSerializedSize();
        }

        return size;
    }

    /// <summary>Ensures a record of the given payload size fits the single-record byte bound and returns its total byte size.</summary>
    /// <param name="payloadSize">The computed payload byte size.</param>
    /// <param name="checksum">The record checksum algorithm.</param>
    /// <param name="entry">The entry being sized, named on the thrown exception.</param>
    /// <returns>The total record byte size.</returns>
    /// <exception cref="ArgumentException">The record would exceed <see cref="int.MaxValue"/> bytes.</exception>
    private static int EnsureRecordFits(long payloadSize, ChecksumAlgorithm checksum, in DatasetJournalEntry entry)
    {
        long total = LengthPrefixSize + payloadSize + checksum.ByteWidth;
        if(total > int.MaxValue)
        {
            throw new ArgumentException($"A durable dataset journal record of {payloadSize} payload bytes exceeds the {int.MaxValue}-byte single-record bound; one dataset transition serialises into one record, and a bulk initial build beyond roughly two gigabytes of payload must persist through the generation store instead.", nameof(entry));
        }

        return (int)total;
    }

    /// <summary>Writes the entry payload into <paramref name="destination"/> (exactly <see cref="ComputePayloadSize"/> bytes).</summary>
    /// <param name="destination">The payload-sized destination.</param>
    /// <param name="entry">The entry to write.</param>
    /// <param name="termWatermark">The exclusive lower bound of this record's term identifier range.</param>
    /// <param name="newTerms">The terms minted since the previous durable record, written in identifier order.</param>
    private static void WritePayload(Span<byte> destination, in DatasetJournalEntry entry, int termWatermark, ReadOnlySpan<RdfTerm> newTerms)
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

        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], termWatermark);
        p += sizeof(int);
        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], newTerms.Length);
        p += sizeof(int);
        Stack<RdfTerm> work = new();
        foreach(RdfTerm term in newTerms)
        {
            p += TermRecordCodec.Write(term, destination[p..], work);
        }

        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], entry.Transitions.Length);
        p += sizeof(int);
        foreach(DatasetGraphTransition transition in entry.Transitions)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination[p..], transition.Graph.Encoded);
            p += sizeof(uint);
            p += WriteRoot(destination[p..], transition.ParentRoot);
            p += WriteRoot(destination[p..], transition.ChildRoot);
            p += LittleEndianBuffer.WriteArray(destination[p..], transition.Additions.AsSpan());
            p += LittleEndianBuffer.WriteArray(destination[p..], transition.Removals.AsSpan());
        }

        if(entry.Causality is { } causality)
        {
            destination[p++] = 1;
            p += causality.WriteTo(destination[p..]);
        }
        else
        {
            destination[p++] = 0;
        }
    }

    /// <summary>Writes a presence-flagged graph root: a presence byte, and the root's value when present. A <see langword="null"/> root (create on a parent, drop on a child) writes only the flag; <see cref="NodeIdentifier.Empty"/> is a present value, so existed-and-empty is distinct from absent across the round-trip.</summary>
    /// <param name="destination">The buffer slice to write into.</param>
    /// <param name="root">The root, or <see langword="null"/> when the side is absent.</param>
    /// <returns>The number of bytes written.</returns>
    private static int WriteRoot(Span<byte> destination, NodeIdentifier? root)
    {
        if(root.HasValue)
        {
            destination[0] = 1;
            BinaryPrimitives.WriteUInt64LittleEndian(destination[1..], root.Value.Value);

            return 1 + sizeof(ulong);
        }

        destination[0] = 0;

        return 1;
    }

    /// <summary>Reads a record payload written by <see cref="WritePayload"/>.</summary>
    /// <param name="source">The payload bytes (exactly one payload).</param>
    /// <param name="pool">The pool the decoded term bytes are interned into.</param>
    /// <returns>The decoded entry, term watermark, and new terms.</returns>
    /// <exception cref="InvalidDataException">The payload is truncated, has a bad presence flag, kind, or count, an out-of-range timestamp, or unexpected trailing bytes.</exception>
    /// <exception cref="NotSupportedException">The payload format version is unsupported.</exception>
    private static DatasetJournalRecord ReadPayload(ReadOnlySpan<byte> source, Utf8StringPool pool)
    {
        int p = 0;
        EnsureRemaining(source, p, sizeof(byte));
        byte version = source[p++];
        if(version != PayloadFormatVersion)
        {
            throw new NotSupportedException($"Dataset journal record payload format version {version} is not supported; this build reads version {PayloadFormatVersion}.");
        }

        EnsureRemaining(source, p, sizeof(long));
        long sequence = BinaryPrimitives.ReadInt64LittleEndian(source[p..]);
        p += sizeof(long);

        EnsureRemaining(source, p, sizeof(byte));
        byte kindByte = source[p++];
        if(kindByte > (byte)EditSessionEntryKind.Abandoned)
        {
            throw new InvalidDataException("A dataset journal record names an entry kind the durable format does not accept.");
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
            throw new InvalidDataException("A dataset journal record has an invalid edit-commitment presence flag.");
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
            throw new InvalidDataException("A dataset journal record has an invalid session-id presence flag.");
        }

        EnsureRemaining(source, p, sizeof(long));
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(source[p..]);
        p += sizeof(long);
        DateTimeOffset timestamp = ReadTimestamp(ticks);

        RdfTerm[] newTerms = ReadTermSection(source, ref p, pool, out int termWatermark);
        ImmutableArray<DatasetGraphTransition> transitions = ReadTransitions(source, ref p);

        EnsureRemaining(source, p, sizeof(byte));
        byte causalityPresence = source[p++];
        CommitCausality? causality = null;
        if(causalityPresence == 1)
        {
            causality = CommitCausality.ReadFrom(source, ref p);
        }
        else if(causalityPresence != 0)
        {
            throw new InvalidDataException("A dataset journal record has an invalid causality presence flag.");
        }

        if(p != source.Length)
        {
            throw new InvalidDataException("A dataset journal record payload has unexpected trailing bytes.");
        }

        DatasetJournalEntry entry = new(new NodeIdentifier(parent), new NodeIdentifier(child), kind, session, commitment, transitions, timestamp, sequence, causality);

        return new DatasetJournalRecord(entry, termWatermark, newTerms);
    }

    /// <summary>Reads the term section: the watermark, the term count, and one record per term.</summary>
    /// <param name="source">The payload bytes.</param>
    /// <param name="position">The read cursor; advanced past the section.</param>
    /// <param name="pool">The pool the decoded term bytes are interned into.</param>
    /// <param name="watermark">The section's watermark — the exclusive lower bound of the record's term identifier range.</param>
    /// <returns>The decoded terms, in identifier order.</returns>
    /// <exception cref="InvalidDataException">The section is truncated or declares a negative or out-of-bounds count.</exception>
    private static RdfTerm[] ReadTermSection(ReadOnlySpan<byte> source, ref int position, Utf8StringPool pool, out int watermark)
    {
        EnsureRemaining(source, position, sizeof(int) + sizeof(int));
        watermark = BinaryPrimitives.ReadInt32LittleEndian(source[position..]);
        position += sizeof(int);
        int termCount = BinaryPrimitives.ReadInt32LittleEndian(source[position..]);
        position += sizeof(int);
        if(watermark < 0)
        {
            throw new InvalidDataException("A dataset journal record declares a negative term watermark.");
        }

        if(termCount < 0 || termCount > source.Length - position)
        {
            //Each term record is at least one byte, so a count beyond the remaining bytes is malformed; the
            //bound also caps the allocation below against an adversarial checksum-valid record.
            throw new InvalidDataException("A dataset journal record declares a term count beyond its payload bounds.");
        }

        RdfTerm[] newTerms = new RdfTerm[termCount];
        Stack<TermRecordCodec.TripleFrame> frames = new();
        for(int i = 0; i < termCount; i++)
        {
            newTerms[i] = TermRecordCodec.Read(source[position..], out int consumed, pool, frames);
            position += consumed;
        }

        return newTerms;
    }

    /// <summary>Reads the transitions: the count, then per transition a graph id, two presence-flagged roots, and two triple arrays.</summary>
    /// <param name="source">The payload bytes.</param>
    /// <param name="position">The read cursor; advanced past the transitions.</param>
    /// <returns>The decoded transitions.</returns>
    /// <exception cref="InvalidDataException">The section is truncated, declares a negative or out-of-bounds count, or carries an invalid presence flag.</exception>
    private static ImmutableArray<DatasetGraphTransition> ReadTransitions(ReadOnlySpan<byte> source, ref int position)
    {
        EnsureRemaining(source, position, sizeof(int));
        int count = BinaryPrimitives.ReadInt32LittleEndian(source[position..]);
        position += sizeof(int);
        if(count < 0 || count > source.Length - position)
        {
            //Each transition is at least one byte, so a count beyond the remaining bytes is malformed; the
            //bound caps the builder allocation against an adversarial checksum-valid record.
            throw new InvalidDataException("A dataset journal record declares a transition count beyond its payload bounds.");
        }

        if(count == 0)
        {
            return [];
        }

        ImmutableArray<DatasetGraphTransition>.Builder builder = ImmutableArray.CreateBuilder<DatasetGraphTransition>(count);
        for(int j = 0; j < count; j++)
        {
            EnsureRemaining(source, position, sizeof(uint));
            uint graph = BinaryPrimitives.ReadUInt32LittleEndian(source[position..]);
            position += sizeof(uint);

            NodeIdentifier? parentRoot = ReadRoot(source, ref position, "parent-root");
            NodeIdentifier? childRoot = ReadRoot(source, ref position, "child-root");

            EncodedTriple[] additions = LittleEndianBuffer.ReadArray<EncodedTriple>(source[position..], out int additionsConsumed);
            position += additionsConsumed;
            EncodedTriple[] removals = LittleEndianBuffer.ReadArray<EncodedTriple>(source[position..], out int removalsConsumed);
            position += removalsConsumed;

            builder.Add(new DatasetGraphTransition(TermId.FromEncoded(graph), parentRoot, childRoot, [.. additions], [.. removals]));
        }

        return builder.MoveToImmutable();
    }

    /// <summary>Reads a presence-flagged graph root written by <see cref="WriteRoot"/>.</summary>
    /// <param name="source">The payload bytes.</param>
    /// <param name="position">The read cursor; advanced past the flag and any value.</param>
    /// <param name="role">The role named on a malformed-flag refusal (a parent or child root).</param>
    /// <returns>The root, or <see langword="null"/> when the side is absent.</returns>
    /// <exception cref="InvalidDataException">The section is truncated or the presence flag is neither 0 nor 1.</exception>
    private static NodeIdentifier? ReadRoot(ReadOnlySpan<byte> source, ref int position, string role)
    {
        EnsureRemaining(source, position, sizeof(byte));
        byte presence = source[position++];
        if(presence == 1)
        {
            EnsureRemaining(source, position, sizeof(ulong));
            NodeIdentifier root = new(BinaryPrimitives.ReadUInt64LittleEndian(source[position..]));
            position += sizeof(ulong);

            return root;
        }

        if(presence != 0)
        {
            throw new InvalidDataException($"A dataset journal record has an invalid {role} presence flag.");
        }

        return null;
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
            throw new InvalidDataException("A dataset journal record carries an out-of-range timestamp.");
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
            throw new InvalidDataException("A dataset journal record payload is truncated.");
        }
    }
}
