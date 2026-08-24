namespace Lumoin.Veritas.Database;

/// <summary>
/// The well-known world names of a mutable database's world registry.
/// </summary>
public static class WellKnownWorlds
{
    /// <summary>
    /// The primary world's name: the dataset a mutable open seeds, the durable journal rides, and every
    /// world-scoped operation defaults to when no world is named. The primary world cannot be dropped.
    /// </summary>
    public const string Primary = "main";
}
