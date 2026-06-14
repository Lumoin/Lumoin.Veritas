using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// Load-time knobs that parameterise how the library uses a host's
/// compute, memory-protection, and persistence resources — the
/// deliberate sibling of <c>QueryEnginePolicy</c> and
/// <c>ReasoningPolicy</c> (same idiom: a <see langword="readonly"/>
/// <see langword="record"/> <see langword="struct"/> with a static
/// <see cref="Default"/>, governing fully dynamic per-request
/// behaviour rather than performing it). Every knob defaults to
/// <em>derive</em> or <em>auto</em>, so <see cref="Default"/> is
/// correct everywhere from a browser WebAssembly runtime to a
/// 64-core container without per-host tuning.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Resolve()"/> turns this intent into a
/// <see cref="ResolvedExecutionPlan"/> of concrete facts by observing
/// the running environment once. The split is deliberate: this type is
/// the operator-facing surface (mostly "auto"); the plan is the
/// resolved numbers and backend families the compute lane, host
/// hardening, and persistence layers consume.
/// </para>
/// <para>
/// As with the sibling policies, <see cref="Default"/> — not
/// <see langword="default"/>(<see cref="ExecutionPolicy"/>) — is the
/// canonical starting point: a zero-initialised value would read the
/// host pool floor as disabled rather than at its transitional
/// default. Build on <see cref="Default"/> with a <c>with</c>
/// expression to change individual knobs.
/// </para>
/// </remarks>
/// <param name="ComputeLaneWorkers">Worker threads on the compute lane; <c>0</c> derives one core of headroom below the observed CPU budget. An explicit value is honoured as operator intent except on the browser, which is always a single cooperative thread.</param>
/// <param name="ComputeQueueCapacity">The bounded depth of the compute lane's work queue, beyond which admission sheds; <c>0</c> derives a small multiple of the worker count.</param>
/// <param name="HostPoolFloorMultiplier">The thread-pool minimum floor applied at host startup, as a multiple of the processor count; <c>0</c> disables it. A transitional patch-fix that compensated for CPU work mis-placed on the I/O pool — now shipped lifted (default <c>0</c>): the compute lane moves the build CPU off the serve pool, so a measured floor-lift gate retired the floor as the default. Set a positive multiplier to re-enable it. The browser, which has no pool to floor, always resolves to <c>0</c>.</param>
/// <param name="KernelWidthCap">Caps the SIMD codec ladder below hardware capability; <see cref="Execution.KernelWidthCap.Auto"/> leaves it uncapped.</param>
/// <param name="EccAssumption">The operator's stance on hardware memory error correction, which scales verify-on-load and scrub cadence; <see cref="MemoryProtectionAssumption.AutoDetect"/> probes and defaults conservatively to unprotected when inconclusive.</param>
/// <param name="ScrubCadence">An explicit override of the scrub-initiation cadence. <c>null</c> defers to <paramref name="ScrubCadenceEstimator"/>; <see cref="TimeSpan.Zero"/> disables scrubbing; a positive value sets it explicitly. The realised per-block coverage latency is load-dependent and read from telemetry, not this knob.</param>
/// <param name="ColumnAccessMode">How the persistence layer reaches column bytes; <see cref="Execution.ColumnAccessMode.Auto"/> memory-maps local files where the OS supports it and range-streams otherwise.</param>
/// <param name="ScrubCadenceEstimator">The configurable function that derives the scrub-initiation cadence from the protection, data-size, and cluster context; <c>null</c> uses <see cref="ScrubCadenceEstimators.Default"/>. Consulted only when <paramref name="ScrubCadence"/> is <c>null</c>.</param>
[DebuggerDisplay("ExecutionPolicy Workers={ComputeLaneWorkers} Queue={ComputeQueueCapacity} Floor={HostPoolFloorMultiplier} Cap={KernelWidthCap} Ecc={EccAssumption} Scrub={ScrubCadence} Access={ColumnAccessMode}")]
public readonly record struct ExecutionPolicy(
    int ComputeLaneWorkers = 0,
    int ComputeQueueCapacity = 0,
    int HostPoolFloorMultiplier = 0,
    KernelWidthCap KernelWidthCap = KernelWidthCap.Auto,
    MemoryProtectionAssumption EccAssumption = MemoryProtectionAssumption.AutoDetect,
    TimeSpan? ScrubCadence = null,
    ColumnAccessMode ColumnAccessMode = ColumnAccessMode.Auto,
    ScrubCadenceEstimatorDelegate? ScrubCadenceEstimator = null)
{
    /// <summary>The derived compute-queue depth per worker when <see cref="ComputeQueueCapacity"/> is left to derive. A placeholder constant until characterised under a real serve load.</summary>
    private const int DerivedQueueDepthPerWorker = 4;

    /// <summary>
    /// The default policy: every knob derives from the observed
    /// environment, so the same value is correct from a browser
    /// WebAssembly runtime to a many-core container. The compute lane
    /// sizes to one core of headroom below the CPU budget, the host
    /// pool floor sits at its transitional default, memory protection
    /// is probed, and column access and the SIMD ladder follow the
    /// hardware.
    /// </summary>
    public static ExecutionPolicy Default { get; } = new(
        ComputeLaneWorkers: 0,
        ComputeQueueCapacity: 0,
        HostPoolFloorMultiplier: 0,
        KernelWidthCap: KernelWidthCap.Auto,
        EccAssumption: MemoryProtectionAssumption.AutoDetect,
        ScrubCadence: null,
        ColumnAccessMode: ColumnAccessMode.Auto,
        ScrubCadenceEstimator: null);

    /// <summary>
    /// Resolves this policy against the real running environment,
    /// observed once, into a <see cref="ResolvedExecutionPlan"/> of
    /// concrete facts.
    /// </summary>
    /// <returns>The resolved plan.</returns>
    internal ResolvedExecutionPlan Resolve()
    {
        return Resolve(ExecutionEnvironment.Observe());
    }

    /// <summary>
    /// Resolves this policy against a given environment snapshot — the
    /// pure core of <see cref="Resolve()"/>, total in policy and
    /// environment so every derivation branch is deterministically
    /// exercisable.
    /// </summary>
    /// <param name="environment">The observed runtime facts.</param>
    /// <returns>The resolved plan.</returns>
    internal ResolvedExecutionPlan Resolve(ExecutionEnvironment environment)
    {
        int budget = ResolveProcessorBudget(environment);
        int workers = ResolveComputeLaneWorkers(environment, budget);
        int queueCapacity = ComputeQueueCapacity > 0 ? ComputeQueueCapacity : workers * DerivedQueueDepthPerWorker;
        int floorMultiplier = environment.IsBrowser ? 0 : HostPoolFloorMultiplier;
        ResolvedProtectionState protection = ResolveProtection(environment);

        return new ResolvedExecutionPlan(
            ObservedProcessorBudget: budget,
            ComputeLaneWorkers: workers,
            ComputeQueueCapacity: queueCapacity,
            HostPoolFloorMultiplier: floorMultiplier,
            KernelWidthCap: KernelWidthCap,
            Protection: protection,
            ScrubCadence: ResolveScrubCadence(protection),
            ColumnAccessMode: ResolveColumnAccess(environment));
    }

    /// <summary>
    /// Derives the effective core budget: the cgroup CPU quota when one
    /// constrains the group (capped by the physical processor count),
    /// otherwise the processor count, floored at one whole core.
    /// </summary>
    /// <param name="environment">The observed runtime facts.</param>
    /// <returns>The effective core budget, at least one.</returns>
    private static int ResolveProcessorBudget(ExecutionEnvironment environment)
    {
        if(environment.IsBrowser)
        {
            return 1;
        }

        if(environment.CpuQuotaCores is double quotaCores)
        {
            //A fractional quota floors at one whole core; a whole-plus-fractional quota truncates
            //down (never over-subscribe the budget) and can never exceed the physical core count.
            int wholeQuotaCores = (int)Math.Floor(quotaCores);

            return Math.Max(1, Math.Min(wholeQuotaCores, environment.ProcessorCount));
        }

        return Math.Max(1, environment.ProcessorCount);
    }

    /// <summary>
    /// Derives the compute-lane width: an explicit override honoured as
    /// operator intent, or one core of headroom below the budget so the
    /// latency-sensitive serve pool is not starved. The browser is
    /// always a single cooperative thread regardless of any override.
    /// </summary>
    /// <param name="environment">The observed runtime facts.</param>
    /// <param name="budget">The resolved core budget.</param>
    /// <returns>The compute-lane worker count, at least one.</returns>
    private int ResolveComputeLaneWorkers(ExecutionEnvironment environment, int budget)
    {
        if(environment.IsBrowser)
        {
            return 1;
        }

        return ComputeLaneWorkers > 0 ? ComputeLaneWorkers : Math.Max(1, budget - 1);
    }

    /// <summary>
    /// Resolves the memory-protection verdict: an operator assertion
    /// forces it; otherwise the probe reading decides, with an
    /// inconclusive reading defaulting conservatively to unprotected.
    /// </summary>
    /// <param name="environment">The observed runtime facts.</param>
    /// <returns>The resolved protection state.</returns>
    private ResolvedProtectionState ResolveProtection(ExecutionEnvironment environment)
    {
        return EccAssumption switch
        {
            MemoryProtectionAssumption.AssumeProtected => new ResolvedProtectionState(true, ProtectionDetectionSource.AssumedByPolicy),
            MemoryProtectionAssumption.AssumeUnprotected => new ResolvedProtectionState(false, ProtectionDetectionSource.AssumedByPolicy),
            _ => ResolveProbedProtection(environment.MemoryErrorCorrectionDetected),
        };
    }

    /// <summary>
    /// Maps a hardware probe reading to a protection verdict: an
    /// affirmative reading is trusted, and an inconclusive reading
    /// (<c>null</c>) defaults to unprotected — verify more, not less.
    /// </summary>
    /// <param name="memoryErrorCorrectionDetected">The probe reading.</param>
    /// <returns>The resolved protection state.</returns>
    private static ResolvedProtectionState ResolveProbedProtection(bool? memoryErrorCorrectionDetected)
    {
        return memoryErrorCorrectionDetected switch
        {
            true => new ResolvedProtectionState(true, ProtectionDetectionSource.Probed),
            false => new ResolvedProtectionState(false, ProtectionDetectionSource.Probed),
            null => new ResolvedProtectionState(false, ProtectionDetectionSource.UnknownDefaulted),
        };
    }

    /// <summary>
    /// Resolves the scrub-initiation cadence: an explicit value (a
    /// positive cadence, or <see cref="TimeSpan.Zero"/> to disable) is
    /// taken verbatim; otherwise it derives from the resolved
    /// protection state.
    /// </summary>
    /// <param name="protection">The resolved protection state.</param>
    /// <returns>The resolved scrub cadence; <see cref="TimeSpan.Zero"/> when disabled.</returns>
    private TimeSpan ResolveScrubCadence(ResolvedProtectionState protection)
    {
        if(ScrubCadence is TimeSpan requested)
        {
            return requested;
        }

        //Only the protection verdict is known at resolve time; the data size and the
        //cluster constellation are runtime and persistence facts the scrub scheduler
        //re-estimates with later. The estimator is configurable; the default is the
        //Poisson reliability model.
        ScrubCadenceEstimatorDelegate estimator = ScrubCadenceEstimator ?? ScrubCadenceEstimators.Default;

        return estimator(new ScrubCadenceContext(
            MemoryIsProtected: protection.MemoryIsProtected,
            ProtectedReplicaCount: protection.MemoryIsProtected ? 1 : 0));
    }

    /// <summary>
    /// Resolves the column byte-source family: an explicit mode is taken
    /// verbatim; <see cref="Execution.ColumnAccessMode.Auto"/>
    /// memory-maps where a mappable local file exists and range-streams
    /// on the browser, where it does not.
    /// </summary>
    /// <param name="environment">The observed runtime facts.</param>
    /// <returns>A concrete access mode — never <see cref="Execution.ColumnAccessMode.Auto"/>.</returns>
    private ColumnAccessMode ResolveColumnAccess(ExecutionEnvironment environment)
    {
        ColumnAccessMode requested = ColumnAccessMode;

        return requested switch
        {
            ColumnAccessMode.MemoryMapped => ColumnAccessMode.MemoryMapped,
            ColumnAccessMode.Streamed => ColumnAccessMode.Streamed,
            _ => environment.IsBrowser ? ColumnAccessMode.Streamed : ColumnAccessMode.MemoryMapped,
        };
    }
}
