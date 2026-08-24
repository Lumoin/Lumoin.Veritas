namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// The kind of transition a <see cref="JournalEntry"/> records.
/// Distinguishes initial builds from edit-session lifecycle
/// events (start, commit, abandon) and journal forks.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mutating vs non-mutating entries.</b>
/// <see cref="Initial"/> and <see cref="Committed"/> entries
/// move the journal head — their <c>ChildId</c> is the new
/// snapshot identifier, distinct from <c>ParentId</c>.
/// <see cref="Started"/> and <see cref="Abandoned"/> entries
/// are non-mutating: they record session lifecycle events
/// without producing a new snapshot, so their <c>ChildId</c>
/// equals their <c>ParentId</c> and the journal head is
/// unchanged after the append. A <see cref="Forked"/> entry is
/// the one exception to that equivalence: its <c>ChildId</c>
/// equals its <c>ParentId</c>, yet the append MOVES the head —
/// from the empty sentinel to the fork point — because it is
/// appended with the empty sentinel, not <c>ParentId</c>, as the
/// expected head. The head after any successful append is always
/// the appended entry's <c>ChildId</c>.
/// </para>
/// <para>
/// <b>EditCommitment population.</b>
/// <see cref="Initial"/> and <see cref="Committed"/> entries
/// carry a non-null <c>EditCommitment</c> — the content-addressed
/// fingerprint of the edits that produced the new snapshot.
/// <see cref="Started"/> and <see cref="Abandoned"/> entries
/// leave it null; there are no committed edits to fingerprint.
/// </para>
/// <para>
/// <b>SessionId population.</b>
/// <see cref="Started"/>, <see cref="Committed"/>, and
/// <see cref="Abandoned"/> entries carry a non-null
/// <c>SessionId</c> identifying the originating edit session.
/// <see cref="Initial"/> and <see cref="Forked"/> entries leave
/// it null — neither originates from an edit session.
/// </para>
/// <para>
/// <b>Fork entries.</b> A <see cref="Forked"/> entry is the
/// cross-journal edge that makes a set of linear logs a DAG: it
/// is only ever a journal's FIRST entry (appended with the
/// empty-head sentinel as the expected head), and its
/// <c>ParentId</c> and <c>ChildId</c> both name the fork-point
/// state — a state produced by the SOURCE journal, not by this
/// one. The append moves the new journal's head from the
/// empty sentinel to the fork point without changing any
/// content; replay of the forked journal starts from the state
/// the source journal's replay reaches at that identifier.
/// </para>
/// </remarks>
public enum EditSessionEntryKind
{
    /// <summary>
    /// An initial build of the graph from a sequence of triples.
    /// <c>ParentId</c> is <see cref="Storage.NodeIdentifier.Empty"/>;
    /// <c>ChildId</c> is the new root identifier; <c>SessionId</c>
    /// is null.
    /// </summary>
    Initial,

    /// <summary>
    /// An edit session was opened. Non-mutating: <c>ChildId</c>
    /// equals <c>ParentId</c>, the journal head is unchanged.
    /// <c>SessionId</c> identifies the opened session.
    /// </summary>
    Started,

    /// <summary>
    /// An edit session committed. <c>ChildId</c> is the new
    /// snapshot identifier produced by the session's edits;
    /// <c>EditCommitment</c> is the content-addressed fingerprint
    /// of those edits; <c>SessionId</c> identifies the committing
    /// session.
    /// </summary>
    Committed,

    /// <summary>
    /// An edit session was disposed without committing.
    /// Non-mutating: <c>ChildId</c> equals <c>ParentId</c>, the
    /// journal head is unchanged. <c>SessionId</c> identifies the
    /// abandoned session.
    /// </summary>
    Abandoned,

    /// <summary>
    /// The journal was forked from another journal's committed
    /// state. Only ever a journal's first entry: <c>ParentId</c>
    /// and <c>ChildId</c> both name the fork-point state (produced
    /// by the source journal), the expected head is the empty
    /// sentinel, and the append moves the head to the fork point.
    /// <c>SessionId</c> is null. Written by the dataset-level
    /// world fork; the per-store durable record format does not
    /// accept this kind.
    /// </summary>
    Forked
}
