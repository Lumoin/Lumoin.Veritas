using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Hashing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Serialization;

namespace Lumoin.Veritas.Core.Persistence.Journal;

/// <summary>
/// The read-only facts a dataset-journal file's optional v2 header carries, exposed for the engine's reopen and
/// the recovery pivot to consume. A v1 file (no header) yields <see cref="IsV2"/> <see langword="false"/> with the
/// remaining fields at their neutral defaults, so a caller that reads the header of any file gets a uniform shape.
/// </summary>
/// <param name="IsV2">Whether the file begins with a v2 header; <see langword="false"/> for a headerless v1 log.</param>
/// <param name="Anchor">The onboarding anchor — the persisted dataset state this log continues from — or <see cref="NodeIdentifier.Empty"/> for a create-path (self-contained) log or a v1 file.</param>
/// <param name="ReplicationEpoch">The dictionary replication epoch stamped at file creation; <c>0</c> for a v1 file.</param>
/// <param name="AttachTermWatermark">The dictionary term count at file creation — the exclusive lower bound the log's term-watermark chain starts from; <c>0</c> for a create-path or v1 file.</param>
/// <param name="HeaderLength">The header's TRUE on-disk byte length (preamble + declared payload + self-checksum) — the offset the record stream begins at. A same-major higher-minor header declares a LONGER payload, so the record scan and every truncation boundary must use this, never the fixed v1.0 <see cref="DatasetJournalHeader.Size"/>; <c>0</c> for a v1 file (records at offset 0).</param>
/// <param name="RecordStreamChecksumId">The record-stream checksum algorithm id the header records; the opener refuses a log whose framing algorithm disagrees with it, since a mis-framed scan would read every record as a clean torn tail. <c>0</c> for a v1 file.</param>
internal readonly record struct DatasetJournalHeaderInfo(bool IsV2, NodeIdentifier Anchor, ulong ReplicationEpoch, int AttachTermWatermark, int HeaderLength, byte RecordStreamChecksumId);

/// <summary>
/// The dataset-journal format v2 file header: a fixed-layout, self-checksummed preamble written once at file
/// creation and read once at open, describing the log that follows it. It carries the onboarding anchor (the
/// persisted state an attached log continues from), the dictionary replication epoch and term watermark the log
/// was created against, and the id of the checksum algorithm the record stream is framed under — resolved and
/// refused at open so an unreadable algorithm never truncates a log. A v1 file has no header: its first record's
/// length prefix sits at offset 0, and it keeps opening exactly as before.
/// </summary>
/// <remarks>
/// <para>
/// <b>The discriminator.</b> The 8-byte magic fixes byte index 3 to <see cref="DiscriminatorByte"/> (0xB1), which
/// is <c>&gt;= 0x80</c> ON PURPOSE: read as the low 4 bytes of a little-endian <see cref="uint"/>, a value with
/// its top byte set is <c>&gt;= 2^31</c>, and a v1 first-record length prefix can never be — every record's total
/// byte size is bounded below <see cref="int.MaxValue"/> (the <c>EnsureRecordFits</c> bound in
/// <see cref="DatasetJournalRecordFormat"/>), so its payload-length prefix is always <c>&lt; 2^31</c> and its byte
/// 3 always <c>&lt; 0x80</c>. The discriminator is therefore EXACT, not probabilistic: any file whose byte 3 is
/// 0xB1 is a v2 file, and no v1 log can be mistaken for one. The hazard is one-way — a reader that predates this
/// header sees an impossible length prefix, recovers a write offset of 0, and would TRUNCATE a v2 file; nothing on
/// disk can protect a v2 file from code that does not know the format, so the upgrade is one-way.
/// </para>
/// <para>
/// <b>Layout, little-endian, in order:</b> magic (8) · format major (1) · format minor (1) · payload length u16
/// (2) · [payload: record-stream checksum algorithm id (1) · anchor presence (1) · anchor state id u64 (8) ·
/// dictionary replication epoch u64 (8) · attach term watermark i32 (4)] · self-checksum (8). A foreign major is
/// refused; a higher minor with the same major is readable — the reader consumes the fields it knows and skips to
/// the checksum using the declared payload length, so sealed-journal reservations (suite id, key epoch, salt) can
/// append as minor bumps. The self-checksum is a FIXED XxHash3-64 over the magic through the payload inclusive: it
/// must be verifiable before the record-stream algorithm id is trusted, so it cannot itself ride the configurable
/// record-stream algorithm. It is an at-rest integrity check, not the tamper-evidence tier — keyed protection
/// arrives with the planned sealed-journal fields.
/// </para>
/// </remarks>
internal static class DatasetJournalHeader
{
    /// <summary>The byte index the discriminator sits at within the magic.</summary>
    internal const int DiscriminatorIndex = 3;

