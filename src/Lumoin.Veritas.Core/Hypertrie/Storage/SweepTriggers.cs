using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Threading;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// Factory for sweep triggers that observe a <see cref="NodeStore"/>
/// and call <see cref="NodeStore.SweepAsync"/> when a configured
/// condition is met. Each factory method returns an
/// <see cref="IAsyncDisposable"/> handle whose asynchronous disposal
/// stops the trigger and awaits its loop draining; the library does
/// not register any trigger by default.
/// </summary>
/// <remarks>
/// <para>
/// Triggers carry a small amount of state — a periodic timer and
/// the cancellation source that retires its loop. The factory
/// hides the concrete trigger types behind <see cref="IDisposable"/>
/// because the only caller-side operation is stop. A trigger that
/// later needs additional configuration after construction is a
/// candidate for promotion to its own type at that point; until
/// then the factory shape is the simplest correct surface.
/// </para>
/// <para>
/// <b>Concurrency.</b> Each trigger spawns one long-lived task
/// driven by a <see cref="PeriodicTimer"/>. The task observes the
/// configured condition and calls <see cref="NodeStore.SweepAsync"/>
/// directly; sweeps serialise on the store's mutation gate, so a
/// trigger that fires during a concurrent build simply waits its
/// turn rather than failing.
/// </para>
/// <para>
/// <b>Time source.</b> Both factories accept a
/// <see cref="TimeProvider"/>; tests pass a fake provider so they
/// can advance time deterministically without relying on real
/// wall-clock delays. Production callers leave the parameter at
/// its default and get <see cref="TimeProvider.System"/>.
/// </para>
/// <para>
/// <b>Observation.</b> Every trigger implements
/// <see cref="ISweepRoundObservation"/>, the per-round seam a test
/// arms to await the loop finishing a tick deterministically;
/// production never arms it, leaving each round a single volatile
/// read.
/// </para>
/// </remarks>
public static class SweepTriggers
{
    private static TimeSpan DefaultWatermarkPollInterval { get; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Builds a trigger that fires <see cref="NodeStore.SweepAsync"/>
    /// whenever <see cref="NodeStore.Count"/> reaches or exceeds
    /// <paramref name="nodeCountThreshold"/>. The trigger polls
    /// the count at <paramref name="pollInterval"/>; a watermark
    /// crossing observed mid-poll is acted on at the next tick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Polling is deliberate. Wiring an event into
    /// <see cref="NodeStore.Intern"/> would let the trigger fire
    /// immediately, but firing from inside the mutation scope
    /// would deadlock — sweep takes the same scope — so any event
    /// path needs a queue and a separate worker that consumes
    /// after the originating mutation releases the scope. That is
    /// more machinery than batch four needs; polling produces the
    /// same eventual outcome with one timer and one task.
    /// </para>
    /// </remarks>
    /// <param name="store">The store to monitor.</param>
    /// <param name="nodeCountThreshold">The watermark at which a sweep is fired; must be positive.</param>
    /// <param name="pollInterval">The polling cadence; defaults to 500 ms when <c>null</c>. Must be positive.</param>
    /// <param name="timeProvider">The time source for the polling timer; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="cancellationToken">An optional cancellation token. Cancelling stops the trigger; disposal does the same.</param>
    /// <returns>A handle whose asynchronous disposal stops the trigger.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="nodeCountThreshold"/> is not positive, or <paramref name="pollInterval"/> is non-positive.</exception>
    public static IAsyncDisposable Watermark(
        NodeStore store,
        int nodeCountThreshold,
        TimeSpan? pollInterval = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        if(nodeCountThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nodeCountThreshold),
                nodeCountThreshold,
                "Threshold must be positive.");
        }

