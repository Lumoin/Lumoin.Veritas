using System;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// Selects and builds the <see cref="IComputeLane"/> implementation for
/// the running platform. <see cref="ForCurrentPlatform"/> is the default
/// <see cref="CreateComputeLaneDelegate"/>: a threaded async-consumer
/// lane on hosts with real concurrency, and the single-cooperative-thread
/// lane on the browser. A host that wants a different substrate supplies
/// its own <see cref="CreateComputeLaneDelegate"/> instead.
/// </summary>
public static class ComputeLane
{
    /// <summary>
    /// Builds the lane for the running platform: a
    /// <see cref="CooperativeComputeLane"/> on the browser (one
    /// cooperative thread), and a <see cref="ThreadedComputeLane"/>
    /// elsewhere (bounded async consumers sized to the resolved width).
    /// </summary>
    /// <param name="policy">The policy the lane sizes itself from.</param>
    /// <param name="recordTurnDuration">The optional per-turn duration sink the lane records to; <c>null</c> leaves the lane meter-free.</param>
    /// <returns>The platform's lane.</returns>
    public static IComputeLane ForCurrentPlatform(ExecutionPolicy policy, RecordTurnDurationDelegate? recordTurnDuration = null)
    {
        return OperatingSystem.IsBrowser()
            ? new CooperativeComputeLane(policy, ExecutionEnvironment.Observe, VeritasClock.System, recordTurnDuration)
            : new ThreadedComputeLane(policy, ExecutionEnvironment.Observe, VeritasClock.System, recordTurnDuration);
    }
}
