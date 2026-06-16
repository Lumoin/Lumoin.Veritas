using System;
using Lumoin.Veritas.Core.Execution;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// Validates the error-estimation system the fault harness relies on — the Poisson scrub-cadence
/// estimator — against the QUANTITATIVE guarantees the directional monotonicity and clamp tests in
/// the Execution suite do not pin: that the cadence keeps the expected undetected-corruption count
/// per period constant (ten times the data, one tenth the cadence) and that protected memory
/// lengthens the cadence by the model's reduction factor, checked in the unclamped band so it pins
/// the factor rather than a direction the clamp would also satisfy. The estimator is pure and
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

    /// <summary>Protected memory lengthens the cadence by the model's reduction factor — measured in the unclamped band (a million items keeps both the protected and unprotected cadences off the clamps), so it pins the factor rather than a mere direction.</summary>
    [TestMethod]
    public void ProtectedMemoryExtendsCadenceByTheReductionFactor()
    {
        ScrubCadenceContext context = new(MemoryIsProtected: false, ProtectedItemCount: 1_000_000);
        TimeSpan unprotected = ScrubCadenceEstimators.Default(context);
        TimeSpan protectedMemory = ScrubCadenceEstimators.Default(context with { MemoryIsProtected = true });

        double ratio = protectedMemory.TotalSeconds / unprotected.TotalSeconds;
        Assert.IsTrue(ratio is > 950.0 and < 1050.0, $"Expected protected memory to lengthen the cadence ~1000x in the unclamped band, got {ratio}x.");
    }
}
