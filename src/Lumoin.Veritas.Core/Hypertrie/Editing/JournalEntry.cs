using System;
using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// One transition in the linear journal — the canonical record
/// that snapshot <see cref="ChildId"/> was produced by applying
/// <see cref="Additions"/> and <see cref="Removals"/> to snapshot
/// <see cref="ParentId"/>, or that an edit session reached a
/// lifecycle event (started or abandoned) without changing the
/// snapshot.
/// </summary>
/// <param name="ParentId">The content-addressed identifier of the snapshot the entry branches from. <see cref="NodeIdentifier.Empty"/> for an <see cref="EditSessionEntryKind.Initial"/> build.</param>
/// <param name="ChildId">The content-addressed identifier of the snapshot produced by the entry. Equals <see cref="ParentId"/> for non-mutating <see cref="EditSessionEntryKind.Started"/> and <see cref="EditSessionEntryKind.Abandoned"/> entries.</param>
/// <param name="EntryKind">Which kind of transition this entry records — initial build, session start, session commit, session abandon.</param>
/// <param name="SessionId">The originating <see cref="EditSession"/>'s identifier; <c>null</c> for <see cref="EditSessionEntryKind.Initial"/> entries that predate any session.</param>
/// <param name="EditCommitment">The content-addressed fingerprint of the edits that produced <paramref name="ChildId"/>; <c>null</c> for non-mutating <see cref="EditSessionEntryKind.Started"/> and <see cref="EditSessionEntryKind.Abandoned"/> entries.</param>
/// <param name="Additions">The triples added to <paramref name="ParentId"/> to produce <paramref name="ChildId"/>. Empty for non-mutating entries; non-empty only for <see cref="EditSessionEntryKind.Initial"/> and <see cref="EditSessionEntryKind.Committed"/> entries with adds.</param>
/// <param name="Removals">The triples removed from <paramref name="ParentId"/> to produce <paramref name="ChildId"/>. Empty for non-mutating entries; non-empty only for <see cref="EditSessionEntryKind.Committed"/> entries with removes (initial builds only have additions).</param>
/// <param name="Timestamp">The wall-clock time at which the entry was appended. Assigned by the journal implementation on append; the caller's value is overwritten.</param>
/// <param name="SequenceNumber">The monotonic position of the entry in the journal. Assigned by the journal implementation on append; increases by one per appended entry. Starts at 0.</param>
/// <remarks>
/// <para>
/// Entries are immutable. Two entries with identical fields are
/// equal — record-struct semantics — but the journal itself does
/// not deduplicate; identical entries with different sequence
/// numbers represent distinct successive commits with the same
/// delta (e.g. a no-op rebase).
/// </para>
/// <para>
/// <b>Journal-owned fields.</b>
/// <see cref="SequenceNumber"/> and <see cref="Timestamp"/> are
/// assigned by the journal at append time. The caller fills them
/// with placeholder values when constructing an entry; the journal
/// overrides both before durably storing the entry. This contract
/// keeps clock and ordering authority with the journal — the only
/// point in the system that can serialise either correctly across
/// concurrent writers — and means caller code never has to read
/// the wall clock or invent a sequence number.
/// </para>
/// <para>
/// <b>EditCommitment vs Additions/Removals.</b>
/// <see cref="EditCommitment"/> is a content-addressed fingerprint
/// of the edits; <see cref="Additions"/> and <see cref="Removals"/>
/// are the literal triples. The fingerprint is for fast comparison
/// (idempotent retry detection, replay verification) and the
/// triple lists are for actual replay and audit. They must be
/// consistent: a journal whose <see cref="EditCommitment"/> does
/// not match a recomputation over its <see cref="ParentId"/>,
/// <see cref="Additions"/>, and <see cref="Removals"/> indicates
/// corruption.
/// </para>
/// <para>
/// <b>Empty initial build.</b> A build from no triples produces a
/// snapshot whose root identifier equals <see cref="NodeIdentifier.Empty"/>.
/// Such a build is recorded as a journal entry whose
/// <see cref="ParentId"/> and <see cref="ChildId"/> both equal
/// <see cref="NodeIdentifier.Empty"/>, whose <see cref="Additions"/>
/// and <see cref="Removals"/> are empty, and whose
/// <see cref="EditCommitment"/> equals <see cref="NodeIdentifier.Empty"/>
/// — no edits fold into the empty base, so the commitment
/// degenerates to the base. Replay reconstructs the empty
/// snapshot.
/// </para>
/// </remarks>
[DebuggerDisplay("JournalEntry {EntryKind} Seq={SequenceNumber} Parent={ParentId.Value:X16} Child={ChildId.Value:X16} +{Additions.Length} -{Removals.Length}")]
public readonly record struct JournalEntry(
    NodeIdentifier ParentId,
    NodeIdentifier ChildId,
    EditSessionEntryKind EntryKind,
    SessionId? SessionId,
    NodeIdentifier? EditCommitment,
    ImmutableArray<EncodedTriple> Additions,
    ImmutableArray<EncodedTriple> Removals,
    DateTimeOffset Timestamp,
    long SequenceNumber)
{
    /// <summary>
    /// Constructs an <see cref="EditSessionEntryKind.Initial"/>
    /// entry for an initial build of the given triple set.
    /// <see cref="SessionId"/> is null; <see cref="EditCommitment"/>
    /// is computed from <paramref name="additions"/> over the empty
    /// base using <paramref name="hash"/>. <see cref="Timestamp"/>
    /// and <see cref="SequenceNumber"/> are placeholders the
    /// journal will overwrite on append.
    /// </summary>
    /// <param name="hash">The hash function used for the commitment fingerprint. Must be the same hash function the originating <see cref="Storage.NodeStore"/> carries.</param>
    /// <param name="childId">The new snapshot identifier produced by the build.</param>
    /// <param name="additions">The full triple set the build indexed.</param>
    public static JournalEntry Initial(VeritasHash hash, NodeIdentifier childId, ImmutableArray<EncodedTriple> additions)
    {
        NodeIdentifier commitment = EditCommitmentHashing.Compute(
            hash,
            NodeIdentifier.Empty,
            additions,
            ImmutableArray<EncodedTriple>.Empty);

        return new JournalEntry(
            ParentId: NodeIdentifier.Empty,
            ChildId: childId,
            EntryKind: EditSessionEntryKind.Initial,
            SessionId: null,
            EditCommitment: commitment,
            Additions: additions,
            Removals: ImmutableArray<EncodedTriple>.Empty,
            Timestamp: default,
            SequenceNumber: 0);
    }

    /// <summary>
    /// Constructs an <see cref="EditSessionEntryKind.Started"/>
    /// entry recording that <paramref name="sessionId"/> was opened
    /// when the journal head was <paramref name="head"/>. Non-
    /// mutating: <c>ChildId</c> equals <paramref name="head"/>.
    /// </summary>
    /// <param name="head">The journal head observed at session open.</param>
    /// <param name="sessionId">The session being opened.</param>
    public static JournalEntry Started(NodeIdentifier head, SessionId sessionId)
    {
        return new JournalEntry(
            ParentId: head,
            ChildId: head,
            EntryKind: EditSessionEntryKind.Started,
            SessionId: sessionId,
            EditCommitment: null,
            Additions: ImmutableArray<EncodedTriple>.Empty,
            Removals: ImmutableArray<EncodedTriple>.Empty,
            Timestamp: default,
            SequenceNumber: 0);
    }

    /// <summary>
    /// Constructs an <see cref="EditSessionEntryKind.Committed"/>
    /// entry for a successful session commit.
    /// <see cref="EditCommitment"/> is computed from the supplied
    /// additions and removals over <paramref name="parentId"/>
    /// using <paramref name="hash"/>.
    /// </summary>
    /// <param name="hash">The hash function used for the commitment fingerprint. Must be the same hash function the originating <see cref="Storage.NodeStore"/> carries.</param>
    /// <param name="parentId">The base snapshot the session committed against.</param>
    /// <param name="childId">The new snapshot identifier produced by the commit.</param>
    /// <param name="sessionId">The committing session.</param>
    /// <param name="additions">The effective additions (already filtered: triples not in <paramref name="parentId"/>).</param>
    /// <param name="removals">The effective removals (already filtered: triples in <paramref name="parentId"/>).</param>
    public static JournalEntry Committed(
        VeritasHash hash,
        NodeIdentifier parentId,
        NodeIdentifier childId,
        SessionId sessionId,
        ImmutableArray<EncodedTriple> additions,
        ImmutableArray<EncodedTriple> removals)
    {
        NodeIdentifier commitment = EditCommitmentHashing.Compute(hash, parentId, additions, removals);

        return new JournalEntry(
            ParentId: parentId,
            ChildId: childId,
            EntryKind: EditSessionEntryKind.Committed,
            SessionId: sessionId,
            EditCommitment: commitment,
            Additions: additions,
            Removals: removals,
            Timestamp: default,
            SequenceNumber: 0);
    }

    /// <summary>
    /// Constructs an <see cref="EditSessionEntryKind.Abandoned"/>
    /// entry recording that <paramref name="sessionId"/> was
    /// disposed without committing. Non-mutating: <c>ChildId</c>
    /// equals <paramref name="head"/>.
    /// </summary>
    /// <param name="head">The journal head at the moment of abandon.</param>
    /// <param name="sessionId">The abandoned session.</param>
    public static JournalEntry Abandoned(NodeIdentifier head, SessionId sessionId)
    {
        return new JournalEntry(
            ParentId: head,
            ChildId: head,
            EntryKind: EditSessionEntryKind.Abandoned,
            SessionId: sessionId,
            EditCommitment: null,
            Additions: ImmutableArray<EncodedTriple>.Empty,
            Removals: ImmutableArray<EncodedTriple>.Empty,
            Timestamp: default,
            SequenceNumber: 0);
    }
}
