using System;
using System.Threading;

namespace Lumoin.Veritas.Tests;

/// <summary>
/// Assembly-level test-run configuration. The worker-thread floor removes the
/// thread-pool hill-climb window in which CPU-saturating test classes starve
/// the async continuations of latency-sensitive socket tests sharing the
/// process pool: with the floor at four times the processor count, a
/// federated round-trip's client and server continuations schedule
/// immediately instead of queueing behind the pool's gradual thread
/// injection while every worker sits in synchronous test code.
/// </summary>
[TestClass]
internal static class TestSuiteInitializer
{
    /// <summary>Applies the assembly-wide thread-pool floor before any test runs.</summary>
    /// <param name="context">The test context the runner supplies.</param>
    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ThreadPool.GetMinThreads(out int workerMinimum, out int completionPortMinimum);
        int floor = Math.Max(workerMinimum, Environment.ProcessorCount * 4);
        ThreadPool.SetMinThreads(floor, completionPortMinimum);
    }
}
