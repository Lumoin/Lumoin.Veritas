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
/// CsCheck-driven property tests for the five SHACL property-pair
/// evaluators: <see cref="EqualsEvaluator"/>,
/// <see cref="DisjointEvaluator"/>, <see cref="SubsetOfEvaluator"/>,
/// <see cref="LessThanEvaluator"/>, and
/// <see cref="LessThanOrEqualsEvaluator"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Property categories.</b> The set-relation evaluators (Equals,
/// Disjoint, SubsetOf) admit clean reference implementations as set
/// arithmetic on integer-valued generators. The ordering evaluators
/// (LessThan, LessThanOrEquals) rely on SPARQL-style comparison
/// semantics whose reference implementation would essentially
/// reimplement the evaluator; instead, those evaluators are pinned
/// by structural invariants — the empty-side rule (no violations
/// when either side has no values) is a sound consequence of the
/// universally-quantified spec definition.
/// </para>
/// <para>
/// <b>Generator shape.</b> One focus, two predicates, V distinct
/// integer values. The <c>Gen.Bool.Array[2*V, 2*V]</c> bitmap encodes
/// "value <c>v</c> at predicate A" in the first half and "value
/// <c>v</c> at predicate B" in the second half. Each Sample
/// iteration constructs one (setA, setB) pair and checks the
/// evaluator against the reference.
/// </para>
/// <para>
/// <b>Async sampling.</b> The pipeline is async; each property drives
/// CsCheck's <c>SampleAsync</c> with an async lambda that awaits
/// <c>RunPairConstraintAsync</c> once per iteration.
/// </para>
/// </remarks>
[TestClass]
internal sealed class PairPropertyEvaluatorPropertyTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExFocus = "http://example.org/focus";
    private const string ExShape = "http://example.org/Shape";
    private const string ExPredA = "http://example.org/pa";
    private const string ExPredB = "http://example.org/pb";
    private const int ValueCount = 4;

    [TestMethod]
    public async Task PropertyEqualsViolationCountEqualsSymmetricDifference()
    {
        //sh:equals (§4.4.1): one violation per value in the
        //symmetric difference of the two value sets. Reference:
        //|setA △ setB|.
        await Gen.Bool.Array[2 * ValueCount, 2 * ValueCount].SampleAsync(async bits =>
        {
            (HashSet<int> setA, HashSet<int> setB) = DecodeSets(bits);

            int expected = setA.Union(setB).Count() - setA.Intersect(setB).Count();

            int actual = await RunPairConstraintAsync(
                ShaclConstraintVocabulary.EqualsTo.ToString(),
                ShaclComponentVocabulary.EqualsTo,
                EqualsEvaluator.EvaluateAsync,
                setA, setB).ConfigureAwait(false);

            Assert.AreEqual(expected, actual,
                $"Equals: expected {expected} symmetric-diff violations for "
                + $"setA={Format(setA)} setB={Format(setB)}, got {actual}.");
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyDisjointViolatesIffIntersectionNonEmpty()
    {
        //sh:disjoint (§4.4.2): the value sets must not overlap.
        //Boolean property: violations > 0 iff intersection is
        //non-empty. The exact per-violation count is left to the
        //evaluator's implementation (the spec phrasing leaves room
        //for one-per-shared-value or one-per-side-per-shared-value).
        await Gen.Bool.Array[2 * ValueCount, 2 * ValueCount].SampleAsync(async bits =>
        {
            (HashSet<int> setA, HashSet<int> setB) = DecodeSets(bits);

            bool intersectionNonEmpty = setA.Overlaps(setB);

            int actual = await RunPairConstraintAsync(
                ShaclConstraintVocabulary.Disjoint.ToString(),
                ShaclComponentVocabulary.Disjoint,
                DisjointEvaluator.EvaluateAsync,
                setA, setB).ConfigureAwait(false);

            if(intersectionNonEmpty)
            {
                Assert.IsGreaterThanOrEqualTo(1, actual,
                    $"Disjoint: must violate when intersection is non-empty. "
                    + $"setA={Format(setA)} setB={Format(setB)} got {actual} violations.");
            }
            else
            {
                Assert.AreEqual(0, actual,
                    $"Disjoint: must not violate when intersection is empty. "
                    + $"setA={Format(setA)} setB={Format(setB)} got {actual} violations.");
            }
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertySubsetOfViolationCountEqualsLeftMinusRight()
    {
        //sh:subsetOf: values at the path predicate must be a subset
        //of values at the named predicate. Reference: one violation
        //per element of setA \ setB.
        await Gen.Bool.Array[2 * ValueCount, 2 * ValueCount].SampleAsync(async bits =>
        {
            (HashSet<int> setA, HashSet<int> setB) = DecodeSets(bits);

            int expected = setA.Except(setB).Count();

            int actual = await RunPairConstraintAsync(
                ShaclConstraintVocabulary.SubsetOf.ToString(),
                ShaclComponentVocabulary.SubsetOf,
                SubsetOfEvaluator.EvaluateAsync,
                setA, setB).ConfigureAwait(false);

            Assert.AreEqual(expected, actual,
                $"SubsetOf: expected {expected} violations for setA \\ setB, "
                + $"setA={Format(setA)} setB={Format(setB)}, got {actual}.");
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyLessThanNoViolationsWhenEitherSideEmpty()
    {
        //sh:lessThan (§4.4.3): every value at A must be less than
        //every value at B. The universal quantifier is vacuously
        //true when either side has no values, so no violations
        //must be produced.
        await Gen.Bool.Array[2 * ValueCount, 2 * ValueCount].SampleAsync(async bits =>
        {
            (HashSet<int> setA, HashSet<int> setB) = DecodeSets(bits);

            if(setA.Count != 0 && setB.Count != 0)
            {
                //Skip — only the empty-side case is being pinned by
                //this property.
                return;
            }

            int actual = await RunPairConstraintAsync(
                ShaclConstraintVocabulary.LessThan.ToString(),
                ShaclComponentVocabulary.LessThan,
                LessThanEvaluator.EvaluateAsync,
                setA, setB).ConfigureAwait(false);

            Assert.AreEqual(0, actual,
                $"LessThan: must not violate when either side is empty. "
                + $"setA={Format(setA)} setB={Format(setB)} got {actual} violations.");
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyLessThanOrEqualsNoViolationsWhenEitherSideEmpty()
    {
        //sh:lessThanOrEquals (§4.4.4): same vacuous-truth invariant
        //as LessThan when either side has no values.
        await Gen.Bool.Array[2 * ValueCount, 2 * ValueCount].SampleAsync(async bits =>
        {
            (HashSet<int> setA, HashSet<int> setB) = DecodeSets(bits);

            if(setA.Count != 0 && setB.Count != 0)
            {
                return;
            }

            int actual = await RunPairConstraintAsync(
                ShaclConstraintVocabulary.LessThanOrEquals.ToString(),
                ShaclComponentVocabulary.LessThanOrEquals,
                LessThanOrEqualsEvaluator.EvaluateAsync,
                setA, setB).ConfigureAwait(false);

            Assert.AreEqual(0, actual,
                $"LessThanOrEquals: must not violate when either side is empty. "
                + $"setA={Format(setA)} setB={Format(setB)} got {actual} violations.");
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyLessThanReflexivePresenceProducesViolation()
    {
        //sh:lessThan: if any value v appears in both sets, the
        //focus must produce at least one violation, since v < v
        //is false under any total order. This is a structural
        //invariant; it does not pin the violation count.
        await Gen.Bool.Array[2 * ValueCount, 2 * ValueCount].SampleAsync(async bits =>
        {
            (HashSet<int> setA, HashSet<int> setB) = DecodeSets(bits);

            if(!setA.Overlaps(setB))
            {
                return;
            }

            int actual = await RunPairConstraintAsync(
                ShaclConstraintVocabulary.LessThan.ToString(),
                ShaclComponentVocabulary.LessThan,
                LessThanEvaluator.EvaluateAsync,
                setA, setB).ConfigureAwait(false);

            Assert.IsGreaterThanOrEqualTo(1, actual,
                $"LessThan: shared value must produce at least one violation. "
                + $"setA={Format(setA)} setB={Format(setB)} got {actual} violations.");
        }).ConfigureAwait(false);
    }

    //Decodes a 2*V-bit bitmap into the two value sets. Bits 0..V-1
    //control setA membership; bits V..2V-1 control setB membership.
    private static (HashSet<int> setA, HashSet<int> setB) DecodeSets(bool[] bits)
    {
        HashSet<int> setA = [];
        HashSet<int> setB = [];

        for(int v = 0; v < ValueCount; v++)
        {
            if(bits[v])
            {
                setA.Add(v);
            }

            if(bits[ValueCount + v])
            {
                setB.Add(v);
            }
        }

        return (setA, setB);
    }

    //Runs a property-pair evaluator with the given (setA, setB) by
    //emitting (focus, predA, v) triples for v in setA and (focus,
    //predB, v) triples for v in setB. The shape is a NodeShape
    //targeting the focus, with a sh:property pointing at a property
    //shape that carries the pair-property constraint on predA.
    private async Task<int> RunPairConstraintAsync(
        string constraintParameterIri,
        Utf8String componentIri,
        ConstraintEvaluator evaluator,
        HashSet<int> setA,
        HashSet<int> setB)
    {
        const string PropShape = "http://example.org/PS";

        TestShaclPipelineShapeState scenario = TestShaclPipeline.BeginWithFocus(ExFocus);

        scenario.Builder.PropertyShape(PropShape, pathIri: ExPredA)
            .With(constraintParameterIri, ShapeGraphBuilder.Iri(ExPredB));

        TestShaclPipelineShapeState shapeState = scenario
            .WithNodeShapeTargetingPipelineFocus(ExShape)
            .With(ShaclConstraintVocabulary.Property.ToString(),
                ShapeGraphBuilder.Iri(PropShape))
            .Done();

        TestShaclPipelineDataState dataState = await shapeState
            .BuildAsync(TestContext.CancellationToken).ConfigureAwait(false);

        foreach(int v in setA)
        {
            dataState = dataState.WithTripleOnFocus(
                ExPredA, ShapeGraphBuilder.IntLiteral(v));
        }

        foreach(int v in setB)
        {
            dataState = dataState.WithTripleOnFocus(
                ExPredB, ShapeGraphBuilder.IntLiteral(v));
        }

        (ValidationReport report, ValidationTrace _) = await dataState
            .WithEvaluator(ShaclComponentVocabulary.Property, PropertyEvaluator.EvaluateAsync)
            .WithEvaluator(componentIri, evaluator)
            .RunWithTraceAsync(TestContext.CancellationToken).ConfigureAwait(false);

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

    private static string Format(HashSet<int> set)
    {
        List<int> sorted = [.. set];
        sorted.Sort();

        return "{" + string.Join(",", sorted) + "}";
    }
}
