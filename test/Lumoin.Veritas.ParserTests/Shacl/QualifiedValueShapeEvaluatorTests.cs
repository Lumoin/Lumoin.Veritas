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
/// Tests for <see cref="QualifiedMinCountEvaluator"/> and
/// <see cref="QualifiedMaxCountEvaluator"/>.
/// </summary>
/// <remarks>
/// <para>
/// Both evaluators share the same scenario topology and the same
/// evaluator wiring. The only per-test differences are which count
/// parameter the constraint takes (<c>qualifiedMinCount</c> vs
/// <c>qualifiedMaxCount</c>), which component IRI fires, and which
/// evaluator delegate is registered. The shared <see cref="RunAsync"/>
/// helper parameterizes over those.
/// </para>
/// <para>
/// <b>Targeting strategy.</b> The scenario uses a node shape with
/// <c>sh:targetNode</c> that pins the focus directly, and references
/// the property shape via <c>sh:property</c>. This pins the focus
/// regardless of whether the data graph contains path triples, which
/// is the only way to exercise the empty-value-set semantics of the
/// qualified-cardinality evaluators correctly. Pinning via
/// <c>sh:targetSubjectsOf</c> (subjects of the path predicate) would
/// silently skip the entire shape when the data graph has no path
/// triples — the spec-correct behaviour, but the wrong scaffolding
/// for testing the constraint itself.
/// </para>
/// <para>
/// Inner shape: <c>sh:datatype xsd:integer</c> — a value conforms iff
/// it is an integer literal. Counting "how many values are integers"
/// keeps the test data readable.
/// </para>
/// </remarks>
[TestClass]
internal sealed class QualifiedValueShapeEvaluatorTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExFocus = "http://example.org/foo";
    private const string ExOuterShape = "http://example.org/Outer";
    private const string ExPropShape = "http://example.org/PS";
    private const string ExInnerShape = "http://example.org/Inner";
    private const string ExPath = "http://example.org/items";

    //sh:qualifiedMinCount tests.

    [TestMethod]
    public async Task QualifiedMinCountConformingCountAboveBoundPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.QualifiedMinCount.ToString(),
            countComponentIri: ShaclComponentVocabulary.QualifiedMinCount,
            evaluator: QualifiedMinCountEvaluator.EvaluateAsync,
            countValue: 2,
            disjoint: false,
            values: [IntLit(1), IntLit(2), IntLit(3)],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task QualifiedMinCountConformingCountAtBoundPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.QualifiedMinCount.ToString(),
            countComponentIri: ShaclComponentVocabulary.QualifiedMinCount,
            evaluator: QualifiedMinCountEvaluator.EvaluateAsync,
            countValue: 2,
            disjoint: false,
            values: [IntLit(1), IntLit(2)],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task QualifiedMinCountConformingCountBelowBoundFails()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.QualifiedMinCount.ToString(),
            countComponentIri: ShaclComponentVocabulary.QualifiedMinCount,
            evaluator: QualifiedMinCountEvaluator.EvaluateAsync,
            countValue: 3,
            disjoint: false,
            values: [IntLit(1), IntLit(2)],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertSingleViolationFromComponent(
            report, trace, dict, ShaclComponentVocabulary.QualifiedMinCount);
    }

    [TestMethod]
    public async Task QualifiedMinCountNonConformingValuesAreNotCounted()
    {
        //Three values; only two are integers; min=3 must fail because
        //the third value (a string literal) does not conform to the
        //inner shape and therefore does not contribute to the count.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.QualifiedMinCount.ToString(),
            countComponentIri: ShaclComponentVocabulary.QualifiedMinCount,
            evaluator: QualifiedMinCountEvaluator.EvaluateAsync,
            countValue: 3,
            disjoint: false,
            values: [IntLit(1), IntLit(2), StringLit("not-an-integer")],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertSingleViolationFromComponent(
            report, trace, dict, ShaclComponentVocabulary.QualifiedMinCount);
    }

    [TestMethod]
    public async Task QualifiedMinCountEmptyValueSetWithPositiveBoundFails()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.QualifiedMinCount.ToString(),
            countComponentIri: ShaclComponentVocabulary.QualifiedMinCount,
            evaluator: QualifiedMinCountEvaluator.EvaluateAsync,
            countValue: 1,
            disjoint: false,
            values: [],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertSingleViolationFromComponent(
            report, trace, dict, ShaclComponentVocabulary.QualifiedMinCount);
    }

    [TestMethod]
    public async Task QualifiedMinCountZeroBoundTriviallyPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.QualifiedMinCount.ToString(),
            countComponentIri: ShaclComponentVocabulary.QualifiedMinCount,
            evaluator: QualifiedMinCountEvaluator.EvaluateAsync,
            countValue: 0,
            disjoint: false,
            values: [],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    //sh:qualifiedMaxCount tests.

    [TestMethod]
    public async Task QualifiedMaxCountConformingCountBelowBoundPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.QualifiedMaxCount.ToString(),
            countComponentIri: ShaclComponentVocabulary.QualifiedMaxCount,
            evaluator: QualifiedMaxCountEvaluator.EvaluateAsync,
            countValue: 5,
            disjoint: false,
            values: [IntLit(1), IntLit(2)],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task QualifiedMaxCountConformingCountAtBoundPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.QualifiedMaxCount.ToString(),
            countComponentIri: ShaclComponentVocabulary.QualifiedMaxCount,
            evaluator: QualifiedMaxCountEvaluator.EvaluateAsync,
            countValue: 2,
            disjoint: false,
            values: [IntLit(1), IntLit(2)],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task QualifiedMaxCountConformingCountAboveBoundFails()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.QualifiedMaxCount.ToString(),
            countComponentIri: ShaclComponentVocabulary.QualifiedMaxCount,
            evaluator: QualifiedMaxCountEvaluator.EvaluateAsync,
            countValue: 2,
            disjoint: false,
            values: [IntLit(1), IntLit(2), IntLit(3)],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertSingleViolationFromComponent(
            report, trace, dict, ShaclComponentVocabulary.QualifiedMaxCount);
    }

    [TestMethod]
    public async Task QualifiedMaxCountNonConformingValuesAreNotCounted()
    {
        //Five values; only two are integers; max=2 passes because the
        //three string-literal values do not conform to the inner
        //shape and therefore do not contribute to the count.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.QualifiedMaxCount.ToString(),
            countComponentIri: ShaclComponentVocabulary.QualifiedMaxCount,
            evaluator: QualifiedMaxCountEvaluator.EvaluateAsync,
            countValue: 2,
            disjoint: false,
            values:
            [
                IntLit(1), IntLit(2),
                StringLit("a"), StringLit("b"), StringLit("c")
            ],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task QualifiedMaxCountEmptyValueSetTriviallyPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.QualifiedMaxCount.ToString(),
            countComponentIri: ShaclComponentVocabulary.QualifiedMaxCount,
            evaluator: QualifiedMaxCountEvaluator.EvaluateAsync,
            countValue: 0,
            disjoint: false,
            values: [],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task QualifiedMaxCountZeroBoundWithConformingValueFails()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.QualifiedMaxCount.ToString(),
            countComponentIri: ShaclComponentVocabulary.QualifiedMaxCount,
            evaluator: QualifiedMaxCountEvaluator.EvaluateAsync,
            countValue: 0,
            disjoint: false,
            values: [IntLit(1)],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertSingleViolationFromComponent(
            report, trace, dict, ShaclComponentVocabulary.QualifiedMaxCount);
    }

    //Helpers below.

    private static Literal IntLit(int n)
        => new(Utf8Strings.From(n.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new NamedNode(Vocabulary.Xsd.Integer));

    private static Literal StringLit(string s)
        => new(Utf8Strings.From(s), new NamedNode(Vocabulary.Xsd.String));

    //Builds the scenario:
    //
    //  ExOuterShape (NodeShape)
    //      sh:targetNode ExFocus
    //      sh:property ExPropShape
    //
    //  ExPropShape (PropertyShape)
    //      sh:path ExPath
    //      sh:qualifiedValueShape ExInnerShape
    //      sh:<countParameterIri> countValue
    //      sh:qualifiedValueShapesDisjoint disjoint
    //
    //  ExInnerShape (NodeShape)
    //      sh:datatype xsd:integer
    //
    //sh:targetNode pins the focus regardless of data, so qualified-
    //cardinality evaluators run even on empty value-node sets — the
    //case the spec requires for sh:qualifiedMinCount violations on
    //zero conforming values.
    private static async Task<(ValidationReport, ValidationTrace, TermDictionary)> RunAsync(
        string countParameterIri,
        Utf8String countComponentIri,
        ConstraintEvaluator evaluator,
        int countValue,
        bool disjoint,
        IReadOnlyList<RdfTerm> values,
        CancellationToken cancellationToken)
    {
        TestShaclPipelineShapeState scenario = TestShaclPipeline.BeginWithFocus(ExFocus);

        //Inner node shape: values must be xsd:integer.
        scenario = scenario
            .WithUntargetedNodeShape(ExInnerShape)
            .With(ShaclConstraintVocabulary.Datatype.ToString(),
                ShapeGraphBuilder.Iri(Vocabulary.Xsd.Integer.ToString()))
            .Done();

        //Property shape, declared once, with all qualified-cardinality
        //constraint parameters attached in one chain. Goes through the
        //builder directly because the test infrastructure's untargeted-
        //property-shape extension does not return a ShapeContext to
        //chain constraints onto.
        scenario.Builder.PropertyShape(ExPropShape, pathIri: ExPath)
            .With(ShaclConstraintVocabulary.QualifiedValueShape.ToString(),
                ShapeGraphBuilder.Iri(ExInnerShape))
            .With(countParameterIri, ShapeGraphBuilder.IntLiteral(countValue))
            .With(ShaclConstraintVocabulary.QualifiedValueShapesDisjoint.ToString(),
                ShapeGraphBuilder.BoolLiteral(disjoint));

        //Outer node shape pins the focus and links to the property
        //shape via sh:property. Pinning via sh:targetNode is essential
        //for the empty-value-set test case: it makes the validator
        //run constraints on the focus even when the data graph
        //contains zero path triples.
        TestShaclPipelineShapeState shapeState = scenario
            .WithNodeShapeTargetingPipelineFocus(ExOuterShape)
            .With(ShaclConstraintVocabulary.Property.ToString(),
                ShapeGraphBuilder.Iri(ExPropShape))
            .Done();

        TestShaclPipelineDataState dataState = await shapeState
            .BuildAsync(cancellationToken).ConfigureAwait(false);

        (ValidationReport report, ValidationTrace trace) = await dataState
            .WithTriplesOnFocus(ExPath, values)

            //sh:property dispatches the property-shape evaluation
            //from the outer node-shape's focus.
            .WithEvaluator(ShaclComponentVocabulary.Property, PropertyEvaluator.EvaluateAsync)
            .WithEvaluator(countComponentIri, evaluator)
            .WithEvaluator(ShaclComponentVocabulary.Datatype, DatatypeEvaluator.EvaluateAsync)
            .RunWithTraceAsync(cancellationToken).ConfigureAwait(false);

        return (report, trace, dataState.Dictionary);
    }
}
