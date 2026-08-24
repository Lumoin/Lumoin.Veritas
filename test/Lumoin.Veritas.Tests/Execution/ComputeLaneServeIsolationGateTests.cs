using System;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Execution;
using Microsoft.Extensions.Time.Testing;

namespace Lumoin.Veritas.Tests.Execution;

/// <summary>
/// The phase-B exit gate for the compute lane's serve isolation: the lane
/// caps concurrent compute at its width, where the same work run inline on the
/// thread pool floods past that width. That occupancy bound is the mechanism
/// the floor-lift rests on — it is why the lane, not the host thread-pool
/// floor, is what keeps compute off the serve path, so the floor ships lifted
/// (<c>HostPoolFloorMultiplier = 0</c>). The gate proves the bound directly
/// and deterministically — a differential between the lane's exact width cap
/// and the pool's unbounded concurrency — rather than through a flaky latency
/// threshold under induced starvation. It runs isolated because it briefly
/// blocks pool threads.
/// </summary>
[TestClass]
[DoNotParallelize]
internal sealed class ComputeLaneServeIsolationGateTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Builds a non-browser environment snapshot.</summary>
    /// <param name="processorCount">The logical processor count.</param>
    /// <returns>The environment snapshot.</returns>
    private static ExecutionEnvironment Env(int processorCount)
    {
        return new ExecutionEnvironment(processorCount, null, false, null);
    }

    [TestMethod]
    public async Task LaneCapsConcurrentComputeAtItsWidthWhereInlineFloods()
    {
        const int width = 2;
        const int taskCount = 5;

        FakeTimeProvider time = new();
        ExecutionPolicy policy = ExecutionPolicy.Default with { ComputeLaneWorkers = width, ComputeQueueCapacity = 16 };

        using ManualResetEventSlim laneRelease = new(false);
        object laneLock = new();
        int laneStarted = 0;
        int laneConcurrent = 0;
        int laneMaxConcurrent = 0;
        TaskCompletionSource widthStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        ThreadedComputeLane lane = new(policy, () => Env(8), time);
        await using(lane.ConfigureAwait(false))
        {
            for(int i = 0; i < taskCount; i++)
            {
                ComputeAdmission admission = lane.Admit(ComputeWorkClass.BulkSort, cancellationToken =>
                {
                    lock(laneLock)
                    {
                        laneStarted++;
                        laneConcurrent++;
                        laneMaxConcurrent = Math.Max(laneMaxConcurrent, laneConcurrent);
                        if(laneStarted == width)
                        {
                            widthStarted.TrySetResult();
                        }
                    }

                    laneRelease.Wait(cancellationToken);

                    lock(laneLock)
                    {
                        laneConcurrent--;
                    }

                    return ValueTask.CompletedTask;
                });

                Assert.AreEqual(ComputeAdmission.Admitted, admission);
            }

            //The width consumers each take a turn and block; no further turn can
            //start because no consumer is free — that is the cap. Releasing lets
            //the queued turns drain, never more than the width at once. Both waits
            //are signaled at the exact state transition and bounded only by the
            //test cancellation token — a stall here is the runner's hang case.
            await widthStarted.Task.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
            laneRelease.Set();
            while(true)
            {
                Task advanced = lane.ObserveStateTransition();
                if(lane.TurnsCompleted == taskCount)
                {
                    break;
                }

                await advanced.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
            }

        }

        lock(laneLock)
        {
            Assert.AreEqual(width, laneMaxConcurrent, "The lane never ran more than its width of turns concurrently.");
        }

        using ManualResetEventSlim inlineRelease = new(false);
        object inlineLock = new();
        int inlineStarted = 0;
        int inlineConcurrent = 0;
        int inlineMaxConcurrent = 0;
        TaskCompletionSource floodPassed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task[] inlineTasks = new Task[taskCount];
        for(int i = 0; i < taskCount; i++)
        {
            inlineTasks[i] = Task.Run(() =>
            {
                lock(inlineLock)
                {
                    inlineStarted++;
                    inlineConcurrent++;
                    inlineMaxConcurrent = Math.Max(inlineMaxConcurrent, inlineConcurrent);
                    if(inlineStarted > width)
                    {
                        floodPassed.TrySetResult();
                    }
                }

                inlineRelease.Wait(TestContext.CancellationToken);

                lock(inlineLock)
                {
                    inlineConcurrent--;
                }
            }, TestContext.CancellationToken);
        }

        //More than the lane's width run at once on the pool — no occupancy bound.
        //The signal fires the moment the width is exceeded; the await is bounded
        //only by the test cancellation token (pool thread injection may take a
        //moment on a starved host, which is the property under proof, not a race).
        await floodPassed.Task.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
        inlineRelease.Set();
        await Task.WhenAll(inlineTasks).ConfigureAwait(false);

        lock(inlineLock)
        {
            Assert.IsGreaterThan(width, inlineMaxConcurrent, "Inline compute ran more than the lane's width concurrently — the lane is what bounds it.");
        }
    }
}
