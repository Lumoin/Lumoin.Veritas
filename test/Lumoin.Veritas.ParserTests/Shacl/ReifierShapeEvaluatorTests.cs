using Lumoin.Veritas.Core;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using Lumoin.Veritas.ParserTests.Infrastructure;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// Tests for <see cref="ReifierShapeEvaluator"/>. The evaluator is a
/// stub pending RDF 1.2 triple-term plumbing; it must emit a
/// single <see cref="Severity.Info"/> result per invocation and never
/// throw. Per SHACL §3.6 that Info result, like any result, makes the
/// report non-conforming.
/// </summary>
[TestClass]
internal sealed class ReifierShapeEvaluatorTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExFocus = "http://example.org/foo";
    private const string ExShape = "http://example.org/S";
    private const string ExPath = "http://example.org/p";
    private const string ExInner = "http://example.org/InnerShape";
    private const string ExValue = "http://example.org/v";

    [TestMethod]
    public async Task ReifierShapeEmitsInfoResultWithCorrectAttribution()
    {
        (ValidationReport report, ValidationTrace _) = await RunAsync(
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(1, report.Results,
            "Expected exactly one Info result from the stub.");

        ValidationResult result = report.Results[0];
        Assert.AreEqual(Severity.Info, result.Severity,
            "Stub must emit Severity.Info, not Violation.");
        Assert.AreEqual(
            ShaclComponentVocabulary.ReifierShape,
            result.SourceConstraintComponent,
            "Result must attribute to sh:ReifierShapeConstraintComponent.");
    }

    [TestMethod]
    public async Task ReifierShapeStubInfoResultMakesReportNonConforming()
    {
        //sh:conforms is the absence of any result (§3.6), so the stub's one Info result ⇒ Conforms == false.
        (ValidationReport report, ValidationTrace trace) = await RunAsync(
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms,
            $"A produced result (any severity) makes Conforms false under §3.6. Trace:\n{trace}");
    }

    //Helper. Builds a property shape with sh:reifierShape pointing
    //at an inner shape, plus one triple emitting a value at the
    //path. The data-graph value is a plain IRI — not a real triple
    //term — but the stub doesn't inspect value nodes, so this is
    //sufficient to drive evaluator invocation.
    private static async Task<(ValidationReport, ValidationTrace)> RunAsync(
        CancellationToken cancellationToken)
    {
        TestShaclPipelineShapeState shapeState = TestShaclPipeline
            .BeginWithFocus(ExFocus)
            .WithPropertyShapeTargetingSubjectsOfPath(ExShape, ExPath)
            .With(ShaclConstraintVocabulary.ReifierShape.ToString(),
                ShapeGraphBuilder.Iri(ExInner))
            .Done();

        TestShaclPipelineDataState dataState = await shapeState
            .BuildAsync(cancellationToken).ConfigureAwait(false);

        dataState = dataState.WithTripleOnFocus(
            ExPath, new NamedNode(Utf8Strings.From(ExValue)));

        return await dataState
            .WithEvaluator(ShaclComponentVocabulary.ReifierShape, ReifierShapeEvaluator.EvaluateAsync)
            .RunWithTraceAsync(cancellationToken).ConfigureAwait(false);
    }
}
