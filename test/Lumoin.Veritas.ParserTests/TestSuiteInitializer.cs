using System;
using System.Threading;

namespace Lumoin.Veritas.ParserTests;

/// <summary>
/// Assembly-level test-run configuration. The worker-thread floor removes the
/// thread-pool hill-climb window in which CPU-saturating test classes starve
/// the async continuations of latency-sensitive socket tests sharing the
/// process pool: with the floor at four times the processor count, a
/// federated round-trip's client and server continuations schedule
/// immediately instead of queueing behind the pool's gradual thread
/// injection while every worker sits in synchronous test code.
/// </summary>
/// <remarks>
/// <para>
/// The starvation this prevents is exception-free and self-healing, which
/// makes it look like flakiness rather than a bug: a round-trip issued while
/// every pool worker is occupied stalls for the pool's climb-out time — the
/// stall scales with how far demand exceeds the floor — and surfaces only if
/// it outlives a transport timeout, as a client-side
/// <see cref="System.Threading.Tasks.TaskCanceledException"/> whose
/// cancellation token is NOT the caller's. Once the pool has grown, the same
/// process cannot reproduce it. The federation harness
/// (<c>Sparql/Federation/SparqlTestHostShell</c>) carries a transport
/// timeline that dumps to a <c>federation-trace-*.txt</c> beside the test
/// binary on exactly that signature, naming the stage a stalled round-trip
/// died in. The same hazard applies to any production host that serves
/// latency-sensitive I/O from a pool shared with long synchronous CPU work.
/// </para>
/// </remarks>
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
