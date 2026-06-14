using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// The compute lane for a single cooperative thread — the browser
/// WebAssembly runtime, where there are no worker threads. One async
/// pump drains the bounded, priority-ordered queue in turns, awaiting
/// each turn so a turn that itself awaits yields the cooperative thread
/// to the event loop. The turn model degenerates correctly to one
/// consumer, so WASM is a configuration of the same lane contract, not a
/// port: admission is still an explicit verdict, and the queue is still
/// bounded and observable.
/// </summary>
/// <remarks>
/// <para>
/// There is no control-plane tick: with a single cooperative thread
/// there is nothing to resize, so the width is fixed at one. No worker
/// threads are started; the pump runs on the host's cooperative thread.
/// </para>
/// </remarks>
[DebuggerDisplay("CooperativeComputeLane QueueDepth={QueueDepth} Turns={TurnsCompleted} Shed={ShedCount}")]
internal sealed class CooperativeComputeLane: IComputeLane
{
    /// <summary>The lock guarding the queue; turns are awaited outside it.</summary>
    private readonly object gate = new();

    /// <summary>The optional sink recording each completed turn's duration; <c>null</c> leaves the lane meter-free.</summary>
    private readonly RecordTurnDurationDelegate? recordTurnDuration;

    /// <summary>The monotonic clock the lane times turns with.</summary>
    private readonly TimeProvider timeProvider;

    /// <summary>The bounded multi-class work queue.</summary>
    private readonly BoundedComputeQueue queue;

    /// <summary>The wake signal: released when work is admitted; the pump awaits it only when idle.</summary>
    private readonly SemaphoreSlim signal = new(0);

    /// <summary>Cancels the idle wait so the pump wakes to drain and exit on disposal.</summary>
    private readonly CancellationTokenSource idleWaitCancellation = new();

    /// <summary>The single pump task.</summary>
    private readonly Task pump;

    /// <summary>Whether the lane is stopping and no longer admitting work; guarded by <see cref="gate"/>.</summary>
    private bool stopping;

    /// <summary>One once disposed; accessed via interlocked operations.</summary>
    private int disposed;

    /// <summary>The running count of completed turns; accessed via interlocked operations.</summary>
    private long turnsCompleted;

    /// <summary>The running count of shed admissions; accessed via interlocked operations.</summary>
    private long shedCount;

    /// <summary>
    /// Constructs and starts the lane, sizing its bounded queue from
    /// <paramref name="policy"/> resolved against the environment
    /// <paramref name="observeEnvironment"/> reports.
    /// </summary>
    /// <param name="policy">The policy whose resolved plan sizes the queue.</param>
    /// <param name="observeEnvironment">The environment observation seam.</param>
    /// <param name="timeProvider">The monotonic clock the lane times turns with.</param>
    /// <param name="recordTurnDuration">The optional per-turn duration sink; <c>null</c> leaves the lane meter-free.</param>
    /// <exception cref="ArgumentNullException"><paramref name="observeEnvironment"/> or <paramref name="timeProvider"/> is <c>null</c>.</exception>
    internal CooperativeComputeLane(ExecutionPolicy policy, ObserveEnvironmentDelegate observeEnvironment, TimeProvider timeProvider, RecordTurnDurationDelegate? recordTurnDuration = null)
    {
        ArgumentNullException.ThrowIfNull(observeEnvironment);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.recordTurnDuration = recordTurnDuration;
        this.timeProvider = timeProvider;
        ResolvedExecutionPlan plan = policy.Resolve(observeEnvironment());
        queue = new BoundedComputeQueue(plan.ComputeQueueCapacity);
        pump = PumpLoopAsync();
    }

    /// <inheritdoc/>
    public int WorkerCount => 1;

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
    /// Stops the lane gracefully: it stops admitting work, the pump
    /// drains the queued turns to completion, and the pump task is
    /// awaited. Idempotent.
    /// </summary>
    /// <returns>A task that completes when the lane has stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lock(gate)
        {
            stopping = true;
        }

        await idleWaitCancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            await pump.ConfigureAwait(false);
        }
        catch(OperationCanceledException)
        {
        }

        signal.Dispose();
        idleWaitCancellation.Dispose();
    }

    /// <summary>The single async pump: drain the highest-priority turn under the lock, await it outside the lock, and repeat until the queue is drained at stop.</summary>
    /// <returns>The pump task.</returns>
    private async Task PumpLoopAsync()
    {
        while(true)
        {
            ComputeWorkClass workClass = default;
            ComputeWorkDelegate? work = null;
            bool exit = false;

            lock(gate)
            {
                if(!queue.TryDequeue(out workClass, out work) && stopping)
                {
                    exit = true;
                }
            }

            if(exit)
            {
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

    /// <summary>Runs one turn to completion, isolating its faults from the pump and keeping the turn counter current. A faulting turn does not stop the pump.</summary>
    /// <param name="workClass">The turn's work class — the tag for the recorded duration.</param>
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
        }
    }
}
