using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using Lumoin.Veritas.Turtle;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Geo;

/// <summary>
/// The vendored GeoSPARQL SHACL fixture sweep: every <c>examples/shacl</c> fixture validates against the
/// vendored informative validator (<c>semantic-resources/validators/geo-validator.ttl</c>) with its
/// filename-declared outcome — valid conforms, invalid violates — under the empty value-datatype registry
/// AND under the registering host (<see cref="GeoArmRegistry.SerializationRegistered"/>). One fixture
/// diverges between the modes, and deliberately: the <c>S17-valid</c> GML literal binds its prefix to the
/// geometry-classes ontology namespace where the specification's own example binds the GML 3.2 schema
/// namespace, so the registered definition answers Invalid on content the fixture declares valid — a
/// measured upstream defect, pinned by its own row. Every other fixture answers identically in both
/// modes; no <c>geo:wktLiteral</c> content distinguishes the registration, because upstream's own
/// well-formedness check is a first-character pattern. The house-authored delta rows close that gap: a
/// malformed body the pattern cannot see conforms unregistered and violates registered, while a curve
/// body abstains and conforms in both modes.
/// </summary>
[TestClass]
internal sealed class GeoValidatorFixtureTests
{
    /// <summary>The vendored fixture file names, the outcome declared by the <c>-valid</c>/<c>-invalid</c> suffix.</summary>
    private static readonly string[] FixtureNames =
    [
        "S01-invalid-01", "S01-invalid-02", "S01-invalid-03", "S01-valid",
        "S02-invalid-01", "S02-invalid-02", "S02-valid",
        "S03-invalid", "S03-valid",
        "S04-invalid-01", "S04-invalid-02", "S04-valid",
        "S09-invalid", "S09-valid",
        "S10-invalid", "S10-valid",
        "S11-invalid", "S11-valid",
        "S12-invalid", "S12-valid",
        "S13-invalid", "S13-valid",
        "S14-invalid-01", "S14-invalid-02", "S14-valid",
        "S15-invalid-01", "S15-invalid-02", "S15-valid",
        "S16-invalid", "S16-valid",
        "S17-invalid", "S17-valid",
        "S18-invalid", "S18-valid",
        "S19-invalid", "S19-valid",
        "S20-valid",
        "S21-invalid", "S21-valid",
        "S22-invalid-01", "S22-invalid-02", "S22-valid",
        "S23-invalid-01", "S23-invalid-02", "S23-valid",
        "S24-invalid-01", "S24-invalid-02", "S24-valid",
    ];

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The vendored fixture directory holds exactly the pre-stated roster — an upstream re-copy that changes the set fails here by name.</summary>
    [TestMethod]
    public void FixtureDirectoryHoldsExactlyTheRoster()
    {
        string directory = Path.Combine(W3cCorpusPath.LibraryDirectory("Geo"), "examples", "shacl");
        List<string> found = [];
        foreach(string path in Directory.GetFiles(directory, "*.ttl"))
        {
            found.Add(Path.GetFileNameWithoutExtension(path));
        }

        found.Sort(StringComparer.Ordinal);

        Assert.AreSequenceEqual(FixtureNames, found);
    }

