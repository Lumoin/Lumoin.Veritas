using System;
using System.Threading.Tasks;
using Lumoin.Veritas.Workbench;

namespace Lumoin.Veritas.Tests.Workbench;

/// <summary>
/// In-process unit tests for the soak operations. These tests
/// invoke <see cref="HypertrieSoak.RunBuildSoakAsync"/> and
/// <see cref="HypertrieSoak.RunQuerySoakAsync"/> directly with
/// small corpora and short durations to keep per-test wall time
/// well under one second.
/// </summary>
[TestClass]
internal sealed class HypertrieSoakOperationsTests
{
    public TestContext TestContext { get; set; } = null!;

    private const int SmallCorpus = 1_000;
    private static TimeSpan ShortDuration => TimeSpan.FromMilliseconds(100);

    [TestMethod]
    public async Task RunBuildSoakWithShortDurationCompletesAndReportsAtLeastOneIteration()
    {
        SoakResult result = await HypertrieSoak.RunBuildSoakAsync(ShortDuration, SmallCorpus).ConfigureAwait(false);

        Assert.IsGreaterThan(0L, result.Iterations);
        Assert.IsGreaterThanOrEqualTo(ShortDuration, result.Elapsed);
        Assert.AreEqual(0L, result.AuxiliaryCount);
    }

    [TestMethod]
    public async Task RunQuerySoakWithShortDurationCompletesAndEmitsSolutions()
    {
        SoakResult result = await HypertrieSoak.RunQuerySoakAsync(ShortDuration, SmallCorpus).ConfigureAwait(false);

        Assert.IsGreaterThan(0L, result.Iterations);
        Assert.IsGreaterThanOrEqualTo(ShortDuration, result.Elapsed);
        //Single unbound pattern over a non-empty graph emits every
        //triple as a solution, so total solutions is iterations
        //times the per-iteration result-set size — at minimum
        //one solution per iteration.
        Assert.IsGreaterThanOrEqualTo(result.Iterations, result.AuxiliaryCount);
    }

    [TestMethod]
    public async Task RunBuildSoakWithZeroDurationCompletesWithoutIterations()
    {
        SoakResult result = await HypertrieSoak.RunBuildSoakAsync(TimeSpan.Zero, SmallCorpus).ConfigureAwait(false);

        Assert.AreEqual(0L, result.Iterations);
        Assert.AreEqual(0L, result.AuxiliaryCount);
    }

    [TestMethod]
    public async Task RunQuerySoakWithZeroDurationCompletesWithoutIterations()
    {
        SoakResult result = await HypertrieSoak.RunQuerySoakAsync(TimeSpan.Zero, SmallCorpus).ConfigureAwait(false);

        Assert.AreEqual(0L, result.Iterations);
        Assert.AreEqual(0L, result.AuxiliaryCount);
    }
}
