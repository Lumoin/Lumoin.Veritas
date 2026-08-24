using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.ParserTests.Infrastructure;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using ValidationReport = Lumoin.Veritas.Shacl.Validation.ValidationReport;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// Pins for <c>sh:datatype</c> over a datatype IRI outside the modelled XSD set, keyed to the
/// value-datatype-seam baseline row list:
/// the constraint is datatype-IRI identity plus a lexical check that accepts every unmodelled datatype, so
/// a garbage lexical form typed <c>geo:wktLiteral</c> conforms exactly as a well-formed one does.
/// </summary>
[TestClass]
internal sealed class UnregisteredDatatypeValidationTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The shape IRI.</summary>
    private const string ExShape = "http://example.org/S";

    /// <summary>The path predicate IRI.</summary>
    private const string ExPred = "http://example.org/pred";

    /// <summary>The focus node IRI.</summary>
    private const string ExFocus = "http://example.org/foo";

    /// <summary>The unmodelled datatype IRI the rows constrain against.</summary>
    private const string GeoWktLiteral = "http://www.opengis.net/ont/geosparql#wktLiteral";

    /// <summary>V1: a garbage lexical form typed with an unmodelled datatype conforms to <c>sh:datatype</c> on that IRI — no lexical space is enforced.</summary>
    [TestMethod]
    public async Task DatatypeGarbageLexicalFormOfUnmodelledDatatypeConforms()
    {
        Literal garbage = new(
            Utf8Strings.From("certainly not a geometry"),
            new NamedNode(Utf8Strings.From(GeoWktLiteral)));

        ValidationReport report = await RunDatatypeAsync(garbage, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    /// <summary>V2: a well-formed lexical form typed with the same unmodelled datatype conforms identically — the two rows are indistinguishable to the constraint.</summary>
    [TestMethod]
    public async Task DatatypeWellFormedLexicalFormOfUnmodelledDatatypeConforms()
    {
        Literal wellFormed = new(
            Utf8Strings.From("POINT(1 2)"),
            new NamedNode(Utf8Strings.From(GeoWktLiteral)));

        ValidationReport report = await RunDatatypeAsync(wellFormed, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    /// <summary>Validates one value node against a property shape constraining <c>sh:datatype</c> to the unmodelled datatype IRI, using only the datatype evaluator.</summary>
    /// <param name="value">The value node.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The validation report.</returns>
    private static async Task<ValidationReport> RunDatatypeAsync(RdfTerm value, CancellationToken cancellationToken)
    {
        ShapeGraphBuilder builder = new();
        builder.PropertyShape(ExShape, pathIri: ExPred)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(ExPred))
            .With(ShaclConstraintVocabulary.Datatype.ToString(), ShapeGraphBuilder.Iri(GeoWktLiteral));

        (InMemoryGraphStore shapeStore, TermDictionary dictionary) = builder.Finish();

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapeStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        NamedNode focus = new(Utf8Strings.From(ExFocus));
        NamedNode pred = new(Utf8Strings.From(ExPred));
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build([new Quad(focus, pred, value).Encode(dictionary).AsTriple()]);

        ConstraintEvaluatorRegistry evaluators = new(new Dictionary<Utf8String, ConstraintEvaluator>
        {
            [ShaclComponentVocabulary.Datatype] = DatatypeEvaluator.EvaluateAsync,
        });

        return await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary, evaluators,
            VeritasClock.System,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