    /// <summary>The discriminator byte (>= 0x80), which no v1 first-record length prefix can carry at this index.</summary>
    internal const byte DiscriminatorByte = 0xB1;

    /// <summary>The number of bytes in the magic.</summary>
    private const int MagicLength = 8;

    /// <summary>The header format major version; a mismatch is refused.</summary>
    private const byte FormatVersionMajor = 1;

    /// <summary>The header format minor version; bumped for backward-compatible payload additions.</summary>
    private const byte FormatVersionMinor = 0;

    /// <summary>The fixed preamble size before the payload: magic (8) + major (1) + minor (1) + payload-length prefix (2). The preamble alone determines the header's true on-disk length (<see cref="DeclaredLength"/>), so a bounded reader fetches it first and then exactly the declared remainder.</summary>
    internal const int PreambleSize = MagicLength + sizeof(byte) + sizeof(byte) + sizeof(ushort);

    /// <summary>The v1.0 payload size: record-stream checksum id (1) + anchor presence (1) + anchor (8) + replication epoch (8) + attach term watermark (4).</summary>
    private const int PayloadSize = sizeof(byte) + sizeof(byte) + sizeof(ulong) + sizeof(ulong) + sizeof(int);

    /// <summary>The self-checksum width — a fixed XxHash3-64 tag.</summary>
    private const int SelfChecksumWidth = sizeof(ulong);

    /// <summary>The offset of the record-stream checksum algorithm id within the payload.</summary>
    private const int RecordChecksumIdOffset = PreambleSize;

    /// <summary>The offset of the anchor presence flag.</summary>
    private const int AnchorPresenceOffset = RecordChecksumIdOffset + sizeof(byte);

    /// <summary>The offset of the anchor state id.</summary>
    private const int AnchorValueOffset = AnchorPresenceOffset + sizeof(byte);

    /// <summary>The offset of the dictionary replication epoch.</summary>
    private const int ReplicationEpochOffset = AnchorValueOffset + sizeof(ulong);

    /// <summary>The offset of the attach term watermark.</summary>
    private const int AttachTermWatermarkOffset = ReplicationEpochOffset + sizeof(ulong);

    /// <summary>The offset the self-checksum sits at in a v1.0 header (immediately after the v1.0 payload).</summary>
    private const int V1PayloadEnd = PreambleSize + PayloadSize;

    /// <summary>The total byte size of a v1.0 header — the offset a freshly-created log's first record is written at.</summary>
    internal const int Size = V1PayloadEnd + SelfChecksumWidth;

    /// <summary>The 8-byte magic identifying a dataset-journal v2 file; byte 3 is the <see cref="DiscriminatorByte"/> discriminator.</summary>
    private static ReadOnlySpan<byte> Magic => [(byte)'V', (byte)'T', (byte)'D', DiscriminatorByte, (byte)'J', (byte)'R', (byte)'N', (byte)'L'];

