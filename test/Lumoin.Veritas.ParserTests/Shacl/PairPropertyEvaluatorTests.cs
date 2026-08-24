using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using Lumoin.Veritas.ParserTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// Tests for the six set-comparison evaluators introduced in 2C-d
/// batch 3.
/// </summary>
[TestClass]
internal sealed class PairPropertyEvaluatorTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExShape = "http://example.org/S";
    private const string ExPropShape = "http://example.org/PS";
    private const string ExPath = "http://example.org/path";
    private const string ExOther = "http://example.org/other";
    private const string ExFocus = "http://example.org/foo";

    //sh:hasValue — at least one value must be term-equal to the required value.

    [TestMethod]
    public async Task HasValueAcceptsRequiredValuePresent()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunHasValueAsync(
            requiredValue: IntLit(42),
            values: [IntLit(40), IntLit(42)],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task HasValueRejectsRequiredValueAbsent()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunHasValueAsync(
            requiredValue: IntLit(42),
            values: [IntLit(40), IntLit(41)],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertSingleViolationFromComponent(report, trace, dict, ShaclComponentVocabulary.HasValue);
        Assert.IsNull(report.Results[0].ValueNode, "hasValue is set-level — no specific value node should be reported.");
    }

    [TestMethod]
    public async Task HasValueIsTermEqualityNotValueEquality()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunHasValueAsync(
            requiredValue: IntLit(5),
            values: [MakeLiteral("5.0", Vocabulary.Xsd.Decimal)],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertSingleViolationFromComponent(report, trace, dict, ShaclComponentVocabulary.HasValue);
    }

    //sh:equals — value-node set must equal comparison-set (term equality).

    [TestMethod]
    public async Task EqualsAcceptsIdenticalSets()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunPairAsync(
            ShaclConstraintVocabulary.EqualsTo.ToString(),
            ShaclComponentVocabulary.EqualsTo,
            EqualsEvaluator.EvaluateAsync,
            valueLexicals: [42, 100],
            comparisonLexicals: [42, 100],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task EqualsAcceptsIdenticalSetsRegardlessOfOrder()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunPairAsync(
            ShaclConstraintVocabulary.EqualsTo.ToString(),
            ShaclComponentVocabulary.EqualsTo,
            EqualsEvaluator.EvaluateAsync,
            valueLexicals: [100, 42],
            comparisonLexicals: [42, 100],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task EqualsRejectsExtraValueInOurSet()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunPairAsync(
            ShaclConstraintVocabulary.EqualsTo.ToString(),
            ShaclComponentVocabulary.EqualsTo,
            EqualsEvaluator.EvaluateAsync,
            valueLexicals: [42, 100, 7],
            comparisonLexicals: [42, 100],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertResultCount(report, trace, dict, expected: 1);
    }

    [TestMethod]
    public async Task EqualsRejectsMissingValueInOurSet()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunPairAsync(
            ShaclConstraintVocabulary.EqualsTo.ToString(),
            ShaclComponentVocabulary.EqualsTo,
            EqualsEvaluator.EvaluateAsync,
            valueLexicals: [42],
            comparisonLexicals: [42, 100],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertResultCount(report, trace, dict, expected: 1);
    }

    //sh:disjoint — value-node set and comparison-set must be term-disjoint.

    [TestMethod]
    public async Task DisjointAcceptsNonOverlappingSets()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunPairAsync(
            ShaclConstraintVocabulary.Disjoint.ToString(),
            ShaclComponentVocabulary.Disjoint,
            DisjointEvaluator.EvaluateAsync,
            valueLexicals: [1, 2, 3],
            comparisonLexicals: [4, 5, 6],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task DisjointRejectsOverlap()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunPairAsync(
            ShaclConstraintVocabulary.Disjoint.ToString(),
            ShaclComponentVocabulary.Disjoint,
            DisjointEvaluator.EvaluateAsync,
            valueLexicals: [1, 2, 3],
            comparisonLexicals: [3, 4, 5],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertResultCount(report, trace, dict, expected: 1);
    }

    [TestMethod]
    public async Task DisjointWithEmptyComparisonSetTriviallyConforms()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunPairAsync(
            ShaclConstraintVocabulary.Disjoint.ToString(),
            ShaclComponentVocabulary.Disjoint,
            DisjointEvaluator.EvaluateAsync,
            valueLexicals: [1, 2, 3],
            comparisonLexicals: [],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    //sh:lessThan — every value strictly less than every comparison-set value.

    [TestMethod]
    public async Task LessThanAcceptsAllValuesBelowAllComparison()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunPairAsync(
            ShaclConstraintVocabulary.LessThan.ToString(),
            ShaclComponentVocabulary.LessThan,
            LessThanEvaluator.EvaluateAsync,
            valueLexicals: [1, 2, 3],
            comparisonLexicals: [10, 20, 30],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task LessThanRejectsValueAtLeastOneNotBelow()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunPairAsync(
            ShaclConstraintVocabulary.LessThan.ToString(),
            ShaclComponentVocabulary.LessThan,
            LessThanEvaluator.EvaluateAsync,
            valueLexicals: [3, 5],
            comparisonLexicals: [5, 10],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertResultCount(report, trace, dict, expected: 1);
    }

    [TestMethod]
    public async Task LessThanWithEmptyComparisonIsVacuouslyTrue()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunPairAsync(
            ShaclConstraintVocabulary.LessThan.ToString(),
            ShaclComponentVocabulary.LessThan,
            LessThanEvaluator.EvaluateAsync,
            valueLexicals: [1, 2, 3],
            comparisonLexicals: [],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    //sh:lessThanOrEquals — every value <= every comparison-set value.

    [TestMethod]
    public async Task LessThanOrEqualsAcceptsEqualValueAndComparison()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunPairAsync(
            ShaclConstraintVocabulary.LessThanOrEquals.ToString(),
            ShaclComponentVocabulary.LessThanOrEquals,
            LessThanOrEqualsEvaluator.EvaluateAsync,
            valueLexicals: [5, 5],
            comparisonLexicals: [5, 5],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task LessThanOrEqualsRejectsValueAboveComparison()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunPairAsync(
            ShaclConstraintVocabulary.LessThanOrEquals.ToString(),
            ShaclComponentVocabulary.LessThanOrEquals,
            LessThanOrEqualsEvaluator.EvaluateAsync,
            valueLexicals: [10],
            comparisonLexicals: [5],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertResultCount(report, trace, dict, expected: 1);
    }

    //sh:closed — node-shape-level. Only allowed predicates may appear.

    [TestMethod]
    public async Task ClosedFalseIsTrivialPass()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunClosedAsync(
            closed: false,
            ignoredProperties: [],
            outgoingPredicateAndObjects: [(ExOther, IntLit(7))],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task ClosedTrueAcceptsTriplesOnDeclaredPath()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunClosedAsync(
            closed: true,
            ignoredProperties: [],
            outgoingPredicateAndObjects: [(ExPath, IntLit(7))],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task ClosedTrueRejectsTripleOnUndeclaredPath()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunClosedAsync(
            closed: true,
            ignoredProperties: [],
            outgoingPredicateAndObjects: [(ExOther, IntLit(7))],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertSingleViolationFromComponent(report, trace, dict, ShaclComponentVocabulary.Closed);
    }

    [TestMethod]
    public async Task ClosedTrueAcceptsTripleOnIgnoredProperty()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunClosedAsync(
            closed: true,
            ignoredProperties: [ExOther],
            outgoingPredicateAndObjects: [(ExOther, IntLit(7))],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    //Helpers below.

    private static Literal IntLit(int n)
        => new(Utf8Strings.From(n.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new NamedNode(Vocabulary.Xsd.Integer));

    private static Literal MakeLiteral(string lexical, Utf8String datatypeIri)
        => new(Utf8Strings.From(lexical), new NamedNode(datatypeIri));

    private static async Task<(ValidationReport, ValidationTrace, TermDictionary)> RunHasValueAsync(
        Literal requiredValue,
        IReadOnlyList<RdfTerm> values,
        CancellationToken cancellationToken)
    {
        TestShaclPipelineShapeState shapeState = TestShaclPipeline
            .BeginWithFocus(ExFocus)
            .WithPropertyShapeTargetingSubjectsOfPath(ExShape, ExPath)
            .With(ShaclConstraintVocabulary.HasValue.ToString(), requiredValue)
            .Done();

        TestShaclPipelineDataState dataState = await shapeState
            .BuildAsync(cancellationToken).ConfigureAwait(false);

        (ValidationReport report, ValidationTrace trace) = await dataState
            .WithTriplesOnFocus(ExPath, values)
            .WithEvaluator(ShaclComponentVocabulary.HasValue, HasValueEvaluator.EvaluateAsync)
            .RunWithTraceAsync(cancellationToken).ConfigureAwait(false);

        return (report, trace, dataState.Dictionary);
    }

    private static async Task<(ValidationReport, ValidationTrace, TermDictionary)> RunPairAsync(
        string constraintParameterIri,
        Utf8String componentIri,
        ConstraintEvaluator evaluator,
        IReadOnlyList<int> valueLexicals,
        IReadOnlyList<int> comparisonLexicals,
        CancellationToken cancellationToken)
    {
        TestShaclPipelineShapeState shapeState = TestShaclPipeline
            .BeginWithFocus(ExFocus)
            .WithPropertyShapeTargetingSubjectsOfPath(ExShape, ExPath)
            .With(constraintParameterIri, ShapeGraphBuilder.Iri(ExOther))
            .Done();

        TestShaclPipelineDataState dataState = await shapeState
            .BuildAsync(cancellationToken).ConfigureAwait(false);

        (ValidationReport report, ValidationTrace trace) = await dataState
            .WithTriplesOnFocus(ExPath, ToIntLiterals(valueLexicals))
            .WithTriplesOnFocus(ExOther, ToIntLiterals(comparisonLexicals))
            .WithEvaluator(componentIri, evaluator)
            .RunWithTraceAsync(cancellationToken).ConfigureAwait(false);

        return (report, trace, dataState.Dictionary);
    }

    private static async Task<(ValidationReport, ValidationTrace, TermDictionary)> RunClosedAsync(
        bool closed,
        IReadOnlyList<string> ignoredProperties,
        IReadOnlyList<(string Predicate, RdfTerm Object)> outgoingPredicateAndObjects,
        CancellationToken cancellationToken)
    {
        TestShaclPipelineShapeState scenario = TestShaclPipeline.BeginWithFocus(ExFocus);

        RdfTerm[] ignoredTerms = new RdfTerm[ignoredProperties.Count];
        for(int i = 0; i < ignoredProperties.Count; i++)
        {
            ignoredTerms[i] = ShapeGraphBuilder.Iri(ignoredProperties[i]);
        }
        RdfTerm ignoredList = scenario.Builder.List(ignoredTerms);

        TestShaclPipelineShapeState shapeState = scenario
            .WithUntargetedPropertyShape(ExPropShape, pathIri: ExPath)
            .WithNodeShapeTargetingPipelineFocus(ExShape)
            .With(ShaclConstraintVocabulary.Closed.ToString(), ShapeGraphBuilder.BoolLiteral(closed))
            .With(ShaclConstraintVocabulary.IgnoredProperties.ToString(), ignoredList)
            .With(ShaclConstraintVocabulary.Property.ToString(), ShapeGraphBuilder.Iri(ExPropShape))
            .Done();

        TestShaclPipelineDataState dataState = await shapeState
            .BuildAsync(cancellationToken).ConfigureAwait(false);

        foreach((string pred, RdfTerm obj) in outgoingPredicateAndObjects)
        {
            dataState = dataState.WithTripleOnFocus(pred, obj);
        }

        (ValidationReport report, ValidationTrace trace) = await dataState
            .WithEvaluator(ShaclComponentVocabulary.Property, PropertyEvaluator.EvaluateAsync)
            .WithEvaluator(ShaclComponentVocabulary.Closed, ClosedEvaluator.EvaluateAsync)
            .RunWithTraceAsync(cancellationToken).ConfigureAwait(false);

        return (report, trace, dataState.Dictionary);
    }

    private static RdfTerm[] ToIntLiterals(IReadOnlyList<int> values)
    {
        RdfTerm[] result = new RdfTerm[values.Count];
        for(int i = 0; i < values.Count; i++)
        {
            result[i] = IntLit(values[i]);
        }

        return result;
    }
}
