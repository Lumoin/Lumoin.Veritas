using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
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
/// CsCheck-driven property tests for the four list-walking SHACL
/// evaluators: <see cref="MinListLengthEvaluator"/>,
/// <see cref="MaxListLengthEvaluator"/>,
/// <see cref="UniqueMembersEvaluator"/>, and
/// <see cref="MemberShapeEvaluator"/>. Per SHACL 1.2 Core §6.12.
/// </summary>
/// <remarks>
/// <para>
/// <b>List-detection invariant.</b> All four evaluators share the
/// same list-detection rule (encoded in
/// <see cref="RdfCollection.TryGetMembersAsync"/>): a value is a
/// SHACL list iff it is <c>rdf:nil</c> or has at least one outgoing
/// <c>rdf:first</c> triple. Non-list values are out of scope. The
/// <see cref="PropertyNonListLiteralValueProducesNoViolations"/>
/// test pins this interpretation across all four evaluators in one
/// place.
/// </para>
/// <para>
/// <b>Per-evaluator properties.</b> Each evaluator has a count-based
/// property: the number of violations equals a quantity computed
/// directly from the generated list shape. The reference is the
/// literal spec definition; mismatches indicate evaluator bugs, not
/// semantic disputes.
/// </para>
/// <para>
/// <b>Generator shape.</b> List length comes from <c>Gen.Int</c>;
/// list members are integer literals (homogeneous case) or a mix of
/// integer and string literals controlled by a bool array
/// (heterogeneous case for <see cref="MemberShapeEvaluator"/>). The
/// path predicate, focus, and shape IRIs are fixed per test for
/// readability.
/// </para>
/// <para>
/// <b>Async sampling.</b> The test pipeline is async; each property
/// drives CsCheck's <c>SampleAsync</c> with an async lambda that
/// awaits the run helper for each generated shape.
/// </para>
/// </remarks>
[TestClass]
internal sealed class ListWalkingEvaluatorPropertyTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExFocus = "http://example.org/foo";
    private const string ExOuterShape = "http://example.org/Outer";
    private const string ExPropShape = "http://example.org/PS";
    private const string ExInnerShape = "http://example.org/Inner";
    private const string ExPath = "http://example.org/items";

    [TestMethod]
    public async Task PropertyMinListLengthViolatesIffLengthBelowBound()
    {
        //For a SHACL list of length N and a bound B, the evaluator
        //violates iff N < B. Generate length and bound independently;
        //assert the violation count is exactly 1 when N < B and 0
        //otherwise.
        await Gen.Select(Gen.Int[0, 10], Gen.Int[0, 10]).SampleAsync(async t =>
        {
            (int length, int bound) = t;

            int violationCount = await RunListLengthAsync(
                ShaclConstraintVocabulary.MinListLength.ToString(),
                ShaclComponentVocabulary.MinListLength,
                MinListLengthEvaluator.EvaluateAsync,
                bound,
                length).ConfigureAwait(false);

            int expected = length < bound ? 1 : 0;
            Assert.AreEqual(expected, violationCount,
                $"Expected {expected} violations for length={length} below bound={bound}, got {violationCount}.");
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyMaxListLengthViolatesIffLengthAboveBound()
    {
        //For a SHACL list of length N and a bound B, the evaluator
        //violates iff N > B.
        await Gen.Select(Gen.Int[0, 10], Gen.Int[0, 10]).SampleAsync(async t =>
        {
            (int length, int bound) = t;

            int violationCount = await RunListLengthAsync(
                ShaclConstraintVocabulary.MaxListLength.ToString(),
                ShaclComponentVocabulary.MaxListLength,
                MaxListLengthEvaluator.EvaluateAsync,
                bound,
                length).ConfigureAwait(false);

            int expected = length > bound ? 1 : 0;
            Assert.AreEqual(expected, violationCount,
                $"Expected {expected} violations for length={length} above bound={bound}, got {violationCount}.");
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyUniqueMembersViolationCountEqualsDistinctDuplicatesCount()
    {
        //Generate an arbitrary list of integer values; the evaluator
        //emits one violation per distinct value that appears at
        //least twice. Reference: count distinct values whose
        //occurrence count is >= 2.
        await Gen.Int[0, 4].Array[0, 8].SampleAsync(async values =>
        {
            int expected = values
                .GroupBy(v => v)
                .Count(g => g.Count() >= 2);

            int violationCount = await RunUniqueMembersAsync(
                uniqueMembersFlag: true,
                values).ConfigureAwait(false);

            Assert.AreEqual(expected, violationCount,
                $"Expected {expected} distinct-duplicate violations for list [{string.Join(",", values)}], got {violationCount}.");
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyUniqueMembersInactiveFlagProducesNoViolations()
    {
        //When sh:uniqueMembers is false, the constraint is inactive
        //regardless of input. Even lists with many duplicates produce
        //zero violations.
        await Gen.Int[0, 4].Array[0, 8].SampleAsync(async values =>
        {
            int violationCount = await RunUniqueMembersAsync(
                uniqueMembersFlag: false,
                values).ConfigureAwait(false);

            Assert.AreEqual(0, violationCount,
                $"Inactive constraint must produce zero violations regardless of input. "
                + $"Got {violationCount} for list [{string.Join(",", values)}].");
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyMemberShapeViolationCountEqualsNonConformingMemberCount()
    {
        //Inner shape requires sh:datatype xsd:integer. Generate a
        //list where some members are integer literals and others
        //are string literals; the evaluator emits one outer
        //violation per non-integer member. Reference: count of
        //bool[i] == false (string member positions).
        await Gen.Bool.Array[0, 8].SampleAsync(async isIntegerByMember =>
        {
            int expected = isIntegerByMember.Count(b => !b);

            int violationCount = await RunMemberShapeAsync(
                isIntegerByMember).ConfigureAwait(false);

            Assert.AreEqual(expected, violationCount,
                $"Expected {expected} non-conforming-member violations for member-types "
                + $"[{string.Join(",", isIntegerByMember.Select(b => b ? "int" : "str"))}], got {violationCount}.");
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyNonListLiteralValueProducesNoViolations()
    {
        //All four list-walking evaluators must skip non-list values
        //per the spec's "non-list value nodes are out of scope"
        //interpretation (encoded in RdfCollection.TryGetMembersAsync
        //returning null for such values). Generate a string-literal
        //value and assert each evaluator emits zero results
        //regardless of its bound or flag setting.
        await Gen.Int[0, 5].SampleAsync(async bound =>
        {
            int minCount = await RunListLengthRawValueAsync(
                ShaclConstraintVocabulary.MinListLength.ToString(),
                ShaclComponentVocabulary.MinListLength,
                MinListLengthEvaluator.EvaluateAsync,
                bound).ConfigureAwait(false);

            int maxCount = await RunListLengthRawValueAsync(
                ShaclConstraintVocabulary.MaxListLength.ToString(),
                ShaclComponentVocabulary.MaxListLength,
                MaxListLengthEvaluator.EvaluateAsync,
                bound).ConfigureAwait(false);

            int uniqueCount = await RunUniqueMembersRawValueAsync(
                uniqueMembersFlag: true).ConfigureAwait(false);

            int memberCount = await RunMemberShapeRawValueAsync().ConfigureAwait(false);

            Assert.AreEqual(0, minCount,
                $"MinListLength must not violate on non-list value. Bound={bound}.");
            Assert.AreEqual(0, maxCount,
                $"MaxListLength must not violate on non-list value. Bound={bound}.");
            Assert.AreEqual(0, uniqueCount,
                "UniqueMembers must not violate on non-list value.");
            Assert.AreEqual(0, memberCount,
                "MemberShape must not violate on non-list value.");
        }).ConfigureAwait(false);
    }

    //Helpers below.

    //Runs a list-cardinality evaluator (MinListLength / MaxListLength)
    //against a generated list of the given length, with the given
    //bound. Returns the count of violation results.
    private async Task<int> RunListLengthAsync(
        string countParameterIri,
        Utf8String countComponentIri,
        ConstraintEvaluator evaluator,
        int bound,
        int length)
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
            .BuildAsync(TestContext.CancellationToken).ConfigureAwait(false);

        RdfTerm[] members = new RdfTerm[length];
        for(int i = 0; i < length; i++)
        {
            members[i] = ShapeGraphBuilder.IntLiteral(i);
        }

        dataState = TestRdfList.Assemble(dataState, ExFocus, ExPath, members);

        (ValidationReport report, ValidationTrace _) = await dataState
            .WithEvaluator(ShaclComponentVocabulary.Property, PropertyEvaluator.EvaluateAsync)
            .WithEvaluator(countComponentIri, evaluator)
            .RunWithTraceAsync(TestContext.CancellationToken).ConfigureAwait(false);

        return CountViolations(report);
    }

    //Runs UniqueMembersEvaluator against a generated list of integer
    //values. The list may contain duplicates.
    private async Task<int> RunUniqueMembersAsync(
        bool uniqueMembersFlag,
        int[] values)
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
            .BuildAsync(TestContext.CancellationToken).ConfigureAwait(false);

        RdfTerm[] members = new RdfTerm[values.Length];
        for(int i = 0; i < values.Length; i++)
        {
            members[i] = ShapeGraphBuilder.IntLiteral(values[i]);
        }

        dataState = TestRdfList.Assemble(dataState, ExFocus, ExPath, members);

        (ValidationReport report, ValidationTrace _) = await dataState
            .WithEvaluator(ShaclComponentVocabulary.Property, PropertyEvaluator.EvaluateAsync)
            .WithEvaluator(ShaclComponentVocabulary.UniqueMembers, UniqueMembersEvaluator.EvaluateAsync)
            .RunWithTraceAsync(TestContext.CancellationToken).ConfigureAwait(false);

        return CountViolations(report);
    }

    //Runs MemberShapeEvaluator with an inner shape that requires
    //sh:datatype xsd:integer. Members where isIntegerByMember[i] is
    //true are integer literals (conforming); members where it's
    //false are string literals (failing inner shape).
    private async Task<int> RunMemberShapeAsync(bool[] isIntegerByMember)
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
            .BuildAsync(TestContext.CancellationToken).ConfigureAwait(false);

        RdfTerm[] members = new RdfTerm[isIntegerByMember.Length];
        for(int i = 0; i < isIntegerByMember.Length; i++)
        {
            members[i] = isIntegerByMember[i]
                ? ShapeGraphBuilder.IntLiteral(i)
                : ShapeGraphBuilder.StringLiteral($"str{i}");
        }

        dataState = TestRdfList.Assemble(dataState, ExFocus, ExPath, members);

        (ValidationReport report, ValidationTrace _) = await dataState
            .WithEvaluator(ShaclComponentVocabulary.Property, PropertyEvaluator.EvaluateAsync)
            .WithEvaluator(ShaclComponentVocabulary.MemberShape, MemberShapeEvaluator.EvaluateAsync)
            .WithEvaluator(ShaclComponentVocabulary.Datatype, DatatypeEvaluator.EvaluateAsync)
            .RunWithTraceAsync(TestContext.CancellationToken).ConfigureAwait(false);

        return CountViolations(report);
    }

    //Runs a list-cardinality evaluator with a non-list value
    //(string literal directly at the path). Used by the
    //out-of-scope property test.
    private async Task<int> RunListLengthRawValueAsync(
        string countParameterIri,
        Utf8String countComponentIri,
        ConstraintEvaluator evaluator,
        int bound)
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
            .BuildAsync(TestContext.CancellationToken).ConfigureAwait(false);

        dataState = dataState.WithTripleOnFocus(
            ExPath, ShapeGraphBuilder.StringLiteral("not-a-list"));

        (ValidationReport report, ValidationTrace _) = await dataState
            .WithEvaluator(ShaclComponentVocabulary.Property, PropertyEvaluator.EvaluateAsync)
            .WithEvaluator(countComponentIri, evaluator)
            .RunWithTraceAsync(TestContext.CancellationToken).ConfigureAwait(false);

        return CountViolations(report);
    }

    //Runs UniqueMembersEvaluator with a non-list value. Used by the
    //out-of-scope property test.
    private async Task<int> RunUniqueMembersRawValueAsync(bool uniqueMembersFlag)
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
            .BuildAsync(TestContext.CancellationToken).ConfigureAwait(false);

        dataState = dataState.WithTripleOnFocus(
            ExPath, ShapeGraphBuilder.StringLiteral("not-a-list"));

        (ValidationReport report, ValidationTrace _) = await dataState
            .WithEvaluator(ShaclComponentVocabulary.Property, PropertyEvaluator.EvaluateAsync)
            .WithEvaluator(ShaclComponentVocabulary.UniqueMembers, UniqueMembersEvaluator.EvaluateAsync)
            .RunWithTraceAsync(TestContext.CancellationToken).ConfigureAwait(false);

        return CountViolations(report);
    }

    //Runs MemberShapeEvaluator with a non-list value. Used by the
    //out-of-scope property test.
    private async Task<int> RunMemberShapeRawValueAsync()
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
            .BuildAsync(TestContext.CancellationToken).ConfigureAwait(false);

        dataState = dataState.WithTripleOnFocus(
            ExPath, ShapeGraphBuilder.StringLiteral("not-a-list"));

        (ValidationReport report, ValidationTrace _) = await dataState
            .WithEvaluator(ShaclComponentVocabulary.Property, PropertyEvaluator.EvaluateAsync)
            .WithEvaluator(ShaclComponentVocabulary.MemberShape, MemberShapeEvaluator.EvaluateAsync)
            .WithEvaluator(ShaclComponentVocabulary.Datatype, DatatypeEvaluator.EvaluateAsync)
            .RunWithTraceAsync(TestContext.CancellationToken).ConfigureAwait(false);

        return CountViolations(report);
    }

    private static int CountViolations(ValidationReport report)
    {
        int count = 0;
        foreach(ValidationResult result in report.Results)
        {
            if(result.Severity == Severity.Violation)
            {
                count++;
            }
        }

        return count;
    }
}
