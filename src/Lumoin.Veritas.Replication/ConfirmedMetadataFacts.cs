using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// What THIS host already knows the deployment has settled: whether its replica identity claim was confirmed
/// by consensus, and the confirmed lineage baseline it read back. It is the local memory that makes a routine
/// reopen of a confirmed identity and lineage cost no coordination at all — the host reads this record beside
/// its identity directory and proceeds, so the metadata plane is never on the open path of a host that has
/// nothing left to coordinate.
/// </summary>
/// <param name="IdentityClaimConfirmed">Whether consensus confirmed this host's claim to its replica identity axis.</param>
/// <param name="ConfirmedCausalityDigest">The causality digest of the confirmed lineage baseline, or <see langword="null"/> while no baseline is confirmed.</param>
/// <param name="ConfirmedStateId">The dataset StateId of the confirmed lineage baseline, or <see langword="null"/> while no baseline is confirmed.</param>
/// <param name="ConfirmedDictionaryEpoch">The term-dictionary epoch of the confirmed lineage baseline, or <see langword="null"/> while no baseline is confirmed.</param>
/// <remarks>
/// <para>
/// IT IS A CACHE OF DECIDED FACTS, NEVER A DECISION. Nothing is written here that consensus has not already
/// decided, so a lost or deleted record costs a plane round trip on the next open and never changes an
/// answer. That is what keeps the local record safe to consult first: it can only report facts that were
/// agreed, so consulting it can only skip work, never take a decision the deployment did not take.
/// </para>
/// <para>
/// IT CARRIES ITS OWN LAYOUT AND NO CODEC SEAM. The coordinated record crosses a wire and reaches hosts that
/// may encode differently, so its encoding is injected; this record never leaves the host that wrote it, so a
/// serialization dependency here would buy nothing and cost a seam every consumer must fill. The layout is
/// fixed-width, little-endian, and exactly <see cref="SerializedLength"/> bytes:
/// </para>
/// <para>
/// byte 0 — the layout version, currently 1. byte 1 — the flag bits: bit 0 the identity-claim confirmation,
/// bit 1 the causality digest's presence, bit 2 the StateId's presence, bit 3 the dictionary epoch's
/// presence; bits 4 to 7 are unused and written as zero. bytes 2 to 9 — the causality digest, unsigned
/// 64-bit. bytes 10 to 17 — the dataset StateId, unsigned 64-bit. bytes 18 to 25 — the dictionary epoch,
/// signed 64-bit. An absent field's bytes are zero.
/// </para>
/// <para>
/// PRESENCE IS A FLAG, NOT A ZERO. Zero is a legitimate value for all three optional fields — the empty
/// dataset's identifier is zero, and so is the first dictionary epoch — so absence is carried in the flag
/// byte and never inferred from the value, matching the tri-state
/// <see cref="LineageBaseline"/> keeps for exactly the same reason.
/// </para>
/// <para>
/// READING FAILS CLOSED. An unknown layout version, an unknown flag bit, or a non-zero absent field is
/// refused rather than read past: the file was written by something other than this layout, and treating it
/// as absent would let the next write overwrite a record this build cannot understand. A refusal costs a
/// plane round trip, which is always available; a silent misread would not be.
/// </para>
/// <para>
/// Equality is the synthesized record equality and is content-based without help: every member is a
/// <see langword="bool"/>, a number, or a nullable of one, so a record read back from its bytes equals the
/// record that was written.
/// </para>
/// </remarks>
public sealed record ConfirmedMetadataFacts(
    bool IdentityClaimConfirmed,
    NodeIdentifier? ConfirmedCausalityDigest,
    NodeIdentifier? ConfirmedStateId,
    long? ConfirmedDictionaryEpoch)
{
    /// <summary>The exact byte length of the serialized record; the layout is fixed-width, so this is both what a write produces and what a read requires.</summary>
    public const int SerializedLength = 26;

    /// <summary>The layout version this build writes and is the only one it reads.</summary>
    private const byte LayoutVersion = 1;

    /// <summary>The flag bit carrying <see cref="IdentityClaimConfirmed"/>.</summary>
    private const byte IdentityClaimConfirmedFlag = 0b0000_0001;

    /// <summary>The flag bit carrying whether <see cref="ConfirmedCausalityDigest"/> is present.</summary>
    private const byte CausalityDigestPresentFlag = 0b0000_0010;

    /// <summary>The flag bit carrying whether <see cref="ConfirmedStateId"/> is present.</summary>
    private const byte StateIdPresentFlag = 0b0000_0100;

    /// <summary>The flag bit carrying whether <see cref="ConfirmedDictionaryEpoch"/> is present.</summary>
    private const byte DictionaryEpochPresentFlag = 0b0000_1000;

    /// <summary>Every flag bit this layout version defines; a set bit outside this mask is a record this build cannot read.</summary>
    private const byte DefinedFlags = IdentityClaimConfirmedFlag | CausalityDigestPresentFlag | StateIdPresentFlag | DictionaryEpochPresentFlag;

    /// <summary>The offset of the layout version byte.</summary>
    private const int VersionOffset = 0;

    /// <summary>The offset of the flag byte.</summary>
    private const int FlagsOffset = 1;

    /// <summary>The offset of the causality digest.</summary>
    private const int CausalityDigestOffset = 2;

    /// <summary>The offset of the dataset StateId.</summary>
    private const int StateIdOffset = 10;

    /// <summary>The offset of the term-dictionary epoch.</summary>
    private const int DictionaryEpochOffset = 18;

    /// <summary>
    /// The record a host starts from when it has confirmed nothing: no identity claim, no baseline. It is
    /// what a host uses when no record has been written yet, so the coordination path has one shape whether
    /// the store is empty or not.
    /// </summary>
    public static ConfirmedMetadataFacts Unconfirmed { get; } = new(
        IdentityClaimConfirmed: false,
        ConfirmedCausalityDigest: null,
        ConfirmedStateId: null,
        ConfirmedDictionaryEpoch: null);

    /// <summary>
    /// Whether a confirmed lineage baseline was read back: the digest, the StateId, and the dictionary epoch
    /// are present together, which is the only shape <see cref="WithConfirmedBaseline"/> writes, so a half
    /// filled baseline names no phase here any more than it does in the coordinated record.
    /// </summary>
    public bool IsBaselineConfirmed => ConfirmedCausalityDigest.HasValue && ConfirmedStateId.HasValue && ConfirmedDictionaryEpoch.HasValue;

    /// <summary>
    /// Whether this host may reopen without consulting the plane: its identity claim is confirmed and its
    /// lineage baseline is confirmed, so there is nothing left for a consultation to settle. A host whose
    /// record does not allow it consults the plane, which is also what a host with no record at all does.
    /// </summary>
    public bool AllowsRoutineReopen => IdentityClaimConfirmed && IsBaselineConfirmed;

    /// <summary>Records that consensus confirmed this host's claim to its replica identity axis.</summary>
    /// <returns>The record with the identity claim marked confirmed.</returns>
    public ConfirmedMetadataFacts WithIdentityClaimConfirmed()
    {
        return this with { IdentityClaimConfirmed = true };
    }

    /// <summary>
    /// Records the confirmed lineage baseline read back from the coordinated record. All three fields are
    /// filled together, because a baseline is confirmed as a whole and a reader must never see one of them
    /// without the others.
    /// </summary>
    /// <param name="causalityDigest">The causality digest of the confirmed baseline.</param>
    /// <param name="stateId">The dataset StateId of the confirmed baseline.</param>
    /// <param name="dictionaryEpoch">The term-dictionary epoch of the confirmed baseline.</param>
    /// <returns>The record carrying the confirmed baseline.</returns>
    public ConfirmedMetadataFacts WithConfirmedBaseline(NodeIdentifier causalityDigest, NodeIdentifier stateId, long dictionaryEpoch)
    {
        return this with
        {
            ConfirmedCausalityDigest = causalityDigest,
            ConfirmedStateId = stateId,
            ConfirmedDictionaryEpoch = dictionaryEpoch
        };
    }

    /// <summary>Writes the record into <paramref name="destination"/> in the fixed layout the type remarks state.</summary>
    /// <param name="destination">The buffer written into; at least <see cref="SerializedLength"/> bytes, of which exactly that many are written.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than <see cref="SerializedLength"/>.</exception>
    public void WriteTo(Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, SerializedLength);

        //An absent field's bytes are zero by construction, and the reader enforces that, so the layout is
        //cleared once rather than each absent field being zeroed on its own path.
        Span<byte> layout = destination[..SerializedLength];
        layout.Clear();
        layout[VersionOffset] = LayoutVersion;

        byte flags = 0;
        if(IdentityClaimConfirmed)
        {
            flags |= IdentityClaimConfirmedFlag;
        }

        if(ConfirmedCausalityDigest is { } causalityDigest)
        {
            flags |= CausalityDigestPresentFlag;
            BinaryPrimitives.WriteUInt64LittleEndian(layout[CausalityDigestOffset..], causalityDigest.Value);
        }

        if(ConfirmedStateId is { } stateId)
        {
            flags |= StateIdPresentFlag;
            BinaryPrimitives.WriteUInt64LittleEndian(layout[StateIdOffset..], stateId.Value);
        }

        if(ConfirmedDictionaryEpoch is { } dictionaryEpoch)
        {
            flags |= DictionaryEpochPresentFlag;
            BinaryPrimitives.WriteInt64LittleEndian(layout[DictionaryEpochOffset..], dictionaryEpoch);
        }

        layout[FlagsOffset] = flags;
    }

    /// <summary>Reads a record written by <see cref="WriteTo"/>, refusing anything this layout version cannot account for.</summary>
    /// <param name="source">The bytes to read; at least <see cref="SerializedLength"/> bytes, of which exactly that many are read.</param>
    /// <returns>The record the bytes carry.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="source"/> is shorter than <see cref="SerializedLength"/>.</exception>
    /// <exception cref="InvalidDataException">The bytes carry an unknown layout version, a flag bit this version does not define, or a non-zero value in a field the flags call absent — each of which means the bytes were not written by this layout.</exception>
    public static ConfirmedMetadataFacts ReadFrom(ReadOnlySpan<byte> source)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(source.Length, SerializedLength);

        ReadOnlySpan<byte> layout = source[..SerializedLength];
        if(layout[VersionOffset] != LayoutVersion)
        {
            throw new InvalidDataException("The stored confirmed-facts record carries a layout version this build does not read; it is refused rather than read past, so a record written by another build is not overwritten on the strength of a misreading.");
        }

        byte flags = layout[FlagsOffset];
        if((flags & ~DefinedFlags) != 0)
        {
            throw new InvalidDataException("The stored confirmed-facts record sets a flag bit this layout version does not define, so the bytes were not written by this layout.");
        }

        ulong causalityDigest = BinaryPrimitives.ReadUInt64LittleEndian(layout[CausalityDigestOffset..]);
        ulong stateId = BinaryPrimitives.ReadUInt64LittleEndian(layout[StateIdOffset..]);
        long dictionaryEpoch = BinaryPrimitives.ReadInt64LittleEndian(layout[DictionaryEpochOffset..]);

        bool causalityDigestPresent = (flags & CausalityDigestPresentFlag) != 0;
        bool stateIdPresent = (flags & StateIdPresentFlag) != 0;
        bool dictionaryEpochPresent = (flags & DictionaryEpochPresentFlag) != 0;

        //A writer of this layout zeroes every absent field, so a non-zero absent field is the cheapest
        //evidence available here that the bytes came from elsewhere.
        if((!causalityDigestPresent && causalityDigest != 0)
            || (!stateIdPresent && stateId != 0)
            || (!dictionaryEpochPresent && dictionaryEpoch != 0))
        {
            throw new InvalidDataException("The stored confirmed-facts record carries a value in a field its flags call absent, so the bytes were not written by this layout.");
        }

        NodeIdentifier? confirmedCausalityDigest = causalityDigestPresent ? new NodeIdentifier(causalityDigest) : null;
        NodeIdentifier? confirmedStateId = stateIdPresent ? new NodeIdentifier(stateId) : null;
        long? confirmedDictionaryEpoch = dictionaryEpochPresent ? dictionaryEpoch : null;

        return new ConfirmedMetadataFacts(
            IdentityClaimConfirmed: (flags & IdentityClaimConfirmedFlag) != 0,
            ConfirmedCausalityDigest: confirmedCausalityDigest,
            ConfirmedStateId: confirmedStateId,
            ConfirmedDictionaryEpoch: confirmedDictionaryEpoch);
    }

    /// <summary>
    /// Saves this record durably beside the host's node state, under the store's own artifact name and the
    /// same stage-flush-rename atomicity: it does not return until the record is on stable storage, so a host
    /// that crashes after a save reopens knowing what it knew.
    /// </summary>
    /// <param name="store">The store whose directory the record is kept in.</param>
    /// <param name="cancellationToken">The token that cancels the write.</param>
    /// <returns>A task that completes once the record is durable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">The staged write, the flush, or the rename failed.</exception>
    [UnsupportedOSPlatform("browser")]
    public async ValueTask SaveAsync(MetadataNodeStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        using IMemoryOwner<byte> layoutOwner = store.Pool.Rent(SerializedLength);
        Memory<byte> layout = layoutOwner.Memory[..SerializedLength];
        WriteTo(layout.Span);

        await store.WriteConfirmedFactsAsync(layout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the record a previous run saved, or reports that this host has confirmed nothing yet. A missing
    /// record is a VALUE — <see langword="null"/>, which the caller reads as
    /// <see cref="Unconfirmed"/> and answers by consulting the plane — while a record that is present but
    /// unreadable is refused loudly by <see cref="ReadFrom"/>.
    /// </summary>
    /// <param name="store">The store whose directory the record is kept in.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The saved record, or <see langword="null"/> when none has been saved.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The stored record has a length its fixed layout cannot produce, ended early under the read, or carries a version, flag, or absent-field value this layout does not account for.</exception>
    [UnsupportedOSPlatform("browser")]
    public static async ValueTask<ConfirmedMetadataFacts?> TryLoadAsync(MetadataNodeStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        using IMemoryOwner<byte> layoutOwner = store.Pool.Rent(SerializedLength);
        Memory<byte> layout = layoutOwner.Memory[..SerializedLength];
        if(!await store.TryReadExactAsync(layout, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadFrom(layout.Span);
    }
}
