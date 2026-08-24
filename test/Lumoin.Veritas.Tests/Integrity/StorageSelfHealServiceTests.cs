using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Database;
using Microsoft.Extensions.Time.Testing;
using static Lumoin.Veritas.Tests.Integrity.PersistenceStagingFixture;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The storage self-heal service: a background loop that verifies, repairs, and atomically re-publishes a store's
/// committed generation, driving the existing scrub turn, repair pass, and commit coordinator through one
/// deterministic round unit (<see cref="StorageSelfHealService.RunRoundAsync"/>) plus a cadence-driven loop. These
/// drive rounds directly (never timers) — rot injected deterministically, a fixed clock — so an end-to-end round
/// detects, heals, publishes, and names a loss; a clean or empty store commits nothing; a round fault does not kill
/// the loop; the shared commit mutex serializes a heal publish against a foreground persist; the jitter formula is
/// exact; and the engine wiring runs the loop over a store-opened database. The loop's own timing is tested only
/// through cancellation (an un-advanced fake clock) and the engine test's sink signal, never a real-time sleep.
/// </summary>
[TestClass]
internal sealed class StorageSelfHealServiceTests
{
    /// <summary>The triple count each generation stages (three ten-item system-of-record blocks).</summary>
    private const uint TripleCount = 30;

    /// <summary>The sketch symbol count each generation stages.</summary>
    private const int SketchSymbolCount = 40;

    /// <summary>The first committed generation a test stages; healing supersedes it with the next.</summary>
    private const long Generation = 7;

    /// <summary>The retained per-generation CURRENT-pointer window the service's manifest writer keeps.</summary>
    private const int RetainedPointers = 4;

    /// <summary>The system-of-record block count for the staged generation (<see cref="TripleCount"/> over ten items per block).</summary>
    private const int SegmentBlockCount = 3;

    /// <summary>The middle system-of-record block a corruption test flips.</summary>
    private const int CorruptBlock = 1;

