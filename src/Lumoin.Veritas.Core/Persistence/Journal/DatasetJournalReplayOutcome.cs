namespace Lumoin.Veritas.Core.Persistence.Journal;

/// <summary>
/// How a <see cref="DatasetJournalRecovery.ReplayAsync"/> resolved a durable dataset journal against the
/// generation the store recovered.
/// </summary>
public enum DatasetJournalReplayOutcome
{
    /// <summary>
    /// The generation anchor was found in the journal and no content-bearing entry follows it: the recovered
    /// generation already names the journal's head state, so there is nothing to replay and the loaded content
    /// serves as-is.
    /// </summary>
    UpToDate,

    /// <summary>
    /// Content-bearing entries were folded — either the whole self-contained log from empty (no anchor), or the
    /// suffix after the anchor — producing the head state's per-graph content the caller resumes over.
    /// </summary>
    Replayed,

    /// <summary>
    /// The generation anchor was not found in the journal: the store and the journal come from different
    /// histories. The caller refuses to serve; the result carries the anchor and the journal head that
    /// disagreed.
    /// </summary>
    Diverged
}
