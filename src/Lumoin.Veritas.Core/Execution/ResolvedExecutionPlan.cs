using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// The settled facts <see cref="ExecutionPolicy.Resolve()"/> derives
/// from a policy and an observed <see cref="ExecutionEnvironment"/> —
/// the plan side of the policy/plan split. The policy is operator
/// intent (which knobs, mostly "auto"); the plan is the concrete
/// numbers and backend families the compute lane, host hardening, and
/// persistence layers act on. Every "derive" sentinel on the policy is
/// gone here: the fields below are decisions, not requests.
/// </summary>
/// <param name="ObservedProcessorBudget">The effective core budget the resolution observed — the cgroup CPU quota when one constrains the group, otherwise the logical processor count, floored at one.</param>
/// <param name="ComputeLaneWorkers">The resolved compute-lane width: an explicit policy override, or one core of headroom below the budget (<c>max(1, budget - 1)</c>) so the latency-sensitive serve pool is not starved. One on a single-core budget and on the browser's single cooperative thread.</param>
/// <param name="ComputeQueueCapacity">The resolved bounded depth of the compute lane's work queue; admission sheds beyond it. An explicit policy override, or a small multiple of the worker count.</param>
/// <param name="HostPoolFloorMultiplier">The effective thread-pool floor multiplier applied at host startup; zero disables it, and the browser (no pool to floor) always resolves to zero. A transitional patch-fix lifted once the lane removes the work it compensates for.</param>
/// <param name="KernelWidthCap">The SIMD codec ladder ceiling threaded into backend selection; <see cref="Execution.KernelWidthCap.Auto"/> leaves the ladder uncapped at hardware capability.</param>
/// <param name="Protection">The resolved memory-protection verdict driving verify-on-load and scrub cadence.</param>
/// <param name="ScrubCadence">The resolved target cadence at which a full scrub walk is initiated; <see cref="TimeSpan.Zero"/> disables scrubbing. This is the launch cadence, not the realised per-block coverage latency, which is load-dependent and surfaced as telemetry.</param>
/// <param name="ColumnAccessMode">The resolved column byte-source family — <see cref="Execution.ColumnAccessMode.MemoryMapped"/> or <see cref="Execution.ColumnAccessMode.Streamed"/>, never <see cref="Execution.ColumnAccessMode.Auto"/>.</param>
[DebuggerDisplay("ResolvedExecutionPlan Budget={ObservedProcessorBudget} Workers={ComputeLaneWorkers} Queue={ComputeQueueCapacity} Floor={HostPoolFloorMultiplier} Cap={KernelWidthCap} Access={ColumnAccessMode} Scrub={ScrubCadence}")]
internal readonly record struct ResolvedExecutionPlan(
    int ObservedProcessorBudget,
    int ComputeLaneWorkers,
    int ComputeQueueCapacity,
    int HostPoolFloorMultiplier,
    KernelWidthCap KernelWidthCap,
    ResolvedProtectionState Protection,
    TimeSpan ScrubCadence,
    ColumnAccessMode ColumnAccessMode);
