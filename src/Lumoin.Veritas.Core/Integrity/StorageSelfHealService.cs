using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Core.Persistence;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// Runs storage self-healing as a background behaviour: on a reliability-driven cadence it verifies the store's
/// committed generation, repairs what it can re-derive from the verified system-of-record, and atomically
/// publishes a healed generation that supersedes the damaged one — wiring the existing scrub turn
/// (<see cref="ScrubTurn"/>), repair pass (<see cref="ScrubRound.RunRepairPass"/>), and commit coordinator
/// (<see cref="GenerationCommitCoordinator"/>) into one running loop. Each round is also exposed as a public
/// deterministic unit (<see cref="RunRoundAsync"/>) so a host or test drives rounds directly without timers.
/// </summary>
/// <remarks>
/// <para>
/// Scope is deliberately single-process and whole-generation. The commit step serializes against a foreground
/// persist ONLY within this process, through the shared <see cref="SelfHealOptions.CommitMutex"/> the owner also
/// takes around its persist; two processes writing one store coordinate through the store's own atomic-publish
/// contract, not this in-memory lock. A round verifies the WHOLE generation each pass (the verify pass is a
/// full-generation walk); a budgeted, chunked verify that bounds the per-round IO is a named follow-up, so there
/// is no IO-budget knob in this cut. Host scheduling stays a policy seam: the loop's cadence is the injected
/// estimator, and a host that wants a different rhythm drives <see cref="RunRoundAsync"/> on its own schedule.
/// </para>
/// <para>
/// The loop never dies silently on a bad round: a round that faults (an unreadable store, a failed commit) records
/// a <see cref="StorageTraceEventKind.ScrubRoundFailed"/> event and the loop continues to the next round;
/// cancellation of the loop's token propagates and stops it. An empty store (no committed generation) is a clean
/// no-op, not a fault.
/// </para>
/// </remarks>
public sealed class StorageSelfHealService: IDisposable
{
    /// <summary>The store whose committed generation is verified, repaired, and re-published.</summary>
    private PersistenceStore Store { get; }

    /// <summary>The injected re-derive knobs and pools a repair pass rebuilds damaged derived artifacts under.</summary>
    private RepairConfiguration Configuration { get; }

    /// <summary>The commit coordinator that atomically publishes a healed generation from a repair report; built once over the store and reused every round.</summary>
    private GenerationCommitCoordinator Coordinator { get; }

    /// <summary>Resolves a stored checksum-algorithm id for the verify and repair passes; <see langword="null"/> uses the default resolver.</summary>
    private ResolveChecksumAlgorithmDelegate? ResolveChecksum { get; }

    /// <summary>The diagnostics sink every round's events are emitted to; <see langword="null"/> emits nothing.</summary>
    private TraceHandler<StorageTraceEvent>? Trace { get; }

    /// <summary>The clock the rounds timestamp with and the loop's delays are scheduled against, so a test controls time.</summary>
    private TimeProvider Clock { get; }

    /// <summary>The policy the loop's cadence, jitter, commit serialization, and round observation run under.</summary>
    private SelfHealOptions Options { get; }

    /// <summary>The source of a fresh correlation id per round, so a round's events group by their shared id.</summary>
    private IdentifierDelegate CorrelationIds { get; } = VeritasIdentifiers.System;

    /// <summary>One once the service has been disposed; guards use after disposal.</summary>
    private int disposed;

    /// <summary>Creates a storage self-heal service over a store, mirroring the seams the scrub-and-commit choreography wires: the store, the repair configuration, the commit coordinator's dependencies, the trace sink, the clock, and the run policy.</summary>
    /// <param name="store">The store whose committed generation is verified, repaired, and re-published.</param>
    /// <param name="configuration">The injected re-derive knobs and pools a repair pass rebuilds damaged derived artifacts under.</param>
    /// <param name="checksum">The checksum algorithm the healed manifest and its entry digests are written under.</param>
    /// <param name="bufferPool">The pool the commit's staging digests and manifest writer rent from.</param>
    /// <param name="retainedCurrentPointerCount">The number of retained per-generation CURRENT copies the healed publish keeps; at least one.</param>
    /// <param name="resolveChecksum">Resolves a stored checksum-algorithm id; <see langword="null"/> uses the default resolver.</param>
    /// <param name="trace">The diagnostics sink; <see langword="null"/> emits nothing.</param>
    /// <param name="timeProvider">The clock the rounds timestamp with and the loop schedules against.</param>
    /// <param name="options">The run policy: cadence, jitter, commit serialization, and round observation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/>, <paramref name="configuration"/>, <paramref name="checksum"/>, <paramref name="bufferPool"/>, <paramref name="timeProvider"/>, or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retainedCurrentPointerCount"/> is less than one, or <paramref name="options"/>'s <see cref="SelfHealOptions.JitterFraction"/> is outside [0, 1).</exception>
    public StorageSelfHealService(
        PersistenceStore store,
        RepairConfiguration configuration,
        ChecksumAlgorithm checksum,
        MemoryPool<byte> bufferPool,
        int retainedCurrentPointerCount,
        ResolveChecksumAlgorithmDelegate? resolveChecksum,
        TraceHandler<StorageTraceEvent>? trace,
        TimeProvider timeProvider,
        SelfHealOptions options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(checksum);
        ArgumentNullException.ThrowIfNull(bufferPool);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.JitterFraction);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(options.JitterFraction, 1.0);

