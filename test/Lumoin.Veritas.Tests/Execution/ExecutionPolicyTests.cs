using System;
using Lumoin.Veritas.Core.Execution;

namespace Lumoin.Veritas.Tests.Execution;

/// <summary>
/// The execution policy's resolution contract: <see cref="ExecutionPolicy.Default"/>
/// is the all-derive starting point, and <see cref="ExecutionPolicy.Resolve(ExecutionEnvironment)"/>
/// is a total function of policy plus observed environment — so the
/// derivation is pinned across its boundaries (lane-width formula, CPU
/// quota, the browser degeneracy, the memory-protection matrix, scrub
/// cadence, column access, and the SIMD width cap) without depending on
/// the host the suite happens to run on.
/// </summary>
[TestClass]
internal sealed class ExecutionPolicyTests
{
    /// <summary>Builds an environment snapshot, defaulting to an eight-core host with no CPU quota, not a browser, and an inconclusive memory-protection probe.</summary>
    /// <param name="processorCount">The logical processor count.</param>
    /// <param name="cpuQuotaCores">The cgroup effective core budget, or <c>null</c> for no quota.</param>
    /// <param name="isBrowser">Whether the host is a browser WebAssembly runtime.</param>
    /// <param name="ecc">The memory error-correction probe reading: <c>true</c>, <c>false</c>, or <c>null</c> for inconclusive.</param>
    /// <returns>The environment snapshot.</returns>
    private static ExecutionEnvironment Env(int processorCount = 8, double? cpuQuotaCores = null, bool isBrowser = false, bool? ecc = null)
    {
        return new ExecutionEnvironment(processorCount, cpuQuotaCores, isBrowser, ecc);
    }

    [TestMethod]
    public void DefaultPolicyDerivesEverythingAndPinsTheTransitionalFloor()
    {
        ExecutionPolicy policy = ExecutionPolicy.Default;

        //Every knob is at its derive/auto sentinel, including the host pool floor: the floor-lift
        //gate retired it once the lane moved the build CPU off the serve pool, so it ships lifted at zero.
        Assert.AreEqual(0, policy.ComputeLaneWorkers);
        Assert.AreEqual(0, policy.ComputeQueueCapacity);
        Assert.AreEqual(0, policy.HostPoolFloorMultiplier);
        Assert.AreEqual(KernelWidthCap.Auto, policy.KernelWidthCap);
        Assert.AreEqual(MemoryProtectionAssumption.AutoDetect, policy.EccAssumption);
        Assert.IsNull(policy.ScrubCadence);
        Assert.AreEqual(ColumnAccessMode.Auto, policy.ColumnAccessMode);
    }

    [TestMethod]
    public void MultiCoreBudgetLeavesOneCoreOfHeadroomForTheServePool()
    {
        ResolvedExecutionPlan plan = ExecutionPolicy.Default.Resolve(Env(processorCount: 8));

        Assert.AreEqual(8, plan.ObservedProcessorBudget);
        Assert.AreEqual(7, plan.ComputeLaneWorkers);
        Assert.AreEqual(28, plan.ComputeQueueCapacity);
        Assert.AreEqual(0, plan.HostPoolFloorMultiplier);
    }

    [TestMethod]
    public void SingleAndDualCoreBudgetsFloorTheLaneAtOneWorker()
    {
        //max(1, budget - 1): a one-core budget degenerates to a single interleaving worker,
        //and a two-core budget keeps one core for the serve pool.
        Assert.AreEqual(1, ExecutionPolicy.Default.Resolve(Env(processorCount: 1)).ComputeLaneWorkers);
        Assert.AreEqual(1, ExecutionPolicy.Default.Resolve(Env(processorCount: 2)).ComputeLaneWorkers);
    }

    [TestMethod]
    public void CpuQuotaCapsTheBudgetBelowThePhysicalCoreCount()
    {
        ResolvedExecutionPlan plan = ExecutionPolicy.Default.Resolve(Env(processorCount: 16, cpuQuotaCores: 4.0));

        Assert.AreEqual(4, plan.ObservedProcessorBudget);
        Assert.AreEqual(3, plan.ComputeLaneWorkers);
    }

    [TestMethod]
    public void FractionalCpuQuotaFloorsTheBudgetAtOneWholeCore()
    {
        ResolvedExecutionPlan plan = ExecutionPolicy.Default.Resolve(Env(processorCount: 16, cpuQuotaCores: 0.5));

        Assert.AreEqual(1, plan.ObservedProcessorBudget);
        Assert.AreEqual(1, plan.ComputeLaneWorkers);
    }

    [TestMethod]
    public void WholePlusFractionalQuotaTruncatesDownNeverOverSubscribing()
    {
        //Three-and-nine-tenths cores of quota yield three whole lanes of budget, never four.
        ResolvedExecutionPlan plan = ExecutionPolicy.Default.Resolve(Env(processorCount: 16, cpuQuotaCores: 3.9));

        Assert.AreEqual(3, plan.ObservedProcessorBudget);
        Assert.AreEqual(2, plan.ComputeLaneWorkers);
    }

