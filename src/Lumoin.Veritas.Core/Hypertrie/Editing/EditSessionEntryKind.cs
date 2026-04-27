namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// The kind of transition a <see cref="JournalEntry"/> records.
/// Distinguishes initial builds from edit-session lifecycle
/// events (start, commit, abandon).
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
/// unchanged after the append.
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
/// <see cref="Initial"/> entries leave it null — initial builds
/// predate any session.
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
    Abandoned
}
