using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Persistence.Manifest;

/// <summary>
/// One immutable manifest generation: the authoritative list of the files that make up a committed
/// state — each with its role, byte range, and checksum — stamped with a monotonic commit generation
/// and the dictionary and provenance epochs the files were written against. A generation is staged and
/// flushed as pre-commit work; it becomes live only when a <see cref="CurrentPointer"/> naming it is
/// published (<see cref="Lumoin.Veritas.Core.Persistence.AtomicPublish"/>). The whole image is
/// self-checksummed so at-rest rot is refused on load rather than trusted.
/// </summary>
/// <remarks>
/// <para>
/// The file-role taxonomy is extensible (<see cref="ManifestFileRole"/>), so a new artifact kind is a
/// new entry, not a format revision; a reader carries roles it does not recognise rather than failing
/// the generation. The manifest is node-local layout, not reconcilable content — a peer recovers the
/// content and rebuilds its own manifest.
/// </para>
/// </remarks>
public sealed class Manifest
{
    /// <summary>The 8-byte magic identifying a Veritas manifest generation image.</summary>
    private static readonly byte[] ManifestMagic = "VTSMFST1"u8.ToArray();

    /// <summary>The format major version; a mismatch is refused.</summary>
    private const byte FormatVersionMajor = 1;

    /// <summary>The format minor version; bumped for backward-compatible additions.</summary>
    private const byte FormatVersionMinor = 0;

    /// <summary>The checksum-algorithm id written when no checksum is selected.</summary>
    private const byte ChecksumAlgorithmNone = 0;

    /// <summary>The fixed header size: magic (8) + major (1) + minor (1) + checksum-algorithm id (1) + commit generation (8) + dictionary epoch (8) + provenance epoch (8) + entry count (4).</summary>
    private const int HeaderSize = 8 + 1 + 1 + 1 + sizeof(long) + sizeof(long) + sizeof(long) + sizeof(int);

    /// <summary>The fixed per-entry prefix size before the variable-length file name and checksum: role code (4) + byte offset (8) + byte length (8) + file-name byte length (4).</summary>
    private const int EntryPrefixSize = sizeof(int) + sizeof(long) + sizeof(long) + sizeof(int);

    /// <summary>Creates a manifest generation.</summary>
    /// <param name="commitGeneration">The monotonic commit-generation stamp.</param>
    /// <param name="dictionaryEpoch">The term-dictionary epoch the files were written against.</param>
    /// <param name="provenanceEpoch">The system-of-record state the files were built from.</param>
    /// <param name="entries">The files this generation makes live.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="commitGeneration"/> is negative.</exception>
    public Manifest(long commitGeneration, long dictionaryEpoch, long provenanceEpoch, IReadOnlyList<ManifestEntry> entries)
        : this(commitGeneration, dictionaryEpoch, provenanceEpoch, entries, checksumAlgorithm: null)
    {
    }

    /// <summary>Creates a manifest generation, recording the checksum algorithm it was reconstructed under so a reader can attest its entries' digests with the same algorithm the generation was written with.</summary>
    /// <param name="commitGeneration">The monotonic commit-generation stamp.</param>
    /// <param name="dictionaryEpoch">The term-dictionary epoch the files were written against.</param>
    /// <param name="provenanceEpoch">The system-of-record state the files were built from.</param>
    /// <param name="entries">The files this generation makes live.</param>
    /// <param name="checksumAlgorithm">The checksum algorithm the image was written under, or <see langword="null"/> when it carries no checksums.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="commitGeneration"/> is negative.</exception>
    private Manifest(long commitGeneration, long dictionaryEpoch, long provenanceEpoch, IReadOnlyList<ManifestEntry> entries, ChecksumAlgorithm? checksumAlgorithm)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(commitGeneration);
        ArgumentNullException.ThrowIfNull(entries);

