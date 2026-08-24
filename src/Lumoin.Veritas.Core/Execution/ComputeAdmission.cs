namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// The verdict the compute lane returns when work is offered to it.
/// Admission is an explicit, observable decision — overload sheds with
/// a named reason rather than blocking the caller or silently dropping
/// the work — so backpressure is a value the producer reacts to, never
/// invisible pool starvation.
/// </summary>
public enum ComputeAdmission
{
    /// <summary>The work was accepted and will run as a turn.</summary>
    Admitted,

    /// <summary>The work was shed because the lane's bounded queue is at capacity for its shed-able classes. The producer decides how to react — retry later, run inline, or report backpressure.</summary>
    ShedQueueFull,

    /// <summary>The work was shed because the lane is stopping or stopped and no longer admits work.</summary>
    ShedLaneStopped,
}
