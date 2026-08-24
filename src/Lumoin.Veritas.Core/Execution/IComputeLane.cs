using System;

namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// A named lane that drains an observable, bounded queue of work in
/// turns. The platform-pluggable seam of the execution model: a
/// threaded async-consumer implementation serves server, desktop, and
/// thread-capable mobile hosts, and a single-cooperative-consumer
/// implementation serves the browser, both behind this one interface so
/// callers admit work without knowing the host. Admission is an explicit
/// <see cref="ComputeAdmission"/> verdict, so overload sheds with a
/// reason rather than starving the serve path invisibly.
/// </summary>
public interface IComputeLane: IAsyncDisposable
{
    /// <summary>The current worker count — the lane width, which moves on a quota re-derivation. One on a single-cooperative-thread host.</summary>
    int WorkerCount { get; }

    /// <summary>The total queued work across all classes — the backpressure signal.</summary>
    int QueueDepth { get; }

    /// <summary>The count of completed turns across all classes.</summary>
    long TurnsCompleted { get; }

    /// <summary>The count of shed admissions — the load-shedding signal.</summary>
    long ShedCount { get; }

    /// <summary>The queued depth of one priority class — the per-class backpressure signal.</summary>
    /// <param name="workClass">The class to read.</param>
    /// <returns>The queued depth of that class.</returns>
    int QueueDepthOf(ComputeWorkClass workClass);

    /// <summary>
    /// Offers work to the lane. The work is queued and run as a turn, or
    /// shed with a verdict when the bounded queue is full or the lane is
    /// stopping. The caller observes the verdict and decides how to react.
    /// </summary>
    /// <param name="workClass">The work's priority class.</param>
    /// <param name="work">The turn body.</param>
    /// <returns>The admission verdict.</returns>
    ComputeAdmission Admit(ComputeWorkClass workClass, ComputeWorkDelegate work);
}
