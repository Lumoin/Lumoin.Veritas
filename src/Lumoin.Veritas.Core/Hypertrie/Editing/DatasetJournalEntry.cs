using System;
using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// One transition in the linear DATASET journal — the canonical
/// record that dataset state <see cref="ChildId"/> was produced by
/// applying <see cref="Transitions"/> (one per touched graph,
/// atomically) to dataset state <see cref="ParentId"/>, or that a
/// dataset edit session reached a lifecycle event without changing
/// the state.
/// </summary>
/// <param name="ParentId">The dataset state identifier the entry branches from (<see cref="DatasetStateHashing.ComputeStateId"/>). <see cref="NodeIdentifier.Empty"/> for an <see cref="EditSessionEntryKind.Initial"/> build — the journal's empty-head sentinel, which no real state identifier equals. For an <see cref="EditSessionEntryKind.Forked"/> entry it names a state produced by the SOURCE journal — the cross-journal DAG edge.</param>
/// <param name="ChildId">The dataset state identifier the entry produces. Equals <see cref="ParentId"/> for non-mutating <see cref="EditSessionEntryKind.Started"/>, <see cref="EditSessionEntryKind.Abandoned"/>, and <see cref="EditSessionEntryKind.Forked"/> entries.</param>
/// <param name="EntryKind">Which kind of transition this entry records — initial build, session start, session commit, session abandon, or a fork from another journal's state.</param>
/// <param name="SessionId">The originating dataset edit session's identifier; <c>null</c> for <see cref="EditSessionEntryKind.Initial"/> entries that predate any session.</param>
/// <param name="EditCommitment">The content-addressed fingerprint of the transitions (<see cref="DatasetStateHashing.ComputeCommitment"/>); <c>null</c> for non-mutating entries.</param>
/// <param name="Transitions">The per-graph transitions the entry commits, one per touched graph. Empty for non-mutating entries. The atomic unit: replay applies all of them or none.</param>
/// <param name="Timestamp">The wall-clock time at which the entry was appended. Assigned by the journal implementation on append; the caller's value is overwritten.</param>
/// <param name="SequenceNumber">The monotonic position of the entry in the journal. Assigned by the journal implementation on append.</param>
/// <param name="Causality">The commit's causality annotation under the dotted observed-remove regime — the dots its additions mint or adopt, the dots its removals drop, and (reconcile write-backs only) the folded peer context — or <see langword="null"/> on a store that is not remove-aware, on a non-mutating entry, and on a commit that moves no default-graph content. Replay READS the recorded dots; it never re-derives them. A <see cref="EditSessionEntryKind.Committed"/> entry may carry a non-<see langword="null"/> annotation with EMPTY transitions and <see cref="ChildId"/> equal to <see cref="ParentId"/>: a causality-only commit (a terminal peer-context fold, or a dot union onto an already-present triple) changes causal knowledge without changing the committed triple set.</param>
/// <remarks>
/// <para>
/// This is the dataset-level instance of the journal consensus
/// seam: the same append/read OCC contract as the per-store
/// <see cref="JournalEntry"/> log, with the head ranging over
/// DATASET state identifiers so one entry can move several graphs
/// at once — a SPARQL Update request touching many graphs commits
/// as one atomic, linearisable transition.
/// </para>
/// <para>
/// <b>Journal-owned fields.</b> <see cref="SequenceNumber"/> and
/// <see cref="Timestamp"/> are assigned by the journal at append
/// time; caller-supplied values are placeholders.
/// </para>
/// </remarks>
[DebuggerDisplay("DatasetJournalEntry {EntryKind} Seq={SequenceNumber} Parent={ParentId.Value:X16} Child={ChildId.Value:X16} Graphs={Transitions.Length}")]
public readonly record struct DatasetJournalEntry(
    NodeIdentifier ParentId,
    NodeIdentifier ChildId,
    EditSessionEntryKind EntryKind,
    SessionId? SessionId,
    NodeIdentifier? EditCommitment,
    ImmutableArray<DatasetGraphTransition> Transitions,
    DateTimeOffset Timestamp,
    long SequenceNumber,
    CommitCausality? Causality = null)
{
    /// <summary>
    /// Constructs an <see cref="EditSessionEntryKind.Initial"/>
    /// entry for the initial build of a dataset.
    /// <see cref="SessionId"/> is null; <see cref="EditCommitment"/>
    /// is computed from <paramref name="transitions"/> over the
    /// empty parent using <paramref name="hash"/>.
    /// </summary>
    /// <param name="hash">The hash function used for the commitment fingerprint. Must be the same hash function the dataset's arena carries.</param>
    /// <param name="childId">The dataset state identifier produced by the build.</param>
    /// <param name="transitions">One creating transition per built graph (the default graph's under <see cref="Lumoin.Veritas.Core.Encoding.TermId.None"/>).</param>
    /// <param name="causality">The build's baseline causality annotation when the store is created with a host identity — the Initial entry IS its baseline, dotting every seed triple on the supplied axis — or <see langword="null"/> for a store created without identity (add-only until an explicit baseline step).</param>
    /// <returns>The entry, with placeholder journal-owned fields.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hash"/> is <c>null</c>.</exception>
    public static DatasetJournalEntry Initial(VeritasHash hash, NodeIdentifier childId, ImmutableArray<DatasetGraphTransition> transitions, CommitCausality? causality = null)
    {
        ArgumentNullException.ThrowIfNull(hash);

        return new DatasetJournalEntry(
            ParentId: NodeIdentifier.Empty,
            ChildId: childId,
            EntryKind: EditSessionEntryKind.Initial,
            SessionId: null,
            EditCommitment: DatasetStateHashing.ComputeCommitment(hash, NodeIdentifier.Empty, transitions),
            Transitions: transitions,
            Timestamp: default,
            SequenceNumber: 0,
            Causality: causality);
    }

    /// <summary>
    /// Constructs an <see cref="EditSessionEntryKind.Started"/>
    /// entry recording that <paramref name="sessionId"/> was opened
    /// when the journal head was <paramref name="head"/>.
    /// Non-mutating: <see cref="ChildId"/> equals <paramref name="head"/>.
    /// </summary>
    /// <param name="head">The journal head observed at session open.</param>
    /// <param name="sessionId">The session being opened.</param>
    /// <returns>The entry, with placeholder journal-owned fields.</returns>
    public static DatasetJournalEntry Started(NodeIdentifier head, SessionId sessionId)
    {
        return new DatasetJournalEntry(
            ParentId: head,
            ChildId: head,
            EntryKind: EditSessionEntryKind.Started,
            SessionId: sessionId,
            EditCommitment: null,
            Transitions: [],
            Timestamp: default,
            SequenceNumber: 0);
    }

    /// <summary>
    /// Constructs an <see cref="EditSessionEntryKind.Committed"/>
    /// entry for a successful dataset session commit.
    /// <see cref="EditCommitment"/> is computed from the supplied
    /// transitions over <paramref name="parentId"/> using
    /// <paramref name="hash"/>.
    /// </summary>
    /// <param name="hash">The hash function used for the commitment fingerprint. Must be the same hash function the dataset's arena carries.</param>
    /// <param name="parentId">The dataset state the session committed against.</param>
    /// <param name="childId">The dataset state identifier produced by the commit.</param>
    /// <param name="sessionId">The committing session.</param>
    /// <param name="transitions">The per-graph transitions, deltas effective against the parent state. Empty on a causality-only commit, whose <paramref name="childId"/> equals <paramref name="parentId"/>.</param>
    /// <param name="causality">The commit's causality annotation on a remove-aware store, or <see langword="null"/> when the store is not remove-aware or the commit moves no default-graph content.</param>
    /// <returns>The entry, with placeholder journal-owned fields.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hash"/> is <c>null</c>.</exception>
    public static DatasetJournalEntry Committed(
        VeritasHash hash,
        NodeIdentifier parentId,
        NodeIdentifier childId,
        SessionId sessionId,
        ImmutableArray<DatasetGraphTransition> transitions,
        CommitCausality? causality = null)
    {
        ArgumentNullException.ThrowIfNull(hash);

        return new DatasetJournalEntry(
            ParentId: parentId,
            ChildId: childId,
            EntryKind: EditSessionEntryKind.Committed,
            SessionId: sessionId,
            EditCommitment: DatasetStateHashing.ComputeCommitment(hash, parentId, transitions),
            Transitions: transitions,
            Timestamp: default,
            SequenceNumber: 0,
            Causality: causality);
    }

    /// <summary>
    /// Constructs an <see cref="EditSessionEntryKind.Abandoned"/>
    /// entry recording that <paramref name="sessionId"/> was
    /// disposed without committing. Non-mutating:
    /// <see cref="ChildId"/> equals <paramref name="head"/>.
    /// </summary>
    /// <param name="head">The journal head at the moment of abandon.</param>
    /// <param name="sessionId">The abandoned session.</param>
    /// <returns>The entry, with placeholder journal-owned fields.</returns>
    public static DatasetJournalEntry Abandoned(NodeIdentifier head, SessionId sessionId)
    {
        return new DatasetJournalEntry(
            ParentId: head,
            ChildId: head,
            EntryKind: EditSessionEntryKind.Abandoned,
            SessionId: sessionId,
            EditCommitment: null,
            Transitions: [],
            Timestamp: default,
            SequenceNumber: 0);
    }

    /// <summary>
    /// Constructs an <see cref="EditSessionEntryKind.Forked"/>
    /// entry recording that this journal was forked from another
    /// journal's committed state. Only valid as a journal's first
    /// entry: the caller appends it with
    /// <see cref="NodeIdentifier.Empty"/> as the expected head, so
    /// the append succeeds exactly when the journal is unborn — no
    /// real dataset state identifier equals the empty sentinel.
    /// <see cref="ParentId"/> and <see cref="ChildId"/> both name
    /// the fork-point state, which the SOURCE journal produced.
    /// </summary>
    /// <param name="forkPoint">The dataset state identifier the fork branches from.</param>
    /// <returns>The entry, with placeholder journal-owned fields.</returns>
    public static DatasetJournalEntry Forked(NodeIdentifier forkPoint)
    {
        return new DatasetJournalEntry(
            ParentId: forkPoint,
            ChildId: forkPoint,
            EntryKind: EditSessionEntryKind.Forked,
            SessionId: null,
            EditCommitment: null,
            Transitions: [],
            Timestamp: default,
            SequenceNumber: 0);
    }
}