        CommitGeneration = commitGeneration;
        DictionaryEpoch = dictionaryEpoch;
        ProvenanceEpoch = provenanceEpoch;
        Entries = entries;
        ChecksumAlgorithm = checksumAlgorithm;
    }

    /// <summary>The monotonic commit-generation stamp; orders generations and excludes superseded ones in a degraded scan.</summary>
    public long CommitGeneration { get; }

    /// <summary>The term-dictionary epoch the files were written against; a file is read only under its epoch.</summary>
    public long DictionaryEpoch { get; }

    /// <summary>The system-of-record state the files were built from; stale-sidecar detection.</summary>
    public long ProvenanceEpoch { get; }

    /// <summary>The files this generation makes live.</summary>
    public IReadOnlyList<ManifestEntry> Entries { get; }

    /// <summary>The checksum algorithm this generation's image was written under — the algorithm its per-entry digests and self-checksum trailer are computed with — or <see langword="null"/> when the manifest carries no checksums. A manifest reconstructed from an image (<see cref="ReadFrom"/>) carries the resolved algorithm; one built in memory to be written carries none until it is read back.</summary>
    public ChecksumAlgorithm? ChecksumAlgorithm { get; }

    /// <summary>The number of bytes <see cref="WriteTo"/> writes under the given checksum selection.</summary>
    /// <param name="checksum">The checksum algorithm whose per-entry digests and self-trailer size the image, or <see langword="null"/> for none.</param>
    /// <returns>The image byte size.</returns>
    public int ComputeSerializedSize(ChecksumAlgorithm? checksum)
    {
        int checksumWidth = checksum?.ByteWidth ?? 0;
        int total = HeaderSize;
        for(int i = 0; i < Entries.Count; i++)
        {
            total += EntryPrefixSize + System.Text.Encoding.UTF8.GetByteCount(Entries[i].FileName) + checksumWidth;
        }

        return total + checksumWidth;
    }

    /// <summary>Writes this generation's self-describing image into <paramref name="destination"/> (exactly <see cref="ComputeSerializedSize"/> bytes for the same <paramref name="checksum"/>).</summary>
    /// <param name="destination">The buffer to write into; at least the computed size long.</param>
    /// <param name="checksum">The checksum algorithm for the per-entry digests and the self-trailer, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentException">An entry's stored checksum width does not match <paramref name="checksum"/>.</exception>
    /// <exception cref="NotSupportedException">The host is big-endian.</exception>
    public void WriteTo(Span<byte> destination, ChecksumAlgorithm? checksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        int checksumWidth = checksum?.ByteWidth ?? 0;

        int p = 0;
        ManifestMagic.CopyTo(destination[p..]);
        p += ManifestMagic.Length;
        destination[p++] = FormatVersionMajor;
        destination[p++] = FormatVersionMinor;
        destination[p++] = checksum?.Id ?? ChecksumAlgorithmNone;
        BinaryPrimitives.WriteInt64LittleEndian(destination[p..], CommitGeneration);
        p += sizeof(long);
        BinaryPrimitives.WriteInt64LittleEndian(destination[p..], DictionaryEpoch);
        p += sizeof(long);
        BinaryPrimitives.WriteInt64LittleEndian(destination[p..], ProvenanceEpoch);
        p += sizeof(long);
        BinaryPrimitives.WriteInt32LittleEndian(destination[p..], Entries.Count);
        p += sizeof(int);

        for(int i = 0; i < Entries.Count; i++)
        {
            ManifestEntry entry = Entries[i];
            BinaryPrimitives.WriteInt32LittleEndian(destination[p..], entry.Role.Code);
            p += sizeof(int);
            BinaryPrimitives.WriteInt64LittleEndian(destination[p..], entry.ByteOffset);
            p += sizeof(long);
            BinaryPrimitives.WriteInt64LittleEndian(destination[p..], entry.ByteLength);
            p += sizeof(long);

            int nameBytes = System.Text.Encoding.UTF8.GetBytes(entry.FileName, destination[(p + sizeof(int))..]);
            BinaryPrimitives.WriteInt32LittleEndian(destination[p..], nameBytes);
            p += sizeof(int) + nameBytes;

            if(checksumWidth != 0)
            {
                if(entry.Checksum.Length != checksumWidth)
                {
                    throw new ArgumentException($"A manifest entry's checksum is {entry.Checksum.Length} bytes but the manifest algorithm is {checksumWidth} bytes wide.", nameof(checksum));
                }

                entry.Checksum.Span.CopyTo(destination.Slice(p, checksumWidth));
                p += checksumWidth;
            }
        }

        if(checksum is not null)
        {
            checksum.Compute(destination[..p], destination.Slice(p, checksum.ByteWidth));
        }
    }

    /// <summary>Reconstructs a generation from an image written by <see cref="WriteTo"/>, verifying its self-checksum so at-rest rot is refused rather than trusted.</summary>
    /// <param name="source">The manifest image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The reconstructed generation.</returns>
    /// <exception cref="InvalidDataException">The image is not a manifest, is malformed, or fails its self-checksum.</exception>
    /// <exception cref="NotSupportedException">The major version or checksum algorithm is unsupported, or the host is big-endian.</exception>
    /// <exception cref="InvalidOperationException">The resolver violated the resolution witness — a misrouted answer or a downgraded reserved keyed id (see <see cref="ChecksumAlgorithm.ResolveForRead"/>).</exception>
    public static Manifest ReadFrom(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        if(source.Length < HeaderSize)
        {
            throw new InvalidDataException("The bytes are too short to be a manifest image.");
        }

        int p = 0;
        if(!source[..ManifestMagic.Length].SequenceEqual(ManifestMagic))
        {
            throw new InvalidDataException("The bytes are not a manifest image (magic mismatch).");
        }

        p += ManifestMagic.Length;
        byte versionMajor = source[p++];
        byte versionMinor = source[p++];
        if(versionMajor != FormatVersionMajor)
        {
            throw new NotSupportedException($"Manifest format version {versionMajor}.{versionMinor} is not supported; this build reads major version {FormatVersionMajor}.");
        }

        byte checksumAlgorithmId = source[p++];
        ChecksumAlgorithm? checksum = null;
        if(checksumAlgorithmId != ChecksumAlgorithmNone)
        {
            checksum = ChecksumAlgorithm.ResolveForRead(checksumAlgorithmId, resolveChecksum, "manifest");
        }

        int checksumWidth = checksum?.ByteWidth ?? 0;
        long commitGeneration = BinaryPrimitives.ReadInt64LittleEndian(source[p..]);
        p += sizeof(long);
        long dictionaryEpoch = BinaryPrimitives.ReadInt64LittleEndian(source[p..]);
        p += sizeof(long);
        long provenanceEpoch = BinaryPrimitives.ReadInt64LittleEndian(source[p..]);
        p += sizeof(long);
        if(commitGeneration < 0)
        {
            throw new InvalidDataException("The manifest names a negative commit generation.");
        }

        int entryCount = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
        p += sizeof(int);
        if(entryCount < 0)
        {
            throw new InvalidDataException("The manifest declares a negative entry count.");
        }

        ManifestEntry[] entries = new ManifestEntry[entryCount];
        for(int e = 0; e < entryCount; e++)
        {
            if(source.Length - p < EntryPrefixSize)
            {
                throw new InvalidDataException("The manifest is truncated within an entry.");
            }

            int roleCode = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
            p += sizeof(int);
            long byteOffset = BinaryPrimitives.ReadInt64LittleEndian(source[p..]);
            p += sizeof(long);
            long byteLength = BinaryPrimitives.ReadInt64LittleEndian(source[p..]);
            p += sizeof(long);
            int nameLength = BinaryPrimitives.ReadInt32LittleEndian(source[p..]);
            p += sizeof(int);
            if(nameLength < 0 || nameLength > source.Length - p)
            {
                throw new InvalidDataException("The manifest declares a file-name length beyond its bounds.");
            }

            string fileName = System.Text.Encoding.UTF8.GetString(source.Slice(p, nameLength));
            p += nameLength;

            byte[] entryChecksum = [];
            if(checksumWidth != 0)
            {
                if(source.Length - p < checksumWidth)
                {
                    throw new InvalidDataException("The manifest is truncated before an entry's checksum.");
                }

                entryChecksum = source.Slice(p, checksumWidth).ToArray();
                p += checksumWidth;
            }

            entries[e] = ResolveRole(roleCode, fileName, byteOffset, byteLength, entryChecksum);
        }

        if(checksum is not null)
        {
            if(source.Length - p < checksum.ByteWidth)
            {
                throw new InvalidDataException("The manifest is truncated before its checksum.");
            }

            Span<byte> computed = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
            checksum.Compute(source[..p], computed[..checksum.ByteWidth]);
            if(!computed[..checksum.ByteWidth].SequenceEqual(source.Slice(p, checksum.ByteWidth)))
            {
                throw new InvalidDataException("The manifest failed its checksum (at-rest corruption).");
            }
        }

        return new Manifest(commitGeneration, dictionaryEpoch, provenanceEpoch, entries, checksum);
    }

    /// <summary>Reconstructs an entry, resolving its role from the on-disk code (the built-in roles by code, else a carried unknown-role placeholder so the generation is not failed by an unrecognised role).</summary>
    /// <param name="roleCode">The on-disk role code.</param>
    /// <param name="fileName">The entry's file name.</param>
    /// <param name="byteOffset">The entry's byte offset.</param>
    /// <param name="byteLength">The entry's byte length.</param>
    /// <param name="checksum">The entry's stored checksum.</param>
    /// <returns>The reconstructed entry.</returns>
    /// <exception cref="InvalidDataException">The role code is the reserved 0.</exception>
    private static ManifestEntry ResolveRole(int roleCode, string fileName, long byteOffset, long byteLength, ReadOnlyMemory<byte> checksum)
    {
        if(roleCode == 0)
        {
            throw new InvalidDataException("The manifest names a reserved role code 0.");
        }

        ManifestFileRole role = roleCode switch
        {
            1 => ManifestFileRole.DataSegment,
            2 => ManifestFileRole.Sidecar,
            3 => ManifestFileRole.Sketch,
            4 => ManifestFileRole.Parity,
            5 => ManifestFileRole.Stats,
            6 => ManifestFileRole.Dictionary,
            7 => ManifestFileRole.NamedGraphSegment,
            8 => ManifestFileRole.Losses,
            _ => ManifestFileRole.Create(roleCode, "Unknown"),
        };

        return new ManifestEntry(role, fileName, byteOffset, byteLength, checksum);
    }
}
