using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using Lumoin.Veritas.ParserTests.Infrastructure;
using ValidationReport = Lumoin.Veritas.Shacl.Validation.ValidationReport;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// Tests for the four numeric-range evaluators
/// (<see cref="MinInclusiveEvaluator"/>,
/// <see cref="MaxInclusiveEvaluator"/>,
/// <see cref="MinExclusiveEvaluator"/>,
/// <see cref="MaxExclusiveEvaluator"/>) end-to-end through
/// <see cref="ShaclValidator.ValidateAsync"/>. Each test builds a
/// one-property-shape graph with the relevant range constraint and
/// verifies the boundary semantics.
/// </summary>
[TestClass]
internal sealed class NumericRangeEvaluatorTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExShape = "http://example.org/S";
    private const string ExPred = "http://example.org/pred";
    private const string ExFocus = "http://example.org/foo";

    [TestMethod]
    public async Task MinInclusiveAcceptsValueAboveBound()
    {
        ValidationReport report = await RunRangeAsync(
            ShaclConstraintVocabulary.MinInclusive.ToString(),
            ShaclComponentVocabulary.MinInclusive,
            MinInclusiveEvaluator.EvaluateAsync,
            boundLexical: "5",
            boundDatatype: Vocabulary.Xsd.Integer,
            valueLexicals: ["10"],
            valueDatatype: Vocabulary.Xsd.Integer,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task MinInclusiveAcceptsValueAtBound()
    {
        //Equality conforms for inclusive constraints.
        ValidationReport report = await RunRangeAsync(
            ShaclConstraintVocabulary.MinInclusive.ToString(),
            ShaclComponentVocabulary.MinInclusive,
            MinInclusiveEvaluator.EvaluateAsync,
            boundLexical: "5",
            boundDatatype: Vocabulary.Xsd.Integer,
            valueLexicals: ["5"],
            valueDatatype: Vocabulary.Xsd.Integer,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
    }

    [TestMethod]
    public async Task MinInclusiveRejectsValueBelowBound()
    {
        ValidationReport report = await RunRangeAsync(
            ShaclConstraintVocabulary.MinInclusive.ToString(),
            ShaclComponentVocabulary.MinInclusive,
            MinInclusiveEvaluator.EvaluateAsync,
            boundLexical: "5",
            boundDatatype: Vocabulary.Xsd.Integer,
            valueLexicals: ["3"],
            valueDatatype: Vocabulary.Xsd.Integer,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(ShaclComponentVocabulary.MinInclusive, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task MaxInclusiveAcceptsValueBelowBound()
    {
        ValidationReport report = await RunRangeAsync(
            ShaclConstraintVocabulary.MaxInclusive.ToString(),
            ShaclComponentVocabulary.MaxInclusive,
            MaxInclusiveEvaluator.EvaluateAsync,
            boundLexical: "10",
            boundDatatype: Vocabulary.Xsd.Integer,
            valueLexicals: ["7"],
            valueDatatype: Vocabulary.Xsd.Integer,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
    }

    [TestMethod]
    public async Task MaxInclusiveAcceptsValueAtBound()
    {
        ValidationReport report = await RunRangeAsync(
            ShaclConstraintVocabulary.MaxInclusive.ToString(),
            ShaclComponentVocabulary.MaxInclusive,
            MaxInclusiveEvaluator.EvaluateAsync,
            boundLexical: "10",
            boundDatatype: Vocabulary.Xsd.Integer,
            valueLexicals: ["10"],
            valueDatatype: Vocabulary.Xsd.Integer,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
    }

    [TestMethod]
    public async Task MaxInclusiveRejectsValueAboveBound()
    {
        ValidationReport report = await RunRangeAsync(
            ShaclConstraintVocabulary.MaxInclusive.ToString(),
            ShaclComponentVocabulary.MaxInclusive,
            MaxInclusiveEvaluator.EvaluateAsync,
            boundLexical: "10",
            boundDatatype: Vocabulary.Xsd.Integer,
            valueLexicals: ["15"],
            valueDatatype: Vocabulary.Xsd.Integer,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
    }

    [TestMethod]
    public async Task MinExclusiveAcceptsValueAboveBound()
    {
        ValidationReport report = await RunRangeAsync(
            ShaclConstraintVocabulary.MinExclusive.ToString(),
            ShaclComponentVocabulary.MinExclusive,
            MinExclusiveEvaluator.EvaluateAsync,
            boundLexical: "5",
            boundDatatype: Vocabulary.Xsd.Integer,
            valueLexicals: ["6"],
            valueDatatype: Vocabulary.Xsd.Integer,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
    }

    [TestMethod]
    public async Task MinExclusiveRejectsValueAtBound()
    {
        //Equality fails for exclusive constraints — this is the
        //defining difference from the inclusive variant.
        ValidationReport report = await RunRangeAsync(
            ShaclConstraintVocabulary.MinExclusive.ToString(),
            ShaclComponentVocabulary.MinExclusive,
            MinExclusiveEvaluator.EvaluateAsync,
            boundLexical: "5",
            boundDatatype: Vocabulary.Xsd.Integer,
            valueLexicals: ["5"],
            valueDatatype: Vocabulary.Xsd.Integer,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(ShaclComponentVocabulary.MinExclusive, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task MaxExclusiveAcceptsValueBelowBound()
    {
        ValidationReport report = await RunRangeAsync(
            ShaclConstraintVocabulary.MaxExclusive.ToString(),
            ShaclComponentVocabulary.MaxExclusive,
            MaxExclusiveEvaluator.EvaluateAsync,
            boundLexical: "10",
            boundDatatype: Vocabulary.Xsd.Integer,
            valueLexicals: ["9"],
            valueDatatype: Vocabulary.Xsd.Integer,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
    }

    [TestMethod]
    public async Task MaxExclusiveRejectsValueAtBound()
    {
        ValidationReport report = await RunRangeAsync(
            ShaclConstraintVocabulary.MaxExclusive.ToString(),
            ShaclComponentVocabulary.MaxExclusive,
            MaxExclusiveEvaluator.EvaluateAsync,
            boundLexical: "10",
            boundDatatype: Vocabulary.Xsd.Integer,
            valueLexicals: ["10"],
            valueDatatype: Vocabulary.Xsd.Integer,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
    }

    [TestMethod]
    public async Task RangeRejectsValueWithMismatchedDatatype()
    {
        //Constraint bounded by xsd:integer "5"; value is xsd:string
        //"5". Different value spaces → Incomparable → violation.
        ValidationReport report = await RunRangeAsync(
            ShaclConstraintVocabulary.MinInclusive.ToString(),
            ShaclComponentVocabulary.MinInclusive,
            MinInclusiveEvaluator.EvaluateAsync,
            boundLexical: "5",
            boundDatatype: Vocabulary.Xsd.Integer,
            valueLexicals: ["5"],
            valueDatatype: Vocabulary.Xsd.String,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
    }

    [TestMethod]
    public async Task RangeAcceptsCrossNumericPromotion()
    {
        //Constraint bounded by xsd:integer "5"; value is xsd:decimal
        //"5.0". Promotes to decimal, equal, conforms.
        ValidationReport report = await RunRangeAsync(
            ShaclConstraintVocabulary.MinInclusive.ToString(),
            ShaclComponentVocabulary.MinInclusive,
            MinInclusiveEvaluator.EvaluateAsync,
            boundLexical: "5",
            boundDatatype: Vocabulary.Xsd.Integer,
            valueLexicals: ["5.0"],
            valueDatatype: Vocabulary.Xsd.Decimal,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
    }

    [TestMethod]
    public async Task RangeWorksOnDateTimeBound()
    {
        //sh:minInclusive "2024-01-01T00:00:00Z"^^xsd:dateTime;
        //value is "2024-06-01T00:00:00Z" — later, conforms.
        ValidationReport report = await RunRangeAsync(
            ShaclConstraintVocabulary.MinInclusive.ToString(),
            ShaclComponentVocabulary.MinInclusive,
            MinInclusiveEvaluator.EvaluateAsync,
            boundLexical: "2024-01-01T00:00:00Z",
            boundDatatype: Vocabulary.Xsd.DateTime,
            valueLexicals: ["2024-06-01T00:00:00Z"],
            valueDatatype: Vocabulary.Xsd.DateTime,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
    }

    [TestMethod]
    public async Task RangeMixedConformingAndNonConformingValues()
    {
        //Three values: 3 (fails, < 5), 5 (passes, == bound), 7
        //(passes, > bound). Expect one result for the 3.
        ValidationReport report = await RunRangeAsync(
            ShaclConstraintVocabulary.MinInclusive.ToString(),
            ShaclComponentVocabulary.MinInclusive,
            MinInclusiveEvaluator.EvaluateAsync,
            boundLexical: "5",
            boundDatatype: Vocabulary.Xsd.Integer,
            valueLexicals: ["3", "5", "7"],
            valueDatatype: Vocabulary.Xsd.Integer,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
    }

    //Helpers below.

    //Build a property shape with the named numeric-range constraint
    //pointing at a typed literal bound, and one or more typed-literal
    //values along the path. Run validation with a registry containing
    //just the evaluator under test.
    private static async Task<ValidationReport> RunRangeAsync(
        string constraintIri,
        Utf8String componentIri,
        ConstraintEvaluator evaluator,
        string boundLexical,
        Utf8String boundDatatype,
        IReadOnlyList<string> valueLexicals,
        Utf8String valueDatatype,
        CancellationToken cancellationToken)
    {
        ShapeGraphBuilder builder = new();

        //Construct the bound literal as a typed RdfTerm and pass it
        //into the shape graph as the constraint value.
        Literal boundLiteral = new(Utf8Strings.From(boundLexical), new NamedNode(boundDatatype));
        builder.PropertyShape(ExShape, pathIri: ExPred)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(ExPred))
            .With(constraintIri, boundLiteral);

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        NamedNode focus = new(Utf8Strings.From(ExFocus));
        NamedNode pred = new(Utf8Strings.From(ExPred));
        List<EncodedTriple> dataTriples = [];
        foreach(string lexical in valueLexicals)
        {
            Literal value = new(Utf8Strings.From(lexical), new NamedNode(valueDatatype));
            dataTriples.Add(new Quad(focus, pred, value).Encode(dictionary).AsTriple());
        }
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build(dataTriples);

        ConstraintEvaluatorRegistry evaluators = new(new Dictionary<Utf8String, ConstraintEvaluator>
        {
            [componentIri] = evaluator,
        });

        return await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary, evaluators,
            VeritasClock.System,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
