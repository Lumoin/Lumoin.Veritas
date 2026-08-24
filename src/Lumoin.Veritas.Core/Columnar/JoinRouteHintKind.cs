namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Which view-borne route a per-query hint names. The vocabulary is the join-route seam's own route set
/// plus the absent case, so a hint can only name a route the seam serves; an unservable hint on a shape
/// that route declines costs a fall-through and never an answer.
/// </summary>
public enum JoinRouteHintKind
{
    /// <summary>No route was hinted, so the force-or-selector decision stands. The default.</summary>
    None = 0,

    /// <summary>The Free Join generic join over generalized hash tries.</summary>
    FreeJoin = 1,

    /// <summary>The columnar batched scan-and-hash pipeline.</summary>
    Batched = 2,

    /// <summary>The columnar leapfrog driver.</summary>
    Leapfrog = 3
}
