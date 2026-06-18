using Lumoin.Veritas.Tests.Conformance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Jsonata;

/// <summary>
/// Runs the vendored JSONata test suite against the engine.
/// </summary>
/// <remarks>
/// This is a measurement baseline, not a green gate: a case where the engine's value or error differs from
/// the suite's expectation is a real <see cref="W3cOutcomeStatus.Failed"/>, so the failing count is the
/// honest distance to full conformance. Skips are reserved for cases that genuinely cannot run yet —
/// external variable bindings and not-yet-built syntax (the <c>JS0003</c> unsupported-construct bound). The
/// one documented comparison divergence is S-code leniency for syntactically-invalid expressions; see
/// <see cref="JsonataConformanceRunner"/>.
/// </remarks>
[TestClass]
internal sealed class JsonataConformanceTests
{
    /// <summary>Runs one vendored JSONata suite case.</summary>
    /// <param name="testCase">The case supplied by the suite loader.</param>
    [TestMethod]
    [JsonataConformanceData]
    public void RunJsonataTest(JsonataConformanceCase testCase)
    {
        ConformanceAssertions.Apply(JsonataConformanceRunner.Run(testCase));
    }
}