        TimeSpan effectiveInterval = pollInterval ?? DefaultWatermarkPollInterval;
        if(effectiveInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                effectiveInterval,
                "Poll interval must be positive.");
        }

        return new WatermarkTrigger(
            store,
            nodeCountThreshold,
            effectiveInterval,
            timeProvider ?? TimeProvider.System,
            cancellationToken);
    }

    /// <summary>
    /// Builds a trigger that fires <see cref="NodeStore.SweepAsync"/>
    /// once per <paramref name="interval"/> regardless of the
    /// current node count.
    /// </summary>
    /// <param name="store">The store to sweep.</param>
    /// <param name="interval">The wall-clock interval between sweeps; must be positive.</param>
    /// <param name="timeProvider">The time source for the timer; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="cancellationToken">An optional cancellation token. Cancelling stops the trigger; disposal does the same.</param>
    /// <returns>A handle whose asynchronous disposal stops the trigger.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> is non-positive.</exception>
    public static IAsyncDisposable Scheduled(
        NodeStore store,
        TimeSpan interval,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        if(interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                interval,
                "Interval must be positive.");
        }

        return new ScheduledTrigger(
            store,
            interval,
            timeProvider ?? TimeProvider.System,
            cancellationToken);
    }

    [DebuggerDisplay("WatermarkTrigger Threshold={Threshold} Interval={Interval}")]
    private sealed class WatermarkTrigger: IAsyncDisposable, ISweepRoundObservation
    {
        private NodeStore Store { get; }

        private int Threshold { get; }

        private TimeSpan Interval { get; }

        private TimeProvider TimeProvider { get; }

        private CancellationTokenSource Cts { get; }

        private Task Loop { get; }

        /// <summary>The per-round observation seam, signaled once per processed tick — swept or declined.</summary>
        private StateTransitionObservation RoundObservation { get; } = new();

        private int disposed;

        public WatermarkTrigger(
            NodeStore store,
            int threshold,
            TimeSpan interval,
            TimeProvider timeProvider,
            CancellationToken externalCancellationToken)
        {
            Store = store;
            Threshold = threshold;
            Interval = interval;
            TimeProvider = timeProvider;
            Cts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
            Loop = RunLoopAsync(this);
        }

        /// <summary>Stops the trigger and awaits its loop draining so a subsequent step observes a stable sweep count. Idempotent.</summary>
        /// <returns>A task that completes when the loop has drained.</returns>
        public async ValueTask DisposeAsync()
        {
            if(Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            await Cts.CancelAsync().ConfigureAwait(false);

            //Await the loop draining so the next step can observe a stable
            //SweepCount. The loop's only blocking points either return
            //cleanly on cancellation or throw OperationCanceledException,
            //both of which we accept.
            try
            {
                await Loop.ConfigureAwait(false);
            }
            catch(OperationCanceledException)
            {
            }

            Cts.Dispose();
        }

        /// <summary>Arms the round seam and returns the task completing when the loop finishes its next round.</summary>
        /// <returns>The task completing on the next finished round.</returns>
        public Task ObserveRound()
        {
            return RoundObservation.Observe();
        }

        private static async Task RunLoopAsync(WatermarkTrigger self)
        {
            using PeriodicTimer timer = new(self.Interval, self.TimeProvider);

            try
            {
                while(await timer.WaitForNextTickAsync(self.Cts.Token).ConfigureAwait(false))
                {
                    if(self.Store.Count >= self.Threshold)
                    {
                        try
                        {
                            await self.Store.SweepAsync(self.Cts.Token).ConfigureAwait(false);
                        }
                        catch(OperationCanceledException) when(self.Cts.Token.IsCancellationRequested)
                        {
                            return;
                        }
                    }

                    self.RoundObservation.Signal();
                }
            }
            catch(OperationCanceledException) when(self.Cts.Token.IsCancellationRequested)
            {
                //Expected on disposal — the linked token fires when Dispose calls Cancel.
            }
        }
    }

    [DebuggerDisplay("ScheduledTrigger Interval={Interval}")]
    private sealed class ScheduledTrigger: IAsyncDisposable, ISweepRoundObservation
    {
        private NodeStore Store { get; }

        private TimeSpan Interval { get; }

        private TimeProvider TimeProvider { get; }

        private CancellationTokenSource Cts { get; }

        private Task Loop { get; }

        /// <summary>The per-round observation seam, signaled once per processed tick.</summary>
        private StateTransitionObservation RoundObservation { get; } = new();

        private int disposed;

        public ScheduledTrigger(
            NodeStore store,
            TimeSpan interval,
            TimeProvider timeProvider,
            CancellationToken externalCancellationToken)
        {
            Store = store;
            Interval = interval;
            TimeProvider = timeProvider;
            Cts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
            Loop = RunLoopAsync(this);
        }

        /// <summary>Stops the trigger and awaits its loop draining so a subsequent step observes a stable sweep count. Idempotent.</summary>
        /// <returns>A task that completes when the loop has drained.</returns>
        public async ValueTask DisposeAsync()
        {
            if(Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            await Cts.CancelAsync().ConfigureAwait(false);

            try
            {
                await Loop.ConfigureAwait(false);
            }
            catch(OperationCanceledException)
            {
            }

            Cts.Dispose();
        }

        /// <summary>Arms the round seam and returns the task completing when the loop finishes its next round.</summary>
        /// <returns>The task completing on the next finished round.</returns>
        public Task ObserveRound()
        {
            return RoundObservation.Observe();
        }

        private static async Task RunLoopAsync(ScheduledTrigger self)
        {
            using PeriodicTimer timer = new(self.Interval, self.TimeProvider);

            try
            {
                while(await timer.WaitForNextTickAsync(self.Cts.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await self.Store.SweepAsync(self.Cts.Token).ConfigureAwait(false);
                    }
                    catch(OperationCanceledException) when(self.Cts.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    self.RoundObservation.Signal();
                }
            }
            catch(OperationCanceledException) when(self.Cts.Token.IsCancellationRequested)
            {
            }
        }
    }
}