    [TestMethod]
    public void CpuQuotaAboveThePhysicalCoreCountIsCappedAtIt()
    {
        ResolvedExecutionPlan plan = ExecutionPolicy.Default.Resolve(Env(processorCount: 8, cpuQuotaCores: 32.0));

        Assert.AreEqual(8, plan.ObservedProcessorBudget);
        Assert.AreEqual(7, plan.ComputeLaneWorkers);
    }

    [TestMethod]
    public void ExplicitWorkerAndQueueOverridesAreHonouredAsOperatorIntent()
    {
        ExecutionPolicy policy = ExecutionPolicy.Default with { ComputeLaneWorkers = 16, ComputeQueueCapacity = 100 };
        ResolvedExecutionPlan plan = policy.Resolve(Env(processorCount: 8));

        Assert.AreEqual(16, plan.ComputeLaneWorkers);
        Assert.AreEqual(100, plan.ComputeQueueCapacity);
    }

    [TestMethod]
    public void BrowserIsAlwaysOneCooperativeThreadWithNoPoolFloor()
    {
        //An explicit worker override cannot create threads the single cooperative runtime lacks,
        //the pool floor is meaningless with no pool, and there is no file to memory-map.
        ExecutionPolicy policy = ExecutionPolicy.Default with { ComputeLaneWorkers = 8 };
        ResolvedExecutionPlan plan = policy.Resolve(Env(processorCount: 8, isBrowser: true));

        Assert.AreEqual(1, plan.ObservedProcessorBudget);
        Assert.AreEqual(1, plan.ComputeLaneWorkers);
        Assert.AreEqual(0, plan.HostPoolFloorMultiplier);
        Assert.AreEqual(ColumnAccessMode.Streamed, plan.ColumnAccessMode);
    }

    [TestMethod]
    public void AutoDetectWithAnInconclusiveProbeDefaultsToUnprotected()
    {
        //Decision 7: unknown resolves to unprotected — verify more, not less.
        ResolvedExecutionPlan plan = ExecutionPolicy.Default.Resolve(Env(ecc: null));

        Assert.IsFalse(plan.Protection.MemoryIsProtected);
        Assert.AreEqual(ProtectionDetectionSource.UnknownDefaulted, plan.Protection.Source);
    }

    [TestMethod]
    public void AutoDetectTrustsAnAffirmativeProbeReadingEitherWay()
    {
        ResolvedProtectionState detectedProtected = ExecutionPolicy.Default.Resolve(Env(ecc: true)).Protection;
        Assert.IsTrue(detectedProtected.MemoryIsProtected);
        Assert.AreEqual(ProtectionDetectionSource.Probed, detectedProtected.Source);

        ResolvedProtectionState detectedUnprotected = ExecutionPolicy.Default.Resolve(Env(ecc: false)).Protection;
        Assert.IsFalse(detectedUnprotected.MemoryIsProtected);
        Assert.AreEqual(ProtectionDetectionSource.Probed, detectedUnprotected.Source);
    }

    [TestMethod]
    public void OperatorAssertionOverridesTheProbeEitherWay()
    {
        ResolvedProtectionState assumedProtected =
            (ExecutionPolicy.Default with { EccAssumption = MemoryProtectionAssumption.AssumeProtected }).Resolve(Env(ecc: false)).Protection;
        Assert.IsTrue(assumedProtected.MemoryIsProtected);
        Assert.AreEqual(ProtectionDetectionSource.AssumedByPolicy, assumedProtected.Source);

        ResolvedProtectionState assumedUnprotected =
            (ExecutionPolicy.Default with { EccAssumption = MemoryProtectionAssumption.AssumeUnprotected }).Resolve(Env(ecc: true)).Protection;
        Assert.IsFalse(assumedUnprotected.MemoryIsProtected);
        Assert.AreEqual(ProtectionDetectionSource.AssumedByPolicy, assumedUnprotected.Source);
    }

    [TestMethod]
    public void DerivedScrubCadenceIsHeavierWhenMemoryIsUnprotected()
    {
        //The inconclusive AutoDetect default resolves unprotected, which scrubs more often than
        //asserted-protected memory.
        TimeSpan unprotected = ExecutionPolicy.Default.Resolve(Env(ecc: null)).ScrubCadence;
        TimeSpan protectedCadence =
            (ExecutionPolicy.Default with { EccAssumption = MemoryProtectionAssumption.AssumeProtected }).Resolve(Env()).ScrubCadence;

        Assert.IsGreaterThan(TimeSpan.Zero, unprotected);
        Assert.IsGreaterThan(unprotected, protectedCadence);
    }

