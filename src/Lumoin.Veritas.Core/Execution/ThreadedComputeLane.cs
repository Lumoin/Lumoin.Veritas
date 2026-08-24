using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Threading;

namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// The compute lane for hosts with real concurrency — server, desktop,
/// and thread-capable mobile. A bounded count of idiomatic .NET async
/// consumers drain one observable, priority-ordered queue in turns;
/// concurrency is capped at the resolved width so compute pressure on
/// the shared pool is bounded (which, with the serve-pool floor,
/// isolates the serve path), and a control-plane tick re-sizes the lane
/// between turns when the CPU budget changes.
/// </summary>
/// <remarks>
/// <para>
/// One lock guards the queue and worker bookkeeping; turns are awaited
/// outside the lock, so the lock is held only for constant-time
/// enqueue/dequeue. Consumers drain greedily and block on an async
/// signal only when the queue is empty, so the signal need only
/// guarantee a wake when work or a resize arrives — surplus wakes are
/// harmless re-checks. Resize is by attrition at turn boundaries: growth
/// starts consumers, shrink lets surplus consumers self-exit when they
/// next reach the top of their loop, so a turn is never interrupted.
/// </para>
/// </remarks>
[DebuggerDisplay("ThreadedComputeLane Workers={WorkerCount} QueueDepth={QueueDepth} Turns={TurnsCompleted} Shed={ShedCount}")]
internal sealed class ThreadedComputeLane: IComputeLane
{
    /// <summary>The control-plane re-read cadence — small and independent of the scrub cadence, since its natural period is control-plane responsiveness, not integrity staleness. A placeholder; observed resize latency is additionally floored by the longest in-flight turn.</summary>
    private static readonly TimeSpan ControlPlaneTickCadence = TimeSpan.FromSeconds(2);

    /// <summary>The policy this lane re-resolves its width from on each tick.</summary>
    private readonly ExecutionPolicy policy;

    /// <summary>The environment observation seam re-read on each control-plane tick.</summary>
    private readonly ObserveEnvironmentDelegate observeEnvironment;

    /// <summary>The optional sink recording each completed turn's duration; <c>null</c> leaves the lane meter-free.</summary>
    private readonly RecordTurnDurationDelegate? recordTurnDuration;

    /// <summary>The monotonic clock the lane times turns with and runs the control-plane tick on.</summary>
    private readonly TimeProvider timeProvider;

    /// <summary>The lock guarding the queue and worker bookkeeping; turns are awaited outside it.</summary>
    private readonly object gate = new();

    /// <summary>The bounded multi-class work queue.</summary>
    private readonly BoundedComputeQueue queue;

    /// <summary>The wake signal: released when work is admitted or a resize must be noticed; consumers await it only when idle.</summary>
    private readonly SemaphoreSlim signal = new(0);

    /// <summary>Cancels the idle wait so consumers wake to drain and exit on disposal.</summary>
    private readonly CancellationTokenSource idleWaitCancellation = new();

    /// <summary>Cancels the control-plane tick loop on disposal.</summary>
    private readonly CancellationTokenSource tickLoopCancellation = new();

    /// <summary>The running consumer tasks; pruned of completed tasks on resize and awaited on disposal.</summary>
    private readonly List<Task> consumers = [];

    /// <summary>The control-plane tick loop task.</summary>
    private readonly Task tickLoop;

    /// <summary>The worker count the lane is sizing toward; guarded by <see cref="gate"/>.</summary>
    private int targetWorkerCount;

    /// <summary>The current live consumer count; guarded by <see cref="gate"/>.</summary>
    private int liveWorkerCount;

    /// <summary>Whether the lane is stopping and no longer admitting work; guarded by <see cref="gate"/>.</summary>
    private bool stopping;

    /// <summary>One when a control-plane tick is queued or in flight, coalescing redundant ticks; accessed via interlocked operations.</summary>
    private int tickPending;

    /// <summary>One once disposed; accessed via interlocked operations.</summary>
    private int disposed;

    /// <summary>The running count of completed turns; accessed via interlocked operations.</summary>
    private long turnsCompleted;

    /// <summary>The running count of shed admissions; accessed via interlocked operations.</summary>
    private long shedCount;

    /// <summary>The observation seam signaled on each state transition — a turn completing or the live worker count changing — so a test can await a transition without a wall-clock spin; unarmed in production, where the per-turn pulse is a single volatile read.</summary>
    private readonly StateTransitionObservation stateObservation = new();

