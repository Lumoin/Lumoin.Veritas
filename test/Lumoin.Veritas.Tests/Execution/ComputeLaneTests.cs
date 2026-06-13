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
/// and bounded spin-waits — no sleeps — so the assertions are
/// deterministic.
/// </summary>
[TestClass]
internal sealed class ComputeLaneTests
{
    /// <summary>The safety bound for waiting on a cross-thread condition that a correct lane reaches near-instantly; a timeout means a real defect, not a slow machine.</summary>
    private static readonly TimeSpan ConditionTimeout = TimeSpan.FromSeconds(10);

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
        using ManualResetEventSlim ran = new(false);

        ThreadedComputeLane lane = new(ExecutionPolicy.Default, () => Env(4), time);
        await using var laneScope = lane.ConfigureAwait(false);

        ComputeAdmission admission = lane.Admit(ComputeWorkClass.ViewBuild, _ =>
        {
            ran.Set();

            return ValueTask.CompletedTask;
        });

        Assert.AreEqual(ComputeAdmission.Admitted, admission);
        Assert.IsTrue(ran.Wait(ConditionTimeout, TestContext.CancellationToken), "Expected the admitted turn to run.");
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

        Assert.IsTrue(
            SpinWait.SpinUntil(() => lane.WorkerCount == plan.ComputeLaneWorkers, ConditionTimeout),
            $"Expected the live lane to realize {plan.ComputeLaneWorkers} workers, saw {lane.WorkerCount}.");
    }

    [TestMethod]
    public async Task AFullBoundedQueueShedsWithAVerdict()
    {
        FakeTimeProvider time = new();
        using ManualResetEventSlim started = new(false);
        TaskCompletionSource release = new();

        //One worker, queue capacity two: occupy the worker, fill the queue, then the next sheds.
        ExecutionPolicy policy = ExecutionPolicy.Default with { ComputeLaneWorkers = 1, ComputeQueueCapacity = 2 };
        ThreadedComputeLane lane = new(policy, () => Env(8), time);
        await using var laneScope = lane.ConfigureAwait(false);

        lane.Admit(ComputeWorkClass.ViewBuild, async _ =>
        {
            started.Set();
            await release.Task.ConfigureAwait(false);
        });

        Assert.IsTrue(started.Wait(ConditionTimeout, TestContext.CancellationToken), "Expected the first turn to occupy the worker.");

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

        Assert.IsTrue(SpinWait.SpinUntil(() => lane.WorkerCount == 3, ConditionTimeout), $"Expected three workers, saw {lane.WorkerCount}.");
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

        Assert.IsTrue(SpinWait.SpinUntil(() => lane.WorkerCount == 3, ConditionTimeout), $"Expected the timer-driven tick to resize to three, saw {lane.WorkerCount}.");
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
        using ManualResetEventSlim ran = new(false);

        ExecutionPolicy policy = ExecutionPolicy.Default with { ComputeLaneWorkers = 1, ComputeQueueCapacity = 16 };
        ThreadedComputeLane lane = new(policy, () => Env(8), time);
        await using var laneScope = lane.ConfigureAwait(false);

        lane.Admit(ComputeWorkClass.Reasoning, _ => throw new InvalidOperationException("turn fault"));
        lane.Admit(ComputeWorkClass.Reasoning, _ =>
        {
            ran.Set();

            return ValueTask.CompletedTask;
        });

        //The consumer survived the faulting turn and ran the next one.
        Assert.IsTrue(ran.Wait(ConditionTimeout, TestContext.CancellationToken), "Expected the consumer to survive a faulting turn and run the next.");
    }

    [TestMethod]
    public async Task CooperativeLaneRunsTurnsOnItsSinglePump()
    {
        using ManualResetEventSlim ran = new(false);

        CooperativeComputeLane lane = new(ExecutionPolicy.Default, () => Env(8));
        await using var laneScope = lane.ConfigureAwait(false);

        //The single-cooperative-thread lane is always width one.
        Assert.AreEqual(1, lane.WorkerCount);

        ComputeAdmission admission = lane.Admit(ComputeWorkClass.ViewBuild, _ =>
        {
            ran.Set();

            return ValueTask.CompletedTask;
        });

        Assert.AreEqual(ComputeAdmission.Admitted, admission);
        Assert.IsTrue(ran.Wait(ConditionTimeout, TestContext.CancellationToken), "Expected the cooperative pump to run the admitted turn.");
    }

    [TestMethod]
    public async Task CooperativeLaneShedsAFullQueueWithAVerdict()
    {
        using ManualResetEventSlim started = new(false);
        TaskCompletionSource release = new();

        ExecutionPolicy policy = ExecutionPolicy.Default with { ComputeQueueCapacity = 2 };
        CooperativeComputeLane lane = new(policy, () => Env(8));
        await using var laneScope = lane.ConfigureAwait(false);

        lane.Admit(ComputeWorkClass.ViewBuild, async _ =>
        {
            started.Set();
            await release.Task.ConfigureAwait(false);
        });

        Assert.IsTrue(started.Wait(ConditionTimeout, TestContext.CancellationToken), "Expected the first turn to occupy the pump.");

        Assert.AreEqual(ComputeAdmission.Admitted, lane.Admit(ComputeWorkClass.ViewBuild, Nothing));
        Assert.AreEqual(ComputeAdmission.Admitted, lane.Admit(ComputeWorkClass.ViewBuild, Nothing));
        Assert.AreEqual(ComputeAdmission.ShedQueueFull, lane.Admit(ComputeWorkClass.ViewBuild, Nothing));

        release.SetResult();
    }

    /// <summary>A turn body that does nothing — for admission and shed assertions that do not need the turn to observe anything.</summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    private static ValueTask Nothing(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
