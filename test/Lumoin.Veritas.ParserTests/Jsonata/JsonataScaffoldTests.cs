using Lumoin.Veritas.Jsonata;

namespace Lumoin.Veritas.ParserTests.Jsonata;

/// <summary>
/// Validates the <c>Lumoin.Veritas.Jsonata</c> project skeleton: the evaluation-limit exception
/// carries its <see cref="JsonataLimit"/>.
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
}
