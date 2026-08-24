using System;
using Lumoin.Veritas.Core.Execution;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// Validates the error-estimation system the fault harness relies on — the Poisson scrub-cadence
/// estimator — against the QUANTITATIVE guarantees the directional monotonicity and clamp tests in
/// the Execution suite do not pin: that the cadence keeps the expected undetected-corruption count
/// per period constant (ten times the data, one tenth the cadence) and that error correction
/// lengthens the cadence but by strictly less than the memory-channel reduction factor — the
/// unscaled storage channel caps the benefit — checked in the unclamped band so it pins the channel
/// separation rather than a direction the clamp would also satisfy. The estimator is pure and
/// time-free, so no clock is needed; the fault harness in this folder separately proves a verify
/// round detects 100% of injected column-blob byte-faults, the detection premise this cadence
/// assumes. Directional monotonicity and the clamp bounds are covered by the Execution suite and
/// not restated here.
/// </summary>
[TestClass]
internal sealed class ScrubCadenceEstimatorValidationTests
{
    /// <summary>The estimator keeps the expected undetected-corruption count per period constant: ten times the data yields one tenth the cadence (both within the unclamped band).</summary>
    [TestMethod]
    public void CadenceKeepsExpectedUndetectedConstantAsDataGrows()
    {
        TimeSpan tenThousand = ScrubCadenceEstimators.Default(new ScrubCadenceContext(MemoryIsProtected: false, ProtectedItemCount: 10_000));
        TimeSpan hundredThousand = ScrubCadenceEstimators.Default(new ScrubCadenceContext(MemoryIsProtected: false, ProtectedItemCount: 100_000));

        double ratio = tenThousand.TotalSeconds / hundredThousand.TotalSeconds;
        Assert.IsTrue(ratio is > 9.5 and < 10.5, $"Expected ~10x cadence for one tenth the data, got {ratio}x.");
    }

    /// <summary>Error correction lengthens the cadence, but by strictly less than the memory-channel reduction factor (1000): it scales only the memory channel, and the unscaled storage channel sets a floor the reduction cannot cross. Measured at a million items so both cadences stay off the clamps. A regression that scaled the whole per-item intensity by error correction would push the ratio to ~1000 and fail.</summary>
    [TestMethod]
    public void ErrorCorrectionLengthensCadenceButTheUnscaledStorageChannelCapsTheBenefit()
    {
        ScrubCadenceContext context = new(MemoryIsProtected: false, ProtectedItemCount: 1_000_000);
        TimeSpan unprotected = ScrubCadenceEstimators.Default(context);
        TimeSpan protectedMemory = ScrubCadenceEstimators.Default(context with { MemoryIsProtected = true });

        double ratio = protectedMemory.TotalSeconds / unprotected.TotalSeconds;
        Assert.IsTrue(ratio is > 1.0 and < 1000.0, $"Expected error correction to lengthen the cadence but well under the 1000x memory-channel factor, got {ratio}x.");
    }
}
