using System;
using System.Buffers.Binary;
using System.IO;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Persistence.Manifest;

/// <summary>
/// The single commit pointer: a tiny, self-checksummed record naming the live manifest generation.
/// Publishing a new one by an atomic rename is the one commit point of the persistence layer
/// (<see cref="AtomicPublish"/>); recovery reads it and uses the generation it names — never the
/// highest generation on disk — so a crash before the publish leaves the prior pointer, naming the
/// prior committed generation, wholly in force. Its own checksum detects at-rest rot, against which a
/// few prior pointers are retained.
/// </summary>
public sealed class CurrentPointer
{
    /// <summary>The 8-byte magic identifying a Veritas CURRENT pointer image.</summary>
    private static readonly byte[] PointerMagic = "VTSCURR1"u8.ToArray();

    /// <summary>The format major version; a mismatch is refused.</summary>
    private const byte FormatVersionMajor = 1;

    /// <summary>The format minor version; bumped for backward-compatible additions.</summary>
    private const byte FormatVersionMinor = 0;

    /// <summary>The checksum-algorithm id written when no checksum is selected; such a pointer carries no self-check.</summary>
    private const byte ChecksumAlgorithmNone = 0;

    /// <summary>The fixed prefix size: magic (8) + major (1) + minor (1) + checksum-algorithm id (1) + commit generation (8).</summary>
    private const int PrefixSize = 8 + 1 + 1 + 1 + sizeof(long);

    /// <summary>Creates a pointer naming a committed manifest generation.</summary>
    /// <param name="commitGeneration">The committed manifest generation this pointer makes live.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="commitGeneration"/> is negative.</exception>
    public CurrentPointer(long commitGeneration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(commitGeneration);

        CommitGeneration = commitGeneration;
    }

    /// <summary>The committed manifest generation this pointer makes live.</summary>
    public long CommitGeneration { get; }

    /// <summary>The number of bytes <see cref="WriteTo"/> writes under the given checksum selection.</summary>
    /// <param name="checksum">The checksum algorithm whose trailer self-checks the pointer, or <see langword="null"/> for none.</param>
    /// <returns>The image byte size.</returns>
    public static int ComputeSerializedSize(ChecksumAlgorithm? checksum)
    {
        return PrefixSize + (checksum?.ByteWidth ?? 0);
    }

    /// <summary>Writes this pointer's self-describing image into <paramref name="destination"/> (exactly <see cref="ComputeSerializedSize"/> bytes for the same <paramref name="checksum"/>).</summary>
    /// <param name="destination">The buffer to write into; at least the computed size long.</param>
    /// <param name="checksum">The checksum algorithm whose trailer self-checks the pointer, or <see langword="null"/> for none.</param>
    /// <exception cref="NotSupportedException">The host is big-endian.</exception>
    public void WriteTo(Span<byte> destination, ChecksumAlgorithm? checksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();

        int p = 0;
        PointerMagic.CopyTo(destination[p..]);
        p += PointerMagic.Length;
        destination[p++] = FormatVersionMajor;
        destination[p++] = FormatVersionMinor;
        destination[p++] = checksum?.Id ?? ChecksumAlgorithmNone;
        BinaryPrimitives.WriteInt64LittleEndian(destination[p..], CommitGeneration);
        p += sizeof(long);

        if(checksum is not null)
        {
            checksum.Compute(destination[..p], destination.Slice(p, checksum.ByteWidth));
        }
    }

    /// <summary>Reconstructs a pointer from an image written by <see cref="WriteTo"/>, verifying its self-checksum so at-rest rot is refused rather than trusted.</summary>
    /// <param name="source">The pointer image.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The reconstructed pointer.</returns>
    /// <exception cref="InvalidDataException">The image is not a CURRENT pointer, is truncated, or fails its self-checksum.</exception>
    /// <exception cref="NotSupportedException">The major version or checksum algorithm is unsupported, or the host is big-endian.</exception>
    /// <exception cref="InvalidOperationException">The resolver violated the resolution witness — a misrouted answer or a downgraded reserved keyed id (see <see cref="ChecksumAlgorithm.ResolveForRead"/>).</exception>
    public static CurrentPointer ReadFrom(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        if(source.Length < PrefixSize)
        {
            throw new InvalidDataException("The bytes are too short to be a CURRENT pointer image.");
        }

        int p = 0;
        if(!source[..PointerMagic.Length].SequenceEqual(PointerMagic))
        {
            throw new InvalidDataException("The bytes are not a CURRENT pointer image (magic mismatch).");
        }

        p += PointerMagic.Length;
        byte versionMajor = source[p++];
        byte versionMinor = source[p++];
        if(versionMajor != FormatVersionMajor)
        {
            throw new NotSupportedException($"CURRENT pointer format version {versionMajor}.{versionMinor} is not supported; this build reads major version {FormatVersionMajor}.");
        }

        byte checksumAlgorithmId = source[p++];
        ChecksumAlgorithm? checksum = null;
        if(checksumAlgorithmId != ChecksumAlgorithmNone)
        {
            checksum = ChecksumAlgorithm.ResolveForRead(checksumAlgorithmId, resolveChecksum, "CURRENT pointer");
        }

        long commitGeneration = BinaryPrimitives.ReadInt64LittleEndian(source[p..]);
        p += sizeof(long);
        if(commitGeneration < 0)
        {
            throw new InvalidDataException("The CURRENT pointer names a negative commit generation.");
        }

        if(checksum is not null)
        {
            if(source.Length - p < checksum.ByteWidth)
            {
                throw new InvalidDataException("The CURRENT pointer is truncated before its checksum.");
            }

            Span<byte> computed = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
            checksum.Compute(source[..p], computed[..checksum.ByteWidth]);
            if(!computed[..checksum.ByteWidth].SequenceEqual(source.Slice(p, checksum.ByteWidth)))
            {
                throw new InvalidDataException("The CURRENT pointer failed its checksum (at-rest corruption).");
            }
        }

        return new CurrentPointer(commitGeneration);
    }
}
