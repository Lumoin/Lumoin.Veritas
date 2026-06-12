using System;
using System.Threading;

namespace Lumoin.Veritas.Cli;

/// <summary>
/// Raises the runtime worker-thread pool's minimum to a floor proportional to the
/// processor count, so the latency-sensitive response continuations of the HTTP host
/// schedule immediately instead of queueing behind the pool's gradual thread injection
/// while every worker sits in synchronous CPU work. Applied once at endpoint startup.
/// </summary>
internal static class HostThreadPoolFloor
{
    /// <summary>The worker-thread minimum, as a multiple of <see cref="Environment.ProcessorCount"/>, the floor raises the pool to.</summary>
    private const int WorkerThreadsPerProcessor = 4;

    /// <summary>
    /// Raises the pool's minimum worker-thread count to four times the processor count when it
    /// is currently lower, and otherwise leaves it untouched. The completion-port minimum is
    /// read and written back unchanged. Returns the worker minimum in effect afterwards.
    /// </summary>
    /// <returns>The worker-thread minimum after the floor is applied.</returns>
    public static int Apply()
    {
        ThreadPool.GetMinThreads(out int workerMinimum, out int completionPortMinimum);
        int floor = Math.Max(workerMinimum, Environment.ProcessorCount * WorkerThreadsPerProcessor);
        ThreadPool.SetMinThreads(floor, completionPortMinimum);

        return floor;
    }
}
