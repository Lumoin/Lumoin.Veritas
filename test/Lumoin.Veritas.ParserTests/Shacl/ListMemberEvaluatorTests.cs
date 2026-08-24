using System.Collections.Generic;
using System.Linq;
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
/// Tests for <see cref="UniqueMembersEvaluator"/> and
/// <see cref="MemberShapeEvaluator"/>. Both apply per SHACL 1.2 Core
/// §6.12 to value nodes that are SHACL lists; non-list value nodes
/// are out of scope.
/// </summary>
/// <remarks>
/// <para>
/// Combined class following the pair-1 pattern. The list-assembly
/// helper is shared; each evaluator has a dedicated runner that wires
/// its specific constraint parameter (a boolean literal for
/// <c>sh:uniqueMembers</c>, an IRI reference for
/// <c>sh:memberShape</c>).
/// </para>
/// <para>
/// MemberShape tests build an inner property shape that pins a
/// <c>sh:datatype</c> of <c>xsd:integer</c> on a synthetic per-member
/// path; this lets the inner-shape recursion produce predictable
/// pass/fail outcomes per list member.
/// </para>
/// </remarks>
[TestClass]
internal sealed class ListMemberEvaluatorTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExFocus = "http://example.org/foo";
    private const string ExOuterShape = "http://example.org/Outer";
    private const string ExPropShape = "http://example.org/PS";
    private const string ExPath = "http://example.org/items";

    //sh:uniqueMembers tests.

    [TestMethod]
    public async Task UniqueMembersFlagFalseIsInactive()
    {
        //Even with duplicates in the list, the constraint is inactive
        //when the flag is false. No results expected.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunUniqueMembersAsync(
            uniqueMembersFlag: false,
            listMembers: [1, 2, 1, 2],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task UniqueMembersDistinctListPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunUniqueMembersAsync(
            uniqueMembersFlag: true,
            listMembers: [1, 2, 3, 4],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task UniqueMembersEmptyListPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunUniqueMembersAsync(
            uniqueMembersFlag: true,
            listMembers: [],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task UniqueMembersSingleDuplicateProducesOneResult()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary _) = await RunUniqueMembersAsync(
            uniqueMembersFlag: true,
            listMembers: [1, 2, 1, 3],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms,
            $"Expected non-conformance for duplicate member; trace:\n{trace}");
        Assert.HasCount(1, report.Results,
            "Expected exactly one result for the single duplicate value.");
        Assert.AreEqual(
            ShaclComponentVocabulary.UniqueMembers,
            report.Results[0].SourceConstraintComponent,
            "Source constraint component should be sh:UniqueMembersConstraintComponent.");
    }

    [TestMethod]
    public async Task UniqueMembersTripleOccurrenceProducesOneResultPerDistinctValue()
    {
        //List [1, 2, 1, 1] — value 1 appears 3 times. Per the
        //evaluator's per-distinct-duplicated-value semantics, exactly
        //one result is emitted (not two — the third occurrence does
        //not produce a separate result).
        (ValidationReport report, ValidationTrace trace, TermDictionary _) = await RunUniqueMembersAsync(
            uniqueMembersFlag: true,
            listMembers: [1, 2, 1, 1],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms,
            $"Expected non-conformance; trace:\n{trace}");
        Assert.HasCount(1, report.Results,
            "Expected one result per distinct duplicated value, not per duplicate occurrence.");
    }

    [TestMethod]
    public async Task UniqueMembersTwoDistinctDuplicatesProduceTwoResults()
    {
        //List [1, 2, 1, 2] — both 1 and 2 are duplicated.
        //Two distinct duplicated values → two results.
        (ValidationReport report, ValidationTrace trace, TermDictionary _) = await RunUniqueMembersAsync(
            uniqueMembersFlag: true,
            listMembers: [1, 2, 1, 2],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms,
            $"Expected non-conformance; trace:\n{trace}");
        Assert.HasCount(2, report.Results,
            "Expected one result per distinct duplicated value.");
    }

    [TestMethod]
    public async Task UniqueMembersNonListLiteralValueIsOutOfScopeAndPasses()
    {
        //Pin the spec interpretation: non-list value nodes are out of
        //scope of sh:uniqueMembers, even when the flag is true.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunUniqueMembersWithRawValueAsync(
            uniqueMembersFlag: true,
            value: new Literal(Utf8Strings.From("not-a-list"),
                new NamedNode(Vocabulary.Xsd.String)),
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    //sh:memberShape tests. The inner shape requires xsd:integer on
    //the value (each list member). Members that are not integer
    //literals fail; integer-literal members pass.

    [TestMethod]
    public async Task MemberShapeAllMembersConformPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunMemberShapeAsync(
            listMembers: [ShapeGraphBuilder.IntLiteral(1), ShapeGraphBuilder.IntLiteral(2), ShapeGraphBuilder.IntLiteral(3)],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task MemberShapeEmptyListPasses()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunMemberShapeAsync(
            listMembers: [],
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    [TestMethod]
    public async Task MemberShapeNonConformingMemberProducesOneResult()
    {
        //One non-integer member (a string literal) violates the
        //inner shape's sh:datatype xsd:integer constraint. One outer
        //result expected, attributed to MemberShapeConstraintComponent
        //(not to DatatypeConstraintComponent — inner results are not
        //surfaced per NodeEvaluator-style result shape).
        (ValidationReport report, ValidationTrace trace, TermDictionary _) = await RunMemberShapeAsync(
            listMembers: [
                ShapeGraphBuilder.IntLiteral(1),
                new Literal(Utf8Strings.From("nope"), new NamedNode(Vocabulary.Xsd.String)),
                ShapeGraphBuilder.IntLiteral(3)
            ],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms,
            $"Expected non-conformance; trace:\n{trace}");
        Assert.HasCount(1, report.Results,
            "Expected one outer result for the single non-conforming member.");
        Assert.AreEqual(
            ShaclComponentVocabulary.MemberShape,
            report.Results[0].SourceConstraintComponent,
            "Source constraint component should be sh:MemberShapeConstraintComponent " +
            "(inner Datatype results must not be surfaced).");
    }

    [TestMethod]
    public async Task MemberShapeMultipleNonConformingMembersProduceMultipleResults()
    {
        (ValidationReport report, ValidationTrace trace, TermDictionary _) = await RunMemberShapeAsync(
            listMembers: [
                new Literal(Utf8Strings.From("a"), new NamedNode(Vocabulary.Xsd.String)),
                ShapeGraphBuilder.IntLiteral(2),
                new Literal(Utf8Strings.From("b"), new NamedNode(Vocabulary.Xsd.String))
            ],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms,
            $"Expected non-conformance; trace:\n{trace}");
        Assert.HasCount(2, report.Results,
            "Expected one outer result per non-conforming member.");
        Assert.IsTrue(
            report.Results.All(r => r.SourceConstraintComponent.Equals(ShaclComponentVocabulary.MemberShape)),
            "All results must attribute to sh:MemberShapeConstraintComponent.");
    }

    [TestMethod]
    public async Task MemberShapeNonListLiteralValueIsOutOfScopeAndPasses()
    {
        //Pin the spec interpretation: non-list value nodes are out
        //of scope of sh:memberShape.
        (ValidationReport report, ValidationTrace trace, TermDictionary dict) = await RunMemberShapeWithRawValueAsync(
            value: new Literal(Utf8Strings.From("not-a-list"),
                new NamedNode(Vocabulary.Xsd.String)),
            TestContext.CancellationToken).ConfigureAwait(false);

        ValidationAssertions.AssertConforms(report, trace, dict);
    }

    //Helpers below.

    private const string ExInnerShape = "http://example.org/Inner";

    //sh:uniqueMembers runner. Builds the same outer/property-shape
    //scaffold as the list-length tests, sets sh:uniqueMembers to
    //the boolean literal, and assembles an integer-literal list.
    private static async Task<(ValidationReport, ValidationTrace, TermDictionary)> RunUniqueMembersAsync(
        bool uniqueMembersFlag,
        IReadOnlyList<int> listMembers,
        CancellationToken cancellationToken)
    {
        TestShaclPipelineShapeState scenario = TestShaclPipeline.BeginWithFocus(ExFocus);

        scenario.Builder.PropertyShape(ExPropShape, pathIri: ExPath)
            .With(ShaclConstraintVocabulary.UniqueMembers.ToString(),
                ShapeGraphBuilder.BoolLiteral(uniqueMembersFlag));

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
            .WithEvaluator(ShaclComponentVocabulary.UniqueMembers, UniqueMembersEvaluator.EvaluateAsync)
            .RunWithTraceAsync(cancellationToken).ConfigureAwait(false);

        return (report, trace, dataState.Dictionary);
    }

    private static async Task<(ValidationReport, ValidationTrace, TermDictionary)> RunUniqueMembersWithRawValueAsync(
        bool uniqueMembersFlag,
        RdfTerm value,
        CancellationToken cancellationToken)
    {
        TestShaclPipelineShapeState scenario = TestShaclPipeline.BeginWithFocus(ExFocus);

        scenario.Builder.PropertyShape(ExPropShape, pathIri: ExPath)
            .With(ShaclConstraintVocabulary.UniqueMembers.ToString(),
                ShapeGraphBuilder.BoolLiteral(uniqueMembersFlag));

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
            .WithEvaluator(ShaclComponentVocabulary.UniqueMembers, UniqueMembersEvaluator.EvaluateAsync)
            .RunWithTraceAsync(cancellationToken).ConfigureAwait(false);

        return (report, trace, dataState.Dictionary);
    }

    //sh:memberShape runner. The inner node shape ExInnerShape is
    //declared in the shape graph with a single sh:datatype
    //xsd:integer constraint, applied to the focus itself (the list
    //member, when recursed by the evaluator). Members are arbitrary
    //RdfTerms — typically a mix of integer and string literals to
    //exercise pass/fail per member.
    private static async Task<(ValidationReport, ValidationTrace, TermDictionary)> RunMemberShapeAsync(
        IReadOnlyList<RdfTerm> listMembers,
        CancellationToken cancellationToken)
    {
        TestShaclPipelineShapeState scenario = TestShaclPipeline.BeginWithFocus(ExFocus);

        //Inner node shape: each list member, treated as a focus,
        //must be an xsd:integer literal.
        scenario.Builder.NodeShape(ExInnerShape)
            .With(ShaclConstraintVocabulary.Datatype.ToString(),
                ShapeGraphBuilder.Iri(Vocabulary.Xsd.Integer.ToString()));

        scenario.Builder.PropertyShape(ExPropShape, pathIri: ExPath)
            .With(ShaclConstraintVocabulary.MemberShape.ToString(),
                ShapeGraphBuilder.Iri(ExInnerShape));

        TestShaclPipelineShapeState shapeState = scenario
            .WithNodeShapeTargetingPipelineFocus(ExOuterShape)
            .With(ShaclConstraintVocabulary.Property.ToString(),
                ShapeGraphBuilder.Iri(ExPropShape))
            .Done();

        TestShaclPipelineDataState dataState = await shapeState
            .BuildAsync(cancellationToken).ConfigureAwait(false);

        dataState = TestRdfList.Assemble(dataState, ExFocus, ExPath, listMembers);

        (ValidationReport report, ValidationTrace trace) = await dataState
            .WithEvaluator(ShaclComponentVocabulary.Property, PropertyEvaluator.EvaluateAsync)
            .WithEvaluator(ShaclComponentVocabulary.MemberShape, MemberShapeEvaluator.EvaluateAsync)
            .WithEvaluator(ShaclComponentVocabulary.Datatype, DatatypeEvaluator.EvaluateAsync)
            .RunWithTraceAsync(cancellationToken).ConfigureAwait(false);

        return (report, trace, dataState.Dictionary);
    }

    private static async Task<(ValidationReport, ValidationTrace, TermDictionary)> RunMemberShapeWithRawValueAsync(
        RdfTerm value,
        CancellationToken cancellationToken)
    {
        TestShaclPipelineShapeState scenario = TestShaclPipeline.BeginWithFocus(ExFocus);

        scenario.Builder.NodeShape(ExInnerShape)
            .With(ShaclConstraintVocabulary.Datatype.ToString(),
                ShapeGraphBuilder.Iri(Vocabulary.Xsd.Integer.ToString()));

        scenario.Builder.PropertyShape(ExPropShape, pathIri: ExPath)
            .With(ShaclConstraintVocabulary.MemberShape.ToString(),
                ShapeGraphBuilder.Iri(ExInnerShape));

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
            .WithEvaluator(ShaclComponentVocabulary.MemberShape, MemberShapeEvaluator.EvaluateAsync)
            .WithEvaluator(ShaclComponentVocabulary.Datatype, DatatypeEvaluator.EvaluateAsync)
            .RunWithTraceAsync(cancellationToken).ConfigureAwait(false);

        return (report, trace, dataState.Dictionary);
    }
}
