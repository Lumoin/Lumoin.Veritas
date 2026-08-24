using System;
using Lumoin.Veritas.Core.Execution;

namespace Lumoin.Veritas.Tests.Execution;

/// <summary>
/// The default Poisson scrub-cadence estimator: the interval shortens as
/// the protected data grows, lengthens when memory is error-corrected and
/// when a larger or better-protected fleet cross-checks, scales with the
/// tolerated undetected-corruption probability, substitutes a nominal size
/// when the data size is unknown, and is clamped to a sane range.
/// </summary>
[TestClass]
internal sealed class ScrubCadenceEstimatorsTests
{
    /// <summary>The estimator's lower clamp.</summary>
    private static readonly TimeSpan MinimumCadence = TimeSpan.FromHours(1);

    /// <summary>The estimator's upper clamp.</summary>
    private static readonly TimeSpan MaximumCadence = TimeSpan.FromDays(365);

    [TestMethod]
    public void MoreProtectedDataScrubsSooner()
    {
        TimeSpan moreData = ScrubCadenceEstimators.EstimatePoisson(new ScrubCadenceContext(MemoryIsProtected: false, ProtectedItemCount: 1_000_000));
        TimeSpan lessData = ScrubCadenceEstimators.EstimatePoisson(new ScrubCadenceContext(MemoryIsProtected: false, ProtectedItemCount: 100_000));

        Assert.IsLessThan(lessData, moreData);
    }

    [TestMethod]
    public void ErrorCorrectedMemoryScrubsLessOften()
    {
        TimeSpan protectedCadence = ScrubCadenceEstimators.EstimatePoisson(new ScrubCadenceContext(MemoryIsProtected: true, ProtectedItemCount: 1_000_000));
        TimeSpan unprotectedCadence = ScrubCadenceEstimators.EstimatePoisson(new ScrubCadenceContext(MemoryIsProtected: false, ProtectedItemCount: 1_000_000));

        Assert.IsGreaterThan(unprotectedCadence, protectedCadence);
    }

    [TestMethod]
    public void ALargerFleetScrubsEachNodeLessOften()
    {
        TimeSpan singleNode = ScrubCadenceEstimators.EstimatePoisson(new ScrubCadenceContext(MemoryIsProtected: false, ProtectedItemCount: 1_000_000, ReplicaCount: 1));
        TimeSpan fourReplicas = ScrubCadenceEstimators.EstimatePoisson(new ScrubCadenceContext(MemoryIsProtected: false, ProtectedItemCount: 1_000_000, ReplicaCount: 4, ProtectedReplicaCount: 2));

        Assert.IsGreaterThan(singleNode, fourReplicas);
    }

    [TestMethod]
    public void ABetterProtectedFleetScrubsLessOftenThanAnUnprotectedOneOfTheSameSize()
    {
        TimeSpan unprotectedFleet = ScrubCadenceEstimators.EstimatePoisson(new ScrubCadenceContext(MemoryIsProtected: false, ProtectedItemCount: 1_000_000, ReplicaCount: 3, ProtectedReplicaCount: 0));
        TimeSpan protectedFleet = ScrubCadenceEstimators.EstimatePoisson(new ScrubCadenceContext(MemoryIsProtected: false, ProtectedItemCount: 1_000_000, ReplicaCount: 3, ProtectedReplicaCount: 2));

        Assert.IsGreaterThan(unprotectedFleet, protectedFleet);
    }

    [TestMethod]
    public void AHigherToleranceScrubsLessOften()
    {
        TimeSpan tighter = ScrubCadenceEstimators.EstimatePoisson(new ScrubCadenceContext(MemoryIsProtected: false, ProtectedItemCount: 1_000_000, TargetUndetectedCorruptionProbability: 5e-3));
        TimeSpan looser = ScrubCadenceEstimators.EstimatePoisson(new ScrubCadenceContext(MemoryIsProtected: false, ProtectedItemCount: 1_000_000, TargetUndetectedCorruptionProbability: 1e-2));

        Assert.IsGreaterThan(tighter, looser);
    }

    [TestMethod]
    public void UnknownDataSizeUsesANominalEstimate()
    {
        TimeSpan unknown = ScrubCadenceEstimators.EstimatePoisson(new ScrubCadenceContext(MemoryIsProtected: false, ProtectedItemCount: 0));
        TimeSpan nominal = ScrubCadenceEstimators.EstimatePoisson(new ScrubCadenceContext(MemoryIsProtected: false, ProtectedItemCount: 1_000_000));

        Assert.AreEqual(nominal, unknown);
    }

    [TestMethod]
    public void TheIntervalIsClampedToASaneRange()
    {
        //An enormous protected dataset would arithmetic to a sub-hour interval.
        TimeSpan floored = ScrubCadenceEstimators.EstimatePoisson(new ScrubCadenceContext(MemoryIsProtected: false, ProtectedItemCount: 1_000_000_000_000L));
        Assert.AreEqual(MinimumCadence, floored);

        //A tiny error-corrected dataset would arithmetic to centuries.
        TimeSpan capped = ScrubCadenceEstimators.EstimatePoisson(new ScrubCadenceContext(MemoryIsProtected: true, ProtectedItemCount: 1));
        Assert.AreEqual(MaximumCadence, capped);
    }

    [TestMethod]
    public void TheDefaultEstimatorIsThePoissonModel()
    {
        ScrubCadenceContext context = new(MemoryIsProtected: false, ProtectedItemCount: 250_000, ReplicaCount: 2, ProtectedReplicaCount: 1);

        Assert.AreEqual(ScrubCadenceEstimators.EstimatePoisson(context), ScrubCadenceEstimators.Default(context));
    }
}