    /// <summary>The test context, whose cancellation token bounds the sink and loop waits as the run's hang safety net.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A round over a generation whose single system-of-record block was lost but is parity-restorable heals it, publishes the next generation, republishes the restored system-of-record, names no loss, and leaves the store re-verifying clean.</summary>
    [TestMethod]
    public async Task EndToEndParityRestorableRotIsHealedAndPublishedWithoutLoss()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(TripleCount);
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(SketchSymbolCount, bytePool);
        using ArtifactImage parity = ParityImage(triples, bytePool);
        CorruptSegmentBlock(segment, CorruptBlock, SegmentBlockCount);
        FileSystemPersistenceStore store = StageGeneration(Generation, segment, sidecar, sketch, parity, bytePool, out string directory);
        try
        {
            FakeTimeProvider clock = new();
            using StorageSelfHealService service = Service(store, bytePool, triplePool, clock, new SelfHealOptions());

            GenerationCommitReport? report = await service.RunRoundAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(report);
            Assert.AreEqual(GenerationCommitOutcome.Committed, report.Outcome, "The parity-restorable rot heals and publishes.");
            Assert.AreEqual(Generation + 1, report.Generation, "The healed generation is the next after the damaged one.");
            Assert.Contains(ManifestFileRole.DataSegment, report.RepublishedRoles, "The parity-restored system-of-record is republished.");
            Assert.IsEmpty(report.NamedLosses, "A parity-restored block loses nothing.");
            Assert.AreEqual(Generation + 1, LiveGeneration(store), "The healed generation is live.");

            ScrubRoundReport reverify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, clock);
            Assert.IsTrue(reverify.IsClean, "The healed generation re-verifies clean.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A round over a generation whose system-of-record lost a block that no rung can restore, alongside a re-derivable damaged sidecar, heals the sidecar, publishes the generation carrying the named system-of-record loss, and writes a durable loss record readable after a cold reopen.</summary>
    [TestMethod]
    public async Task EndToEndUnrepairableRotIsHealedAndNamedInADurableLossRecord()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(TripleCount);
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(SketchSymbolCount, bytePool);
        CorruptSegmentBlock(segment, CorruptBlock, SegmentBlockCount);
        CorruptSidecarFrontMatter(sidecar);
        FileSystemPersistenceStore store = StageGeneration(Generation, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            FakeTimeProvider clock = new();
            using StorageSelfHealService service = Service(store, bytePool, triplePool, clock, new SelfHealOptions());

            GenerationCommitReport? report = await service.RunRoundAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(report);
            Assert.AreEqual(GenerationCommitOutcome.Committed, report.Outcome);
            Assert.Contains(ManifestFileRole.Sidecar, report.RepublishedRoles, "The re-derivable sidecar is healed and republished.");
            Assert.DoesNotContain(ManifestFileRole.DataSegment, report.RepublishedRoles, "The unrepairable system-of-record block is named lost, not re-derived.");
            Assert.IsNotEmpty(report.NamedLosses, "The unrepairable block is named as a loss on the committed heal.");

            DurableSystemOfRecordStore reopened = new(new FileSystemPersistenceStore(directory, NoOpBarrier), bytePool);
            DurableLossRecord? losses = reopened.TryReadRecordedLosses();
            Assert.IsNotNull(losses, "The healed generation's loss is durable across a cold reopen.");
            Assert.AreEqual(Generation + 1, losses.Generation, "The loss record names the healed generation.");
            Assert.IsNotEmpty(losses.Losses, "The loss record names the lost system-of-record range.");
            Assert.AreEqual(ManifestFileRole.DataSegment.Code, losses.Losses[0].RoleCode, "The named loss is the system-of-record's.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A round over a clean generation finds nothing to repair, so the coordinator commits nothing and the live generation is unchanged.</summary>
    [TestMethod]
    public async Task CleanStoreRoundCommitsNothing()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(TripleCount);
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(SketchSymbolCount, bytePool);
        FileSystemPersistenceStore store = StageGeneration(Generation, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            FakeTimeProvider clock = new();
            using StorageSelfHealService service = Service(store, bytePool, triplePool, clock, new SelfHealOptions());

            GenerationCommitReport? report = await service.RunRoundAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(report);
            Assert.AreEqual(GenerationCommitOutcome.NothingToCommit, report.Outcome, "A clean generation publishes nothing.");
            Assert.AreEqual(Generation, LiveGeneration(store), "The live generation is unchanged.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A round over a store holding no committed generation is a clean no-op: it returns no report and does not throw, so the loop continues.</summary>
    [TestMethod]
    public async Task EmptyStoreRoundIsANoOp()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        string directory = Directory.CreateTempSubdirectory("veritas-selfheal-empty-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            StorageTraceCapture trace = new();
            FakeTimeProvider clock = new();
            using StorageSelfHealService service = Service(store, bytePool, triplePool, clock, new SelfHealOptions(), trace.Capture);

            GenerationCommitReport? report = await service.RunRoundAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.IsNull(report, "An empty store yields no commit report.");
            Assert.DoesNotContain(StorageTraceEventKind.ScrubRoundFailed, trace.Events.Select(static e => e.Kind), "An empty store is a clean no-op, not a fault.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A round whose store read faults surfaces the fault (a recorded round-failed event plus the rethrown exception) rather than swallowing it, and the service is left healthy: a second round over the recovered store heals normally.</summary>
    [TestMethod]
    public async Task RoundExceptionDoesNotKillTheLoop()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(TripleCount);
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(SketchSymbolCount, bytePool);
        CorruptSidecarFrontMatter(sidecar);
        FileSystemPersistenceStore backing = StageGeneration(Generation, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            ThrowOnceOnFirstOpenImageStore store = new(backing);
            StorageTraceCapture trace = new();
            FakeTimeProvider clock = new();
            using StorageSelfHealService service = Service(store, bytePool, triplePool, clock, new SelfHealOptions(), trace.Capture);

            IOException? surfaced = null;
            try
            {
                await service.RunRoundAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch(IOException exception)
            {
                surfaced = exception;
            }

            Assert.IsNotNull(surfaced, "The first round surfaces the injected read fault rather than swallowing it.");
            Assert.Contains(StorageTraceEventKind.ScrubRoundFailed, trace.Events.Select(static e => e.Kind), "A faulted round records a round-failed event on the trace.");

            GenerationCommitReport? healed = await service.RunRoundAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.IsNotNull(healed);
            Assert.AreEqual(GenerationCommitOutcome.Committed, healed.Outcome, "The next round over the recovered store heals normally, so the fault left the service healthy.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>While a foreground holder holds the shared commit mutex, a concurrent round runs its verify and repair but cannot publish its heal — the live generation stays put until the mutex is released, after which the heal commits. Deterministic: the round's pre-commit sink signals it has reached the commit boundary, the assertion holds because a publish provably requires the mutex the test holds, and the commit is awaited only after release.</summary>
    [TestMethod]
    public async Task CommitMutexSerializesHealAgainstForegroundPersist()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(TripleCount);
        using ArtifactImage segment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        using ArtifactImage sketch = SketchImage(SketchSymbolCount, bytePool);
        CorruptSidecarFrontMatter(sidecar);
        FileSystemPersistenceStore store = StageGeneration(Generation, segment, sidecar, sketch, bytePool, out string directory);
        try
        {
            FakeTimeProvider clock = new();
            Lock commitMutex = new();
            RoundSink sink = new();
            SelfHealOptions options = new() { CommitMutex = commitMutex, OnRoundComplete = sink.Handle };
            using StorageSelfHealService service = Service(store, bytePool, triplePool, clock, options);

            //A foreground persist simulator holds the mutex on ONE dedicated thread (the lock is thread-affine, so
            //Enter and Exit must not straddle an await): it acquires, signals, blocks until released, then exits.
            using ManualResetEventSlim release = new(initialState: false);
            TaskCompletionSource acquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task persist = Task.Run(() =>
            {
                commitMutex.Enter();
                try
                {
                    acquired.SetResult();
                    release.Wait(TestContext.CancellationToken);
                }
                finally
                {
                    commitMutex.Exit();
                }
            }, TestContext.CancellationToken);

            await acquired.Task.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
            Task<GenerationCommitReport?> round = Task.Run(() => service.RunRoundAsync(CancellationToken.None).AsTask());
            try
            {
                //The round has verified, repaired, and fired its pre-commit sink; it is now heading into the commit,
                //which cannot publish while the simulator holds the mutex — so the live generation is unchanged.
                await sink.Observed.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(Generation, LiveGeneration(store), "No heal publishes while the foreground holder holds the commit mutex.");
            }
            finally
            {
                release.Set();
            }

            await persist.ConfigureAwait(false);
            GenerationCommitReport? report = await round.ConfigureAwait(false);

            Assert.IsNotNull(report);
            Assert.AreEqual(GenerationCommitOutcome.Committed, report.Outcome, "The heal commits once the mutex is released.");
            Assert.AreEqual(Generation + 1, LiveGeneration(store), "The healed generation is live only after the mutex is released.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The jittered per-round delay is the exact bound formula <c>interval * (1 - jitterFraction/2 + sample * jitterFraction)</c> clamped into the loop's schedulable range: the midpoint sample recovers the interval, the low and high samples span the band, a zero jitter fraction yields the interval unchanged, a multi-month reliability estimate clamps to the maximum loop delay (the Poisson model's ceiling is a year, the scheduler's timer tops out near 49.7 days — an unclamped delay would fault the loop), and a degenerate zero or negative estimate clamps to the minimum rather than spinning rounds hot.</summary>
    [TestMethod]
    public void JitteredIntervalUsesTheEstimatorAndJitterSeams()
    {
        TimeSpan interval = TimeSpan.FromHours(8);

        Assert.AreEqual(TimeSpan.FromHours(6), StorageSelfHealService.ComputeJitteredDelay(interval, 0.5, 0.0), "The low sample sits at interval*(1 - jitterFraction/2).");
        Assert.AreEqual(TimeSpan.FromHours(8), StorageSelfHealService.ComputeJitteredDelay(interval, 0.5, 0.5), "The midpoint sample recovers the interval.");
        Assert.AreEqual(TimeSpan.FromHours(10), StorageSelfHealService.ComputeJitteredDelay(interval, 0.5, 1.0), "The high sample sits at interval*(1 + jitterFraction/2).");
        Assert.AreEqual(interval, StorageSelfHealService.ComputeJitteredDelay(interval, 0.0, 0.7), "A zero jitter fraction yields the interval unchanged for any sample.");

        //The reliability model legitimately estimates up to a year for a small store (a handful of blocks maps to
        //the model's maximum cadence); the loop's single wait must stay under the scheduler's timer ceiling.
        TimeSpan yearly = ScrubCadenceEstimators.EstimatePoisson(new ScrubCadenceContext(MemoryIsProtected: false, ProtectedItemCount: 3));
        Assert.AreEqual(StorageSelfHealService.MaximumLoopDelay, StorageSelfHealService.ComputeJitteredDelay(yearly, 0.1, 1.0), "A multi-month estimate clamps to the maximum loop delay.");
        Assert.IsLessThan(TimeSpan.FromDays(49), StorageSelfHealService.MaximumLoopDelay, "The maximum loop delay sits under the scheduler's ~49.7-day timer ceiling.");
        Assert.AreEqual(StorageSelfHealService.MinimumLoopDelay, StorageSelfHealService.ComputeJitteredDelay(TimeSpan.Zero, 0.1, 0.0), "A zero estimate clamps to the minimum rather than spinning hot.");
        Assert.AreEqual(StorageSelfHealService.MinimumLoopDelay, StorageSelfHealService.ComputeJitteredDelay(TimeSpan.FromHours(-1), 0.1, 0.5), "A negative estimate clamps to the minimum.");
    }

    /// <summary>The loop stops cleanly when its token is cancelled: cancelling before the fake clock advances (so no round runs) completes the loop promptly by cancellation rather than hanging.</summary>
    [TestMethod]
    public async Task LoopStopsCleanlyOnCancellation()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        string directory = Directory.CreateTempSubdirectory("veritas-selfheal-loop-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            FakeTimeProvider clock = new();
            using StorageSelfHealService service = Service(store, bytePool, triplePool, clock, new SelfHealOptions());
            using CancellationTokenSource cancellation = new();

            Task loop = service.RunAsync(cancellation.Token);
            await cancellation.CancelAsync().ConfigureAwait(false);

            OperationCanceledException? stopped = null;
            try
            {
                await loop.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException exception)
            {
                stopped = exception;
            }

            Assert.IsNotNull(stopped, "The loop stops by cancellation.");
            Assert.IsTrue(loop.IsCompleted, "The loop completes promptly on cancellation rather than hanging.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Opening a store-backed database with a self-heal policy runs the loop over that store, scheduled on the injected engine clock: the loop's first delay-timer registration is the arm signal, one fake-clock advance of exactly the minimum loop delay releases it, and the background round detects the staged rot, heals it, and atomically re-publishes — observed through the storage-trace healed-generation event. Disposing the database stops the loop cleanly, leaving the healed generation live. No wait in the row rides real time.</summary>
    [TestMethod]
    public async Task EngineWiringRunsTheSelfHealLoopOverTheStore()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        EncodedTriple[] triples = SampleTriples(TripleCount);
        TermDictionary dictionary = SampleDictionary(TripleCount);
        using ArtifactImage dictionaryImage = DictionaryImage(dictionary, blockTermCount: 8, bytePool);
        using ArtifactImage dataSegment = SegmentImage(triples, bytePool);
        using ArtifactImage sidecar = SidecarImage(triples, bytePool);
        CorruptSidecarFrontMatter(sidecar);
        FileSystemPersistenceStore store = StageProductionGeneration(Generation, dictionaryImage, dataSegment, [], sidecar, bytePool, out string directory);
        try
        {
            HealedTraceSignal healed = new();
            FakeTimeProvider fake = new();
            TimerArmSignalingTimeProvider clock = new(fake);
            SelfHealOptions selfHeal = new() { CadenceEstimator = static _ => TimeSpan.FromMilliseconds(5), JitterFraction = 0.0 };
            VeritasEngineOptions options = new() { Reasoning = null, SelfHeal = selfHeal, StorageTrace = healed.Capture, Clock = clock };

            VeritasEngine engine = await VeritasEngine.OpenAsync(store, options, TestContext.CancellationToken).ConfigureAwait(false);
            try
            {
                //Signal-at-the-transition: the loop arming its round delay IS the transition, observed at
                //the clock's first timer registration. The requested 5ms cadence floors at the service's
                //minimum loop delay, so one advance of exactly that delay releases the one armed timer.
                await clock.TimerArmed.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
                fake.Advance(StorageSelfHealService.MinimumLoopDelay);

                //The sink signal is the completion condition, awaited bound only by the test cancellation
                //token — a loop that never heals blocks here and surfaces at the runner-level hang guard.
                await healed.Healed.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(Generation + 1, healed.HealedGeneration, "The background loop healed and published the next generation.");
            }
            finally
            {
                await engine.DisposeAsync().ConfigureAwait(false);
            }

            //A second disposal is a no-op: the self-heal runtime guards its cancellation source and pools.
            await engine.DisposeAsync().ConfigureAwait(false);

            Assert.AreEqual(Generation + 1, LiveGeneration(store), "The healed generation stays live after the database is disposed.");

            //The published heal is genuinely clean: a fresh verify pass over the healed generation finds nothing.
            ScrubRoundReport reverify = ScrubRound.RunVerifyPass(store, null, null, Guid.Empty, TimeProvider.System);
            Assert.IsTrue(reverify.CorruptBlocks.Count == 0 && !reverify.IsDegradedSnapshot, "The healed generation re-verifies clean.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Builds a self-heal service over the store under the fixture's XxHash3 repair configuration and the given policy.</summary>
    /// <param name="store">The store the service heals.</param>
    /// <param name="bytePool">The byte pool the repair and commit rent from.</param>
    /// <param name="triplePool">The triple pool the repair feed rents from.</param>
    /// <param name="clock">The clock the rounds timestamp with.</param>
    /// <param name="options">The run policy.</param>
    /// <param name="trace">The diagnostics sink, or <see langword="null"/>.</param>
    /// <returns>The service.</returns>
    private static StorageSelfHealService Service(PersistenceStore store, MemoryPool<byte> bytePool, MemoryPool<EncodedTriple> triplePool, TimeProvider clock, SelfHealOptions options, TraceHandler<StorageTraceEvent>? trace = null)
    {
        return new StorageSelfHealService(store, RepairConfig(bytePool, triplePool), ChecksumAlgorithm.XxHash3, bytePool, RetainedPointers, null, trace, clock, options);
    }

    /// <summary>Recovers the live committed generation of a store.</summary>
    /// <param name="store">The store to recover.</param>
    /// <returns>The live commit generation.</returns>
    private static long LiveGeneration(PersistenceStore store)
    {
        return new ManifestRecovery(store).Recover().Manifest.CommitGeneration;
    }

    /// <summary>Captures a round's verify and repair verdicts through a method group and signals a task the first time it observes a round, so a test holds no closure — the pre-commit observation the mutex test waits on.</summary>
    private sealed class RoundSink
    {
        /// <summary>Completed the first time a round is observed (before its heal commits).</summary>
        private TaskCompletionSource ObservedSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>A task completing the first time a round is observed.</summary>
        public Task Observed => ObservedSource.Task;

        /// <summary>The round-result handler entry point: signals the observation, before the round's heal commits.</summary>
        /// <param name="verifyReport">The round's verify verdict.</param>
        /// <param name="repairReport">The round's repair verdict.</param>
        /// <param name="cancellationToken">The round's cancellation token.</param>
        /// <returns>A completed task.</returns>
        public ValueTask Handle(ScrubRoundReport verifyReport, RepairPassReport repairReport, CancellationToken cancellationToken)
        {
            _ = ObservedSource.TrySetResult();

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Captures the storage trace through a method group and signals a task the first time a healed-generation event is seen, recording its generation, so the engine test holds no closure and waits on a deterministic heal signal.</summary>
    private sealed class HealedTraceSignal
    {
        /// <summary>Completed the first time a healed-generation event is captured.</summary>
        private TaskCompletionSource HealedSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>A task completing the first time a healed-generation event is captured.</summary>
        public Task Healed => HealedSource.Task;

        /// <summary>The healed generation the first healed-generation event named, or 0 before one is seen.</summary>
        public long HealedGeneration { get; private set; }

        /// <summary>The trace handler entry point: records and signals the first healed-generation event.</summary>
        /// <param name="evt">The emitted event.</param>
        public void Capture(in StorageTraceEvent evt)
        {
            if(evt.Kind == StorageTraceEventKind.GenerationHealed)
            {
                HealedGeneration = evt.CommitGeneration;
                _ = HealedSource.TrySetResult();
            }
        }
    }

    /// <summary>
    /// A clock that delegates every operation to an inner fake and signals a task when a timer
    /// is first registered — the deterministic transition marker for "the self-heal loop armed
    /// its round delay", after which one fake advance releases that delay. The signal is
    /// idempotent (<see cref="TaskCompletionSource.TrySetResult"/>) because the loop registers
    /// a fresh timer every round for its whole lifetime; later registrations park un-advanced
    /// until disposal cancels the loop.
    /// </summary>
    private sealed class TimerArmSignalingTimeProvider(FakeTimeProvider inner): TimeProvider
    {
        /// <summary>The fake the timers and readings delegate to.</summary>
        private FakeTimeProvider Inner { get; } = inner;

        /// <summary>Completed at the first timer registration.</summary>
        private TaskCompletionSource ArmedSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>A task completing when the first timer has been registered on this clock.</summary>
        public Task TimerArmed => ArmedSource.Task;

        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow()
        {
            return Inner.GetUtcNow();
        }

        /// <inheritdoc/>
        public override TimeZoneInfo LocalTimeZone => Inner.LocalTimeZone;

        /// <inheritdoc/>
        public override long TimestampFrequency => Inner.TimestampFrequency;

        /// <inheritdoc/>
        public override long GetTimestamp()
        {
            return Inner.GetTimestamp();
        }

        /// <summary>Registers the timer on the inner fake, then signals the first registration.</summary>
        /// <param name="callback">The timer callback.</param>
        /// <param name="state">The callback state.</param>
        /// <param name="dueTime">The due time.</param>
        /// <param name="period">The period.</param>
        /// <returns>The inner fake's timer.</returns>
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ITimer timer = Inner.CreateTimer(callback, state, dueTime, period);
            _ = ArmedSource.TrySetResult();

            return timer;
        }
    }

    /// <summary>A store decorator that throws once on the first image open — modelling a transient read fault mid-round — then delegates every operation to the inner store, so the next round reads it whole.</summary>
    /// <param name="inner">The inner store every operation delegates to.</param>
    private sealed class ThrowOnceOnFirstOpenImageStore(PersistenceStore inner): PersistenceStore
    {
        /// <summary>The inner store every operation delegates to.</summary>
        private PersistenceStore Inner { get; } = inner;

        /// <summary>The number of image opens seen; the first throws, the rest delegate.</summary>
        private int opens;

        /// <inheritdoc/>
        public override void WriteStaged(string name, ReadOnlySpan<byte> content)
        {
            Inner.WriteStaged(name, content);
        }

        /// <inheritdoc/>
        public override void Publish(string stagedName, string finalName)
        {
            Inner.Publish(stagedName, finalName);
        }

        /// <inheritdoc/>
        public override byte[]? Read(string name)
        {
            return Inner.Read(name);
        }

        /// <inheritdoc/>
        public override SegmentImageSource? OpenImage(string name)
        {
            if(Interlocked.Increment(ref opens) == 1)
            {
                throw new IOException("Injected transient read fault.");
            }

            return Inner.OpenImage(name);
        }

        /// <inheritdoc/>
        public override PooledSegmentImageSource? OpenPooledImage(string name, MemoryPool<byte> pool)
        {
            return Inner.OpenPooledImage(name, pool);
        }

        /// <inheritdoc/>
        public override IReadOnlyList<string> List(string prefix)
        {
            return Inner.List(prefix);
        }

        /// <inheritdoc/>
        public override void Delete(string name)
        {
            Inner.Delete(name);
        }
    }
}