    /// <summary>
    /// Constructs and starts the lane, sized from <paramref name="policy"/>
    /// resolved against the environment <paramref name="observeEnvironment"/>
    /// reports.
    /// </summary>
    /// <param name="policy">The policy whose resolved plan sizes the lane.</param>
    /// <param name="observeEnvironment">The environment observation seam, re-read on each control-plane tick.</param>
    /// <param name="timeProvider">The time source for the control-plane tick timer.</param>
    /// <param name="recordTurnDuration">The optional per-turn duration sink; <c>null</c> leaves the lane meter-free.</param>
    /// <exception cref="ArgumentNullException"><paramref name="observeEnvironment"/> or <paramref name="timeProvider"/> is <c>null</c>.</exception>
    internal ThreadedComputeLane(ExecutionPolicy policy, ObserveEnvironmentDelegate observeEnvironment, TimeProvider timeProvider, RecordTurnDurationDelegate? recordTurnDuration = null)
    {
        ArgumentNullException.ThrowIfNull(observeEnvironment);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.policy = policy;
        this.observeEnvironment = observeEnvironment;
        this.recordTurnDuration = recordTurnDuration;
        this.timeProvider = timeProvider;

        ResolvedExecutionPlan plan = policy.Resolve(observeEnvironment());
        queue = new BoundedComputeQueue(plan.ComputeQueueCapacity);
        targetWorkerCount = plan.ComputeLaneWorkers;

        lock(gate)
        {
            for(int i = 0; i < targetWorkerCount; i++)
            {
                SpawnConsumer();
            }
        }

        tickLoop = RunControlPlaneLoopAsync(this);
    }

    /// <inheritdoc/>
    public int WorkerCount
    {
        get
        {
            lock(gate)
            {
                return liveWorkerCount;
            }
        }
    }

    /// <inheritdoc/>
    public int QueueDepth
    {
        get
        {
            lock(gate)
            {
                return queue.Count;
            }
        }
    }

    /// <inheritdoc/>
    public long TurnsCompleted => Interlocked.Read(ref turnsCompleted);

    /// <inheritdoc/>
    public long ShedCount => Interlocked.Read(ref shedCount);

    /// <inheritdoc/>
    public int QueueDepthOf(ComputeWorkClass workClass)
    {
        lock(gate)
        {
            return queue.DepthOf(workClass);
        }
    }

    /// <inheritdoc/>
    public ComputeAdmission Admit(ComputeWorkClass workClass, ComputeWorkDelegate work)
    {
        ArgumentNullException.ThrowIfNull(work);

        lock(gate)
        {
            if(stopping)
            {
                Interlocked.Increment(ref shedCount);

                return ComputeAdmission.ShedLaneStopped;
            }

            ComputeAdmission admission = queue.TryEnqueue(workClass, work);
            if(admission == ComputeAdmission.Admitted)
            {
                signal.Release();
            }
            else
            {
                Interlocked.Increment(ref shedCount);
            }

            return admission;
        }
    }

