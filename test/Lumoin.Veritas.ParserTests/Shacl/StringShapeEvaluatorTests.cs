using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using Lumoin.Veritas.ParserTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ValidationResult = Lumoin.Veritas.Shacl.Validation.ValidationResult;
using ValidationReport = Lumoin.Veritas.Shacl.Validation.ValidationReport;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// Tests for the five string-shape leaf evaluators introduced in
/// phase 2C-d batch 1: <see cref="MinLengthEvaluator"/>,
/// <see cref="MaxLengthEvaluator"/>, <see cref="SingleLineEvaluator"/>,
/// <see cref="LanguageInEvaluator"/>, and
/// <see cref="UniqueLanguageEvaluator"/>.
/// </summary>
/// <remarks>
/// All five operate on the value-node set produced by a property
/// shape's <c>sh:path</c>. Each test builds a one-property-shape
/// shape graph, encodes a small data graph with the relevant value
/// nodes, runs validation, and checks the report.
/// </remarks>
[TestClass]
internal sealed class StringShapeEvaluatorTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string ExShape = "http://example.org/S";
    private const string ExPred = "http://example.org/pred";
    private const string ExFocus = "http://example.org/foo";

    [TestMethod]
    public async Task MinLengthAcceptsValueAtFloor()
    {
        Literal exact = new(Utf8Strings.From("abc"), new NamedNode(Vocabulary.Xsd.String));

        ValidationReport report = await RunStringLengthAsync(
            ShaclConstraintVocabulary.MinLength.ToString(),
            ShaclComponentVocabulary.MinLength,
            MinLengthEvaluator.EvaluateAsync,
            constraintValue: 3,
            values: [exact],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task MinLengthRejectsShorterValue()
    {
        Literal tooShort = new(Utf8Strings.From("ab"), new NamedNode(Vocabulary.Xsd.String));

        ValidationReport report = await RunStringLengthAsync(
            ShaclConstraintVocabulary.MinLength.ToString(),
            ShaclComponentVocabulary.MinLength,
            MinLengthEvaluator.EvaluateAsync,
            constraintValue: 3,
            values: [tooShort],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(ShaclComponentVocabulary.MinLength, report.Results[0].SourceConstraintComponent);
        Assert.IsNotNull(report.Results[0].ValueNode);
    }

    [TestMethod]
    public async Task MinLengthCountsIriValueByIriString()
    {
        //IRI string "http://example.org/x" has length 20.
        NamedNode iri = new(Utf8Strings.From("http://example.org/x"));

        ValidationReport report = await RunStringLengthAsync(
            ShaclConstraintVocabulary.MinLength.ToString(),
            ShaclComponentVocabulary.MinLength,
            MinLengthEvaluator.EvaluateAsync,
            constraintValue: 5,
            values: [iri],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
    }

    [TestMethod]
    public async Task MinLengthRejectsBlankNodeRegardlessOfFloor()
    {
        BlankNode blank = new(Utf8Strings.From("b1"));

        ValidationReport report = await RunStringLengthAsync(
            ShaclConstraintVocabulary.MinLength.ToString(),
            ShaclComponentVocabulary.MinLength,
            MinLengthEvaluator.EvaluateAsync,
            //Even with a floor of zero, blank nodes have no string
            //form per SHACL and therefore fail.
            constraintValue: 0,
            values: [blank],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
    }

    [TestMethod]
    public async Task MaxLengthAcceptsValueAtCeiling()
    {
        Literal exact = new(Utf8Strings.From("abc"), new NamedNode(Vocabulary.Xsd.String));

        ValidationReport report = await RunStringLengthAsync(
            ShaclConstraintVocabulary.MaxLength.ToString(),
            ShaclComponentVocabulary.MaxLength,
            MaxLengthEvaluator.EvaluateAsync,
            constraintValue: 3,
            values: [exact],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
    }

    [TestMethod]
    public async Task MaxLengthRejectsLongerValue()
    {
        Literal tooLong = new(Utf8Strings.From("abcd"), new NamedNode(Vocabulary.Xsd.String));

        ValidationReport report = await RunStringLengthAsync(
            ShaclConstraintVocabulary.MaxLength.ToString(),
            ShaclComponentVocabulary.MaxLength,
            MaxLengthEvaluator.EvaluateAsync,
            constraintValue: 3,
            values: [tooLong],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(ShaclComponentVocabulary.MaxLength, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task SingleLineWithFalseFlagIsTrivialPass()
    {
        //Even a value containing newlines passes when sh:singleLine
        //is set to false — the constraint is declared but not active.
        Literal multiLine = new(Utf8Strings.From("foo\nbar"), new NamedNode(Vocabulary.Xsd.String));

        ValidationReport report = await RunSingleLineAsync(
            singleLine: false,
            values: [multiLine],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    [TestMethod]
    public async Task SingleLineWithTrueFlagAcceptsSingleLineValue()
    {
        Literal clean = new(Utf8Strings.From("foo bar baz"), new NamedNode(Vocabulary.Xsd.String));

        ValidationReport report = await RunSingleLineAsync(
            singleLine: true,
            values: [clean],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
    }

    [TestMethod]
    public async Task SingleLineWithTrueFlagRejectsLineFeed()
    {
        Literal multiLine = new(Utf8Strings.From("foo\nbar"), new NamedNode(Vocabulary.Xsd.String));

        ValidationReport report = await RunSingleLineAsync(
            singleLine: true,
            values: [multiLine],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(ShaclComponentVocabulary.SingleLine, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task SingleLineWithTrueFlagRejectsCarriageReturn()
    {
        Literal cr = new(Utf8Strings.From("foo\rbar"), new NamedNode(Vocabulary.Xsd.String));

        ValidationReport report = await RunSingleLineAsync(
            singleLine: true,
            values: [cr],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
    }

    [TestMethod]
    public async Task LanguageInExactTagMatches()
    {
        Literal english = new(
            Utf8Strings.From("Hello"),
            new NamedNode(Vocabulary.Rdf.LangString),
            Utf8Strings.From("en"));

        ValidationReport report = await RunLanguageInAsync(
            ranges: ["en", "fr"],
            values: [english],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
    }

    [TestMethod]
    public async Task LanguageInPrefixMatchOnHyphenBoundary()
    {
        //Range "en" matches "en-US" via basic-filtering prefix match.
        Literal usEnglish = new(
            Utf8Strings.From("Color"),
            new NamedNode(Vocabulary.Rdf.LangString),
            Utf8Strings.From("en-US"));

        ValidationReport report = await RunLanguageInAsync(
            ranges: ["en"],
            values: [usEnglish],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
    }

    [TestMethod]
    public async Task LanguageInPrefixMustEndOnHyphenBoundary()
    {
        //Range "en" must NOT match tag "eng" — the next character
        //after the prefix is 'g', not '-'.
        Literal threeLetterEnglish = new(
            Utf8Strings.From("Hello"),
            new NamedNode(Vocabulary.Rdf.LangString),
            Utf8Strings.From("eng"));

        ValidationReport report = await RunLanguageInAsync(
            ranges: ["en"],
            values: [threeLetterEnglish],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(ShaclComponentVocabulary.LanguageIn, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task LanguageInWildcardMatchesAnyTag()
    {
        Literal anything = new(
            Utf8Strings.From("Hej"),
            new NamedNode(Vocabulary.Rdf.LangString),
            Utf8Strings.From("sv"));

        ValidationReport report = await RunLanguageInAsync(
            ranges: ["*"],
            values: [anything],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
    }

    [TestMethod]
    public async Task LanguageInRejectsLiteralWithoutLanguageTag()
    {
        //xsd:string literal with no language tag — the constraint
        //requires a tag.
        Literal plain = new(Utf8Strings.From("plain"), new NamedNode(Vocabulary.Xsd.String));

        ValidationReport report = await RunLanguageInAsync(
            ranges: ["en"],
            values: [plain],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        Assert.HasCount(1, report.Results);
    }

    [TestMethod]
    public async Task UniqueLangFalseIsTrivialPass()
    {
        //Two literals with the same tag — passes when uniqueLang
        //is declared with value false.
        Literal a = new(Utf8Strings.From("Hello"), new NamedNode(Vocabulary.Rdf.LangString),
            Utf8Strings.From("en"));
        Literal b = new(Utf8Strings.From("World"), new NamedNode(Vocabulary.Rdf.LangString),
            Utf8Strings.From("en"));

        ValidationReport report = await RunUniqueLangAsync(
            uniqueLang: false,
            values: [a, b],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
    }

    [TestMethod]
    public async Task UniqueLangTrueAcceptsDistinctTags()
    {
        Literal en = new(Utf8Strings.From("Hello"), new NamedNode(Vocabulary.Rdf.LangString),
            Utf8Strings.From("en"));
        Literal fr = new(Utf8Strings.From("Bonjour"), new NamedNode(Vocabulary.Rdf.LangString),
            Utf8Strings.From("fr"));

        ValidationReport report = await RunUniqueLangAsync(
            uniqueLang: true,
            values: [en, fr],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
    }

    [TestMethod]
    public async Task UniqueLangTrueRejectsDuplicateTag()
    {
        Literal first = new(Utf8Strings.From("Hello"), new NamedNode(Vocabulary.Rdf.LangString),
            Utf8Strings.From("en"));
        Literal second = new(Utf8Strings.From("World"), new NamedNode(Vocabulary.Rdf.LangString),
            Utf8Strings.From("en"));

        ValidationReport report = await RunUniqueLangAsync(
            uniqueLang: true,
            values: [first, second],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
        //One set-level result; ValueNode is not set on set-level
        //violations.
        Assert.HasCount(1, report.Results);
        Assert.IsNull(report.Results[0].ValueNode);
        Assert.AreEqual(ShaclComponentVocabulary.UniqueLang, report.Results[0].SourceConstraintComponent);
    }

    [TestMethod]
    public async Task UniqueLangCaseInsensitiveTagComparison()
    {
        //Tags "en-US" and "EN-us" are the same per BCP 47 case-fold.
        Literal first = new(Utf8Strings.From("a"), new NamedNode(Vocabulary.Rdf.LangString),
            Utf8Strings.From("en-US"));
        Literal second = new(Utf8Strings.From("b"), new NamedNode(Vocabulary.Rdf.LangString),
            Utf8Strings.From("EN-us"));

        ValidationReport report = await RunUniqueLangAsync(
            uniqueLang: true,
            values: [first, second],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms);
    }

    [TestMethod]
    public async Task UniqueLangIgnoresLiteralsWithoutLanguageTag()
    {
        //Two xsd:string literals, no language tags — uniqueLang
        //doesn't constrain them, regardless of their values matching.
        Literal a = new(Utf8Strings.From("same"), new NamedNode(Vocabulary.Xsd.String));
        Literal b = new(Utf8Strings.From("same"), new NamedNode(Vocabulary.Xsd.String));

        ValidationReport report = await RunUniqueLangAsync(
            uniqueLang: true,
            values: [a, b],
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
    }

    //Helpers below.

    //Builds a property shape with a string-form length constraint
    //(MinLength or MaxLength) and runs validation using only the
    //specified evaluator. The component IRI selects which evaluator
    //slot to wire; passing the matching ConstraintEvaluator through
    //isolates the evaluator under test from the rest of the registry.
    private static async Task<ValidationReport> RunStringLengthAsync(
        string constraintIri,
        Utf8String componentIri,
        ConstraintEvaluator evaluator,
        int constraintValue,
        IReadOnlyList<RdfTerm> values,
        CancellationToken cancellationToken)
    {
        ShapeGraphBuilder builder = new();
        builder.PropertyShape(ExShape, pathIri: ExPred)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(ExPred))
            .With(constraintIri, ShapeGraphBuilder.IntLiteral(constraintValue));

        return await RunOneEvaluatorAsync(builder, componentIri, evaluator, values, cancellationToken).ConfigureAwait(false);
    }

    //Builds a property shape with sh:singleLine and the given flag.
    private static async Task<ValidationReport> RunSingleLineAsync(
        bool singleLine,
        IReadOnlyList<RdfTerm> values,
        CancellationToken cancellationToken)
    {
        ShapeGraphBuilder builder = new();
        builder.PropertyShape(ExShape, pathIri: ExPred)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(ExPred))
            .With(ShaclConstraintVocabulary.SingleLine.ToString(), ShapeGraphBuilder.BoolLiteral(singleLine));

        return await RunOneEvaluatorAsync(
            builder, ShaclComponentVocabulary.SingleLine, SingleLineEvaluator.EvaluateAsync,
            values, cancellationToken).ConfigureAwait(false);
    }

    //Builds a property shape with sh:languageIn and the given list of
    //BCP 47 ranges.
    private static async Task<ValidationReport> RunLanguageInAsync(
        IReadOnlyList<string> ranges,
        IReadOnlyList<RdfTerm> values,
        CancellationToken cancellationToken)
    {
        ShapeGraphBuilder builder = new();

        RdfTerm[] rangeTerms = new RdfTerm[ranges.Count];
        for(int i = 0; i < ranges.Count; i++)
        {
            rangeTerms[i] = ShapeGraphBuilder.StringLiteral(ranges[i]);
        }
        RdfTerm listHead = builder.List(rangeTerms);

        builder.PropertyShape(ExShape, pathIri: ExPred)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(ExPred))
            .With(ShaclConstraintVocabulary.LanguageIn.ToString(), listHead);

        return await RunOneEvaluatorAsync(
            builder, ShaclComponentVocabulary.LanguageIn, LanguageInEvaluator.EvaluateAsync,
            values, cancellationToken).ConfigureAwait(false);
    }

    //Builds a property shape with sh:uniqueLang and the given flag.
    private static async Task<ValidationReport> RunUniqueLangAsync(
        bool uniqueLang,
        IReadOnlyList<RdfTerm> values,
        CancellationToken cancellationToken)
    {
        ShapeGraphBuilder builder = new();
        builder.PropertyShape(ExShape, pathIri: ExPred)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(ExPred))
            .With(ShaclConstraintVocabulary.UniqueLang.ToString(), ShapeGraphBuilder.BoolLiteral(uniqueLang));

        return await RunOneEvaluatorAsync(
            builder, ShaclComponentVocabulary.UniqueLang, UniqueLanguageEvaluator.EvaluateAsync,
            values, cancellationToken).ConfigureAwait(false);
    }

    //Compiles the shape graph through the loader, encodes the value
    //triples, runs validation with a single-evaluator registry.
    private static async Task<ValidationReport> RunOneEvaluatorAsync(
        ShapeGraphBuilder builder,
        Utf8String componentIri,
        ConstraintEvaluator evaluator,
        IReadOnlyList<RdfTerm> values,
        CancellationToken cancellationToken)
    {
        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();
        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        NamedNode focus = new(Utf8Strings.From(ExFocus));
        NamedNode pred = new(Utf8Strings.From(ExPred));
        List<EncodedTriple> dataTriples = [];
        foreach(RdfTerm value in values)
        {
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