    /// <summary>Reports whether a file's leading bytes carry the v2 discriminator, so the reader knows to parse a header rather than a v1 record at offset 0.</summary>
    /// <param name="source">The file's leading bytes.</param>
    /// <returns><see langword="true"/> when byte index 3 is the discriminator — an impossible value in a v1 first-record length prefix; <see langword="false"/> for a v1 log or a file too short to carry the discriminator.</returns>
    internal static bool LooksLikeV2(ReadOnlySpan<byte> source)
    {
        return source.Length > DiscriminatorIndex && source[DiscriminatorIndex] == DiscriminatorByte;
    }

    /// <summary>The header's true on-disk byte length declared by its preamble: the preamble, the declared payload, and the self-checksum. A higher-minor header declares a longer payload, so this — not the fixed v1.0 <see cref="Size"/> — is where the record stream begins.</summary>
    /// <param name="preamble">At least the first <see cref="PreambleSize"/> bytes of the header.</param>
    /// <returns>The declared total header length in bytes.</returns>
    internal static int DeclaredLength(ReadOnlySpan<byte> preamble)
    {
        int payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(preamble[(MagicLength + sizeof(byte) + sizeof(byte))..]);

        return PreambleSize + payloadLength + SelfChecksumWidth;
    }

    /// <summary>Writes a v1.0 header into <paramref name="destination"/> (exactly <see cref="Size"/> bytes).</summary>
    /// <param name="destination">The buffer to write into; at least <see cref="Size"/> bytes long.</param>
    /// <param name="anchor">The onboarding anchor, or <see cref="NodeIdentifier.Empty"/> for a self-contained create-path log (absent anchor, zeroed value).</param>
    /// <param name="replicationEpoch">The dictionary replication epoch at file creation.</param>
    /// <param name="attachTermWatermark">The dictionary term count at file creation; the exclusive lower bound the log's term-watermark chain starts from.</param>
    /// <param name="recordStreamChecksum">The checksum algorithm the record stream is framed under; its id is recorded so a reader resolves and refuses an unreadable stream at open.</param>
    /// <exception cref="ArgumentNullException"><paramref name="recordStreamChecksum"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The host is big-endian.</exception>
    internal static void Write(Span<byte> destination, NodeIdentifier anchor, ulong replicationEpoch, int attachTermWatermark, ChecksumAlgorithm recordStreamChecksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();
        ArgumentNullException.ThrowIfNull(recordStreamChecksum);

        Magic.CopyTo(destination);
        destination[MagicLength] = FormatVersionMajor;
        destination[MagicLength + sizeof(byte)] = FormatVersionMinor;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[(MagicLength + sizeof(byte) + sizeof(byte))..], PayloadSize);

