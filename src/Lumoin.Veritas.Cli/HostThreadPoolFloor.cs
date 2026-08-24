using System;
using System.Threading;

namespace Lumoin.Veritas.Cli;

/// <summary>
/// Raises the runtime worker-thread pool's minimum toward a floor proportional to the
/// processor count, so the latency-sensitive response continuations of the HTTP host are
/// far less likely to queue behind the pool's gradual thread injection while every worker
/// sits in synchronous CPU work. Applied once at endpoint startup. The floor is a
/// transitional measure; the dedicated compute lane that moves synchronous CPU work off the
/// I/O pool is the durable answer to that pressure.
/// </summary>
internal static class HostThreadPoolFloor
{
    /// <summary>The absolute ceiling on the targeted worker-thread minimum, bounding the committed thread-stack memory on high-core hosts where an unbounded multiple would oversubscribe.</summary>
    private const int MaximumWorkerFloor = 256;

    /// <summary>
    /// Raises the pool's minimum worker-thread count toward
    /// <paramref name="workerThreadsPerProcessor"/> times the processor count, bounded by
    /// <see cref="MaximumWorkerFloor"/>, when it is currently lower, and otherwise leaves it untouched. A
    /// multiplier of zero or less lifts the floor entirely — the runtime's own minimum stands, which is the
    /// state the lane model targets once it has moved synchronous CPU work off the I/O pool. The
    /// pool maximum is never changed: a deliberately low operator ceiling is honoured, so on a host whose
    /// maximum sits below the target the minimum is floored only up to that ceiling — the pool saturates at
    /// the operator's maximum rather than silently overriding it. Because the requested minimum is clamped
    /// to the existing maximum it is always admissible, so the completion-port minimum is read and written
    /// back unchanged and the request applies. Returns the worker minimum the runtime reports afterwards —
    /// read back, not the requested value — so a caller observes what was actually applied.
    /// </summary>
    /// <param name="workerThreadsPerProcessor">The worker-thread minimum the floor targets, as a multiple of the processor count; zero or less lifts the floor.</param>
    /// <returns>The worker-thread minimum in effect after the floor is applied.</returns>
    public static int Apply(int workerThreadsPerProcessor)
    {
        ThreadPool.GetMinThreads(out int workerMinimum, out int completionPortMinimum);

        if(workerThreadsPerProcessor <= 0)
        {
            return workerMinimum;
        }

        ThreadPool.GetMaxThreads(out int workerMaximum, out _);
        int target = Math.Min(Environment.ProcessorCount * workerThreadsPerProcessor, MaximumWorkerFloor);
        int floor = Math.Min(Math.Max(workerMinimum, target), workerMaximum);

        ThreadPool.SetMinThreads(floor, completionPortMinimum);

        ThreadPool.GetMinThreads(out int appliedWorkerMinimum, out _);

        return appliedWorkerMinimum;
    }
}
