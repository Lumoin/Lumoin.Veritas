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
/// CsCheck-driven property tests for
/// <see cref="UniqueValuesForEvaluator"/>. Per SHACL 1.2 Core §6.10.2:
/// a value <c>v</c> of focus <c>f</c> violates the constraint iff
/// <c>v</c> appears as object at any predicate <c>p ∈
/// PredicateIds</c> of any focus <c>f' ≠ f</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reference implementation as oracle.</b> Each property computes
/// the expected violation set directly from the generated data graph
/// using set arithmetic on the assignment table, then compares to
/// the validator's report. The reference is a literal transcription
/// of the spec definition; any disagreement is an evaluator bug, not
/// a semantic dispute.
/// </para>
/// <para>
/// <b>Async sampling.</b> The test pipeline is async, so each
/// property drives CsCheck's <c>SampleAsync</c> with an async lambda
/// that awaits <c>RunAndExtractViolationsAsync</c> once per
/// iteration and asserts on its result.
/// </para>
/// <para>
/// <b>Generator shape.</b> Each property uses
/// <c>Gen.Bool.Array[L, L]</c> to randomise the assignment of values
/// to <c>(focus, predicate)</c> cells, with <c>L</c> derived from
/// fixed dimensions (small focus / predicate / value counts).
/// Iteration count comes from CsCheck's own sample budget; the
/// fixed-dimension approach trades breadth of dimension coverage for
/// depth of distribution coverage at each fixed shape, complementing
/// the example tests in
/// <see cref="UniqueValuesForEvaluatorTests"/>.
/// </para>
/// </remarks>
[TestClass]
internal sealed class UniqueValuesForEvaluatorPropertyTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExShape = "http://example.org/S";

    [TestMethod]
    public async Task PropertyReportEqualsSpecDefinedCollisionsSinglePredicate()
    {
        //3 focuses, 1 predicate (path == only predicate in
        //PredicateIds), 4 distinct value tokens. Bitmap of length 12
        //(3*1*4) drives the assignment.
        const int FocusCount = 3;
        const int PredicateCount = 1;
        const int ValueCount = 4;
        const int CellCount = FocusCount * PredicateCount * ValueCount;

        await Gen.Bool.Array[CellCount, CellCount].SampleAsync(async bits =>
        {
            HashSet<(int focusIdx, int predIdx, int valIdx)> assignments =
                BitsToAssignments(bits, FocusCount, PredicateCount, ValueCount);

            HashSet<(int, int)> expected = ComputeExpectedViolations(
                assignments, FocusCount, PredicateCount, ValueCount);

            HashSet<(int, int)> actual = await RunAndExtractViolationsAsync(
                assignments, FocusCount, PredicateCount, ValueCount).ConfigureAwait(false);

            Assert.IsTrue(expected.SetEquals(actual),
                $"Expected violations {Format(expected)}, got {Format(actual)}. "
                + $"Assignments: {FormatAssignments(assignments)}.");
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyReportEqualsSpecDefinedCollisionsMultiPredicate()
    {
        //2 focuses, 3 predicates, 3 distinct value tokens. The path
        //predicate is one of the three, but values appearing at any
        //of the others (for some other focus) also count as
        //collisions per the spec's "any of the listed predicates"
        //reading. Bitmap length 18.
        const int FocusCount = 2;
        const int PredicateCount = 3;
        const int ValueCount = 3;
        const int CellCount = FocusCount * PredicateCount * ValueCount;

        await Gen.Bool.Array[CellCount, CellCount].SampleAsync(async bits =>
        {
            HashSet<(int focusIdx, int predIdx, int valIdx)> assignments =
                BitsToAssignments(bits, FocusCount, PredicateCount, ValueCount);

            HashSet<(int, int)> expected = ComputeExpectedViolations(
                assignments, FocusCount, PredicateCount, ValueCount);

            HashSet<(int, int)> actual = await RunAndExtractViolationsAsync(
                assignments, FocusCount, PredicateCount, ValueCount).ConfigureAwait(false);

            Assert.IsTrue(expected.SetEquals(actual),
                $"Expected violations {Format(expected)}, got {Format(actual)}. "
                + $"Assignments: {FormatAssignments(assignments)}.");
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyResultIsOrderIndependent()
    {
        //SHACL semantics are triple-set semantics; permuting triple
        //insertion order must not change the result set. Pins
        //order-independence for the data-graph store / match
        //delegate, which is critical for future graph-store backends
        //whose iteration order may differ from the current backend.
        const int FocusCount = 2;
        const int PredicateCount = 2;
        const int ValueCount = 3;
        const int CellCount = FocusCount * PredicateCount * ValueCount;

        await Gen.Bool.Array[CellCount, CellCount].SampleAsync(async bits =>
        {
            HashSet<(int focusIdx, int predIdx, int valIdx)> assignments =
                BitsToAssignments(bits, FocusCount, PredicateCount, ValueCount);

            HashSet<(int, int)> firstPass = await RunAndExtractViolationsAsync(
                assignments, FocusCount, PredicateCount, ValueCount, reverseOrder: false).ConfigureAwait(false);

            HashSet<(int, int)> secondPass = await RunAndExtractViolationsAsync(
                assignments, FocusCount, PredicateCount, ValueCount, reverseOrder: true).ConfigureAwait(false);

            Assert.IsTrue(firstPass.SetEquals(secondPass),
                $"Result must not depend on triple insertion order. "
                + $"Forward {Format(firstPass)}, reverse {Format(secondPass)}.");
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task PropertyAllSharedValuesProduceSymmetricViolations()
    {
        //When all focuses hold all values at the path predicate, and
        //the path is the only predicate in PredicateIds, every
        //(focus, value) pair must violate (since every value
        //collides with every other focus). This is the symmetry
        //invariant in the densest possible form. The generator
        //varies focus count; assignments are deterministic-dense.
        await Gen.Int[2, 5].SampleAsync(async focusCount =>
        {
            const int PredicateCount = 1;
            const int ValueCount = 3;

            HashSet<(int focusIdx, int predIdx, int valIdx)> assignments = [];
            for(int f = 0; f < focusCount; f++)
            {
                for(int v = 0; v < ValueCount; v++)
                {
                    assignments.Add((f, 0, v));
                }
            }

            HashSet<(int, int)> actual = await RunAndExtractViolationsAsync(
                assignments, focusCount, PredicateCount, ValueCount).ConfigureAwait(false);

            Assert.HasCount(focusCount * ValueCount, actual,
                $"Every (focus, value) pair must violate when all values are shared. "
                + $"focusCount={focusCount} got {Format(actual)}.");
        }).ConfigureAwait(false);
    }

    //Decodes a bitmap into an assignment set by linearising the
    //three-dimensional (focus, predicate, value) coordinate space.
    //Bit at index f * (predicateCount * valueCount) + p *
    //valueCount + v controls cell (f, p, v).
    private static HashSet<(int focusIdx, int predIdx, int valIdx)> BitsToAssignments(
        bool[] bits, int focusCount, int predicateCount, int valueCount)
    {
        HashSet<(int, int, int)> result = [];
        for(int f = 0; f < focusCount; f++)
        {
            for(int p = 0; p < predicateCount; p++)
            {
                for(int v = 0; v < valueCount; v++)
                {
                    int idx = ((f * predicateCount) + p) * valueCount + v;
                    if(bits[idx])
                    {
                        result.Add((f, p, v));
                    }
                }
            }
        }

        return result;
    }

    //Reference implementation: computes the spec-defined violation
    //set directly from assignments. The path predicate is index 0;
    //PredicateIds contains predicates 0 through predicateCount-1
    //(i.e., all generated predicates).
    //
    //A focus's value-nodes are its values at the path predicate
    //(index 0). A (focus, value) pair violates iff there exists
    //some other focus that has value at any predicate in
    //PredicateIds.
    private static HashSet<(int focusIdx, int valIdx)> ComputeExpectedViolations(
        HashSet<(int focusIdx, int predIdx, int valIdx)> assignments,
        int focusCount,
        int predicateCount,
        int valueCount)
    {
        HashSet<(int, int)> expected = [];

        for(int f = 0; f < focusCount; f++)
        {
            for(int v = 0; v < valueCount; v++)
            {
                if(!assignments.Contains((f, 0, v)))
                {
                    continue;
                }

                bool collides = false;
                for(int otherF = 0; otherF < focusCount && !collides; otherF++)
                {
                    if(otherF == f)
                    {
                        continue;
                    }

                    for(int pp = 0; pp < predicateCount && !collides; pp++)
                    {
                        if(assignments.Contains((otherF, pp, v)))
                        {
                            collides = true;
                        }
                    }
                }

                if(collides)
                {
                    expected.Add((f, v));
                }
            }
        }

        return expected;
    }

    //Materialises the assignment table into the SHACL pipeline,
    //runs the validator, and reverse-maps violation results to
    //(focusIdx, valIdx) pairs. Awaited once per sample iteration
    //from the property's async CsCheck lambda.
    private async Task<HashSet<(int focusIdx, int valIdx)>> RunAndExtractViolationsAsync(
        HashSet<(int focusIdx, int predIdx, int valIdx)> assignments,
        int focusCount,
        int predicateCount,
        int valueCount,
        bool reverseOrder = false)
    {
        TestShaclPipelineShapeState scenario = TestShaclPipeline.BeginWithFocus(FocusIri(0));

        RdfTerm[] predicateIris = new RdfTerm[predicateCount];
        for(int pp = 0; pp < predicateCount; pp++)
        {
            predicateIris[pp] = ShapeGraphBuilder.Iri(PredicateIri(pp));
        }
        RdfTerm predicateList = scenario.Builder.List(predicateIris);

        TestShaclPipelineShapeState shapeState = scenario
            .WithPropertyShapeTargetingSubjectsOfPath(ExShape, PredicateIri(0))
            .With(ShaclConstraintVocabulary.UniqueValuesFor.ToString(), predicateList)
            .Done();

        TestShaclPipelineDataState dataState = await shapeState
            .BuildAsync(TestContext.CancellationToken).ConfigureAwait(false);

        IEnumerable<(int focusIdx, int predIdx, int valIdx)> ordered = reverseOrder
            ? assignments.Reverse()
            : assignments;

        foreach((int focusIdx, int predIdx, int valIdx) cell in ordered)
        {
            dataState = dataState.WithExplicitTriple(
                subjectIri: FocusIri(cell.focusIdx),
                predicateIri: PredicateIri(cell.predIdx),
                @object: ValueLiteral(cell.valIdx));
        }

        (ValidationReport report, ValidationTrace _) = await dataState
            .WithEvaluator(ShaclComponentVocabulary.UniqueValuesFor, UniqueValuesForEvaluator.EvaluateAsync)
            .RunWithTraceAsync(TestContext.CancellationToken).ConfigureAwait(false);

        TermDictionary dict = dataState.Dictionary;
        HashSet<(int, int)> actual = [];

        foreach(ValidationResult result in report.Results)
        {
            if(result.Severity != Severity.Violation)
            {
                continue;
            }

            int focusIdx = ResolveFocusIndex(result.FocusNode, dict, focusCount);
            int valIdx = ResolveValueIndex(result.ValueNode, dict, valueCount);

            Assert.IsGreaterThanOrEqualTo(0, focusIdx,
                "Violation FocusNode must resolve to a generated focus IRI.");
            Assert.IsGreaterThanOrEqualTo(0, valIdx,
                "Violation ValueNode must resolve to a generated value literal.");

            actual.Add((focusIdx, valIdx));
        }

        return actual;
    }

    private static int ResolveFocusIndex(TermId focusId, TermDictionary dict, int focusCount)
    {
        if(dict.Resolve(focusId) is not NamedNode named)
        {
            return -1;
        }

        string iri = named.Iri.ToString();
        for(int f = 0; f < focusCount; f++)
        {
            if(iri == FocusIri(f))
            {
                return f;
            }
        }

        return -1;
    }

    private static int ResolveValueIndex(TermId? valueId, TermDictionary dict, int valueCount)
    {
        if(valueId is null)
        {
            return -1;
        }

        if(dict.Resolve(valueId.Value) is not Literal lit)
        {
            return -1;
        }

        string lex = lit.Value.ToString();
        for(int v = 0; v < valueCount; v++)
        {
            if(lex == ValueLexical(v))
            {
                return v;
            }
        }

        return -1;
    }

    private static string FocusIri(int index) => $"http://example.org/focus{index}";

    private static string PredicateIri(int index) => $"http://example.org/p{index}";

    private static string ValueLexical(int index) => $"value{index}";

    private static Literal ValueLiteral(int index)
        => new(Utf8Strings.From(ValueLexical(index)),
            new NamedNode(Vocabulary.Xsd.String));

    private static string Format(IEnumerable<(int focusIdx, int valIdx)> pairs)
    {
        IEnumerable<string> sorted = pairs
            .OrderBy(p => p.focusIdx)
            .ThenBy(p => p.valIdx)
            .Select(p => $"(f{p.focusIdx},v{p.valIdx})");

        return "{" + string.Join(", ", sorted) + "}";
    }

    private static string FormatAssignments(IEnumerable<(int focusIdx, int predIdx, int valIdx)> cells)
    {
        IEnumerable<string> sorted = cells
            .OrderBy(c => c.focusIdx)
            .ThenBy(c => c.predIdx)
            .ThenBy(c => c.valIdx)
            .Select(c => $"(f{c.focusIdx},p{c.predIdx},v{c.valIdx})");

        return "{" + string.Join(", ", sorted) + "}";
    }
}
