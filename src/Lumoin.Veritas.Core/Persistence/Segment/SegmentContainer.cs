using System;
using System.Buffers.Binary;
using System.IO;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Persistence.Segment;

/// <summary>
/// The framing discipline shared by every Veritas segment format (the system-of-record item segment, the term
/// dictionary, the sketch, the parity, and the columnar sidecar): a fixed magic-versioned header — an 8-byte magic,
/// a major/minor format version, a required-feature mask, and an explicitly-selected per-block
/// <see cref="ChecksumAlgorithm"/> id — and a single front-matter checksum trailer over each format's front matter.
/// Each format keeps its own magic and version (passed in) and lays down its own scalars, block directory, and
/// payload after the header; this primitive owns ONLY the bytes and the validation discipline they share, so the
/// version/feature/checksum rules cannot drift between formats and a new format adopts them by calling here rather
/// than re-deriving them.
/// </summary>
/// <remarks>
/// <para>
/// This is NOT the control-plane framing: the manifest (<c>VTSMFST1</c>) and CURRENT pointer carry no feature mask
/// and have their own headers. The feature mask currently defines exactly one bit,
/// <see cref="FrontMatterChecksumFeature"/> (a front-matter trailer is present), which a checksum-bearing image sets
/// and a checksum-free image clears; an image whose mask sets any other bit is refused as requiring an unknown
/// feature.
/// </para>
/// </remarks>
internal static class SegmentContainer
{
    /// <summary>The fixed segment header size: magic (8) + major (1) + minor (1) + required-feature mask (8) + checksum-algorithm id (1).</summary>
    public const int HeaderSize = 8 + 1 + 1 + 8 + 1;

    /// <summary>The checksum-algorithm id written when no checksum is selected.</summary>
    public const byte ChecksumAlgorithmNone = 0;

    /// <summary>The required-feature bit set when a front-matter checksum trailer is present; an older reader refuses an image whose mask carries an unknown bit.</summary>
    public const ulong FrontMatterChecksumFeature = 1UL << 0;

    /// <summary>The required features this build understands; an image whose mask sets any other bit is refused. Currently exactly the front-matter-checksum feature.</summary>
    private const ulong KnownRequiredFeatures = FrontMatterChecksumFeature;

    /// <summary>
    /// Writes the shared segment header (magic, version, required-feature mask, checksum-algorithm id) into the
    /// front of <paramref name="destination"/>; the feature mask sets <see cref="FrontMatterChecksumFeature"/> when a
    /// checksum is supplied. The caller lays its own scalars and payload down from <see cref="HeaderSize"/> onward.
    /// </summary>
    /// <param name="destination">The image buffer to write the header into; at least <see cref="HeaderSize"/> bytes.</param>
    /// <param name="magic">The format's 8-byte magic.</param>
    /// <param name="versionMajor">The format's major version.</param>
    /// <param name="versionMinor">The format's minor version.</param>
    /// <param name="checksum">The per-block checksum algorithm whose id is stamped (and whose presence sets the front-matter feature), or <see langword="null"/> for a checksum-free image.</param>
    /// <returns>The number of bytes written — always <see cref="HeaderSize"/>.</returns>
    /// <exception cref="NotSupportedException">The host is big-endian.</exception>
    public static int WriteHeader(Span<byte> destination, ReadOnlySpan<byte> magic, byte versionMajor, byte versionMinor, ChecksumAlgorithm? checksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();

        int p = 0;
        magic.CopyTo(destination[p..]);
        p += magic.Length;
        destination[p++] = versionMajor;
        destination[p++] = versionMinor;
        BinaryPrimitives.WriteUInt64LittleEndian(destination[p..], checksum is null ? 0UL : FrontMatterChecksumFeature);
        p += sizeof(ulong);
        destination[p++] = checksum?.Id ?? ChecksumAlgorithmNone;

        return p;
    }