        Store = store;
        Configuration = configuration;
        ResolveChecksum = resolveChecksum;
        Trace = trace;
        Clock = timeProvider;
        Options = options;

        //The coordinator is stateless across commits (it recovers the live generation each Commit), so it is built
        //once and reused; its ctor enforces the retained-pointer lower bound, so a misconfiguration fails here.
        Coordinator = new GenerationCommitCoordinator(store, checksum, bufferPool, retainedCurrentPointerCount, resolveChecksum, trace, timeProvider);
    }

    /// <summary>
    /// Runs the self-heal loop until cancelled: each iteration waits the jittered target interval (scheduled on the
    /// injected clock so a test controls time), then runs one round. A round that faults does not kill the loop — it
    /// was recorded on the trace and the loop continues; cancellation of <paramref name="cancellationToken"/> stops
    /// the loop and propagates.
    /// </summary>
    /// <param name="cancellationToken">Stops the loop when signalled; the stop propagates as cancellation.</param>
    /// <returns>A task that completes (via cancellation) when the loop stops.</returns>
    /// <exception cref="ObjectDisposedException">The service has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The self-heal loop must survive any single round's fault (an unreadable store, a failed commit) rather than dying silently; the fault was already recorded on the trace by the round, and cancellation is rethrown so it still stops the loop.")]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) == 1, this);

        //The protected-item count the cadence scales by is the live generation's block count, observed per round.
        //The configured context's count seeds the first interval, before any round has walked the generation.
        long observedItemCount = Options.CadenceContext.ProtectedItemCount;
        while(!cancellationToken.IsCancellationRequested)
        {
            //The scheduling phase must not kill background healing: a faulting estimator or jitter source is
            //recorded and the loop falls back to a fixed delay, staying alive and visible round after round.
            TimeSpan delay;
            try
            {
                TimeSpan interval = Options.CadenceEstimator(Options.CadenceContext with { ProtectedItemCount = observedItemCount });
                delay = ComputeJitteredDelay(interval, Options.JitterFraction, SampleJitter());
            }
            catch(Exception)
            {
                EmitRoundFailed(CorrelationIds(new IdentifierRequest(IdentifierPurpose.Correlation, default)));
                delay = FallbackDelay;
            }

            await Task.Delay(delay, Clock, cancellationToken).ConfigureAwait(false);

            try
            {
                (_, long roundItemCount) = await RunRoundInternalAsync(cancellationToken).ConfigureAwait(false);
                observedItemCount = roundItemCount;
            }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch(Exception)
            {
                //The round already recorded a ScrubRoundFailed event; the loop survives and reuses the last good
                //item count for the next interval.
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Runs ONE deterministic scrub round: verify the committed generation, repair what it can, and publish a healed
    /// generation when the repair produced re-derived artifacts (the coordinator declines a clean, refused, or
    /// superseded report). Returns the commit verdict, or <see langword="null"/> when the store holds no committed
    /// generation (a clean no-op). A round that faults records a <see cref="StorageTraceEventKind.ScrubRoundFailed"/>
    /// event on the trace and rethrows, so a direct caller sees the fault and the loop can continue past it.
    /// </summary>
    /// <param name="cancellationToken">Abandons the round cooperatively; forwarded to the round-result sink.</param>
    /// <returns>The commit verdict, or <see langword="null"/> when the store holds no committed generation.</returns>
    /// <exception cref="ObjectDisposedException">The service has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public async ValueTask<GenerationCommitReport?> RunRoundAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) == 1, this);

        (GenerationCommitReport? report, _) = await RunRoundInternalAsync(cancellationToken).ConfigureAwait(false);

        return report;
    }

    /// <summary>Runs one round and returns both the commit verdict and the generation's block count (the protected-item count the next interval scales by). An empty store yields a <see langword="null"/> verdict and a zero count.</summary>
    /// <param name="cancellationToken">Abandons the round cooperatively.</param>
    /// <returns>The commit verdict (<see langword="null"/> when no generation) and the round's protected-item count.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A round records the fault on the trace and rethrows it so the loop can decide to continue and a direct caller sees it; cancellation and the no-generation no-op are handled before the general catch.")]
    private async ValueTask<(GenerationCommitReport? Report, long ItemCount)> RunRoundInternalAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Guid correlationId = CorrelationIds(new IdentifierRequest(IdentifierPurpose.Correlation, default));
        RoundCommitStep step = new(Coordinator, Options.CommitMutex, correlationId, Options.OnRoundComplete);
        ScrubTurn turn = new(Store, ResolveChecksum, Configuration, step.HandleAsync, Trace, correlationId, Clock, Options.ProvidePeerSource, Options.ProvideShardedPeerSource);
        try
        {
            await turn.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(InvalidDataException)
        {
            //No committed generation could be recovered — an empty store is a clean no-op, not a fault.
            return (null, 0);
        }
        catch(OperationCanceledException)
        {
            throw;
        }
        catch(Exception)
        {
            EmitRoundFailed(correlationId);

            throw;
        }

        return (step.Report, step.ItemCount);
    }

    /// <summary>The shortest delay the loop schedules, whatever the estimator returned — the floor that keeps a degenerate (zero or negative) estimate from spinning rounds hot.</summary>
    internal static TimeSpan MinimumLoopDelay { get; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest delay the loop schedules in one wait. The reliability model legitimately estimates multi-month cadences for small stores (its ceiling is a year), but the scheduler's timer tops out near 49.7 days — so the delay is capped safely below that and the loop simply wakes, re-estimates, and waits again; waking early only ever scrubs sooner than the model asked, never later.</summary>
    internal static TimeSpan MaximumLoopDelay { get; } = TimeSpan.FromDays(45);

    /// <summary>The fixed delay the loop falls back to when the scheduling phase itself faults (a throwing estimator or jitter source), keeping the loop alive and its fault visible on the trace once per attempt.</summary>
    private static TimeSpan FallbackDelay { get; } = TimeSpan.FromHours(1);

    /// <summary>Computes the delay for one round: the estimated interval spread over its jitter band, clamped to the loop's schedulable range. Pure and total so the loop's timing is testable without a clock — the delay is <c>interval * (1 - jitterFraction/2 + jitterSample * jitterFraction)</c>, clamped into [<see cref="MinimumLoopDelay"/>, <see cref="MaximumLoopDelay"/>] so a degenerate estimate never spins the loop hot and a multi-month estimate never exceeds the scheduler's timer ceiling.</summary>
    /// <param name="interval">The estimated target interval between rounds.</param>
    /// <param name="jitterFraction">The fraction of the interval the delay is spread over; in [0, 1).</param>
    /// <param name="jitterSample">The jitter sample in [0, 1) positioning the delay within the band.</param>
    /// <returns>The jittered, clamped delay.</returns>
    internal static TimeSpan ComputeJitteredDelay(TimeSpan interval, double jitterFraction, double jitterSample)
    {
        double factor = 1.0 - (jitterFraction / 2.0) + (jitterSample * jitterFraction);
        TimeSpan delay = interval * factor;

        if(delay < MinimumLoopDelay)
        {
            return MinimumLoopDelay;
        }

        return delay > MaximumLoopDelay ? MaximumLoopDelay : delay;
    }

    /// <summary>Draws one jitter sample: the injected source when configured, else the shared non-cryptographic RNG.</summary>
    /// <returns>A jitter sample in [0, 1).</returns>
    private double SampleJitter()
    {
        return Options.JitterSample is { } sample ? sample() : DefaultJitterSample();
    }

    /// <summary>Draws a default jitter sample from the shared non-cryptographic RNG. The jitter only de-correlates a fleet's scrub timing — it is not an identity or a security token — and the <see cref="SelfHealOptions.JitterSample"/> seam injects a deterministic sample in tests, so the shared RNG is the correct source here, as it is for the synthetic-graph seam.</summary>
    /// <returns>A jitter sample in [0, 1).</returns>
    [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Justification = "The scrub-cadence jitter de-correlates a fleet's scrub timing; it is not an identity or a security token, and the JitterSample seam injects a deterministic sample in tests, so Random.Shared is the correct non-cryptographic source here — mirroring the RandomSources synthetic-graph seam.")]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "The scrub-cadence jitter only de-correlates a fleet's scrub timing; it is not security-sensitive, so the standard non-cryptographic Random.Shared is the correct source.")]
    private static double DefaultJitterSample()
    {
        return Random.Shared.NextDouble();
    }

    /// <summary>Emits the round-failed lifecycle marker when a sink is attached: a round abandoned with a fault rather than a clean verdict. Round-level (role code 0, block index -1); no generation, block, or item is scoped, since the fault may have preceded the generation being resolved.</summary>
    /// <param name="correlationId">The correlation id the failed round carried.</param>
    private void EmitRoundFailed(Guid correlationId)
    {
        if(Trace is null)
        {
            return;
        }

        StorageTraceEvent evt = new(0, Clock.GetUtcNow().UtcTicks, correlationId, StorageTraceEventKind.ScrubRoundFailed, 0, 0, -1, 0, 0, 0);
        Trace(in evt);
    }

    /// <summary>Marks the service disposed so its public entry points refuse further use; idempotent. The pools, trace, and clock are caller-owned seams the service does not own, so it releases nothing else — the owner cancels the loop and disposes the pools it lent.</summary>
    public void Dispose()
    {
        _ = Interlocked.Exchange(ref disposed, 1);
    }

    /// <summary>
    /// Brackets one round's commit: it receives the verify and repair verdicts from the scrub turn (a
    /// generation-agnostic producer that commits nothing), publishes the heal through the coordinator — under the
    /// shared commit mutex when one is configured, so a heal publish never interleaves a foreground persist — and
    /// captures the commit verdict and the round's block count for the caller. Carried as explicit state so the
    /// scrub turn's round-result callback is a bound method group rather than a closure.
    /// </summary>
    /// <param name="coordinator">The commit coordinator the heal is published through.</param>
    /// <param name="commitMutex">The in-process mutex the commit runs under, or <see langword="null"/> to run it unsynchronized.</param>
    /// <param name="correlationId">The correlation id the healed-generation marker carries.</param>
    /// <param name="roundSink">The optional per-round observation sink invoked before the commit, or <see langword="null"/>.</param>
    private sealed class RoundCommitStep(GenerationCommitCoordinator coordinator, Lock? commitMutex, Guid correlationId, ScrubRoundResultDelegate? roundSink)
    {
        /// <summary>The commit coordinator the heal is published through.</summary>
        private GenerationCommitCoordinator Coordinator { get; } = coordinator;

        /// <summary>The in-process mutex the commit runs under, or <see langword="null"/> to run it unsynchronized.</summary>
        private Lock? CommitMutex { get; } = commitMutex;

        /// <summary>The correlation id the healed-generation marker carries.</summary>
        private Guid CorrelationId { get; } = correlationId;

        /// <summary>The optional per-round observation sink invoked before the commit, or <see langword="null"/>.</summary>
        private ScrubRoundResultDelegate? RoundSink { get; } = roundSink;

        /// <summary>The commit verdict the round published, or <see langword="null"/> before the round handed its reports off.</summary>
        public GenerationCommitReport? Report { get; private set; }

        /// <summary>The round's protected-item count — the generation's total block count from the verify pass — used to scale the next interval.</summary>
        public long ItemCount { get; private set; }

        /// <summary>The scrub turn's round-result callback: records the round's block count, lets any observer see the verdicts before the publish, then publishes the heal under the commit mutex.</summary>
        /// <param name="verifyReport">The round's verify verdict.</param>
        /// <param name="repairReport">The round's repair verdict — the re-derived artifacts and named losses, or a refusal.</param>
        /// <param name="cancellationToken">The round's cancellation token, forwarded to the observation sink.</param>
        /// <returns>A task that completes when the observation sink and the commit are done.</returns>
        public async ValueTask HandleAsync(ScrubRoundReport verifyReport, RepairPassReport repairReport, CancellationToken cancellationToken)
        {
            ItemCount = verifyReport.BlocksVerified + verifyReport.CorruptBlocks.Count;

            //The observer sees the verify and repair verdicts BEFORE the publish; the report's pooled images are
            //still live here (the scrub turn disposes them after this returns), so the sink reads them synchronously.
            if(RoundSink is not null)
            {
                await RoundSink(verifyReport, repairReport, cancellationToken).ConfigureAwait(false);
            }

            Report = Commit(repairReport);
        }

        /// <summary>Publishes the heal, serializing it against a foreground persist under the shared mutex when one is configured. The persist and the commit are both synchronous, so a plain lock serializes their staging and rename windows.</summary>
        /// <param name="repairReport">The repair verdict to publish.</param>
        /// <returns>The commit verdict.</returns>
        private GenerationCommitReport Commit(RepairPassReport repairReport)
        {
            if(CommitMutex is null)
            {
                return Coordinator.Commit(repairReport, CorrelationId);
            }

            lock(CommitMutex)
            {
                return Coordinator.Commit(repairReport, CorrelationId);
            }
        }
    }
}
