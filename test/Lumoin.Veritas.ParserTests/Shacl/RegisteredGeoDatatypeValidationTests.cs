using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.ParserTests.Geo;
using Lumoin.Veritas.ParserTests.Infrastructure;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Constraints;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using ValidationReport = Lumoin.Veritas.Shacl.Validation.ValidationReport;

namespace Lumoin.Veritas.ParserTests.Shacl;

/// <summary>
/// The registration rung's behavioural delta at the <c>sh:datatype</c> seam: with the geometry
/// serialization datatypes registered (<see cref="GeoArmRegistry.SerializationRegistered"/>), a provably ill-formed
/// lexical form now violates the constraint where IRI identity alone conformed
/// (<see cref="UnregisteredDatatypeValidationTests"/> pins that baseline), a well-formed form still
/// conforms, and every recognizer abstention — a grammar-uncertified curve body, a body beyond the
/// nesting cap, the uncertified GML, GeoJSON and KML constructs, and the DGGS geometry data after a valid
/// prefix — leaves acceptance standing: an abstention can never reject. The rows of the four registered
/// serializations run their case under the registering host's registry and under the empty one, so the
/// delta and the degradation stand side by side.
/// </summary>
[TestClass]
internal sealed class RegisteredGeoDatatypeValidationTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The shape IRI.</summary>
    private const string ExShape = "http://example.org/S";

    /// <summary>The path predicate IRI.</summary>
    private const string ExPred = "http://example.org/pred";

    /// <summary>The focus node IRI.</summary>
    private const string ExFocus = "http://example.org/foo";

    /// <summary>The delta row: a provably ill-formed lexical form typed <c>geo:wktLiteral</c> violates <c>sh:datatype</c> under the registering host — the one behavioural change the whole rung introduces.</summary>
    [TestMethod]
    public async Task DatatypeIllFormedLexicalFormViolatesUnderRegistration()
    {
        Literal garbage = new(
            Utf8Strings.From("certainly not a geometry"),
            new NamedNode(GeoVocabulary.Geo.WktLiteral));

        ValidationReport report = await RunDatatypeAsync(garbage, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(report.Conforms, "The registered definition answers Invalid, so sh:datatype must violate where IRI identity alone conformed.");
        Assert.IsNotEmpty(report.Results);
    }

    /// <summary>A well-formed lexical form still conforms under the registering host.</summary>
    [TestMethod]
    public async Task DatatypeWellFormedLexicalFormConformsUnderRegistration()
    {
        Literal wellFormed = new(
            Utf8Strings.From("POINT(1 2)"),
            new NamedNode(GeoVocabulary.Geo.WktLiteral));

        ValidationReport report = await RunDatatypeAsync(wellFormed, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    /// <summary>A grammar-uncertified curve body abstains at the recognizer, the definition answers Indeterminate, and the literal conforms — the abstention leaves the engine's acceptance standing.</summary>
    [TestMethod]
    public async Task DatatypeCurveBodyConformsUnderRegistration()
    {
        Literal curve = new(
            Utf8Strings.From("CIRCULARSTRING(0 0, 1 1, 2 0)"),
            new NamedNode(GeoVocabulary.Geo.WktLiteral));

        ValidationReport report = await RunDatatypeAsync(curve, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    /// <summary>A body beyond the nesting cap abstains as a resource bound, never an invalidity claim, and the literal conforms.</summary>
    [TestMethod]
    public async Task DatatypeNestingBeyondCapConformsUnderRegistration()
    {
        string form = string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION(", WktLexical.MaximumNestingDepth)) + "POINT(1 2)" + new string(')', WktLexical.MaximumNestingDepth);
        Literal deep = new(
            Utf8Strings.From(form),
            new NamedNode(GeoVocabulary.Geo.WktLiteral));

        ValidationReport report = await RunDatatypeAsync(deep, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms);
        Assert.IsEmpty(report.Results);
    }

    /// <summary>A GML body whose root element sits in no namespace is provably not an element of the GML schema, so the constraint violates under the registering host and conforms under the empty registry.</summary>
    [TestMethod]
    public async Task DatatypeMalformedGmlLiteralViolatesOnlyUnderRegistration()
    {
        Literal malformed = new(
            Utf8Strings.From("<Point/>"),
            new NamedNode(GeoVocabulary.Geo.GmlLiteral));

        ValidationReport registered = await RunDatatypeAsync(malformed, GeoVocabulary.Geo.GmlLiteral, GeoArmRegistry.SerializationRegistered, TestContext.CancellationToken).ConfigureAwait(false);
        ValidationReport empty = await RunDatatypeAsync(malformed, GeoVocabulary.Geo.GmlLiteral, ValueDatatypeRegistry.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(registered.Conforms, "The registered definition answers Invalid, so sh:datatype must violate.");
        Assert.IsNotEmpty(registered.Results);
        Assert.IsTrue(empty.Conforms, "With no definition registered the constraint is datatype-IRI identity alone.");
        Assert.IsEmpty(empty.Results);
    }

    /// <summary>A GML body carrying a document type declaration abstains at the recognizer, and the literal conforms under both registries.</summary>
    [TestMethod]
    public async Task DatatypeAbstainingGmlLiteralConformsUnderBothRegistries()
    {
        Literal abstaining = new(
            Utf8Strings.From("<!DOCTYPE Point><Point xmlns=\"http://www.opengis.net/gml/3.2\"><pos>1 2</pos></Point>"),
            new NamedNode(GeoVocabulary.Geo.GmlLiteral));

        ValidationReport registered = await RunDatatypeAsync(abstaining, GeoVocabulary.Geo.GmlLiteral, GeoArmRegistry.SerializationRegistered, TestContext.CancellationToken).ConfigureAwait(false);
        ValidationReport empty = await RunDatatypeAsync(abstaining, GeoVocabulary.Geo.GmlLiteral, ValueDatatypeRegistry.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(registered.Conforms, "A declared entity can carry anything, so the abstention leaves acceptance standing.");
        Assert.IsEmpty(registered.Results);
        Assert.IsTrue(empty.Conforms);
        Assert.IsEmpty(empty.Results);
    }

    /// <summary>A GeoJSON object carrying no <c>type</c> member is provably not a geometry object, so the constraint violates under the registering host and conforms under the empty registry.</summary>
    [TestMethod]
    public async Task DatatypeMalformedGeoJsonLiteralViolatesOnlyUnderRegistration()
    {
        Literal malformed = new(
            Utf8Strings.From("{\"geometry\": null}"),
            new NamedNode(GeoVocabulary.Geo.GeoJsonLiteral));

        ValidationReport registered = await RunDatatypeAsync(malformed, GeoVocabulary.Geo.GeoJsonLiteral, GeoArmRegistry.SerializationRegistered, TestContext.CancellationToken).ConfigureAwait(false);
        ValidationReport empty = await RunDatatypeAsync(malformed, GeoVocabulary.Geo.GeoJsonLiteral, ValueDatatypeRegistry.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(registered.Conforms, "The registered definition answers Invalid, so sh:datatype must violate.");
        Assert.IsNotEmpty(registered.Results);
        Assert.IsTrue(empty.Conforms, "With no definition registered the constraint is datatype-IRI identity alone.");
        Assert.IsEmpty(empty.Results);
    }

    /// <summary>A GeoJSON <c>type</c> value written with a backslash escape abstains at the recognizer, and the literal conforms under both registries.</summary>
    [TestMethod]
    public async Task DatatypeAbstainingGeoJsonLiteralConformsUnderBothRegistries()
    {
        Literal abstaining = new(
            Utf8Strings.From("{\"type\": \"\\u0050oint\", \"coordinates\": [1, 2]}"),
            new NamedNode(GeoVocabulary.Geo.GeoJsonLiteral));

        ValidationReport registered = await RunDatatypeAsync(abstaining, GeoVocabulary.Geo.GeoJsonLiteral, GeoArmRegistry.SerializationRegistered, TestContext.CancellationToken).ConfigureAwait(false);
        ValidationReport empty = await RunDatatypeAsync(abstaining, GeoVocabulary.Geo.GeoJsonLiteral, ValueDatatypeRegistry.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(registered.Conforms, "An escaped spelling is a claim the recognizer does not make, so acceptance stands.");
        Assert.IsEmpty(registered.Results);
        Assert.IsTrue(empty.Conforms);
        Assert.IsEmpty(empty.Results);
    }

    /// <summary>A KML body whose root is bound to a namespace outside the KML family is provably not an element of the KML schema, so the constraint violates under the registering host and conforms under the empty registry.</summary>
    [TestMethod]
    public async Task DatatypeMalformedKmlLiteralViolatesOnlyUnderRegistration()
    {
        Literal malformed = new(
            Utf8Strings.From("<Point xmlns=\"http://example.org/ns\"/>"),
            new NamedNode(GeoVocabulary.Geo.KmlLiteral));

        ValidationReport registered = await RunDatatypeAsync(malformed, GeoVocabulary.Geo.KmlLiteral, GeoArmRegistry.SerializationRegistered, TestContext.CancellationToken).ConfigureAwait(false);
        ValidationReport empty = await RunDatatypeAsync(malformed, GeoVocabulary.Geo.KmlLiteral, ValueDatatypeRegistry.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(registered.Conforms, "The registered definition answers Invalid, so sh:datatype must violate.");
        Assert.IsNotEmpty(registered.Results);
        Assert.IsTrue(empty.Conforms, "With no definition registered the constraint is datatype-IRI identity alone.");
        Assert.IsEmpty(empty.Results);
    }

    /// <summary>A KML body whose root carries no namespace declaration abstains at the recognizer, and the literal conforms under both registries.</summary>
    [TestMethod]
    public async Task DatatypeAbstainingKmlLiteralConformsUnderBothRegistries()
    {
        Literal abstaining = new(
            Utf8Strings.From("<Point><coordinates>1,2</coordinates></Point>"),
            new NamedNode(GeoVocabulary.Geo.KmlLiteral));

        ValidationReport registered = await RunDatatypeAsync(abstaining, GeoVocabulary.Geo.KmlLiteral, GeoArmRegistry.SerializationRegistered, TestContext.CancellationToken).ConfigureAwait(false);
        ValidationReport empty = await RunDatatypeAsync(abstaining, GeoVocabulary.Geo.KmlLiteral, ValueDatatypeRegistry.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(registered.Conforms, "A fragment torn from a document conventionally loses the default binding, so acceptance stands.");
        Assert.IsEmpty(registered.Results);
        Assert.IsTrue(empty.Conforms);
        Assert.IsEmpty(empty.Results);
    }

    /// <summary>A whitespace-only DGGS form is not the empty form and carries no angle-bracket IRI prefix, so the constraint violates under the registering host and conforms under the empty registry.</summary>
    [TestMethod]
    public async Task DatatypeMalformedDggsLiteralViolatesOnlyUnderRegistration()
    {
        Literal malformed = new(
            Utf8Strings.From("   "),
            new NamedNode(GeoVocabulary.Geo.DggsLiteral));

        ValidationReport registered = await RunDatatypeAsync(malformed, GeoVocabulary.Geo.DggsLiteral, GeoArmRegistry.SerializationRegistered, TestContext.CancellationToken).ConfigureAwait(false);
        ValidationReport empty = await RunDatatypeAsync(malformed, GeoVocabulary.Geo.DggsLiteral, ValueDatatypeRegistry.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(registered.Conforms, "The registered definition answers Invalid, so sh:datatype must violate.");
        Assert.IsNotEmpty(registered.Results);
        Assert.IsTrue(empty.Conforms, "With no definition registered the constraint is datatype-IRI identity alone.");
        Assert.IsEmpty(empty.Results);
    }

    /// <summary>A DGGS form with a valid prefix and geometry data abstains at the recognizer — the data's formulation belongs to the identified DGGS — and the literal conforms under both registries.</summary>
    [TestMethod]
    public async Task DatatypeAbstainingDggsLiteralConformsUnderBothRegistries()
    {
        Literal abstaining = new(
            Utf8Strings.From("<https://w3id.org/dggs/auspix> CELL (R3234)"),
            new NamedNode(GeoVocabulary.Geo.DggsLiteral));

        ValidationReport registered = await RunDatatypeAsync(abstaining, GeoVocabulary.Geo.DggsLiteral, GeoArmRegistry.SerializationRegistered, TestContext.CancellationToken).ConfigureAwait(false);
        ValidationReport empty = await RunDatatypeAsync(abstaining, GeoVocabulary.Geo.DggsLiteral, ValueDatatypeRegistry.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(registered.Conforms, "The geometry data is per-DGGS territory the recognizer does not certify, so acceptance stands.");
        Assert.IsEmpty(registered.Results);
        Assert.IsTrue(empty.Conforms);
        Assert.IsEmpty(empty.Results);
    }

    /// <summary>A violating house-flavour cell-set body under the GENERIC DGGS datatype is certified through the shared recognizer, so the constraint violates under the registering host and conforms under the empty registry.</summary>
    [TestMethod]
    public async Task DatatypeHouseFlavourDggsLiteralViolatesOnlyUnderRegistration()
    {
        Literal violating = new(
            Utf8Strings.From("<https://lumoin.com/veritas/dggs/a5> CELLS ()"),
            new NamedNode(GeoVocabulary.Geo.DggsLiteral));

        ValidationReport registered = await RunDatatypeAsync(violating, GeoVocabulary.Geo.DggsLiteral, GeoArmRegistry.SerializationRegistered, TestContext.CancellationToken).ConfigureAwait(false);
        ValidationReport empty = await RunDatatypeAsync(violating, GeoVocabulary.Geo.DggsLiteral, ValueDatatypeRegistry.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(registered.Conforms, "The house flavour's whole body grammar is certified, so an empty cell roster must violate.");
        Assert.IsNotEmpty(registered.Results);
        Assert.IsTrue(empty.Conforms);
        Assert.IsEmpty(empty.Results);
    }

    /// <summary>The house <c>a5Literal</c> subclass certifies its whole grammar: a conformant cell-set form conforms under both registries, and a foreign-grid form violates only under the registering host — the subclass names the implementation.</summary>
    [TestMethod]
    public async Task DatatypeA5LiteralCertifiesItsWholeGrammar()
    {
        NamedNode a5Datatype = new(A5DggsVocabulary.DatatypeIri);
        Literal conformant = new(Utf8Strings.From("<https://lumoin.com/veritas/dggs/a5> CELLS (600000000000000)"), a5Datatype);
        Literal foreign = new(Utf8Strings.From("<https://w3id.org/dggs/auspix> CELL (R3234)"), a5Datatype);

        ValidationReport conformantRegistered = await RunDatatypeAsync(conformant, A5DggsVocabulary.DatatypeIri, GeoArmRegistry.SerializationRegistered, TestContext.CancellationToken).ConfigureAwait(false);
        ValidationReport conformantEmpty = await RunDatatypeAsync(conformant, A5DggsVocabulary.DatatypeIri, ValueDatatypeRegistry.Empty, TestContext.CancellationToken).ConfigureAwait(false);
        ValidationReport foreignRegistered = await RunDatatypeAsync(foreign, A5DggsVocabulary.DatatypeIri, GeoArmRegistry.SerializationRegistered, TestContext.CancellationToken).ConfigureAwait(false);
        ValidationReport foreignEmpty = await RunDatatypeAsync(foreign, A5DggsVocabulary.DatatypeIri, ValueDatatypeRegistry.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(conformantRegistered.Conforms);
        Assert.IsTrue(conformantEmpty.Conforms);
        Assert.IsFalse(foreignRegistered.Conforms, "A foreign grid IRI under the implementation-naming subclass is itself the violation.");
        Assert.IsTrue(foreignEmpty.Conforms, "With no definition registered the constraint is datatype-IRI identity alone.");
    }

    /// <summary>The registry models no datatype subsumption, so a constraint scoped to the generic <c>geo:dggsLiteral</c> never matches an <c>a5Literal</c>-typed literal under either registry — the decided tradeoff of indicating the flavour through the subclass.</summary>
    [TestMethod]
    public async Task DatatypeGenericScopeDoesNotMatchTheSubclassTypedLiteral()
    {
        Literal subclassTyped = new(
            Utf8Strings.From("<https://lumoin.com/veritas/dggs/a5> CELLS (600000000000000)"),
            new NamedNode(A5DggsVocabulary.DatatypeIri));

        ValidationReport registered = await RunDatatypeAsync(subclassTyped, GeoVocabulary.Geo.DggsLiteral, GeoArmRegistry.SerializationRegistered, TestContext.CancellationToken).ConfigureAwait(false);
        ValidationReport empty = await RunDatatypeAsync(subclassTyped, GeoVocabulary.Geo.DggsLiteral, ValueDatatypeRegistry.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(registered.Conforms);
        Assert.IsFalse(empty.Conforms);
    }

    /// <summary>Validates one value node against a property shape constraining <c>sh:datatype</c> to <c>geo:wktLiteral</c>, using only the datatype evaluator, under the registering host's registry.</summary>
    /// <param name="value">The value node.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The validation report.</returns>
    private static async Task<ValidationReport> RunDatatypeAsync(RdfTerm value, CancellationToken cancellationToken)
    {
        return await RunDatatypeAsync(value, GeoVocabulary.Geo.WktLiteral, GeoArmRegistry.SerializationRegistered, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Validates one value node against a property shape constraining <c>sh:datatype</c> to a datatype IRI, using only the datatype evaluator, under a given value-datatype registry.</summary>
    /// <param name="value">The value node.</param>
    /// <param name="datatypeIri">The datatype IRI the shape constrains to.</param>
    /// <param name="valueDatatypes">The registry the validator consults.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The validation report.</returns>
    private static async Task<ValidationReport> RunDatatypeAsync(RdfTerm value, Utf8String datatypeIri, ValueDatatypeRegistry valueDatatypes, CancellationToken cancellationToken)
    {
        ShapeGraphBuilder builder = new();
        builder.PropertyShape(ExShape, pathIri: ExPred)
            .With(ShaclCoreVocabulary.TargetSubjectsOf.ToString(), ShapeGraphBuilder.Iri(ExPred))
            .With(ShaclConstraintVocabulary.Datatype.ToString(), ShapeGraphBuilder.Iri(datatypeIri.ToString()));

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

        ShaclValidatorOptions options = new() { ValueDatatypes = valueDatatypes };

        return await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary, evaluators,
            VeritasClock.System,
            options: options,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
