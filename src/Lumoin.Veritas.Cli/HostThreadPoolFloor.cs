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
    /// <summary>The worker-thread minimum, as a multiple of <see cref="Environment.ProcessorCount"/>, the floor targets.</summary>
    private const int WorkerThreadsPerProcessor = 4;

    /// <summary>The absolute ceiling on the targeted worker-thread minimum, bounding the committed thread-stack memory on high-core hosts where an unbounded multiple would oversubscribe.</summary>
    private const int MaximumWorkerFloor = 256;

    /// <summary>
    /// Raises the pool's minimum worker-thread count toward four times the processor count, bounded by
    /// <see cref="MaximumWorkerFloor"/>, when it is currently lower, and otherwise leaves it untouched. The
    /// pool's maximum is first raised to admit the target when it would otherwise clamp it, so the minimum
    /// the runtime accepts equals the one requested. The completion-port minimum is read and written back
    /// unchanged. Returns the worker minimum the runtime reports afterwards — read back, not the requested
    /// value — so a caller observes what was actually applied rather than what was asked for.
    /// </summary>
    /// <returns>The worker-thread minimum in effect after the floor is applied.</returns>
    public static int Apply()
    {
        ThreadPool.GetMinThreads(out int workerMinimum, out int completionPortMinimum);
        int target = Math.Min(Environment.ProcessorCount * WorkerThreadsPerProcessor, MaximumWorkerFloor);
        int floor = Math.Max(workerMinimum, target);

        //SetMinThreads fails silently when the requested minimum exceeds the pool maximum; raise the
        //maximum to admit the floor first so the minimum the runtime accepts is the one requested.
        ThreadPool.GetMaxThreads(out int workerMaximum, out int completionPortMaximum);
        if(workerMaximum < floor)
        {
            ThreadPool.SetMaxThreads(floor, completionPortMaximum);
        }

        ThreadPool.SetMinThreads(floor, completionPortMinimum);

        ThreadPool.GetMinThreads(out int appliedWorkerMinimum, out _);

        return appliedWorkerMinimum;
    }
}
