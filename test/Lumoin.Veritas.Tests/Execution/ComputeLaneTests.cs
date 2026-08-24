using System;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Execution;
using Microsoft.Extensions.Time.Testing;

namespace Lumoin.Veritas.Tests.Execution;

/// <summary>
/// The compute lane's behaviour across both platform implementations:
/// admitted work runs as a turn, a full bounded queue sheds with a
/// verdict, the threaded lane's live width matches the resolved plan and
/// re-derives on a control-plane tick, disposal drains gracefully, the
/// cooperative (web) lane runs turns on its single pump, the platform
/// factory realizes the resolved width on the actual host, and a
/// faulting turn does not tear down its worker. Coordination is by events
/// bounded only by the test cancellation token — no sleeps, no deadlines —
/// so the assertions are deterministic.
/// </summary>
[TestClass]
internal sealed class ComputeLaneTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Builds a non-browser environment snapshot with no CPU quota and an inconclusive protection probe.</summary>
    /// <param name="processorCount">The logical processor count.</param>
    /// <returns>The environment snapshot.</returns>
    private static ExecutionEnvironment Env(int processorCount)
    {
        return new ExecutionEnvironment(processorCount, null, false, null);
    }

    /// <summary>A mutable environment source the control-plane tick re-reads, so a quota change is exercisable.</summary>
    private sealed class EnvironmentSource
    {
        /// <summary>The snapshot the next observation returns.</summary>
        public ExecutionEnvironment Current { get; set; }

        /// <summary>Returns the current snapshot — bound as the lane's observation seam.</summary>
        /// <returns>The current snapshot.</returns>
        public ExecutionEnvironment Observe()
        {
            return Current;
        }
    }

    [TestMethod]
    public async Task ThreadedLaneRunsAdmittedWorkAsATurn()
    {
        FakeTimeProvider time = new();
        TaskCompletionSource ran = new(TaskCreationOptions.RunContinuationsAsynchronously);

        ThreadedComputeLane lane = new(ExecutionPolicy.Default, () => Env(4), time);
        await using var laneScope = lane.ConfigureAwait(false);

        ComputeAdmission admission = lane.Admit(ComputeWorkClass.ViewBuild, _ =>
        {
            ran.TrySetResult();

            return ValueTask.CompletedTask;
        });

        Assert.AreEqual(ComputeAdmission.Admitted, admission);

        //Await the turn's completion signal, bounded only by the test's cancellation token: this asserts
        //the turn ran (correctness), not that it ran inside an arbitrary wall-clock window (latency).
        await ran.Task.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ThreadedLaneWidthMatchesTheResolvedPlan()
    {
        FakeTimeProvider time = new();

        //An eight-core budget leaves one core of serve headroom: seven workers.
        ThreadedComputeLane lane = new(ExecutionPolicy.Default, () => Env(8), time);
        await using var laneScope = lane.ConfigureAwait(false);

        Assert.AreEqual(7, lane.WorkerCount);
    }

    [TestMethod]
    public async Task ThePlatformFactoryRealizesTheResolvedWidthOnThisHost()
    {
        //The produced resource model matches the actual platform: the lane the factory builds for
        //this host runs exactly the worker count the plan resolves for that same host.
        ResolvedExecutionPlan plan = ExecutionPolicy.Default.Resolve(ExecutionEnvironment.Observe());

        IComputeLane lane = ComputeLane.ForCurrentPlatform(ExecutionPolicy.Default);
        await using var laneScope = lane.ConfigureAwait(false);

        //Both lane implementations realize their worker count synchronously before the
        //factory returns, so the width is directly assertable — nothing to wait for.
        Assert.AreEqual(plan.ComputeLaneWorkers, lane.WorkerCount);
    }

    [TestMethod]
    public async Task AFullBoundedQueueShedsWithAVerdict()
    {
        FakeTimeProvider time = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new();

        //One worker, queue capacity two: occupy the worker, fill the queue, then the next sheds.
        ExecutionPolicy policy = ExecutionPolicy.Default with { ComputeLaneWorkers = 1, ComputeQueueCapacity = 2 };
        ThreadedComputeLane lane = new(policy, () => Env(8), time);
        await using var laneScope = lane.ConfigureAwait(false);

        lane.Admit(ComputeWorkClass.ViewBuild, async _ =>
        {
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
        });

        await started.Task.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ComputeAdmission.Admitted, lane.Admit(ComputeWorkClass.ViewBuild, Nothing));
        Assert.AreEqual(ComputeAdmission.Admitted, lane.Admit(ComputeWorkClass.ViewBuild, Nothing));

        //The queue is full at capacity two while the worker is busy: the third sheds.
        Assert.AreEqual(ComputeAdmission.ShedQueueFull, lane.Admit(ComputeWorkClass.ViewBuild, Nothing));
        Assert.AreEqual(1, lane.ShedCount);

        release.SetResult();
    }

    [TestMethod]
    public async Task ControlPlaneTickGrowsTheLaneWhenTheBudgetRises()
    {
        FakeTimeProvider time = new();
        EnvironmentSource environment = new() { Current = Env(8) };
        ThreadedComputeLane lane = new(ExecutionPolicy.Default, environment.Observe, time);
        await using var laneScope = lane.ConfigureAwait(false);

        Assert.AreEqual(7, lane.WorkerCount);

        //A risen budget grows the lane synchronously on the tick.
        environment.Current = Env(16);
        lane.RunControlPlaneTickOnce();

        Assert.AreEqual(15, lane.WorkerCount);
    }

    [TestMethod]
    public async Task ControlPlaneTickShrinksTheLaneWhenTheBudgetFalls()
    {
        FakeTimeProvider time = new();
        EnvironmentSource environment = new() { Current = Env(8) };
        ThreadedComputeLane lane = new(ExecutionPolicy.Default, environment.Observe, time);
        await using var laneScope = lane.ConfigureAwait(false);

        Assert.AreEqual(7, lane.WorkerCount);

        //A fallen budget shrinks by attrition at turn boundaries.
        environment.Current = Env(4);
        lane.RunControlPlaneTickOnce();

        await WaitForWorkerCountAsync(lane, 3, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(3, lane.WorkerCount);
    }

    [TestMethod]
    public async Task TheTimerDrivesTheControlPlaneTick()
    {
        FakeTimeProvider time = new();
        EnvironmentSource environment = new() { Current = Env(8) };
        ThreadedComputeLane lane = new(ExecutionPolicy.Default, environment.Observe, time);
        await using var laneScope = lane.ConfigureAwait(false);

        Assert.AreEqual(7, lane.WorkerCount);

        //Advancing past the tick cadence fires the periodic re-read with no manual tick call.
        environment.Current = Env(4);
        time.Advance(TimeSpan.FromSeconds(5));

        await WaitForWorkerCountAsync(lane, 3, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(3, lane.WorkerCount);
    }

    [TestMethod]
    public async Task DisposeDrainsQueuedTurnsThenStops()
    {
        FakeTimeProvider time = new();
        int completed = 0;

        ExecutionPolicy policy = ExecutionPolicy.Default with { ComputeLaneWorkers = 1, ComputeQueueCapacity = 16 };
        ThreadedComputeLane lane = new(policy, () => Env(8), time);
        for(int i = 0; i < 8; i++)
        {
            lane.Admit(ComputeWorkClass.BulkSort, _ =>
            {
                Interlocked.Increment(ref completed);

                return ValueTask.CompletedTask;
            });
        }

        //Graceful disposal drains every admitted turn before the consumer exits.
        await lane.DisposeAsync().ConfigureAwait(false);

        Assert.AreEqual(8, Volatile.Read(ref completed));
    }

    [TestMethod]
    public async Task AStoppedLaneShedsWithTheStoppedVerdict()
    {
        FakeTimeProvider time = new();
        ThreadedComputeLane lane = new(ExecutionPolicy.Default, () => Env(4), time);
        await lane.DisposeAsync().ConfigureAwait(false);

        Assert.AreEqual(ComputeAdmission.ShedLaneStopped, lane.Admit(ComputeWorkClass.ViewBuild, Nothing));
    }

    [TestMethod]
    public async Task AFaultingTurnDoesNotTearDownItsWorker()
    {
        FakeTimeProvider time = new();
        TaskCompletionSource ran = new(TaskCreationOptions.RunContinuationsAsynchronously);

        ExecutionPolicy policy = ExecutionPolicy.Default with { ComputeLaneWorkers = 1, ComputeQueueCapacity = 16 };
        ThreadedComputeLane lane = new(policy, () => Env(8), time);
        await using var laneScope = lane.ConfigureAwait(false);

        lane.Admit(ComputeWorkClass.Reasoning, _ => throw new InvalidOperationException("turn fault"));
        lane.Admit(ComputeWorkClass.Reasoning, _ =>
        {
            ran.TrySetResult();

            return ValueTask.CompletedTask;
        });

        //The consumer survived the faulting turn and ran the next one.
        await ran.Task.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task CooperativeLaneRunsTurnsOnItsSinglePump()
    {
        TaskCompletionSource ran = new(TaskCreationOptions.RunContinuationsAsynchronously);

        CooperativeComputeLane lane = new(ExecutionPolicy.Default, () => Env(8), TimeProvider.System);
        await using var laneScope = lane.ConfigureAwait(false);

        //The single-cooperative-thread lane is always width one.
        Assert.AreEqual(1, lane.WorkerCount);

        ComputeAdmission admission = lane.Admit(ComputeWorkClass.ViewBuild, _ =>
        {
            ran.TrySetResult();

            return ValueTask.CompletedTask;
        });

        Assert.AreEqual(ComputeAdmission.Admitted, admission);
        await ran.Task.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task CooperativeLaneShedsAFullQueueWithAVerdict()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new();

        ExecutionPolicy policy = ExecutionPolicy.Default with { ComputeQueueCapacity = 2 };
        CooperativeComputeLane lane = new(policy, () => Env(8), TimeProvider.System);
        await using var laneScope = lane.ConfigureAwait(false);

        lane.Admit(ComputeWorkClass.ViewBuild, async _ =>
        {
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
        });

        await started.Task.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ComputeAdmission.Admitted, lane.Admit(ComputeWorkClass.ViewBuild, Nothing));
        Assert.AreEqual(ComputeAdmission.Admitted, lane.Admit(ComputeWorkClass.ViewBuild, Nothing));
        Assert.AreEqual(ComputeAdmission.ShedQueueFull, lane.Admit(ComputeWorkClass.ViewBuild, Nothing));

        release.SetResult();
    }

    /// <summary>
    /// Awaits until the lane reports <paramref name="expected"/> live workers,
    /// driven by the lane's state-transition signal and bounded by the test
    /// token — no wall-clock spin, so the assertion is robust under thread-pool
    /// load. Shrink is by attrition, so the worker count settles asynchronously.
    /// </summary>
    /// <param name="lane">The lane under test.</param>
    /// <param name="expected">The worker count to await.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>A task that completes when the lane reaches the expected width.</returns>
    private static async Task WaitForWorkerCountAsync(ThreadedComputeLane lane, int expected, CancellationToken cancellationToken)
    {
        while(true)
        {
            Task advanced = lane.ObserveStateTransition();
            if(lane.WorkerCount == expected)
            {
                return;
            }

            await advanced.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A turn body that does nothing — for admission and shed assertions that do not need the turn to observe anything.</summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    private static ValueTask Nothing(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
