using Lumoin.Veritas.Jsonata;
using JsonataEngine = Lumoin.Veritas.Jsonata.Jsonata;

namespace Lumoin.Veritas.Tests.Jsonata;

/// <summary>
/// Validates the <c>Lumoin.Veritas.Jsonata</c> project skeleton: the evaluation-limit exception
/// carries its <see cref="JsonataLimit"/>, and the engine facade reports an unsupported-feature
/// error until it is implemented.
/// </summary>
[TestClass]
internal sealed class JsonataScaffoldTests
{
    /// <summary>An evaluation-limit exception carries the bound it reports.</summary>
    [TestMethod]
    public void EvaluationLimitExceptionCarriesItsLimit()
    {
        JsonataEvaluationLimitException exception = new(JsonataLimit.EvaluationDepth, "Evaluation depth exceeded.");

        Assert.AreEqual(JsonataLimit.EvaluationDepth, exception.Limit);
    }

    /// <summary>The engine facade reports an unsupported-feature error until it is implemented.</summary>
    [TestMethod]
    public void EvaluateReportsUnsupportedUntilImplemented()
    {
        Assert.ThrowsExactly<JsonataUnsupportedFeatureException>(() =>
        {
            JsonataEngine.Evaluate(default, default);
        });
    }
}
