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
/// The model treats uncorrectable errors as a Poisson process. The
/// per-item intensity is lowered when memory is error-corrected; the
/// aggregate intensity scales with the item count; and cross-checking
/// across replicas (the heterogeneous-protection constellation, where a
/// protected replica is the more reliable detector) divides the
/// <em>undetected</em> intensity by a fleet factor, so a larger or
/// better-protected fleet scrubs each node less often for the same
/// guarantee. The numeric coefficients below are deliberately tunable
/// default assumptions — a placeholder calibration, not measured rates —
/// and a deployment with measured error rates is expected to supply its
/// own estimator.
/// </para>
/// </remarks>
public static class ScrubCadenceEstimators
{
    /// <summary>The assumed per-item uncorrectable-error intensity per year for unprotected memory. A tunable default assumption.</summary>
    private const double UnprotectedItemFailureIntensityPerYear = 5e-6;

    /// <summary>The factor by which error correction lowers the effective uncorrectable-error intensity. A tunable default assumption.</summary>
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

    /// <summary>The default estimator: the Poisson reliability model of <see cref="EstimatePoisson"/>.</summary>
    public static ScrubCadenceEstimatorDelegate Default { get; } = EstimatePoisson;

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
        double itemIntensity = context.MemoryIsProtected
            ? UnprotectedItemFailureIntensityPerYear / ProtectedIntensityReductionFactor
            : UnprotectedItemFailureIntensityPerYear;
        double aggregateIntensityPerYear = itemCount * itemIntensity;
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
