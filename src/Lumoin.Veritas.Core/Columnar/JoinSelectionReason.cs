namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Why a built-in join-route rule decided as it did. A deployment-supplied selector names itself through
/// <see cref="JoinStrategySelectorKind"/> and leaves this <see cref="Unspecified"/>.
/// </summary>
public enum JoinSelectionReason
{
    /// <summary>No rationale was stated — the default, and what a deployment-supplied selector carries unless it names one.</summary>
    Unspecified = 0,

    /// <summary>The shape's core is cyclic (the GYO reduction does not clear it) on a connected shape, where the batched route declines and the generic join is the measured winner.</summary>
    CyclicCore = 1,

    /// <summary>The shape has two or more connected components — a cartesian answer — where the batched route declines and the leapfrog driver has no shared variable to seek on.</summary>
    DisconnectedComponents = 2,

    /// <summary>The shape is acyclic and connected, the batched scan-and-hash pipeline's measured home.</summary>
    AcyclicBatched = 3,

    /// <summary>No engagement applied; the route is the always-sound columnar leapfrog driver.</summary>
    SoundDefault = 4,

    /// <summary>An explicit policy force decided the route.</summary>
    PolicyForced = 5,

    /// <summary>A per-query hint named the route; the selector was not consulted for it.</summary>
    HintedRoute = 6
}
