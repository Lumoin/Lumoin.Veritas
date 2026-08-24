using System;

namespace Lumoin.Veritas.Core.Execution;

/// <summary>
/// The built-in <see cref="ScrubCadenceEstimatorDelegate"/> implementations.
/// <see cref="Default"/> is a Poisson reliability model: it scrubs often
/// enough to keep the expected probability of undetected corruption over a
/// period below a target, scaling the interval with the protection state,
/// the data size, and the replica constellation.
/// </summary>
/// <remarks>
/// <para>
/// The model treats uncorrectable errors as a Poisson process whose per-item
/// intensity is the SUM over independent corruption source channels (see
/// <see cref="CorruptionChannel"/>). Memory error correction lowers ONLY the
/// memory channel's intensity — it does nothing for the storage channel (data
/// at rest) or any other channel — so a protected node's benefit is bounded by
/// the channels error correction does not cover. The aggregate intensity scales
/// with the item count; and cross-checking across replicas (the heterogeneous-
/// protection constellation, where a protected replica is the more reliable
/// detector) divides the <em>undetected</em> intensity by a fleet factor, so a
/// larger or better-protected fleet scrubs each node less often for the same
/// guarantee. The numeric coefficients below are deliberately tunable default
/// assumptions — a placeholder calibration, not measured rates — and a
/// deployment with measured per-channel rates is expected to supply its own
/// estimator.
/// </para>
/// <para>
/// Two consequences of the linear model are the estimator's contract rather
/// than incidental, and are what the validation tests pin. First, the interval
/// is inversely proportional to the item count, so the expected number of
/// undetected corruptions per period is held constant as the data grows — ten
/// times the data yields one tenth the interval. Second, error-corrected memory
/// lengthens the interval, but by STRICTLY LESS than
/// <see cref="ProtectedIntensityReductionFactor"/>: it scales down only the
/// memory channel, and the unscaled storage channel sets a floor the reduction
/// cannot cross. Both relations hold only where the result stays inside the
/// <see cref="MinimumCadence"/>–<see cref="MaximumCadence"/> band that clamps
/// the arithmetic at each end.
/// </para>
/// <para>
/// The per-channel intensities are the estimator's own default assumptions; the
/// memory channel's protection input arrives as
/// <see cref="ScrubCadenceContext.MemoryIsProtected"/>, itself the resolution of
/// the <see cref="MemoryProtectionAssumption"/> knob and the host probe. Other
/// channels have no protection input yet; a future per-channel detection routine
/// — the storage analogue of the memory probe — would supply measured intensities
/// keyed by <see cref="CorruptionChannel"/>.
/// </para>
/// </remarks>
public static class ScrubCadenceEstimators
{
    /// <summary>The assumed per-item uncorrectable-error intensity per year for the memory channel on unprotected (non-error-corrected) memory. A tunable default assumption.</summary>
    private const double MemoryChannelItemIntensityPerYear = 4e-6;

    /// <summary>The assumed per-item uncorrectable-error intensity per year for the storage channel (data at rest); memory error correction does not scale this. A tunable default assumption.</summary>
    private const double StorageChannelItemIntensityPerYear = 1e-6;

    /// <summary>The factor by which error correction lowers the memory channel's intensity. A tunable default assumption.</summary>
    private const double ProtectedIntensityReductionFactor = 1_000.0;

    /// <summary>The item count assumed when the context reports the data size as unknown, so a resolve-time estimate is still sane.</summary>
    private const double NominalItemCount = 1_000_000.0;

    /// <summary>The target probability of undetected corruption per period used when the context does not set one. A tunable default assumption.</summary>
    private const double DefaultTargetUndetectedCorruptionProbability = 1e-3;

    /// <summary>The detection weight of a protected replica relative to an unprotected one, reflecting its more reliable cross-check.</summary>
    private const double ProtectedReplicaWeight = 4.0;

    /// <summary>The shortest interval the model will return — scrubbing more often than this buys little against its overhead.</summary>
    private static readonly TimeSpan MinimumCadence = TimeSpan.FromHours(1);

    /// <summary>The longest interval the model will return — beyond this the staleness window is unacceptable regardless of the reliability arithmetic.</summary>
    private static readonly TimeSpan MaximumCadence = TimeSpan.FromDays(365);

