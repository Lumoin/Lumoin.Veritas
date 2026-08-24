using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Indexing;

/// <summary>
/// One registration's persisted snapshot inside a value-index sidecar image: the axis identity the
/// recovery matches against the composed registry — the datatype IRI and the declared predicate
/// IRI(s) — and the method-owned payload bytes.
/// </summary>
public sealed class ValueIndexImageEntry
{
    /// <summary>Constructs an entry.</summary>
    /// <param name="datatypeIri">The registration's axis datatype IRI.</param>
    /// <param name="startPredicateIri">The point-axis predicate, or the interval pair's start predicate.</param>
    /// <param name="endPredicateIri">The interval pair's end predicate, or <see langword="null"/> for a point axis.</param>
    /// <param name="payload">The method-owned snapshot payload.</param>
    public ValueIndexImageEntry(Utf8String datatypeIri, Utf8String startPredicateIri, Utf8String? endPredicateIri, ReadOnlyMemory<byte> payload)
    {
        DatatypeIri = datatypeIri;
        StartPredicateIri = startPredicateIri;
        EndPredicateIri = endPredicateIri;
        Payload = payload;
    }

    /// <summary>The registration's axis datatype IRI.</summary>
    public Utf8String DatatypeIri { get; }

    /// <summary>The point-axis predicate, or the interval pair's start predicate.</summary>
    public Utf8String StartPredicateIri { get; }

    /// <summary>The interval pair's end predicate, or <see langword="null"/> for a point axis.</summary>
    public Utf8String? EndPredicateIri { get; }

    /// <summary>The method-owned snapshot payload.</summary>
    public ReadOnlyMemory<byte> Payload { get; }
}

/// <summary>
/// The value-index sidecar container: every registered access method's snapshot built from ONE
/// captured dataset state, stamped with that state's content-addressed identifier. The stamp is the
/// staleness check — recovery refuses an image whose stamp differs from the recovered generation's
/// provenance epoch, so a sidecar can only ever warm-install over exactly the data it was built
/// from; each method additionally validates its own payload's configuration stamps at install.
/// The image is re-derivable: a missing, damaged, stale, or configuration-mismatched sidecar is
/// dropped and the registered methods rebuild from the served store at the first probe.
/// </summary>
public sealed class ValueIndexImage
{
    /// <summary>The serialized image's magic leader.</summary>
    private static ReadOnlySpan<byte> Magic => "VIDX"u8;

    /// <summary>The serialized image format version this implementation writes and accepts.</summary>
    private const byte FormatVersion = 1;

    /// <summary>The fixed header size: the magic, the version byte, the state stamp, and the entry count.</summary>
    private const int HeaderSize = 4 + 1 + 8 + 4;