    /// <summary>Every vendored fixture validates to its filename-declared outcome, identically under the empty registry and under the registering host.</summary>
    /// <param name="fixtureName">The fixture file name without extension.</param>
    /// <param name="registered">Whether the run registers <c>geo:wktLiteral</c> at the value layer.</param>
    [TestMethod]
    [DynamicData(nameof(FixtureRows))]
    public async Task VendoredFixtureValidatesAsExpected(string fixtureName, bool registered)
    {
        bool expectedConforms = fixtureName.Contains("-valid", StringComparison.Ordinal);

        ValidationReport report = await ValidateFixtureAsync(fixtureName, registered, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(expectedConforms, report.Conforms, $"{fixtureName} (registered={registered}): wrong conformance verdict.");
        if(!expectedConforms)
        {
            Assert.IsNotEmpty(report.Results, $"{fixtureName} (registered={registered}): a violating fixture must report at least one result.");
        }
    }

    /// <summary>A malformed WKT body whose first character satisfies the validator's own pattern check conforms unregistered and violates under the registering host — the registration's single behavioural delta through the vendored validator.</summary>
    /// <param name="registered">Whether the run registers <c>geo:wktLiteral</c> at the value layer.</param>
    /// <param name="expectedConforms">The expected conformance verdict.</param>
    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task PatternInvisibleMalformedBodyFlipsOnlyUnderRegistration(bool registered, bool expectedConforms)
    {
        ValidationReport report = await ValidateLiteralAsync("POINT(1", registered, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(expectedConforms, report.Conforms, $"registered={registered}: the truncated body must conform exactly when nothing is registered.");
    }

    /// <summary>A curve body is in the WKT roster but grammar-uncertified: the recognizer abstains, the datatype answers Indeterminate, and the literal conforms in both modes — an abstention can never reject.</summary>
    /// <param name="registered">Whether the run registers <c>geo:wktLiteral</c> at the value layer.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CurveBodyConformsUnderBothModes(bool registered)
    {
        ValidationReport report = await ValidateLiteralAsync("CIRCULARSTRING(0 0, 1 1, 2 0)", registered, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(report.Conforms, $"registered={registered}: the curve abstention must leave acceptance standing.");
    }

    /// <summary>
    /// The fixture sweep rows: every vendored fixture under both registry modes, except the registered
    /// mode of the one fixture whose declared outcome rides an upstream defect — that pair is pinned by
    /// its own divergence row.
    /// </summary>
    public static IEnumerable<object[]> FixtureRows
    {
        get
        {
            foreach(string fixtureName in FixtureNames)
            {
                yield return [fixtureName, false];
                if(!string.Equals(fixtureName, "S17-valid", StringComparison.Ordinal))
                {
                    yield return [fixtureName, true];
                }
            }
        }
    }

    /// <summary>
    /// The vendored <c>S17-valid</c> fixture's GML literal binds its prefix to the geometry-classes
    /// ontology namespace, while the specification's own example binds the GML 3.2 schema namespace; an
    /// element of the ontology namespace is provably not from the GML schema, so the registered
    /// definition answers Invalid and the fixture violates under registration despite its declared
    /// outcome — a measured upstream defect, conforming under the empty registry where the constraint is
    /// datatype-IRI identity alone.
    /// </summary>
    [TestMethod]
    public async Task GmlOntologyNamespaceFixtureViolatesOnlyUnderRegistration()
    {
        ValidationReport registered = await ValidateFixtureAsync("S17-valid", registered: true, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(registered.Conforms, "The ontology-namespace GML literal is provably outside the GML schema, so the registered definition must violate.");
        Assert.IsNotEmpty(registered.Results);
    }

    /// <summary>Validates one vendored fixture's data graph against the vendored validator.</summary>
    /// <param name="fixtureName">The fixture file name without extension.</param>
    /// <param name="registered">Whether the run registers <c>geo:wktLiteral</c> at the value layer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The validation report.</returns>
    private static async Task<ValidationReport> ValidateFixtureAsync(string fixtureName, bool registered, CancellationToken cancellationToken)
    {
        string fixturePath = Path.Combine(W3cCorpusPath.LibraryDirectory("Geo"), "examples", "shacl", fixtureName + ".ttl");
        TermDictionary dictionary = new();
        List<Quad> dataQuads = await ParseGraphAsync(fixturePath, cancellationToken).ConfigureAwait(false);
        InMemoryGraphStore dataStore = BuildStore(dataQuads, dictionary);

        return await ValidateAsync(dataStore, dictionary, registered, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Validates a single <c>geo:asWKT</c> literal, typed <c>geo:wktLiteral</c>, against the vendored validator.</summary>
    /// <param name="lexicalForm">The literal's lexical form.</param>
    /// <param name="registered">Whether the run registers <c>geo:wktLiteral</c> at the value layer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The validation report.</returns>
    private static async Task<ValidationReport> ValidateLiteralAsync(string lexicalForm, bool registered, CancellationToken cancellationToken)
    {
        TermDictionary dictionary = new();
        Literal value = new(Utf8Strings.From(lexicalForm), new NamedNode(GeoVocabulary.Geo.WktLiteral));
        Quad quad = new(
            new NamedNode(Utf8Strings.From("urn:x-veritas:geo-delta#geometry")),
            new NamedNode(GeoVocabulary.Geo.AsWkt),
            value);
        InMemoryGraphStore dataStore = InMemoryGraphStore.Build([quad.Encode(dictionary).AsTriple()]);

        return await ValidateAsync(dataStore, dictionary, registered, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs the vendored validator over a data store, under the empty or the registering registry.</summary>
    /// <param name="dataStore">The encoded data graph.</param>
    /// <param name="dictionary">The dictionary the data graph is encoded by; the shapes share it.</param>
    /// <param name="registered">Whether the run registers <c>geo:wktLiteral</c> at the value layer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The validation report.</returns>
    private static async Task<ValidationReport> ValidateAsync(InMemoryGraphStore dataStore, TermDictionary dictionary, bool registered, CancellationToken cancellationToken)
    {
        string validatorPath = Path.Combine(W3cCorpusPath.LibraryDirectory("Geo"), "semantic-resources", "validators", "geo-validator.ttl");
        List<Quad> shapesQuads = await ParseGraphAsync(validatorPath, cancellationToken).ConfigureAwait(false);
        InMemoryGraphStore shapesStore = BuildStore(shapesQuads, dictionary);

        ShapeRegistry registry = await ShapeLoader.LoadAsync(
            shapesStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All, cancellationToken: cancellationToken).ConfigureAwait(false);

        ShaclValidatorOptions options = registered
            ? new ShaclValidatorOptions { ValueDatatypes = GeoArmRegistry.SerializationRegistered }
            : ShaclValidatorOptions.Default;
        RdfTerm shapesGraphIri = new NamedNode(Utf8Strings.From(new Uri(Path.GetFullPath(validatorPath)).AbsoluteUri));

        return await ShaclValidator.ValidateAsync(
            registry, dataStore.AsMatchOps(), dictionary, ShaclBuiltInEvaluators.All, TimeProvider.System,
            shapesGraphMatchOps: shapesStore.AsMatchOps(), shapesGraphIri: shapesGraphIri, options: options,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Parses a Turtle graph file into quads, resolving relative IRIs against the file's own URL.</summary>
    /// <param name="path">The absolute path to the Turtle file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed quads.</returns>
    private static async Task<List<Quad>> ParseGraphAsync(string path, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        Uri baseUri = new(Path.GetFullPath(path));

        List<Quad> quads = [];
        DiagnosticBag diagnostics = new();
        await foreach(Quad quad in TurtleReader.ReadAsync(
            bytes, TurtleSyntax.Turtle, diagnostics, pool: null, baseIri: baseUri.AbsoluteUri, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            quads.Add(quad);
        }

        if(diagnostics.HasErrors)
        {
            throw new TurtleParseException(TurtleConformanceReader.DescribeFirstError(diagnostics));
        }

        return quads;
    }

    /// <summary>Encodes a graph's quads into an <see cref="InMemoryGraphStore"/> over the shared dictionary.</summary>
    /// <param name="quads">The quads to encode.</param>
    /// <param name="dictionary">The shared term dictionary.</param>
    /// <returns>The built store.</returns>
    private static InMemoryGraphStore BuildStore(List<Quad> quads, TermDictionary dictionary)
    {
        List<EncodedTriple> triples = new(quads.Count);
        foreach(Quad quad in quads)
        {
            triples.Add(quad.Encode(dictionary).AsTriple());
        }

        return InMemoryGraphStore.Build(triples);
    }
}
