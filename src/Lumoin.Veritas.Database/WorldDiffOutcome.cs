namespace Lumoin.Veritas.Database;

/// <summary>
/// The outcome of diffing two named worlds on a mutable database. An unknown name is an expected
/// condition — surfaces race drops — so it answers as a value rather than an exception.
/// </summary>
public enum WorldDiffOutcome
{
    /// <summary>Both worlds were found and the transitions were computed.</summary>
    Diffed,

    /// <summary>At least one of the named worlds is not registered; nothing was computed.</summary>
    UnknownWorld
}