    /// <summary>Constructs an image over per-registration entries stamped with the dataset state they were built from.</summary>
    /// <param name="stateId">The content-addressed dataset state identifier the snapshots were built from.</param>
    /// <param name="entries">The per-registration entries.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    public ValueIndexImage(ulong stateId, IReadOnlyList<ValueIndexImageEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        StateId = stateId;
        Entries = entries;
    }

    /// <summary>The content-addressed dataset state identifier the snapshots were built from — the staleness stamp recovery validates against the recovered generation's provenance epoch.</summary>
    public ulong StateId { get; }

    /// <summary>The per-registration entries.</summary>
    public IReadOnlyList<ValueIndexImageEntry> Entries { get; }

    /// <summary>Computes the serialized image's byte size.</summary>
    /// <returns>The byte size.</returns>
    public int ComputeSerializedSize()
    {
        int size = HeaderSize;
        for(int i = 0; i < Entries.Count; i++)
        {
            ValueIndexImageEntry entry = Entries[i];
            size += sizeof(int) + entry.DatatypeIri.Span.Length;
            size += sizeof(int) + entry.StartPredicateIri.Span.Length;
            size += 1 + (entry.EndPredicateIri is { } end ? sizeof(int) + end.Span.Length : 0);
            size += sizeof(int) + entry.Payload.Length;
        }

        return size;
    }

    /// <summary>Writes the image into <paramref name="destination"/>, whose length is at least <see cref="ComputeSerializedSize"/> bytes.</summary>
    /// <param name="destination">The destination buffer.</param>
    public void WriteTo(Span<byte> destination)
    {
        Magic.CopyTo(destination);
        destination[4] = FormatVersion;
        BinaryPrimitives.WriteUInt64LittleEndian(destination[5..], StateId);
        BinaryPrimitives.WriteInt32LittleEndian(destination[13..], Entries.Count);

        int position = HeaderSize;
        for(int i = 0; i < Entries.Count; i++)
        {
            ValueIndexImageEntry entry = Entries[i];
            position = WriteBlock(destination, position, entry.DatatypeIri.Span);
            position = WriteBlock(destination, position, entry.StartPredicateIri.Span);
            if(entry.EndPredicateIri is { } end)
            {
                destination[position] = 1;
                position = WriteBlock(destination, position + 1, end.Span);
            }
            else
            {
                destination[position] = 0;
                position++;
            }

            position = WriteBlock(destination, position, entry.Payload.Span);
        }
    }

    /// <summary>
    /// Parses a serialized image, refusing a malformed one: a wrong magic or version, a negative or
    /// over-length block, or trailing bytes all decline rather than yield a partial image. Every
    /// parsed block is copied out of <paramref name="image"/>, so the returned value does not alias
    /// the caller's buffer.
    /// </summary>
    /// <param name="image">The serialized image.</param>
    /// <param name="value">Receives the parsed image on success.</param>
    /// <returns><see langword="true"/> when the image parsed whole.</returns>
    public static bool TryReadFrom(ReadOnlySpan<byte> image, out ValueIndexImage? value)
    {
        value = null;
        if(image.Length < HeaderSize || !image[..4].SequenceEqual(Magic) || image[4] != FormatVersion)
        {
            return false;
        }

        ulong stateId = BinaryPrimitives.ReadUInt64LittleEndian(image[5..]);
        int entryCount = BinaryPrimitives.ReadInt32LittleEndian(image[13..]);
        if(entryCount < 0)
        {
            return false;
        }

        List<ValueIndexImageEntry> entries = new(entryCount);
        int position = HeaderSize;
        for(int i = 0; i < entryCount; i++)
        {
            if(!TryReadBlock(image, ref position, out byte[]? datatype)
                || !TryReadBlock(image, ref position, out byte[]? start))
            {
                return false;
            }

            if(position >= image.Length)
            {
                return false;
            }

            byte endFlag = image[position];
            position++;
            byte[]? end = null;
            if(endFlag == 1)
            {
                if(!TryReadBlock(image, ref position, out end))
                {
                    return false;
                }
            }
            else if(endFlag != 0)
            {
                return false;
            }

            if(!TryReadBlock(image, ref position, out byte[]? payload))
            {
                return false;
            }

            entries.Add(new ValueIndexImageEntry(
                new Utf8String(datatype!),
                new Utf8String(start!),
                end is null ? null : new Utf8String(end),
                payload));
        }

        if(position != image.Length)
        {
            return false;
        }

        value = new ValueIndexImage(stateId, entries);

        return true;
    }

    /// <summary>Writes one length-prefixed block at <paramref name="position"/>.</summary>
    /// <param name="destination">The destination buffer.</param>
    /// <param name="position">The write position.</param>
    /// <param name="block">The block bytes.</param>
    /// <returns>The position after the block.</returns>
    private static int WriteBlock(Span<byte> destination, int position, ReadOnlySpan<byte> block)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination[position..], block.Length);
        block.CopyTo(destination[(position + sizeof(int))..]);

        return position + sizeof(int) + block.Length;
    }

    /// <summary>Reads one length-prefixed block at <paramref name="position"/>, copying it out; false on a negative or over-length prefix.</summary>
    /// <param name="image">The serialized image.</param>
    /// <param name="position">The read position, advanced past the block on success.</param>
    /// <param name="block">Receives the copied block bytes.</param>
    /// <returns><see langword="true"/> when the block lies wholly within the image.</returns>
    private static bool TryReadBlock(ReadOnlySpan<byte> image, ref int position, out byte[]? block)
    {
        block = null;
        if(position + sizeof(int) > image.Length)
        {
            return false;
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(image[position..]);
        if(length < 0 || position + sizeof(int) + length > image.Length)
        {
            return false;
        }

        block = image.Slice(position + sizeof(int), length).ToArray();
        position += sizeof(int) + length;

        return true;
    }
}
