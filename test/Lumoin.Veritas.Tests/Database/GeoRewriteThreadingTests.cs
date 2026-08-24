using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.Json.Stj;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Sparql.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The end-to-end Geo query-rewrite composition rows: a pipeline composed on
/// <see cref="VeritasEngineOptions.SparqlExecution"/> beside the module registries reaches the opened
/// database's query and update paths on both engine lanes with no rewrite-specific threading, and the
/// composition closure's derived relations feed plain asserted matching — the entailment-side route, no
/// geometry anywhere.
/// </summary>
[TestClass]
internal sealed class GeoRewriteThreadingTests
{
    /// <summary>The example-namespace prefix the data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The relation property term of each base relation, indexed by the relation's numeric value.</summary>
    private static Utf8String[] PropertiesByRelation { get; } =
    [
        GeoVocabulary.Geo.Rcc8Dc, GeoVocabulary.Geo.Rcc8Ec, GeoVocabulary.Geo.Rcc8Po, GeoVocabulary.Geo.Rcc8Tpp,
        GeoVocabulary.Geo.Rcc8Ntpp, GeoVocabulary.Geo.Rcc8Tppi, GeoVocabulary.Geo.Rcc8Ntppi, GeoVocabulary.Geo.Rcc8Eq,
    ];

    /// <summary>The immutable lane threads the pipeline: the engine-options policy carries the module pipeline to <c>AskAsync</c>, where the feature pair derives; the same options without the pipeline keep asserted-only matching.</summary>
    [TestMethod]
    public async Task ImmutableLaneThreadsTheRewritePipelineThroughAskAsync()
    {
        VeritasEngine rewriting = await VeritasEngine.OpenAsync(FeatureData(), [], RewritingOptions(), TestContext.CancellationToken).ConfigureAwait(false);
        await using var rewritingScope = rewriting.ConfigureAwait(false);
        bool derived = await rewriting.AskAsync(
            Utf8Strings.From($"ASK {{ <{Ex}fA> <{GeoVocabulary.Geo.SfContains}> <{Ex}fB> }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(derived, "The options-composed pipeline reaches the query path: the containment derives from the geometries.");

        VeritasEngine dark = await VeritasEngine.OpenAsync(FeatureData(), [], DarkOptions(), TestContext.CancellationToken).ConfigureAwait(false);
        await using var darkScope = dark.ConfigureAwait(false);
        bool asserted = await dark.AskAsync(
            Utf8Strings.From($"ASK {{ <{Ex}fA> <{GeoVocabulary.Geo.SfContains}> <{Ex}fB> }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsFalse(asserted, "Without the pipeline the same engine options keep asserted-only matching.");
    }

    /// <summary>The mutable lane threads the pipeline too: <c>AskAsync</c> derives, and an update's <c>WHERE</c> pattern derives through the same pipeline so the marker lands.</summary>
    [TestMethod]
    public async Task MutableLaneThreadsTheRewritePipelineThroughUpdateAndQuery()
    {
        VeritasEngine engine = await VeritasEngine.OpenMutableAsync(FeatureData(), RewritingOptions(), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        bool derived = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ <{Ex}fA> <{GeoVocabulary.Geo.SfContains}> <{Ex}fB> }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(derived, "The mutable database's per-query engine carries the pipeline.");

        await engine.UpdateAsync(
            Utf8Strings.From($"INSERT {{ <{Ex}fA> <{Ex}hit> <{Ex}yes> }} WHERE {{ <{Ex}fA> <{GeoVocabulary.Geo.SfContains}> <{Ex}fB> }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        bool inserted = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ <{Ex}fA> <{Ex}hit> <{Ex}yes> }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(inserted, "The update's WHERE pattern derived the containment through the pipeline, so the marker landed.");
    }

    /// <summary>
    /// The composition closure feeds the asserted route end to end: relations derived symbolically from a
    /// non-tangential chain land as plain triples, and the chained pair then matches with no pipeline, no
    /// functions, and no geometry anywhere in the dataset.
    /// </summary>
    [TestMethod]
    public async Task TheCompositionClosureFeedsTheAssertedRouteEndToEnd()
    {
        NamedNode a = new(Utf8Strings.From(Ex + "a"));
        NamedNode b = new(Utf8Strings.From(Ex + "b"));
        NamedNode c = new(Utf8Strings.From(Ex + "c"));
        List<Rcc8Assertion> asserted =
        [
            new Rcc8Assertion(a, Rcc8Relation.Ntpp, b),
            new Rcc8Assertion(b, Rcc8Relation.Ntpp, c),
        ];
        List<Rcc8Assertion> derived = [];
        Rcc8DerivationReport report = Rcc8Composition.Derive(asserted, derived);
        Assert.IsTrue(report.Consistent, "The chain premises are consistent.");

        List<DataTriple> data = [];
        foreach(Rcc8Assertion assertion in asserted)
        {
            data.Add(Triple(assertion));
        }

        foreach(Rcc8Assertion assertion in derived)
        {
            data.Add(Triple(assertion));
        }

        VeritasEngine engine = await VeritasEngine.OpenAsync(data, [], new VeritasEngineOptions { Reasoning = null }, TestContext.CancellationToken).ConfigureAwait(false);
        await using var engineScope = engine.ConfigureAwait(false);

        bool chained = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ <{Ex}a> <{GeoVocabulary.Geo.Rcc8Ntpp}> <{Ex}c> }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(chained, "The composed relation matches as a plain asserted triple.");

        bool converse = await engine.AskAsync(
            Utf8Strings.From($"ASK {{ <{Ex}c> <{GeoVocabulary.Geo.Rcc8Ntppi}> <{Ex}a> }}"),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(converse, "The derived converse matches too.");
    }

    /// <summary>Maps a closure assertion to its data triple over the relation's property IRI.</summary>
    /// <param name="assertion">The assertion.</param>
    /// <returns>The data triple.</returns>
    private static DataTriple Triple(Rcc8Assertion assertion)
    {
        return new DataTriple(assertion.Subject, new NamedNode(PropertiesByRelation[(int)assertion.Relation]), assertion.Object);
    }

    /// <summary>The engine options composing the module registries and the module rewrite pipeline, reasoning unwired.</summary>
    /// <returns>The options.</returns>
    private static VeritasEngineOptions RewritingOptions()
    {
        return new VeritasEngineOptions
        {
            Reasoning = null,
            ValueDatatypes = Datatypes(),
            ExtensionFunctions = Functions(),
            SparqlExecution = new SparqlEnginePolicy(Rewrites: GeoExtensionModule.CreateRewritePipeline()),
        };
    }

    /// <summary>The engine options composing the module registries without the pipeline, reasoning unwired.</summary>
    /// <returns>The options.</returns>
    private static VeritasEngineOptions DarkOptions()
    {
        return new VeritasEngineOptions
        {
            Reasoning = null,
            ValueDatatypes = Datatypes(),
            ExtensionFunctions = Functions(),
        };
    }

    /// <summary>Builds the module-composed extension-function registry.</summary>
    /// <returns>The registry.</returns>
    private static SparqlFunctionRegistry Functions()
    {
        SparqlFunctionRegistryBuilder builder = new();
        GeoExtensionModule.RegisterFunctions(builder, GeoJsonGeometryReader.TryRead);

        return builder.Build();
    }

    /// <summary>Builds the module-composed value-datatype registry.</summary>
    /// <returns>The registry.</returns>
    private static ValueDatatypeRegistry Datatypes()
    {
        ValueDatatypeRegistryBuilder builder = new();
        GeoExtensionModule.RegisterValueDatatypes(builder);

        return builder.Build();
    }

    /// <summary>The feature pair: each feature reaches its geometry node through <c>geo:hasDefaultGeometry</c>, and the nodes carry a containing square and a contained square.</summary>
    /// <returns>The data triples.</returns>
    private static List<DataTriple> FeatureData()
    {
        NamedNode hasDefaultGeometry = new(GeoVocabulary.Geo.HasDefaultGeometry);
        NamedNode asWkt = new(GeoVocabulary.Geo.AsWkt);
        NamedNode wktLiteral = new(GeoVocabulary.Geo.WktLiteral);

        return
        [
            new DataTriple(new NamedNode(Utf8Strings.From(Ex + "fA")), hasDefaultGeometry, new NamedNode(Utf8Strings.From(Ex + "fAGeom"))),
            new DataTriple(new NamedNode(Utf8Strings.From(Ex + "fAGeom")), asWkt, new Literal(Utf8Strings.From("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))"), wktLiteral)),
            new DataTriple(new NamedNode(Utf8Strings.From(Ex + "fB")), hasDefaultGeometry, new NamedNode(Utf8Strings.From(Ex + "fBGeom"))),
            new DataTriple(new NamedNode(Utf8Strings.From(Ex + "fBGeom")), asWkt, new Literal(Utf8Strings.From("POLYGON ((1 1, 2 1, 2 2, 1 2, 1 1))"), wktLiteral)),
        ];
    }
}