        destination[RecordChecksumIdOffset] = recordStreamChecksum.Id;
        bool hasAnchor = anchor != NodeIdentifier.Empty;
        destination[AnchorPresenceOffset] = hasAnchor ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt64LittleEndian(destination[AnchorValueOffset..], hasAnchor ? anchor.Value : 0UL);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[ReplicationEpochOffset..], replicationEpoch);
        BinaryPrimitives.WriteInt32LittleEndian(destination[AttachTermWatermarkOffset..], attachTermWatermark);

        XxHash3.Hash(destination[..V1PayloadEnd], destination.Slice(V1PayloadEnd, SelfChecksumWidth));
    }

    /// <summary>Reads and validates a v2 header from a file whose leading bytes carry the discriminator (<see cref="LooksLikeV2"/>).</summary>
    /// <param name="source">The file bytes, positioned at the header.</param>
    /// <param name="resolveChecksum">Resolves the record-stream checksum algorithm id; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The header facts, including the header's true on-disk length (the record-stream start) and the record-stream algorithm id the caller cross-checks against its framing algorithm.</returns>
    /// <exception cref="InvalidDataException">The header is truncated, its full magic is corrupt, it declares a payload shorter than the major version requires, its anchor presence flag or watermark is invalid, or it fails its self-checksum.</exception>
    /// <exception cref="NotSupportedException">The header's major version is unsupported, its record-stream checksum algorithm id does not resolve, or the host is big-endian.</exception>
    /// <exception cref="InvalidOperationException">The resolver violated the resolution witness — a misrouted answer or a downgraded reserved keyed id (see <see cref="ChecksumAlgorithm.ResolveForRead"/>).</exception>
    internal static DatasetJournalHeaderInfo Read(ReadOnlySpan<byte> source, ResolveChecksumAlgorithmDelegate? resolveChecksum)
    {
        LittleEndianBuffer.EnsureLittleEndian();

        if(source.Length < PreambleSize)
        {
            throw new InvalidDataException("A dataset journal v2 header is truncated before its preamble; a torn creation acked nothing, so the file is refused.");
        }

        if(!source[..MagicLength].SequenceEqual(Magic))
        {
            throw new InvalidDataException("A dataset journal file carries the v2 discriminator but not the full v2 magic (a corrupt header).");
        }

        byte major = source[MagicLength];
        byte minor = source[MagicLength + sizeof(byte)];
        if(major != FormatVersionMajor)
        {
            throw new NotSupportedException($"Dataset journal header format version {major}.{minor} is not supported; this build reads major version {FormatVersionMajor}.");
        }

        int payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(source[(MagicLength + sizeof(byte) + sizeof(byte))..]);
        if(payloadLength < PayloadSize)
        {
            throw new InvalidDataException($"A dataset journal v2 header declares a {payloadLength}-byte payload, shorter than the {PayloadSize} bytes major version {major} requires.");
        }

        int checksumOffset = PreambleSize + payloadLength;
        if(source.Length < checksumOffset + SelfChecksumWidth)
        {
            throw new InvalidDataException("A dataset journal v2 header is truncated before its self-checksum; a torn creation acked nothing, so the file is refused.");
        }

        //The self-checksum is verified BEFORE the record-stream algorithm id is trusted: a reader must know the
        //header is intact before it acts on any field it carries.
        Span<byte> computed = stackalloc byte[SelfChecksumWidth];
        XxHash3.Hash(source[..checksumOffset], computed);
        if(!computed.SequenceEqual(source.Slice(checksumOffset, SelfChecksumWidth)))
        {
            throw new InvalidDataException("A dataset journal v2 header failed its self-checksum (at-rest corruption).");
        }

        //The witnessed resolution refuses an unresolvable, misrouted, or downgraded record-stream algorithm at
        //open, so an unreadable or lied-about algorithm never truncates a log; the resolved instance itself is
        //not needed — the caller cross-checks the raw id against its framing algorithm.
        byte recordStreamId = source[RecordChecksumIdOffset];
        _ = ChecksumAlgorithm.ResolveForRead(recordStreamId, resolveChecksum, "dataset journal record stream");

        byte anchorPresence = source[AnchorPresenceOffset];
        if(anchorPresence > 1)
        {
            throw new InvalidDataException("A dataset journal v2 header has an invalid anchor presence flag.");
        }

        ulong anchorValue = BinaryPrimitives.ReadUInt64LittleEndian(source[AnchorValueOffset..]);
        NodeIdentifier anchor = anchorPresence == 1 ? new NodeIdentifier(anchorValue) : NodeIdentifier.Empty;
        ulong replicationEpoch = BinaryPrimitives.ReadUInt64LittleEndian(source[ReplicationEpochOffset..]);
        int attachTermWatermark = BinaryPrimitives.ReadInt32LittleEndian(source[AttachTermWatermarkOffset..]);
        if(attachTermWatermark < 0)
        {
            throw new InvalidDataException("A dataset journal v2 header declares a negative attach term watermark.");
        }

        return new DatasetJournalHeaderInfo(IsV2: true, anchor, replicationEpoch, attachTermWatermark, checksumOffset + SelfChecksumWidth, recordStreamId);
    }
}
