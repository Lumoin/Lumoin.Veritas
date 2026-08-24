using System;
using System.Threading.Tasks;
using Lumoin.Veritas.Workbench;

namespace Lumoin.Veritas.Tests.Workbench;

/// <summary>
/// In-process unit tests for the soak operations, driven on a stepping clock so every pin is
/// exact: the clock advances one fixed step per timestamp draw, making the iteration count and
/// the reported <see cref="SoakResult.Elapsed"/> deterministic functions of the requested
/// duration on every machine under any load. The exact Elapsed pins — always whole multiples
/// of the step — are the provenance guard proving the result is computed from the injected
/// clock and nothing else.
/// </summary>
[TestClass]
internal sealed class HypertrieSoakOperationsTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The synthetic corpus size, small enough to keep per-test wall time low.</summary>
    private const int SmallCorpus = 1_000;

    /// <summary>The clock step every timestamp draw advances by.</summary>
    private static TimeSpan Step => TimeSpan.FromMilliseconds(10);

    /// <summary>The requested soak duration: ten steps, so deadline checks one through nine pass at 10..90ms, check ten fails at exactly 100ms, and the loop runs exactly nine iterations.</summary>
    private static TimeSpan Duration => TimeSpan.FromMilliseconds(100);

    /// <summary>The exact iteration count <see cref="Duration"/> admits on the stepping clock.</summary>
    private const long ExpectedIterations = 9L;

    /// <summary>The exact Elapsed a soak reports for <see cref="Duration"/>: ten deadline draws plus the deliberate measured-at-exit draw, eleven steps after the start.</summary>
    private static TimeSpan ExpectedElapsed => TimeSpan.FromMilliseconds(110);

    /// <summary>The exact Elapsed a zero-duration soak reports: the one failing deadline draw plus the measured-at-exit draw.</summary>
    private static TimeSpan ExpectedZeroDurationElapsed => TimeSpan.FromMilliseconds(20);

    /// <summary>The build soak runs exactly the iterations the stepping clock admits and reports the clock-derived elapsed value.</summary>
    [TestMethod]
    public async Task RunBuildSoakRunsExactlyTheClockAdmittedIterations()
    {
        SoakResult result = await HypertrieSoak.RunBuildSoakAsync(Duration, new SteppingTimeProvider(Step), SmallCorpus, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExpectedIterations, result.Iterations, "Deadline checks one through nine pass; check ten fails at exactly the duration.");
        Assert.AreEqual(ExpectedElapsed, result.Elapsed, "Elapsed is the measured-at-exit draw, eleven steps after the start.");
        Assert.AreEqual(0L, result.AuxiliaryCount, "The build soak tallies nothing beyond iterations.");
    }

    /// <summary>The query soak runs exactly the iterations the stepping clock admits and emits exactly one solution per corpus triple per iteration.</summary>
    [TestMethod]
    public async Task RunQuerySoakRunsExactlyTheClockAdmittedIterations()
    {
        SoakResult result = await HypertrieSoak.RunQuerySoakAsync(Duration, new SteppingTimeProvider(Step), SmallCorpus, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(ExpectedIterations, result.Iterations, "Deadline checks one through nine pass; check ten fails at exactly the duration.");
        Assert.AreEqual(ExpectedElapsed, result.Elapsed, "Elapsed is the measured-at-exit draw, eleven steps after the start.");
        Assert.AreEqual(ExpectedIterations * SmallCorpus, result.AuxiliaryCount, "The unbound single pattern emits every distinct corpus triple once per iteration.");
    }

    /// <summary>A zero-duration build soak completes without iterating and still reports the clock-derived elapsed value.</summary>
    [TestMethod]
    public async Task RunBuildSoakWithZeroDurationCompletesWithoutIterations()
    {
        SoakResult result = await HypertrieSoak.RunBuildSoakAsync(TimeSpan.Zero, new SteppingTimeProvider(Step), SmallCorpus, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0L, result.Iterations, "The first deadline check already fails for a zero duration.");
        Assert.AreEqual(ExpectedZeroDurationElapsed, result.Elapsed, "Elapsed is the failing check's draw plus the measured-at-exit draw.");
        Assert.AreEqual(0L, result.AuxiliaryCount, "The build soak tallies nothing beyond iterations.");
    }

    /// <summary>A zero-duration query soak completes without iterating and still reports the clock-derived elapsed value.</summary>
    [TestMethod]
    public async Task RunQuerySoakWithZeroDurationCompletesWithoutIterations()
    {
        SoakResult result = await HypertrieSoak.RunQuerySoakAsync(TimeSpan.Zero, new SteppingTimeProvider(Step), SmallCorpus, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0L, result.Iterations, "The first deadline check already fails for a zero duration.");
        Assert.AreEqual(ExpectedZeroDurationElapsed, result.Elapsed, "Elapsed is the failing check's draw plus the measured-at-exit draw.");
        Assert.AreEqual(0L, result.AuxiliaryCount, "No iteration ran, so nothing was emitted.");
    }

    /// <summary>
    /// A clock whose timestamp advances one fixed step per draw, starting at zero: draw k
    /// reads k steps of elapsed time, so a loop's deadline checks and its measured-at-exit
    /// read are exact, machine-invariant functions of the draw count. Single-threaded
    /// sequential use only — the soak loops draw strictly one at a time.
    /// </summary>
    private sealed class SteppingTimeProvider : TimeProvider
    {
        /// <summary>The step every draw advances by, in timestamp ticks.</summary>
        private readonly long stepTicks;

        /// <summary>The number of draws taken so far.</summary>
        private long draws;

        /// <summary>Constructs the clock over its per-draw step.</summary>
        /// <param name="step">The amount each timestamp draw advances the clock by.</param>
        public SteppingTimeProvider(TimeSpan step)
        {
            stepTicks = step.Ticks;
        }

        /// <summary>The timestamp frequency: one tick per <see cref="TimeSpan"/> tick, so timestamp arithmetic maps to time spans directly.</summary>
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <summary>Draws the next timestamp: zero first, then one step later per draw.</summary>
        /// <returns>The timestamp.</returns>
        public override long GetTimestamp()
        {
            long value = draws * stepTicks;
            draws++;

            return value;
        }
    }
}