    [TestMethod]
    public void ExplicitScrubCadenceIsTakenVerbatimIncludingZeroToDisable()
    {
        TimeSpan explicitCadence = TimeSpan.FromMinutes(30);
        Assert.AreEqual(explicitCadence, (ExecutionPolicy.Default with { ScrubCadence = explicitCadence }).Resolve(Env()).ScrubCadence);

        //Zero disables scrubbing and is distinct from the null derive sentinel.
        Assert.AreEqual(TimeSpan.Zero, (ExecutionPolicy.Default with { ScrubCadence = TimeSpan.Zero }).Resolve(Env()).ScrubCadence);
    }

    [TestMethod]
    public void AutoColumnAccessMapsLocallyAndStreamsOnTheBrowser()
    {
        Assert.AreEqual(ColumnAccessMode.MemoryMapped, ExecutionPolicy.Default.Resolve(Env(isBrowser: false)).ColumnAccessMode);
        Assert.AreEqual(ColumnAccessMode.Streamed, ExecutionPolicy.Default.Resolve(Env(isBrowser: true)).ColumnAccessMode);
    }

    [TestMethod]
    public void ExplicitColumnAccessIsTakenVerbatim()
    {
        Assert.AreEqual(
            ColumnAccessMode.Streamed,
            (ExecutionPolicy.Default with { ColumnAccessMode = ColumnAccessMode.Streamed }).Resolve(Env(isBrowser: false)).ColumnAccessMode);
        Assert.AreEqual(
            ColumnAccessMode.MemoryMapped,
            (ExecutionPolicy.Default with { ColumnAccessMode = ColumnAccessMode.MemoryMapped }).Resolve(Env(isBrowser: true)).ColumnAccessMode);
    }

    [TestMethod]
    public void KernelWidthCapPassesThroughToThePlanUnchanged()
    {
        Assert.AreEqual(KernelWidthCap.Auto, ExecutionPolicy.Default.Resolve(Env()).KernelWidthCap);
        Assert.AreEqual(KernelWidthCap.Portable, (ExecutionPolicy.Default with { KernelWidthCap = KernelWidthCap.Portable }).Resolve(Env()).KernelWidthCap);
        Assert.AreEqual(KernelWidthCap.Bits256, (ExecutionPolicy.Default with { KernelWidthCap = KernelWidthCap.Bits256 }).Resolve(Env()).KernelWidthCap);
    }

    [TestMethod]
    public void ObserveReflectsTheRealHost()
    {
        ExecutionEnvironment environment = ExecutionEnvironment.Observe();

        Assert.AreEqual(Environment.ProcessorCount, environment.ProcessorCount);
        Assert.AreEqual(OperatingSystem.IsBrowser(), environment.IsBrowser);
    }

    [TestMethod]
    public void ResolveOnTheRealHostProducesAnInternallyConsistentPlan()
    {
        //The parameterless overload reads the real environment; the plan is consistent on any host.
        ResolvedExecutionPlan plan = ExecutionPolicy.Default.Resolve();

        Assert.IsGreaterThanOrEqualTo(1, plan.ObservedProcessorBudget);
        Assert.IsGreaterThanOrEqualTo(1, plan.ComputeLaneWorkers);
        Assert.IsGreaterThanOrEqualTo(plan.ComputeLaneWorkers, plan.ComputeQueueCapacity);
        Assert.AreNotEqual(ColumnAccessMode.Auto, plan.ColumnAccessMode);
    }

    [TestMethod]
    public void ParseCpuMaxReadsQuotaPeriodPairsAsEffectiveCores()
    {
        Assert.AreEqual<double?>(0.5, ExecutionEnvironment.ParseCpuMax("50000 100000"));
        Assert.AreEqual<double?>(1.5, ExecutionEnvironment.ParseCpuMax("150000 100000"));
        Assert.AreEqual<double?>(2.0, ExecutionEnvironment.ParseCpuMax("200000 100000"));

        //Surrounding whitespace is tolerated — the file ends with a newline.
        Assert.AreEqual<double?>(2.0, ExecutionEnvironment.ParseCpuMax("200000 100000\n"));
    }

    [TestMethod]
    public void ParseCpuMaxTreatsAnUnconstrainedOrMalformedPayloadAsNoQuota()
    {
        Assert.IsNull(ExecutionEnvironment.ParseCpuMax("max 100000"));
        Assert.IsNull(ExecutionEnvironment.ParseCpuMax(""));
        Assert.IsNull(ExecutionEnvironment.ParseCpuMax("100000"));
        Assert.IsNull(ExecutionEnvironment.ParseCpuMax("garbage here"));
        Assert.IsNull(ExecutionEnvironment.ParseCpuMax("0 100000"));
        Assert.IsNull(ExecutionEnvironment.ParseCpuMax("100000 0"));
    }
}