    /// <summary>
    /// Stops the lane gracefully: it stops admitting work, the consumers
    /// drain the queued turns to completion, the control-plane tick loop
    /// is retired, and every consumer task is awaited. Idempotent.
    /// </summary>
    /// <returns>A task that completes when the lane has stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await tickLoopCancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            await tickLoop.ConfigureAwait(false);
        }
        catch(OperationCanceledException)
        {
        }

        Task[] toAwait;
        lock(gate)
        {
            stopping = true;
            toAwait = new Task[consumers.Count];
            consumers.CopyTo(toAwait);
        }

        //Wake the idle consumers so they drain the remaining queue and exit.
        await idleWaitCancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(toAwait).ConfigureAwait(false);
        }
        catch(OperationCanceledException)
        {
        }

        signal.Dispose();
        idleWaitCancellation.Dispose();
        tickLoopCancellation.Dispose();
    }

    /// <summary>
    /// Re-reads the environment, re-resolves the lane width, and applies
    /// it. Exposed for deterministic testing; in production it is the
    /// body of the queued control-plane tick turn.
    /// </summary>
    internal void RunControlPlaneTickOnce()
    {
        ResolvedExecutionPlan plan = policy.Resolve(observeEnvironment());
        ResizeWorkers(plan.ComputeLaneWorkers);
    }

    /// <summary>
    /// Arms state observation and returns a task that completes at the next
    /// turn completion or worker-count change. A test re-reads this between
    /// state checks to await a transition deterministically — bounded by its
    /// own token rather than a wall clock — so the assertion stays robust
    /// under thread-pool load. The observation counterpart to
    /// <see cref="RunControlPlaneTickOnce"/>; production leaves it unarmed and
    /// the pulse costs a single volatile read per turn.
    /// </summary>
    /// <returns>The task completing on the next observed state transition.</returns>
    internal Task ObserveStateTransition()
    {
        return stateObservation.Observe();
    }

    /// <summary>Signals the observation seam, waking any armed observer; a no-op until a test arms observation. Continuations run asynchronously, so it is safe to call while holding <see cref="gate"/>.</summary>
    private void SignalStateAdvanced()
    {
        stateObservation.Signal();
    }

    /// <summary>The async consumer loop: drain the highest-priority turn under the lock, await it outside the lock, and repeat until shrunk away or drained at stop.</summary>
    /// <returns>The loop task.</returns>
    private async Task ConsumerLoopAsync()
    {
        while(true)
        {
            ComputeWorkClass workClass = default;
            ComputeWorkDelegate? work = null;
            bool exit = false;

            lock(gate)
            {
                if(!stopping && liveWorkerCount > targetWorkerCount)
                {
                    liveWorkerCount--;
                    exit = true;
                }
                else if(!queue.TryDequeue(out workClass, out work) && stopping)
                {
                    liveWorkerCount--;
                    exit = true;
                }
            }

            if(exit)
            {
                SignalStateAdvanced();

                return;
            }

            if(work is not null)
            {
                await RunTurnAsync(workClass, work).ConfigureAwait(false);

                continue;
            }

            try
            {
                await signal.WaitAsync(idleWaitCancellation.Token).ConfigureAwait(false);
            }
            catch(OperationCanceledException)
            {
            }
        }
    }

    /// <summary>Runs one turn to completion, isolating its faults from the consumer and keeping the turn and tick-coalescing state current. A faulting turn does not tear down its consumer; the lane keeps draining.</summary>
    /// <param name="workClass">The turn's class — used to clear the control-plane tick coalescing flag.</param>
    /// <param name="work">The turn body.</param>
    /// <returns>A task that completes when the turn is done.</returns>
    private async Task RunTurnAsync(ComputeWorkClass workClass, ComputeWorkDelegate work)
    {
        long startTimestamp = recordTurnDuration is not null ? timeProvider.GetTimestamp() : 0;
        try
        {
            await work(CancellationToken.None).ConfigureAwait(false);
        }
        catch(OperationCanceledException)
        {
        }
        catch(Exception)
        {
        }
        finally
        {
            //Record before counting the turn done, so an observer that waits
            //on the turn count sees the matching duration already recorded.
            if(recordTurnDuration is not null)
            {
                recordTurnDuration(workClass, timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds);
            }

            Interlocked.Increment(ref turnsCompleted);
            if(workClass == ComputeWorkClass.ControlPlaneTick)
            {
                Volatile.Write(ref tickPending, 0);
            }

            SignalStateAdvanced();
        }
    }

    /// <summary>The control-plane tick loop: every cadence, enqueue a reserved tick turn that re-reads the budget and resizes the lane. Retired by cancelling its token on disposal.</summary>
    /// <param name="self">The lane.</param>
    /// <returns>The loop task.</returns>
    private static async Task RunControlPlaneLoopAsync(ThreadedComputeLane self)
    {
        using PeriodicTimer timer = new(ControlPlaneTickCadence, self.timeProvider);

        try
        {
            while(await timer.WaitForNextTickAsync(self.tickLoopCancellation.Token).ConfigureAwait(false))
            {
                self.EnqueueControlPlaneTick();
            }
        }
        catch(OperationCanceledException) when(self.tickLoopCancellation.Token.IsCancellationRequested)
        {
        }
    }

    /// <summary>Enqueues a single reserved control-plane tick turn, coalescing so at most one tick is queued or in flight at a time.</summary>
    private void EnqueueControlPlaneTick()
    {
        if(Interlocked.CompareExchange(ref tickPending, 1, 0) != 0)
        {
            return;
        }

        lock(gate)
        {
            if(stopping)
            {
                Volatile.Write(ref tickPending, 0);

                return;
            }

            queue.TryEnqueue(ComputeWorkClass.ControlPlaneTick, ControlPlaneTickTurn);
            signal.Release();
        }
    }

    /// <summary>The body of the reserved control-plane tick turn: re-read the budget and resize. Method-group converted to <see cref="ComputeWorkDelegate"/>.</summary>
    /// <param name="cancellationToken">Unused; the tick is short and runs to completion.</param>
    /// <returns>A completed task.</returns>
    private ValueTask ControlPlaneTickTurn(CancellationToken cancellationToken)
    {
        RunControlPlaneTickOnce();

        return ValueTask.CompletedTask;
    }

    /// <summary>Applies a new target worker count: grows by starting consumers now, and lets surplus consumers self-exit at their next turn boundary. Idempotent.</summary>
    /// <param name="newTargetWorkerCount">The desired width; floored at one.</param>
    private void ResizeWorkers(int newTargetWorkerCount)
    {
        lock(gate)
        {
            if(stopping)
            {
                return;
            }

            targetWorkerCount = Math.Max(1, newTargetWorkerCount);
            consumers.RemoveAll(static consumer => consumer.IsCompleted);
            while(liveWorkerCount < targetWorkerCount)
            {
                SpawnConsumer();
            }

            //Wake idle consumers so any now-surplus consumer notices and self-exits.
            if(liveWorkerCount > 0)
            {
                signal.Release(liveWorkerCount);
            }
        }
    }

    /// <summary>Starts and tracks one async consumer. The caller holds <see cref="gate"/>.</summary>
    private void SpawnConsumer()
    {
        consumers.Add(Task.Run(ConsumerLoopAsync));
        liveWorkerCount++;
    }
}
