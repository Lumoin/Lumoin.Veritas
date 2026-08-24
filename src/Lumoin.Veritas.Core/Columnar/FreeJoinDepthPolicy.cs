namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The depth a join-route decision names for the Free Join route's relations.
/// </summary>
public enum FreeJoinDepthPolicy
{
    /// <summary>No depth was decided, so the engine's own per-relation rule applies — its join-cover depths, extended where a relation's key fan-out justifies hashing its private tail. The default.</summary>
    Unspecified = 0,

    /// <summary>Every relation builds at its join-cover depth, whatever its key fan-out.</summary>
    Cover = 1,

    /// <summary>Every relation builds through its private tail, whatever its key fan-out.</summary>
    Full = 2
}
