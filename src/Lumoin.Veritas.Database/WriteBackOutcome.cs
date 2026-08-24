namespace Lumoin.Veritas.Database;

/// <summary>
/// The value-based outcome of a reconcile write-back: whether the recovered delta was committed, was empty, or
/// could not commit within the retry budget.
/// </summary>
public enum WriteBackOutcome
{
    /// <summary>The recovered delta was applied and committed through the dataset journal.</summary>
    Committed,

    /// <summary>The recovered delta was empty, so nothing was applied.</summary>
    NoOp,

    /// <summary>A concurrent committer kept advancing the journal head past the bounded retry budget, so the delta was not applied; a later reconcile round re-detects and retries.</summary>
    ConflictExhausted,
}