    /// <summary>
    /// Parses and validates the shared segment header: refuses a too-short image, a magic mismatch, an unsupported
    /// major version, a required-feature bit this build does not understand, or a checksum-algorithm id the resolver
    /// declines, and refuses a header whose front-matter-checksum feature bit disagrees with its checksum-algorithm
    /// id. Returns the resolved per-block checksum (<see langword="null"/> when the image carries none); the caller
    /// reads its scalars from <see cref="HeaderSize"/> onward.
    /// </summary>
    /// <param name="source">The byte image.</param>
    /// <param name="magic">The format's expected 8-byte magic.</param>
    /// <param name="versionMajor">The format major version this build reads.</param>
    /// <param name="resolveChecksum">Resolves the image's checksum-algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <param name="artifactDescription">A short human-readable name of the artifact kind for the diagnostic messages (e.g. "item segment").</param>
    /// <returns>The resolved per-block checksum, or <see langword="null"/> when the image carries none.</returns>
    /// <exception cref="InvalidDataException">The image is too short, not this format (magic mismatch), or its feature flag disagrees with its checksum id.</exception>
    /// <exception cref="NotSupportedException">The major version, a required feature, or the checksum algorithm is unsupported, or the host is big-endian.</exception>
    /// <exception cref="InvalidOperationException">The resolver violated the resolution witness — a misrouted answer or a downgraded reserved keyed id (see <see cref="ChecksumAlgorithm.ResolveForRead"/>).</exception>
    public static ChecksumAlgorithm? ParseHeader(ReadOnlySpan<byte> source, ReadOnlySpan<byte> magic, byte versionMajor, ResolveChecksumAlgorithmDelegate? resolveChecksum, string artifactDescription)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        if(source.Length < HeaderSize)
        {
            throw new InvalidDataException($"The bytes are too short to be a {artifactDescription} image.");
        }

        int p = 0;
        if(!source[..magic.Length].SequenceEqual(magic))
        {
            throw new InvalidDataException($"The bytes are not a {artifactDescription} image (magic mismatch).");
        }

        p += magic.Length;
        byte imageVersionMajor = source[p++];
        byte imageVersionMinor = source[p++];
        if(imageVersionMajor != versionMajor)
        {
            throw new NotSupportedException($"{artifactDescription} format version {imageVersionMajor}.{imageVersionMinor} is not supported; this build reads major version {versionMajor}.");
        }

        ulong featureFlags = BinaryPrimitives.ReadUInt64LittleEndian(source[p..]);
        p += sizeof(ulong);
        if((featureFlags & ~KnownRequiredFeatures) != 0)
        {
            throw new NotSupportedException($"The {artifactDescription} requires a feature this build does not understand.");
        }

        byte checksumAlgorithmId = source[p];
        ChecksumAlgorithm? checksum = null;
        if(checksumAlgorithmId != ChecksumAlgorithmNone)
        {
            checksum = ChecksumAlgorithm.ResolveForRead(checksumAlgorithmId, resolveChecksum, artifactDescription);
        }

        bool hasFrontMatterChecksum = (featureFlags & FrontMatterChecksumFeature) != 0;
        if(hasFrontMatterChecksum != (checksum is not null))
        {
            throw new InvalidDataException($"The {artifactDescription}'s front-matter-checksum feature flag disagrees with its checksum-algorithm id.");
        }

        return checksum;
    }

    /// <summary>
    /// Computes the front-matter trailer — a checksum over <c>destination[..frontMatterEnd]</c> — into the image
    /// tail (the last <see cref="ChecksumAlgorithm.ByteWidth"/> bytes). The front matter is everything a format's
    /// per-block digests do NOT cover (its header, scalars, block directory, and per-block checksum section).
    /// </summary>
    /// <param name="destination">The image buffer.</param>
    /// <param name="checksum">The checksum algorithm.</param>
    /// <param name="frontMatterEnd">The byte offset one past the front matter the trailer covers.</param>
    /// <param name="totalSize">The total image byte size; the trailer occupies its last <see cref="ChecksumAlgorithm.ByteWidth"/> bytes.</param>
    public static void WriteTrailer(Span<byte> destination, ChecksumAlgorithm checksum, int frontMatterEnd, int totalSize)
    {
        checksum.Compute(destination[..frontMatterEnd], destination.Slice(totalSize - checksum.ByteWidth, checksum.ByteWidth));
    }

    /// <summary>Recomputes the front-matter trailer and compares it to the stored digest at the image tail, returning the verdict rather than throwing.</summary>
    /// <param name="source">The byte image.</param>
    /// <param name="checksum">The checksum algorithm.</param>
    /// <param name="frontMatterEnd">The byte offset one past the front matter the trailer covers.</param>
    /// <param name="totalSize">The total image byte size; the trailer occupies its last <see cref="ChecksumAlgorithm.ByteWidth"/> bytes.</param>
    /// <returns>Whether the recomputed trailer matched its stored digest.</returns>
    public static bool VerifyTrailer(ReadOnlySpan<byte> source, ChecksumAlgorithm checksum, int frontMatterEnd, int totalSize)
    {
        Span<byte> computed = stackalloc byte[ChecksumAlgorithm.MaximumByteWidth];
        checksum.Compute(source[..frontMatterEnd], computed[..checksum.ByteWidth]);

        return computed[..checksum.ByteWidth].SequenceEqual(source.Slice(totalSize - checksum.ByteWidth, checksum.ByteWidth));
    }
}
