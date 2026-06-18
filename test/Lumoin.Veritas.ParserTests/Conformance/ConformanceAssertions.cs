using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Maps a <see cref="W3cOutcome"/> to the MSTest assertion that
/// reports it: pass returns silently, skip becomes
/// <see cref="Assert.Inconclusive(string)"/>, fail becomes
/// <see cref="Assert.Fail(string)"/>.
/// </summary>
/// <remarks>
/// Centralised so every suite class has the same mapping —
/// otherwise drift would let one suite bury a real failure
/// while another reports it. <c>Inconclusive</c> remains
/// reserved for structural skips (<see cref="W3cTestType.Unknown"/>
/// or a fixture-missing case the harness chose to skip rather
/// than fail). Real defects always become <see cref="Assert.Fail(string)"/>.
/// </remarks>
internal static class ConformanceAssertions
{
    /// <summary>
    /// Applies the outcome to MSTest. Returns when passed; raises an assertion otherwise.
    /// </summary>
    /// <param name="outcome">The outcome from <see cref="W3cTestRunner"/>.</param>
    public static void Apply(W3cOutcome outcome)
    {
        switch(outcome.Status)
        {
            case W3cOutcomeStatus.Passed:
            {
                return;
            }

            case W3cOutcomeStatus.Skipped:
            {
                Assert.Inconclusive(outcome.Message);
                return;
            }

            case W3cOutcomeStatus.Failed:
            {
                Assert.Fail(outcome.Message);
                return;
            }

            default:
            {
                Assert.Inconclusive($"Unexpected outcome status: {outcome.Status}.");
                return;
            }
        }
    }
}