    /// <summary>The estimator's default per-channel intensities; their sum is the per-item intensity before the fleet cross-check.</summary>
    private static readonly ChannelCalibration[] DefaultCalibration =
    [
        new(CorruptionChannel.Memory, MemoryChannelItemIntensityPerYear),
        new(CorruptionChannel.Storage, StorageChannelItemIntensityPerYear),
    ];

    /// <summary>The default estimator: the Poisson reliability model of <see cref="EstimatePoisson"/>.</summary>
    public static ScrubCadenceEstimatorDelegate Default { get; } = EstimatePoisson;

    /// <summary>A corruption source channel paired with its assumed per-item intensity — the estimator's default calibration for that channel.</summary>
    /// <param name="Channel">The corruption source channel.</param>
    /// <param name="ItemIntensityPerYear">The assumed per-item uncorrectable-error intensity per year for that channel on unprotected hardware. A tunable default assumption.</param>
    private readonly record struct ChannelCalibration(CorruptionChannel Channel, double ItemIntensityPerYear);

    /// <summary>Sums the per-item uncorrectable-error intensity across the modelled channels, lowering only the channels memory error correction covers when <paramref name="memoryIsProtected"/> is set.</summary>
    /// <param name="memoryIsProtected">Whether this node's memory is hardware error-corrected.</param>
    /// <returns>The per-item intensity per year across all channels.</returns>
    private static double PerItemIntensityPerYear(bool memoryIsProtected)
    {
        double total = 0.0;
        foreach(ChannelCalibration calibration in DefaultCalibration)
        {
            bool reduced = memoryIsProtected && calibration.Channel.IsReducedByMemoryErrorCorrection;
            total += reduced ? calibration.ItemIntensityPerYear / ProtectedIntensityReductionFactor : calibration.ItemIntensityPerYear;
        }

        return total;
    }

    /// <summary>
    /// Estimates the scrub interval as the period over which the expected
    /// undetected-corruption probability reaches the target: longer when
    /// memory is error-corrected or the fleet cross-checks more, shorter as
    /// the protected data grows. Clamped to a sane range.
    /// </summary>
    /// <param name="context">The protection, data-size, and cluster inputs.</param>
    /// <returns>The target interval between scrub walks.</returns>
    public static TimeSpan EstimatePoisson(ScrubCadenceContext context)
    {
        double itemCount = context.ProtectedItemCount > 0 ? context.ProtectedItemCount : NominalItemCount;
        double aggregateIntensityPerYear = itemCount * PerItemIntensityPerYear(context.MemoryIsProtected);
        double target = context.TargetUndetectedCorruptionProbability > 0
            ? context.TargetUndetectedCorruptionProbability
            : DefaultTargetUndetectedCorruptionProbability;
        double fleetFactor = FleetDetectionFactor(context.ReplicaCount, context.ProtectedReplicaCount);

        //P(undetected within T) ~= aggregateIntensity * T / fleetFactor for small probabilities;
        //solving for T at the target gives the interval, in years, then days.
        double days = target * fleetFactor / aggregateIntensityPerYear * 365.0;
        if(!double.IsFinite(days))
        {
            return MaximumCadence;
        }

        return TimeSpan.FromDays(Math.Clamp(days, MinimumCadence.TotalDays, MaximumCadence.TotalDays));
    }

    /// <summary>
    /// The factor by which the fleet's cross-checking lets a single node
    /// stretch its scrub interval. A single node has no cross-check (one);
    /// additional replicas raise it, and protected replicas — the more
    /// reliable detectors — weigh more.
    /// </summary>
    /// <param name="replicaCount">The number of replicas holding the data.</param>
    /// <param name="protectedReplicaCount">How many replicas have error-corrected memory.</param>
    /// <returns>The fleet detection factor, at least one.</returns>
    private static double FleetDetectionFactor(int replicaCount, int protectedReplicaCount)
    {
        int replicas = Math.Max(1, replicaCount);
        if(replicas <= 1)
        {
            return 1.0;
        }

        int protectedReplicas = Math.Clamp(protectedReplicaCount, 0, replicas);

        return replicas + (protectedReplicas * (ProtectedReplicaWeight - 1.0));
    }
}
