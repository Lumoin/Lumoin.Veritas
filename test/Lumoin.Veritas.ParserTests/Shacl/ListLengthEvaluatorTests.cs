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
/// Tests for <see cref="MinListLengthEvaluator"/> and
/// <see cref="MaxListLengthEvaluator"/>. Both evaluators apply per
/// SHACL 1.2 Core §6.12: each value node that is a SHACL list must
/// satisfy the corresponding bound; non-list value nodes are out of
/// scope.
/// </summary>
/// <remarks>
/// Combined class following the qualified-value-shape pattern: one
/// <see cref="RunAsync"/> helper parameterized over the constraint
/// parameter IRI, component IRI, and evaluator delegate. The fixture
/// builds an rdf:list chain of integer literals using synthetic IRIs
/// for the list cells; the validator does not care whether list cells
/// are IRIs or blank nodes.
/// </remarks>
[TestClass]
internal sealed class ListLengthEvaluatorTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExFocus = "http://example.org/foo";
    private const string ExOuterShape = "http://example.org/Outer";
    private const string ExPropShape = "http://example.org/PS";
    private const string ExPath = "http://example.org/items";

    //sh:minListLength tests.

    [TestMethod]
    public async Task MinListLengthListMeetsBoundPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.MinListLength.ToString(),
            countComponentIri: ShaclComponentVocabulary.MinListLength,
            evaluator: MinListLengthEvaluator.EvaluateAsync,
            bound: 3,
            listMembers: [1, 2, 3, 4],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task MinListLengthListAtBoundPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.MinListLength.ToString(),
            countComponentIri: ShaclComponentVocabulary.MinListLength,
            evaluator: MinListLengthEvaluator.EvaluateAsync,
            bound: 3,
            listMembers: [1, 2, 3],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task MinListLengthListBelowBoundFails()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.MinListLength.ToString(),
            countComponentIri: ShaclComponentVocabulary.MinListLength,
            evaluator: MinListLengthEvaluator.EvaluateAsync,
            bound: 3,
            listMembers: [1, 2],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertSingleViolationFromComponent(
            report, trace, dict, ShaclComponentVocabulary.MinListLength);
    }

    [TestMethod]
    public async Task MinListLengthEmptyListBelowPositiveBoundFails()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.MinListLength.ToString(),
            countComponentIri: ShaclComponentVocabulary.MinListLength,
            evaluator: MinListLengthEvaluator.EvaluateAsync,
            bound: 1,
            listMembers: [],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertSingleViolationFromComponent(
            report, trace, dict, ShaclComponentVocabulary.MinListLength);
    }

    [TestMethod]
    public async Task MinListLengthEmptyListAtZeroBoundPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.MinListLength.ToString(),
            countComponentIri: ShaclComponentVocabulary.MinListLength,
            evaluator: MinListLengthEvaluator.EvaluateAsync,
            bound: 0,
            listMembers: [],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    //sh:maxListLength tests.

    [TestMethod]
    public async Task MaxListLengthListWithinBoundPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.MaxListLength.ToString(),
            countComponentIri: ShaclComponentVocabulary.MaxListLength,
            evaluator: MaxListLengthEvaluator.EvaluateAsync,
            bound: 5,
            listMembers: [1, 2, 3],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task MaxListLengthListAtBoundPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.MaxListLength.ToString(),
            countComponentIri: ShaclComponentVocabulary.MaxListLength,
            evaluator: MaxListLengthEvaluator.EvaluateAsync,
            bound: 3,
            listMembers: [1, 2, 3],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task MaxListLengthListAboveBoundFails()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.MaxListLength.ToString(),
            countComponentIri: ShaclComponentVocabulary.MaxListLength,
            evaluator: MaxListLengthEvaluator.EvaluateAsync,
            bound: 2,
            listMembers: [1, 2, 3],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertSingleViolationFromComponent(
            report, trace, dict, ShaclComponentVocabulary.MaxListLength);
    }

    [TestMethod]
    public async Task MaxListLengthEmptyListPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsync(
            countParameterIri: ShaclConstraintVocabulary.MaxListLength.ToString(),
            countComponentIri: ShaclComponentVocabulary.MaxListLength,
            evaluator: MaxListLengthEvaluator.EvaluateAsync,
            bound: 0,
            listMembers: [],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    //Non-list value nodes are out of scope per SHACL 1.2 Core §6.12.
    //A literal value with MinListLength = 1 must NOT fire — the
    //constraint applies only when the value is a SHACL list. These
    //two tests pin that interpretation against accidental drift.

    [TestMethod]
    public async Task MinListLengthNonListLiteralValueIsOutOfScopeAndPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsyncWithRawValue(
            countParameterIri: ShaclConstraintVocabulary.MinListLength.ToString(),
            countComponentIri: ShaclComponentVocabulary.MinListLength,
            evaluator: MinListLengthEvaluator.EvaluateAsync,
            bound: 1,
            value: new Literal(Utf8Strings.From("not-a-list"),
                new NamedNode(Vocabulary.Xsd.String)),
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task MaxListLengthNonListLiteralValueIsOutOfScopeAndPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunAsyncWithRawValue(
            countParameterIri: ShaclConstraintVocabulary.MaxListLength.ToString(),
            countComponentIri: ShaclComponentVocabulary.MaxListLength,
            evaluator: MaxListLengthEvaluator.EvaluateAsync,
            bound: 0,
            value: new Literal(Utf8Strings.From("not-a-list"),
                new NamedNode(Vocabulary.Xsd.String)),
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    //Helpers below.

    //Builds:
    //  ExOuterShape (NodeShape)
    //      sh:targetNode ExFocus
    //      sh:property ExPropShape
    //  ExPropShape (PropertyShape)
    //      sh:path ExPath
    //      sh:<countParameterIri> bound
    //
    //Data: TestRdfList.Assemble emits the focus-to-list-head triple
    //plus one rdf:first / rdf:rest pair per member, terminating at
    //rdf:nil. An empty member list materialises as a single
    //(focus, path, rdf:nil) triple.
    private static async Task<(ValidationReport, ValidationTrace, TermDictionary)> RunAsync(
        string countParameterIri,
        Utf8String countComponentIri,
        ConstraintEvaluator evaluator,
        int bound,
        IReadOnlyList<int> listMembers,
        CancellationToken cancellationToken)
    {
        TestShaclPipelineShapeState scenario = TestShaclPipeline.BeginWithFocus(ExFocus);

        scenario.Builder.PropertyShape(ExPropShape, pathIri: ExPath)
            .With(countParameterIri, ShapeGraphBuilder.IntLiteral(bound));

        TestShaclPipelineShapeState shapeState = scenario
            .WithNodeShapeTargetingPipelineFocus(ExOuterShape)
            .With(ShaclConstraintVocabulary.Property.ToString(),
                ShapeGraphBuilder.Iri(ExPropShape))
            .Done();

        TestShaclPipelineDataState dataState = await shapeState
            .BuildAsync(cancellationToken).ConfigureAwait(false);

        RdfTerm[] memberTerms = new RdfTerm[listMembers.Count];
        for(int i = 0; i < listMembers.Count; i++)
        {
            memberTerms[i] = ShapeGraphBuilder.IntLiteral(listMembers[i]);
        }

        dataState = TestRdfList.Assemble(dataState, ExFocus, ExPath, memberTerms);

        (ValidationReport report, ValidationTrace trace) = await dataState
            .WithEvaluator(ShaclComponentVocabulary.Property, PropertyEvaluator.EvaluateAsync)
            .WithEvaluator(countComponentIri, evaluator)
            .RunWithTraceAsync(cancellationToken).ConfigureAwait(false);

        return (report, trace, dataState.Dictionary);
    }

    //Variant for the non-list-out-of-scope tests. Emits a single
    //triple (focus, path, value) where value is an arbitrary RdfTerm
    //— typically a literal that does not look like a SHACL list at
    //all. The evaluator must skip the value silently and the report
    //must conform.
    private static async Task<(ValidationReport, ValidationTrace, TermDictionary)> RunAsyncWithRawValue(
        string countParameterIri,
        Utf8String countComponentIri,
        ConstraintEvaluator evaluator,
        int bound,
        RdfTerm value,
        CancellationToken cancellationToken)
    {
        TestShaclPipelineShapeState scenario = TestShaclPipeline.BeginWithFocus(ExFocus);

        scenario.Builder.PropertyShape(ExPropShape, pathIri: ExPath)
            .With(countParameterIri, ShapeGraphBuilder.IntLiteral(bound));

        TestShaclPipelineShapeState shapeState = scenario
            .WithNodeShapeTargetingPipelineFocus(ExOuterShape)
            .With(ShaclConstraintVocabulary.Property.ToString(),
                ShapeGraphBuilder.Iri(ExPropShape))
            .Done();

        TestShaclPipelineDataState dataState = await shapeState
            .BuildAsync(cancellationToken).ConfigureAwait(false);

        dataState = dataState.WithTripleOnFocus(ExPath, value);

        (ValidationReport report, ValidationTrace trace) = await dataState
            .WithEvaluator(ShaclComponentVocabulary.Property, PropertyEvaluator.EvaluateAsync)
            .WithEvaluator(countComponentIri, evaluator)
            .RunWithTraceAsync(cancellationToken).ConfigureAwait(false);

        return (report, trace, dataState.Dictionary);
    }
}
